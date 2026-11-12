using System.Net;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S148 / TASK-14840-T — the paid-absence day bound and a cleared cell (addendum A1 AC-CL-17, AC-CL-18). In plain
/// terms: a day is worth its planned hours, and paid absence and work together may not claim more (ADR-043 D2). Before
/// cells could be cleared, a day locked by a typing mistake (a sick day typed where the work was meant) could not be
/// repaired, because the work was refused for exceeding the bound and the sick day could not be removed. The bound now
/// judges the day AS IT WILL BE, with the cleared cells removed, and a date the request only clears is not judged at all.
///
/// <para>Dates: Wednesday 10 June 2026, planned 7,4 h (full time). Work time 08:00-15:24 is 7,4 h. Every refusal is the
/// bound's 422 with <c>kind = "paid-absence-day-bound"</c>. Docker-gated: <b>CI-pending</b>. "Base" is
/// <c>728f4702</c>, where the <c>cleared</c> member is ignored.</para>
/// </summary>
[Trait("Category", "Docker")]
[Collection("SkemaCellClear")]
public sealed class SkemaCellClearBoundTests : SkemaCellClearTestBase
{
    private const string BoundKind = "paid-absence-day-bound";

    public SkemaCellClearBoundTests(SkemaCellClearFixture fx) : base(fx) { }

