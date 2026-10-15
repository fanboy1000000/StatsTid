using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S145 / TASK-14519-T (adjudicator R-20, QUAL-203) — the activation key of a local agreement profile
/// is the employee's DATED record, never the request body.
///
/// <para>
/// <b>The story, in plain language.</b> An organisation can switch on a "local agreement profile" (its
/// own norm hours and the like) partway through a month. That date splits the month into two pieces, and
/// under the live rule set a split month is refused. A profile belongs to ONE (organisation, agreement,
/// OK version). So the question "did a profile switch on this month?" only makes sense for the agreement
/// and OK version the employee actually had on each day, and that is what the employee's dated records say.
/// Before this task the question was asked with whatever the request body said, so a stale body for a past
/// month could invent a split the employee never had (refusing the month for nothing) or miss a real one
/// (paying a month that should have split as one piece), and the manifest's cause summary recorded the wrong
/// reason. No paid figure depends on it (the Rule Engine applies no local profile); the boundary is the
/// product.
/// </para>
///
/// <para>
/// <b>Fixtures.</b> April 2026 is wholly OK26 (OK26 runs from 2026-04-01, <c>OkVersionResolver</c>), so the
/// period start is not an interior OK transition and the only interior dates are the ones a fact seeds.
/// The planner is the real <see cref="PeriodPlanner"/> through the real
/// <see cref="PeriodCalculationService"/> with a real <see cref="LocalAgreementProfileRepository"/> and the
/// dated <see cref="EmploymentProfileResolver"/>; only the Rule Engine HTTP call is stubbed. All facts are
/// Docker-gated: CI-verified, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class LocalProfileActivationKeyTests : IAsyncLifetime
{
    private const string OrgId = "STY_S145_R20";

    // 15 April 2026 is a Wednesday, 16 April a Thursday, 20 April a Monday.
    private static readonly DateOnly Apr01 = new(2026, 4, 1);
    private static readonly DateOnly Apr30 = new(2026, 4, 30);
    private static readonly DateOnly Apr15 = new(2026, 4, 15);
    private static readonly DateOnly Apr16 = new(2026, 4, 16);
    private static readonly DateOnly Apr20 = new(2026, 4, 20);

    private TestFixtures.DockerHarness _harness = null!;
    private PostgresEventStore _eventStore = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
        await SeedWageTypeMappingsForAsync("AC", "OK26");
        _eventStore = new PostgresEventStore(_harness.Factory, new OutboxServiceContext("payroll"));
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // 1. The body says one thing, the record says another: the record's key finds the profile
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// (S145 R-20, fact 1) The request body says HK / OK24 (a stale caller); the employee's dated record
    /// says AC and the date (2026-04) is OK26. The organisation switched on its AC / OK26 profile on
    /// 15 April. The activation is asked under the RECORD's key (ORG, AC, OK26), so it is found: the month
    /// plans in two pieces and the persisted manifest's <c>boundary_cause_summary</c> contains
    /// <c>LocalProfileActivation</c> and holds exactly two segments. Observation surface: the
    /// <c>segment_manifests</c> row of the returned manifest id (the cause summary and the segment count).
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): the production mutation "the
    /// activation lookup is keyed on the request body again" (<c>GetActivationsInPeriodAsync(profile.OrgId,
    /// profile.AgreementCode, profile.OkVersion, ...)</c> over the whole period). It asks (ORG, HK, OK24),
    /// finds nothing, the month is one segment, and <c>Assert.Contains("LocalProfileActivation",
    /// manifest.BoundaryCauseSummary)</c> trips (the segment-count assertion would trip too).
    /// </summary>
    [Fact]
    public async Task BodySaysHkOk24_RecordSaysAcOk26_ActivationFoundUnderTheRecordsKey_ManifestRecordsLocalProfileActivation()
    {
        // S145 R-20:
        const string employeeId = "EMP-S145-R20-BODY-VS-RECORD";
        await SeedEmployeeAsync(employeeId, "AC", "OK26");
        await SeedActivationAsync("AC", "OK26", Apr15);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);

        var outcome = await RunShimAsync(
            BuildPcs(HoursOf(entries), TestFixtures.StraddleSafeRuleSet),
            Body(employeeId, agreementCode: "HK", okVersion: "OK24"));

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        var manifest = await LoadManifestAsync(outcome.ManifestId);
        Assert.Contains("LocalProfileActivation", manifest.BoundaryCauseSummary);
        Assert.Equal(2, manifest.SegmentCount);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 2. The mirror: a profile that matches only the body invents no boundary
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// (S145 R-20, fact 2 — the mirror) The same body (HK / OK24), the same record (AC; the date is OK26),
    /// but now the only activation row belongs to the BODY's scope: (ORG, HK, OK24), effective 15 April.
    /// The employee never had HK or OK24 that month, so no profile of theirs switched on: the month is ONE
    /// segment and the manifest's <c>boundary_cause_summary</c> does not contain
    /// <c>LocalProfileActivation</c>. Observation surface: the same manifest row (cause summary, segment
    /// count 1).
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): the production mutation "the
    /// activation lookup is keyed on the request body again". It asks (ORG, HK, OK24), finds the 15 April
    /// row, invents a boundary the employee never had, and <c>Assert.DoesNotContain("LocalProfileActivation",
    /// manifest.BoundaryCauseSummary)</c> trips (as does <c>Assert.Equal(1, manifest.SegmentCount)</c>).
    /// </summary>
    [Fact]
    public async Task BodySaysHkOk24_RecordSaysAcOk26_ActivationOfTheBodysScopeInventsNoBoundary()
    {
        // S145 R-20:
        const string employeeId = "EMP-S145-R20-MIRROR";
        await SeedEmployeeAsync(employeeId, "AC", "OK26");
        await SeedActivationAsync("HK", "OK24", Apr15);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);

        var outcome = await RunShimAsync(
            BuildPcs(HoursOf(entries), TestFixtures.StraddleSafeRuleSet),
            Body(employeeId, agreementCode: "HK", okVersion: "OK24"));

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        var manifest = await LoadManifestAsync(outcome.ManifestId);
        Assert.DoesNotContain("LocalProfileActivation", manifest.BoundaryCauseSummary);
        Assert.Equal(1, manifest.SegmentCount);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3. The straddle: a second (agreement, OK-version) interval is asked too
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// (S145 R-20, fact 3 — the straddle) The employee's agreement changes AC to HK on 16 April, and the
    /// organisation switches on its HK / OK26 profile on 20 April. The body says AC / OK26 (the month's
    /// first interval). Two intervals have two keys: (ORG, AC, OK26) for 1-15 April and (ORG, HK, OK26) for
    /// 16-30 April; the second finds the 20 April activation. Under the LIVE rule set the planner refuses
    /// the split month, and its structured refusal (<see cref="PlannerInvariantViolation.InteriorBoundaryCauses"/>,
    /// also spelled in the message at <c>PeriodPlanner.cs</c> "interior boundary causes") lists, in date
    /// order, <see cref="BoundaryCause.AgreementCodeChange"/> (16th) then
    /// <see cref="BoundaryCause.LocalProfileActivation"/> (20th); three EMPLOYED segments. Observation
    /// surface: the thrown <see cref="PlannerInvariantViolation"/> (its causes list and message); nothing is
    /// written, so no manifest.
    ///
    /// Red conditions (Docker-gated — cannot run locally; CI-verified): the production mutation "a single
    /// lookup over the whole period" (keyed on the period start's record, or on the body, once). It asks
    /// only (ORG, AC, OK26) and never the HK interval, so the 20 April row is missed; the refusal names
    /// <c>AgreementCodeChange</c> alone and <c>Assert.Equal(new[] { AgreementCodeChange,
    /// LocalProfileActivation }, ex.InteriorBoundaryCauses)</c> trips (and the employed-segment count is 2,
    /// not 3).
    /// </summary>
    [Fact]
    public async Task AgreementChangeThenHkActivation_SecondIntervalIsAsked_RefusalNamesBothCauses()
    {
        // S145 R-20:
        const string employeeId = "EMP-S145-R20-STRADDLE";
        await SeedEmployeeAsync(employeeId, "AC", "OK26");
        await SupersedeAgreementCodeAsync(employeeId, "HK", Apr16);
        await SeedActivationAsync("HK", "OK26", Apr20);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);

        var ex = await Assert.ThrowsAsync<PlannerInvariantViolation>(() =>
            RunShimAsync(
                BuildPcs(HoursOf(entries), new RuleRegistry().GetAll()),
                Body(employeeId, agreementCode: "AC", okVersion: "OK26")));

        Assert.True(ex.IsSplitRefusal);
        Assert.Equal(
            new[] { BoundaryCause.AgreementCodeChange, BoundaryCause.LocalProfileActivation },
            ex.InteriorBoundaryCauses);
        Assert.Equal(3, ex.EmployedSegmentCount);
        Assert.Contains("AgreementCodeChange", ex.Message);
        Assert.Contains("LocalProfileActivation", ex.Message);
        Assert.Equal(0, await CountManifestsAsync(employeeId));
    }

    // ─── Seeding ─────────────────────────────────────────────────────────

    private async Task SeedEmployeeAsync(string employeeId, string agreementCode, string okVersion)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        // users + employee_profiles + user_agreement_codes (history-covering rows) + the organizations
        // FK parent. Employment window both-NULL (unbounded): employed all month.
        await RegressionSeed.SeedEmployeeAsync(
            conn, employeeId, OrgId, agreementCode, okVersion, partTimeFraction: 1.0m);
    }

    /// <summary>An open-ended local profile of (ORG, agreement, OK version) effective from the date.</summary>
    private async Task SeedActivationAsync(string agreementCode, string okVersion, DateOnly effectiveFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version,
                effective_from, effective_to,
                weekly_norm_hours, max_flex_balance, flex_carryover_max,
                max_overtime_hours_per_period, overtime_requires_pre_approval,
                created_by)
            VALUES (
                gen_random_uuid(), @org, @ac, @ok,
                @from, NULL,
                36, NULL, NULL, NULL, NULL,
                'admin1')
            """, conn);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>Closes the live agreement-code row at <paramref name="effectiveFrom"/> (end-exclusive)
    /// and inserts the successor — written directly so the test controls the dates.</summary>
    private async Task SupersedeAgreementCodeAsync(string employeeId, string newAgreementCode, DateOnly effectiveFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var close = new NpgsqlCommand(
            """
            UPDATE user_agreement_codes SET effective_to = @from
            WHERE user_id = @id AND effective_to IS NULL
            """, conn, tx))
        {
            close.Parameters.AddWithValue("id", employeeId);
            close.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await close.ExecuteNonQueryAsync());
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO user_agreement_codes (assignment_id, user_id, agreement_code, effective_from, effective_to, version)
            VALUES (gen_random_uuid(), @id, @code, @from, NULL, 2)
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", employeeId);
            insert.Parameters.AddWithValue("code", newAgreementCode);
            insert.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        await tx.CommitAsync();
    }

    /// <summary>The wage-type mappings the seeded harness only has for HK, for another agreement, so the
    /// per-segment (dated-record) mapping key finds its rows.</summary>
    private async Task SeedWageTypeMappingsForAsync(string agreementCode, string okVersion)
    {
        var rows = new (string TimeType, string WageType)[]
        {
            ("NORMAL_HOURS", "SLS_0110"),
            ("OVERTIME_50", "SLS_0210"),
            ("OVERTIME_100", "SLS_0220"),
            ("VACATION", "SLS_0310"),
        };
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        foreach (var (timeType, wageType) in rows)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description, effective_from)
                VALUES (@t, @w, @ok, @code, '', NULL, DATE '2020-01-01')
                ON CONFLICT (time_type, ok_version, agreement_code, position) WHERE effective_to IS NULL DO NOTHING
                """, conn);
            cmd.Parameters.AddWithValue("t", timeType);
            cmd.Parameters.AddWithValue("w", wageType);
            cmd.Parameters.AddWithValue("ok", okVersion);
            cmd.Parameters.AddWithValue("code", agreementCode);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    // ─── Reads ───────────────────────────────────────────────────────────

    private async Task<int> CountManifestsAsync(string employeeId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM segment_manifests WHERE employee_id = @e", conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private sealed record PersistedManifest(int SegmentCount, IReadOnlyList<string> BoundaryCauseSummary);

    private async Task<PersistedManifest> LoadManifestAsync(Guid manifestId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT segments_jsonb::text, boundary_cause_summary
            FROM segment_manifests
            WHERE manifest_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", manifestId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"no segment_manifests row for {manifestId}");
        using var doc = System.Text.Json.JsonDocument.Parse(reader.GetString(0));
        var count = doc.RootElement.GetArrayLength();
        var summary = ((string[])reader.GetValue(1)).ToList();
        return new PersistedManifest(count, summary);
    }

    // ─── Wiring (the production DI shapes, hand-built) ───────────────────

    /// <summary>The payroll-host PCS shape WITH the real <see cref="LocalAgreementProfileRepository"/> and
    /// the dated <see cref="EmploymentProfileResolver"/>: without the repository the activation lookup is
    /// skipped whatever the production code does, and without the resolver the body would stand in as the
    /// record, so the facts could not tell a body key from a record key.</summary>
    private PeriodCalculationService BuildPcs(
        IRecordedHoursReader hoursReader, IReadOnlyList<RuleClassification> classifications)
    {
        var httpFactory = new SingleClientFactory(new TestFixtures.StubHandler(TestFixtures.DefaultRuleEngineHandler));
        var wtmRepo = new WageTypeMappingRepository(_harness.Factory);
        var mappingService = new PayrollMappingService(
            _harness.Factory, NullLogger<PayrollMappingService>.Instance, wtmRepo);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
            })
            .Build();
        var userAgreementCodeRepo = new UserAgreementCodeRepository(_harness.Factory);

        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            _eventStore,
            _harness.Factory,
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryRuleClassificationProvider(classifications),
            localAgreementProfileRepo: new LocalAgreementProfileRepository(_harness.Factory),
            profileResolver: new EmploymentProfileResolver(_harness.Factory, userAgreementCodeRepo),
            employmentWindowResolver: new EmploymentWindowResolver(_harness.Factory),
            employeeProfileRepo: new EmployeeProfileRepository(_harness.Factory),
            userAgreementCodeRepo: userAgreementCodeRepo,
            hoursReader: hoursReader,
            // This suite is not about the prior-month check; M-1 is treated as APPROVED.
            priorPeriodApprovalReader: new TestFixtures.AlwaysApprovedPriorPeriods());
    }

    private static InMemoryRecordedHoursReader HoursOf(IEnumerable<TimeEntry> entries) => new(entries);

    private static async Task<PeriodCalculationService.PeriodCalculationOutcome> RunShimAsync(
        PeriodCalculationService pcs, EmploymentProfile body)
    {
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export and /recalculate).
        return await pcs.CalculateWithOutcomeAsync(body, Apr01, Apr30, previousFlexBalance: 0m);
#pragma warning restore CS0618
    }

    /// <summary>The request body: what a (possibly stale) caller says about the employee.</summary>
    private static EmploymentProfile Body(string employeeId, string agreementCode, string okVersion) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = agreementCode,
        OkVersion = okVersion,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = OrgId,
    };

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    /// <summary>An in-memory <see cref="IRuleClassificationProvider"/> over a fixed set (the same private
    /// twin the sibling suites carry).</summary>
    private sealed class InMemoryRuleClassificationProvider : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public InMemoryRuleClassificationProvider(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }
}
