using Npgsql;

namespace StatsTid.Infrastructure;

/// <summary>
/// S74 / ADR-027 D4 amendment (SPRINT-74 R5, TASK-7402) — the ONE canonical
/// approve-authority predicate (the A3 "edge GRANTS authority within the styrelse"
/// expansion; OQ-3a; a P7 privilege expansion).
///
/// <para>
/// <b>Single home, single encoding.</b> Both the my-reports dashboard reads (R6,
/// <see cref="ApprovalPeriodRepository.GetPendingForDesignatedReportsAsync"/> +
/// <see cref="ApprovalPeriodRepository.GetByMonthForDesignatedReportsAsync"/>) AND the
/// three manager action endpoints (R7 — approve / reject / reopen-Leader-branch in
/// <c>ApprovalEndpoints</c>) authorize through <em>this</em> predicate, so the
/// "see == act" invariant holds at every level and the two surfaces cannot drift.
/// </para>
///
/// <para>
/// <b>Semantics.</b> <c>IsEffectiveDesignatedApproverAsync(actorId, employeeId, asOf)</c>
/// is <c>true</c> ⟺ the actor is <b>active</b> AND holds a <b>LeaderOrAbove</b> role
/// AND is the <b>single resolved effective approver</b> of <paramref name="employeeId"/>
/// at <c>asOf</c> per the R3 precedence (admin-assigned ACTING → the resolved PRIMARY
/// manager M's active approver-owned vikar → M-if-active → inactive-manager escalation),
/// resolved by the vikar-aware
/// <see cref="ReportingLineRepository.ResolveDesignatedApproverAsync"/>.
/// </para>
///
/// <para>
/// <b>Organisation bound is structural, AND explicitly re-checked (S74-7402 B1 fix).</b> Most
/// resolving edges are intra-Organisation by the assign-time
/// <see cref="ReportingLineRepository.ValidateSameOrganisationAsync(string, string, CancellationToken)"/>
/// invariant — but a <c>manager_vikar</c> stand-in is approver-owned and was historically created
/// without a same-Organisation check, so <c>actor == resolvedManager</c> alone did NOT guarantee the
/// same Organisation. This predicate therefore re-checks the Organisation for BOTH the actor and the
/// employee (via
/// <see cref="ReportingLineRepository.ValidateSameOrganisationAsync(string, string, CancellationToken)"/>)
/// and denies on any mismatch. The cross-styrelse bound is thus TRULY structural in the
/// authority predicate (ADR-027 D2), independent of how any edge/vikar was created — even a
/// directly-planted cross-tree vikar row is denied. (S92/ADR-035 flatten: a tree root is
/// now a MAO/ORGANISATION row; the former afdelinger are collapsed into their parent
/// ORGANISATION, so an intra-Organisation edge naturally shares the same
/// <c>organisation_id</c>. Transitional machinery — retired in S95.)
/// </para>
///
/// <para>
/// This is deliberately <b>NOT</b> a union of the recursive transitive-report set: a
/// grand-manager (whose grandchild has an active intermediate manager) is NOT the single
/// effective approver of that grandchild and so is correctly denied — see == act one
/// level up too.
/// </para>
/// </summary>
public sealed class DesignatedApproverAuthorizer
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly ReportingLineRepository _reportingLineRepo;

    public DesignatedApproverAuthorizer(
        DbConnectionFactory connectionFactory,
        ReportingLineRepository reportingLineRepo)
    {
        _connectionFactory = connectionFactory;
        _reportingLineRepo = reportingLineRepo;
    }

    /// <summary>
    /// The R5 canonical predicate. Returns <c>true</c> iff the actor is active +
    /// LeaderOrAbove AND is the single resolved effective approver of
    /// <paramref name="employeeId"/> at <paramref name="asOf"/>.
    /// </summary>
    /// <param name="actorId">The acting user (the JWT subject).</param>
    /// <param name="employeeId">The employee whose period the actor wants to see/act on.</param>
    /// <param name="asOf">
    /// The authority date. For an action ("who may act NOW") the caller passes
    /// <c>today</c>; the parameter defaults to today (<c>null</c> ⇒ today) so the dashboard
    /// reads (which mean "now") need not thread a date.
    /// </param>
    public async Task<bool> IsEffectiveDesignatedApproverAsync(
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(actorId) || string.IsNullOrEmpty(employeeId))
            return false;

        var effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // (1) The actor must be an active LeaderOrAbove. The resolver only returns ACTIVE
        //     approvers, so "active" is implied when the resolved id == actor; but the role
        //     gate is NOT enforced by the resolver (a vikar could be an Employee-role user),
        //     so we check it explicitly here — defense-in-depth and the load-bearing gate
        //     when the actor is reached purely as a vikar stand-in.
        if (!await IsActiveLeaderOrAboveAsync(actorId, ct))
            return false;

        // (2) Resolve the SINGLE effective approver at asOf (vikar-aware, R3 precedence).
        var (resolvedManagerId, _, _) =
            await _reportingLineRepo.ResolveDesignatedApproverAsync(employeeId, ct, asOf: effectiveAsOf);

        // (3) The edge grants authority IFF the actor IS that single winner.
        if (resolvedManagerId is null
            || !string.Equals(resolvedManagerId, actorId, StringComparison.Ordinal))
            return false;

        // (4) SECURITY (ADR-027 D2 — S74-7402 B1 fix): re-verify STRUCTURALLY that the actor
        //     and the employee share an Organisation (the same primary_org_id). We do NOT trust
        //     edge-creation correctness alone — an approver-owned vikar could historically be
        //     cross-Organisation, so even a directly-planted cross-Organisation vikar row that wins
        //     resolution must be denied here. S95 / ADR-035 slice 4: ValidateSameOrganisationAsync
        //     reads both users' primary_org_id directly (the tree-WALK is retired — post-S92 the
        //     Organisation IS the primary_org_id) and throws CrossOrganisationAssignmentException on
        //     mismatch; a throw ⇒ deny. An intra-Organisation edge shares a home ⇒ still passes.
        try
        {
            await _reportingLineRepo.ValidateSameOrganisationAsync(employeeId, actorId, ct);
        }
        catch (CrossOrganisationAssignmentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // Either user not found / inactive — cannot affirm same-Organisation, so deny
            // (fail-closed).
            return false;
        }

        return true;
    }

    /// <summary>
    /// S105 / ADR-038 D4 (the keystone) — the SECONDARY/peer unit-leader approval path, the FIRST
    /// time <c>unit_leaders</c> legitimately enters authority. Returns <c>true</c> iff
    /// <see cref="ResolveUnitLeaderApprovalKindAsync"/> classifies the actor as a Direct unit-leader OR
    /// an active vikar of a unit-leader of the employee's OWN unit, same Organisation. STRICTLY
    /// <c>E.unit_id</c>-bounded (the employee's own unit's direct members) — NOT an ancestor/recursive
    /// walk: a leader of a PARENT / GRANDPARENT / SIBLING unit holds no <c>unit_leaders</c> row for
    /// <c>E.unit_id</c> and so grants NOTHING (the LOCKED D5 boundary; the S76/S85/S91 subtree-
    /// inheritance bug class stays closed). A NULL <c>E.unit_id</c> → no match.
    /// </summary>
    public async Task<bool> IsUnitLeaderApproverAsync(
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
        => await ResolveUnitLeaderApprovalKindAsync(actorId, employeeId, asOf, ct)
            != UnitLeaderApprovalKind.None;

    /// <summary>
    /// S105 / ADR-038 D4 — the CENTRALIZED "edge OR unit-leader" approval predicate (the ONE shared
    /// helper every read-filter + the action endpoints' in-lock re-eval route through, so the two
    /// stages of the my-reports pipeline + the team-overview filter + the allocation-breakdown gate +
    /// the compliance gate can never drift apart). Returns <c>true</c> iff the actor holds the effective
    /// designated-approver EDGE (<see cref="IsEffectiveDesignatedApproverAsync"/>) OR the secondary
    /// unit-leader path (<see cref="IsUnitLeaderApproverAsync"/>). This is the my-reports "edge OR
    /// unit-leader visibility" set — it does NOT include the HR/Admin org-scope branch (TASK-10502: the
    /// action endpoints compose that separately as a pre-tx JWT gate).
    /// </summary>
    public async Task<bool> IsEffectiveApproverOrUnitLeaderAsync(
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
    {
        if (await IsEffectiveDesignatedApproverAsync(actorId, employeeId, asOf, ct))
            return true;
        return await IsUnitLeaderApproverAsync(actorId, employeeId, asOf, ct);
    }

    /// <summary>
    /// S105 / ADR-038 D4 — classifies HOW (if at all) the actor holds the secondary unit-leader
    /// approval authority over <paramref name="employeeId"/> at <paramref name="asOf"/>, for the audit
    /// <c>approval_method</c> (Direct → <c>UNIT_LEADER</c>, Vikar → <c>UNIT_LEADER_VIKAR</c>). All the
    /// floors of the predicate apply: the actor must be an active LeaderOrAbove (the SAME gate the edge
    /// path applies, even to a vikar stand-in), the membership/vikar check is the SINGLE-TABLE
    /// <c>unit_leaders(E.unit_id)</c> lookup (NO ancestor walk), and the same-Organisation re-check
    /// holds. Direct membership takes precedence over the vikar classification.
    /// </summary>
    public async Task<UnitLeaderApprovalKind> ResolveUnitLeaderApprovalKindAsync(
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(actorId) || string.IsNullOrEmpty(employeeId))
            return UnitLeaderApprovalKind.None;

        var effectiveAsOf = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // (1) The actor must be an active LeaderOrAbove — the SAME role floor the edge path applies
        //     (a unit_leaders row for an Employee-role / inactive user grants nothing; D3 role-coupling).
        if (!await IsActiveLeaderOrAboveAsync(actorId, ct))
            return UnitLeaderApprovalKind.None;

        // (2) The SINGLE-TABLE membership/vikar lookup over the employee's OWN unit's leaders
        //     (unit_leaders.unit_id = E.unit_id) — NEVER an ancestor/recursive walk (the LOCKED D5
        //     boundary). NULL E.unit_id → zero rows → (false, false) → None.
        var rawKind = await QueryUnitLeaderKindAsync(actorId, employeeId, effectiveAsOf, ct);
        if (rawKind == UnitLeaderApprovalKind.None)
            return UnitLeaderApprovalKind.None;

        // (3) SECURITY — re-verify STRUCTURALLY that the actor and the employee share an Organisation
        //     (the same primary_org_id), the SAME re-check the edge path applies. Same-Org binds the
        //     vikar path transitively (D12). A throw ⇒ deny (fail-closed).
        try
        {
            await _reportingLineRepo.ValidateSameOrganisationAsync(employeeId, actorId, ct);
        }
        catch (CrossOrganisationAssignmentException)
        {
            return UnitLeaderApprovalKind.None;
        }
        catch (InvalidOperationException)
        {
            // Either user not found / inactive — cannot affirm same-Organisation, so deny.
            return UnitLeaderApprovalKind.None;
        }

        return rawKind;
    }

    /// <summary>
    /// The SINGLE-TABLE structural lookup behind <see cref="ResolveUnitLeaderApprovalKindAsync"/> (no
    /// active/role/same-Org floors — those are applied by the caller). Over the leaders of the
    /// employee's OWN unit (<c>unit_leaders.unit_id = users.unit_id</c>), reports whether the actor is a
    /// Direct leader and/or an active vikar (covering <paramref name="asOf"/>) of one of those leaders.
    /// Direct membership wins. NO recursive walk over <c>units.parent_unit_id</c> (the D5 keystone).
    /// </summary>
    private async Task<UnitLeaderApprovalKind> QueryUnitLeaderKindAsync(
        string actorId, string employeeId, DateOnly asOf, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT
                COALESCE(bool_or(ul.user_id = @actorId), FALSE)       AS is_direct,
                COALESCE(bool_or(mv.vikar_user_id = @actorId), FALSE) AS is_vikar
            FROM users e
            JOIN unit_leaders ul ON ul.unit_id = e.unit_id
            LEFT JOIN manager_vikar mv
                   ON mv.absent_approver_id = ul.user_id
                  AND mv.vikar_user_id = @actorId
                  AND mv.effective_to IS NULL
                  AND mv.until_date >= @asOf
            WHERE e.user_id = @employeeId
              AND e.unit_id IS NOT NULL
              -- SEGREGATION OF DUTIES (S105 Step-7a BLOCKER): a unit leader IS a member of the unit
              -- they lead (the D3 member-invariant), so without this a leader would match as the
              -- approver of their OWN period. The unit-leader edge covers OTHER direct members only;
              -- a leader's own period routes to their primary edge / HR-Admin (never self-approval).
              AND e.user_id <> @actorId
            """, conn);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("asOf", asOf);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return UnitLeaderApprovalKind.None; // defensive — the aggregate always returns one row.
        var isDirect = reader.GetBoolean(0);
        var isVikar = reader.GetBoolean(1);
        if (isDirect)
            return UnitLeaderApprovalKind.Direct;
        if (isVikar)
            return UnitLeaderApprovalKind.Vikar;
        return UnitLeaderApprovalKind.None;
    }

    /// <summary>
    /// Returns <c>true</c> iff <paramref name="userId"/> is an active user holding at least
    /// one active role assignment with <c>hierarchy_level &lt;= 4</c> (LOCAL_LEADER or above).
    /// Single query against <c>users</c> + <c>role_assignments</c> + <c>roles</c>; mirrors the
    /// <c>RoleAssignmentRepository</c> active-assignment predicate (is_active + non-expired).
    /// </summary>
    private async Task<bool> IsActiveLeaderOrAboveAsync(string userId, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT 1
            FROM users u
            JOIN role_assignments ra ON ra.user_id = u.user_id
            JOIN roles r ON r.role_id = ra.role_id
            WHERE u.user_id = @userId
              AND u.is_active = TRUE
              AND ra.is_active = TRUE
              AND (ra.expires_at IS NULL OR ra.expires_at > NOW())
              AND r.hierarchy_level <= 4
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null && result is not DBNull;
    }
}

/// <summary>
/// S105 / ADR-038 D4 — how the actor holds the secondary unit-leader approval authority over an
/// employee (drives the persisted <c>approval_method</c> audit classification). <see cref="Direct"/>
/// = the actor is a designated leader of the employee's OWN unit (→ <c>UNIT_LEADER</c>);
/// <see cref="Vikar"/> = the actor is an active stand-in (<c>manager_vikar</c>) for such a leader
/// (→ <c>UNIT_LEADER_VIKAR</c>); <see cref="None"/> = neither (the actor was admitted via the edge or
/// HR/Admin scope, or not at all). Direct membership takes precedence over Vikar.
/// </summary>
public enum UnitLeaderApprovalKind
{
    None = 0,
    Direct = 1,
    Vikar = 2,
}