    /// <summary>
    /// AC-CL-17 (1). <c>SICK_DAY</c> 7,4 on Wednesday 10 June, then clear it, then work 08:00-15:24 with PRJ-1 7,4: each
    /// request answers 200 and the sick day is retired.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so the sick day stands and the work save is the
    /// bound's 422; the work save's 200 assertion trips.</para>
    /// </summary>
    [Fact]
    public async Task MistakenSickDay_ClearThenWork_200()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Wed10, "SICK_DAY", 7.4m) }), "sick day");

        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(clearAbsences: new[] { (Wed10, "SICK_DAY") })), HttpStatusCode.OK, "the clear");
        await AssertStatusAsync(await PostSaveAsync(EmployeeClient(emp), emp, Save(
            workTime: new[] { (Wed10, "08:00", "15:24") }, entries: new[] { (Wed10, "PRJ-1", 7.4m) })), HttpStatusCode.OK, "the work save");

        Assert.NotNull(Assert.Single(await AbsencesAsync(emp)).SupersededBy);
        Assert.Equal("08:00-15:24", Assert.Single(await WorkAsync(emp)).Intervals);
    }

    /// <summary>
    /// AC-CL-17 (2). The same clear and work as ONE request: 200, the sick day retired, the work stored. The bound judges
    /// the day with the sick day removed.
    ///
    /// <para>Kind F. Red conditions: on base the sick day is judged as standing, so the save answers 422 and the 200
    /// assertion trips.</para>
    /// </summary>
    [Fact]
    public async Task ClearSickAndPostWork_OneRequest_200()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Wed10, "SICK_DAY", 7.4m) }), "sick day");

        await AssertStatusAsync(await PostSaveAsync(EmployeeClient(emp), emp, Save(
            workTime: new[] { (Wed10, "08:00", "15:24") },
            entries: new[] { (Wed10, "PRJ-1", 7.4m) },
            clearAbsences: new[] { (Wed10, "SICK_DAY") })), HttpStatusCode.OK, "clear and work in one request");

        Assert.NotNull(Assert.Single(await AbsencesAsync(emp)).SupersededBy);
        Assert.Equal("08:00-15:24", Assert.Single(await WorkAsync(emp)).Intervals);
        Assert.Equal(7.4m, Assert.Single(await EntriesAsync(emp)).Hours);
    }

    /// <summary>
    /// AC-CL-17 (3). <c>CARE_DAY</c> 7,4 (a day of the care-day balance, <c>used</c> 1,0), then clear it, then the
    /// same work: 200, and the day is back (<c>used</c> 0,0).
    ///
    /// <para>Kind F. Red conditions: on base the care day stands, so the work save answers 422 and its 200 assertion
    /// trips.</para>
    /// </summary>
    [Fact]
    public async Task MistakenCareDay_ClearThenWork_200_DayBack()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(absences: new[] { (Wed10, "CARE_DAY", 7.4m) }), "care day");
        Assert.Equal(1.0m, await UsedAsync(emp, "CARE_DAY"));

        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(clearAbsences: new[] { (Wed10, "CARE_DAY") })), HttpStatusCode.OK, "the clear");
        await AssertStatusAsync(await PostSaveAsync(EmployeeClient(emp), emp, Save(
            workTime: new[] { (Wed10, "08:00", "15:24") }, entries: new[] { (Wed10, "PRJ-1", 7.4m) })), HttpStatusCode.OK, "the work save");

        Assert.Equal(0.0m, await UsedAsync(emp, "CARE_DAY"));
    }

    /// <summary>
    /// AC-CL-17 (4) (owner Q1, 2026-11-10). PRJ-1 0,01 on Wednesday 10 June, then a <c>CARE_DAY</c> 7,4 is refused by the
    /// bound (7,4 &gt; 7,4 - 0,01); the entry is cleared; the same <c>CARE_DAY</c> then answers 200.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so the second care-day save is still the bound's 422
    /// and its 200 assertion trips.</para>
    /// </summary>
    [Fact]
    public async Task MistakenProjectEntry_ClearThenCareDay_200()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(entries: new[] { (Wed10, "PRJ-1", 0.01m) }), "the 0,01 h entry");
        var refused = await RefusalAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(absences: new[] { (Wed10, "CARE_DAY", 7.4m) })),
            HttpStatusCode.UnprocessableEntity);
        Assert.Equal(BoundKind, refused.GetProperty("kind").GetString());

        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(clearEntries: new[] { (Wed10, "PRJ-1") })), HttpStatusCode.OK, "the clear");
        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(absences: new[] { (Wed10, "CARE_DAY", 7.4m) })), HttpStatusCode.OK, "the care day");

        Assert.NotNull(Assert.Single(await EntriesAsync(emp)).SupersededBy);
        Assert.Equal(7.4m, Assert.Single(await AbsencesAsync(emp)).Hours);
    }

    /// <summary>
    /// AC-CL-17 (5). A day holding 7,4 h of interval time and PRJ-1 7,4: one request clearing PRJ-1 and posting
    /// <c>SICK_DAY</c> 7,4 gets the bound's 422 (the day's worked hours stay 7,4 from the intervals), and nothing is
    /// written: PRJ-1 is still current, no sick day exists, no clear event.
    ///
    /// <para>Kind R (green on base too, where the work is also 7,4). Red conditions: none on base; fails if the bound
    /// forgets the interval hours when it removes the cleared entry (the save answers 200).</para>
    /// </summary>
    [Fact]
    public async Task ClearProjectButIntervalsRemain_SickDay422()
    {
        var emp = await SeedEmployeeAsync();
        await SaveOkAsync(emp, Save(
            workTime: new[] { (Wed10, "08:00", "15:24") }, entries: new[] { (Wed10, "PRJ-1", 7.4m) }), "work and project");
        var eventsBefore = (await OutboxAsync(emp)).Count;

        var body = await RefusalAsync(await PostSaveAsync(EmployeeClient(emp), emp, Save(
            absences: new[] { (Wed10, "SICK_DAY", 7.4m) },
            clearEntries: new[] { (Wed10, "PRJ-1") })), HttpStatusCode.UnprocessableEntity);

        Assert.Equal(BoundKind, body.GetProperty("kind").GetString());
        Assert.Null(Assert.Single(await EntriesAsync(emp)).SupersededBy);
        Assert.Empty(await AbsencesAsync(emp));
        Assert.Empty(await OutboxAsync(emp, "TimeEntryCleared"));
        Assert.Equal(eventsBefore, (await OutboxAsync(emp)).Count);
    }

    /// <summary>
    /// AC-CL-18. A clear-only date is not judged by the bound. A day stored over the bound by fixture (<c>SICK_DAY</c> 4,0
    /// + <c>LEAVE_WITH_PAY</c> 3,4 + PRJ-1 7,4 on Wednesday 10 June, all SKEMA rows written by SQL below any real outbox
    /// id): a request clearing only <c>SICK_DAY</c> answers 200 and retires it, though the rest of the day still exceeds
    /// the bound.
    ///
    /// <para>Kind F. Red conditions: on base the clear is ignored, so the sick day stays current and the retire
    /// assertion trips.</para>
    /// </summary>
    [Fact]
    public async Task ClearOnlyOnADayAlreadyOverTheBound_200()
    {
        var emp = await SeedEmployeeAsync();
        foreach (var (type, hours) in new[] { ("SICK_DAY", 4.0m), ("LEAVE_WITH_PAY", 3.4m) })
            await ExecAsync(
                """
                INSERT INTO absences_projection (event_id, employee_id, date, absence_type, hours, feriedage,
                                                 agreement_code, ok_version, occurred_at, outbox_id, source)
                VALUES (gen_random_uuid(), @e, @d, @t, @h, NULL, 'AC', 'OK26', NOW(), 0, 'SKEMA')
                """, ("e", emp), ("d", Wed10), ("t", type), ("h", hours));
        await ExecAsync(
            """
            INSERT INTO time_entries_projection (event_id, employee_id, date, hours, task_id, agreement_code, ok_version,
                                                 voluntary_unsocial_hours, occurred_at, outbox_id, source)
            VALUES (gen_random_uuid(), @e, @d, 7.4, 'PRJ-1', 'AC', 'OK26', FALSE, NOW(), 0, 'SKEMA')
            """, ("e", emp), ("d", Wed10));

        await AssertStatusAsync(
            await PostSaveAsync(EmployeeClient(emp), emp, Save(clearAbsences: new[] { (Wed10, "SICK_DAY") })), HttpStatusCode.OK);

        var rows = await AbsencesAsync(emp);
        Assert.NotNull(rows.Single(r => r.Type == "SICK_DAY").SupersededBy);
        Assert.Null(rows.Single(r => r.Type == "LEAVE_WITH_PAY").SupersededBy);
        Assert.Null(Assert.Single(await EntriesAsync(emp)).SupersededBy);
    }
}
