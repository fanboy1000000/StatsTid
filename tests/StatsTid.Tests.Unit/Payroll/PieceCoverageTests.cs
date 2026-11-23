using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S148 / TASK-14847-T (AC-PIL-i (1)-(5); QUAL-265) - payroll checks that a complete dated employment record covers the LAST
/// day of every piece it pays, not only the first. Locally runnable (no database, no Docker).
///
/// <para>
/// <b>In plain language.</b> Payroll cuts a month into pieces and decides which employment record pays each piece by looking
/// the record up on the piece's first day. If HR closes a profile partway through a piece while the person stays employed,
/// the first day is still covered and the rest of the piece would be paid on a profile that has ended, silently. The rule is
/// now that the record must cover the piece's last day too. If it does not, the month is refused by name
/// (<see cref="EmployeeProfileNotFoundException"/>, which the routes answer 422 <c>payroll-employment-record-gap</c>) and
/// nothing is calculated; inside a recording's turn on the pay-inputs lock the same finding is a changed input
/// (<see cref="PayInputsChangedException"/>, 409). A month with no break pays exactly as before.
/// </para>
///
/// <para>
/// <b>Seam.</b> The profile resolver is a per-date fake that covers the days a fact chooses. The fixture passes no profile
/// repository, so a fake's coverage can stop in the middle of a piece with no piece edge there, which is exactly the shape of
/// a profile close. March 2026 opens on a Sunday, so its opening-week context is Monday 23 to Saturday 28 February 2026, one
/// piece.
/// </para>
///
/// <para>
/// <b>Kind.</b> (1)-(4) are F (RED at <c>f493bb32</c>, which looks only at each piece's first day: the calculation succeeds
/// and the recheck passes, so <c>ThrowsAsync</c> fails with "No exception was thrown"). They name only types that exist
/// before TASK-14847. (5) is the control (R): green before and after.
/// </para>
/// </summary>
public sealed class PieceCoverageTests
{
    private const string EmployeeId = "EMP-S148-PIL-I";

    private static readonly DateOnly Feb23 = new(2026, 2, 23); // a Monday: the opening Monday of March's first week
    private static readonly DateOnly Feb28 = new(2026, 2, 28); // a Saturday: the last day of the context piece
    private static readonly DateOnly Mar20 = new(2026, 3, 20); // a Friday: the first day a closed record no longer covers

    /// <summary>A per-date profile resolver: the profile on the days <see cref="Covered"/> accepts, otherwise no profile (a
    /// missing <c>employee_profiles</c> row) or, with <see cref="ThrowWhenUncovered"/>, the resolver's own fail-loud for a
    /// profile row whose agreement-code or organisation row does not cover the day. Records every date it is asked.</summary>
    private sealed class DatedResolver : IEmploymentProfileResolver
    {
        private readonly List<DateOnly> _asOfDates = new();

        public DatedResolver(Func<DateOnly, bool> covered, bool throwWhenUncovered = false)
        {
            Covered = covered;
            ThrowWhenUncovered = throwWhenUncovered;
        }

        public Func<DateOnly, bool> Covered { get; set; }

        public bool ThrowWhenUncovered { get; }

        public IReadOnlyList<DateOnly> AsOfDates => _asOfDates;

        public Task<EmploymentProfile?> GetByEmployeeIdAtAsync(string employeeId, DateOnly asOfDate, CancellationToken ct = default)
        {
            _asOfDates.Add(asOfDate);
            if (Covered(asOfDate))
                return Task.FromResult<EmploymentProfile?>(Profile(employeeId));
            if (ThrowWhenUncovered)
                throw new EmployeeProfileNotFoundException(employeeId, asOfDate);
            return Task.FromResult<EmploymentProfile?>(null);
        }
    }

    private static PlannedCalculation MarchPlan() => Plan(EmployeeId, Mar01, Mar31, windows: null);

