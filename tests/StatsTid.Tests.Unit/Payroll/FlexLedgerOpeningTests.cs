using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using Xunit;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S146 / TASK-14606 (QUAL-188; AC-C3 (h); rulings R-14 as amended by R-28, R-47) — one fact per case of the pure
/// function <see cref="FlexLedgerOpening.Derive"/> that gives a paid month its opening flex balance.
///
/// <para>
/// <b>Plain language.</b> Flex is the running balance of hours above or below the norm. A month opens at the
/// previous month's recorded closing (CHAINED); the first month the system ever pays opens at 0 (LEDGER_START); a
/// month after a gap in employment opens at 0 again (REHIRE); a month before the first paid month cannot be
/// exported (refused, naming the first paid month) and, if it only exists as a raw record, its flex is not tracked
/// (UNTRACKED). The expected values below are transcribed from the refinement's R-14 table (rev 3.2, C2) and R-28,
/// never read off the code.
/// </para>
///
/// <para>
/// <b>Kind: N for every fact</b> — each names the type this sprint creates. RED is proven by a named MUTATION of the
/// pure core in a throwaway checkout (the table is in the TASK-14606 report), never by reverting. Calendar anchors:
/// 1 June 2026 is a Monday; 31 May 2026 is a Sunday.
/// </para>
/// </summary>
public class FlexLedgerOpeningTests
{
    private static readonly DateOnly Mar = new(2026, 3, 1);
    private static readonly DateOnly Apr = new(2026, 4, 1);
    private static readonly DateOnly May = new(2026, 5, 1);
    private static readonly DateOnly Jun = new(2026, 6, 1);

    private static FlexLedgerRecord Ledger(DateOnly month, decimal opening, decimal closing, string provenance) =>
        new(month.Year, month.Month, opening, closing, provenance);

    private static FlexLedgerRecord Raw(DateOnly month) => new(month.Year, month.Month, null, null, null);

    private static IReadOnlyList<EmploymentWindow> Continuous =>
        new[] { new EmploymentWindow(new DateOnly(2025, 1, 1), null) };

    // ---- case 1 -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Case 1 — M is a LEDGER_START record: the start is never re-derived, so a re-run or correction keeps the opening
    /// the record holds (here 7.5, not the 0 that case 2 would give). Kind N. Red conditions: the Balance is 7.5 and the
    /// provenance LEDGER_START and LedgerStart is M; a core that re-derives (case 2 → 0) or throws trips it.
    /// </summary>
    [Fact]
    public void Case1_LedgerStartRecordOfM_KeepsItsOpening()
    {
        var records = new[] { Ledger(Jun, 7.5m, 9m, FlexLedgerOpening.LedgerStart) };

        var opening = FlexLedgerOpening.Derive(Jun, records, null).Opening!;

        Assert.Equal(7.5m, opening.Balance);
        Assert.Equal("LEDGER_START", opening.Provenance);
        Assert.Equal(Jun, opening.LedgerStart);
    }

    // ---- case 2 -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Case 2 — the ledger is empty: the first month the system pays opens at 0, LEDGER_START, whatever month it is.
    /// Raw records do not count (raw history never dead-ends), including a raw record of M itself (the correction route's
    /// "raw-recorded month through /recalculate"). Kind N. Red conditions: Balance 0 and LEDGER_START for both inputs; a core
    /// that counts raw records as ledger records refuses or goes UNTRACKED/CHAINED.
    /// </summary>
    [Fact]
    public void Case2_EmptyLedger_RawHistoryOnly_IsLedgerStartZero()
    {
        var raw = new[] { Raw(Mar), Raw(Apr), Raw(Jun) };

        var onRawM = FlexLedgerOpening.Derive(Jun, raw, null).Opening!;
        var onUnrecordedM = FlexLedgerOpening.Derive(May, raw, null).Opening!;

        Assert.Equal((0m, "LEDGER_START"), (onRawM.Balance, onRawM.Provenance));
        Assert.Equal((0m, "LEDGER_START"), (onUnrecordedM.Balance, onUnrecordedM.Provenance));
        Assert.Equal(Jun, onRawM.LedgerStart);
    }

    // ---- case 3 -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Case 3a — M precedes the ledger start S and has no record at all: a first export of an earlier month is refused
    /// 409 payroll-export-out-of-order, naming S (June here). There is no route input (R-28). Kind N. Red conditions: Opening is
    /// null; Refusal.Kind is the out-of-order kind and Month is S; a core that opens it (LEDGER_START or UNTRACKED) or names
    /// M instead of S trips it.
    /// </summary>
    [Fact]
    public void Case3a_UnrecordedMonthBeforeLedgerStart_RefusesOutOfOrderNamingS()
    {
        var records = new[] { Ledger(Jun, 0m, 1m, FlexLedgerOpening.LedgerStart) };

        var decision = FlexLedgerOpening.Derive(Apr, records, Continuous);

        Assert.Null(decision.Opening);
        Assert.Equal("payroll-export-out-of-order", decision.Refusal!.Kind);
        Assert.Equal(Jun, decision.Refusal.Month);
    }

