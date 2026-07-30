using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S125 / TASK-12501 step 3b — an <see cref="IReportingLineDataSource"/> backed by three set-based
/// reads instead of one round-trip per question.
///
/// <para><b>What it is and is not.</b> It answers the resolver's four data questions from memory; it
/// does NOT re-implement the resolution rule. The R3 precedence, the FAIL-004 self-exclusion invariant
/// and the depth ceiling all still live in
/// <see cref="ReportingLineRepository.ResolveDesignatedApproverAsync(IReportingLineDataSource, string, DateOnly, CancellationToken)"/>
/// and execute the same branches in the same order — this class just hands the same facts back
/// faster. That distinction is the whole reason the projection can be made flat without forking
/// authorization.</para>
///
/// <para><b>Why loading the WHOLE Organisation is correct rather than lazy.</b> The escalation walk
/// climbs from a pending employee through up to ten managers, and the managers it reaches are not
/// known until it gets there. Prefetching only the pending set would miss them and silently change
/// resolutions. Loading every active line, every user's activity flag and every active vikar for the
/// Organisation is bounded (lines ≪ people ≪ rows already scanned by phase (1)) and closes that hole
/// by construction.</para>
///
/// <para><b>Correctness rests on the snapshot.</b> This is built inside the projection's REPEATABLE
/// READ transaction, so the prefetched rows are exactly the rows a per-question read would have
/// returned at any point in the same transaction. Outside a snapshot it would be a staleness
/// trade-off; inside one it is an equivalence. Lifetime is a single projection call.</para>
///
/// <para><b>Absence is never guessed.</b> Within the covered Organisation, a missing line or vikar
/// answers <c>null</c> exactly as the SQL would for an absent row. OUTSIDE it, the question is handed
/// to live SQL rather than answered from an empty map — see <see cref="_covered"/>, which exists
/// because guessing "no" there is wrong in the PERMISSIVE direction.</para>
/// </summary>
public sealed class PrefetchedReportingLineDataSource : IReportingLineDataSource
{
    // (employeeId, relationship) → the single active line. The DB's partial-unique indexes
    // (uq_reporting_line_active_primary / _acting) guarantee at most one, so a dictionary is faithful.
    private readonly Dictionary<(string EmployeeId, string Relationship), ReportingLine> _lines;
    private readonly Dictionary<string, bool> _userActive;
    // absent_approver_id → their single active vikar (uq_manager_vikar_active guarantees at most one).
    private readonly Dictionary<string, ManagerVikar> _vikarByApprover;

    /// <summary>
    /// The user ids this prefetch actually COVERS (the projected Organisation's members).
    ///
    /// <para><b>This set is the correctness keystone, not an optimisation.</b> Without it, "absent
    /// from the map" is ambiguous between "no such row" and "outside what I loaded", and the resolver
    /// reads the second as the first. That is not merely inaccurate — it is inaccurate in the
    /// PERMISSIVE direction, because the resolver SELECTS A WINNER among alternatives: a
    /// cross-Organisation vikar read as "inactive" is SKIPPED (R3b), the walk falls through to the
    /// in-Organisation PRIMARY manager, and that manager then passes the same-Organisation check and
    /// is ADMITTED. Live SQL would have resolved to the vikar and then DENIED on same-Organisation,
    /// admitting nobody.</para>
    ///
    /// <para>A cross-Organisation vikar is an explicitly defended-against planted-data threat
    /// (see <c>DesignatedApproverAuthorityTests</c>), so this is a real reopening of a closed hole,
    /// not a hypothetical. Found by the external review lens at the S125 close.</para>
    ///
    /// <para>Note the asymmetry with <see cref="PrefetchedAuthorityFacts"/>, which needs no such set:
    /// its facts only GATE a nominated actor, so an out-of-scope miss denies — and live SQL would also
    /// have denied, via same-Organisation. Only the resolver picks among candidates, so only the
    /// resolver can be made more permissive by a wrong "no".</para>
    /// </summary>
    private readonly HashSet<string> _covered;

    /// <summary>Live-SQL source consulted for anything OUTSIDE <see cref="_covered"/>. Misses are rare
    /// (cross-Organisation or planted rows only), so flatness in the pending count is preserved.</summary>
    private readonly IReportingLineDataSource _fallback;

    private PrefetchedReportingLineDataSource(
        Dictionary<(string, string), ReportingLine> lines,
        Dictionary<string, bool> userActive,
        Dictionary<string, ManagerVikar> vikarByApprover,
        HashSet<string> covered,
        IReportingLineDataSource fallback)
    {
        _lines = lines;
        _userActive = userActive;
        _vikarByApprover = vikarByApprover;
        _covered = covered;
        _fallback = fallback;
    }

    /// <summary>Statements this source cost to build — three, regardless of the pending count. Exposed
    /// so the perf guard can assert flatness rather than infer it from wall-clock.</summary>
    public const int BuildStatementCount = 3;

