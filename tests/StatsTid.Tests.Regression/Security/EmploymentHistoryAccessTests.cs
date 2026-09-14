using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// S141 / TASK-14113 (refinement C2) — the ACCESS pins for the employment-history read,
/// <c>GET /api/hr/employees/{employeeId}/history</c>, plus the three behavioural facts that make the
/// read worth gating.
///
/// <para>
/// <b>Why this suite exists at all, and why the endpoint's own author wrote it.</b> The Step-0b plan
/// review found that "HR-gated and organisation-scoped on the subject's CURRENT organisation" — a
/// security invariant — had NO test owner anywhere in the sprint. An employment history discloses one
/// named person's position changes, working-time changes and agreement changes, so an unpinned gate is
/// not an academic gap. The pins therefore ship with the endpoint rather than waiting for a test task
/// that does not cover it.
/// </para>
///
/// <para>
/// <b>RED-FIRST, reasoned from the spec — and CI-VERIFIED, never claimed green locally.</b> Docker
/// does not run on the authoring machine, so every fact below is derived from
/// <c>EmploymentHistoryEndpoints.cs</c> / <c>EmploymentHistoryReadRepository.cs</c> /
/// <c>OrgScopeValidator.cs</c> as read, not from an observed run. They first execute in the
/// sprint-close watched CI run. Each fact's doc comment states what deleting or weakening would turn
/// it RED, so a reviewer can check the pin's discrimination without running it.
/// </para>
///
/// <para>
/// <b>PAT-008 — one fixed anchor, one host per fact.</b> <c>F = 2026-03-11</c> (a Wednesday). The
/// endpoint derives "today" from the injected <see cref="TimeProvider"/>, and the SCHEDULED fact below
/// turns on which side of today an interval starts, so a wall-clock host would make that pin a
/// coin-flip. Per <see cref="FixedTimeProvider"/>'s boot-order rule, every fixture row is written AFTER
/// the fixed host's first <c>CreateClient()</c> — the startup seeders re-run on that call and would
/// otherwise overwrite the state a fact depends on.
/// </para>
///
/// <para>
/// <b>Fixtures are seeded by direct SQL, on purpose.</b> A future-dated profile row is written with an
/// <c>INSERT</c> rather than through the profile PUT, so this suite does NOT depend on wave 2's sibling
/// task having lifted the endpoint-side future-date refusal. The facts here are about the HISTORY
/// READ; coupling them to another agent's in-flight change would make a red here ambiguous about which
/// task broke.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentHistoryAccessTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    /// <summary>The subject's CURRENT organisation — the only one whose HR may read them.</summary>
    private const string SubjectOrg = "STY_HIST_SUBJ";

    /// <summary>A disjoint organisation. Its HR must never reach the subject.</summary>
    private const string ForeignOrg = "STY_HIST_FOREIGN";

    /// <summary>Wednesday. The one fixed "today" for every fact in this class.</summary>
    private static readonly DateOnly F = new(2026, 3, 11);

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // Deliberately NOT booted here (PAT-008 "one host per fact").
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private static int Seq;
    private static string NextId(string prefix) => $"{prefix}_{Interlocked.Increment(ref Seq)}";
    private static string Url(string employeeId) => $"/api/hr/employees/{employeeId}/history";

    // ════════════════════════════════════════════════════════════════════════
    // THE SECURITY INVARIANT — HR floor + the subject's CURRENT organisation.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The baseline the denials are measured against.</b> HR whose scope IS the subject's current
    /// organisation reads the history: 200, and the subject echoed back. Without this fact the three
    /// denials below would all pass on an endpoint that refuses everyone — the classic way a security
    /// suite proves nothing.
    /// </summary>
    [Fact]
    public async Task Access_HrInSubjectsCurrentOrg_Returns200()
    {
        using var host = _factory.WithFixedToday(F);
        _ = host.CreateClient(); // boot the seeders BEFORE seeding (PAT-008 boot-order rule)

        var employeeId = NextId("hist_ok");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, SubjectOrg);

        var rsp = await Client(host, HrToken(SubjectOrg)).GetAsync(Url(employeeId));
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);

        using var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync());
        Assert.Equal(employeeId, doc.RootElement.GetProperty("employeeId").GetString());
    }

    /// <summary>
    /// <b>★ Out of organisation ⇒ 403.</b> HR of a DISJOINT organisation asks for a subject who is not
    /// theirs. The scope loop in
    /// <c>OrgScopeValidator.ValidateEmployeeAccessIncludingTerminatedAsync</c> resolves the subject
    /// through <c>users.primary_org_id</c> — their CURRENT home — and no scope of this actor covers it.
    /// RED if the endpoint's validator call were dropped and <c>HROrAbove</c> left to stand alone: the
    /// policy proves the actor is SOME organisation's HR, never that they are THIS subject's.
    /// </summary>
    [Fact]
    public async Task Access_HrInForeignOrg_Returns403()
    {
        using var host = _factory.WithFixedToday(F);
        _ = host.CreateClient();

        var employeeId = NextId("hist_foreign");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, SubjectOrg);
        // ForeignOrg must exist as a real organisation for the actor's scope to resolve; a throwaway
        // employee is RegressionSeed's only organisation-creating path and its own row is irrelevant here.
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, NextId("hist_foreign_orgseed"), ForeignOrg);

        var rsp = await Client(host, HrToken(ForeignOrg)).GetAsync(Url(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);

        using var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync());
        Assert.Equal("Access denied", doc.RootElement.GetProperty("error").GetString());
    }

    /// <summary>
    /// <b>★ Below the HR floor ⇒ 403, even though the scope COVERS the subject.</b> This is the
    /// mixed-role leak (FAIL-001, closed by S76 / TASK-7600) in its exact shape: the token's PRIMARY
    /// role is LocalHR — enough to clear the <c>HROrAbove</c> policy — but its HR scope sits in a
    /// DISJOINT organisation, and the only scope that covers the subject is a LocalLeader one. Without
    /// the <see cref="StatsTidRoles.LocalHR"/> floor argument the validator would admit on that
    /// covering Leader scope and hand a leader another organisation's employment history.
    ///
    /// <para>RED the moment the endpoint calls the NO-FLOOR overload (or passes <c>null</c>) — which is
    /// the single most likely way this gate gets weakened later, because the call still looks correct.
    /// This is the highest-value fact in the file.</para>
    /// </summary>
    [Fact]
    public async Task Access_MixedRoleHrElsewherePlusLeaderCoveringSubject_Returns403()
    {
        using var host = _factory.WithFixedToday(F);
        _ = host.CreateClient();

        var employeeId = NextId("hist_mixed");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, SubjectOrg);
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, NextId("hist_mixed_orgseed"), ForeignOrg);

        var rsp = await Client(host, MixedHrElsewhereLeaderHereToken()).GetAsync(Url(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>
    /// <b>A LocalLeader token is refused by the POLICY, before the handler.</b> A leader in the
    /// subject's own organisation — an actor who legitimately sees that person every day — still may not
    /// read their employment history. Answers 403 from <c>RequireAuthorization("HROrAbove")</c>, so no
    /// handler code runs and no body is asserted. RED if the endpoint's policy were relaxed to
    /// <c>LeaderOrAbove</c> or dropped.
    /// </summary>
    [Fact]
    public async Task Access_LeaderInSubjectsOwnOrg_Returns403()
    {
        using var host = _factory.WithFixedToday(F);
        _ = host.CreateClient();

        var employeeId = NextId("hist_leader");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, SubjectOrg);

        var rsp = await Client(host, LeaderToken(SubjectOrg)).GetAsync(Url(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>
    /// <b>An employee may not read their OWN history here.</b> Not an oversight — a recorded decision:
    /// this is an HR surface, and whether an employee should see their own dated history is a separate
    /// product question nobody has ruled on. The <c>HROrAbove</c> policy refuses the token, and the
    /// terminated-inclusive validator behind it deliberately has no own-data branch either, so the
    /// answer would be 403 even if the policy were widened. RED if an own-data short-circuit were added
    /// to either layer without a ruling.
    /// </summary>
    [Fact]
    public async Task Access_EmployeeReadingOwnHistory_Returns403()
    {
        using var host = _factory.WithFixedToday(F);
        _ = host.CreateClient();

        var employeeId = NextId("hist_self");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, SubjectOrg);

        var rsp = await Client(host, EmployeeToken(employeeId, SubjectOrg)).GetAsync(Url(employeeId));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    /// <summary>
    /// <b>An unknown subject answers 403, not 404.</b> Deliberate: distinguishing "no such employee"
    /// from "not yours" would let an out-of-scope caller enumerate employee ids one request at a time.
    /// The validator returns its own deny for both. RED if a "does this employee exist?" probe were
    /// added ahead of the scope check.
    /// </summary>
    [Fact]
    public async Task Access_UnknownEmployee_Returns403NotFound404()
    {
        using var host = _factory.WithFixedToday(F);
        _ = host.CreateClient();

        var rsp = await Client(host, HrToken(SubjectOrg)).GetAsync(Url("no_such_employee_14113"));
        Assert.Equal(HttpStatusCode.Forbidden, rsp.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════
    // BEHAVIOUR — the three facts that make the gated data worth reading.
    // (The register assigns TASK-14113 only the security pin; these ride along
    // because an unpinned read is a gate around an unverified answer.)
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>★ Ordered by EFFECTIVE date, and that is the whole point.</b> Three profile intervals are
    /// written in an order that does NOT match their effective dates — the middle one is inserted LAST,
    /// exactly as a backdated correction would arrive. The response must come back oldest-effective
    /// first. RED if the read ever ordered by <c>created_at</c> / insertion order, which is the audit
    /// log's ordering and the very substitution this endpoint exists to replace.
    /// </summary>
    [Fact]
    public async Task History_OrdersByEffectiveDate_NotByWhenTheChangeWasRecorded()
    {
        using var host = _factory.WithFixedToday(F);
        _ = host.CreateClient();

        var employeeId = NextId("hist_order");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, SubjectOrg,
            effectiveFrom: new DateOnly(2024, 1, 1), position: "Fuldmægtig");

        // Close the seeded open row at 2025-01-01 and append the 2025 interval (recorded 2nd).
        await ExecAsync(
            "UPDATE employee_profiles SET effective_to = @to WHERE employee_id = @id AND effective_to IS NULL",
            ("to", NpgsqlDbType.Date, new DateOnly(2025, 1, 1)), ("id", NpgsqlDbType.Text, employeeId));
        await InsertProfileRowAsync(employeeId, new DateOnly(2025, 1, 1), null, 1.000m, "Specialkonsulent");

        // The BACKDATED correction, recorded LAST but effective in the MIDDLE: split 2024 at 2024-07-01.
        await ExecAsync(
            "UPDATE employee_profiles SET effective_to = @to WHERE employee_id = @id AND effective_from = @from",
            ("to", NpgsqlDbType.Date, new DateOnly(2024, 7, 1)),
            ("id", NpgsqlDbType.Text, employeeId),
            ("from", NpgsqlDbType.Date, new DateOnly(2024, 1, 1)));
        await InsertProfileRowAsync(employeeId, new DateOnly(2024, 7, 1), new DateOnly(2025, 1, 1), 0.800m, "Fuldmægtig");

        using var doc = JsonDocument.Parse(
            await Client(host, HrToken(SubjectOrg)).GetStringAsync(Url(employeeId)));
        var intervals = doc.RootElement.GetProperty("profileHistory").EnumerateArray().ToList();

        Assert.Equal(3, intervals.Count);
        Assert.Equal("2024-01-01", intervals[0].GetProperty("effectiveFrom").GetString());
        Assert.Equal("2024-07-01", intervals[1].GetProperty("effectiveFrom").GetString());
        Assert.Equal("2025-01-01", intervals[2].GetProperty("effectiveFrom").GetString());

        // The first interval is the baseline — empty changedFields means "nothing to compare against",
        // and the pin says so explicitly so a future reader does not read it as "nothing changed".
        Assert.True(intervals[0].GetProperty("isInitial").GetBoolean());
        Assert.Empty(intervals[0].GetProperty("changedFields").EnumerateArray());

        // The backdated middle interval dropped to 0.8 FTE and kept its title: partTimeFraction only.
        var middleChanged = intervals[1].GetProperty("changedFields").EnumerateArray()
            .Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { "partTimeFraction" }, middleChanged);
    }

    /// <summary>
    /// <b>★ A change that has not started yet is SHOWN, marked SCHEDULED.</b> Wave 1 made a future-dated
    /// change legal, so the history may contain an interval whose <c>effectiveFrom</c> is after today.
    /// The owner's standing requirement for this sprint is that a scheduled change is visible wherever a
    /// profile is read, and a history view is the most surprising possible place to hide one. Pins all
    /// three statuses off the ONE fixed today, so the boundary rule is nailed down too: an interval
    /// ending ON today is already PAST (end-exclusive, ADR-018 D9), not current.
    ///
    /// <para>RED if the read filtered future rows out, if it marked them CURRENT, or if the end-exclusive
    /// boundary were read as inclusive.</para>
    /// </summary>
    [Fact]
    public async Task History_FutureDatedInterval_IsIncludedAndMarkedScheduled()
    {
        using var host = _factory.WithFixedToday(F);
        _ = host.CreateClient();

        var employeeId = NextId("hist_future");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, SubjectOrg,
            effectiveFrom: new DateOnly(2025, 1, 1), position: "Fuldmægtig");

        // [2025-01-01, F) PAST — it ends ON the fixed today, so end-exclusivity makes it already over.
        await ExecAsync(
            "UPDATE employee_profiles SET effective_to = @to WHERE employee_id = @id AND effective_to IS NULL",
            ("to", NpgsqlDbType.Date, F), ("id", NpgsqlDbType.Text, employeeId));
        // [F, F+30) CURRENT.
        await InsertProfileRowAsync(employeeId, F, F.AddDays(30), 1.000m, "Specialkonsulent");
        // [F+30, ∞) SCHEDULED — booked, not yet in force.
        await InsertProfileRowAsync(employeeId, F.AddDays(30), null, 0.600m, "Specialkonsulent");

        using var doc = JsonDocument.Parse(
            await Client(host, HrToken(SubjectOrg)).GetStringAsync(Url(employeeId)));

        Assert.Equal(F.ToString("yyyy-MM-dd"), doc.RootElement.GetProperty("today").GetString());
        var statuses = doc.RootElement.GetProperty("profileHistory").EnumerateArray()
            .Select(i => i.GetProperty("status").GetString()).ToList();
        Assert.Equal(new[] { "PAST", "CURRENT", "SCHEDULED" }, statuses);
    }

    /// <summary>
    /// <b>The agreement-code track is read as well as the profile track</b>, with the same ordering and
    /// the same interval semantics — the response is not half an answer. RED if the second range read
    /// were dropped or returned the profile rows.
    /// </summary>
    [Fact]
    public async Task History_AgreementCodeTrack_IsReadAndOrderedByEffectiveDate()
    {
        using var host = _factory.WithFixedToday(F);
        _ = host.CreateClient();

        var employeeId = NextId("hist_agr");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, employeeId, SubjectOrg,
            agreementCode: "AC", effectiveFrom: new DateOnly(2024, 1, 1));

        await ExecAsync(
            "UPDATE user_agreement_codes SET effective_to = @to WHERE user_id = @id AND effective_to IS NULL",
            ("to", NpgsqlDbType.Date, new DateOnly(2025, 6, 1)), ("id", NpgsqlDbType.Text, employeeId));
        await ExecAsync(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @id, 'HK', @from, NULL, 1)
            """,
            ("id", NpgsqlDbType.Text, employeeId), ("from", NpgsqlDbType.Date, new DateOnly(2025, 6, 1)));

        using var doc = JsonDocument.Parse(
            await Client(host, HrToken(SubjectOrg)).GetStringAsync(Url(employeeId)));
        var intervals = doc.RootElement.GetProperty("agreementCodeHistory").EnumerateArray().ToList();

        Assert.Equal(2, intervals.Count);
        Assert.Equal("AC", intervals[0].GetProperty("agreementCode").GetString());
        Assert.Equal("2025-06-01", intervals[0].GetProperty("effectiveTo").GetString());
        Assert.Equal("HK", intervals[1].GetProperty("agreementCode").GetString());
        Assert.Equal(new[] { "agreementCode" },
            intervals[1].GetProperty("changedFields").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    /// <summary>
    /// <b>An inverted window is a caller mistake, not an empty history.</b> <c>from &gt;= to</c> answers
    /// 422 rather than a 200 with two empty tracks, because "you mistyped the filter" and "this employee
    /// has never changed" must not look identical on screen. RED if the guard were removed.
    /// </summary>
    [Fact]
    public async Task History_InvertedWindow_Returns422NotEmpty200()
    {
        using var host = _factory.WithFixedToday(F);
        _ = host.CreateClient();

        var employeeId = NextId("hist_window");
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, SubjectOrg);

        var rsp = await Client(host, HrToken(SubjectOrg))
            .GetAsync($"{Url(employeeId)}?from=2026-01-01&to=2025-01-01");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);
    }

    // ─────────────────────────────── fixtures ───────────────────────────────

    private async Task ExecAsync(string sql, params (string Name, NpgsqlDbType Type, object Value)[] ps)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
#pragma warning disable CA2100 // Constant SQL from this file only; every value is a bound parameter.
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, type, value) in ps)
            cmd.Parameters.Add(new NpgsqlParameter(name, type) { Value = value });
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Appends one dated <c>employee_profiles</c> interval. <c>employment_category</c> is copied from the
    /// <c>users</c> row exactly as <see cref="RegressionSeed"/> and production do — the column is NOT NULL
    /// since S138 / TASK-13804, so a placeholder here would be both a lie and a constraint violation.
    /// </summary>
    private Task InsertProfileRowAsync(
        string employeeId, DateOnly from, DateOnly? to, decimal partTimeFraction, string? position) =>
        ExecAsync(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version, employment_category)
            VALUES (
                gen_random_uuid(), @id, @fraction, @position, @from, @to, 1,
                (SELECT u.employment_category FROM users u WHERE u.user_id = @id))
            """,
            ("id", NpgsqlDbType.Text, employeeId),
            ("fraction", NpgsqlDbType.Numeric, partTimeFraction),
            ("position", NpgsqlDbType.Text, (object?)position ?? DBNull.Value),
            ("from", NpgsqlDbType.Date, from),
            ("to", NpgsqlDbType.Date, to.HasValue ? to.Value : (object)DBNull.Value));

    // ─────────────────────────────── tokens ───────────────────────────────

    private static HttpClient Client(WebApplicationFactory<Program> host, string token)
    {
        var c = host.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static JwtTokenService NewTokenService() => new(new JwtSettings
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = DevFallbackSigningKey,
        ExpirationMinutes = 60,
    });

    private static string HrToken(string orgId) => NewTokenService().GenerateToken(
        employeeId: "hr_s141_hist_actor", name: "hr_s141_hist_actor", role: StatsTidRoles.LocalHR,
        agreementCode: "AC", orgId: orgId,
        scopes: new[] { new RoleScope(StatsTidRoles.LocalHR, orgId, "ORG_ONLY") });

    private static string LeaderToken(string orgId) => NewTokenService().GenerateToken(
        employeeId: "leader_s141_hist_actor", name: "leader_s141_hist_actor", role: StatsTidRoles.LocalLeader,
        agreementCode: "AC", orgId: orgId,
        scopes: new[] { new RoleScope(StatsTidRoles.LocalLeader, orgId, "ORG_ONLY") });

    private static string EmployeeToken(string employeeId, string orgId) => NewTokenService().GenerateToken(
        employeeId: employeeId, name: employeeId, role: StatsTidRoles.Employee,
        agreementCode: "AC", orgId: orgId,
        scopes: new[] { new RoleScope(StatsTidRoles.Employee, orgId, "ORG_ONLY") });

    /// <summary>
    /// The FAIL-001 shape: primary role LocalHR (clears the <c>HROrAbove</c> policy) with its HR scope in
    /// a DISJOINT organisation, plus a LocalLeader scope that DOES cover the subject. Only the per-scope
    /// LocalHR floor keeps this token out.
    /// </summary>
    private static string MixedHrElsewhereLeaderHereToken() => NewTokenService().GenerateToken(
        employeeId: "mixed_s141_hist_actor", name: "mixed_s141_hist_actor", role: StatsTidRoles.LocalHR,
        agreementCode: "AC", orgId: ForeignOrg,
        scopes: new[]
        {
            new RoleScope(StatsTidRoles.LocalHR, ForeignOrg, "ORG_ONLY"),
            new RoleScope(StatsTidRoles.LocalLeader, SubjectOrg, "ORG_ONLY"),
        });
}
