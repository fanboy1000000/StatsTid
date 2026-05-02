using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// Per-source coverage for the four boundary kinds the planner consumes (ADR-016 D5):
/// OK transition, agreement-config promotion, position-override effective date, EU WTD
/// ruleset version. For each: one valid scenario (planner accepts and produces the
/// expected segment count) and one invalid scenario (an aligned-window rule with
/// <see cref="PlannerOptions.AllowUpstreamAlignment"/> = false rejects per ADR-016 D4).
///
/// <para>
/// Eight scenarios total. The planner-only scenarios (7 of 8) do not require a database
/// or HTTP — they exercise <see cref="PeriodPlanner.Plan"/> directly. The OK-valid
/// scenario (1 of 8) is testcontainer-backed because it also exercises the full
/// <c>PeriodCalculationService</c> manifest-emission round-trip — it is the only test
/// in this file that carries <c>[Trait("Category", "Docker")]</c>.
/// </para>
///
/// <para>
/// Q7 anchor scenarios from ADR-016 D11:
/// </para>
/// <list type="bullet">
///   <item>2-segment OK transition (the OK valid case below).</item>
///   <item>3-segment OK + agreement-config simultaneous (agreement-config valid case).</item>
///   <item>4-segment synthetic position-override (position-override valid case).</item>
///   <item>EU-WTD compliance ruleset version bump (EU-WTD valid case).</item>
/// </list>
/// </summary>
public sealed class BoundaryScenarioTests
{
    // Common straddling period used across every scenario unless explicitly noted.
    private static readonly DateOnly Mar25 = new(2026, 3, 25);
    private static readonly DateOnly Apr07 = new(2026, 4, 7);

    private static RuleClassification SegmentSafeCalc(string ruleId) => new(
        ruleId, Span.Entry, SplitBehavior.SegmentSafe, Family.Calculation,
        MergeStrategy.Concatenate, SnapshotContract: null);

    private static RuleClassification AlignedWindowCalc(string ruleId) => new(
        ruleId, Span.Window, SplitBehavior.AlignedWindow, Family.Calculation,
        MergeStrategy.RejectIfMultipleSegments, SnapshotContract: null);

    // ----------------------------------------------------------------------------------
    // 2. Agreement-config promotion — VALID (planner-only): 3-segment scenario combining
    //    a 2026-04-01 OK transition with a 2026-04-04 agreement-config promotion.
    // ----------------------------------------------------------------------------------
    [Fact]
    public void AgreementConfigPromotion_Valid_Produces3Segments()
    {
        var sources = new BoundarySources(
            OkTransitions: new List<(DateOnly, string, string)>
            {
                (new DateOnly(2026, 4, 1), "OK24", "OK26")
            },
            AgreementConfigPromotions: new List<(DateOnly, string)>
            {
                (new DateOnly(2026, 4, 4), "HK")
            },
            PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
            EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
            NonDatedSourceValues: new Dictionary<string, object?>());

        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-AGREE-VALID",
            periodStart: Mar25,
            periodEnd: Apr07,
            calculationKind: "forward-calc",
            ruleSet: new[] { SegmentSafeCalc("SUPPLEMENT_CALC") },
            sources: sources,
            options: PlannerOptions.Default);