    /// <summary>
    /// Loads the resolver's inputs for every active user whose home Organisation sits within
    /// <paramref name="treeRootPathPrefix"/>, in three set-based reads. Must be called inside the
    /// caller's snapshot transaction.
    /// </summary>
    /// <param name="escapedPathPrefixParam">The caller's ALREADY-ESCAPED <c>LIKE</c> parameter
    /// (i.e. <c>EscapeLike(path) + "%"</c>). Taken pre-escaped deliberately: the repo already carries
    /// three copies of that helper, and adding a fourth here would be one more place for the
    /// metacharacter-escaping rule to drift.</param>
    /// <param name="fallback">Live-SQL source for ids outside the projected Organisation — see
    /// <see cref="_covered"/> for why an out-of-scope miss must NOT be answered from the map.</param>
    public static async Task<PrefetchedReportingLineDataSource> BuildAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string escapedPathPrefixParam, DateOnly asOf,
        IReportingLineDataSource fallback, CancellationToken ct)
    {
        var pathParam = escapedPathPrefixParam;

        // (1) Every ACTIVE reporting line whose EMPLOYEE is in the Organisation. The walk climbs to
        //     managers, whose own lines are included because they are Organisation members too (the
        //     same-Organisation invariant, ADR-027 D2).
        var lines = new Dictionary<(string, string), ReportingLine>();
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT rl.* FROM reporting_lines rl
            JOIN users u ON u.user_id = rl.employee_id
            JOIN organizations o ON o.org_id = u.primary_org_id
            WHERE rl.effective_to IS NULL
              AND rl.relationship IN ('PRIMARY', 'ACTING')
              AND o.materialized_path LIKE @pathPrefix ESCAPE '\'
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("pathPrefix", pathParam);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var line = ReportingLineRepository.MapReaderRow(reader);
                lines[(line.EmployeeId, line.Relationship)] = line;
            }
        }

        // (2) Activity for every user in the Organisation. Deliberately NOT filtered to is_active —
        //     the resolver asks "is this user active?" and must be able to hear "no".
        var userActive = new Dictionary<string, bool>(StringComparer.Ordinal);
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT u.user_id, u.is_active FROM users u
            JOIN organizations o ON o.org_id = u.primary_org_id
            WHERE o.materialized_path LIKE @pathPrefix ESCAPE '\'
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("pathPrefix", pathParam);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                userActive[reader.GetString(0)] = reader.GetBoolean(1);
        }

        // (3) Active vikars covering asOf. The coverage predicate is the SAME one the per-question
        //     read applies — effective_to IS NULL AND until_date >= asOf, INCLUSIVE — so a vikar
        //     expiring today is still covering, exactly as before.
        var vikarByApprover = new Dictionary<string, ManagerVikar>(StringComparer.Ordinal);
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT mv.* FROM manager_vikar mv
            JOIN users u ON u.user_id = mv.absent_approver_id
            JOIN organizations o ON o.org_id = u.primary_org_id
            WHERE mv.effective_to IS NULL
              AND mv.until_date >= @asOf
              AND o.materialized_path LIKE @pathPrefix ESCAPE '\'
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("pathPrefix", pathParam);
            cmd.Parameters.AddWithValue("asOf", asOf);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var vikar = ManagerVikarRepository.MapReaderRow(reader);
                vikarByApprover[vikar.AbsentApproverId] = vikar;
            }
        }

        // Everything the activity query returned is, by construction, exactly the Organisation's
        // membership — which is the same scope the line and vikar queries used.
        var covered = new HashSet<string>(userActive.Keys, StringComparer.Ordinal);
        return new PrefetchedReportingLineDataSource(lines, userActive, vikarByApprover, covered, fallback);
    }

    public Task<ReportingLine?> GetActiveLineAsync(string employeeId, string relationship, CancellationToken ct)
        => _covered.Contains(employeeId)
            ? Task.FromResult(_lines.TryGetValue((employeeId, relationship), out var line) ? line : null)
            : _fallback.GetActiveLineAsync(employeeId, relationship, ct);

    public Task<ManagerVikar?> GetActiveVikarByApproverAsync(string approverId, DateOnly asOf, CancellationToken ct)
    {
        if (!_covered.Contains(approverId))
            return _fallback.GetActiveVikarByApproverAsync(approverId, asOf, ct);

        // asOf is fixed for the projection (invariant 7) and was applied at load time; asserting it
        // here would be dead weight, but a DIFFERENT date would silently get the wrong coverage set,
        // so the caller contract is: build with the same asOf the resolver runs at.
        return Task.FromResult(
            _vikarByApprover.TryGetValue(approverId, out var vikar) ? vikar : null);
    }

    public Task<bool> IsUserActiveAsync(string userId, CancellationToken ct)
        => _covered.Contains(userId)
            ? Task.FromResult(_userActive[userId])
            : _fallback.IsUserActiveAsync(userId, ct);
}
