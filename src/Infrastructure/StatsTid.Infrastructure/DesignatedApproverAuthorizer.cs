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

    // ════════════════════════════════════════════════════════════════════════════════════════
    //  S125 / TASK-12501 step 1 — the OVERLOAD-PAIR PATTERN, finally adopted here.
    //
    //  `ReportingLineRepository` is built throughout on a connection-reusing primitive
    //  `(NpgsqlConnection conn, NpgsqlTransaction? tx, …)` plus a self-contained overload that opens a
    //  connection and DELEGATES to it (see ValidateSameOrganisationAsync :397/:448 and its rationale at
    //  :405-412). This class never adopted it: every primitive below existed ONLY in self-contained
    //  form, each opening its own connection.
    //
    //  That single gap produced every symptom of the F1 defect — 15 connection opens per pending
    //  employee, an authority gate that must RE-RESOLVE what its caller already computed (44% of the
    //  round-trips) because nothing can be handed in, and no way for two reads to share a snapshot.
    //
    //  The delegation direction matters: the self-contained overload calls the reusing one, so each
    //  rule has EXACTLY ONE implementation. That is what makes ADR-027/038's one-encoding requirement
    //  structural here rather than a convention reviewers have to police.
    //
    //  STEP 1 IS SEMANTICALLY INERT. With `tx: null` every statement still autocommits, so each read
    //  observes the latest committed state exactly as before — Postgres transactions are session-scoped,
    //  so sharing a connection without a transaction changes only who pays for the handshake. The
    //  snapshot (step 2) and the redundancy deletion (step 3) build on this; neither is done here.
    // ════════════════════════════════════════════════════════════════════════════════════════

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
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await IsEffectiveDesignatedApproverAsync(conn, tx: null, actorId, employeeId, asOf, ct);
    }

    /// <summary>Connection-reusing sibling of
    /// <see cref="IsEffectiveDesignatedApproverAsync(string, string, DateOnly?, CancellationToken)"/>.
    /// Same rule, same order, same fail-closed behaviour — the self-contained overload delegates here.</summary>
    public Task<bool> IsEffectiveDesignatedApproverAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
        => IsEffectiveDesignatedApproverAsync(
            conn, tx, ctx: null, actorId, employeeId, asOf, ct);

    /// <summary>
    /// S125 / TASK-12501 step 3 — the memoized form. <paramref name="ctx"/> is a per-projection cache
    /// FILLED BY THIS METHOD's own code path (see <see cref="ApprovalAuthorityContext"/>); passing
    /// <c>null</c> gives exactly today's behaviour, one query per question. It is sound ONLY inside a
    /// snapshot, where "ask once" and "ask each time" are the same answer by construction.
    /// </summary>
    public Task<bool> IsEffectiveDesignatedApproverAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
        => IsEffectiveDesignatedApproverAsync(conn, tx, ctx, source: null, actorId, employeeId, asOf, ct);

    /// <summary>Step 3b form — <paramref name="source"/> supplies the edge leg's facts; null = live SQL.</summary>
    public Task<bool> IsEffectiveDesignatedApproverAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        IReportingLineDataSource? source,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
        => IsEffectiveDesignatedApproverAsync(conn, tx, ctx, source, facts: null, actorId, employeeId, asOf, ct);

    /// <summary>Step 3c form — see the combined predicate's remarks.</summary>
    public async Task<bool> IsEffectiveDesignatedApproverAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        IReportingLineDataSource? source, IAuthorityFactsSource? facts,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
    {
        EnsureContextIsSnapshotBound(ctx, conn, tx);

        if (string.IsNullOrEmpty(actorId) || string.IsNullOrEmpty(employeeId))
            return false;

        var effectiveAsOf = asOf ?? ctx?.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // (1) The actor must be an active LeaderOrAbove. The resolver only returns ACTIVE
        //     approvers, so "active" is implied when the resolved id == actor; but the role
        //     gate is NOT enforced by the resolver (a vikar could be an Employee-role user),
        //     so we check it explicitly here — defense-in-depth and the load-bearing gate
        //     when the actor is reached purely as a vikar stand-in.
        if (!await RoleFloorAsync(conn, tx, ctx, facts, actorId, ct))
            return false;

        // (2) Resolve the SINGLE effective approver at asOf (vikar-aware, R3 precedence).
        //     Memoized per employee: the projection's caller resolved this one line before calling us,
        //     and every further candidate for the same employee asks the identical question.
        var (resolvedManagerId, _, _) = await ResolveEdgeAsync(conn, tx, ctx, source, employeeId, effectiveAsOf, ct);

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
        return await SameOrganisationAsync(conn, tx, ctx, facts, employeeId, actorId, ct);
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

    /// <summary>Connection-reusing sibling of
    /// <see cref="IsUnitLeaderApproverAsync(string, string, DateOnly?, CancellationToken)"/>.</summary>
    public async Task<bool> IsUnitLeaderApproverAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
        => await ResolveUnitLeaderApprovalKindAsync(conn, tx, ctx: null, actorId, employeeId, asOf, ct)
            != UnitLeaderApprovalKind.None;

    /// <summary>Memoized form — see <see cref="ApprovalAuthorityContext"/>.</summary>
    public async Task<bool> IsUnitLeaderApproverAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
        => await ResolveUnitLeaderApprovalKindAsync(conn, tx, ctx, actorId, employeeId, asOf, ct)
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
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await IsEffectiveApproverOrUnitLeaderAsync(conn, tx: null, actorId, employeeId, asOf, ct);
    }

    /// <summary>Connection-reusing sibling of
    /// <see cref="IsEffectiveApproverOrUnitLeaderAsync(string, string, DateOnly?, CancellationToken)"/> —
    /// the overload the period-status projection's tally loop uses so one connection serves the whole
    /// pass. Identical short-circuit order (edge first, then unit-leader).</summary>
    public Task<bool> IsEffectiveApproverOrUnitLeaderAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
        => IsEffectiveApproverOrUnitLeaderAsync(conn, tx, ctx: null, actorId, employeeId, asOf, ct);

    /// <summary>Memoized form — the overload the period-status projection's tally loop uses. Identical
    /// short-circuit order (edge first, then unit-leader); <paramref name="ctx"/> only prevents the
    /// same question being asked twice. See <see cref="ApprovalAuthorityContext"/>.</summary>
    public Task<bool> IsEffectiveApproverOrUnitLeaderAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
        => IsEffectiveApproverOrUnitLeaderAsync(conn, tx, ctx, source: null, actorId, employeeId, asOf, ct);

    /// <summary>Step 3b form — additionally takes the prefetched
    /// <see cref="IReportingLineDataSource"/> the edge leg resolves through. Null means live SQL.</summary>
    public Task<bool> IsEffectiveApproverOrUnitLeaderAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        IReportingLineDataSource? source,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
        => IsEffectiveApproverOrUnitLeaderAsync(conn, tx, ctx, source, facts: null, actorId, employeeId, asOf, ct);

    /// <summary>Step 3c form — <paramref name="facts"/> supplies the role floor, the home-Organisation
    /// lookup and the unit-leader classification. Null means live SQL. The DECISIONS are unchanged and
    /// still live here; only the lookups move.</summary>
    public async Task<bool> IsEffectiveApproverOrUnitLeaderAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        IReportingLineDataSource? source, IAuthorityFactsSource? facts,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
    {
        // ── RES-003 / SEC-009 — THE STRUCTURAL SELF-GUARD (segregation of duties, fail CLOSED) ──────
        // Deny outright when the actor IS the employee whose period this is: nobody holds manager
        // approval authority over their OWN period. Every EDGE / UNIT-LEADER / VIKAR authority path
        // funnels through this terminal predicate (all public overloads delegate here), so this single
        // guard makes the SoD rule fail CLOSED by default and turns each per-path SQL exclusion (the
        // edge's FAIL-004 self-resolution guard, the unit-leader `e.user_id <> @actorId`, the vikar
        // `mv.absent_approver_id <> e.user_id`) into defence-in-depth rather than the only line — the
        // fix RES-003 item 2 asked for. No legitimate self-authority caller of this predicate exists
        // (cycle-2 review VERIFIED: the read surfaces self-exclude, and the one leg that bypasses this
        // predicate — the org-scope / HR-Admin fallback, SEC-009's exact path — is guarded separately
        // at the three manager-decision endpoints via ApprovalSelfGuard).
        if (string.Equals(actorId, employeeId, StringComparison.Ordinal))
            return false;

        if (await IsEffectiveDesignatedApproverAsync(conn, tx, ctx, source, facts, actorId, employeeId, asOf, ct))
            return true;
        return await ResolveUnitLeaderApprovalKindAsync(conn, tx, ctx, facts, actorId, employeeId, asOf, ct)
            != UnitLeaderApprovalKind.None;
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
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ResolveUnitLeaderApprovalKindAsync(conn, tx: null, actorId, employeeId, asOf, ct);
    }

    /// <summary>Connection-reusing sibling of
    /// <see cref="ResolveUnitLeaderApprovalKindAsync(string, string, DateOnly?, CancellationToken)"/>.
    /// All floors and the Direct-before-Vikar precedence are unchanged.</summary>
    public Task<UnitLeaderApprovalKind> ResolveUnitLeaderApprovalKindAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
        => ResolveUnitLeaderApprovalKindAsync(conn, tx, ctx: null, actorId, employeeId, asOf, ct);

    /// <summary>Memoized form — see <see cref="ApprovalAuthorityContext"/>. All floors and the
    /// Direct-before-Vikar precedence are unchanged.</summary>
    public Task<UnitLeaderApprovalKind> ResolveUnitLeaderApprovalKindAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
        => ResolveUnitLeaderApprovalKindAsync(conn, tx, ctx, facts: null, actorId, employeeId, asOf, ct);

    /// <summary>Step 3c form — see the combined predicate's remarks.</summary>
    public async Task<UnitLeaderApprovalKind> ResolveUnitLeaderApprovalKindAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        IAuthorityFactsSource? facts,
        string actorId, string employeeId, DateOnly? asOf = null, CancellationToken ct = default)
    {
        EnsureContextIsSnapshotBound(ctx, conn, tx);

        if (string.IsNullOrEmpty(actorId) || string.IsNullOrEmpty(employeeId))
            return UnitLeaderApprovalKind.None;

        var effectiveAsOf = asOf ?? ctx?.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);

        // (1) The actor must be an active LeaderOrAbove — the SAME role floor the edge path applies
        //     (a unit_leaders row for an Employee-role / inactive user grants nothing; D3 role-coupling).
        if (!await RoleFloorAsync(conn, tx, ctx, facts, actorId, ct))
            return UnitLeaderApprovalKind.None;

        // (2) The SINGLE-TABLE membership/vikar lookup over the employee's OWN unit's leaders
        //     (unit_leaders.unit_id = E.unit_id) — NEVER an ancestor/recursive walk (the LOCKED D5
        //     boundary). NULL E.unit_id → zero rows → (false, false) → None.
        var rawKind = await UnitLeaderKindAsync(conn, tx, facts, actorId, employeeId, effectiveAsOf, ct);
        if (rawKind == UnitLeaderApprovalKind.None)
            return UnitLeaderApprovalKind.None;

        // (3) SECURITY — re-verify STRUCTURALLY that the actor and the employee share an Organisation
        //     (the same primary_org_id), the SAME re-check the edge path applies. Same-Org binds the
        //     vikar path transitively (D12). A throw ⇒ deny (fail-closed).
        if (!await SameOrganisationAsync(conn, tx, ctx, facts, employeeId, actorId, ct))
            return UnitLeaderApprovalKind.None;

        return rawKind;
    }

    /// <summary>
    /// The SINGLE-TABLE structural lookup behind <see cref="ResolveUnitLeaderApprovalKindAsync"/> (no
    /// active/role/same-Org floors — those are applied by the caller). Over the leaders of the
    /// employee's OWN unit (<c>unit_leaders.unit_id = users.unit_id</c>), reports whether the actor is a
    /// Direct leader and/or an active vikar (covering <paramref name="asOf"/>) of one of those leaders.
    /// Direct membership wins. NO recursive walk over <c>units.parent_unit_id</c> (the D5 keystone).
    /// </summary>
    /// <summary>Live-SQL form behind <see cref="IAuthorityFactsSource.GetUnitLeaderKindAsync"/> —
    /// exposed so <see cref="SqlAuthorityFactsSource"/> reuses this exact query.</summary>
    internal static Task<UnitLeaderApprovalKind> QueryUnitLeaderKindSqlAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string actorId, string employeeId, DateOnly asOf, CancellationToken ct)
        => QueryUnitLeaderKindAsync(conn, tx, actorId, employeeId, asOf, ct);

    private static async Task<UnitLeaderApprovalKind> QueryUnitLeaderKindAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string actorId, string employeeId, DateOnly asOf, CancellationToken ct)
    {
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
                  -- S125 / RES-003 (owner ruling 2026-07-30): a stand-in inherits the approvals the
                  -- absent leader OWES, never the approval that leader RECEIVES. A vikar covering
                  -- leader L may approve L's unit MEMBERS, but not L's own period.
                  AND mv.absent_approver_id <> e.user_id
            WHERE e.user_id = @employeeId
              AND e.unit_id IS NOT NULL
              -- SEGREGATION OF DUTIES (S105 Step-7a BLOCKER): a unit leader IS a member of the unit
              -- they lead (the D3 member-invariant), so without this a leader would match as the
              -- approver of their OWN period. The unit-leader edge covers OTHER direct members only;
              -- a leader's own period routes to their primary edge / HR-Admin (never self-approval).
              AND e.user_id <> @actorId
            """, conn, tx);
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

    // ── S125 / TASK-12501 step 3 — the memo wrappers ────────────────────────────────────────
    //  Each takes the SAME code path as before and merely remembers the answer. There is no second
    //  encoding of any rule here: the lambda passed to the context IS the original call.

    /// <summary>Role floor, memoized per USER. It is a fact about the candidate alone, yet was asked
    /// once per (candidate, employee) pair — and twice per pair for a unit-leader candidate, once in
    /// each leg. Across a projection the candidate set is small and highly repeated, so this
    /// amortises to near zero.</summary>
    private Task<bool> RoleFloorAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        IAuthorityFactsSource? facts, string userId, CancellationToken ct)
    {
        Task<bool> Query() => facts is null
            ? IsActiveLeaderOrAboveAsync(conn, tx, userId, ct)
            : facts.IsActiveLeaderOrAboveAsync(userId, ct);

        return ctx is null ? Query() : ctx.RoleFloorAsync(userId, Query);
    }

    private static Task<UnitLeaderApprovalKind> UnitLeaderKindAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, IAuthorityFactsSource? facts,
        string actorId, string employeeId, DateOnly asOf, CancellationToken ct)
        => facts is null
            ? QueryUnitLeaderKindAsync(conn, tx, actorId, employeeId, asOf, ct)
            : facts.GetUnitLeaderKindAsync(actorId, employeeId, ct);

    /// <summary>Edge resolution, memoized per EMPLOYEE — the single biggest redundancy (12 of the 27
    /// statements): the projection resolves it, then the gate re-resolved it once per candidate.</summary>
    private Task<(string? ManagerId, string? Method, int Depth)> ResolveEdgeAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        IReportingLineDataSource? source, string employeeId, DateOnly asOf, CancellationToken ct)
    {
        Task<(string? ManagerId, string? Method, int Depth)> Resolve() => source is null
            ? _reportingLineRepo.ResolveDesignatedApproverAsync(conn, tx, employeeId, asOf, ct)
            : _reportingLineRepo.ResolveDesignatedApproverAsync(source, employeeId, asOf, ct);

        return ctx is null ? Resolve() : ctx.ResolveEdgeAsync(employeeId, Resolve);
    }

    /// <summary>
    /// The same-Organisation re-check (ADR-027 D2), fail-closed, memoized per (employee, actor) pair.
    ///
    /// <para>Two things changed here and both are deliberate. It now passes <c>lockRows: false</c> —
    /// this is a READ, and inside step 2's snapshot the <c>FOR UPDATE</c> would hold write locks on
    /// every row it touches for the whole projection. And the two catch arms are unchanged in meaning:
    /// <see cref="CrossOrganisationAssignmentException"/> (different Organisations) and
    /// <see cref="InvalidOperationException"/> (user missing/inactive, or home Organisation inactive)
    /// BOTH deny. Collapsing them into a bool at this single site keeps the fail-closed rule in one
    /// place rather than duplicated in each caller.</para>
    /// </summary>
    /// <summary>
    /// S126 / W3 — the ONE place the memo's snapshot precondition is enforced. Called from BOTH
    /// terminal funnels (<see cref="IsEffectiveDesignatedApproverAsync(NpgsqlConnection, NpgsqlTransaction?, ApprovalAuthorityContext?, IReportingLineDataSource?, IAuthorityFactsSource?, string, string, DateOnly?, CancellationToken)"/>
    /// and <see cref="ResolveUnitLeaderApprovalKindAsync(NpgsqlConnection, NpgsqlTransaction?, ApprovalAuthorityContext?, IAuthorityFactsSource?, string, string, DateOnly?, CancellationToken)"/>);
    /// the combined predicate delegates to both, so every ctx-bearing path passes through here.
    ///
    /// <para>Guarding only one funnel would let the other bypass it silently — which is why this is a
    /// shared helper and not an inline check.</para>
    ///
    /// <para><b>Why a null tx is rejected rather than tolerated.</b> The memo is sound because a
    /// REPEATABLE READ snapshot makes "ask once" and "ask every time" the same answer. With no
    /// transaction there is no snapshot, so the memo becomes an accepted-staleness trade nobody ruled
    /// on. Serializable is accepted too — it is strictly stronger.</para>
    /// </summary>
    private static void EnsureContextIsSnapshotBound(
        ApprovalAuthorityContext? ctx, NpgsqlConnection conn, NpgsqlTransaction? tx)
    {
        if (ctx is null) return;

        if (tx is null)
            throw new InvalidOperationException(
                "An ApprovalAuthorityContext was supplied without a transaction. The memo is only " +
                "equivalent to re-querying inside a REPEATABLE READ snapshot; outside one it would " +
                "serve answers from before a mid-request role revocation, edge reassignment, " +
                "deactivation or transfer. Pass the projection's transaction, or pass ctx: null.");

        if (tx.IsolationLevel is not (System.Data.IsolationLevel.RepeatableRead
                                      or System.Data.IsolationLevel.Serializable))
            throw new InvalidOperationException(
                $"An ApprovalAuthorityContext was supplied under isolation level {tx.IsolationLevel}. " +
                "Memoizing authority answers requires REPEATABLE READ (or stronger) — at READ " +
                "COMMITTED the underlying rows can change mid-projection and the memo would authorize " +
                "against state that no longer exists.");

        ctx.BindTo(conn);
    }

    private Task<bool> SameOrganisationAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ApprovalAuthorityContext? ctx,
        IAuthorityFactsSource? facts, string employeeId, string actorId, CancellationToken ct)
    {
        Task<bool> Check() => CheckSameOrganisationAsync(conn, tx, facts, employeeId, actorId, ct);
        return ctx is null ? Check() : ctx.SameOrganisationAsync(employeeId, actorId, Check);
    }

    private async Task<bool> CheckSameOrganisationAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, IAuthorityFactsSource? facts,
        string employeeId, string actorId, CancellationToken ct)
    {
        try
        {
            if (facts is null)
            {
                await _reportingLineRepo.ValidateSameOrganisationAsync(
                    conn, tx, employeeId, actorId, lockRows: false, ct);
            }
            else
            {
                // Same DECISION, different lookup: the two home Organisations come from the prefetch,
                // then ReportingLineRepository.DecideSameOrganisation applies the identical
                // null-checks and equality and throws the identical exception types the arms below
                // catch. The rule is not restated here.
                ReportingLineRepository.DecideSameOrganisation(
                    employeeId, actorId,
                    await facts.GetActiveHomeOrgAsync(employeeId, ct),
                    await facts.GetActiveHomeOrgAsync(actorId, ct));
            }
            return true;
        }
        catch (CrossOrganisationAssignmentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            // Either user not found / inactive, or the home Organisation is inactive — cannot affirm
            // same-Organisation, so deny (fail-closed).
            return false;
        }
    }

    /// <summary>
    /// Returns <c>true</c> iff <paramref name="userId"/> is an active user holding at least
    /// one active role assignment with <c>hierarchy_level &lt;= 4</c> (LOCAL_LEADER or above).
    /// Single query against <c>users</c> + <c>role_assignments</c> + <c>roles</c>; mirrors the
    /// <c>RoleAssignmentRepository</c> active-assignment predicate (is_active + non-expired).
    /// </summary>
    /// <summary>Live-SQL form behind <see cref="IAuthorityFactsSource.IsActiveLeaderOrAboveAsync"/>.</summary>
    internal static Task<bool> QueryActiveLeaderOrAboveAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string userId, CancellationToken ct)
        => IsActiveLeaderOrAboveAsync(conn, tx, userId, ct);

    private static async Task<bool> IsActiveLeaderOrAboveAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx, string userId, CancellationToken ct)
    {
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
            """, conn, tx);
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
