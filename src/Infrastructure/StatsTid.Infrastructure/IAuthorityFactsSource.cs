using Npgsql;

namespace StatsTid.Infrastructure;

/// <summary>
/// S125 / TASK-12501 step 3c — the three facts the approval-authority predicate needs beyond the
/// resolved edge, behind an interface so they can come from live SQL (one round-trip per question) or
/// from a set-based prefetch of the whole Organisation.
///
/// <para>Same discipline as <see cref="IReportingLineDataSource"/>: this abstracts the LOOKUPS, never
/// the DECISIONS. The role floor's meaning, the fail-closed same-Organisation comparison and the
/// Direct-before-Vikar precedence all stay in
/// <see cref="DesignatedApproverAuthorizer"/> / <see cref="ReportingLineRepository"/> and run
/// identically whichever source is in play.</para>
/// </summary>
public interface IAuthorityFactsSource
{
    /// <summary>Active user holding ≥1 active, non-expired role assignment with
    /// <c>hierarchy_level &lt;= 4</c>. Deliberately NOT constrained by the assignment's org or scope —
    /// that absence is the current contract (the cross-org bound is enforced separately by the
    /// same-Organisation check), and a prefetched source must preserve it rather than tidy it up.</summary>
    Task<bool> IsActiveLeaderOrAboveAsync(string userId, CancellationToken ct);

    /// <summary>The user's home <c>primary_org_id</c>, or <b>null</b> when the user is missing, is
    /// inactive, or their home Organisation is inactive. Null means DENY — absence must never read as
    /// permission.</summary>
    Task<string?> GetActiveHomeOrgAsync(string userId, CancellationToken ct);

    /// <summary>Whether the actor leads the employee's OWN unit directly, or stands in as an active
    /// vikar for one of its leaders. STRICTLY single-table on <c>users.unit_id</c> — no ancestor walk
    /// (the LOCKED ADR-038 D5 boundary). A NULL unit, or actor == employee, yields
    /// <see cref="UnitLeaderApprovalKind.None"/>.</summary>
    Task<UnitLeaderApprovalKind> GetUnitLeaderKindAsync(string actorId, string employeeId, CancellationToken ct);
}

/// <summary>Live-SQL implementation — the behaviour every caller had before step 3c, and the reference
/// the prefetched source is differentially tested against.</summary>
public sealed class SqlAuthorityFactsSource : IAuthorityFactsSource
{
    private readonly NpgsqlConnection _conn;
    private readonly NpgsqlTransaction? _tx;
    private readonly DateOnly _asOf;

    public SqlAuthorityFactsSource(NpgsqlConnection conn, NpgsqlTransaction? tx, DateOnly asOf)
    {
        _conn = conn;
        _tx = tx;
        _asOf = asOf;
    }

    public Task<bool> IsActiveLeaderOrAboveAsync(string userId, CancellationToken ct)
        => DesignatedApproverAuthorizer.QueryActiveLeaderOrAboveAsync(_conn, _tx, userId, ct);

    public Task<string?> GetActiveHomeOrgAsync(string userId, CancellationToken ct)
        => ReportingLineRepository.QueryActiveHomeOrgAsync(_conn, _tx, userId, ct);

    public Task<UnitLeaderApprovalKind> GetUnitLeaderKindAsync(string actorId, string employeeId, CancellationToken ct)
        => DesignatedApproverAuthorizer.QueryUnitLeaderKindSqlAsync(_conn, _tx, actorId, employeeId, _asOf, ct);
}

