using Microsoft.Extensions.Logging;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S141 / TASK-14114 — the payroll calculation path refuses a calculation it cannot ground in a
/// real employment record, and says SO BY NAME. Locally runnable (no DB, no Docker).
///
/// <para>
/// Plain-language, for a reader who is not the code's author: Sprint 141 lets HR schedule an
/// employment change ahead of time. A side effect nobody had written down is that an employee can
/// end up with <b>no record describing them today</b> — a record ended in September whose
/// replacement does not start until November leaves a hole in between. When a payroll calculation
/// lands in that hole, refusing to produce a figure is the RIGHT answer: a payroll amount derived
/// from a guessed agreement code or part-time fraction would put wrong money on a real wage line,
/// which is worse than producing nothing. What was wrong was that the refusal arrived as an
/// <i>anonymous crash</i>. Somebody reading the log could not tell a broken server from an
/// employee whose records have a gap — so nobody knew to go fix the records.
/// </para>
///
/// <para>
/// These pins hold three things that pull in different directions, so a future change cannot
/// quietly trade one for another:
/// <list type="bullet">
///   <item><b>It is NAMED.</b> The refusal logs <c>employment_record_gap</c> — the same name
///     <c>ComplianceEndpoints</c> uses for the same state (TASK-14105) — says WHICH record is
///     missing, and points at the HR follow-up "cannot register" list (HRP-015) where an
///     administrator actually repairs it.</item>
///   <item><b>It still FAILS CLOSED.</b> No fallback profile is substituted, the segment is not
///     skipped, and no rule is evaluated on a guess. The calculation still refuses.</item>
///   <item><b>No employment date leaks (ADR-040 D7).</b> This is the constraint that shapes the
///     code: the resolver's own exception embeds its as-of date in its message, and for a
///     starter's first employed segment that as-of date IS THE HIRE DATE. So the caught exception
///     is neither attached to the log nor wrapped as an inner exception, and the exception that
///     escapes is re-anchored on the caller's own period start. Diagnostics carry the employee id,
///     the manifest id and the caller's period — nothing date-shaped about the employment itself.
///     The tests below assert the hire date appears NOWHERE in what an operator can read.</item>
/// </list>
/// </para>
///
/// <para>
/// Scenario geometry is chosen so the two dates are distinguishable: the caller asks for all of
/// March 2026 (<c>2026-03-01 .. 2026-03-31</c>) while the employment window starts 2026-03-10.
/// The planner therefore emits a NOT_EMPLOYED prefix and an EMPLOYED segment whose
/// <c>StartDate</c> is the hire date, which is exactly the date that must not escape. A test that
/// used a windowless plan would pass vacuously, because there the segment start and the period
/// start are the same date.
/// </para>
/// </summary>
public sealed class EmploymentRecordGapDiagnosticsTests
{
    private const string EmployeeId = "EMP-S141-GAP";

    /// <summary>The hire date — the employment date that must never reach a log or a message.</summary>
    private static readonly DateOnly Hire = new(2026, 3, 10);

    /// <summary>Every rendering of <see cref="Hire"/> an operator could plausibly read.</summary>
    private static readonly string[] HireDateRenderings =
    {
        "2026-03-10",
        "03/10/2026",
        "10-03-2026",
        "10/03/2026",
    };

