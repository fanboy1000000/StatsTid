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
