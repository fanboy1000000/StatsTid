using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Worklist;

/// <summary>
/// S145 Stage B / TASK-14524 (pins for TASK-14521) - the HR worklist's "cannot recalculate" marker follows the
/// ONE shared week rule, over the real HTTP host (R50: a new file, because the three existing worklist test files
/// are TASK-14521-T's). Docker-gated: CI-verified, never locally green.
///
/// <para>
/// <b>The story, in plain language.</b> When HR corrects someone's history after a month was sent to payroll, the
/// worklist flags the month and marks it "cannot recalculate in the system" when payroll would refuse it. Before
/// Stage B any profile, category or agreement change inside the month was marked blocked (QUAL-149 / QUAL-150).
/// Stage B pays those months part by part, so only ONE case stays blocked: an agreement change dated inside a
/// Monday-Sunday week the month settles (an agreement change on a day other than a Monday). Its register id is
/// QUAL-209. A profile change, a category change and a Monday agreement change derive an empty block, so a Global
/// Admin may record RECALCULATED; the mid-week agreement change derives QUAL-209 and the server refuses
/// RECALCULATED with 409 <c>worklist-recalc-blocked</c>. A correction in a month's last days also flags the NEXT
/// month (R5(a)), whose opening week holds those days, so the NEXT month's row shows the block.
/// </para>
///
/// <para>
/// <b>Red conditions.</b> The block is derived by <c>BackdateWorklistDerivation.RecalcBlockedByForTrigger</c>, a
/// pure core, so the mutations are Unit mutations by nature (fact 3; ruled R-72): WR1's PROFILE and CATEGORY
/// cases rest on MB-6p (PROFILE and CATEGORY still return QUAL-149) and its Monday AGREEMENT case on MB-6 (the
/// agreement arm restored to Stage A's interior-day rule); WR2 rests on MB-6r (the residual arm returns null:
/// nothing blocks); WR3 rests on MB-6 (Stage A's rule judges 29 Sep outside October, so October's row shows no
/// block). The Unit pins are <c>BackdateWorklistResidualDerivationTests</c>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class WorklistResidualBlockTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string Org = "STY_WL_RESIDUAL";
    private const string Residual = "QUAL-209";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boots the host (seeders run)
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // =====================================================================
    // WR1 - the three non-residual triggers: RECALCULATED is accepted
    // =====================================================================

    /// <summary>
    /// WR1 (refinement :702). An exported April 2026 with ONE open row, raised by a trigger that is no longer a
    /// block: PROFILE_CHANGE on Wed 15 Apr, EMPLOYMENT_CATEGORY_CHANGE on Wed 15 Apr, or AGREEMENT_CODE_CHANGE on
    /// MONDAY 13 Apr (a Monday starts a clean week). Each case is its own employee and row. A Global Admin
    /// resolving it RECALCULATED gets <b>200</b> (ETag "2"), and the resolution is stamped with the EMPTY set (not
    /// NULL: NULL means open) on the row and on the event's <c>blockedBy</c>.
    ///
    /// Red conditions: MB-6p (Unit; PROFILE and CATEGORY still return QUAL-149) turns the first two cases into 409;
    /// MB-6 (Unit; the agreement arm restored to Stage A's interior-day rule) turns the Monday AGREEMENT case into
    /// 409 (QUAL-150).
    /// </summary>
    [Theory]
    [InlineData(WorklistTriggerKinds.ProfileChange, "2026-04-15")]
    [InlineData(WorklistTriggerKinds.EmploymentCategoryChange, "2026-04-15")]
    [InlineData(WorklistTriggerKinds.AgreementCodeChange, "2026-04-13")]
    public async Task Resolve_Recalculated_NonResidualTriggerInsideExportedMonth_GlobalAdmin200_StampsEmptySet(
        string kind, string date)
    {
        var employee = $"wl_res_wr1_{kind.ToLowerInvariant()}";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employee, Org);
        await InsertExportRecordAsync(employee, 2026, 4);
        var rowId = await RaiseRowAsync(employee, kind, DateOnly.Parse(date), new DateOnly(2026, 5, 1));

        var rsp = await SendResolveAsync(Client(GlobalAdminToken()), $"/api/hr/backdate-worklist/{rowId}/resolve", "\"1\"",
            new { resolution = "RECALCULATED", reason = "Re-planned April 2026 via /api/payroll/recalculate" });

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        Assert.Equal("\"2\"", rsp.Headers.ETag!.Tag);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolution = 'RECALCULATED' AND version = 2 AND resolution_blocked_by = '{}'",
            rowId));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved' AND event_payload ->> 'worklistId' = @p1 AND event_payload -> 'blockedBy' = '[]'::jsonb",
            $"employee-{employee}", rowId.ToString()));
    }

    // =====================================================================
    // WR2 - the residual: a mid-week agreement change is refused
    // =====================================================================

    /// <summary>
    /// WR2 (refinement :703). The same exported April 2026, the open row raised by an AGREEMENT_CODE_CHANGE on
    /// WEDNESDAY 15 Apr (inside the settled week Mon 13 - Sun 19 Apr). A Global Admin resolving it RECALCULATED
    /// gets <b>409</b> <c>worklist-recalc-blocked</c> with <c>blockedBy: ["QUAL-209"]</c>, and NOTHING is written:
    /// the row stays open at version 1 with no stamp, no event and no audit row.
    ///
    /// Red conditions: MB-6r (Unit; the residual arm returns null, nothing blocks) turns the 409 into a 200 and
    /// writes the row, tripping the status assertion.
    /// </summary>
    [Fact]
    public async Task Resolve_Recalculated_MidWeekAgreementChange_GlobalAdmin409_BlockedByResidual()
    {
        const string employee = "wl_res_wr2";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employee, Org);
        await InsertExportRecordAsync(employee, 2026, 4);
        var rowId = await RaiseRowAsync(employee, WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 4, 15), new DateOnly(2026, 5, 1));

        var rsp = await SendResolveAsync(Client(GlobalAdminToken()), $"/api/hr/backdate-worklist/{rowId}/resolve", "\"1\"",
            new { resolution = "RECALCULATED", reason = "Claiming a recalculation payroll would refuse" });

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        using (var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("worklist-recalc-blocked", doc.RootElement.GetProperty("kind").GetString());
            Assert.Equal(new[] { Residual },
                doc.RootElement.GetProperty("blockedBy").EnumerateArray().Select(e => e.GetString()).ToArray());
            Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("error").GetString()));
        }

        // Nothing was written.
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_at IS NULL AND resolution_blocked_by IS NULL AND version = 1",
            rowId));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved'", $"employee-{employee}"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowResolved' AND target_resource_id = @p0", employee));
    }

    // =====================================================================
    // WR3 - a context-day correction blocks the NEXT month's row
    // =====================================================================

    /// <summary>
    /// WR3 (refinement :699, the context-day case). An agreement correction dated TUESDAY 29 Sep 2026, with
    /// September AND October exported. R5(a) flags both months, and each row's trigger carries the real backdate.
    /// 29 September lies in the week Mon 28 Sep - Sun 4 Oct, whose Sunday is in October, so OCTOBER settles that
    /// week: the OCTOBER row shows <c>recalcBlockedBy: ["QUAL-209"]</c> (on its trigger and on the row), while
    /// September's row, whose settled weeks do not contain that week, shows none.
    ///
    /// Red conditions: MB-6 (Unit; Stage A's rule judges a date strictly inside the month, so 29 Sep is outside
    /// October) leaves October's row with no block, tripping the array assertion.
    /// </summary>
    [Fact]
    public async Task Get_ContextDayAgreementChange_OnNextMonthsRow_RecalcBlockedByResidual()
    {
        const string employee = "wl_res_wr3";
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employee, Org);
        await InsertExportRecordAsync(employee, 2026, 9);
        await InsertExportRecordAsync(employee, 2026, 10);
        var ids = await RaiseRowsAsync(employee, WorklistTriggerKinds.AgreementCodeChange, new DateOnly(2026, 9, 29), new DateOnly(2026, 10, 1));
        Assert.Equal(2, ids.Count); // September and October

        using var doc = JsonDocument.Parse(
            await Client(GlobalAdminToken()).GetStringAsync($"/api/hr/backdate-worklist?employeeId={employee}"));
        var rows = doc.RootElement.EnumerateArray().ToList();
        var october = Assert.Single(rows, r => r.GetProperty("month").GetInt32() == 10);
        var september = Assert.Single(rows, r => r.GetProperty("month").GetInt32() == 9);

        var octoberTrigger = Assert.Single(october.GetProperty("triggers").EnumerateArray());
        Assert.Equal("2026-09-29", octoberTrigger.GetProperty("effectiveFrom").GetString()); // the real backdate
        Assert.Equal(Residual, octoberTrigger.GetProperty("recalcBlockedBy").GetString());
        Assert.Equal(new[] { Residual },
            october.GetProperty("recalcBlockedBy").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Empty(september.GetProperty("recalcBlockedBy").EnumerateArray());
    }

    // =====================================================================
    // Seeding (copied from BackdateWorklistEndpointTests: the files may not share a private helper)
    // =====================================================================

    private async Task InsertExportRecordAsync(string employee, int year, int month) =>
        await ExecAsync(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month, original_lines, current_effective_lines, content_hash)
            VALUES (@p0, NULL, @p1, @p2, @p3, '[]'::jsonb, '[]'::jsonb, @p4)
            """, Guid.NewGuid(), employee, year, month, $"h-{year}-{month}");

    /// <summary>Raises the worklist row(s) for one correction through the production writer, in its own tx.</summary>
    private async Task<IReadOnlyList<Guid>> RaiseRowsAsync(string employee, string kind, DateOnly effectiveFrom, DateOnly toExclusive)
    {
        var repo = _factory.Services.GetRequiredService<HrBackdateWorklistRepository>();
        var dbFactory = _factory.Services.GetRequiredService<DbConnectionFactory>();
        await using var conn = dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var ids = await repo.WriteForExportedMonthsAsync(conn, tx, employee,
            new WorklistTrigger(kind, Guid.NewGuid(), effectiveFrom, "hr_seed"),
            effectiveFrom, toExclusive, CancellationToken.None);
        await tx.CommitAsync();
        return ids;
    }

    private async Task<Guid> RaiseRowAsync(string employee, string kind, DateOnly effectiveFrom, DateOnly toExclusive) =>
        Assert.Single(await RaiseRowsAsync(employee, kind, effectiveFrom, toExclusive));

    // =====================================================================
    // HTTP helpers
    // =====================================================================

    private static async Task<HttpResponseMessage> SendResolveAsync(HttpClient client, string url, string ifMatch, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        req.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(ifMatch));
        return await client.SendAsync(req);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string GlobalAdminToken() => new JwtTokenService(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    }).GenerateToken(
        employeeId: "wl_res_admin", name: "S145 QA Admin", role: StatsTidRoles.GlobalAdmin, agreementCode: "AC",
        scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    // =====================================================================
    // DB helpers
    // =====================================================================

    private async Task<int> CountAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values go
        // through parameters below, never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values go
        // through parameters below, never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }
}
