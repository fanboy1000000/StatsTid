namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14811-T fix-up cycle 2 (QUAL-236; owner ruling Q5, 2026-11-12, "build both rules") — two rules of one
/// child's illness, judged beside the day-2/day-3 chain (order: chain, rule A, rule B; advisory and in-lock).
///
/// <para>
/// <b>Rule A, where an illness starts.</b> In plain terms: a day directly after a child's sick day belongs to the same
/// illness, so it cannot start a new one. <c>CHILD_SICK_DAY</c> (the 1st sick day) is refused when the calendar day before
/// holds a 1st, 2nd or 3rd sick day; the call-home day is refused when the day before holds any of the four child codes.
/// A 1st sick day straight after a call-home day is fine: that is how an illness goes on. A row posted BEFORE a stored
/// day 1 is judged too (the window is each posted date plus the two days after it) and the refusal names the stored date.
/// <c>kind = child-sick-illness-start</c>.
/// </para>
///
/// <para>
/// <b>Rule B, the call-home day needs work.</b> The call-home day is the day the parent had come to work and was called
/// home, so a call-home row needs worked hours above 0 the same day. Worked is the paid-absence bound's own figure,
/// max(project hours, interval hours), and is judged on every date the save touches whose resulting state holds a
/// call-home row. <c>kind = child-sick-call-home-without-work</c>.
/// </para>
///
/// <para>
/// Expected values come from the owner's ruling and the circular's wording (021-26 / 020-26 § 14), never from the code.
/// Every refusal asserts 422, <c>error = absence_type_not_eligible</c>, the <c>kind</c>, the refused code, the dependent
/// date, the exact Danish message, and that nothing the refused save posted was written. Opted-in full-time HK employees
/// (OK26), weekdays in June 2026: Monday 8, Tuesday 9 and Wednesday 10 June (each checked against the calendar;
/// <c>init.sql</c> seeds no public holiday on them). Docker-gated, <b>CI-pending</b>; never run locally. "Red on the
/// base" below means on <c>ba247730</c>, the tip before the two rules.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ChildSickIllnessStartTests : IAsyncLifetime
{
    private const string Day1 = "CHILD_SICK_DAY";
    private const string CallHome = "CHILD_SICK_CALL_HOME";
    private const string StartKind = "child-sick-illness-start";
    private const string WithoutWorkKind = "child-sick-call-home-without-work";

    private static readonly DateOnly June = new(2026, 6, 1);
    private static readonly DateOnly Monday = new(2026, 6, 8);
    private static readonly DateOnly Tuesday = new(2026, 6, 9);
    private static readonly DateOnly Wednesday = new(2026, 6, 10);

    private static readonly (string Start, string End)[] Morning3Hours = { ("08:00", "11:00") }; // 3,00 h
    private static readonly (string Start, string End)[] Morning3Point9 = { ("08:00", "11:54") }; // 3,90 h

    private ChildSickHost _host = null!;

    public async Task InitializeAsync() => _host = await ChildSickHost.StartAsync();

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    private static string DayOneMessage(string code, DateOnly day) =>
        $"{(code == Day1 ? "Barns 1. sygedag" : "Hjemkaldelsesdagen")} den {day:dd-MM-yyyy} kan ikke registreres: dagen før ({day.AddDays(-1):dd-MM-yyyy}) er registreret som barns sygdom, så dagen hører til samme sygdomsforløb.";

    private static string WithoutWorkMessage(DateOnly day) =>
        $"Hjemkaldelsesdagen den {day:dd-MM-yyyy} kræver registreret arbejde samme dag: det er dagen, hvor barnet blev sygt, efter at du var mødt på arbejde.";

    // ── Rule A ──

    /// <summary>
    /// Rule A (1). <c>CHILD_SICK_DAY</c> on Monday 8 and Tuesday 9 June in ONE save: Tuesday directly follows a 1st sick day,
    /// so it is refused, <c>kind = child-sick-illness-start</c>, naming Tuesday 9 June. Neither row is written.
    ///
    /// <para><b>Kind F.</b> Red on the base: the save answers 200 (only day 2 and 3 are judged) and both rows exist.</para>
    /// </summary>
    [Fact]
    public async Task ConsecutiveDayOneRows_SecondRefused()
    {
        var emp = await _host.SeedEmployeeAsync("HK");

        var rsp = await _host.SaveAsync(emp, "HK", June, (Day1, 7.4m, Monday), (Day1, 7.4m, Tuesday));

        await ChildSickHost.AssertRefusalAsync(rsp, StartKind, Day1, Tuesday, DayOneMessage(Day1, Tuesday));
        Assert.Empty(await _host.ReadAbsencesAsync(emp));
    }

    /// <summary>
    /// Rule A (2). A call-home day on Monday 8 June (4,40 h, with 08:00-11:00 worked that day), then a 1st sick day on
    /// Tuesday 9 June in a second save: accepted, both rows stored. Day 1 directly after a call-home day is how an illness
    /// goes on.
    ///
    /// <para><b>Kind R</b> (a regression pin: it answers 200 on the base too, where neither rule exists). Its discriminating
    /// power is against a rule A that over-reaches and refuses a day 1 after a call-home day. Red conditions: the 200
    /// assertions and the stored rows.</para>
    /// </summary>
    [Fact]
    public async Task CallHomeThenDayOne_Accepted()
    {
        var emp = await _host.SeedEmployeeAsync("HK");

        await ChildSickHost.AssertOkAsync(await _host.SaveRawAsync(emp, "HK", June,
            absences: new[] { (CallHome, 4.4m, Monday) },
            workTime: new[] { (Monday, Morning3Hours) }));
        await ChildSickHost.AssertOkAsync(await _host.SaveAsync(emp, "HK", June, (Day1, 7.4m, Tuesday)));

        Assert.Equal(new[] { (Monday, CallHome, 4.4m), (Tuesday, Day1, 7.4m) }, await _host.ReadAbsencesAsync(emp));
    }

    /// <summary>
    /// Rule A (3). A 1st sick day on Monday 8 June, then a call-home day on Tuesday 9 June (3,50 h WITH 08:00-11:54 worked that
    /// day, so rule B is satisfied and only rule A can refuse), in one save: refused,
    /// <c>kind = child-sick-illness-start</c>, <c>absenceType = CHILD_SICK_CALL_HOME</c>, naming Tuesday 9 June. Nothing,
    /// neither absence nor work-time day, is written.
    ///
    /// <para><b>Kind F.</b> Red on the base: the save answers 200.</para>
    /// </summary>
    [Fact]
    public async Task DayOneThenCallHome_CallHomeRefused()
    {
        var emp = await _host.SeedEmployeeAsync("HK");

        var rsp = await _host.SaveRawAsync(emp, "HK", June,
            absences: new[] { (Day1, 7.4m, Monday), (CallHome, 3.5m, Tuesday) },
            workTime: new[] { (Tuesday, Morning3Point9) });

        await ChildSickHost.AssertRefusalAsync(rsp, StartKind, CallHome, Tuesday, DayOneMessage(CallHome, Tuesday));
        Assert.Empty(await _host.ReadAbsencesAsync(emp));
        Assert.Empty(await _host.ReadWorkTimeAsync(emp));
    }

    /// <summary>
    /// Rule A (4). A 1st sick day on Tuesday 9 June is saved (200). A later save of a 1st sick day on Monday 8 June is
    /// refused naming TUESDAY 9 June, the stored day 1's date (the window reaches the two days after each posted date),
    /// <c>absenceType = CHILD_SICK_DAY</c>. Only the Tuesday row is stored.
    ///
    /// <para><b>Kind F.</b> Red on the base: the Monday save answers 200 and two rows exist.</para>
    /// </summary>
    [Fact]
    public async Task DayOnePostedBeforeStoredDayOne_StoredDateRefused()
    {
        var emp = await _host.SeedEmployeeAsync("HK");
        await ChildSickHost.AssertOkAsync(await _host.SaveAsync(emp, "HK", June, (Day1, 7.4m, Tuesday)));

        var rsp = await _host.SaveAsync(emp, "HK", June, (Day1, 7.4m, Monday));

        await ChildSickHost.AssertRefusalAsync(rsp, StartKind, Day1, Tuesday, DayOneMessage(Day1, Tuesday));
        Assert.Equal(new[] { (Tuesday, Day1, 7.4m) }, await _host.ReadAbsencesAsync(emp));
    }

    /// <summary>
    /// Rule A (5), the 1st-sick-day pair. In ONE save: day 1 on Monday 8, day 2 on Tuesday 9 and day 1 on Wednesday 10 June.
    /// The day before Wednesday holds a 2nd sick day, so Wednesday's day 1 is refused (any of the three day codes before it
    /// counts), <c>kind = child-sick-illness-start</c>, naming Wednesday 10 June, <c>absenceType = CHILD_SICK_DAY</c>. No row is
    /// written. (Monday's and Tuesday's rows are legal in themselves.)
    ///
    /// <para><b>Kind F.</b> Red on the base: no rule A, the save answers 200 and all three rows exist. Mutation X (the arm's
    /// "day before holds day 1, 2 or 3" narrowed to "holds day 1") also turns it red: Tuesday holds day 2, so the narrowed rule
    /// no longer refuses and the save answers 200.</para>
    /// </summary>
    [Fact]
    public async Task DayOneAfterDayTwo_Refused()
    {
        var emp = await _host.SeedEmployeeAsync("HK");

        var rsp = await _host.SaveAsync(emp, "HK", June,
            (Day1, 7.4m, Monday), ("CHILD_SICK_DAY_2", 7.4m, Tuesday), (Day1, 7.4m, Wednesday));

        await ChildSickHost.AssertRefusalAsync(rsp, StartKind, Day1, Wednesday, DayOneMessage(Day1, Wednesday));
        Assert.Empty(await _host.ReadAbsencesAsync(emp));
    }

    /// <summary>
    /// Rule A (6), the call-home pair. A call-home day on Monday 8 June (4,40 h, 08:00-11:00 worked) is saved (200). A second
    /// save of a call-home day on Tuesday 9 June (4,40 h, 08:00-11:00 worked, so rule B is satisfied) is refused: the day
    /// before holds a call-home day, and the call-home arm counts ALL four child codes. <c>kind = child-sick-illness-start</c>,
    /// <c>absenceType = CHILD_SICK_CALL_HOME</c>, naming Tuesday 9 June. Tuesday's absence and work-time day are not written;
    /// only Monday's remain.
    ///
    /// <para><b>Kind F.</b> Red on the base: no rule A, the Tuesday save answers 200. Mutation Y (the call-home arm narrowed to
    /// the three day codes, so a call-home day before no longer counts) also turns it red.</para>
    /// </summary>
    [Fact]
    public async Task CallHomeAfterCallHome_Refused()
    {
        var emp = await _host.SeedEmployeeAsync("HK");
        await ChildSickHost.AssertOkAsync(await _host.SaveRawAsync(emp, "HK", June,
            absences: new[] { (CallHome, 4.4m, Monday) },
            workTime: new[] { (Monday, Morning3Hours) }));

        var rsp = await _host.SaveRawAsync(emp, "HK", June,
            absences: new[] { (CallHome, 4.4m, Tuesday) },
            workTime: new[] { (Tuesday, Morning3Hours) });

        await ChildSickHost.AssertRefusalAsync(rsp, StartKind, CallHome, Tuesday, DayOneMessage(CallHome, Tuesday));
        Assert.Equal(new[] { (Monday, CallHome, 4.4m) }, await _host.ReadAbsencesAsync(emp));
        Assert.Equal(new[] { (Monday, "08:00-11:00") }, await _host.ReadWorkTimeAsync(emp));
    }

    // ── Rule B ──

    /// <summary>
    /// Rule B (5). A call-home day of 3,50 h with 08:00-11:54 (3,90 h) worked the same Wednesday 10 June: accepted (the
    /// paid-absence bound also holds: 3,50 &lt;= 7,40 - 3,90). Both the absence and the work-time day are stored.
    ///
    /// <para><b>Kind R</b> (answers 200 on the base too). Discriminating power: against a rule B that reads the wrong
    /// figure for interval work. Red conditions: the 200 assertion and the stored rows.</para>
    /// </summary>
    [Fact]
    public async Task CallHomeWithIntervals_Accepted()
    {
        var emp = await _host.SeedEmployeeAsync("HK");

        await ChildSickHost.AssertOkAsync(await _host.SaveRawAsync(emp, "HK", June,
            absences: new[] { (CallHome, 3.5m, Wednesday) },
            workTime: new[] { (Wednesday, Morning3Point9) }));

        Assert.Equal(new[] { (Wednesday, CallHome, 3.5m) }, await _host.ReadAbsencesAsync(emp));
        Assert.Equal(new[] { (Wednesday, "08:00-11:54") }, await _host.ReadWorkTimeAsync(emp));
    }

    /// <summary>
    /// Rule B (6). A call-home day of 7,40 h on Wednesday 10 June with no work at all is refused,
    /// <c>kind = child-sick-call-home-without-work</c>, naming 10 June. Nothing is written.
    ///
    /// <para><b>Kind F.</b> Red on the base: the save answers 200 (the bound allows 7,40 on an empty 7,40 day).</para>
    /// </summary>
    [Fact]
    public async Task CallHomeWithoutWork_Refused()
    {
        var emp = await _host.SeedEmployeeAsync("HK");

        var rsp = await _host.SaveAsync(emp, "HK", June, (CallHome, 7.4m, Wednesday));

        await ChildSickHost.AssertRefusalAsync(rsp, WithoutWorkKind, CallHome, Wednesday, WithoutWorkMessage(Wednesday));
        Assert.Empty(await _host.ReadAbsencesAsync(emp));
    }

    /// <summary>
    /// Rule B (7). A call-home day of 3,50 h on Wednesday 10 June where the day holds only project hours (3,90 h) and no
    /// intervals: accepted, because worked is max(project hours, interval hours).
    ///
    /// <para><b>Kind R</b> (answers 200 on the base too). Discriminating power: against a rule B that looks only at
    /// intervals. Red conditions: the 200 assertion and the stored absence.</para>
    /// </summary>
    [Fact]
    public async Task CallHomeWithOnlyProjectHours_Accepted()
    {
        var emp = await _host.SeedEmployeeAsync("HK");

        await ChildSickHost.AssertOkAsync(await _host.SaveRawAsync(emp, "HK", June,
            absences: new[] { (CallHome, 3.5m, Wednesday) },
            entries: new[] { (Wednesday, 3.9m) }));

        Assert.Equal(new[] { (Wednesday, CallHome, 3.5m) }, await _host.ReadAbsencesAsync(emp));
        Assert.Empty(await _host.ReadWorkTimeAsync(emp));
    }

    /// <summary>
    /// Rule B (8). A stored call-home day with work (the shape of fact 5: 3,50 h, 08:00-11:54). A later save posts that date's
    /// work-time day with an EMPTY <c>intervals</c> array and no project hours, which would bring the day's worked hours to 0:
    /// refused, <c>kind = child-sick-call-home-without-work</c>, naming Wednesday 10 June. The stored work-time day is
    /// unchanged (still 08:00-11:54) and the call-home row is still stored.
    ///
    /// <para><b>Kind F.</b> Red on the base: nothing refuses an empty-intervals work-time day, so the save answers 200 and
    /// the stored day's intervals are replaced by none.</para>
    /// </summary>
    [Fact]
    public async Task EmptyIntervalsOnStoredCallHomeDay_Refused()
    {
        var emp = await _host.SeedEmployeeAsync("HK");
        await ChildSickHost.AssertOkAsync(await _host.SaveRawAsync(emp, "HK", June,
            absences: new[] { (CallHome, 3.5m, Wednesday) },
            workTime: new[] { (Wednesday, Morning3Point9) }));

        var rsp = await _host.SaveRawAsync(emp, "HK", June,
            workTime: new[] { (Wednesday, Array.Empty<(string Start, string End)>()) });

        await ChildSickHost.AssertRefusalAsync(rsp, WithoutWorkKind, CallHome, Wednesday, WithoutWorkMessage(Wednesday));
        Assert.Equal(new[] { (Wednesday, "08:00-11:54") }, await _host.ReadWorkTimeAsync(emp));
        Assert.Equal(new[] { (Wednesday, CallHome, 3.5m) }, await _host.ReadAbsencesAsync(emp));
    }
}
