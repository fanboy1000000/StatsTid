using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// S148 / TASK-14818-T — the payroll planner sees the dated agreement-configuration and position-override
/// versions (QUAL-182, ADR-043 D12): a month in which a new version starts is split on that day, and a version
/// dated the very day an employee changes agreement or position leaves that change as the cause.
///
/// <para>
/// <b>In plain language.</b> A GlobalAdmin can publish a configuration "from Monday 16 March". Payroll plans a
/// month in parts so that each part can be paid under the version that applied to it; this teaches the planner
/// where a configuration version starts. When the same day is also the day the employee's own agreement or
/// position changes, the record keeps naming that change, not the configuration, because the version there is
/// the new part's starting state, not an extra boundary.
/// </para>
///
/// <para>
/// <b>Reached directly (DV-8).</b> The payroll host registers the two dated repositories only at TASK-14825,
/// so these facts build <see cref="PeriodCalculationService"/> with TASK-14818's two new optional dependencies
/// and invoke its private <c>BuildPlanWithHistoriesAsync</c> by reflection (the sibling
/// <c>ProfileBoundaryHydrationTests</c>). Planning reads only: QUAL-182's no-write rule is asserted inside both
/// facts as the row counts of six tables, before and after each planning call (B14). Docker-gated:
/// CI-pending until CI-W5a (both facts were CI-verified at CI-W4a and CI-M4a/b on the previous build; the service
/// construction changed since); run green once on a local scratch cluster (not CI evidence).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class DatedConfigBoundaryTests : IAsyncLifetime
{
    private const string OrgId = "STY_S148_PLAN";
    private const string Ok24 = "OK24";

    // March 2026 sits wholly on the OK24 side of the OK24 to OK26 transition (2026-04-01); 16 March is a Monday.
    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar15 = new(2026, 3, 15);
    private static readonly DateOnly Mar16 = new(2026, 3, 16);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    private static readonly string[] CountedTables =
    {
        "agreement_configs", "agreement_config_audit", "position_override_configs",
        "position_override_config_versions", "outbox_events", "hr_backdate_worklist",
    };

    private TestFixtures.DockerHarness _harness = null!;
    private AgreementConfigRepository _configRepo = null!;
    private PositionOverrideRepository _overrideRepo = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _configRepo = new AgreementConfigRepository(_harness.Factory);
        _overrideRepo = new PositionOverrideRepository(_harness.Factory);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// AC-C2a, the split half. (a) An HK / OK24 employee, and a published HK / OK24 version dated Monday 16
    /// March 2026 (the same weekly norm as the seed): the March 2026 plan has exactly two segments, 1–15 and
    /// 16–31 March, cause <see cref="BoundaryCause.AgreementConfigPromotion"/> (FAIL-007: in a two-segment plan
    /// both segments carry it). (b) A second employee, position P2, with an override for (AC, OK24, P2) created
    /// dated Monday 16 March: two segments at the 16th, cause <see cref="BoundaryCause.PositionOverrideEffective"/>.
    /// QUAL-182's no-write rule: the row counts of <c>agreement_configs</c>, <c>agreement_config_audit</c>,
    /// <c>position_override_configs</c>, <c>position_override_config_versions</c>, <c>outbox_events</c> and
    /// <c>hr_backdate_worklist</c> are equal before and after each planning call.
    ///
    /// <para>
    /// Arm (b) uses agreement AC, not HK as the brief's example: arm (a)'s HK / OK24 version is dated the same
    /// 16 March, and in one database it would also split arm (b)'s HK employee with the configuration cause,
    /// which outranks the override's. A different agreement keeps the two arms independent (declared).
    /// </para>
    ///
    /// <para>
    /// <b>Kind: N (CI; M-39)</b>: it names the two new optional constructor dependencies of
    /// <see cref="PeriodCalculationService"/>. <b>Red conditions</b> (M-39, the hydration skipped: both planner
    /// sources stay empty): each arm plans one segment, the count assertion expects two. The shared-date fact is
    /// green under M-39 (no configuration date reaches the planner, so the change's own cause stands), which is
    /// why M-39 and M-40 run apart. The no-write assertions are discriminating: a hydration that wrote, or
    /// landed a hand-off at calculation time, moves a count.
    /// </para>
    /// </summary>
    [Fact]
    public async Task VersionFromMondayInsideMonth_SplitsTheMonth()
    {
        // Arm (a): a published HK / OK24 version dated Monday 16 March 2026.
        const string hkEmployee = "EMP-S148-SPLIT-HK";
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, hkEmployee, OrgId, "HK", Ok24, position: null);
        await PublishVersionAsync("HK", Mar16);

        var before = await CountsAsync();
        var plan = await PlanAsync(Profile(hkEmployee, "HK", position: null), Mar01, Mar31);
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal((Mar01, Mar15), (plan.Segments[0].StartDate, plan.Segments[0].EndDate));
        Assert.Equal((Mar16, Mar31), (plan.Segments[1].StartDate, plan.Segments[1].EndDate));
        Assert.Equal(BoundaryCause.AgreementConfigPromotion, plan.Segments[0].BoundaryCause);
        Assert.Equal(BoundaryCause.AgreementConfigPromotion, plan.Segments[1].BoundaryCause);

        // Arm (b): an override for (AC, OK24, P2) created dated Monday 16 March 2026.
        const string acEmployee = "EMP-S148-SPLIT-AC";
        var position = await SeedPositionAsync("P2_SPLIT", "AC");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, acEmployee, OrgId, "AC", Ok24, position: position);
        await CreateOverrideAsync("AC", position, Mar16);

        before = await CountsAsync();
        plan = await PlanAsync(Profile(acEmployee, "AC", position), Mar01, Mar31);
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal((Mar01, Mar15), (plan.Segments[0].StartDate, plan.Segments[0].EndDate));
        Assert.Equal((Mar16, Mar31), (plan.Segments[1].StartDate, plan.Segments[1].EndDate));
        Assert.Equal(BoundaryCause.PositionOverrideEffective, plan.Segments[0].BoundaryCause);
        Assert.Equal(BoundaryCause.PositionOverrideEffective, plan.Segments[1].BoundaryCause);
    }

    /// <summary>
    /// AC-C2a, the shared-date fact (B6). Arm 1: an employee whose agreement changes HK to AC on Monday 16 March
    /// 2026, with an AC / OK24 version published dated that same day: the March plan splits at the 16th and the
    /// cause there is <see cref="BoundaryCause.AgreementCodeChange"/>, not
    /// <see cref="BoundaryCause.AgreementConfigPromotion"/> (the version is the new run's starting state).
    /// Arm 2: an employee whose position changes P1 to P2 that day (a profile row from Monday 16 March), with an
    /// override for P2 dated that day: the split carries <see cref="BoundaryCause.EmployeeProfileChange"/>, not
    /// <see cref="BoundaryCause.PositionOverrideEffective"/>. The same no-write assertions as the split fact.
    /// The fact publishes its own AC / OK24 version and its own override (no seeded configuration is read), so
    /// M-10 cannot redden it.
    ///
    /// <para>
    /// <b>Kind: N (CI; M-40)</b>. <b>Red conditions</b> (M-40, the run's lower bound made inclusive): the
    /// version dated the run's first day becomes a boundary, and the configuration cause outranks the change's
    /// (<c>BoundaryDetector</c>'s order), so arm 1 reads <c>AgreementConfigPromotion</c> and arm 2
    /// <c>PositionOverrideEffective</c>: each cause assertion trips. Green under M-39 (the hydration skipped
    /// adds no boundary), so the two mutations run apart.
    /// </para>
    /// </summary>
    [Fact]
    public async Task VersionDatedOnAKeyChange_KeepsTheKeyChangesCause()
    {
        // Arm 1: HK to AC on Monday 16 March; an AC / OK24 version dated the same day.
        const string agreementChanger = "EMP-S148-SHARED-AGR";
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, agreementChanger, OrgId, "HK", Ok24, position: null);
        await SupersedeAgreementCodeAsync(agreementChanger, "AC", Mar16);
        await PublishVersionAsync("AC", Mar16);

        var before = await CountsAsync();
        var plan = await PlanAsync(Profile(agreementChanger, "HK", position: null), Mar01, Mar31);
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(Mar16, plan.Segments[1].StartDate);
        Assert.Equal(BoundaryCause.AgreementCodeChange, plan.Segments[1].BoundaryCause);

        // Arm 2: position P1 to P2 on Monday 16 March; an override for P2 dated the same day.
        const string positionChanger = "EMP-S148-SHARED-POS";
        var p1 = await SeedPositionAsync("P1_SHARED", "HK");
        var p2 = await SeedPositionAsync("P2_SHARED", "HK");
        await RegressionSeed.SeedEmployeeAsync(
            _harness.ConnectionString, positionChanger, OrgId, "HK", Ok24, position: p1);
        await SupersedePositionAsync(positionChanger, p2, Mar16);
        await CreateOverrideAsync("HK", p2, Mar16);

        before = await CountsAsync();
        plan = await PlanAsync(Profile(positionChanger, "HK", p1), Mar01, Mar31);
        Assert.Equal(before, await CountsAsync());
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(Mar16, plan.Segments[1].StartDate);
        Assert.Equal(BoundaryCause.EmployeeProfileChange, plan.Segments[1].BoundaryCause);
    }

    // ── Planner harness ───────────────────────────────────────────────────────────────────────

    private PeriodCalculationService BuildPcs()
    {
        var mappingService = new PayrollMappingService(_harness.Factory, NullLogger<PayrollMappingService>.Instance);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceUrls:RuleEngine"] = "http://rule-engine.test" })
            .Build();
        var userAgreementCodeRepo = new UserAgreementCodeRepository(_harness.Factory);
        var httpFactory = new SingleClientHttpFactory(new TestFixtures.StubHandler(TestFixtures.DefaultRuleEngineHandler));

        // The DI-wired, dated path (profile + window resolvers, the profile and agreement-code repositories)
        // plus TASK-14818's two optional dependencies, which are what make the hydration run (DV-8).
        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            new PostgresSegmentManifestStore(_harness.Factory, _harness.EventStore, _harness.EventStore),
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: null,
            localAgreementProfileRepo: null,
            profileResolver: new EmploymentProfileResolver(_harness.Factory, userAgreementCodeRepo),
            employmentWindowResolver: new EmploymentWindowResolver(_harness.Factory),
            employeeProfileRepo: new EmployeeProfileRepository(_harness.Factory),
            userAgreementCodeRepo: userAgreementCodeRepo,
            agreementConfigRepo: _configRepo,
            positionOverrideRepo: _overrideRepo,
            // S148 TASK-14822-T: not about transfers; a fake answering "no transfer dates" (the service refuses a plan without an organisation reader).
            organisationHistoryReader: new TestFixtures.NoTransferDates());
    }

    private async Task<PlannedCalculation> PlanAsync(EmploymentProfile profile, DateOnly periodStart, DateOnly periodEnd)
    {
        var method = typeof(PeriodCalculationService).GetMethod(
            "BuildPlanWithHistoriesAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = (Task)method!.Invoke(
            BuildPcs(), new object[] { profile, periodStart, periodEnd, CancellationToken.None })!;
        await task;
        var planAndHistories = (System.Runtime.CompilerServices.ITuple)method.ReturnType
            .GetProperty(nameof(Task<object>.Result))!.GetValue(task)!;
        return (PlannedCalculation)planAndHistories[0]!;
    }

    private static EmploymentProfile Profile(string employeeId, string agreementCode, string? position) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = agreementCode,
        OkVersion = Ok24,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        Position = position,
        OrgId = OrgId,
    };

    // ── Fixtures ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A published (agreement, OK24) version dated <paramref name="appliesFrom"/>, written through the repository's
    /// dated publish, with the seeded row's values copied (so no week-read field changes). The seed is read by
    /// status (<c>GetActiveAsync</c>), never through the in-force read, so M-10 (the seeds carry no
    /// <c>published_at</c>) cannot redden a fact that only needs the seed's values (NR-P24's condition).
    /// </summary>
    private async Task PublishVersionAsync(string agreementCode, DateOnly appliesFrom)
    {
        var seed = await _configRepo.GetActiveAsync(agreementCode, Ok24)
            ?? throw new InvalidOperationException($"{agreementCode}/{Ok24} has no seeded ACTIVE row to copy the values from");
        var draftId = await _configRepo.CreateAsync(new AgreementConfigEntity
        {
            ConfigId = Guid.Empty,
            AgreementCode = agreementCode,
            OkVersion = Ok24,
            Status = AgreementConfigStatus.DRAFT,
            WeeklyNormHours = seed.WeeklyNormHours,
            NormPeriodWeeks = seed.NormPeriodWeeks,
            NormModel = seed.NormModel,
            AnnualNormHours = seed.AnnualNormHours,
            MaxFlexBalance = seed.MaxFlexBalance,
            HasOvertime = seed.HasOvertime,
            HasMerarbejde = seed.HasMerarbejde,
            OvertimeThreshold50 = seed.OvertimeThreshold50,
            OvertimeThreshold100 = seed.OvertimeThreshold100,
            EveningSupplementEnabled = seed.EveningSupplementEnabled,
            NightSupplementEnabled = seed.NightSupplementEnabled,
            WeekendSupplementEnabled = seed.WeekendSupplementEnabled,
            HolidaySupplementEnabled = seed.HolidaySupplementEnabled,
            EveningStart = seed.EveningStart,
            EveningEnd = seed.EveningEnd,
            NightStart = seed.NightStart,
            NightEnd = seed.NightEnd,
            EveningRate = seed.EveningRate,
            NightRate = seed.NightRate,
            WeekendSaturdayRate = seed.WeekendSaturdayRate,
            WeekendSundayRate = seed.WeekendSundayRate,
            HolidayRate = seed.HolidayRate,
            OnCallDutyEnabled = seed.OnCallDutyEnabled,
            OnCallDutyRate = seed.OnCallDutyRate,
            CallInWorkEnabled = seed.CallInWorkEnabled,
            CallInMinimumHours = seed.CallInMinimumHours,
            CallInRate = seed.CallInRate,
            TravelTimeEnabled = seed.TravelTimeEnabled,
            WorkingTravelRate = seed.WorkingTravelRate,
            NonWorkingTravelRate = seed.NonWorkingTravelRate,
            MaxDailyHours = seed.MaxDailyHours,
            MinimumRestHours = seed.MinimumRestHours,
            RestPeriodDerogationAllowed = seed.RestPeriodDerogationAllowed,
            WeeklyMaxHoursReferencePeriod = seed.WeeklyMaxHoursReferencePeriod,
            VoluntaryUnsocialHoursAllowed = seed.VoluntaryUnsocialHoursAllowed,
            DefaultCompensationModel = seed.DefaultCompensationModel,
            EmployeeCompensationChoice = seed.EmployeeCompensationChoice,
            MaxOvertimeHoursPerPeriod = seed.MaxOvertimeHoursPerPeriod,
            OvertimeRequiresPreApproval = seed.OvertimeRequiresPreApproval,
            CreatedBy = "tester",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Description = "S148 planner boundary fact",
        });
        var draft = await _configRepo.GetByIdAsync(draftId) ?? throw new InvalidOperationException("draft vanished");
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await _configRepo.PublishAsync(conn, tx, draftId, draft.Version, appliesFrom, "tester");
        await tx.CommitAsync();
    }

    private async Task CreateOverrideAsync(string agreementCode, string position, DateOnly appliesFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await _overrideRepo.CreateAsync(conn, tx, new PositionOverrideConfigEntity
        {
            OverrideId = Guid.Empty,
            AgreementCode = agreementCode,
            OkVersion = Ok24,
            PositionCode = position,
            Status = "ACTIVE",
            MaxFlexBalance = 150m,
            NormPeriodWeeks = 4,
            WeeklyNormHours = null,
            CreatedBy = "tester",
            Description = "S148 planner boundary fact",
        }, appliesFrom);
        await tx.CommitAsync();
    }

    private async Task<string> SeedPositionAsync(string prefix, string agreementCode)
    {
        var code = prefix + "_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO positions (position_code, display_label, agreement_code) VALUES (@code, @code, @agreement)", conn);
        cmd.Parameters.AddWithValue("code", code);
        cmd.Parameters.AddWithValue("agreement", agreementCode);
        await cmd.ExecuteNonQueryAsync();
        return code;
    }

    /// <summary>Closes the live agreement-code row at <paramref name="effectiveFrom"/> and inserts the successor.</summary>
    private async Task SupersedeAgreementCodeAsync(string employeeId, string newAgreementCode, DateOnly effectiveFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var close = new NpgsqlCommand(
            "UPDATE user_agreement_codes SET effective_to = @from WHERE user_id = @id AND effective_to IS NULL", conn, tx))
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

    /// <summary>Closes the live profile row at <paramref name="effectiveFrom"/> and inserts the successor with a new position.</summary>
    private async Task SupersedePositionAsync(string employeeId, string newPosition, DateOnly effectiveFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var close = new NpgsqlCommand(
            "UPDATE employee_profiles SET effective_to = @from WHERE employee_id = @id AND effective_to IS NULL", conn, tx))
        {
            close.Parameters.AddWithValue("id", employeeId);
            close.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await close.ExecuteNonQueryAsync());
        }
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version, employment_category)
            VALUES (gen_random_uuid(), @id, 1.0, @position, @from, NULL, 1,
                    (SELECT u.employment_category FROM users u WHERE u.user_id = @id))
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", employeeId);
            insert.Parameters.AddWithValue("position", newPosition);
            insert.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }
        await tx.CommitAsync();
    }

    /// <summary>The row counts of the six tables QUAL-182's no-write rule covers.</summary>
#pragma warning disable CA2100 // the table names are the private CountedTables constants; no value is concatenated (QUAL-073).
    private async Task<long[]> CountsAsync()
    {
        var counts = new long[CountedTables.Length];
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        for (var i = 0; i < CountedTables.Length; i++)
        {
            await using var cmd = new NpgsqlCommand($"SELECT COUNT(*) FROM {CountedTables[i]}", conn);
            counts[i] = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
        return counts;
    }

#pragma warning restore CA2100

    private sealed class SingleClientHttpFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientHttpFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
