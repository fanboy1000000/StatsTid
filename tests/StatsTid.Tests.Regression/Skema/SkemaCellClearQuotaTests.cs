using System.Net;
using System.Text.Json;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14840-T — a cleared holiday day is given back (addendum A1 AC-CL-13 … -16, -36, -37). In plain terms: a
/// removed <c>VACATION</c>, special-holiday, care or senior day returns exactly the feriedage (holiday-day value) the
/// registration recorded, never a fresh calculation (ADR-032 D2); a removal is never refused for quota, even when a
/// recalculation left the balance above its cap; a day booked into a balance row that no longer exists ends at 0 and not
/// below; and a request that removes one day and books others of the same type is quota-checked on its net change.
///
/// <para>Dates: Thursday 11 and Friday 12 June 2026 (ferieår 2025 for <c>VACATION</c>, reset month September); the
/// employee is full time, so a day is 7,4 h and 1,0 feriedag. The balance row is read without its year: each fact
/// touches one year only. Docker-gated: <b>CI-pending</b>. "Base" is <c>728f4702</c>, where the <c>cleared</c> member is
/// ignored.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SkemaCellClear")]
public sealed class SkemaCellClearQuotaTests : SkemaCellClearTestBase
{
    private const string GiveBackReason = "Absence cleared via Skema save";

    public SkemaCellClearQuotaTests(SkemaCellClearFixture fx) : base(fx) { }

    /// <summary>The employee's balance events of one entitlement type, in order.</summary>
    private async Task<List<OutboxRow>> BalanceEventsAsync(string emp, string entitlementType) =>
        (await OutboxAsync(emp, "EntitlementBalanceAdjusted"))
            .Where(e => e.Payload.GetProperty("entitlementType").GetString() == entitlementType).ToList();

    private Task SetUsedAsync(string emp, string entitlementType, decimal used) => ExecAsync(
        "UPDATE entitlement_balances SET used = @u WHERE employee_id = @e AND entitlement_type = @t",
        ("u", used), ("e", emp), ("t", entitlementType));