    /// <summary>
    /// Case 3b — M precedes S but has a raw record (a re-run or a correction): opens at 0, UNTRACKED, nothing stored. Kind N.
    /// Red conditions: Balance 0, provenance UNTRACKED, LedgerStart = S (June); a core that refuses (as 3a) or chains trips it.
    /// </summary>
    [Fact]
    public void Case3b_RawMonthBeforeLedgerStart_IsUntrackedZero()
    {
        var records = new[] { Raw(Apr), Ledger(Jun, 0m, 1m, FlexLedgerOpening.LedgerStart) };

        var decision = FlexLedgerOpening.Derive(Apr, records, Continuous);

        Assert.Null(decision.Refusal);
        Assert.Equal(0m, decision.Opening!.Balance);
        Assert.Equal("UNTRACKED", decision.Opening.Provenance);
        Assert.True(decision.Opening.IsUntracked);
        Assert.Equal(Jun, decision.Opening.LedgerStart);
    }

    // ---- case 4 -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Case 4a — M &gt; S, P employed and a ledger record: M is CHAINED from P's recorded CLOSING (9.5, not P's opening 4).
    /// Kind N. Red conditions: Balance 9.5 and CHAINED; a core that chains from P's opening, or from the latest record
    /// rather than P, trips it (the chain's older records carry other closings).
    /// </summary>
    [Fact]
    public void Case4a_PreviousMonthEmployedAndRecorded_ChainsFromItsClosing()
    {
        var records = new[]
        {
            Ledger(Mar, 0m, 3m, FlexLedgerOpening.LedgerStart),
            Ledger(Apr, 3m, 4m, FlexLedgerOpening.Chained),
            Ledger(May, 4m, 9.5m, FlexLedgerOpening.Chained),
        };

        var opening = FlexLedgerOpening.Derive(Jun, records, Continuous).Opening!;

        Assert.Equal(9.5m, opening.Balance);
        Assert.Equal("CHAINED", opening.Provenance);
        Assert.Equal(Mar, opening.LedgerStart);
    }

    /// <summary>
    /// Case 4b (no record of P) — P is employed but has no export record: refused 409 payroll-prior-month-not-recorded,
    /// naming P (May), with the "export it first" message. Kind N. Red conditions: Opening null; kind, Month = May, and the
    /// sentence says to EXPORT that month first (and not to correct it); a core that opens at 0 (REHIRE) trips it.
    /// </summary>
    [Fact]
    public void Case4b_PreviousMonthEmployedButUnrecorded_RefusesNamingP_ExportFirst()
    {
        var records = new[] { Ledger(Mar, 0m, 3m, FlexLedgerOpening.LedgerStart) };

        var decision = FlexLedgerOpening.Derive(Jun, records, Continuous);

        Assert.Null(decision.Opening);
        Assert.Equal("payroll-prior-month-not-recorded", decision.Refusal!.Kind);
        Assert.Equal(May, decision.Refusal.Month);
        Assert.Contains("Export that month first", decision.Refusal.ErrorSentence);
        Assert.DoesNotContain("Correct that month first", decision.Refusal.ErrorSentence);
    }

    /// <summary>
    /// Case 4b (P has a raw record) — the same refusal, naming P, with the "correct it first" message. Kind N. Red
    /// conditions: the sentence says to CORRECT that month first (and not to export it); a core that gives one message for
    /// both, or that treats a raw P as a ledger record, trips it.
    /// </summary>
    [Fact]
    public void Case4b_PreviousMonthEmployedButRaw_RefusesNamingP_CorrectFirst()
    {
        var records = new[] { Ledger(Mar, 0m, 3m, FlexLedgerOpening.LedgerStart), Raw(May) };

        var decision = FlexLedgerOpening.Derive(Jun, records, Continuous);

        Assert.Null(decision.Opening);
        Assert.Equal("payroll-prior-month-not-recorded", decision.Refusal!.Kind);
        Assert.Equal(May, decision.Refusal.Month);
        Assert.Contains("Correct that month first", decision.Refusal.ErrorSentence);
        Assert.DoesNotContain("Export that month first", decision.Refusal.ErrorSentence);
    }

