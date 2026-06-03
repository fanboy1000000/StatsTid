namespace StatsTid.SharedKernel.Interfaces;

using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;

/// <summary>
/// S33 / TASK-3301 (ADR-023) — temporal resolver for the fully-hydrated
/// <see cref="EmploymentProfile"/> at a given <see cref="DateOnly"/>. This is the
/// Phase 4d-3 Part 2 cutover surface that replaces ad-hoc <c>EmploymentProfile</c>
/// construction at PCS / Compliance call sites with a single dated lookup so
/// historical replays are byte-stable under mid-period profile mutations
/// (ADR-016 D5b consumption-time-lookup, inherited).
/// </summary>
public interface IEmploymentProfileResolver
{
    /// <summary>
    /// Returns the fully-hydrated <see cref="EmploymentProfile"/> for the given
    /// employee as of the given date, or <c>null</c> if no dated row covers
    /// <paramref name="asOfDate"/>. Never throws on missing-row — caller decides
    /// fail-closed vs fallback semantic per ADR-023 D3.
    ///
    /// Dated fields (<c>weekly_norm_hours</c>, <c>part_time_fraction</c>, <c>position</c>)
    /// are sourced from <c>employee_profiles</c> with the end-exclusive predicate
    /// <c>effective_from &lt;= asOfDate AND (effective_to IS NULL OR effective_to &gt; asOfDate)</c>.
    /// <c>agreement_code</c> is sourced from <c>user_agreement_codes</c> with the
    /// same end-exclusive predicate per S34 / ADR-023 D2 option (b) — closes
    /// ADR-016 D10 retroactive-replay determinism for the 4th and final rule-
    /// engine input. Remaining sibling fields (<c>ok_version</c>,
    /// <c>employment_category</c>, <c>primary_org_id</c>) are joined live from
    /// <c>users</c> — none feeds replay-sensitive rule-engine logic.
    /// </summary>
    Task<EmploymentProfile?> GetByEmployeeIdAtAsync(
        string employeeId, DateOnly asOfDate, CancellationToken ct = default);

    /// <summary>
    /// S62 / TASK-6202 — returns the full part-time-fraction version history for
    /// <paramref name="employeeId"/> over the half-open date window
    /// <c>[<paramref name="from"/>, <paramref name="to"/>)</c>, so a caller can compute
    /// piecewise accrual (<see cref="StatsTid.SharedKernel.Calendar.AccrualMath.EarnedToDatePiecewise"/>,
    /// ADR-030 D8) in ONE query instead of an N+1 per-month dated-lookup loop.
    ///
    /// <para>Each returned <see cref="FractionPeriod"/> carries only
    /// <c>(effective_from, effective_to, part_time_fraction)</c> from a single-table
    /// <c>employee_profiles</c> scan — there is <b>no</b> <c>users</c> JOIN and <b>no</b>
    /// dated agreement-code lookup. Unlike the sibling <see cref="GetByEmployeeIdAtAsync"/>
    /// (which fail-loud-throws on a missing agreement-code row), fraction history needs none
    /// of that, so this method <b>never throws</b> on missing data.</para>
    ///
    /// <para><b>Row-overlap predicate.</b> Returns every period whose
    /// <c>[effective_from, effective_to)</c> span overlaps the window — i.e.
    /// <c>effective_from &lt; @to AND (effective_to IS NULL OR effective_to &gt; @from)</c>
    /// (note <c>effective_from &lt; @to</c>, NOT the point-in-time <c>&lt;= asOfDate</c> of
    /// the sibling) — ordered ascending by <c>effective_from</c> (end-exclusive
    /// <c>effective_to</c> per ADR-018 D8).</para>
    ///
    /// <para><b>Empty-history polarity is the caller's call (ADR-023 D3).</b> When no row
    /// overlaps the window this returns an <b>empty list</b> — never <c>null</c>, never a
    /// throw. The caller decides the empty-history semantic: Skema fail-closes; the
    /// Balance/series dashboard defaults to a full-time 1.0 fraction.</para>
    /// </summary>
    Task<IReadOnlyList<FractionPeriod>> GetFractionHistoryAsync(
        string employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
