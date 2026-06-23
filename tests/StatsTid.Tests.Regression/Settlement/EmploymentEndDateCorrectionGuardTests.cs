using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Settlement;

/// <summary>
/// S71 / TASK-7102 (SPRINT-71 R13 + the R7a-409 reversal-pointer contract) — the end-date PUT's
/// correction guard, post-refactor:
///
/// <list type="bullet">
///   <item><description><b>R13 intermediate-year pins, BOTH directions:</b> a forward AND a
///   backward end-date correction crossing an INTERMEDIATE ferieår that holds an active
///   settlement row now 409s — the S70 {old,new} pair guard silently bypassed exactly this
///   shape (the intermediate year is in neither endpoint's ferieår);</description></item>
///   <item><description><b>The machine-readable reversal pointer:</b> the 409 carries
///   <c>reversalEndpoint</c> + <c>blockingSettlements</c> (identity + settlement-row sequence +
///   version — everything the reversal endpoint's body/If-Match need) while the S70 fields
///   (<c>conflictingSettlement</c>, the "R7a" error, the "3b" hint) stay
///   compatible;</description></item>
///   <item><description><b>Span sanity (negative control):</b> a same-ferieår correction is NOT
///   blocked by a settlement in an unrelated year — the widened span over-blocks
///   nothing.</description></item>
/// </list>
///
/// <para>The FULL S70 lifecycle behavior of the refactored PUT (now delegating to the shared
/// <c>EmploymentEndDateLifecycleWriter</c>) is re-pinned by the existing
/// <see cref="EmploymentEndDateLifecycleTests"/> suite — this class adds only the S71 deltas.
/// Real-clock anchored dates (the S70 convention; ±2-year margins).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentEndDateCorrectionGuardTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string OrgId = "STY01";
    private const string CoveringOrg = "STY01";  // S93 flat role-scope: covers STY01 by exact ORG_ONLY match (a MAO no longer covers a child)
    private const string VacationType = "VACATION";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    private static readonly DateOnly TodayUtc = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly PastDate = TodayUtc.AddYears(-2);
    private static readonly DateOnly FutureDate = TodayUtc.AddYears(2);

    private static int FerieaarOf(DateOnly d) => d.Month >= 9 ? d.Year : d.Year - 1;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot seeders
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════
    // R13 — FORWARD correction across an intermediate settled ferieår ⇒ 409.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R13_ForwardCorrection_IntermediateYearActiveRow_409_WithReversalPointer()
    {
        var employeeId = await SeedEmployeeAsync();
        // old = PastDate (flips the user — fine; the correction below is terminated-inclusive).
        await AssertOk(PutEndDateAsync(HrClient(), employeeId, PastDate, ifMatch: "\"1\""));

        // The INTERMEDIATE ferieår: strictly between ferieår(PastDate) and ferieår(FutureDate)
        // (±2y margins guarantee at least 3 years between them) — in NEITHER endpoint's year,
        // so the S70 pair guard would have silently passed.
        var oldYear = FerieaarOf(PastDate);
        var newYear = FerieaarOf(FutureDate);
        var intermediateYear = oldYear + 1;
        Assert.True(intermediateYear < newYear, "test geometry: the seeded year must be intermediate");
        await SeedSettlementRowAsync(employeeId, intermediateYear, state: "SETTLED");

        var rsp = await PutEndDateAsync(HrClient(), employeeId, FutureDate, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();

        // S70-compatible fields intact.
        Assert.Contains("R7a", body.GetProperty("error").GetString());
        Assert.Contains("3b", body.GetProperty("hint").GetString());
        Assert.Equal(intermediateYear,
            body.GetProperty("conflictingSettlement").GetProperty("entitlementYear").GetInt32());

        // The S71 reversal pointer: endpoint + the blocking rows' identity + sequence + version.
        Assert.Equal($"/api/admin/employees/{employeeId}/settlement-reversal",
            body.GetProperty("reversalEndpoint").GetString());
        var blockers = body.GetProperty("blockingSettlements");
        Assert.Equal(1, blockers.GetArrayLength());
        var blocker = blockers[0];
        Assert.Equal(VacationType, blocker.GetProperty("entitlementType").GetString());
        Assert.Equal(intermediateYear, blocker.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(1, blocker.GetProperty("sequence").GetInt32());
        Assert.Equal("SETTLED", blocker.GetProperty("settlementState").GetString());
        Assert.Equal(1L, blocker.GetProperty("version").GetInt64());

        // The widened span includes the intermediate year.
        var span = body.GetProperty("affectedEntitlementYears").EnumerateArray()
            .Select(e => e.GetInt32()).ToArray();
        Assert.Contains(intermediateYear, span);
        Assert.Contains(oldYear, span);
        Assert.Contains(newYear, span);

        // Fail-closed: NOTHING mutated.
        var tuple = await ReadEndDateTupleAsync(employeeId);
        Assert.Equal(PastDate, tuple.EndDate);
        Assert.Equal(2L, tuple.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // R13 — BACKWARD correction across an intermediate settled ferieår ⇒ 409
    // (cycle-2 Codex N: BOTH directions pinned).
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R13_BackwardCorrection_IntermediateYearActiveRow_409()
    {
        var employeeId = await SeedEmployeeAsync();
        // old = FutureDate (no flip), backward correction target = PastDate.
        await AssertOk(PutEndDateAsync(HrClient(), employeeId, FutureDate, ifMatch: "\"1\""));

        // ferieår(TodayUtc) is strictly between ferieår(PastDate) and ferieår(FutureDate)
        // (±2y margins), i.e. an intermediate year for the BACKWARD span.
        var intermediateYear = FerieaarOf(TodayUtc);
        Assert.True(FerieaarOf(PastDate) < intermediateYear && intermediateYear < FerieaarOf(FutureDate),
            "test geometry: ferieår(today) must be intermediate");
        await SeedSettlementRowAsync(employeeId, intermediateYear, state: "PENDING_REVIEW");

        var rsp = await PutEndDateAsync(HrClient(), employeeId, PastDate, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.Conflict, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(intermediateYear,
            body.GetProperty("blockingSettlements")[0].GetProperty("entitlementYear").GetInt32());

        // Fail-closed: the future end date stands, no flip happened.
        var tuple = await ReadEndDateTupleAsync(employeeId);
        Assert.Equal(FutureDate, tuple.EndDate);
        Assert.True(tuple.IsActive);
        Assert.Equal(2L, tuple.Version);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Negative control — the widened span over-blocks nothing: a same-ferieår correction
    // succeeds despite an active settlement in an unrelated (outside-span) year.
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SameFerieaarCorrection_SettlementOutsideSpan_StillAllowed()
    {
        var employeeId = await SeedEmployeeAsync();
        // Two FUTURE dates inside the SAME ferieår F (derived from the ferieår number so the
        // pin is robust year-round): 1 Oct F and 1 Mar F+1 both belong to ferieår F.
        var f = FerieaarOf(FutureDate);
        var oldDate = new DateOnly(f, 10, 1);
        var correctedDate = new DateOnly(f + 1, 3, 1);
        Assert.Equal(FerieaarOf(oldDate), FerieaarOf(correctedDate));
        await AssertOk(PutEndDateAsync(HrClient(), employeeId, oldDate, ifMatch: "\"1\""));

        // An active settlement in a year OUTSIDE the span {F}.
        await SeedSettlementRowAsync(employeeId, f - 1, state: "SETTLED");

        var rsp = await PutEndDateAsync(HrClient(), employeeId, correctedDate, ifMatch: "\"2\"");

        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var tuple = await ReadEndDateTupleAsync(employeeId);
        Assert.Equal(correctedDate, tuple.EndDate);
        Assert.Equal(3L, tuple.Version);
    }

    // ─────────────────────────────── HTTP helpers ───────────────────────────────

    private static string EndDateUrl(string employeeId) =>
        $"/api/admin/employees/{employeeId}/employment-end-date";

    private static async Task<HttpResponseMessage> PutEndDateAsync(
        HttpClient client, string employeeId, DateOnly? endDate, string? ifMatch)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, EndDateUrl(employeeId))
        {
            Content = JsonContent.Create(new { employmentEndDate = endDate }),
        };
        if (ifMatch is not null) req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(req);
    }

    private static async Task AssertOk(Task<HttpResponseMessage> call)
    {
        var rsp = await call;
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
    }

    private HttpClient HrClient()
    {
        var client = _factory.CreateClient();
        var svc = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = svc.GenerateToken(
            employeeId: "hr_s71_r13", name: "hr_s71_r13", role: StatsTidRoles.LocalHR,
            agreementCode: "AC", orgId: CoveringOrg,
            scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, CoveringOrg, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ─────────────────────────────── seeding / reads ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s71_r13_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    /// <summary>Direct settlement seed (the S70 EmploymentEndDateLifecycleTests shape — the
    /// guard is any-trigger, any-state-but-REVERSED).</summary>
    private async Task SeedSettlementRowAsync(string employeeId, int year, string state)
    {
        var snapshotJson = JsonSerializer.Serialize(new
        {
            recordedAbsences = Array.Empty<object>(),
            earned = 25m,
            used = 0m,
            planned = 0m,
            carryoverIn = 0m,
            annualQuota = 25m,
            carryoverMax = 5m,
            resetMonth = 9,
            okVersion = "OK24",
            transferAgreementDays = 0m,
            isFeriehindret = false,
        });

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO vacation_settlements
                (employee_id, entitlement_type, entitlement_year, sequence,
                 settlement_state, trigger, snapshot, transfer_days, payout_days, forfeit_days,
                 review_disposition, version)
            VALUES
                (@e, @t, @y, 1, @state, 'YEAR_END', @snapshot::jsonb, 0, 0, 0, NULL, 1)
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("t", VacationType);
        cmd.Parameters.AddWithValue("y", year);
        cmd.Parameters.AddWithValue("state", state);
        cmd.Parameters.AddWithValue("snapshot", snapshotJson);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<(DateOnly? EndDate, bool IsActive, long Version)> ReadEndDateTupleAsync(string employeeId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT employment_end_date, is_active, version FROM users WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (
            reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0),
            reader.GetBoolean(1),
            reader.GetInt64(2));
    }
}
