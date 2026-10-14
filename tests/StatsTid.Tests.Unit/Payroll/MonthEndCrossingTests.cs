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
/// purpose and is to be retired; this pin holds the behaviour that makes that safe.
/// </para>
///
/// <para>
/// <b>Ruling and history.</b> The adjudicator's ruling at TASK-14508's return (the guard was interim until
/// payroll widened its own read; QUAL-144; ADR-039 D5b/D8; SPRINT-132 lines 315-320 said the two must
/// change in lockstep, and TASK-14508 is that widened read). The guard is retired in a 14508 fix-up that
/// the implementer does after this pin is written.
/// </para>
///
/// <para>
/// <b>Red conditions (mechanic (a): observed RED on the branch as it stood at 66ec0d1, guard present).</b>
/// Assertion 1 fails: September's <c>CalculateWithOutcomeAsync</c> throws
/// <c>PlannerInvariantViolation</c> from the guard. After the fix-up it is green. Mutation MU-43, catalogued
/// at the A.4 gate: restore the throw in <c>AssertNoDroppedBoundaryCrossing</c> (keep the API, bring back
/// the refusal) — assertion 1 trips again.
/// </para>
///
/// <para>
/// Deliberately NOT asserted: any overtime or hour totals (they change when TASK-14511 lands). Only what is
/// stable across A.2 and A.3 is pinned: no refusal, no September entry past 30 September, October reads
/// from on or before 30 September, October receives the 2.00 h on 1 October.
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

        var profile = Profile(Emp) with { OkVersion = "OK26" }; // HK, full time, employed throughout (no windows)

        // ── September ────────────────────────────────────────────────────────────────
        var septReader = new WindowHonouringReader(NightShiftRow());
        var septEngine = new CapturingEngine();
        var septPcs = BuildPcsOver(septEngine, septReader);

        // 1. September's calculation completes: no PlannerInvariantViolation or other throw.
        var september = await septPcs.CalculateWithOutcomeAsync(
            Plan(Emp, Sep01, Sep30, windows: null), profile, previousFlexBalance: 0m);
        Assert.True(september.Result.Success, september.Result.ErrorMessage);

        // 2. No entry September sends to the engine is dated after 30 Sep.
        Assert.NotEmpty(septEngine.SentEntries);
        Assert.All(septEngine.SentEntries, e => Assert.True(
            e.Date <= Sep30, $"September sent an entry dated {e.Date:yyyy-MM-dd}, after 30 Sep."));

        // ── October ──────────────────────────────────────────────────────────────────
        var octReader = new WindowHonouringReader(NightShiftRow());
        var octEngine = new CapturingEngine();
        var octPcs = BuildPcsOver(octEngine, octReader);

        var october = await octPcs.CalculateWithOutcomeAsync(
            Plan(Emp, Oct01, Oct31, windows: null), profile, previousFlexBalance: 0m);
        Assert.True(october.Result.Success, october.Result.ErrorMessage);

        // 3. October's reader call starts on or before 30 Sep.
        var from = Assert.Single(octReader.FromInclusiveCalls);
        Assert.True(from <= Sep30, $"October's read started {from:yyyy-MM-dd}, after 30 Sep.");

        // 4. An entry October sends is dated 1 Oct with 2.00 h.
        var postMidnight = Assert.Single(octEngine.SentEntries.Where(e => e.Date == Oct01).Distinct());
        Assert.Equal(2.00m, postMidnight.Hours);
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

    private static TimeEntryProjectionRow NightShiftRow() => new()
    {
        EventId = Guid.Parse("00000000-0000-0000-0000-0000000145aa"),
        EmployeeId = Emp,
        Date = Sep30,
        Hours = 3.00m,
        StartTime = new TimeOnly(23, 0),
        EndTime = new TimeOnly(2, 0),
        AgreementCode = "HK",
        OkVersion = "OK26",
        OccurredAt = new DateTime(2026, 9, 30, 21, 0, 0, DateTimeKind.Utc),
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
        private readonly List<(DateOnly Date, decimal Hours)> _entries = new();
        public IReadOnlyList<(DateOnly Date, decimal Hours)> SentEntries { get { lock (_entries) return _entries.ToList(); } }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("entries", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                lock (_entries)
                    foreach (var e in arr.EnumerateArray())
                        _entries.Add((DateOnly.Parse(e.GetProperty("date").GetString()!), e.GetProperty("hours").GetDecimal()));
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
