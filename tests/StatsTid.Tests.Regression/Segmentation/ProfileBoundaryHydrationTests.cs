using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// D11 fixture #14 — exercises
/// <c>PeriodCalculationService.BuildPlanForLegacyCallersAsync</c>'s S21 hydration
/// of <see cref="BoundarySources.LocalProfileActivations"/> from the
/// <c>local_agreement_profiles</c> table (ADR-017 D9c, TASK-2108).
///
/// <para>
/// The hydration shim is private; we invoke it via reflection because it is the
/// production seam between the repository (DB) and the planner (pure-data). Going
/// through the public <c>CalculateAsync</c> shim instead would also exercise the
/// rule-engine HTTP path + manifest emission, which are out of scope for this
/// fixture and would require stubbing.
/// </para>
///
/// <para>Variants:</para>
/// <list type="bullet">
///   <item>Profile with <c>effective_from = 2026-04-15</c> + period <c>2026-04-01 .. 2026-04-30</c>
///         → plan has a 2-segment break at 2026-04-15 with cause
///         <see cref="BoundaryCause.LocalProfileActivation"/>.</item>
///   <item>Same period but <c>EmploymentProfile.OrgId == null</c> →
///         <c>LocalProfileActivations</c> is hydrated as empty (or null) and the plan is
///         single-segment per the D9c profile-less-callers contract.</item>
/// </list>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileBoundaryHydrationTests : IAsyncLifetime
{
    private const string OrgId = "STY02";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await Config.ProfileTestSchema.ApplyAsync(_harness.ConnectionString);
        await Config.ProfileTestSchema.SeedOrganizationAsync(_harness.ConnectionString, OrgId);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ProfileEffectiveFromInsidePeriod_ProducesBoundary()
    {
        // Seed a single open-ended profile with effective_from = 2026-04-15.
        var profileId = await SeedProfileAsync(new DateOnly(2026, 4, 15));

        // -- Variant A: orgId populated → boundary expected --
        var employmentProfile = new EmploymentProfile
        {
            EmployeeId = "EMP-2110",
            AgreementCode = AgreementCode,
            OkVersion = OkVersion,
            EmploymentCategory = "Standard",
            OrgId = OrgId,
        };

        var pcs = BuildPcs();
        var plan = await InvokeBuildPlanAsync(
            pcs, employmentProfile,
            periodStart: new DateOnly(2026, 4, 1),
            periodEnd: new DateOnly(2026, 4, 30));

        // Expectation: 2 segments — one ending 2026-04-14, the other starting 2026-04-15
        // with cause LocalProfileActivation. The first segment's cause defaults to
        // OkTransition per the planner's convention (BoundaryCause names "the cause that
        // INTRODUCED this segment by displacing its predecessor").
        Assert.Equal(2, plan.Segments.Count);
        Assert.Equal(new DateOnly(2026, 4, 1), plan.Segments[0].StartDate);
        Assert.Equal(new DateOnly(2026, 4, 14), plan.Segments[0].EndDate);
        Assert.Equal(new DateOnly(2026, 4, 15), plan.Segments[1].StartDate);
        Assert.Equal(new DateOnly(2026, 4, 30), plan.Segments[1].EndDate);
        Assert.Equal(BoundaryCause.LocalProfileActivation, plan.Segments[1].BoundaryCause);

        // -- Variant B: orgId == null → empty LocalProfileActivations, single-segment plan --
        var profileLessCaller = new EmploymentProfile
        {
            EmployeeId = "EMP-2110",
            AgreementCode = AgreementCode,
            OkVersion = OkVersion,
            EmploymentCategory = "Standard",
            OrgId = null,
        };

        var planNoOrg = await InvokeBuildPlanAsync(
            pcs, profileLessCaller,
            periodStart: new DateOnly(2026, 4, 1),
            periodEnd: new DateOnly(2026, 4, 30));

        // No boundary sources → single-segment plan covering the whole period.
        Assert.Single(planNoOrg.Segments);
        Assert.Equal(new DateOnly(2026, 4, 1), planNoOrg.Segments[0].StartDate);
        Assert.Equal(new DateOnly(2026, 4, 30), planNoOrg.Segments[0].EndDate);

        // The seeded profile id is unused by the assertions but kept here so a future
        // schema-drift failure (e.g., column rename) surfaces with a clear seed step.
        Assert.NotEqual(Guid.Empty, profileId);
    }

    private async Task<Guid> SeedProfileAsync(DateOnly effectiveFrom)
    {
        var profileId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
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
                @id, @org, @ac, @ok,
                @from, NULL,
                36, NULL, NULL, NULL, NULL,
                'admin1')
            """, conn);
        cmd.Parameters.AddWithValue("id", profileId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        await cmd.ExecuteNonQueryAsync();
        return profileId;
    }

    private PeriodCalculationService BuildPcs()
    {
        // Mirror TestFixtures.BuildPcs but with the LocalAgreementProfileRepository wired
        // in (D9c hydration depends on the repo being non-null).
        var mappingService = new PayrollMappingService(_harness.Factory, NullLogger<PayrollMappingService>.Instance);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
            })
            .Build();
        var profileRepo = new LocalAgreementProfileRepository(_harness.Factory);

        // Stubbed HTTP factory — never actually called by BuildPlanForLegacyCallersAsync,
        // but PeriodCalculationService's constructor requires non-null. Re-using the
        // segmentation TestFixtures default handler keeps shape identical.
        var stubHandler = new TestFixtures.StubHandler(TestFixtures.DefaultRuleEngineHandler);
        var httpFactory = new SingleClientHttpFactory(stubHandler);

        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            _harness.EventStore,
            _harness.Factory,
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: null,
            localAgreementProfileRepo: profileRepo);
    }

    private static async Task<PlannedCalculation> InvokeBuildPlanAsync(
        PeriodCalculationService pcs,
        EmploymentProfile profile,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        var method = typeof(PeriodCalculationService).GetMethod(
            "BuildPlanForLegacyCallersAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = (Task<PlannedCalculation>)method!.Invoke(
            pcs, new object[] { profile, periodStart, periodEnd, CancellationToken.None })!;
        return await task;
    }

    private sealed class SingleClientHttpFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientHttpFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
