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
/// S138 / TASK-13803 — Docker-gated HTTP pins for the two HR backdate-worklist endpoints
/// (<c>GET /api/hr/backdate-worklist</c>, <c>POST /api/hr/backdate-worklist/{id}/resolve</c>).
/// RED-FIRST from the refinement spec (rev 4.2, TASK-13803 ACs): HR-only (HROrAbove policy + the
/// LocalHR per-scope floor bound to the ROW's employee, terminated-inclusive); the org-wide listing
/// filtered by the actor's HR accessible-org set; typed rows carrying keys, triggers, the derived
/// fields and <c>version</c> as ETag; resolve = If-Match REQUIRED (428 missing / 412 stale),
/// 404 unknown, 403 foreign HR, 409 already resolved, 422 bad verb / blank reason; 200 + new ETag.
///
/// <para>HTTP-level via <see cref="StatsTidWebApplicationFactory"/> + <c>CreateClient()</c>; JWT
/// minting via the dev-fallback signing key (the AdminUserVersioningTests helper shape).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class BackdateWorklistEndpointTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private const string EmployeeOrg = "STY_WL_EP1";
    private const string ForeignOrg = "STY_WL_EP_FOREIGN";
    private const string Employee = "wl_ep_emp1";
    private const string ScopedHrActor = "wl_ep_hr_scoped";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;
    private Guid _openRowId;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boots the host (seeders run)

        // Subject: an employee in EmployeeOrg with ONE exported month (March 2026) and ONE open
        // EXPORTED_MONTH worklist row raised by a PROFILE_CHANGE effective 15 Mar (strictly inside
        // the month → QUAL-149 visible). Also seed the foreign org so a foreign-HR scope is real.
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, Employee, EmployeeOrg);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, "wl_ep_foreign_emp", ForeignOrg);
        await ExecAsync(
            """
            INSERT INTO payroll_export_records
                (export_id, period_id, employee_id, year, month, original_lines, current_effective_lines, content_hash)
            VALUES (@p0, NULL, @p1, 2026, 3, '[]'::jsonb, '[]'::jsonb, 'h-mar')
            """, Guid.NewGuid(), Employee);

        var repo = _factory.Services.GetRequiredService<HrBackdateWorklistRepository>();
        var dbFactory = _factory.Services.GetRequiredService<DbConnectionFactory>();
        await using var conn = dbFactory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var ids = await repo.WriteForExportedMonthsAsync(conn, tx, Employee,
            new WorklistTrigger(WorklistTriggerKinds.ProfileChange, Guid.NewGuid(), new DateOnly(2026, 3, 15), "hr_seed"),
            new DateOnly(2026, 3, 15), new DateOnly(2026, 4, 1), CancellationToken.None);
        await tx.CommitAsync();
        _openRowId = Assert.Single(ids);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET — per-employee read
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_WithEmployeeId_GlobalAdmin_ReturnsTypedRow_WithKeysTriggersDerivedFieldsAndVersion()
    {
        var client = Client(GlobalAdminToken());
        var rsp = await client.GetAsync($"/api/hr/backdate-worklist?employeeId={Employee}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        using var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind); // BARE array, not an envelope
        var row = Assert.Single(doc.RootElement.EnumerateArray());

        Assert.Equal(_openRowId, row.GetProperty("worklistId").GetGuid());
        Assert.Equal(Employee, row.GetProperty("employeeId").GetString());
        Assert.Equal("EXPORTED_MONTH", row.GetProperty("kind").GetString());
        Assert.Equal(2026, row.GetProperty("year").GetInt32());
        Assert.Equal(3, row.GetProperty("month").GetInt32());
        Assert.NotEqual(Guid.Empty, row.GetProperty("exportId").GetGuid());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("entitlementType").ValueKind);
        Assert.Equal(1L, row.GetProperty("version").GetInt64());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("resolvedAt").ValueKind);

        var trigger = Assert.Single(row.GetProperty("triggers").EnumerateArray());
        Assert.Equal("PROFILE_CHANGE", trigger.GetProperty("kind").GetString());
        Assert.Equal("2026-03-15", trigger.GetProperty("effectiveFrom").GetString()); // HR-only surface — may be carried
        Assert.Equal("h-mar", trigger.GetProperty("baselineContentHash").GetString());
        Assert.False(trigger.GetProperty("recalculatedSince").GetBoolean());
        Assert.Equal(JsonValueKind.Null, trigger.GetProperty("reversedSince").ValueKind);
        Assert.Equal("QUAL-149", trigger.GetProperty("recalcBlockedBy").GetString());

        Assert.Equal(new[] { "QUAL-149" }, row.GetProperty("recalcBlockedBy").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.False(row.GetProperty("recalculatedSince").GetBoolean());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("reversedSince").ValueKind);
    }

    [Fact]
    public async Task Get_WithEmployeeId_ScopedHr_Own_200_Foreign_403_Employee_403_Anonymous_401()
    {
        var own = await Client(HrToken(EmployeeOrg, ScopedHrActor)).GetAsync($"/api/hr/backdate-worklist?employeeId={Employee}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        var foreign = await Client(HrToken(ForeignOrg, "wl_ep_hr_foreign")).GetAsync($"/api/hr/backdate-worklist?employeeId={Employee}");
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        // An Employee-role token is denied by the HROrAbove policy — no employee-facing exposure.
        var employee = await Client(EmployeeToken(Employee, EmployeeOrg)).GetAsync($"/api/hr/backdate-worklist?employeeId={Employee}");
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);

        var anonymous = await _factory.CreateClient().GetAsync($"/api/hr/backdate-worklist?employeeId={Employee}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // GET — org-wide listing
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Get_OrgWide_ScopedHr_SeesOnlyOwnOrgRows_ForeignHr_EmptyList_GlobalAdmin_All()
    {
        using var ownDoc = JsonDocument.Parse(await Client(HrToken(EmployeeOrg, ScopedHrActor)).GetStringAsync("/api/hr/backdate-worklist"));
        Assert.Equal(new[] { Employee }, ownDoc.RootElement.EnumerateArray().Select(r => r.GetProperty("employeeId").GetString()).ToArray());

        // A foreign HR has a REAL (non-empty) accessible-org set → 200 with an EMPTY list, never
        // another org's rows.
        var foreignRsp = await Client(HrToken(ForeignOrg, "wl_ep_hr_foreign")).GetAsync("/api/hr/backdate-worklist");
        Assert.Equal(HttpStatusCode.OK, foreignRsp.StatusCode);
        using var foreignDoc = JsonDocument.Parse(await foreignRsp.Content.ReadAsStringAsync());
        Assert.Empty(foreignDoc.RootElement.EnumerateArray());

        using var adminDoc = JsonDocument.Parse(await Client(GlobalAdminToken()).GetStringAsync("/api/hr/backdate-worklist"));
        Assert.Contains(adminDoc.RootElement.EnumerateArray(), r => r.GetProperty("employeeId").GetString() == Employee);
    }

    // ════════════════════════════════════════════════════════════════════════
    // POST resolve — the ADR-019 If-Match contract + scope + 409/422
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Resolve_MissingIfMatch_428_Stale_412_Fresh_200WithNewEtag_Repeat_409_OpenFilterHonoured()
    {
        var client = Client(HrToken(EmployeeOrg, ScopedHrActor));
        var url = $"/api/hr/backdate-worklist/{_openRowId}/resolve";
        var body = new { resolution = "RECALCULATED", reason = "Re-planned March 2026 via /api/payroll/recalculate" };

        // (1) No If-Match → 428 Precondition Required.
        var noHeader = await client.PostAsJsonAsync(url, body);
        Assert.Equal((HttpStatusCode)428, noHeader.StatusCode);

        // (2) Stale If-Match → 412 with the structured expected/actual body; row untouched.
        var stale = await SendResolveAsync(client, url, "\"99\"", body);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        using (var staleDoc = JsonDocument.Parse(await stale.Content.ReadAsStringAsync()))
        {
            Assert.Equal(99L, staleDoc.RootElement.GetProperty("expectedVersion").GetInt64());
            Assert.Equal(1L, staleDoc.RootElement.GetProperty("actualVersion").GetInt64());
        }
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_at IS NOT NULL", _openRowId));

        // (3) Fresh If-Match "1" → 200, ETag "2", typed body.
        var ok = await SendResolveAsync(client, url, "\"1\"", body);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("\"2\"", ok.Headers.ETag!.Tag);
        using (var okDoc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync()))
        {
            Assert.Equal(_openRowId, okDoc.RootElement.GetProperty("worklistId").GetGuid());
            Assert.Equal(Employee, okDoc.RootElement.GetProperty("employeeId").GetString());
            Assert.Equal("RECALCULATED", okDoc.RootElement.GetProperty("resolution").GetString());
            Assert.Equal(2L, okDoc.RootElement.GetProperty("version").GetInt64());
            Assert.NotEqual(JsonValueKind.Null, okDoc.RootElement.GetProperty("resolvedAt").ValueKind);
        }
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_by = @p1 AND resolution = 'RECALCULATED' AND version = 2", _openRowId, ScopedHrActor));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM outbox_events WHERE stream_id = @p0 AND event_type = 'BackdateWorklistRowResolved'", $"employee-{Employee}"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM audit_projection WHERE event_type = 'BackdateWorklistRowResolved' AND target_resource_id = @p0", Employee));

        // (4) open=true (default) no longer lists it; open=false does, with the resolution fields.
        using (var openDoc = JsonDocument.Parse(await client.GetStringAsync($"/api/hr/backdate-worklist?employeeId={Employee}")))
            Assert.Empty(openDoc.RootElement.EnumerateArray());
        using (var allDoc = JsonDocument.Parse(await client.GetStringAsync($"/api/hr/backdate-worklist?employeeId={Employee}&open=false")))
        {
            var row = Assert.Single(allDoc.RootElement.EnumerateArray());
            Assert.Equal("RECALCULATED", row.GetProperty("resolution").GetString());
            Assert.Equal(ScopedHrActor, row.GetProperty("resolvedBy").GetString());
            Assert.Equal(2L, row.GetProperty("version").GetInt64());
        }

        // (5) Resolving again with the FRESH token → 409 (already resolved, not a silent re-write).
        var again = await SendResolveAsync(client, url, "\"2\"", new { resolution = "DISMISSED", reason = "again" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Resolve_ForeignHr_403_Employee_403_UnknownId_404_BadVerb_422_BlankReason_422()
    {
        var url = $"/api/hr/backdate-worklist/{_openRowId}/resolve";
        var good = new { resolution = "DISMISSED", reason = "Not payroll-relevant" };

        var foreign = await SendResolveAsync(Client(HrToken(ForeignOrg, "wl_ep_hr_foreign")), url, "\"1\"", good);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        var employee = await SendResolveAsync(Client(EmployeeToken(Employee, EmployeeOrg)), url, "\"1\"", good);
        Assert.Equal(HttpStatusCode.Forbidden, employee.StatusCode);

        var hr = Client(HrToken(EmployeeOrg, ScopedHrActor));

        var unknown = await SendResolveAsync(hr, $"/api/hr/backdate-worklist/{Guid.NewGuid()}/resolve", "\"1\"", good);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var badVerb = await SendResolveAsync(hr, url, "\"1\"", new { resolution = "IGNORED", reason = "x" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badVerb.StatusCode);

        var blankReason = await SendResolveAsync(hr, url, "\"1\"", new { resolution = "DISMISSED", reason = "   " });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blankReason.StatusCode);

        // None of the refusals touched the row.
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM hr_backdate_worklist WHERE worklist_id = @p0 AND resolved_at IS NULL AND version = 1", _openRowId));
    }

    // ─── HTTP helpers ────────────────────────────────────────────────────────

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

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    private static string GlobalAdminToken() => NewTokenService().GenerateToken(
        employeeId: "wl_ep_admin", name: "S138 QA Admin", role: StatsTidRoles.GlobalAdmin, agreementCode: "AC",
        scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    private static string HrToken(string orgId, string actorId) => NewTokenService().GenerateToken(
        employeeId: actorId, name: actorId, role: StatsTidRoles.LocalHR, agreementCode: "AC", orgId: orgId,
        scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });

    private static string EmployeeToken(string employeeId, string orgId) => NewTokenService().GenerateToken(
        employeeId: employeeId, name: employeeId, role: StatsTidRoles.Employee, agreementCode: "AC", orgId: orgId,
        scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });

    // ─── DB helpers ──────────────────────────────────────────────────────────

    private async Task<int> CountAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): SQL is a compile-time test constant; values go
        // through parameters below — never user input.
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
        // through parameters below — never user input.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }
}
