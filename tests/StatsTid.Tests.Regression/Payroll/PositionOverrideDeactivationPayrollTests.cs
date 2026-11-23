using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Tests.Regression.Contracts;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14828-T (AC-C1k) - a position override that a database deactivation switched off before the month
/// stops applying to that month's pay.
///
/// <para>
/// <b>In plain language.</b> The seeded AC position "Kontorchef" (<c>DEPARTMENT_HEAD</c>) may keep a flex balance up
/// to 200 hours; AC's ordinary cap is 150. An administrator deactivates the override from a Monday in May. From then
/// on a Kontorchef is held to the ordinary 150, and any flex above it is paid out as a <c>FLEX_PAYOUT</c> line.
/// Before this stack payroll applied the override compiled into the code whatever the database said, so the cap stayed
/// 200 and nothing was paid out.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PositionOverrideDeactivationPayrollTests : DatedPayrollFixture
{
    private const string OrgId = "STY01";
    private const string Position = "DEPARTMENT_HEAD";
    private static readonly DateOnly Clock = new(2026, 11, 10);
    private static readonly DateOnly May01 = new(2026, 5, 1);
    private static readonly DateOnly May31 = new(2026, 5, 31);
    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);

    /// <summary>
    /// <b>Kind F (CI).</b> AC-C1k; the discriminating result is the <c>FLEX_PAYOUT</c> line (ruling 21). Two AC employees
    /// hold <c>DEPARTMENT_HEAD</c>; each has a recorded May ledger closing of <b>160.00</b> (inside (150, 200]) and
    /// works exactly the day norm on every June weekday (so June moves no flex). First employee A is calculated for June
    /// while the seeded override is in force: the cap is 200, the month closes at 160.00 and writes <b>no</b> FLEX_PAYOUT
    /// line (the control: the override really applied). The override is then deactivated from Monday 4 May 2026 through the
    /// route; employee B is calculated for June: the cap is AC's 150, so the month closes at <b>150.00</b> and exactly one
    /// <c>FLEX_PAYOUT</c> line of <b>10.00</b> h (160 - 150) is written.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker-gated; CI-pending; argued statically). On the code before the stack payroll applied the
    /// compiled <c>DEPARTMENT_HEAD</c> override whatever the database said, so B is calculated at cap 200 as A is:
    /// the closing is 160.00 and no FLEX_PAYOUT line exists, failing the 150.00 and 10.00 assertions. A test-created
    /// override is not used: the code before the stack ignored database overrides entirely, so that fact would be
    /// green there and not F.
    /// </para>
    /// </summary>
    [Fact]
    public async Task DeactivatedBeforeMonth_StopsApplying()
    {
        using var admin = AdminAt(Clock);
        const string inForce = "EMP-S148-PAY-OVR-A";
        const string deactivated = "EMP-S148-PAY-OVR-B";
        foreach (var employeeId in new[] { inForce, deactivated })
        {
            await SeedEmployeeAsync(employeeId, OrgId, "AC", position: Position);
            await FlexLedgerSeed.SeedAsync(ConnectionString, employeeId, May01, closing: 160m);
            await SeedApprovedMonthAsync(employeeId, OrgId, "AC", Ok26, May01, May31);
            await SeedWeekdayHoursAsync(employeeId, "AC", Ok26, Jun01, Jun30);
            await SeedApprovedMonthAsync(employeeId, OrgId, "AC", Ok26, Jun01, Jun30);
        }

        // Control: the seeded override applies (cap 200), so nothing is paid out.
        var before = await CalculateAndExportAsync(inForce, "AC", Ok26, OrgId, Jun01, Jun30, Position);
        AssertOk(before);
        var beforeResult = ReadResult(before.Json);
        Assert.Equal((decimal?)160.00m, beforeResult.ClosingFlexBalance);
        Assert.Empty(beforeResult.RuleResults.SelectMany(r => r.LineItems).Where(li => li.TimeType == "FLEX_PAYOUT"));

        // The deactivation, dated Monday 4 May 2026, through the real route.
        var overrideId = await SeededOverrideIdAsync(admin);
        using var get = await admin.GetAsync($"/api/admin/position-overrides/{overrideId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var version = S118ContractAssert.EtagVersion(get);
        using (var deactivate = await TransitionOverrideAsync(admin, overrideId, version, "deactivate", "2026-05-04"))
            Assert.True(deactivate.StatusCode == HttpStatusCode.OK, $"deactivate expected 200, got {(int)deactivate.StatusCode}: {await deactivate.Content.ReadAsStringAsync()}");

        var after = await CalculateAndExportAsync(deactivated, "AC", Ok26, OrgId, Jun01, Jun30, Position);

        AssertOk(after);
        var result = ReadResult(after.Json);
        var payouts = result.RuleResults.SelectMany(r => r.LineItems).Where(li => li.TimeType == "FLEX_PAYOUT").ToList();
        Assert.Equal((decimal?)150.00m, result.ClosingFlexBalance);
        Assert.Equal(new[] { 10.00m }, payouts.Select(li => li.Hours).ToArray());
    }

    private static async Task<Guid> SeededOverrideIdAsync(HttpClient admin)
    {
        using var list = await admin.GetAsync("/api/admin/position-overrides/agreement/AC/OK26");
        var text = await list.Content.ReadAsStringAsync();
        Assert.True(list.StatusCode == HttpStatusCode.OK, $"override list expected 200, got {(int)list.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.EnumerateArray()
            .Single(o => o.GetProperty("positionCode").GetString() == Position)
            .GetProperty("overrideId").GetGuid();
    }
}
