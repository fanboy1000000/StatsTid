namespace StatsTid.SharedKernel.Interfaces;

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
}
