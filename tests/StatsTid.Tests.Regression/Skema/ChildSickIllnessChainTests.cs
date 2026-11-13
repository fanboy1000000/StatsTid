namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14811-T (QUAL-236), AC-S0-2d and AC-S0-2i — the illness chain. In plain terms: one child's illness is a
/// run of consecutive calendar days, "1st, 2nd, 3rd sick day". Day 2 may be registered only when day 1 stands the day
/// before, and day 3 only after day 2, unless the missing day had no planned hours (a weekend day needs no row because
/// nothing is planned on it). An illness keeps the OK version of its first day, so one that starts on 30 March 2026
/// (OK24, two days) gets no third day on 1 April even though a new illness on 1 April would get three. The yearly cap
/// is gone: a second illness the same year is accepted in full.
///
/// <para>Every refusal is 422 with <c>error = absence_type_not_eligible</c> and <c>kind = child-sick-chain</c> (or
/// <c>child-sick-not-offered</c> when the code itself is not offered on its own date), naming the refused code and its
/// date, and writes nothing. Expected values come from the agreement texts and the refinement (<c>REF:267-291</c>),
/// never from the code. The helper (<see cref="ChildSickHost"/>) is reusable: TASK-14840-T adds
/// <c>RemovingDay1WhileDay2Exists_422_NamesTheDependentDate</c> to this class, driven by a clear (AC-CL-19); that
/// name is not used here (AC-S0-2j, NR-A1).</para>
///
/// <para>Dates (each a weekday checked against the calendar; <c>init.sql</c> seeds no public holiday on any of them):
/// Monday 30 and Tuesday 31 March 2026 (OK24), Wednesday 1 April 2026 (OK26), Monday 13 April, Monday 8 to Wednesday 10
/// June, Monday 7 to Wednesday 9 September 2026. Docker-gated, <b>CI-pending</b> (CI-W2); never run locally.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ChildSickIllnessChainTests : IAsyncLifetime
{
    private const string Day1 = "CHILD_SICK_DAY";
    private const string Day2 = "CHILD_SICK_DAY_2";
    private const string Day3 = "CHILD_SICK_DAY_3";
    private const string ChainKind = "child-sick-chain";
    private const string NotOfferedKind = "child-sick-not-offered";

    private ChildSickHost _host = null!;

    public async Task InitializeAsync() => _host = await ChildSickHost.StartAsync();

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    /// <summary>
    /// AC-S0-2d (1). HK (OK26): <c>CHILD_SICK_DAY_2</c> 7,40 h on Wednesday 10 June 2026 with nothing on Tuesday 9 June
    /// (a working day, so day 1 is genuinely missing) is refused: 422, <c>kind = child-sick-chain</c>, naming
    /// <c>CHILD_SICK_DAY_2</c> and 2026-06-10, and no row is written.
    ///
    /// <para><b>Kind F.</b> Red conditions: the 422 assertion. The base has no chain: the code is in the vocabulary and
    /// HK's yearly quota of 2 admits one day, so the save answers 200.</para>
    /// </summary>
    [Fact]
    public async Task Day2WithoutDay1OnWorkingDay_422()
    {
        var wednesday = new DateOnly(2026, 6, 10);
        var emp = await _host.SeedEmployeeAsync("HK");

        var rsp = await _host.SaveAsync(emp, "HK", wednesday, (Day2, 7.4m, wednesday));

        await ChildSickHost.AssertRefusalAsync(rsp, ChainKind, Day2, wednesday);
        Assert.Empty(await _host.ReadAbsencesAsync(emp));
    }

    /// <summary>
    /// AC-S0-2d (2). PROSA (OK26): illness 1 is <c>CHILD_SICK_DAY</c> Monday 8, <c>_2</c> Tuesday 9 and <c>_3</c>
    /// Wednesday 10 June 2026 (one June save, 7,40 h each); illness 2 is the same three codes on Monday 7, Tuesday 8 and
    /// Wednesday 9 September 2026 (one September save). Both saves answer 200 and all six rows are stored with their
    /// code and 7,40 h: no yearly cap limits the second illness.
    ///
    /// <para><b>Kind F.</b> Red conditions: the September save's 200 assertion. The base counts the three codes against
    /// one yearly <c>CHILD_SICK</c> quota (PROSA 3), which illness 1 has used, so the September save is refused with
    /// "Entitlement quota exceeded".</para>
    /// </summary>
    [Fact]
    public async Task TwoIllnessesInOneYear_BothAcceptedInFull()
    {
        var emp = await _host.SeedEmployeeAsync("PROSA");
        var june = new DateOnly(2026, 6, 1);
        var september = new DateOnly(2026, 9, 1);

        await ChildSickHost.AssertOkAsync(await _host.SaveAsync(emp, "PROSA", june,
            (Day1, 7.4m, new DateOnly(2026, 6, 8)),
            (Day2, 7.4m, new DateOnly(2026, 6, 9)),
            (Day3, 7.4m, new DateOnly(2026, 6, 10))));
        await ChildSickHost.AssertOkAsync(await _host.SaveAsync(emp, "PROSA", september,
            (Day1, 7.4m, new DateOnly(2026, 9, 7)),
            (Day2, 7.4m, new DateOnly(2026, 9, 8)),
            (Day3, 7.4m, new DateOnly(2026, 9, 9))));

        Assert.Equal(
            new[]
            {
                (new DateOnly(2026, 6, 8), Day1, 7.4m), (new DateOnly(2026, 6, 9), Day2, 7.4m), (new DateOnly(2026, 6, 10), Day3, 7.4m),
                (new DateOnly(2026, 9, 7), Day1, 7.4m), (new DateOnly(2026, 9, 8), Day2, 7.4m), (new DateOnly(2026, 9, 9), Day3, 7.4m),
            },
            await _host.ReadAbsencesAsync(emp));
    }

    /// <summary>
    /// AC-S0-2i (1). HK (OK26): <c>CHILD_SICK_DAY_3</c> 7,40 h on Monday 13 April 2026 alone answers 200 and the row is
    /// stored. The illness began on Saturday 11 April, so day 1 and day 2 (Saturday and Sunday) had no planned hours and
    /// need no rows: a chain that demanded a row on every earlier calendar day would refuse it.
    ///
    /// <para><b>Kind R</b> (a regression pin: it answers 200 on the base too, where the vocabulary gate knows the code,
    /// there is no chain and HK's yearly quota of 2 admits one day; the wave-2 adjudication ruled it R by Block A's
    /// letter). Its discriminating power is against a chain that would demand rows on zero-planned days: red conditions
    /// are the 200 assertion and the stored-row assertion.</para>
    /// </summary>
    [Fact]
    public async Task Saturday11April2026Onset_Day3Monday13April_200()
    {
        var monday = new DateOnly(2026, 4, 13);
        var emp = await _host.SeedEmployeeAsync("HK");

        await ChildSickHost.AssertOkAsync(await _host.SaveAsync(emp, "HK", monday, (Day3, 7.4m, monday)));

        Assert.Equal(new[] { (monday, Day3, 7.4m) }, await _host.ReadAbsencesAsync(emp));
    }

    /// <summary>
    /// AC-S0-2i (2). HK (OK24): <c>CHILD_SICK_DAY_3</c> on Monday 30 March 2026, after an onset on Saturday 28 March, is
    /// refused: OK24 gives two days per illness, so 30 March offers no third-day code. 422,
    /// <c>kind = child-sick-not-offered</c>, naming <c>CHILD_SICK_DAY_3</c> and 2026-03-30, and no row is written.
    ///
    /// <para><b>Kind F.</b> Red conditions: the 422 assertion. The base saves HK day 3 on 30 March (no per-code check;
    /// HK's yearly quota of 2 admits one day).</para>
    /// </summary>
    [Fact]
    public async Task Saturday28March2026Onset_Day3Monday30March_422()
    {
        var monday = new DateOnly(2026, 3, 30);
        var emp = await _host.SeedEmployeeAsync("HK", "OK24");

        var rsp = await _host.SaveAsync(emp, "HK", monday, (Day3, 7.4m, monday));

        await ChildSickHost.AssertRefusalAsync(rsp, NotOfferedKind, Day3, monday);
        Assert.Empty(await _host.ReadAbsencesAsync(emp));
    }

    /// <summary>
    /// AC-S0-2i (3). PROSA: a March save of <c>CHILD_SICK_DAY</c> Monday 30 March and <c>_2</c> Tuesday 31 March 2026
    /// (OK24) answers 200. The April month view then serves <c>CHILD_SICK_DAY_3</c> (a new illness on 1 April would get
    /// three days), yet an April save of <c>_3</c> on Wednesday 1 April is refused: 422, <c>kind = child-sick-chain</c>,
    /// naming 2026-04-01, because the illness follows day 1's OK24, which gave two days. No row is written for 1 April.
    ///
    /// <para><b>Kind F.</b> Red conditions: the final 422 assertion. The base saves the April <c>_3</c> (PROSA's yearly 3
    /// admits the third day).</para>
    /// </summary>
    [Fact]
    public async Task Day1Monday30March_Day3Wednesday1April_422_FollowsDay1Ok24()
    {
        var emp = await _host.SeedEmployeeAsync("PROSA", "OK24");
        var monday = new DateOnly(2026, 3, 30);
        var tuesday = new DateOnly(2026, 3, 31);
        var wednesday = new DateOnly(2026, 4, 1);

        await ChildSickHost.AssertOkAsync(await _host.SaveAsync(emp, "PROSA", monday, (Day1, 7.4m, monday), (Day2, 7.4m, tuesday)));

        var april = await _host.GetMonthAsync(emp, "PROSA", 2026, 4);
        Assert.Contains(april.GetProperty("absenceTypes").EnumerateArray(), e => e.GetProperty("type").GetString() == Day3);

        var rsp = await _host.SaveAsync(emp, "PROSA", wednesday, (Day3, 7.4m, wednesday));

        await ChildSickHost.AssertRefusalAsync(rsp, ChainKind, Day3, wednesday);
        Assert.Equal(
            new[] { (monday, Day1, 7.4m), (tuesday, Day2, 7.4m) },
            await _host.ReadAbsencesAsync(emp));
    }

    /// <summary>
    /// AC-CL-19 (S148 TASK-14840-T; AC-S0-2j moved from 14811-T, NR-A1: no save could remove a row before cells could be cleared).
    /// HK (OK26): <c>CHILD_SICK_DAY</c> Monday 8 June and <c>CHILD_SICK_DAY_2</c> Tuesday 9 June 2026 (one save, 7,40 h each). A save
    /// that CLEARS Monday's 1st sick day while Tuesday's 2nd stands is refused: 422, <c>kind = child-sick-chain</c>, naming
    /// <c>CHILD_SICK_DAY_2</c> and TUESDAY 2026-06-09 (the dependent date, not the cleared one), and nothing changes: both rows are
    /// still current.
    ///
    /// <para><b>Kind F.</b> Red conditions: on base the <c>cleared</c> member is ignored, so the save answers 200 and the 422
    /// assertion trips.</para>
    /// </summary>
    [Fact]
    public async Task RemovingDay1WhileDay2Exists_422_NamesTheDependentDate()
    {
        var june = new DateOnly(2026, 6, 1);
        var monday = new DateOnly(2026, 6, 8);
        var tuesday = new DateOnly(2026, 6, 9);
        var emp = await _host.SeedEmployeeAsync("HK");
        await ChildSickHost.AssertOkAsync(await _host.SaveAsync(emp, "HK", june, (Day1, 7.4m, monday), (Day2, 7.4m, tuesday)));

        var rsp = await _host.SaveClearedAsync(emp, "HK", june, clearedAbsences: new[] { (Day1, monday) });

        await ChildSickHost.AssertRefusalAsync(rsp, ChainKind, Day2, tuesday);
        Assert.Equal(
            new[] { (monday, Day1, 7.4m), (tuesday, Day2, 7.4m) },
            await _host.ReadAbsencesAsync(emp));
    }
}