    /// <summary>
    /// Case 4c — P has no employed day: a new stretch of employment, REHIRE at 0 (even though an older closing exists).
    /// Kind N. Red conditions: Balance 0 and REHIRE; a core that refuses (4b) or chains from the older record trips it.
    /// </summary>
    [Fact]
    public void Case4c_PreviousMonthNotEmployed_IsRehireZero()
    {
        var records = new[] { Ledger(Mar, 0m, 3m, FlexLedgerOpening.LedgerStart) };
        // Employed through April 2026, then again from June: May holds no employed day.
        var windows = new[]
        {
            new EmploymentWindow(new DateOnly(2025, 1, 1), new DateOnly(2026, 4, 30)),
            new EmploymentWindow(new DateOnly(2026, 6, 1), null),
        };

        var opening = FlexLedgerOpening.Derive(Jun, records, windows).Opening!;

        Assert.Equal(0m, opening.Balance);
        Assert.Equal("REHIRE", opening.Provenance);
    }

    // ---- EmployedIn (R-28) --------------------------------------------------------------------------------------

    /// <summary>
    /// R-28 / AC-C3 (e) — June 2026 opens on a MONDAY, so the F1 "context days" read finds no May day in its opening week;
    /// but under continuous employment May IS employed, so June is CHAINED, never REHIRE. Kind N. Red conditions:
    /// EmployedIn(May) is true and June derives CHAINED from May's closing 2; a core that tests only the opening week, or
    /// that treats "no context days" as "not employed", answers REHIRE.
    /// </summary>
    [Fact]
    public void EmployedIn_MondayStartMonth_UnderContinuousEmployment_IsTrue()
    {
        Assert.Equal(DayOfWeek.Monday, Jun.DayOfWeek);
        Assert.True(FlexLedgerOpening.EmployedIn(May, Continuous));

        var records = new[]
        {
            Ledger(Mar, 0m, 3m, FlexLedgerOpening.LedgerStart),
            Ledger(May, 3m, 2m, FlexLedgerOpening.Chained),
        };
        var opening = FlexLedgerOpening.Derive(Jun, records, Continuous).Opening!;

        Assert.Equal((2m, "CHAINED"), (opening.Balance, opening.Provenance));
    }

    /// <summary>
    /// R-28 — a spell that ENDS INSIDE P still makes P employed (read over [P.start, M.end]): ending 10 May, May is
    /// employed (June is not), and a June record derives CHAINED from May's closing. Kind N. Red conditions:
    /// EmployedIn(May) true, EmployedIn(June) false, and CHAINED 4; a core that needs the spell to cover all of P, or to
    /// reach M, answers REHIRE or false.
    /// </summary>
    [Fact]
    public void EmployedIn_PreviousMonth_SpellEndsInP_IsTrue()
    {
        var windows = new[] { new EmploymentWindow(new DateOnly(2025, 1, 1), new DateOnly(2026, 5, 10)) };

        Assert.True(FlexLedgerOpening.EmployedIn(May, windows));
        Assert.False(FlexLedgerOpening.EmployedIn(Jun, windows));

        var records = new[]
        {
            Ledger(Mar, 0m, 3m, FlexLedgerOpening.LedgerStart),
            Ledger(May, 3m, 4m, FlexLedgerOpening.Chained),
        };
        var opening = FlexLedgerOpening.Derive(Jun, records, windows).Opening!;

        Assert.Equal((4m, "CHAINED"), (opening.Balance, opening.Provenance));
    }

    /// <summary>
    /// R-37 — no window resolver (windows null) means every day is employed; an empty window list means none is. Kind N. Red
    /// conditions: EmployedIn true on null windows and false on an empty list; a core that reads null as "never employed"
    /// trips the first assertion.
    /// </summary>
    [Fact]
    public void EmployedIn_NullWindows_EveryMonthIsEmployed()
    {
        Assert.True(FlexLedgerOpening.EmployedIn(May, null));
        Assert.False(FlexLedgerOpening.EmployedIn(May, Array.Empty<EmploymentWindow>()));
    }

