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
    /// Dated fields (<c>part_time_fraction</c>, <c>position</c>) are sourced from
    /// <c>employee_profiles</c> with the end-exclusive predicate
    /// <c>effective_from &lt;= asOfDate AND (effective_to IS NULL OR effective_to &gt; asOfDate)</c>.
    /// <c>agreement_code</c> is sourced from <c>user_agreement_codes</c> with the
    /// same end-exclusive predicate per S34 / ADR-023 D2 option (b) — closes
    /// ADR-016 D10 retroactive-replay determinism for the 4th and final rule-
    /// engine input. <c>ok_version</c> is a PURE FUNCTION OF THE DATE
    /// (<c>OkVersionResolver.ResolveVersion(asOfDate)</c>, ADR-003) — S137 / ADR-040 D4
    /// moved that overlay INTO the implementation so every consumer is correct by
    /// construction (QUAL-147 closed; before S137 it was joined live from <c>users</c>).
    /// <c>employment_category</c> is the DATED <c>employee_profiles</c> column since S137 —
    /// and since S138 / TASK-13804 that cell ALONE: the column is NOT NULL and the S137
    /// COALESCE-to-<c>users</c> fail-safe is retired, because with the category editable per
    /// date the live column is only the cache of the row covering TODAY and falling back to it
    /// would mislabel a historical read. The one remaining live-joined sibling is <c>primary_org_id</c>
    /// (org/unit membership history is a named follow-up program, ADR-040 D4 tail).
    /// </summary>
    Task<EmploymentProfile?> GetByEmployeeIdAtAsync(
        string employeeId, DateOnly asOfDate, CancellationToken ct = default);
}