        Assert.Equal(3, plan.Segments.Count);
        Assert.Equal(BoundaryCause.OkTransition, plan.Segments[0].BoundaryCause);
        Assert.Equal(BoundaryCause.AgreementConfigPromotion, plan.Segments[2].BoundaryCause);
    }

    // ----------------------------------------------------------------------------------
    // 2b. Agreement-config promotion — INVALID: aligned-window rule + an
    //     agreement-config promotion inside the period; AllowUpstreamAlignment=false.
    // ----------------------------------------------------------------------------------
    [Fact]
    public void AgreementConfigPromotion_Invalid_AlignedWindowRejects()
    {
        var sources = new BoundarySources(
            OkTransitions: Array.Empty<(DateOnly, string, string)>(),
            AgreementConfigPromotions: new List<(DateOnly, string)>
            {
                (new DateOnly(2026, 4, 4), "HK")
            },
            PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
            EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
            NonDatedSourceValues: new Dictionary<string, object?>());

        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(
                employeeId: "EMP-AGREE-INVALID",
                periodStart: Mar25,
                periodEnd: Apr07,
                calculationKind: "forward-calc",
                ruleSet: new[] { AlignedWindowCalc("OVERTIME_CALC") },
                sources: sources,
                options: PlannerOptions.Default));

        Assert.Contains("OVERTIME_CALC", ex.Message);
        Assert.Contains("AlignedWindow", ex.Message);
    }

    // ----------------------------------------------------------------------------------
    // 3. Position-override effective date — VALID (planner-only): 4-segment scenario.
    //    Two position-override boundaries plus an OK boundary inside the period.
    // ----------------------------------------------------------------------------------
    [Fact]
    public void PositionOverrideEffective_Valid_Produces4Segments()
    {
        var sources = new BoundarySources(
            OkTransitions: new List<(DateOnly, string, string)>
            {
                (new DateOnly(2026, 4, 1), "OK24", "OK26")
            },
            AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
            PositionOverrideEffectiveDates: new List<(DateOnly, string)>
            {
                (new DateOnly(2026, 3, 28), "RESEARCHER"),
                (new DateOnly(2026, 4, 5),  "PROFESSOR"),
            },
            EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
            NonDatedSourceValues: new Dictionary<string, object?>());

        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-POS-VALID",
            periodStart: Mar25,
            periodEnd: Apr07,
            calculationKind: "forward-calc",
            ruleSet: new[] { SegmentSafeCalc("SUPPLEMENT_CALC") },
            sources: sources,
            options: PlannerOptions.Default);

        Assert.Equal(4, plan.Segments.Count);
        // Boundary causes are sorted ascending by date — first segment carries the cause
        // of the first interior boundary.
        Assert.Equal(BoundaryCause.PositionOverrideEffective, plan.Segments[0].BoundaryCause);
    }

    // ----------------------------------------------------------------------------------
    // 3b. Position-override effective date — INVALID: aligned-window rule.
    // ----------------------------------------------------------------------------------
    [Fact]
    public void PositionOverrideEffective_Invalid_AlignedWindowRejects()
    {
        var sources = new BoundarySources(
            OkTransitions: Array.Empty<(DateOnly, string, string)>(),
            AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
            PositionOverrideEffectiveDates: new List<(DateOnly, string)>
            {
                (new DateOnly(2026, 4, 4), "RESEARCHER")
            },
            EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
            NonDatedSourceValues: new Dictionary<string, object?>());

        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(
                employeeId: "EMP-POS-INVALID",
                periodStart: Mar25,
                periodEnd: Apr07,
                calculationKind: "forward-calc",
                ruleSet: new[] { AlignedWindowCalc("OVERTIME_CALC") },
                sources: sources,
                options: PlannerOptions.Default));

        Assert.Contains("OVERTIME_CALC", ex.Message);
        Assert.Contains("AlignedWindow", ex.Message);
    }

    // ----------------------------------------------------------------------------------
    // 4. EU WTD ruleset transition — VALID (planner-only): 2-segment scenario, EU WTD
    //    boundary is the only interior boundary.
    // ----------------------------------------------------------------------------------
    [Fact]
    public void EuWtdRulesetTransition_Valid_Produces2Segments()
    {
        var sources = new BoundarySources(
            OkTransitions: Array.Empty<(DateOnly, string, string)>(),
            AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
            PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
            EuWtdRulesetTransitions: new List<(DateOnly, int, int)>
            {
                (new DateOnly(2026, 4, 1), 1, 2)
            },
            NonDatedSourceValues: new Dictionary<string, object?>());

        var plan = PeriodPlanner.Plan(
            employeeId: "EMP-WTD-VALID",
            periodStart: Mar25,
            periodEnd: Apr07,
            calculationKind: "forward-calc",
            ruleSet: new[] { SegmentSafeCalc("REST_PERIOD_DAILY") },
            sources: sources,
            options: PlannerOptions.Default);

        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(BoundaryCause.EuWtdRulesetVersion, plan.Segments[0].BoundaryCause);
        Assert.Equal(BoundaryCause.EuWtdRulesetVersion, plan.Segments[1].BoundaryCause);
    }

    // ----------------------------------------------------------------------------------
    // 4b. EU WTD ruleset transition — INVALID: aligned-window rule.
    // ----------------------------------------------------------------------------------
    [Fact]
    public void EuWtdRulesetTransition_Invalid_AlignedWindowRejects()
    {
        var sources = new BoundarySources(
            OkTransitions: Array.Empty<(DateOnly, string, string)>(),
            AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
            PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
            EuWtdRulesetTransitions: new List<(DateOnly, int, int)>
            {
                (new DateOnly(2026, 4, 1), 1, 2)
            },
            NonDatedSourceValues: new Dictionary<string, object?>());

        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(
                employeeId: "EMP-WTD-INVALID",
                periodStart: Mar25,
                periodEnd: Apr07,
                calculationKind: "forward-calc",
                ruleSet: new[] { AlignedWindowCalc("OVERTIME_CALC") },
                sources: sources,
                options: PlannerOptions.Default));

        Assert.Contains("OVERTIME_CALC", ex.Message);
    }

    // ----------------------------------------------------------------------------------
    // 1b. OK transition — INVALID: aligned-window rule + OK boundary inside period.
    //     This is the only invalid case from the OK source.
    // ----------------------------------------------------------------------------------
    [Fact]
    public void OkTransition_Invalid_AlignedWindowRejects()
    {
        var ex = Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(
                employeeId: "EMP-OK-INVALID",
                periodStart: Mar25,
                periodEnd: Apr07,
                calculationKind: "forward-calc",
                ruleSet: new[] { AlignedWindowCalc("OVERTIME_CALC") },
                sources: new BoundarySources(
                    new List<(DateOnly, string, string)>
                    {
                        (new DateOnly(2026, 4, 1), "OK24", "OK26")
                    },
                    Array.Empty<(DateOnly, string)>(),
                    Array.Empty<(DateOnly, string)>(),
                    Array.Empty<(DateOnly, int, int)>(),
                    new Dictionary<string, object?>()),
                options: PlannerOptions.Default));

        Assert.Contains("OVERTIME_CALC", ex.Message);
        Assert.Contains("AlignedWindow", ex.Message);
    }

    // ----------------------------------------------------------------------------------
    // 1. OK transition — VALID (PCS-backed): 2-segment scenario with full PCS
    //    round-trip. Manifest is emitted; the boundary cause summary contains
    //    "OkTransition". This is the only Docker-gated test in this file.
    // ----------------------------------------------------------------------------------
    [Fact]
    [Trait("Category", "Docker")]
    public async Task OkTransition_Valid_PcsRoundTrip_EmitsManifestWithOkTransitionCause()
    {
        await using var fixture = await BoundaryScenarioFixture.CreateAsync();

        var profile = TestFixtures.Profile("EMP-OK-VALID-PCS");
        var entries = TestFixtures.WeekdayEntriesForPeriod(profile.EmployeeId, Mar25, Apr07);

        var plan = PeriodPlanner.Plan(
            employeeId: profile.EmployeeId,
            periodStart: Mar25,
            periodEnd: Apr07,
            calculationKind: "forward-calc",
            ruleSet: TestFixtures.RuleSet,
            sources: TestFixtures.OkStraddleSources(),
            options: PlannerOptions.Default);

        Assert.Equal(2, plan.Segments.Count);

        var result = await fixture.Pcs.CalculateAsync(
            plan, profile, entries, Array.Empty<AbsenceEntry>(), 0m);
        Assert.True(result.Success);

        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT boundary_cause_summary FROM segment_manifests WHERE manifest_id = @id", conn);
        cmd.Parameters.AddWithValue("id", plan.ManifestId);
        var causes = (string[])(await cmd.ExecuteScalarAsync())!;
        Assert.Contains("OkTransition", causes);
    }
}