/// <summary>
/// Set-based prefetch of the same three facts for a whole Organisation — four reads, regardless of how
/// many employees are pending.
///
/// <para><b>Correctness rests on the snapshot</b>, exactly as for
/// <see cref="PrefetchedReportingLineDataSource"/>: built inside the projection's REPEATABLE READ
/// transaction, the prefetched rows are the same rows a per-question read would have returned, so this
/// is an equivalence rather than a staleness trade-off. Lifetime is one projection call.</para>
///
/// <para><b>Fail-closed parity is the thing to get right.</b> A user absent from the role-floor set is
/// NOT a leader; a user absent from the home-org map denies the same-Organisation check; a unit with
/// no leaders yields None. Every "absent" answer is the denying answer, matching what the SQL returns
/// when its joins produce no row.</para>
///
/// <para><b>S126 / N6b — the parity above holds at the VERDICT level, not the FACT level, and the
/// difference is worth stating.</b> Read (2) requires <c>o.is_active = TRUE</c>, while the projection's
/// phase (1) joins <c>organizations</c> with no such filter
/// (<see cref="ApprovalPeriodRepository"/>) and the live-SQL unit-leader query filters neither. So an
/// employee under an INACTIVE home Organisation is enumerated as pending, is absent from
/// <c>_unitOfUser</c>, and gets <c>None</c> here where live SQL would say Direct/Vikar. The two agree
/// anyway because the same-Organisation gate independently denies that whole population — but that is
/// a COINCIDENCE BETWEEN TWO UNRELATED PREDICATES, not a guarantee this class enforces. Anything that
/// loosens the same-Organisation gate (the deferred HR/GlobalAdmin <c>ORG_SCOPE_FALLBACK</c> ruling is
/// the likely candidate) must re-check this, because the differential test compares verdicts only and
/// would not catch it.</para>
/// </summary>
public sealed class PrefetchedAuthorityFacts : IAuthorityFactsSource
{
    private readonly HashSet<string> _leaderOrAbove;
    private readonly Dictionary<string, string> _activeHomeOrg;
    private readonly Dictionary<string, Guid> _unitOfUser;
    private readonly Dictionary<Guid, HashSet<string>> _leadersOfUnit;
    // vikar stand-in → the leaders they currently cover (reverse of absent_approver_id).
    private readonly Dictionary<string, HashSet<string>> _coversLeaders;

    private PrefetchedAuthorityFacts(
        HashSet<string> leaderOrAbove,
        Dictionary<string, string> activeHomeOrg,
        Dictionary<string, Guid> unitOfUser,
        Dictionary<Guid, HashSet<string>> leadersOfUnit,
        Dictionary<string, HashSet<string>> coversLeaders)
    {
        _leaderOrAbove = leaderOrAbove;
        _activeHomeOrg = activeHomeOrg;
        _unitOfUser = unitOfUser;
        _leadersOfUnit = leadersOfUnit;
        _coversLeaders = coversLeaders;
    }

    /// <summary>Reads issued to build this — four, independent of the pending count.</summary>
    public const int BuildStatementCount = 4;

