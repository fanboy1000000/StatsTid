using Npgsql;

namespace StatsTid.Infrastructure;

/// <summary>
/// Where an employee stands on HR's "missing an approver" lists (HRP-013) on one day. Decided by
/// <see cref="ApproverCoverage.ClassifyAsync"/>, the one rule both lists use.
/// </summary>
public enum ApproverCoverageKind
{
    /// <summary>The approval walk names someone for the day.</summary>
    Covered,

    /// <summary>The walk names nobody, and another person in the read reports to them on an open
    /// PRIMARY line: the top of a people hierarchy, which by design has no approver (ADR-027 D9).</summary>
    Root,

    /// <summary>The walk names nobody, and nobody in the read reports to them: no designated approver
    /// exists for their month.</summary>
    Orphan,
}

/// <summary>
/// HRP-013 — the ONE definition of covered, root and orphan behind both of HR's missing-approver
/// surfaces: the per-organisation roster card
/// (<see cref="ApprovalPeriodRepository.GetMedarbejderRosterForTreeAsync"/>, its <c>isRoot</c> and
/// <c>isOrphan</c>) and the cross-organisation roll-up
/// (<see cref="HrFollowUpApprovalReadRepository.GetOrphanEmployeesAsync"/>). Both call this, so they
/// agree by construction rather than by two copies of a rule happening to match (PAT-030).
///
/// <para><b>Covered</b> on <c>today</c> means the walk the approval routes use,
/// <see cref="ReportingLineRepository.ResolveDesignatedApproverAsync(IReportingLineDataSource, string, DateOnly, CancellationToken)"/>,
/// returns someone. The walk is called as it is, never re-expressed (RES-003): it honours an ACTING
/// line, keeps a stand-in the manager appointed before leaving (owner Q-B = (c)), skips a holder who
/// has left from the day after their last day although the 5-day grace keeps them switched on
/// (ADR-040, amendment 2026-11-03), climbs the chain, and never names the employee themselves
/// (FAIL-004). So nothing here can make a person covered by themselves, or covered where the walk
/// names nobody. The walk is not the whole approve authority: the approve routes also require the
/// named person to hold a leader role in the same Organisation, and a unit leader may approve where
/// the walk names nobody (<see cref="DesignatedApproverAuthorizer"/>); this read does not apply either.</para>
///
/// <para><b>"Someone's approver"</b> (R-19) means another member of <paramref name="population"/>
/// reports to them on an open PRIMARY line. A line from a person to themselves does not count. This is
/// the reading both surfaces used before "covered" came from the walk, kept because being a root is a
/// fact about the hierarchy, not about today's routing. Under "the walk names them for someone", a
/// director who appoints a stand-in for a holiday, or who leaves with nobody above, would turn from root
/// into orphan, and a stand-in with no approver of her own would drop off the list. Under "holds any
/// open PRIMARY line", lines from people outside the read would count too (a switched-off leaver's line
/// stays open), and finding them needs a read beyond the prefetch below.</para>
///
/// <para><b>Cost.</b> For each path prefix, the walk's inputs are prefetched in three set-based reads
/// (<see cref="PrefetchedReportingLineDataSource"/>). The sources are chained, so an id outside one
/// prefix is answered by the one before it and finally by live SQL. A walk that stays inside the
/// prefixes costs no further round-trip. Call it inside the caller's REPEATABLE READ snapshot (the
/// prefetch equals live SQL only there), with the same <paramref name="today"/> the caller reads its
/// rows at (PAT-028).</para>
/// </summary>
public static class ApproverCoverage
{
    /// <param name="orgPathPrefixes">Raw <c>organizations.materialized_path</c> prefixes the prefetch
    /// loads (escaped here). The empty string loads every organisation.</param>
    /// <param name="population">The read's employees, each classified once. "Someone's approver" is
    /// judged over this set.</param>
    /// <returns>One entry per id in <paramref name="population"/>.</returns>
    public static async Task<IReadOnlyDictionary<string, ApproverCoverageKind>> ClassifyAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction snapshot,
        ReportingLineRepository resolver,
        IReadOnlyCollection<string> orgPathPrefixes,
        IReadOnlyCollection<string> population,
        DateOnly today,
        CancellationToken ct)
    {
        IReportingLineDataSource source = resolver.CreateSqlDataSource(conn, snapshot);
        foreach (var pathPrefix in orgPathPrefixes)
        {
            source = await PrefetchedReportingLineDataSource.BuildAsync(
                conn, snapshot, EscapeLike(pathPrefix) + "%", today, source, ct);
        }

        var covered = new HashSet<string>(StringComparer.Ordinal);
        var someonesApprover = new HashSet<string>(StringComparer.Ordinal);
        foreach (var employeeId in population)
        {
            var (approverId, _, _) = await resolver.ResolveDesignatedApproverAsync(source, employeeId, today, ct);
            if (approverId is not null)
                covered.Add(employeeId);

            var primaryLine = await source.GetActiveLineAsync(employeeId, "PRIMARY", ct);
            if (primaryLine is not null && !string.Equals(primaryLine.ManagerId, employeeId, StringComparison.Ordinal))
                someonesApprover.Add(primaryLine.ManagerId);
        }

        var result = new Dictionary<string, ApproverCoverageKind>(population.Count, StringComparer.Ordinal);
        foreach (var employeeId in population)
        {
            result[employeeId] = covered.Contains(employeeId) ? ApproverCoverageKind.Covered
                : someonesApprover.Contains(employeeId) ? ApproverCoverageKind.Root
                : ApproverCoverageKind.Orphan;
        }
        return result;
    }

    /// <summary>Escapes the LIKE metacharacters (<c>\</c>, <c>%</c>, <c>_</c>) in a system-derived
    /// path so an org id holding <c>_</c> cannot widen the prefix; the prefetch's statements use
    /// <c>ESCAPE '\'</c>. The same text as <c>ApprovalPeriodRepository.EscapeLike</c>.</summary>
    private static string EscapeLike(string raw) => raw
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");
}
