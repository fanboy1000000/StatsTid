using StatsTid.Backend.Api.Endpoints;

namespace StatsTid.Tests.Unit.Endpoints;

/// <summary>
/// S136 / TASK-13604 (ADR-040 D1) — unit pins on the PURE guard predicates behind the two admin
/// employment-date PUTs: <see cref="EmploymentDateEndpoints.IsRehireStartDateMove"/> (the D1
/// re-hire signature — start moved past a CLOSED spell's recorded end; settlement-INDEPENDENT by
/// construction, the predicate takes no settlement input) and
/// <see cref="EmploymentDateEndpoints.IsInvertedWindow"/> (the cross-field end&lt;start check —
/// before S136 nothing anywhere validated the two dates against each other). The HTTP /
/// transactional / lock / strand-guard behavior is pinned in the regression class
/// <c>Settlement.EmploymentDateGuardTests</c>.
/// </summary>
public class EmploymentDateGuardLogicTests
{
    private static readonly DateOnly Today = new(2026, 6, 10);

    // ────────────────── IsRehireStartDateMove — the ADR-040 D1 re-hire signature ──────────────────

    /// <summary>The headline: the recorded end is set and PASSED (closed spell), and the new
    /// start lies strictly after it — the one edit that would overwrite a completed employment's
    /// record. The predicate takes NO settlement input: settlement-independence is structural.</summary>
    [Fact]
    public void Rehire_StartAfterPassedEnd_IsRehireSignature()
    {
        Assert.True(EmploymentDateEndpoints.IsRehireStartDateMove(
            newStartDate: new DateOnly(2026, 3, 1),
            recordedEndDate: new DateOnly(2025, 12, 31),
            copenhagenToday: Today));
    }

    /// <summary>"Passed" boundary mirrors the R1 lifecycle: the end date is the LAST day
    /// employed, so end == today is NOT passed (spell still open — no re-hire signature),
    /// end == yesterday IS passed.</summary>
    [Fact]
    public void Rehire_PassedBoundary_EndToday_False_EndYesterday_True()
    {
        Assert.False(EmploymentDateEndpoints.IsRehireStartDateMove(
            newStartDate: Today.AddDays(5), recordedEndDate: Today, copenhagenToday: Today));

        Assert.True(EmploymentDateEndpoints.IsRehireStartDateMove(
            newStartDate: Today.AddDays(5), recordedEndDate: Today.AddDays(-1), copenhagenToday: Today));
    }

    /// <summary>An UNPASSED (future) end with a start after it is an inverted window (the 422
    /// class), never the re-hire signature — the spell is not closed yet.</summary>
    [Fact]
    public void Rehire_StartAfterUnpassedFutureEnd_False()
    {
        Assert.False(EmploymentDateEndpoints.IsRehireStartDateMove(
            newStartDate: new DateOnly(2027, 6, 1),
            recordedEndDate: new DateOnly(2027, 1, 31),
            copenhagenToday: Today));
    }

    /// <summary>Same-spell corrections stay legal (ADR-040 D1): a new start ON the recorded end
    /// (one-day spell) or BEFORE it keeps the range overlapping the recorded spell — no
    /// signature, even when the spell is closed.</summary>
    [Fact]
    public void Rehire_SameSpellCorrection_StartOnOrBeforeClosedEnd_False()
    {
        var closedEnd = new DateOnly(2025, 12, 31); // passed relative to Today

        Assert.False(EmploymentDateEndpoints.IsRehireStartDateMove(
            newStartDate: closedEnd, recordedEndDate: closedEnd, copenhagenToday: Today));

        Assert.False(EmploymentDateEndpoints.IsRehireStartDateMove(
            newStartDate: closedEnd.AddYears(-1), recordedEndDate: closedEnd, copenhagenToday: Today));
    }

    /// <summary>NULL = unbounded (ADR-040 D2): clearing the start, or a spell with no recorded
    /// end, can never be a re-hire signature — the guard passes through on a NULL side.</summary>
    [Fact]
    public void Rehire_NullStartOrNullEnd_False()
    {
        Assert.False(EmploymentDateEndpoints.IsRehireStartDateMove(
            newStartDate: null, recordedEndDate: new DateOnly(2025, 12, 31), copenhagenToday: Today));

        Assert.False(EmploymentDateEndpoints.IsRehireStartDateMove(
            newStartDate: new DateOnly(2026, 3, 1), recordedEndDate: null, copenhagenToday: Today));
    }

    // ────────────────── IsInvertedWindow — the cross-field end<start check ──────────────────

    [Fact]
    public void Inverted_EndBeforeStart_True()
    {
        Assert.True(EmploymentDateEndpoints.IsInvertedWindow(
            start: new DateOnly(2026, 5, 1), end: new DateOnly(2026, 4, 30)));
    }

    /// <summary>start == end is a LEGAL one-day spell (end inclusive, ADR-040 D1) — not
    /// inverted. The DB CHECK (TASK-13601) pins the same boundary as end &gt;= start.</summary>
    [Fact]
    public void Inverted_StartEqualsEnd_False_OneDaySpellIsLegal()
    {
        var day = new DateOnly(2026, 5, 1);
        Assert.False(EmploymentDateEndpoints.IsInvertedWindow(start: day, end: day));
    }

    [Fact]
    public void Inverted_WellFormedWindow_False()
    {
        Assert.False(EmploymentDateEndpoints.IsInvertedWindow(
            start: new DateOnly(2026, 1, 1), end: new DateOnly(2026, 12, 31)));
    }

    /// <summary>NULL = unbounded (ADR-040 D2): a half-open or fully-open window is never
    /// inverted — both PUTs pass through when the other field is unset.</summary>
    [Fact]
    public void Inverted_NullEitherOrBothSides_False()
    {
        Assert.False(EmploymentDateEndpoints.IsInvertedWindow(start: null, end: new DateOnly(2026, 4, 30)));
        Assert.False(EmploymentDateEndpoints.IsInvertedWindow(start: new DateOnly(2026, 5, 1), end: null));
        Assert.False(EmploymentDateEndpoints.IsInvertedWindow(start: null, end: null));
    }

    // ────────────────── The precedence premise the endpoint's guard ORDER relies on ──────────────────

    /// <summary>A closed-spell re-hire move satisfies BOTH predicates (start after end is also
    /// an inverted window). This pin documents WHY the start-date PUT checks the re-hire guard
    /// FIRST: were the 422 evaluated first, the D1 409 would be dead code.</summary>
    [Fact]
    public void ClosedSpellRehireMove_SatisfiesBothPredicates_So409MustBeCheckedFirst()
    {
        var newStart = new DateOnly(2026, 3, 1);
        var closedEnd = new DateOnly(2025, 12, 31); // passed relative to Today

        Assert.True(EmploymentDateEndpoints.IsRehireStartDateMove(newStart, closedEnd, Today));
        Assert.True(EmploymentDateEndpoints.IsInvertedWindow(start: newStart, end: closedEnd));
    }
}