    /// <summary>
    /// AC-CL-13 (Theory: <c>VACATION</c>, <c>SPECIAL_HOLIDAY_ALLOWANCE</c> whose entitlement type is
    /// <c>SPECIAL_HOLIDAY</c>). Two days of the type are booked (Thursday 11 and Friday 12 June, 7,4 h each, <c>used</c>
    /// 2,0); Friday's is cleared: <c>used</c> drops 1,0 to 1,0; exactly one new <c>EntitlementBalanceAdjusted</c>
    /// follows the booking's, with <c>deltaDays</c> -1,0, <c>newUsed</c> 1,0 and the reason "Absence cleared via Skema
    /// save"; the balance summary shows the day back; Friday's row is retired and Thursday's current.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so <c>used</c> stays 2,0 and the
    /// <c>Assert.Equal(1.0m, used)</c> (first assertion after the 200) trips.</para>
    /// </summary>
    [Theory]
    [InlineData("VACATION", "VACATION")]
    [InlineData("SPECIAL_HOLIDAY_ALLOWANCE", "SPECIAL_HOLIDAY")]
    public async Task ClearHolidayDay_UsedDropsByTheStoredFeriedage_OneBalanceEvent(string absenceType, string entitlementType)
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Thu11, absenceType, 7.4m), (Fri12, absenceType, 7.4m) }), "two holiday days");
        Assert.Equal(2.0m, await UsedAsync(emp, entitlementType));
        var eventsBefore = (await BalanceEventsAsync(emp, entitlementType)).Count;

        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(clearAbsences: new[] { (Fri12, absenceType) })), HttpStatusCode.OK);

        Assert.Equal(1.0m, await UsedAsync(emp, entitlementType));
        var events = await BalanceEventsAsync(emp, entitlementType);
        Assert.Equal(eventsBefore + 1, events.Count);
        var give = events[^1];
        Assert.Multiple(
            () => Assert.Equal(-1.0m, give.Payload.GetProperty("deltaDays").GetDecimal()),
            () => Assert.Equal(1.0m, give.Payload.GetProperty("newUsed").GetDecimal()),
            () => Assert.Equal(GiveBackReason, give.Payload.GetProperty("reason").GetString()));
        var summary = await GetJsonAsync(EmployeeClient(emp), $"/api/balance/{emp}/summary?year=2026&month=6");
        var row = summary.GetProperty("entitlements").EnumerateArray()
            .Single(r => r.GetProperty("type").GetString() == entitlementType);
        Assert.Equal(1.0m, row.GetProperty("used").GetDecimal());
        var rows = await AbsencesAsync(emp);
        Assert.Null(rows.Single(r => r.Date == Thu11).SupersededBy);
        Assert.NotNull(rows.Single(r => r.Date == Fri12).SupersededBy);
    }

    /// <summary>
    /// AC-CL-14. What is stored is given back, not a recomputation. A <c>VACATION</c> day (Friday 12 June, 7,4 h) is
    /// stored with feriedage 0,5 and the matching <c>used</c> 0,5 (as an ADR-032 D4 revaluation leaves them, set by
    /// fixture); its clear gives back 0,5: <c>used</c> ends at 0,0 and the event's <c>deltaDays</c> is -0,5. A
    /// recomputation (7,4 h over today's day basis, 1,0) would end at -0,5.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so <c>used</c> stays 0,5 and the final
    /// <c>Assert.Equal(0.0m, used)</c> trips (the event lookup, first, finds no give-back event).</para>
    /// </summary>
    [Fact]
    public async Task ClearGivesBackTheStoredFeriedage_NotARecomputation()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Fri12, "VACATION", 7.4m) }), "VACATION Friday");
        await ExecAsync(
            "UPDATE absences_projection SET feriedage = 0.5 WHERE employee_id = @e AND absence_type = 'VACATION'", ("e", emp));
        await SetUsedAsync(emp, "VACATION", 0.5m);

        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(clearAbsences: new[] { (Fri12, "VACATION") })), HttpStatusCode.OK);

        var give = (await BalanceEventsAsync(emp, "VACATION"))[^1];
        Assert.Equal(GiveBackReason, give.Payload.GetProperty("reason").GetString());
        Assert.Equal(-0.5m, give.Payload.GetProperty("deltaDays").GetDecimal());
        Assert.Equal(0.0m, await UsedAsync(emp, "VACATION"));
    }

    /// <summary>
    /// AC-CL-15. A clear is never refused for quota. With <c>used</c> above the cap (set to 30,0 by fixture, as a
    /// revaluation can leave it; the cap is 25,0) clearing a <c>VACATION</c> day: 200, <c>used</c> -1,0 to 29,0.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so <c>used</c> stays 30,0 and the final assertion
    /// trips (the cap guard is not reached either way).</para>
    /// </summary>
    [Fact]
    public async Task ClearVacationDay_UsedAboveCap_200_GivesBack()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Fri12, "VACATION", 7.4m) }), "VACATION Friday");
        await SetUsedAsync(emp, "VACATION", 30.0m);

        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(clearAbsences: new[] { (Fri12, "VACATION") })), HttpStatusCode.OK);

        Assert.Equal(29.0m, await UsedAsync(emp, "VACATION"));
        Assert.NotNull(Assert.Single(await AbsencesAsync(emp)).SupersededBy);
    }

    /// <summary>
    /// AC-CL-16 (Theory: <c>CARE_DAY</c>, <c>SENIOR_DAY</c>; the employee is born 1 January 1960 so a senior day is
    /// offered). A full day (7,4 h) on Thursday 11 June is cleared whole: the row is retired and 1,0 is given back
    /// (<c>used</c> 1,0 to 0,0). A posted partial still answers the full-day 422
    /// (<c>SkemaCellSupersessionTests.Resave_FullDayOnlyAbsence_PositivePartialHours_Keeps422</c>, kept).
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so the row stays current and <c>used</c> stays 1,0:
    /// the <c>Assert.Equal(0.0m, used)</c> trips.</para>
    /// </summary>
    [Theory]
    [InlineData("CARE_DAY")]
    [InlineData("SENIOR_DAY")]
    public async Task ClearFullDayOnly_RemovedWhole_OneDayBack(string absenceType)
    {
        var emp = await SeedEmployeeAsync(birthDate: new DateOnly(1960, 1, 1));
        await SaveOkAsync(emp, Save(absences: new[] { (Thu11, absenceType, 7.4m) }), $"{absenceType} full day");
        Assert.Equal(1.0m, await UsedAsync(emp, absenceType));

        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(clearAbsences: new[] { (Thu11, absenceType) })), HttpStatusCode.OK);

        Assert.Equal(0.0m, await UsedAsync(emp, absenceType));
        Assert.NotNull(Assert.Single(await AbsencesAsync(emp)).SupersededBy);
    }

    /// <summary>
    /// AC-CL-36. Clearing the only booked day when its balance row is missing ends at 0 and not below. <c>VACATION</c> 7,4
    /// h on Friday 12 June saved; the balance row then deleted by SQL (the state QUAL-196 describes); the day cleared:
    /// 200, the row retired; the balance row exists again with <c>used</c> 0,0, not -1,0; one new
    /// <c>EntitlementBalanceAdjusted</c> with <c>deltaDays</c> -1,0 and <c>newUsed</c> 0,0.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so the row stays current and no balance row is
    /// recreated: the <c>Assert.Single</c> on the balance rows trips.</para>
    /// </summary>
    [Fact]
    public async Task ClearTheOnlyBookedDay_BalanceRowMissing_UsedEndsAtZero()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Fri12, "VACATION", 7.4m) }), "VACATION Friday");
        await ExecAsync("DELETE FROM entitlement_balances WHERE employee_id = @e AND entitlement_type = 'VACATION'", ("e", emp));
        var eventsBefore = (await BalanceEventsAsync(emp, "VACATION")).Count;

        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(clearAbsences: new[] { (Fri12, "VACATION") })), HttpStatusCode.OK);

        Assert.Equal(0.0m, await UsedAsync(emp, "VACATION"));
        Assert.NotNull(Assert.Single(await AbsencesAsync(emp)).SupersededBy);
        var events = await BalanceEventsAsync(emp, "VACATION");
        Assert.Equal(eventsBefore + 1, events.Count);
        Assert.Equal(-1.0m, events[^1].Payload.GetProperty("deltaDays").GetDecimal());
        Assert.Equal(0.0m, events[^1].Payload.GetProperty("newUsed").GetDecimal());
    }

    /// <summary>
    /// AC-CL-37. A mixed request is quota-checked on its net change. An HK employee employed since before 1 September
    /// 2025 (<c>VACATION</c> 2025 cap 25,0) has <c>VACATION</c> 7,4 h saved on Friday 12 June, and the 2025 <c>used</c>
    /// is then set to 24,0 by SQL. (1) One request clearing Friday 12 June and posting <c>VACATION</c> 7,4 on Wednesday
    /// 10 and Thursday 11 June, a net of +1,0: 200; <c>used</c> 25,0; one new <c>EntitlementBalanceAdjusted</c> with
    /// <c>deltaDays</c> +1,0. (2) Then one request clearing Wednesday 10 June and posting <c>VACATION</c> 7,4 on Monday 8
    /// and Tuesday 9 June, a net of +1,0 over the cap: a quota 422 and nothing written (Wednesday still current, no row
    /// on Monday or Tuesday, <c>used</c> 25,0, no clear event for Wednesday).
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so (1)'s two posted days meet one remaining day
    /// (24,0 + 2,0 over 25,0) and answer 422; the first status assertion trips.</para>
    /// </summary>
    [Fact]
    public async Task ClearAndPostSameType_QuotaCheckedOnTheNet()
    {
        var emp = await SeedEmployeeAsync("HK", employmentStart: new DateOnly(2024, 1, 1));
        await SaveOkAsync(emp, Save(absences: new[] { (Fri12, "VACATION", 7.4m) }), "VACATION Friday", EmployeeClient(emp, "HK"));
        await SetUsedAsync(emp, "VACATION", 24.0m);
        var eventsBefore = (await BalanceEventsAsync(emp, "VACATION")).Count;
        var client = EmployeeClient(emp, "HK");

        await AssertStatusAsync(await PostSaveAsync(client, emp, Save(
            absences: new[] { (Wed10, "VACATION", 7.4m), (Thu11, "VACATION", 7.4m) },
            clearAbsences: new[] { (Fri12, "VACATION") })), HttpStatusCode.OK, "(1) the net +1,0 within the cap");

        Assert.Equal(25.0m, await UsedAsync(emp, "VACATION"));
        var events = await BalanceEventsAsync(emp, "VACATION");
        Assert.Equal(eventsBefore + 1, events.Count);
        Assert.Equal(1.0m, events[^1].Payload.GetProperty("deltaDays").GetDecimal());
        var clearsBefore = (await OutboxAsync(emp, "AbsenceCleared")).Count;

        var rsp = await PostSaveAsync(client, emp, Save(
            absences: new[] { (Mon8, "VACATION", 7.4m), (Tue9, "VACATION", 7.4m) },
            clearAbsences: new[] { (Wed10, "VACATION") }));

        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.True(rsp.StatusCode == HttpStatusCode.UnprocessableEntity, $"(2) expected the quota 422, got {(int)rsp.StatusCode}: {raw}");
        Assert.Contains("quota", raw, StringComparison.OrdinalIgnoreCase);
        var rows = await AbsencesAsync(emp);
        Assert.Null(rows.Single(r => r.Date == Wed10).SupersededBy);
        Assert.DoesNotContain(rows, r => r.Date == Mon8 || r.Date == Tue9);
        Assert.Equal(25.0m, await UsedAsync(emp, "VACATION"));
        Assert.Equal(clearsBefore, (await OutboxAsync(emp, "AbsenceCleared")).Count);
        Assert.Equal(events.Count, (await BalanceEventsAsync(emp, "VACATION")).Count);
    }
}