    public static async Task<PrefetchedAuthorityFacts> BuildAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string escapedPathPrefixParam, DateOnly asOf,
        CancellationToken ct)
    {
        // (1) The role floor — the SAME predicate as the per-user query, evaluated for the whole
        //     Organisation at once. Membership of the returned set IS the answer; nothing is
        //     re-derived in C#.
        var leaderOrAbove = new HashSet<string>(StringComparer.Ordinal);
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT DISTINCT u.user_id
            FROM users u
            JOIN role_assignments ra ON ra.user_id = u.user_id
            JOIN roles r ON r.role_id = ra.role_id
            JOIN organizations o ON o.org_id = u.primary_org_id
            WHERE u.is_active = TRUE
              AND ra.is_active = TRUE
              AND (ra.expires_at IS NULL OR ra.expires_at > NOW())
              AND r.hierarchy_level <= 4
              AND o.materialized_path LIKE @pathPrefix ESCAPE '\'
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("pathPrefix", escapedPathPrefixParam);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                leaderOrAbove.Add(reader.GetString(0));
        }

        // (2) Home Organisation for ACTIVE users whose home Organisation is itself ACTIVE — the same
        //     join the per-pair check applies. A user failing either condition is simply absent, which
        //     is the deny answer.
        var activeHomeOrg = new Dictionary<string, string>(StringComparer.Ordinal);
        var unitOfUser = new Dictionary<string, Guid>(StringComparer.Ordinal);
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT u.user_id, u.primary_org_id, u.unit_id
            FROM users u
            JOIN organizations o ON o.org_id = u.primary_org_id AND o.is_active = TRUE
            WHERE u.is_active = TRUE
              AND o.materialized_path LIKE @pathPrefix ESCAPE '\'
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("pathPrefix", escapedPathPrefixParam);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var userId = reader.GetString(0);
                activeHomeOrg[userId] = reader.GetString(1);
                if (!reader.IsDBNull(2))
                    unitOfUser[userId] = reader.GetGuid(2);
            }
        }

        // (3) unit → its designated leaders, bounded to the units employees are actually homed in.
        //
        // S126 / N6b — KEY-BOUNDED, not org-scoped. `leadersOfUnit` is only ever indexed by
        // `unitOfUser[employeeId]` (see GetUnitLeaderKindAsync), so restricting the read to exactly
        // those unit ids removes only rows no lookup could reach. That makes this answer-identical BY
        // CONSTRUCTION rather than by test — the property survives someone editing the query later.
        //
        // Why NOT the path-prefix scope the sibling reads use: the ACTOR dimension must stay global.
        // Scoping by the leader's home Organisation would drop an in-Organisation vikar covering a
        // cross-Organisation leader, whom live SQL admits (QueryUnitLeaderKindAsync carries no org
        // bound) — locking out a legitimate approver. This read is a GATE, not a resolver: a miss can
        // only deny. The direction that could fail OPEN lives in PrefetchedReportingLineDataSource,
        // which picks a winner and can fall through.
        var leadersOfUnit = new Dictionary<Guid, HashSet<string>>();
        var unitIds = unitOfUser.Values.Distinct().ToArray();
        await using (var cmd = new NpgsqlCommand(
            "SELECT ul.unit_id, ul.user_id FROM unit_leaders ul WHERE ul.unit_id = ANY(@unitIds)",
            conn, tx))
        {
            cmd.Parameters.AddWithValue("unitIds", unitIds);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var unitId = reader.GetGuid(0);
                if (!leadersOfUnit.TryGetValue(unitId, out var set))
                    leadersOfUnit[unitId] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(reader.GetString(1));
            }
        }

        // (4) Active vikar coverage, keyed by the STAND-IN. Coverage is the same inclusive predicate:
        //     effective_to IS NULL AND until_date >= asOf.
        //
        // S126 / N6b — bounded by the leaders read (3) actually found, for the same reason: a
        // coversLeaders entry only matters when the covered leader passes `leaders.Contains(...)`, so
        // rows for any other absent approver are unreachable. The VIKAR (actor) side stays unbounded.
        var coversLeaders = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var leaderIds = leadersOfUnit.Values.SelectMany(s => s).Distinct().ToArray();
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT mv.vikar_user_id, mv.absent_approver_id
            FROM manager_vikar mv
            WHERE mv.effective_to IS NULL AND mv.until_date >= @asOf
              AND mv.absent_approver_id = ANY(@leaderIds)
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("asOf", asOf);
            cmd.Parameters.AddWithValue("leaderIds", leaderIds);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var vikarUser = reader.GetString(0);
                if (!coversLeaders.TryGetValue(vikarUser, out var set))
                    coversLeaders[vikarUser] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(reader.GetString(1));
            }
        }

        return new PrefetchedAuthorityFacts(
            leaderOrAbove, activeHomeOrg, unitOfUser, leadersOfUnit, coversLeaders);
    }

    public Task<bool> IsActiveLeaderOrAboveAsync(string userId, CancellationToken ct)
        => Task.FromResult(_leaderOrAbove.Contains(userId));

    public Task<string?> GetActiveHomeOrgAsync(string userId, CancellationToken ct)
        => Task.FromResult(_activeHomeOrg.TryGetValue(userId, out var org) ? org : null);

    public Task<UnitLeaderApprovalKind> GetUnitLeaderKindAsync(
        string actorId, string employeeId, CancellationToken ct)
    {
        // Mirrors the SQL's WHERE clause exactly: a NULL unit_id yields no rows, and the
        // segregation-of-duties exclusion (e.user_id <> @actorId) means a leader never qualifies over
        // their OWN period.
        if (string.Equals(actorId, employeeId, StringComparison.Ordinal))
            return Task.FromResult(UnitLeaderApprovalKind.None);
        if (!_unitOfUser.TryGetValue(employeeId, out var unitId))
            return Task.FromResult(UnitLeaderApprovalKind.None);
        if (!_leadersOfUnit.TryGetValue(unitId, out var leaders))
            return Task.FromResult(UnitLeaderApprovalKind.None);

        // Direct membership WINS over the vikar classification — the same precedence the SQL applies
        // by testing is_direct before is_vikar (it drives UNIT_LEADER vs UNIT_LEADER_VIKAR in audit).
        if (leaders.Contains(actorId))
            return Task.FromResult(UnitLeaderApprovalKind.Direct);

        // S125 / RES-003 (owner ruling 2026-07-30): the covered leader must not BE the employee — a
        // stand-in inherits the approvals the absent leader OWES, never the approval that leader
        // RECEIVES. Mirrors `AND mv.absent_approver_id <> e.user_id` in the SQL form; the combined
        // differential test compares the two over self-pairs, which is what would catch a drift here.
        if (_coversLeaders.TryGetValue(actorId, out var covered)
            && covered.Any(coveredLeader =>
                !string.Equals(coveredLeader, employeeId, StringComparison.Ordinal)
                && leaders.Contains(coveredLeader)))
        {
            return Task.FromResult(UnitLeaderApprovalKind.Vikar);
        }

        return Task.FromResult(UnitLeaderApprovalKind.None);
    }
}