    /// <summary>The refusal is the named record gap, anchored on the caller's own period start (never a day of the
    /// employment record, ADR-040 D7), and no rule was called.</summary>
    private static void AssertRecordGap(EmployeeProfileNotFoundException ex, RecordingRuleEngine engine)
    {
        Assert.Equal(EmployeeId, ex.EmployeeId);
        Assert.Equal(Mar01, ex.AsOfDate);
        Assert.Null(ex.InnerException);
        Assert.Empty(engine.Calls);
    }

    /// <summary>
    /// <b>F (Unit).</b> AC-PIL-i (1). The profile covers March until Thursday 19 March 2026 and the person stays employed all
    /// month, so March is one segment whose first day (1 March) is covered and whose last day (31 March) is not. The
    /// calculation is refused as a record gap, nothing is evaluated, and the resolver was asked the segment's last day.
    /// <para><b>Red conditions.</b> At <c>f493bb32</c> only 1 March is asked, the profile is found, the calculation succeeds and
    /// <c>ThrowsAsync</c> fails with "No exception was thrown". A check that probed the first day twice, or the last day of a
    /// context piece only, leaves 31 March unasked and fails the same way.</para>
    /// </summary>
    [Fact]
    public async Task SegmentWhoseProfileEndsBeforeItsLastDay_RefusedAsRecordGap()
    {
        var engine = new RecordingRuleEngine();
        var resolver = new DatedResolver(d => d < Mar20);
        var pcs = BuildPcs(engine, profileResolver: resolver, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var ex = await Assert.ThrowsAsync<EmployeeProfileNotFoundException>(() =>
            pcs.CalculateWithOutcomeAsync(MarchPlan(), Profile(EmployeeId)));

        AssertRecordGap(ex, engine);
        Assert.Contains(Mar31, resolver.AsOfDates);
    }

    /// <summary>
    /// <b>F (Unit).</b> AC-PIL-i (2). The same month where the profile row stays but the agreement-code or organisation row
    /// stops on 19 March: the resolver does not return null, it throws its fail-loud for the uncovered day. That is the same
    /// refusal, by the same single probe of the last day.
    /// <para><b>Red conditions.</b> At <c>f493bb32</c> 31 March is never asked, so nothing throws and <c>ThrowsAsync</c> fails
    /// with "No exception was thrown". A probe that let the resolver's own exception escape would carry 31 March in
    /// <c>AsOfDate</c> and fail the anchoring assertion.</para>
    /// </summary>
    [Fact]
    public async Task SegmentWhoseAgreementCodeOrOrganisationEndsBeforeItsLastDay_RefusedAsRecordGap()
    {
        var engine = new RecordingRuleEngine();
        var resolver = new DatedResolver(d => d < Mar20, throwWhenUncovered: true);
        var pcs = BuildPcs(engine, profileResolver: resolver, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var ex = await Assert.ThrowsAsync<EmployeeProfileNotFoundException>(() =>
            pcs.CalculateWithOutcomeAsync(MarchPlan(), Profile(EmployeeId)));

        AssertRecordGap(ex, engine);
        Assert.Contains(Mar31, resolver.AsOfDates);
    }

    /// <summary>
    /// <b>F (Unit).</b> AC-PIL-i (3). The record covers Monday 23 February (the context piece's first day) and every day from
    /// 1 March, but not 24 to 28 February. The context piece, Monday 23 to Saturday 28 February, is refused although the whole
    /// month it feeds is covered.
    /// <para><b>Red conditions.</b> At <c>f493bb32</c> only 23 February and 1 March are asked: both covered, the calculation
    /// succeeds, and <c>ThrowsAsync</c> fails with "No exception was thrown". A check applied to segments only passes this
    /// month and fails the same way.</para>
    /// </summary>
    [Fact]
    public async Task ContextPieceWhoseRecordEndsBeforeItsLastDay_RefusedAsRecordGap()
    {
        var engine = new RecordingRuleEngine();
        var resolver = new DatedResolver(d => d == Feb23 || d >= Mar01);
        var pcs = BuildPcs(engine, profileResolver: resolver, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var ex = await Assert.ThrowsAsync<EmployeeProfileNotFoundException>(() =>
            pcs.CalculateWithOutcomeAsync(MarchPlan(), Profile(EmployeeId)));

        AssertRecordGap(ex, engine);
        Assert.Contains(Feb23, resolver.AsOfDates);
        Assert.Contains(Feb28, resolver.AsOfDates);
    }

    /// <summary>
    /// <b>F (Unit).</b> AC-PIL-i (4). Inside a recording's turn the pay-inputs recheck derives March's inputs again. The month
    /// is first calculated with a record that covers every day, and its frozen inputs are kept; the record then stops on
    /// 19 March (a profile closed while the person stays employed) and the recheck, through the derivation wrapper, refuses
    /// the month as <c>payroll-inputs-changed</c> with the export's sentence and never as the record gap, whose message names
    /// the employment.
    /// <para><b>Red conditions.</b> At <c>f493bb32</c> the fresh derivation finds only the first days, produces inputs equal to
    /// the frozen ones, and the recheck passes: <c>ThrowsAsync</c> fails with "No exception was thrown". Dropping the wrapper's
    /// <c>EmployeeProfileNotFoundException</c> arm lets the record gap escape and fails it on the exception type.</para>
    /// </summary>
    [Fact]
    public async Task RecheckFindsRecordEndedInsideAPiece_RefusedAsInputsChanged()
    {
        var engine = new RecordingRuleEngine();
        var resolver = new DatedResolver(_ => true);
        var pcs = BuildPcs(engine, profileResolver: resolver, hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));
        var outcome = await pcs.CalculateWithOutcomeAsync(MarchPlan(), Profile(EmployeeId));
        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        var used = Assert.IsType<PlanInputs>(outcome.PlanInputs);
        var callsBefore = engine.Calls.Count;

        resolver.Covered = d => d < Mar20; // HR closes the profile after the calculation

        var ex = await Assert.ThrowsAsync<PayInputsChangedException>(() =>
            pcs.RecheckPayInputsUnderLockAsync(
                Profile(EmployeeId), Mar01, Mar31, used, PeriodCalculationService.PayInputsRoute.Export));

        Assert.Equal(Mar01, ex.Month);
        Assert.Equal("2026-03", ex.MonthText);
        Assert.Equal(
            "The inputs this calculation used changed while it was being recorded; calculate the month again. " +
            "Nothing was exported and no export record was written.",
            ex.ErrorSentence);
        Assert.Equal(callsBefore, engine.Calls.Count); // the recheck calls no rule
    }

    /// <summary>
    /// <b>R (Unit; control).</b> AC-PIL-i (5). A record that covers every day gives March's pieces exactly as an unbroken
    /// timeline does: one context piece, Monday 23 to Saturday 28 February, and one piece for March, with no cut point added by
    /// the last-day check. The calculation succeeds.
    /// <para><b>Red conditions (kind R).</b> A check that cut pieces at the day it probed, or that refused a covered month,
    /// changes the piece list or the outcome and fails here. Green before and after TASK-14847, by design.</para>
    /// </summary>
    [Fact]
    public async Task CoveredThroughout_PiecesUnchanged()
    {
        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine, profileResolver: new DatedResolver(_ => true), hoursReader: WeekdayHours(EmployeeId, Mar01, Mar31));

        var outcome = await pcs.CalculateWithOutcomeAsync(MarchPlan(), Profile(EmployeeId));

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        var pieces = Assert.Single(engine.TimelineCalls).Pieces!;
        Assert.Equal(
            new[] { (Feb23, Feb28), (Mar01, Mar31) },
            pieces.Select(p => (p.From, p.To)).ToArray());
    }
}