    // ═════════════════════════════════════════════════════════════════════
    // Path 1 — the resolver returns NULL (no employee_profiles row covers the date)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The shape S141 newly makes reachable: HR ended one record and scheduled the next to start
    /// later, so nothing covers the segment. The calculation must refuse, name itself, and
    /// evaluate nothing.
    /// </summary>
    [Fact]
    public async Task NoProfileRowCoversSegment_RefusesWithNamedCondition_AndEvaluatesNothing()
    {
        var plan = Plan(EmployeeId, Mar01, Mar31, new[] { new EmploymentWindow(Hire, null) });
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[0].EmploymentStatus);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, plan.Segments[1].EmploymentStatus);
        // The premise of the leak test: the segment PCS resolves at is the hire date, and the
        // caller's period start is a DIFFERENT date. Without this the assertions below are vacuous.
        Assert.Equal(Hire, plan.Segments[1].StartDate);
        Assert.NotEqual(Hire, plan.PeriodStart);

        var engine = new RecordingRuleEngine();
        var logger = new CapturingLogger<PeriodCalculationService>();
        var pcs = BuildPcsWith(engine, logger, GapProfileResolver.ReturningNull());

        var ex = await Assert.ThrowsAsync<EmployeeProfileNotFoundException>(() =>
            pcs.CalculateWithOutcomeAsync(
                plan, Profile(EmployeeId), WeekdayEntries(EmployeeId, Mar01, Mar31),
                Array.Empty<AbsenceEntry>(), previousFlexBalance: 0m));

        // FAIL CLOSED: nothing was evaluated, so no figure could have been produced on a guess.
        Assert.Empty(engine.Calls);

        // NAMED: exactly one Error line, carrying the condition name, the missing record, and the
        // place a human resolves it.
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("employment_record_gap", error.Message, StringComparison.Ordinal);
        Assert.Contains("employee_profiles", error.Message, StringComparison.Ordinal);
        Assert.Contains("HRP-015", error.Message, StringComparison.Ordinal);
        // The identifiers an operator needs to act: the employee and the caller's own period.
        Assert.Contains(EmployeeId, error.Message, StringComparison.Ordinal);
        Assert.Contains(plan.ManifestId.ToString(), error.Message, StringComparison.Ordinal);

        // ADR-040 D7: the employment date is nowhere an operator can read it.
        AssertNoHireDateAnywhere(logger, ex);

        // The escaping exception is anchored on CALLER INPUT, not on the segment's hire date.
        Assert.Equal(plan.PeriodStart, ex.AsOfDate);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Path 2 — the resolver THROWS (profile row exists, agreement-code row does not)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The same underlying state reported the resolver's other way: <c>employee_profiles</c> covers
    /// the date but <c>user_agreement_codes</c> does not, which the resolver signals by throwing
    /// with <c>asOfDate == segment.StartDate</c> — the hire date. This is the RED pin: before
    /// TASK-14114 that exception escaped PCS verbatim, carrying the hire date in its message into
    /// exception logs and any detailed error body, and arriving unnamed. It must now land on the
    /// SAME named condition and be re-anchored on the caller's period.
    /// </summary>
    [Fact]
    public async Task NoAgreementCodeRowCoversSegment_LandsOnSameNamedCondition_WithoutLeakingHireDate()
    {
        var plan = Plan(EmployeeId, Mar01, Mar31, new[] { new EmploymentWindow(Hire, null) });
        Assert.Equal(Hire, plan.Segments[1].StartDate);

        var engine = new RecordingRuleEngine();
        var logger = new CapturingLogger<PeriodCalculationService>();
        var resolver = GapProfileResolver.Throwing();
        var pcs = BuildPcsWith(engine, logger, resolver);

        var ex = await Assert.ThrowsAsync<EmployeeProfileNotFoundException>(() =>
            pcs.CalculateWithOutcomeAsync(
                plan, Profile(EmployeeId), WeekdayEntries(EmployeeId, Mar01, Mar31),
                Array.Empty<AbsenceEntry>(), previousFlexBalance: 0m));

        // The resolver really was asked at the hire date (the correct as-of, ADR-040 D10) — so the
        // date genuinely was in play and its absence below is a property of the diagnostics, not
        // of the scenario.
        Assert.Equal(new[] { Hire }, resolver.AsOfDates);

        // ── The D7 assertions come FIRST: they are the invariant, and ordering them ahead of the
        //    naming assertions makes a regression report the leak rather than the missing log line.
        // The escaping exception is re-anchored on CALLER INPUT. Before TASK-14114 the resolver's
        // own exception propagated untouched, so this read the HIRE DATE.
        Assert.Equal(plan.PeriodStart, ex.AsOfDate);

        // ...and therefore the hire date is in no operator-readable surface.
        AssertNoHireDateAnywhere(logger, ex);

        // The caught exception must NOT be attached to the log record — attaching it would render
        // its hire-date-bearing message straight into the log this site exists to protect.
        Assert.All(logger.Entries, e => Assert.Null(e.Exception));

        // Nor wrapped as an inner exception that a handler could later render.
        Assert.Null(ex.InnerException);

        // FAIL CLOSED: nothing was evaluated.
        Assert.Empty(engine.Calls);

        // ONE CONDITION, ONE NAME — same name as the null path, narrowed to the missing record.
        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("employment_record_gap", error.Message, StringComparison.Ordinal);
        Assert.Contains("user_agreement_codes", error.Message, StringComparison.Ordinal);
        Assert.Contains("HRP-015", error.Message, StringComparison.Ordinal);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Counter-test — the pins above can fail
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Falsifiability guard: with a resolver that DOES cover the segment, the same plan calculates
    /// normally and logs no gap at all. Without this, the two pins above would still pass if PCS
    /// had been broken into refusing everything.
    /// </summary>
    [Fact]
    public async Task ProfileCoversSegment_NoGapLogged_AndCalculationProceeds()
    {
        var plan = Plan(EmployeeId, Mar01, Mar31, new[] { new EmploymentWindow(Hire, null) });

        var engine = new RecordingRuleEngine();
        var logger = new CapturingLogger<PeriodCalculationService>();
        var pcs = BuildPcsWith(engine, logger, new CountingProfileResolver());

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(EmployeeId), WeekdayEntries(EmployeeId, Mar01, Mar31),
            Array.Empty<AbsenceEntry>(), previousFlexBalance: 0m);

        Assert.True(outcome.Result.Success);
        Assert.NotEmpty(engine.Calls);
        Assert.DoesNotContain(logger.Entries, e =>
            e.Message.Contains("employment_record_gap", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Asserts the hire date appears in NO operator-readable surface: not in any log line, not in
    /// any exception attached to a log line, and not in the escaping exception's own rendering.
    /// </summary>
    private static void AssertNoHireDateAnywhere(
        CapturingLogger<PeriodCalculationService> logger, Exception escaped)
    {
        var surfaces = logger.Entries
            .Select(e => e.Message)
            .Concat(logger.Entries.Select(e => e.Exception?.ToString() ?? string.Empty))
            .Append(escaped.ToString())
            .ToList();

        foreach (var surface in surfaces)
        {
            foreach (var rendering in HireDateRenderings)
            {
                Assert.DoesNotContain(rendering, surface, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// Mirrors <c>EmploymentWindowPcsFixture.BuildPcs</c> exactly, except that the logger is
    /// injectable — the shared fixture hard-wires <c>NullLogger</c>, and the named log line IS the
    /// deliverable under test here. Constructed directly rather than by widening the shared
    /// fixture, so no other suite's harness changes.
    /// </summary>
    private static PeriodCalculationService BuildPcsWith(
        RecordingRuleEngine engine,
        ILogger<PeriodCalculationService> logger,
        IEmploymentProfileResolver profileResolver) =>
        new(
            new SingleClientHttpFactory(engine),
            // Never dereferenced: the stub rule engine returns no line items (and on the gap paths
            // it is never called at all), so the export mapper is never reached.
            mappingService: null!,
            new InMemoryEventStore(),
            // The projection insert sits inside PCS's own swallowing try/catch by design.
            connectionFactory: null!,
            Configuration(),
            logger,
            classificationProvider: new FixedRuleClassificationProvider(RuleSet),
            localAgreementProfileRepo: null,
            profileResolver: profileResolver,
            // PCS refuses a profile resolver without a window resolver; an unbounded window keeps
            // the typing decision with the PLAN the test built, not with this fake.
            employmentWindowResolver: new FakeWindowResolver(new EmploymentWindow(null, null)),
            employeeProfileRepo: null);

    /// <summary>
    /// <see cref="IEmploymentProfileResolver"/> fake standing in for an employee with a hole in
    /// their employment records, in each of the two ways the real resolver reports one.
    /// </summary>
    private sealed class GapProfileResolver : IEmploymentProfileResolver
    {
        private readonly bool _throw;
        private readonly List<DateOnly> _asOfDates = new();

        private GapProfileResolver(bool shouldThrow) => _throw = shouldThrow;

        /// <summary>No <c>employee_profiles</c> row covers the date — the resolver returns null.</summary>
        public static GapProfileResolver ReturningNull() => new(shouldThrow: false);

        /// <summary>A profile row covers the date but no <c>user_agreement_codes</c> row does —
        /// the resolver's documented data-integrity fail-loud, anchored on the as-of date it was
        /// asked about (which in this scenario is the hire date).</summary>
        public static GapProfileResolver Throwing() => new(shouldThrow: true);

        public IReadOnlyList<DateOnly> AsOfDates => _asOfDates;

        public Task<EmploymentProfile?> GetByEmployeeIdAtAsync(
            string employeeId, DateOnly asOfDate, CancellationToken ct = default)
        {
            _asOfDates.Add(asOfDate);
            if (_throw)
                throw new EmployeeProfileNotFoundException(employeeId, asOfDate);
            return Task.FromResult<EmploymentProfile?>(null);
        }
    }

    /// <summary>Local copy of the fixture's classification provider, which is private to it.</summary>
    private sealed class FixedRuleClassificationProvider : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public FixedRuleClassificationProvider(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }

    /// <summary>
    /// <see cref="ILogger{T}"/> that records the FORMATTED message and any attached exception —
    /// both halves matter, because attaching the caught exception is precisely the D7 mistake
    /// these tests exist to prevent.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

        private readonly List<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries) _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
        }
    }
}
