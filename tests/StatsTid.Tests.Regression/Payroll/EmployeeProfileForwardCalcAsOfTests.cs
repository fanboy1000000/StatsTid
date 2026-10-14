using System.Text.Json;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;
using static StatsTid.Tests.Regression.Segmentation.ReplayTestKit;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S145 / TASK-14515 (R50, refinement :401) — a FORWARD calculation resolves the employee's dated
/// profile at the segment's start.
///
/// <para>
/// <b>Plain language.</b> Since S145 a replay no longer asks the dated profile resolver anything:
/// it reads the frozen pieces. That turned the old marquee replay pins into pins of the freeze, and
/// left nobody guarding the resolver's "as of" behaviour on a FORWARD calculation (the replay
/// marquee used to catch it by accident). This fact is that guard. It is a NEW file (R50); the
/// marquee file is edited by an open-wave paired pass.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmployeeProfileForwardCalcAsOfTests : IAsyncLifetime
{
    private const string Emp = "EMP-FWD-ASOF";
    private const string OrgId = "STY01";

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        // Full init.sql: users + employee_profiles + user_agreement_codes for the real dated
        // resolver (the EmployeeProfileMarqueeTests idiom). It also holds the hours tables.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>FA (guard; no MD, N1).</b> The employee's dated profile has three rows: fraction 1.0 until
    /// 14 Aug, 0.5 from Sat 15 Aug 2026, and 0.8 from Sun 20 Sep 2026. September 2026 is calculated
    /// forward (one segment, starting Tue 1 Sep, no profile-change boundary source is wired, so one
    /// segment). Every per-segment rule call must carry the row in force at the segment start,
    /// 0.5: not the earlier row (1.0), not the row that begins later in the month (0.8).
    ///
    /// <para><b>Expected, derived from the spec.</b> ADR-023 D1 / ADR-016 D5b: the profile of a
    /// segment is the dated row in force at <c>segment.StartDate</c> (end-exclusive predicate
    /// <c>effective_from ≤ asOf &lt; effective_to</c>). 1 September falls in <c>[15 Aug, 20 Sep)</c>,
    /// so 0.5. The fact asserts only the per-segment lane's request profile (the lane that keeps
    /// <c>/api/rules/evaluate</c>, TASK-14511), so it holds on today's code and after the per-plan
    /// lane. It pins today's behaviour, since PCS already resolves at <c>segment.StartDate</c>
    /// (<c>PeriodCalculationService.cs:570</c>); it is written because no forward-calculation as-of
    /// pin exists (refinement :401).</para>
    ///
    /// <para><b>Red conditions.</b> None by mutation (a guard, no MD). It would fail if a forward
    /// calculation resolved at the period end (0.8), at the first row (1.0) or from the live row.</para>
    /// </summary>
    [Fact]
    public async Task ForwardCalc_ResolverAsOf_UsesRowInForceAtSegmentStart()
    {
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await RegressionSeed.SeedEmployeeAsync(
                conn, employeeId: Emp, orgId: OrgId, agreementCode: "AC", okVersion: "OK26",
                partTimeFraction: 1.0m);
        }
        await SupersedeAsync(0.5m, new DateOnly(2026, 8, 15));
        await SupersedeAsync(0.8m, new DateOnly(2026, 9, 20));

        var engine = new ReplayEchoRuleEngine();
        var pcs = BuildPcs(
            _harness, engine,
            approval: new FakeApprovalReader(), // S145 R26: M-1 treated as APPROVED
            hours: RealHoursReader(_harness),
            profileResolver: new EmploymentProfileResolver(_harness.Factory, new UserAgreementCodeRepository(_harness.Factory)),
            windowResolver: new EmploymentWindowResolver(_harness.Factory));

        var seed = new HoursSeeder(_harness);
        await seed.WeekdaysAsync(Emp, new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30));

        await ForwardAsync(pcs, Emp, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var bodies = engine.EvaluateBodies;
        Assert.NotEmpty(bodies);
        foreach (var body in bodies)
        {
            using var doc = JsonDocument.Parse(body);
            var fraction = doc.RootElement.GetProperty("profile").GetProperty("partTimeFraction").GetDecimal();
            Assert.Equal(0.5m, fraction);
        }
    }

    private async Task SupersedeAsync(decimal fraction, DateOnly effectiveFrom)
    {
        var repo = new EmployeeProfileRepository(_harness.Factory);
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await repo.SupersedeAndCreateAsync(
            conn, tx,
            new EmployeeProfileSupersedeRequest(Emp, fraction, Position: null, EffectiveFrom: effectiveFrom),
            expectedVersion: null);
        await tx.CommitAsync();
    }
}
