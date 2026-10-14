using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S145 / TASK-14508-T (R50) — the month-end night shift: a shift that starts on the LAST evening of a
/// month and runs past midnight must neither fail that month nor lose its after-midnight hours.
///
/// <para>
/// <b>Plain language.</b> An employee works Wednesday 30 September 2026, 23:00 to 02:00 (3 hours),
/// filed as ONE registration dated 30 September. By the calendar, 1 of those hours falls on 30 September
/// and 2 hours fall on 1 October, which is the next pay month (and the next OK-version day count). Before
/// S145 a guard in <c>PeriodCalculationService</c> (<c>AssertNoDroppedBoundaryCrossing</c>) refused to
/// calculate September at all, because September's hours were handed in by the caller and the October
/// half could not be proven to be picked up elsewhere. Now payroll reads its own hours and October's read
/// starts the day before its opening Monday (28 September minus 1 = 27 September), so October SEES the
/// registration and dates the after-midnight half on 1 October. The guard has therefore served its
/// purpose and has been retired (commit 37ab341); this pin holds the behaviour that makes that safe.
/// </para>
///
/// <para>
/// <b>Ruling and history.</b> The adjudicator's ruling at TASK-14508's return (the guard was interim until
/// payroll widened its own read; QUAL-144; ADR-039 D5b/D8; SPRINT-132 lines 315-320 said the two must
/// change in lockstep, and TASK-14508 is that widened read). The guard was retired in the 14508 fix-up
/// (37ab341), which came after this pin was written.
/// </para>
///
/// <para>
/// <b>Red conditions (mechanic (a): observed RED against the guard (0c36ed6), green since its retirement (37ab341)).</b>
/// Assertion 1 fails: September's <c>CalculateWithOutcomeAsync</c> throws
/// <c>PlannerInvariantViolation</c> from the guard. Since the guard was retired it is green. Mutation MU-43, catalogued
/// at the A.4 gate: restore the throw in <c>AssertNoDroppedBoundaryCrossing</c> (keep the API, bring back
/// the refusal) — assertion 1 trips again.
/// </para>
///
/// <para>
/// Deliberately NOT asserted: any overtime or hour totals (they change when TASK-14511 lands). Only what is
/// stable across A.2 and A.3 is pinned: no refusal, no entry past the month end, the read starts EXACTLY
/// on the day before the opening Monday, and the halves are 1.00 h (30 Sep) and 2.00 h (1 Oct), each once per
/// request, adding to the registered 3.00 h. The Step 5a fix-up added the exact-window theory and the Sunday
/// month-end fact (31 Jan 2027), where the extra read day is the only thing that keeps the hours.
/// </para>
/// </summary>
public sealed class MonthEndCrossingTests
{
    private const string Emp = "EMP-S145-MONTHEND";
    private static readonly DateOnly Sep01 = new(2026, 9, 1);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);
    private static readonly DateOnly Oct01 = new(2026, 10, 1);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    [Fact]
    public async Task MonthEndNightShift_Wed30Sep2300To0200_SeptemberNotRefused_PostMidnightHoursReachOctober()
    {
        // The calendar premise the story rests on.
        Assert.Equal(DayOfWeek.Wednesday, Sep30.DayOfWeek);

        // September: 1. completes (no throw); 2. nothing after 30 Sep; 3. 1.00 h on 30 Sep, once per request.
        var september = await RunMonthAsync(Sep01, Sep30, NightShiftRow(Sep30));
        Assert.True(september.Outcome.Result.Success, september.Outcome.Result.ErrorMessage);
        var septHours = AssertShiftHalfInEveryRequest(september.Engine, Sep30, expectedHours: 1.00m, nothingAfter: Sep30);

        // October
        var october = await RunMonthAsync(Oct01, Oct31, NightShiftRow(Sep30));
        Assert.True(october.Outcome.Result.Success, october.Outcome.Result.ErrorMessage);

        // 3. October read starts EXACTLY on Sunday 27 Sep (opening Monday 28 Sep, minus one day).
        var from = Assert.Single(october.Reader.FromInclusiveCalls);
        Assert.Equal(new DateOnly(2026, 9, 27), from);

        // 4. October sends the after-midnight half: one entry dated 1 Oct, 2.00 h (no Distinct()).
        var octHours = AssertShiftHalfInEveryRequest(october.Engine, Oct01, expectedHours: 2.00m, nothingAfter: Oct31);

        // Conservation: the two halves add back to the 3.00 h that was registered.
        Assert.Equal(3.00m, septHours + octHours);
    }

    /// <summary>
    /// The read window starts at WeekStart(periodStart) - 1: the Sunday before the Monday of the
    /// month opening week. The extra day is what lets a Sunday month-end night shift after-midnight
    /// hours reach the next month. Rows derived from the rule, each with its weekday premise.
    /// </summary>
    [Theory]
    [InlineData("2026-10-01", "2026-10-31", "2026-09-27", DayOfWeek.Thursday)] // opening Monday 28 Sep
    [InlineData("2026-06-01", "2026-06-30", "2026-05-31", DayOfWeek.Monday)]   // 1 Jun IS the Monday
    [InlineData("2027-01-01", "2027-01-31", "2026-12-27", DayOfWeek.Friday)]   // opening Monday 28 Dec; year boundary
    [InlineData("2027-02-01", "2027-02-28", "2027-01-31", DayOfWeek.Monday)]   // 1 Feb IS the Monday
    public async Task HoursReadStart_IsTheDayBeforeTheOpeningMonday(
        string periodStart, string periodEnd, string expectedFrom, DayOfWeek firstDayOfWeek)
    {
        var start = DateOnly.Parse(periodStart);
        var from = DateOnly.Parse(expectedFrom);
        Assert.Equal(firstDayOfWeek, start.DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, from.DayOfWeek); // the day before a Monday

        var run = await RunMonthAsync(start, DateOnly.Parse(periodEnd));

        Assert.Equal(from, Assert.Single(run.Reader.FromInclusiveCalls));
    }

    [Fact]
    public async Task MonthEndNightShift_Sun31Jan2027_2300To0200_FebruaryReadsItAndPays200On1Feb()
    {
        var jan31 = new DateOnly(2027, 1, 31);
        var feb01 = new DateOnly(2027, 2, 1);
        Assert.Equal(DayOfWeek.Sunday, jan31.DayOfWeek);
        Assert.Equal(DayOfWeek.Monday, feb01.DayOfWeek);

        // January completes; nothing dated after 31 Jan; the pre-midnight half is 1.00 h on 31 Jan, once per request.
        var january = await RunMonthAsync(new DateOnly(2027, 1, 1), jan31, NightShiftRow(jan31));
        Assert.True(january.Outcome.Result.Success, january.Outcome.Result.ErrorMessage);
        var janHours = AssertShiftHalfInEveryRequest(january.Engine, jan31, expectedHours: 1.00m, nothingAfter: jan31);

        // February read starts exactly on Sunday 31 Jan: the extra day is the ONLY thing that keeps
        // these hours, since a read from 1 Feb would never see the row.
        var february = await RunMonthAsync(feb01, new DateOnly(2027, 2, 28), NightShiftRow(jan31));
        Assert.True(february.Outcome.Result.Success, february.Outcome.Result.ErrorMessage);
        Assert.Equal(jan31, Assert.Single(february.Reader.FromInclusiveCalls));

        // February sends exactly one entry dated Mon 1 Feb, 2.00 h.
        var febHours = AssertShiftHalfInEveryRequest(february.Engine, feb01, expectedHours: 2.00m, nothingAfter: new DateOnly(2027, 2, 28));
        Assert.Equal(3.00m, janHours + febHours);
    }

    private sealed record MonthRun(
        PeriodCalculationService.PeriodCalculationOutcome Outcome, WindowHonouringReader Reader, CapturingEngine Engine);

    /// <summary>Runs PCS for one month (HK, full time, employed throughout) over a window-honouring reader
    /// holding the given rows.</summary>
    private static async Task<MonthRun> RunMonthAsync(
        DateOnly start, DateOnly end, params TimeEntryProjectionRow[] rows)
    {
        var reader = new WindowHonouringReader(rows);
        var engine = new CapturingEngine();
        var pcs = BuildPcsOver(engine, reader);
        var profile = Profile(Emp) with { OkVersion = "OK26" };
        var outcome = await pcs.CalculateWithOutcomeAsync(
            Plan(Emp, start, end, windows: null), profile, previousFlexBalance: 0m);
        return new MonthRun(outcome, reader, engine);
    }

    /// <summary>
    /// Shape found: PCS sends the segment entries to every engine call that carries entries — the four time
    /// rules and the flex call, five requests per segment — so the half appears once PER request, never twice
    /// within one. Asserted per request, without Distinct(): at least one request carries entries, and EVERY
    /// such request carries EXACTLY ONE entry, the half dated the given date with the expected hours. In
    /// these one-row scenarios that pins both leak directions: the other month's half can neither leak in
    /// after the month end nor before the month start (a double-paid hour). Returns the observed hours, so
    /// the conservation sum checks observed values (S145 TASK-14508 Step 5a cycle 2, WARNING 1, NOTEs 1-2).
    /// </summary>
    private static decimal AssertShiftHalfInEveryRequest(
        CapturingEngine engine, DateOnly date, decimal expectedHours, DateOnly nothingAfter)
    {
        var requests = engine.RequestsWithEntries;
        Assert.NotEmpty(requests);
        decimal observed = 0m;
        foreach (var request in requests)
        {
            var half = Assert.Single(request);
            Assert.Equal(date, half.Date);
            Assert.True(half.Date <= nothingAfter, $"The half is dated {half.Date:yyyy-MM-dd}, after {nothingAfter:yyyy-MM-dd}.");
            Assert.Equal(expectedHours, half.Hours);
            observed = half.Hours;
        }
        return observed;
    }

    private static PeriodCalculationService BuildPcsOver(HttpMessageHandler engine, IRecordedHoursReader reader)
        => new PeriodCalculationService(
                new SingleClientHttpFactory(engine),
                mappingService: null!,
                new InMemoryEventStore(),
                connectionFactory: null!,
                Configuration(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<PeriodCalculationService>.Instance,
                classificationProvider: new FixedClassifications(RuleSet),
                localAgreementProfileRepo: null,
                profileResolver: null,
                employmentWindowResolver: null,
                employeeProfileRepo: null,
                hoursReader: reader);

    private static TimeEntryProjectionRow NightShiftRow(DateOnly date) => new()
    {
        EventId = Guid.Parse("00000000-0000-0000-0000-0000000145aa"),
        EmployeeId = Emp,
        Date = date,
        Hours = 3.00m,
        StartTime = new TimeOnly(23, 0),
        EndTime = new TimeOnly(2, 0),
        AgreementCode = "HK",
        OkVersion = "OK26",
        OccurredAt = date.ToDateTime(new TimeOnly(21, 0), DateTimeKind.Utc),
        OutboxId = 1,
    };

    /// <summary>An hours reader that, like production, returns only rows dated inside the requested
    /// window, and records each <c>fromInclusive</c> it is asked for.</summary>
    private sealed class WindowHonouringReader : IRecordedHoursReader
    {
        private readonly List<TimeEntryProjectionRow> _rows;
        public List<DateOnly> FromInclusiveCalls { get; } = new();

        public WindowHonouringReader(params TimeEntryProjectionRow[] rows) => _rows = rows.ToList();

        public Task<RecordedHours> ReadCurrentAsync(
            string employeeId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct)
        {
            FromInclusiveCalls.Add(fromInclusive);
            return Task.FromResult(new RecordedHours(
                _rows.Where(r => r.EmployeeId == employeeId && r.Date >= fromInclusive && r.Date <= toInclusive)
                     .OrderBy(r => r.OutboxId).ToList(),
                Array.Empty<AbsenceProjectionRow>()));
        }

        public Task<RecordedHours> ReadByEventIdsAsync(
            string employeeId, IReadOnlyList<Guid> entryEventIds, IReadOnlyList<Guid> absenceEventIds,
            CancellationToken ct) =>
            Task.FromResult(new RecordedHours(
                _rows.Where(r => entryEventIds.Contains(r.EventId)).ToList(),
                Array.Empty<AbsenceProjectionRow>()));
    }

    /// <summary>Captures the <c>entries</c> of every request PCS sends and answers success with no line items.</summary>
    private sealed class CapturingEngine : HttpMessageHandler
    {
        private readonly List<IReadOnlyList<(DateOnly Date, decimal Hours)>> _requests = new();

        /// <summary>The entries of each request that carried an entries array, one list per request.</summary>
        public IReadOnlyList<IReadOnlyList<(DateOnly Date, decimal Hours)>> RequestsWithEntries
        {
            get { lock (_requests) return _requests.ToList(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("entries", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                IReadOnlyList<(DateOnly Date, decimal Hours)> list = arr.EnumerateArray()
                    .Select(e => (DateOnly.Parse(e.GetProperty("date").GetString()!), e.GetProperty("hours").GetDecimal()))
                    .ToList();
                lock (_requests) _requests.Add(list);
            }

            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var ruleId = path.EndsWith("/evaluate-flex", StringComparison.Ordinal) ? "FLEX_BALANCE"
                : path.EndsWith("/evaluate-absence", StringComparison.Ordinal) ? "ABSENCE"
                : doc.RootElement.TryGetProperty("ruleId", out var rid) ? rid.GetString() : "UNKNOWN";
            var json = JsonSerializer.Serialize(
                new { ruleId, employeeId = "EMP", success = true, lineItems = Array.Empty<object>() },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class FixedClassifications : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public FixedClassifications(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }
}
