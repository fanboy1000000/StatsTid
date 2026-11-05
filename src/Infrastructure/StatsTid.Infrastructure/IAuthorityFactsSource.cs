using Npgsql;
using StatsTid.SharedKernel.Security;

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
    /// <summary>
    /// <b>Acting</b> user at <paramref name="asOf"/> holding ≥1 active, non-expired role assignment with
    /// <c>hierarchy_level &lt;= 4</c>. Deliberately NOT constrained by the assignment's org or scope —
    /// that absence is the current contract (the cross-org bound is enforced separately by the
    /// same-Organisation check), and a prefetched source must preserve it rather than tidy it up.
    ///
    /// <para><b>S147 / TASK-14712 (QUAL-230 T5c; NR-10).</b> Was <c>IsActiveLeaderOrAboveAsync</c>, whose
    /// user half was <c>is_active</c> alone. "Acting" adds "not past their recorded last day of
    /// employment at <paramref name="asOf"/>" — the rule is
    /// <see cref="StatsTid.SharedKernel.Security.DepartedSubject.IsDeparted(bool, DateOnly?, DateOnly)"/>,
    /// negated. Under the owner's 5-day grace (OQ-2) a departed manager stays <c>is_active</c> until
    /// D+6 so that THEIR manager can sign their final month; this floor makes sure the grace does not
    /// also keep them in charge of their own reports. <paramref name="asOf"/> is the authorizer's own
    /// authority date, never a second clock read (PAT-028).</para>
    /// </summary>
    Task<bool> IsActingLeaderOrAboveAsync(string userId, DateOnly asOf, CancellationToken ct);

    /// <summary>The user's home <c>primary_org_id</c>, or <b>null</b> when the user is missing, is
    /// inactive, or their home Organisation is inactive. Null means DENY — absence must never read as
    /// permission.</summary>
    Task<string?> GetActiveHomeOrgAsync(string userId, CancellationToken ct);

    /// <summary>Whether the actor leads the employee's OWN unit directly, or stands in as an active
    /// vikar for one of its leaders. STRICTLY single-table on <c>users.unit_id</c> — no ancestor walk
    /// (the LOCKED ADR-038 D5 boundary). A NULL unit, or actor == employee, yields
    /// <see cref="UnitLeaderApprovalKind.None"/>.</summary>
    Task<UnitLeaderApprovalKind> GetUnitLeaderKindAsync(string actorId, string employeeId, CancellationToken ct);

    /// <summary>
    /// S147 / TASK-14725 (Step 7a W-1; owner ruling 2026-11-05) — <c>true</c> when NO manager may act for
    /// <paramref name="userId"/> any more at <paramref name="asOf"/>: their last day employed lies more
    /// than the owner's 5-day manager grace before <paramref name="asOf"/>
    /// (<see cref="EmploymentEndDateLifecycleWriter.IsPastGrace"/>, i.e. from D+6), OR they are outside
    /// the set <see cref="GetActiveHomeOrgAsync"/> answers for (missing, switched off, or under an
    /// inactive home Organisation). A NULL end date (open-ended employment) is not past the grace.
    ///
    /// <para><b>Why it exists.</b> Until TASK-14725 the manager's day-6 cut-off held only because the
    /// leaver job (Step A, a 5-minute poll) switches the leaver off on D+6 and the same-Organisation
    /// re-check reads active users only. Between Copenhagen midnight and that poll — or for as long as
    /// Step A fails — the manager could still approve, reject or reopen. This fact lets the authorizer
    /// decide the cut-off from the DATE; the switch-off stays as belt and braces. Same discipline as
    /// <see cref="IsActingLeaderOrAboveAsync"/>: both forms read the same columns and decide through the
    /// same call; <paramref name="asOf"/> is the authorizer's own authority date (PAT-028).</para>
    ///
    /// <para><b>The default is fail-CLOSED</b> (<c>true</c>: "past"), so an implementation that does not
    /// supply this fact can only ever DENY manager authority. Both production sources override it; the
    /// default exists so a test double written before TASK-14725 keeps compiling.</para>
    /// </summary>
    Task<bool> IsPastManagerGraceAsync(string userId, DateOnly asOf, CancellationToken ct)
        => Task.FromResult(true);
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

    public Task<bool> IsActingLeaderOrAboveAsync(string userId, DateOnly asOf, CancellationToken ct)
        => DesignatedApproverAuthorizer.QueryActingLeaderOrAboveAsync(_conn, _tx, userId, asOf, ct);

    public Task<string?> GetActiveHomeOrgAsync(string userId, CancellationToken ct)
        => ReportingLineRepository.QueryActiveHomeOrgAsync(_conn, _tx, userId, ct);

    public Task<UnitLeaderApprovalKind> GetUnitLeaderKindAsync(string actorId, string employeeId, CancellationToken ct)
        => DesignatedApproverAuthorizer.QueryUnitLeaderKindSqlAsync(_conn, _tx, actorId, employeeId, _asOf, ct);

    /// <summary>S147 / TASK-14725 — the live form, the authorizer's own query.</summary>
    public Task<bool> IsPastManagerGraceAsync(string userId, DateOnly asOf, CancellationToken ct)
        => DesignatedApproverAuthorizer.QueryPastManagerGraceAsync(_conn, _tx, userId, asOf, ct);
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
    // S147 / TASK-14712 (T5c) — the users holding a LeaderOrAbove assignment, each with the two columns
    // the acting-user rule reads. Absent = no such assignment = NOT a leader (the deny answer, as before).
    // The user half is decided per call by DepartedSubject.IsDeparted, the same call the live form makes.
    private readonly Dictionary<string, (bool IsActive, DateOnly? EmploymentEndDate)> _leaderOrAbove;
    private readonly Dictionary<string, string> _activeHomeOrg;
    // S147 / TASK-14725 — the recorded last day employed of every user in _activeHomeOrg (null = none),
    // from the SAME read (2). Its key set is _activeHomeOrg's; a user absent from it is "past" (deny).
    private readonly Dictionary<string, DateOnly?> _activeEmploymentEndDate;
    private readonly Dictionary<string, Guid> _unitOfUser;
    private readonly Dictionary<Guid, HashSet<string>> _leadersOfUnit;
    // vikar stand-in → the leaders they currently cover (reverse of absent_approver_id).
    private readonly Dictionary<string, HashSet<string>> _coversLeaders;

    private PrefetchedAuthorityFacts(
        Dictionary<string, (bool IsActive, DateOnly? EmploymentEndDate)> leaderOrAbove,
        Dictionary<string, string> activeHomeOrg,
        Dictionary<string, DateOnly?> activeEmploymentEndDate,
        Dictionary<string, Guid> unitOfUser,
        Dictionary<Guid, HashSet<string>> leadersOfUnit,
        Dictionary<string, HashSet<string>> coversLeaders)
    {
        _leaderOrAbove = leaderOrAbove;
        _activeHomeOrg = activeHomeOrg;
        _activeEmploymentEndDate = activeEmploymentEndDate;
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
        //     Organisation at once. Its ROLE half (an active, non-expired LeaderOrAbove assignment) is
        //     decided here in SQL: membership of the returned rows IS that answer.
        //     S147 / TASK-14712 (T5c) — its USER half is no longer `u.is_active = TRUE` in the WHERE.
        //     The two columns the acting-user rule reads come back instead, and IsActingLeaderOrAboveAsync
        //     applies DepartedSubject.IsDeparted to them at the authorizer's date — exactly what the
        //     per-user query (DesignatedApproverAuthorizer.QueryActingLeaderOrAboveAsync) does with
        //     ITS row, so the two forms read the same columns and decide through the same call.
        //     (is_active and employment_end_date are columns of `users`, so DISTINCT stays one row
        //     per user however many qualifying assignments they hold.)
        var leaderOrAbove = new Dictionary<string, (bool IsActive, DateOnly? EmploymentEndDate)>(StringComparer.Ordinal);
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT DISTINCT u.user_id, u.is_active, u.employment_end_date
            FROM users u
            JOIN role_assignments ra ON ra.user_id = u.user_id
            JOIN roles r ON r.role_id = ra.role_id
            JOIN organizations o ON o.org_id = u.primary_org_id
            WHERE ra.is_active = TRUE
              AND (ra.expires_at IS NULL OR ra.expires_at > NOW())
              AND r.hierarchy_level <= 4
              AND o.materialized_path LIKE @pathPrefix ESCAPE '\'
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("pathPrefix", escapedPathPrefixParam);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                leaderOrAbove[reader.GetString(0)] = (
                    reader.GetBoolean(1),
                    reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2));
            }
        }

        // (2) Home Organisation for ACTIVE users whose home Organisation is itself ACTIVE — the same
        //     join the per-pair check applies. A user failing either condition is simply absent, which
        //     is the deny answer.
        //     S147 / TASK-14725 — the same rows also carry employment_end_date, the one column the
        //     subject's manager-grace fact reads (IsPastManagerGraceAsync). A column on this read, not
        //     a fifth statement: BuildStatementCount stays 4 and the projection stays flat.
        var activeHomeOrg = new Dictionary<string, string>(StringComparer.Ordinal);
        var activeEmploymentEndDate = new Dictionary<string, DateOnly?>(StringComparer.Ordinal);
        var unitOfUser = new Dictionary<string, Guid>(StringComparer.Ordinal);
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT u.user_id, u.primary_org_id, u.unit_id, u.employment_end_date
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
                activeEmploymentEndDate[userId] = reader.IsDBNull(3) ? null : reader.GetFieldValue<DateOnly>(3);
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
            leaderOrAbove, activeHomeOrg, activeEmploymentEndDate, unitOfUser, leadersOfUnit, coversLeaders);
    }

    /// <summary>S147 / TASK-14712 (T5c) — absent from the role set → not a leader (deny, as before);
    /// present → acting at <paramref name="asOf"/> by the same rule and the same call as the live form
    /// (<see cref="DesignatedApproverAuthorizer.QueryActingLeaderOrAboveAsync"/>).</summary>
    public Task<bool> IsActingLeaderOrAboveAsync(string userId, DateOnly asOf, CancellationToken ct)
        => Task.FromResult(
            _leaderOrAbove.TryGetValue(userId, out var state)
            && !DepartedSubject.IsDeparted(state.IsActive, state.EmploymentEndDate, asOf));

    public Task<string?> GetActiveHomeOrgAsync(string userId, CancellationToken ct)
        => Task.FromResult(_activeHomeOrg.TryGetValue(userId, out var org) ? org : null);

    /// <summary>S147 / TASK-14725 — absent from read (2) (missing, switched off, home Organisation
    /// inactive, or outside the prefetched tree) → past (deny, the same population
    /// <see cref="GetActiveHomeOrgAsync"/> denies); present → past only when the recorded last day is
    /// past the manager grace at <paramref name="asOf"/>, by the same call as the live form
    /// (<see cref="DesignatedApproverAuthorizer.QueryPastManagerGraceAsync"/>).</summary>
    public Task<bool> IsPastManagerGraceAsync(string userId, DateOnly asOf, CancellationToken ct)
        => Task.FromResult(
            !_activeEmploymentEndDate.TryGetValue(userId, out var endDate)
            || DesignatedApproverAuthorizer.IsPastManagerGrace(endDate, asOf));

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
