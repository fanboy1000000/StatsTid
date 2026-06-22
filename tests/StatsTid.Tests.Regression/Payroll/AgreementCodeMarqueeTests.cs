using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S34 / TASK-3414 MARQUEE — proves ADR-023 D2 option (b): PCS replays produce
/// byte-identical rule-engine output under mid-period admin mutation of the
/// dated <c>user_agreement_codes</c> row, because the
/// <see cref="EmploymentProfileResolver"/> (TASK-3406 cutover) reads
/// <c>agreement_code</c> via
/// <see cref="UserAgreementCodeRepository.GetByUserIdAtAsync"/> with the
/// end-exclusive temporal predicate, and a post-period Case C supersession
/// does NOT shrink the predecessor's window below the calculation period.
///
/// <para>
/// <b>Assertion target.</b> Byte-identical
/// <c>JsonSerializer.Serialize(replay.RuleResults, jsonOpts)</c> between the
/// baseline forward-calc and a replay-after-mutation. ReplayAsync stamps the
/// original manifest-id on every rule result, and the dated resolver lookup
/// returns the predecessor agreement_code for any <c>asOfDate</c> falling in
/// the predecessor's <c>[effective_from, effective_to)</c> window. The
/// custom rule-engine stub in this fixture emits agreement_code-discriminated
/// output (AC → MERARBEJDE, HK → OVERTIME_50) so the byte-identity assertion
/// would fail loudly if the resolver ever regressed to a live read.
/// </para>
///
/// <para>
/// <b>Why this is load-bearing.</b> The TASK-3406 cutover at
/// <c>EmploymentProfileResolver.cs:157-173</c> replaces a JOIN on
/// <c>users.agreement_code</c> with a separate
/// <see cref="UserAgreementCodeRepository.GetByUserIdAtAsync"/> call. If that
/// lookup ever reverted to a live read, a post-period admin agreement_code
/// change would flip the rule-engine output stream and replay determinism —
/// the foundation of ADR-016 D10 — would silently break for the 4th and
/// final rule-engine input. This test fails loudly the first time that
/// happens. MUST FAIL on the S33-baseline (pre-TASK-3406, agreement_code
/// joined live from users) and PASS post-S34 cutover.
/// </para>
///
/// <para>
/// <b>Direct-orchestration shape</b> (S33 <c>EmployeeProfileMarqueeTests</c>
/// precedent). Real <see cref="EmploymentProfileResolver"/> and
/// <see cref="PeriodCalculationService"/> in-process; the rule engine is
/// stubbed with an agreement_code-branching handler in this fixture (the
/// shared <c>TestFixtures.DefaultRuleEngineHandler</c> does NOT vary by
/// agreement_code, so we use a local stub here). Full Backend.Api boot is
/// intentionally skipped — the marquee proves the resolver+PCS contract,
/// not the HTTP surface.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AgreementCodeMarqueeTests : IAsyncLifetime
{
    private const string EmployeeId = "EMP-MARQUEE-AC";
    private const string OrgId = "STY01";
    private const string PredecessorAgreementCode = "AC";
    private const string SuccessorAgreementCode = "HK";
    private const string OkVersion = "OK24";

    // Period inside OK24 only — single-segment marquee (matches S33 employee-profile
    // marquee shape; OK transitions are out of scope for this fixture).
    private static readonly DateOnly PeriodStart = new(2026, 4, 1);
    private static readonly DateOnly PeriodEnd = new(2026, 4, 30);

    // Mutation date = today UTC. PeriodEnd is before today so the predecessor's
    // [predecessor.effective_from='0001-01-01', today) window covers PeriodStart.
    private static readonly DateOnly Today =
        DateOnly.FromDateTime(DateTime.UtcNow);

    private TestFixtures.DockerHarness _harness = null!;
    private EmploymentProfileResolver _resolver = null!;
    private UserAgreementCodeRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        // Apply full init.sql so user_agreement_codes + employee_profiles + users +
        // outbox_events tables exist (segmentation-only DDL is a 4-table subset).
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        _repo = new UserAgreementCodeRepository(_harness.Factory);
        _resolver = new EmploymentProfileResolver(_harness.Factory, _repo);

        await SeedUserAndProfileAndAgreementAsync(PredecessorAgreementCode);
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// Forward-calc against the seeded user_agreement_codes live row (agreement_code='AC'
    /// at effective_from='0001-01-01'); supersede via Case C cross-day routing today
    /// (closes predecessor at today; new live row at agreement_code='HK'); replay
    /// against the original manifest. The dated resolver lookup at
    /// segment.StartDate=PeriodStart=2026-04-01 must return the predecessor's 'AC'
    /// (predecessor's window now ends end-exclusive at today, still covers April 2026),
    /// so the rule-engine inputs (specifically <c>profile.agreementCode</c>) are
    /// byte-identical between baseline and replay. The agreement_code-branching
    /// stub handler emits MERARBEJDE for AC and OVERTIME_50 for HK, so byte-identity
    /// proves the resolver returned the predecessor agreement_code.
    /// </summary>
    [Fact]
    public async Task ReplayAsync_StableUnderAgreementCodeMutation_ResultByteIdentical()
    {
        var pcs = BuildPcsWithResolver(_resolver);

        // Caller-supplied profile (AgreementCode here matches the seed; PCS L344-358
        // overwrites this from the dated resolver per segment).
        var profileSeed = TestFixtures.Profile(EmployeeId) with { AgreementCode = PredecessorAgreementCode };
        var entries = TestFixtures.WeekdayEntriesForPeriod(EmployeeId, PeriodStart, PeriodEnd);
        var absences = Array.Empty<AbsenceEntry>();

        // Baseline forward calc via the (Obsolete) legacy shim — same entry point
        // S33 employee_profile marquee uses (BuildPlanForLegacyCallersAsync registers
        // the WtmNaturalKey hydrator on the plan's IPlannerEnrollment per S29).
#pragma warning disable CS0618 // Obsolete shim is the public entry point for hydrator wiring.
        var baseline = await pcs.CalculateAsync(
            profileSeed, entries, absences, PeriodStart, PeriodEnd, previousFlexBalance: 0m);
#pragma warning restore CS0618
        Assert.True(baseline.Success);
        Assert.NotEmpty(baseline.RuleResults);

        var baselineManifestId = baseline.RuleResults.First().ManifestId;
        Assert.NotEqual(Guid.Empty, baselineManifestId);

        var baselineJson = JsonSerializer.Serialize(baseline.RuleResults, SerializerOptions);

        // Case C cross-day supersession on user_agreement_codes: AC → HK.
        // SeedUserAndProfileAndAgreementAsync inserts the live row with the schema
        // DEFAULT effective_from='0001-01-01', so this supersession at Today creates
        // Case C cross-day routing: predecessor closed at effective_to=Today
        // (window ['0001-01-01', Today)), new row at effective_from=Today,
        // agreement_code='HK'. Replay at segment.StartDate=2026-04-01 falls in the
        // predecessor's window (today > 2026-04-01), so the resolver returns 'AC'.
        await SupersedeAgreementCodeAsync(
            newAgreementCode: SuccessorAgreementCode,
            effectiveFrom: Today);

        // Replay against the baseline's manifest id. Result must serialize
        // byte-identically because the resolver returns the predecessor agreement_code
        // for any asOfDate < Today, and the agreement_code-branching rule stub
        // produces identical output for the same agreement_code input.
        var replay = await pcs.ReplayAsync(
            baselineManifestId, profileSeed, entries, absences, previousFlexBalance: 0m);
        Assert.True(replay.Success);

        var replayJson = JsonSerializer.Serialize(replay.RuleResults, SerializerOptions);

        Assert.Equal(baselineJson, replayJson);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Builds a <see cref="PeriodCalculationService"/> wired with the dated
    /// resolver AND a custom rule-engine stub that branches on
    /// <c>profile.agreementCode</c>. The shared
    /// <c>TestFixtures.DefaultRuleEngineHandler</c> does NOT vary by
    /// agreement_code — so it would silently produce byte-identical output
    /// regardless of whether the resolver returned AC or HK, defeating the
    /// load-bearing assertion in this marquee. We override the handler
    /// here to make the test fail loudly if the cutover ever regresses.
    /// </summary>
    private PeriodCalculationService BuildPcsWithResolver(EmploymentProfileResolver resolver)
    {
        var stubHandler = new TestFixtures.StubHandler(AgreementCodeBranchingRuleHandler);
        var httpFactory = new SingleClientFactory(stubHandler);
        var wtmRepo = new WageTypeMappingRepository(_harness.Factory);
        var mappingService = new PayrollMappingService(
            _harness.Factory, NullLogger<PayrollMappingService>.Instance, wtmRepo);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
            })
            .Build();

        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            _harness.EventStore,
            _harness.Factory,
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryRuleClassificationProvider(TestFixtures.RuleSet),
            localAgreementProfileRepo: null,
            profileResolver: resolver);
    }

    /// <summary>
    /// Rule-engine stub that emits <c>timeType</c> dependent on
    /// <c>profile.agreementCode</c>. AC → "MERARBEJDE" (matches AC's
    /// HasMerarbejde=true / HasOvertime=false production rule), HK →
    /// "OVERTIME_50" (matches HK's HasOvertime=true rule). Any other
    /// rule endpoint returns the same simple-success shape as the default
    /// handler so PCS walks the full segment loop.
    /// </summary>
    private static HttpResponseMessage AgreementCodeBranchingRuleHandler(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        if (!path.EndsWith("/api/rules/evaluate", StringComparison.Ordinal))
        {
            return TestFixtures.DefaultRuleEngineHandler(request);
        }

        var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "{}";
        using var doc = JsonDocument.Parse(body);
        var ruleId = doc.RootElement.TryGetProperty("ruleId", out var rid)
            ? rid.GetString() ?? "UNKNOWN" : "UNKNOWN";
        var entries = doc.RootElement.TryGetProperty("entries", out var ents)
            ? ents.EnumerateArray().ToList()
            : new List<JsonElement>();
        var employeeId = doc.RootElement.TryGetProperty("profile", out var prof) &&
                         prof.TryGetProperty("employeeId", out var eid)
            ? eid.GetString() ?? "EMP"
            : "EMP";
        var agreementCode = doc.RootElement.TryGetProperty("profile", out var pr2) &&
                            pr2.TryGetProperty("agreementCode", out var ac)
            ? ac.GetString() ?? "AC"
            : "AC";

        // For NORM_CHECK_37H emit a timeType discriminated by agreement_code.
        // For other rules return empty line items so they don't contribute.
        var lineItems = ruleId == "NORM_CHECK_37H"
            ? entries.Select(e =>
                {
                    var date = e.TryGetProperty("date", out var d) ? d.GetString() : null;
                    var hours = e.TryGetProperty("hours", out var h) ? h.GetDecimal() : 0m;
                    var timeType = string.Equals(agreementCode, "AC", StringComparison.Ordinal)
                        ? "MERARBEJDE"
                        : "OVERTIME_50";
                    return new
                    {
                        timeType,
                        hours,
                        rate = 1.0m,
                        date,
                    };
                }).ToList<object>()
            : new List<object>();

        var payload = new
        {
            ruleId,
            employeeId,
            success = true,
            // Stash the resolved agreement_code on the rule result so the byte-stable
            // comparator picks it up — independent of the line-items branching.
            agreementCode,
            lineItems,
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Inserts a <c>users</c> row, <c>employee_profiles</c> row, AND
    /// <c>user_agreement_codes</c> live row for the marquee employee.
    /// All three use the schema default <c>effective_from='0001-01-01'</c>
    /// so a today-effective Case C supersession on agreement_code closes a
    /// row whose window covers the entire test period.
    /// </summary>
    private async Task SeedUserAndProfileAndAgreementAsync(string agreementCode)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();

        await using (var orgCmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path,
                                       agreement_code, ok_version)
            VALUES (@orgId, 'Marquee Org', 'ORGANISATION', NULL, '/MARQ/',
                    @agreementCode, @okVersion)
            ON CONFLICT (org_id) DO NOTHING
            """, conn))
        {
            orgCmd.Parameters.AddWithValue("orgId", OrgId);
            orgCmd.Parameters.AddWithValue("agreementCode", agreementCode);
            orgCmd.Parameters.AddWithValue("okVersion", OkVersion);
            await orgCmd.ExecuteNonQueryAsync();
        }

        await using (var userCmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version)
            VALUES (@userId, @userId, 'dev-only', 'Agreement-Code Marquee Employee', NULL,
                    @orgId, @agreementCode, @okVersion)
            ON CONFLICT (user_id) DO NOTHING
            """, conn))
        {
            userCmd.Parameters.AddWithValue("userId", EmployeeId);
            userCmd.Parameters.AddWithValue("orgId", OrgId);
            userCmd.Parameters.AddWithValue("agreementCode", agreementCode);
            userCmd.Parameters.AddWithValue("okVersion", OkVersion);
            await userCmd.ExecuteNonQueryAsync();
        }

        // employee_profiles row at the schema default effective_from='0001-01-01'.
        // S53/TASK-5306 (a7aee58): employee_profiles.weekly_norm_hours removed
        // (universal 37h norm); column + its seed value dropped from this INSERT.
        await using (var epCmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version)
            VALUES (
                gen_random_uuid(), @employeeId, 1.000, NULL,
                DEFAULT, NULL, 1)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            epCmd.Parameters.AddWithValue("employeeId", EmployeeId);
            await epCmd.ExecuteNonQueryAsync();
        }

        // user_agreement_codes live row at schema default effective_from='0001-01-01'.
        await using (var uacCmd = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (
                assignment_id, user_id, agreement_code,
                effective_from, effective_to, version)
            VALUES (
                gen_random_uuid(), @userId, @agreementCode,
                DEFAULT, NULL, 1)
            """, conn))
        {
            uacCmd.Parameters.AddWithValue("userId", EmployeeId);
            uacCmd.Parameters.AddWithValue("agreementCode", agreementCode);
            await uacCmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Calls <see cref="UserAgreementCodeRepository.SupersedeAndCreateAsync"/>
    /// directly to flip the live agreement_code via Case C cross-day routing
    /// (seed row's effective_from='0001-01-01' is strictly less than today).
    /// expectedVersion=null bypasses the OCE check — the marquee is about
    /// dated-resolver byte-stability, not concurrency.
    /// </summary>
    private async Task SupersedeAgreementCodeAsync(
        string newAgreementCode, DateOnly effectiveFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var req = new UserAgreementCodeSupersedeRequest(
            UserId: EmployeeId,
            AgreementCode: newAgreementCode,
            EffectiveFrom: effectiveFrom);
        await _repo.SupersedeAndCreateAsync(conn, tx, req, expectedVersion: null);
        await tx.CommitAsync();
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class InMemoryRuleClassificationProvider : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public InMemoryRuleClassificationProvider(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }
}