/// <summary>
/// Single-use Postgres testcontainer fixture for the PCS-backed OK-valid scenario.
/// Each test creates a new instance so isolation is preserved without xUnit
/// collection fixtures.
/// </summary>
internal sealed class BoundaryScenarioFixture : IAsyncDisposable
{
    private const string ImageTag = "postgres:16-alpine";
    private const string SchemaDdl = """
        CREATE TABLE IF NOT EXISTS event_streams (
            stream_id   TEXT        PRIMARY KEY,
            created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE TABLE IF NOT EXISTS events (
            global_position BIGSERIAL   PRIMARY KEY,
            event_id        UUID        NOT NULL UNIQUE,
            stream_id       TEXT        NOT NULL REFERENCES event_streams(stream_id),
            stream_version  INT         NOT NULL,
            event_type      TEXT        NOT NULL,
            data            JSONB       NOT NULL,
            occurred_at     TIMESTAMPTZ NOT NULL,
            stored_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            actor_id        TEXT,
            actor_role      TEXT,
            correlation_id  UUID,
            UNIQUE (stream_id, stream_version)
        );
        CREATE TABLE IF NOT EXISTS segment_manifests (
            manifest_id             UUID        PRIMARY KEY,
            period_start            DATE        NOT NULL,
            period_end              DATE        NOT NULL,
            employee_id             TEXT        NOT NULL,
            calculation_kind        TEXT        NOT NULL,
            boundary_cause_summary  TEXT[]      NOT NULL,
            created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
            segments_jsonb          JSONB       NOT NULL
        );
        CREATE TABLE IF NOT EXISTS wage_type_mappings (
            time_type       TEXT        NOT NULL,
            wage_type       TEXT        NOT NULL,
            ok_version      TEXT        NOT NULL,
            agreement_code  TEXT        NOT NULL,
            position        TEXT        NOT NULL DEFAULT '',
            description     TEXT,
            created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            PRIMARY KEY (time_type, ok_version, agreement_code, position)
        );
        """;

