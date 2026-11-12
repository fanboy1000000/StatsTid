using System.Net;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14840-T — who may clear a cell, and the employment window (addendum A1 AC-CL-11, AC-CL-12). In plain
/// terms: a removal passes every gate a save passes. HR may remove a cell for an employee in their scope, a manager
/// may not (only HR and above edit another person's registrations), a departed employee may not remove their own
/// cells, and nobody may remove a cell dated outside the employment period, where the refusal names no employment date.
///
/// <para>Docker-gated: <b>CI-pending</b>. "Base" is <c>728f4702</c>, where the <c>cleared</c> member is ignored.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SkemaCellClear")]
public sealed class SkemaCellClearAuthorityTests : SkemaCellClearTestBase
{
    public SkemaCellClearAuthorityTests(SkemaCellClearFixture fx) : base(fx) { }

    /// <summary>One employee holding a sick day on Wednesday 10 June 2026 (saved by the employee: 200).</summary>
    private async Task<string> SeedWithSickDayAsync()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Wed10, "SICK_DAY", 7.4m) }), "sick day Wednesday");
        return emp;
    }

    private static object ClearSick() => Save(clearAbsences: new[] { (Wed10, "SICK_DAY") });

    private async Task AssertNothingClearedAsync(string emp)
    {
        Assert.Null(Assert.Single(await AbsencesAsync(emp)).SupersededBy);
        Assert.Empty(await OutboxAsync(emp, "AbsenceCleared"));
        Assert.Equal(0L, await AuditCountAsync(emp, "AbsenceCleared"));
    }

    /// <summary>
    /// AC-CL-11. HR clearing a date after a leaver's end date (Monday 8 June; the date, Wednesday 10 June, lies inside the
    /// requested month) gets the shared date-free 422: <c>outside_employment_period</c>, <c>kind employment-window</c>,
    /// and no digit of any date in the body. Nothing changes: the sick day stands and no clear event exists.
    ///
    /// <para>Kind F. Red conditions: on base the cleared member is ignored, so HR's save answers 200 and the status
    /// assertion trips.</para>
    /// </summary>
    [Fact]
    public async Task ClearOutsideTheEmploymentWindow_422_DateFree()
    {
        var emp = await SeedWithSickDayAsync();
        await SetEmploymentWindowAsync(emp, null, Mon8);

        var body = await RefusalAsync(
            await PostSaveAsync(ClientFor("hr_s148_cl", StatsTidRoles.LocalHR, Org), emp, ClearSick()),
            HttpStatusCode.UnprocessableEntity);

        Assert.Equal("outside_employment_period", body.GetProperty("error").GetString());
        Assert.Equal("employment-window", body.GetProperty("kind").GetString());
        Assert.DoesNotContain("2026", body.GetRawText());
        await AssertNothingClearedAsync(emp);
    }

    /// <summary>
    /// AC-CL-12 (1). LocalHR in scope clears an employee's cell: 200, and the event's <c>actorId</c> is HR's (and the
    /// role LocalHR), not the employee's.
    ///
    /// <para>Kind F. Red conditions: on base nothing is cleared, so <c>Assert.Single</c> on the <c>AbsenceCleared</c>
    /// event trips.</para>
    /// </summary>
    [Fact]
    public async Task HrClearsForEmployee_200_ActorIsHr()
    {
        var emp = await SeedWithSickDayAsync();

        await AssertStatusAsync(
            await PostSaveAsync(ClientFor("hr_s148_cl", StatsTidRoles.LocalHR, Org), emp, ClearSick()), HttpStatusCode.OK);

        var clear = Assert.Single(await OutboxAsync(emp, "AbsenceCleared"));
        Assert.Equal("hr_s148_cl", clear.ActorId);
        Assert.Equal(StatsTidRoles.LocalHR, clear.ActorRole);
        Assert.Equal(clear.OutboxId, Assert.Single(await AbsencesAsync(emp)).SupersededBy);
    }

    /// <summary>
    /// AC-CL-12 (2). A LocalLeader clearing a report's cell gets 403: a manager can never edit an employee's
    /// registrations. Nothing changes.
    ///
    /// <para>Kind R. Red conditions: none on base; fails if a leader's scope carries a clear (200, the row retired).</para>
    /// </summary>
    [Fact]
    public async Task LeaderClearsReportCell_403()
    {
        var emp = await SeedWithSickDayAsync();

        await AssertStatusAsync(
            await PostSaveAsync(ClientFor("lead_s148_cl", StatsTidRoles.LocalLeader, Org), emp, ClearSick()), HttpStatusCode.Forbidden);

        await AssertNothingClearedAsync(emp);
    }

    /// <summary>
    /// AC-CL-12 (3). A departed employee (last day employed Wednesday 20 May 2026, before the pinned today) clearing their
    /// OWN cell gets 403 with the departed-subject reason (<see cref="DepartedSubject.DenyReason"/>). Nothing changes.
    ///
    /// <para>Kind R. Red conditions: none on base; fails if a departed self may still clear (200).</para>
    /// </summary>
    [Fact]
    public async Task DepartedSelfClear_403()
    {
        var emp = await SeedWithSickDayAsync();
        await SetEmploymentWindowAsync(emp, null, new DateOnly(2026, 5, 20));

        var body = await RefusalAsync(await PostSaveAsync(EmployeeClient(emp), emp, ClearSick()), HttpStatusCode.Forbidden);

        Assert.Equal(DepartedSubject.DenyReason, body.GetProperty("reason").GetString());
        await AssertNothingClearedAsync(emp);
    }

    /// <summary>
    /// AC-CL-12 (4). An employee clearing another employee's cell gets 403 ("Employee can only save own data").
    /// Nothing changes.
    ///
    /// <para>Kind R. Red conditions: none on base; fails if the own-data rule does not cover a clear.</para>
    /// </summary>
    [Fact]
    public async Task EmployeeClearsAnothersCell_403()
    {
        var emp = await SeedWithSickDayAsync();
        var stranger = await SeedEmployeeAsync();

        await AssertStatusAsync(await PostSaveAsync(EmployeeClient(stranger), emp, ClearSick()), HttpStatusCode.Forbidden);

        await AssertNothingClearedAsync(emp);
    }

    /// <summary>
    /// AC-CL-12 (5). A LocalHR user whose scope does not cover the employee (scope STY02; the employee is on STY01) gets
    /// 403 from the terminated-inclusive scope validator. Nothing changes.
    ///
    /// <para>Kind R. Red conditions: none on base; fails if the scope validator is skipped for a clear.</para>
    /// </summary>
    [Fact]
    public async Task HrOutOfScopeClears_403()
    {
        var emp = await SeedWithSickDayAsync();

        await AssertStatusAsync(
            await PostSaveAsync(ClientFor("hr_other_s148_cl", StatsTidRoles.LocalHR, LeaderOrg), emp, ClearSick()),
            HttpStatusCode.Forbidden);

        await AssertNothingClearedAsync(emp);
    }
}