    /// <summary>
    /// R-28 / R-36 — the windows are read over [P.start, M.end]. Kind N. Red conditions: for June 2026 the range is exactly
    /// 2026-05-01 .. 2026-06-30, and for January the previous month is the previous year's December.
    /// </summary>
    [Fact]
    public void WindowsReadRange_IsPreviousMonthStartToThisMonthEnd()
    {
        Assert.Equal((new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 30)), FlexLedgerOpening.WindowsReadRange(new DateOnly(2026, 6, 17)));
        Assert.Equal((new DateOnly(2025, 12, 1), new DateOnly(2026, 1, 31)), FlexLedgerOpening.WindowsReadRange(new DateOnly(2026, 1, 1)));
    }

    // ---- IsWholeCalendarMonth (R-47) ----------------------------------------------------------------------------

    /// <summary>
    /// R-47 — only the 1st through the last day of one calendar month is a whole month (the only period a ledger write may be
    /// made for). Kind N. Red conditions: true for 1..last (incl. a leap and a non-leap February, December 9999 without
    /// throwing); false for 1..last-1, 2..last, a leap February ending on the 28th, and a span across two months.
    /// </summary>
    [Theory]
    [InlineData(2026, 6, 1, 2026, 6, 30, true)]
    [InlineData(2026, 5, 1, 2026, 5, 31, true)]
    [InlineData(2024, 2, 1, 2024, 2, 29, true)]
    [InlineData(2026, 2, 1, 2026, 2, 28, true)]
    [InlineData(9999, 12, 1, 9999, 12, 31, true)]
    [InlineData(2026, 6, 1, 2026, 6, 29, false)]
    [InlineData(2026, 6, 2, 2026, 6, 30, false)]
    [InlineData(2024, 2, 1, 2024, 2, 28, false)]
    [InlineData(2026, 6, 1, 2026, 7, 31, false)]
    [InlineData(2026, 6, 15, 2026, 7, 14, false)]
    [InlineData(2025, 12, 1, 2026, 1, 31, false)]
    public void IsWholeCalendarMonth_OnlyFirstThroughLastDayOfOneMonth(
        int sy, int sm, int sd, int ey, int em, int ed, bool expected)
    {
        Assert.Equal(expected, FlexLedgerOpening.IsWholeCalendarMonth(new DateOnly(sy, sm, sd), new DateOnly(ey, em, ed)));
    }

    // ---- refusal as data, damaged input -------------------------------------------------------------------------

    /// <summary>
    /// The refusal turns into the host's exception with the month the refusal named (yyyy-MM) and the fixed kind; the
    /// opening passes through OpeningOrThrow. Kind N. Red conditions: MonthText "2026-05", Kind unchanged; a refusal thrown
    /// by OpeningOrThrow, an opening returned.
    /// </summary>
    [Fact]
    public void OpeningOrThrow_RefusalThrowsTheNamedException_OpeningPassesThrough()
    {
        var refused = FlexLedgerOpening.Derive(Jun, new[] { Ledger(Mar, 0m, 3m, FlexLedgerOpening.LedgerStart) }, Continuous);
        var ex = Assert.Throws<FlexLedgerRefusalException>(() => refused.OpeningOrThrow("emp-1"));
        Assert.Equal("payroll-prior-month-not-recorded", ex.Kind);
        Assert.Equal("2026-05", ex.MonthText);

        var open = FlexLedgerOpening.Derive(Jun, Array.Empty<FlexLedgerRecord>(), null).OpeningOrThrow("emp-1");
        Assert.Equal("LEDGER_START", open.Provenance);
    }

    /// <summary>
    /// Damaged input is never papered over: two records of one month, and a ledger whose start month is not LEDGER_START,
    /// throw InvalidOperationException. Kind N. Red conditions: both throw; a core that picks one of two records, or that
    /// opens a damaged ledger, trips it.
    /// </summary>
    [Fact]
    public void DamagedInput_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FlexLedgerOpening.Derive(Jun, new[] { Raw(May), Raw(May) }, null));

        // M = S (the earliest ledger month) but its record says CHAINED: a damaged ledger.
        Assert.Throws<InvalidOperationException>(() =>
            FlexLedgerOpening.Derive(Jun, new[] { Ledger(Jun, 1m, 2m, FlexLedgerOpening.Chained) }, null));
    }
}

/// <summary>
/// S146 / TASK-14606 (AC-C3 (g), R-47) — the two Unit-level facts about the calculation service around the pure
/// function: "never silent" for the ledger reader, and the correction service's whole-month invariant.
///
/// <para>
/// <b>Plain language.</b> (g) If the host forgot to register the ledger reader, a calculation must not quietly open the
/// month at 0 (that is the very defect QUAL-188 removes): it throws and names the registration. R-47: a correction is
/// of one whole calendar month; the route refuses a partial period with 400 before anything is read, and the service
/// itself refuses it as a programmer error, because a partial period would overwrite the month's baseline lines and
/// store a part-month's closing balance as the month's.
/// </para>
/// </summary>
public class FlexLedgerNeverSilentTests
{
    private const string Emp = "EMP-S146-NEVERSILENT";