    public PostgreSqlContainer Container { get; }
    public DbConnectionFactory Factory { get; }
    public PostgresEventStore EventStore { get; }
    public StatsTid.Integrations.Payroll.Services.PeriodCalculationService Pcs { get; }
    public string ConnectionString => Container.GetConnectionString();

    private BoundaryScenarioFixture(
        PostgreSqlContainer container,
        DbConnectionFactory factory,
        PostgresEventStore eventStore,
        StatsTid.Integrations.Payroll.Services.PeriodCalculationService pcs)
    {
        Container = container;
        Factory = factory;
        EventStore = eventStore;
        Pcs = pcs;
    }

    public static async Task<BoundaryScenarioFixture> CreateAsync()
    {
        var container = new PostgreSqlBuilder()
            .WithImage(ImageTag)
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();
        await container.StartAsync();

        await using (var conn = new NpgsqlConnection(container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(SchemaDdl, conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var factory = new DbConnectionFactory(container.GetConnectionString());
        var eventStore = new PostgresEventStore(factory);
        await TestFixtures.SeedWageTypeMappingsAsync(factory);
        var pcs = TestFixtures.BuildPcs(factory, eventStore);

        return new BoundaryScenarioFixture(container, factory, eventStore, pcs);
    }

    public async ValueTask DisposeAsync()
    {
        if (Container is not null)
            await Container.DisposeAsync();
    }
}