    /// <summary>Counts reads, so a fact can say "before any read".</summary>
    private sealed class CountingHours : IRecordedHoursReader
    {
        public int Reads { get; private set; }

        public Task<RecordedHours> ReadCurrentAsync(string employeeId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(new RecordedHours(Array.Empty<TimeEntryProjectionRow>(), Array.Empty<AbsenceProjectionRow>()));
        }

        public Task<RecordedHours> ReadByEventIdsAsync(string employeeId, IReadOnlyList<Guid> entryEventIds, IReadOnlyList<Guid> absenceEventIds, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(new RecordedHours(Array.Empty<TimeEntryProjectionRow>(), Array.Empty<AbsenceProjectionRow>()));
        }
    }

    private sealed class Classifications : IRuleClassificationProvider
    {
        public IReadOnlyList<StatsTid.SharedKernel.Segmentation.RuleClassification> GetClassifications() =>
            EmploymentWindowPcsFixture.RuleSet;
    }

    private static PeriodCalculationService BuildWithoutLedgerReader(
        EmploymentWindowPcsFixture.RecordingRuleEngine engine, CountingHours hours) =>
        new(
            new EmploymentWindowPcsFixture.SingleClientHttpFactory(engine),
            mappingService: null!,
            new InMemorySegmentManifestStore(),
            EmploymentWindowPcsFixture.Configuration(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new Classifications(),
            hoursReader: hours,
            priorPeriodApprovalReader: new EmploymentWindowPcsFixture.AlwaysApprovedPriorPeriods(),
            flexLedgerReader: null);

    /// <summary>
    /// AC-C3 (g) — a calculation of an employed month on a service built WITHOUT a ledger reader throws
    /// InvalidOperationException naming IFlexLedgerReader, before the hours read and before any rule call. Kind N (names the
    /// ledger-reader parameter this sprint adds). Red conditions: the exception type is exactly InvalidOperationException, its
    /// message names IFlexLedgerReader, and the hours reader and the engine were never touched; a service that defaults the
    /// missing reader to an empty ledger returns a successful LEDGER_START 0 result instead (RED: no exception). Unit, not
    /// Docker: no database is involved.
    /// </summary>
    [Fact]
    public async Task NoLedgerReader_EmployedMonth_Throws_NamesRegistration_BeforeAnyRead()
    {
        var engine = new EmploymentWindowPcsFixture.RecordingRuleEngine();
        var hours = new CountingHours();
        var pcs = BuildWithoutLedgerReader(engine, hours);

#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pcs.CalculateWithOutcomeAsync(
                EmploymentWindowPcsFixture.Profile(Emp), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));
#pragma warning restore CS0618

        Assert.Contains("IFlexLedgerReader", ex.Message);
        Assert.Equal(0, hours.Reads);
        Assert.Empty(engine.Calls);
    }

    /// <summary>
    /// R-47 — RetroactiveCorrectionService.RecalculateAsync called directly with a partial period (1-15 June) throws
    /// InvalidOperationException as its first statement: the calculation service was never reached (the recording ledger
    /// reader, the hours reader and the engine all show zero reads), and no database was opened (the connection factory
    /// points nowhere: opening it would throw a different exception type). Kind N (names the whole-month guard). Red
    /// conditions: exact type InvalidOperationException and the three zero counts; a service without the guard proceeds
    /// into the calculation (ledger reader read once, or a refusal of another type), tripping the counts or the type.
    /// </summary>
    [Fact]
    public async Task RecalculateAsync_PartialPeriod_Throws_BeforeAnyRead()
    {
        var engine = new EmploymentWindowPcsFixture.RecordingRuleEngine();
        var hours = new CountingHours();
        var ledger = new InMemoryFlexLedgerReader();
        var pcs = EmploymentWindowPcsFixture.BuildPcs(engine, hoursReader: hours, flexLedgerReader: ledger);
        var factory = new StatsTid.Infrastructure.DbConnectionFactory("Host=nowhere.invalid;Port=1;Database=x;Username=x;Password=x;Timeout=1");
        var service = new RetroactiveCorrectionService(
            pcs,
            factory,
            outbox: null!,
            new StatsTid.Infrastructure.AuditProjectionRepository(factory),
            auditMapper: null!,
            new PayrollExportRecordRepository(factory),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RetroactiveCorrectionService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecalculateAsync(
                EmploymentWindowPcsFixture.Profile(Emp),
                new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15),
                reason: "partial", actorId: "admin"));

        Assert.Contains("whole calendar month", ex.Message);
        Assert.Equal(0, ledger.ReadCount);
        Assert.Equal(0, hours.Reads);
        Assert.Empty(engine.Calls);
    }
}
