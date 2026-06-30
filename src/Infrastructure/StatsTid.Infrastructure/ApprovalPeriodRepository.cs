using Npgsql;
using NpgsqlTypes;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure;

public sealed class ApprovalPeriodRepository
{
    private readonly DbConnectionFactory _connectionFactory;
    private readonly DesignatedApproverAuthorizer _designatedAuthorizer;
    private readonly ReportingLineRepository _reportingLineRepo;

    /// <summary>
    /// Primary constructor (DI). The <paramref name="designatedAuthorizer"/> is the ONE
    /// canonical R5 predicate (S74 / ADR-027 D4) that the my-reports dashboard reads
    /// (<see cref="GetPendingForDesignatedReportsAsync"/> +
    /// <see cref="GetByMonthForDesignatedReportsAsync"/>) filter through, so the dashboard
    /// "see" set matches the action "act" authority exactly. It is OPTIONAL so existing
    /// tests that construct the repository with the factory alone keep compiling — when
    /// omitted, an authorizer is derived from the same factory.
    ///
    /// <para>
    /// The <paramref name="reportingLineRepo"/> is the vikar-aware resolver
    /// (<see cref="ReportingLineRepository.ResolveDesignatedApproverAsync"/>) the period-status
    /// projection (<see cref="GetPeriodStatusProjectionForTreeAsync"/>) uses to resolve the EDGE
    /// candidate of the per-authorized-approver pending tally (S106 / TASK-10604 expanded that tally
    /// to ALSO enumerate the ADR-038 D4 unit-leader path) — the SAME resolution the dashboard
    /// predicate uses, so each tile count is consistent with that manager's my-reports semantics.
    /// Also OPTIONAL/derived from the same factory for test-construction compatibility.
    /// </para>
    /// </summary>
    public ApprovalPeriodRepository(
        DbConnectionFactory connectionFactory,
        DesignatedApproverAuthorizer? designatedAuthorizer = null,
        ReportingLineRepository? reportingLineRepo = null)
    {
        _connectionFactory = connectionFactory;
        _reportingLineRepo = reportingLineRepo ?? new ReportingLineRepository(connectionFactory);
        _designatedAuthorizer = designatedAuthorizer
            ?? new DesignatedApproverAuthorizer(connectionFactory, _reportingLineRepo);
    }

    public async Task<ApprovalPeriod?> GetByIdAsync(Guid periodId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM approval_periods WHERE period_id = @periodId", conn);
        cmd.Parameters.AddWithValue("periodId", periodId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadPeriod(reader) : null;
    }

    public async Task<IReadOnlyList<ApprovalPeriod>> GetByEmployeeAsync(string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM approval_periods WHERE employee_id = @employeeId ORDER BY period_start DESC", conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        return await ReadPeriodsAsync(cmd, ct);
    }

    public async Task<ApprovalPeriod?> GetByEmployeeAndPeriodAsync(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM approval_periods WHERE employee_id = @employeeId AND period_start = @periodStart AND period_end = @periodEnd", conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("periodStart", periodStart);
        cmd.Parameters.AddWithValue("periodEnd", periodEnd);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadPeriod(reader) : null;
    }

    public async Task<IReadOnlyList<ApprovalPeriod>> GetPendingByOrgAsync(string orgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM approval_periods WHERE org_id = @orgId AND status IN ('SUBMITTED', 'EMPLOYEE_APPROVED') ORDER BY period_start", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        return await ReadPeriodsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<ApprovalPeriod>> GetPendingByOrgPathAsync(string orgPathPrefix, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT ap.* FROM approval_periods ap
            JOIN organizations o ON ap.org_id = o.org_id
            WHERE o.materialized_path LIKE @pathPrefix ESCAPE '\' AND ap.status IN ('SUBMITTED', 'EMPLOYEE_APPROVED')
            ORDER BY ap.period_start
            """, conn);
        // S76: literal-prefix the (system-derived) path — a '%' or '_' in an org id/path cannot
        // widen the prefix into a wildcard (mirrors the S75 escape at :414/:572).
        cmd.Parameters.AddWithValue("pathPrefix", EscapeLike(orgPathPrefix) + "%");
        return await ReadPeriodsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<ApprovalPeriod>> GetByMonthAndOrgPathAsync(
        string orgPathPrefix, int year, int month, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        var monthStart = new DateOnly(year, month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT ap.* FROM approval_periods ap
            JOIN organizations o ON ap.org_id = o.org_id
            WHERE o.materialized_path LIKE @pathPrefix ESCAPE '\' AND ap.period_start < @nextMonthStart AND ap.period_end >= @monthStart
            ORDER BY ap.period_start
            """, conn);
        // S76: literal-prefix the (system-derived) path (mirrors the S75 escape at :414/:572).
        cmd.Parameters.AddWithValue("pathPrefix", EscapeLike(orgPathPrefix) + "%");
        cmd.Parameters.AddWithValue("monthStart", monthStart);
        cmd.Parameters.AddWithValue("nextMonthStart", nextMonthStart);
        return await ReadPeriodsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<ApprovalPeriod>> GetByMonthAndOrgAsync(
        string orgId, int year, int month, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        var monthStart = new DateOnly(year, month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM approval_periods WHERE org_id = @orgId AND period_start < @nextMonthStart AND period_end >= @monthStart ORDER BY period_start", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("monthStart", monthStart);
        cmd.Parameters.AddWithValue("nextMonthStart", nextMonthStart);
        return await ReadPeriodsAsync(cmd, ct);
    }

    /// <summary>
    /// Returns approval periods (any status) for a given month for the actor's "my-reports"
    /// edge-authority view (S74 / ADR-027 D4, SPRINT-74 R6).
    ///
    /// <para>
    /// <b>Single-immediate semantics (REWRITTEN — see the before/after below).</b> An
    /// employee appears here IFF the actor is that employee's <em>single resolved effective
    /// approver</em> at <c>today</c> per the R3 precedence (admin-ACTING → vikar → PRIMARY →
    /// inactive escalation) — authoritatively decided by the shared R5 predicate
    /// (<see cref="DesignatedApproverAuthorizer.IsEffectiveDesignatedApproverAsync"/>). This
    /// makes the my-reports "see" set EXACTLY the action "act" authority (see == act at every
    /// level, including via a vikar stand-in), and removes the prior grand-manager over-grant.
    /// </para>
    ///
    /// <para>
    /// <b>Before:</b> a <c>WITH RECURSIVE managed_employees</c> that walked DOWN the report
    /// graph transitively (reports-of-reports through ACTIVE managers), intersected with the
    /// actor's RBAC <c>{orgScopeClause}</c>. A grand-manager whose grandchild had an active
    /// intermediate manager still SAW that grandchild (then 403'd on approve — the exact
    /// incoherence R7 prevents, one level up); and the org-scope intersection wrongly hid
    /// cross-afdeling edge reports the actor IS authorized for.
    /// <b>After:</b> a tree-root-bounded candidate superset (see
    /// <see cref="DesignatedCandidateEmployeesCte"/>) that descends from the actor AND from
    /// every absent approver the actor currently stands in for as a vikar, advancing into a
    /// sub-manager's reports ONLY when that sub-manager is INACTIVE (inactive-escalation
    /// only — never a grandchild whose own manager is active). The org-scope intersection is
    /// GONE — the edge grants cross-afdeling visibility within the tree. The candidate set is
    /// then filtered per-employee by the R5 predicate, which cannot drift from the action
    /// authority because it is the same code.
    /// </para>
    /// </summary>
    public async Task<List<ApprovalPeriod>> GetByMonthForDesignatedReportsAsync(
        string actorId, IReadOnlyList<RoleScope> actorScopes, int year, int month, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var monthStart = new DateOnly(year, month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var sql = $"""
            {DesignatedCandidateEmployeesCte}
            SELECT DISTINCT ap.* FROM approval_periods ap
            JOIN candidate_employees ce ON ce.employee_id = ap.employee_id
            WHERE ap.period_start < @nextMonthStart AND ap.period_end >= @monthStart
            ORDER BY ap.period_start
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("today", today);
        cmd.Parameters.AddWithValue("monthStart", monthStart);
        cmd.Parameters.AddWithValue("nextMonthStart", nextMonthStart);

        var candidates = new List<ApprovalPeriod>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                candidates.Add(ReadPeriod(reader));
        }

        return await FilterByEffectiveApproverAsync(actorId, today, candidates, ct);
    }

    /// <summary>
    /// Returns PENDING (SUBMITTED / EMPLOYEE_APPROVED) approval periods for the actor's
    /// "my-reports" edge-authority view (S74 / ADR-027 D4, SPRINT-74 R6) — same
    /// single-immediate-effective-approver semantics as
    /// <see cref="GetByMonthForDesignatedReportsAsync"/>, restricted to the pending statuses.
    ///
    /// <para>
    /// <b>Before:</b> a transitive <c>WITH RECURSIVE managed_employees</c> (reports-of-reports
    /// through ACTIVE managers) intersected with the actor's RBAC <c>{orgScopeClause}</c> —
    /// over-granted a grand-manager (saw a grandchild with an active intermediate manager,
    /// then 403'd on approve) and under-served the actor's own cross-afdeling edge reports.
    /// <b>After:</b> the tree-root-bounded candidate superset (descend from the actor + every
    /// absent approver the actor currently stands in for as a vikar, advancing only through
    /// INACTIVE sub-managers — inactive-escalation only), filtered per-employee by the shared
    /// R5 predicate so the "see" set equals the action "act" authority and cannot drift.
    /// </para>
    /// </summary>
    public async Task<List<ApprovalPeriod>> GetPendingForDesignatedReportsAsync(
        string actorId, IReadOnlyList<RoleScope> actorScopes, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var sql = $"""
            {DesignatedCandidateEmployeesCte}
            SELECT DISTINCT ap.* FROM approval_periods ap
            JOIN candidate_employees ce ON ce.employee_id = ap.employee_id
            WHERE ap.status IN ('SUBMITTED', 'EMPLOYEE_APPROVED')
            ORDER BY ap.period_start
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("today", today);

        var candidates = new List<ApprovalPeriod>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                candidates.Add(ReadPeriod(reader));
        }

        return await FilterByEffectiveApproverAsync(actorId, today, candidates, ct);
    }

    /// <summary>
    /// Shared candidate-employee CTE for the two my-reports edge-authority reads (R6). Produces
    /// a tree-root-bounded SUPERSET of the employees for whom the actor MIGHT be the single
    /// effective approver at <c>@today</c>; the authoritative single-winner decision is made
    /// afterwards by the shared R5 predicate
    /// (<see cref="FilterByEffectiveApproverAsync"/>), so this set may over-include but never
    /// under-include.
    ///
    /// <para>
    /// Seeds: (1) the actor; (2) every <c>absent_approver_id</c> for whom the actor holds an
    /// ACTIVE <c>manager_vikar</c> covering <c>@today</c> — the actor stands in for that
    /// absent manager, so that manager's report set is the actor's too. From each seed S the
    /// recursion collects S's direct reports (PRIMARY or ACTING edge with <c>manager_id = S</c>),
    /// and continues DOWN into a report X's own reports ONLY when X is an INACTIVE user
    /// (inactive-escalation only — never a grandchild whose intermediate manager is active).
    /// The walk stays within the reporting graph (intra-tree by the assign-time same-tree
    /// invariant), so it is tree-root bounded; <c>depth &lt; 10</c> mirrors the resolver's bound
    /// and stops any pre-existing cycle.
    /// </para>
    ///
    /// <para>
    /// <b>S105 / ADR-038 D4 — the SECONDARY unit-leader seed (see == act, stage a).</b> A unit member
    /// whose primary reporting edge does NOT descend from the actor never enters the reporting walk
    /// above, so a secondary unit-leader would ACT on them (D4 path-2/3) without SEEING them. The
    /// <c>unit_led_members</c> CTE adds the candidate superset for that path: the DIRECT members of units
    /// the actor leads (<c>unit_leaders.user_id = @actorId</c> on <c>E.unit_id</c>), plus the direct
    /// members of units led by a manager the actor is an active vikar for. STRICTLY <c>E.unit_id</c>-bounded
    /// (a single-table <c>unit_leaders</c> join — NO ancestor walk over <c>units.parent_unit_id</c>; the
    /// LOCKED D5 boundary). A SUPERSET only — the shared "edge OR unit-leader" predicate
    /// (<see cref="DesignatedApproverAuthorizer.IsEffectiveApproverOrUnitLeaderAsync"/>) applies the FULL
    /// floors (active actor + LeaderOrAbove + same-Organisation + active-vikar) in stage b, so a
    /// bare/expired/Employee-role row never becomes see-without-act.
    /// </para>
    /// </summary>
    private const string DesignatedCandidateEmployeesCte = """
        WITH RECURSIVE seeds AS (
            -- The actor manages their own direct reports + their inactive-escalation subtree.
            SELECT @actorId AS seed_id
            UNION
            -- The actor stands in (vikar) for these absent approvers, covering @today.
            SELECT mv.absent_approver_id
            FROM manager_vikar mv
            WHERE mv.vikar_user_id = @actorId
              AND mv.effective_to IS NULL
              AND mv.until_date >= @today
        ),
        candidate_walk AS (
            -- Level 0: direct reports of each seed (PRIMARY or ACTING manager edge = seed).
            SELECT rl.employee_id, 0 AS depth
            FROM reporting_lines rl
            JOIN seeds s ON s.seed_id = rl.manager_id
            WHERE rl.effective_to IS NULL
              AND rl.relationship IN ('PRIMARY', 'ACTING')
            UNION
            -- Descend into report X's reports ONLY when X is an INACTIVE user
            -- (the inactive-manager escalation walk — X's reports escalate up past X).
            SELECT rl2.employee_id, cw.depth + 1
            FROM reporting_lines rl2
            JOIN candidate_walk cw ON rl2.manager_id = cw.employee_id
            JOIN users ux ON ux.user_id = cw.employee_id
            WHERE rl2.effective_to IS NULL
              AND rl2.relationship = 'PRIMARY'
              AND ux.is_active = FALSE
              AND cw.depth < 10
        ),
        unit_led_members AS (
            -- S105 / ADR-038 D4 path-2: the DIRECT members of units the actor leads (single-table
            -- unit_leaders join on E.unit_id — NEVER an ancestor walk; the LOCKED D5 boundary).
            SELECT e.user_id AS employee_id
            FROM users e
            JOIN unit_leaders ul ON ul.unit_id = e.unit_id AND ul.user_id = @actorId
            WHERE e.unit_id IS NOT NULL
              AND e.is_active = TRUE
              AND e.user_id <> @actorId   -- segregation of duties: a leader never sees/acts on their OWN period via the unit-leader edge (S105 Step-7a BLOCKER)
            UNION
            -- S105 / ADR-038 D4 path-3: the direct members of units led by a manager the actor is an
            -- active vikar (manager_vikar) for, covering @today (the same single-table E.unit_id bound).
            SELECT e.user_id AS employee_id
            FROM users e
            JOIN unit_leaders ul ON ul.unit_id = e.unit_id
            JOIN manager_vikar mv ON mv.absent_approver_id = ul.user_id
              AND mv.vikar_user_id = @actorId
              AND mv.effective_to IS NULL
              AND mv.until_date >= @today
            WHERE e.unit_id IS NOT NULL
              AND e.is_active = TRUE
              AND e.user_id <> @actorId   -- segregation of duties (S105 Step-7a BLOCKER): no self-period via the vikar-of-unit-leader edge
        ),
        candidate_employees AS (
            SELECT employee_id FROM candidate_walk
            UNION
            SELECT employee_id FROM unit_led_members
        )
        """;

    /// <summary>
    /// Filters <paramref name="candidates"/> to the periods whose employee resolves to
    /// <paramref name="actorId"/> as the single effective approver at <paramref name="asOf"/>,
    /// via the shared R5 predicate. The predicate is the SAME code the action endpoints
    /// authorize through (no second encoding → no see/act drift). Distinct employees are
    /// resolved once each.
    /// </summary>
    private async Task<List<ApprovalPeriod>> FilterByEffectiveApproverAsync(
        string actorId, DateOnly asOf, List<ApprovalPeriod> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0)
            return candidates;

        var decisionByEmployee = new Dictionary<string, bool>(StringComparer.Ordinal);
        var result = new List<ApprovalPeriod>(candidates.Count);
        foreach (var period in candidates)
        {
            if (!decisionByEmployee.TryGetValue(period.EmployeeId, out var isApprover))
            {
                // S105 / ADR-038 D4 — the CENTRALIZED "edge OR unit-leader" predicate (stage b). The
                // candidate CTE (stage a) now over-includes the actor's unit-led members, so this filter
                // MUST authorize through the SAME edge-OR-unit-leader code the action endpoints use, else
                // a unit-led member would be filtered OUT (the two-stage half-wire). Full floors apply.
                isApprover = await _designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                    actorId, period.EmployeeId, asOf: asOf, ct: ct);
                decisionByEmployee[period.EmployeeId] = isApprover;
            }
            if (isApprover)
                result.Add(period);
        }
        return result;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  S87-8701 — the team-overview roster read (read-only)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// S87-8701 — the roster backing the leader Teamoversigt aggregate
    /// (<c>GET /api/approval/team-overview</c>). One row per employee in the actor's
    /// <b>designated-act-authority set</b> (ADR-027 D13 "see == act"), for the requested
    /// (<paramref name="year"/>, <paramref name="month"/>), <b>extended to emit zero-period
    /// reports</b>: an employee the leader may act on who has NO <c>approval_periods</c> row this
    /// month still appears, with a null period (a DRAFT row, no actions).
    ///
    /// <para>
    /// <b>Roster derivation (the correctness crux — NOT <c>/reports</c>, NOT period-first):</b>
    /// <list type="number">
    /// <item><description>the shared <see cref="DesignatedCandidateEmployeesCte"/> yields the
    /// tree-root-bounded candidate SUPERSET INDEPENDENTLY of periods (descend from the actor + every
    /// approver they vikar for, advance through INACTIVE sub-managers, same-tree);</description></item>
    /// <item><description>each candidate is filtered through the canonical R5 predicate
    /// (<see cref="DesignatedApproverAuthorizer.IsEffectiveDesignatedApproverAsync"/>) at
    /// <c>today</c> — keeps EXACTLY the employees the leader can act on, the SAME code the action
    /// endpoints authorize through (no see/act drift);</description></item>
    /// <item><description>each surviving employee is LEFT JOINed (in-memory) to their (year,month)
    /// <c>approval_periods</c> row — an employee with no period emits a row with a null period.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// Distinct from <see cref="GetByMonthForDesignatedReportsAsync"/> (period-FIRST: it joins
    /// <c>approval_periods</c> to the candidate CTE, so it can never surface a zero-period employee).
    /// This method enumerates the candidate EMPLOYEES (not their periods) and the LEFT JOIN to the
    /// month period is what makes the zero-period DRAFT row possible. <c>displayName</c> +
    /// <c>usersAgreementCode</c> come from <c>users</c> so a no-period row can still render a name +
    /// agreement. Read-only / additive — touches no write path and emits no events.
    /// </para>
    /// </summary>
    /// <param name="actorId">The acting leader (the JWT subject).</param>
    /// <param name="year">The requested year (validated by the caller).</param>
    /// <param name="month">The requested month 1–12 (validated by the caller).</param>
    public async Task<List<TeamOverviewRosterRow>> GetTeamOverviewRosterAsync(
        string actorId, int year, int month, CancellationToken ct = default)
    {
        var monthStart = new DateOnly(year, month, 1);
        var nextMonthStart = monthStart.AddMonths(1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // (1) Candidate EMPLOYEES (tree-root-bounded superset) + their users name/agreement, LEFT
        //     JOINed to the (year,month) period. One styrelse-tree-bounded query. The LEFT JOIN to
        //     approval_periods is what lets a candidate with NO period this month still come back
        //     (period_id NULL → the DRAFT zero-period row). DISTINCT ON (employee_id) collapses the
        //     (rare) multi-period-per-month case to the latest-starting period, deterministically.
        var sql = $"""
            {DesignatedCandidateEmployeesCte}
            SELECT DISTINCT ON (u.user_id)
                   u.user_id                AS employee_id,
                   u.display_name           AS display_name,
                   u.agreement_code         AS users_agreement_code,
                   ap.period_id             AS period_id
            FROM candidate_employees ce
            JOIN users u ON u.user_id = ce.employee_id
            LEFT JOIN approval_periods ap
                ON ap.employee_id = u.user_id
               AND ap.period_start < @nextMonthStart
               AND ap.period_end >= @monthStart
            ORDER BY u.user_id, ap.period_start DESC
            """;

        var candidateRows = new List<(string EmployeeId, string DisplayName, string UsersAgreementCode, Guid? PeriodId)>();
        await using (var conn = _connectionFactory.Create())
        {
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("actorId", actorId);
            cmd.Parameters.AddWithValue("today", today);
            cmd.Parameters.AddWithValue("monthStart", monthStart);
            cmd.Parameters.AddWithValue("nextMonthStart", nextMonthStart);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var empOrd = reader.GetOrdinal("employee_id");
            var nameOrd = reader.GetOrdinal("display_name");
            var agrOrd = reader.GetOrdinal("users_agreement_code");
            var pidOrd = reader.GetOrdinal("period_id");
            while (await reader.ReadAsync(ct))
            {
                candidateRows.Add((
                    reader.GetString(empOrd),
                    reader.GetString(nameOrd),
                    reader.GetString(agrOrd),
                    reader.IsDBNull(pidOrd) ? null : reader.GetGuid(pidOrd)));
            }
        }

        if (candidateRows.Count == 0)
            return new List<TeamOverviewRosterRow>();

        // (2) Filter the candidate EMPLOYEES through the canonical R5 predicate at today — exactly
        //     the same single-effective-approver decision the action endpoints + the my-reports
        //     dashboard authorize through (see == act, no drift). One decision per distinct employee.
        var result = new List<TeamOverviewRosterRow>(candidateRows.Count);
        foreach (var row in candidateRows)
        {
            // S105 / ADR-038 D4 — the SAME centralized "edge OR unit-leader" predicate (see == act on
            // the team-overview surface too — the candidate CTE now includes the actor's unit-led
            // members, so the filter must match it through the shared helper, full floors applied).
            var isApprover = await _designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                actorId, row.EmployeeId, asOf: today, ct: ct);
            if (!isApprover)
                continue;
            result.Add(new TeamOverviewRosterRow(
                EmployeeId: row.EmployeeId,
                DisplayName: row.DisplayName,
                UsersAgreementCode: row.UsersAgreementCode,
                PeriodId: row.PeriodId));
        }
        return result;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  S74-7404 R11a — per-styrelse period-status projection (read-only)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// S74-7404 R11a — the per-styrelse period-status projection for the redesigned
    /// Medarbejder-administration tree (FE Phases 2-3). For every ACTIVE employee whose
    /// <c>primary_org_id</c> sits within the styrelse subtree rooted at
    /// <paramref name="treeRootPathPrefix"/> (the tree-root org's <c>materialized_path</c>),
    /// returns:
    /// <list type="bullet">
    /// <item><description>their <b>last-closed-month</b> approval status — the
    /// <c>approval_periods</c> row with the greatest <c>period_end &lt; CURRENT_DATE</c>,
    /// projected to the FE's 3-state badge (OPEN / SUBMITTED / APPROVED) — or OPEN when the
    /// employee has no closed period at all;</description></item>
    /// <item><description>a <b>per-authorized-approver pending count</b> (S106 / TASK-10604 — the
    /// S105 EDGE-ONLY scope-out is CLOSED): for each employee who currently holds a
    /// SUBMITTED/EMPLOYEE_APPROVED (any-period) row awaiting a manager, the employee is tallied to
    /// EVERY user who may act on the period — the <em>resolved effective EDGE manager</em> at today
    /// (the vikar-aware R3 resolution) AND each designated leader of the employee's OWN unit + that
    /// leader's active vikar (the ADR-038 D4 secondary unit-leader path, single-table
    /// <c>E.unit_id</c>-bounded, NO ancestor walk). Each DISTINCT candidate is tallied <b>only when
    /// it passes the canonical edge-OR-unit-leader predicate</b>
    /// (<see cref="DesignatedApproverAuthorizer.IsEffectiveApproverOrUnitLeaderAsync"/> — active +
    /// LeaderOrAbove + (single-effective-edge OR direct-unit-leader) + same-Organisation), so each
    /// tile count MATCHES that manager's my-reports dashboard exactly: a role-revoked / inactive /
    /// bare-row candidate is NOT tallied (their dashboard would show zero too). <b>Semantic shift:</b>
    /// a pending employee now counts toward MULTIPLE managers' tiles (edge + each unit-leader/vikar),
    /// so Σ(tiles) ≥ pending count — NO LONGER "tallied exactly once" — which is correct (each
    /// approver who CAN act sees it). An employee whose pending period resolves to NO authorized
    /// approver at all (org-scope-only, no unit-leader) is not tallied to any manager.</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Status projection (R11a, raw → FE 3-state):</b> <c>DRAFT</c> / no closed period /
    /// <c>REJECTED</c> → <b>OPEN</b> (back to the employee); <c>SUBMITTED</c> (legacy) /
    /// <c>EMPLOYEE_APPROVED</c> → <b>SUBMITTED</b> (awaiting the manager); <c>APPROVED</c> →
    /// <b>APPROVED</b>.
    /// </para>
    ///
    /// <para>
    /// Read-only / additive — touches no write path and emits no events. The "greatest
    /// <c>period_end</c> &lt; today per employee" is a <c>DISTINCT ON (employee_id) … ORDER BY
    /// employee_id, period_end DESC</c> served by the S74 <c>idx_approval_employee_period_end</c>
    /// index <c>(employee_id, period_end DESC)</c>.
    /// </para>
    /// </summary>
    /// <param name="treeRootPathPrefix">The tree-root org's <c>materialized_path</c> (e.g.
    /// <c>/MIN01/STY02/</c>). Employees are scoped via <c>organizations.materialized_path LIKE
    /// prefix || '%'</c> — the same path-prefix idiom as <see cref="GetPendingByOrgPathAsync"/>.</param>
    public async Task<TreePeriodStatusProjection> GetPeriodStatusProjectionForTreeAsync(
        string treeRootPathPrefix, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        // (1) The per-employee last-closed-month status + the set of employees with an
        //     outstanding (SUBMITTED/EMPLOYEE_APPROVED, any period) row awaiting a manager.
        //     One round-trip: every active employee in the styrelse LEFT-JOINed to their
        //     greatest closed period and a pending-existence flag.
        await using var cmd = new NpgsqlCommand(
            """
            SELECT
                u.user_id            AS employee_id,
                u.display_name       AS display_name,
                lc.status            AS last_closed_status,
                pend.has_pending     AS has_pending
            FROM users u
            JOIN organizations o ON o.org_id = u.primary_org_id
            LEFT JOIN LATERAL (
                SELECT ap.status
                FROM approval_periods ap
                WHERE ap.employee_id = u.user_id
                  AND ap.period_end < CURRENT_DATE
                ORDER BY ap.period_end DESC
                LIMIT 1
            ) lc ON TRUE
            LEFT JOIN LATERAL (
                SELECT TRUE AS has_pending
                FROM approval_periods ap2
                WHERE ap2.employee_id = u.user_id
                  AND ap2.status IN ('SUBMITTED', 'EMPLOYEE_APPROVED')
                LIMIT 1
            ) pend ON TRUE
            WHERE u.is_active = TRUE
              AND o.materialized_path LIKE @pathPrefix ESCAPE '\'
            ORDER BY u.display_name, u.user_id
            """, conn);
        // Escape LIKE metacharacters in the (system-derived) path so a literal '%' or '_'
        // in an org id/path cannot widen the prefix into a wildcard (cross-styrelse over-match).
        cmd.Parameters.AddWithValue("pathPrefix", EscapeLike(treeRootPathPrefix) + "%");

        var employees = new List<EmployeePeriodStatus>();
        var pendingEmployeeIds = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            var statusOrd = reader.GetOrdinal("last_closed_status");
            var pendingOrd = reader.GetOrdinal("has_pending");
            while (await reader.ReadAsync(ct))
            {
                var employeeId = reader.GetString(reader.GetOrdinal("employee_id"));
                var displayName = reader.GetString(reader.GetOrdinal("display_name"));
                var rawStatus = reader.IsDBNull(statusOrd) ? null : reader.GetString(statusOrd);
                employees.Add(new EmployeePeriodStatus(employeeId, displayName, ProjectStatus(rawStatus)));
                if (!reader.IsDBNull(pendingOrd) && reader.GetBoolean(pendingOrd))
                    pendingEmployeeIds.Add(employeeId);
            }
        }

        // (2) Per-authorized-approver pending count (S106 / TASK-10604 — the S105 EDGE-ONLY scope-out
        //     CLOSED). For each pending employee we ENUMERATE the full set of users who may act on
        //     that period — NOT a single edge manager — and tally EACH authorized approver:
        //       (a) the single resolved EDGE manager at today (the vikar-aware R3 precedence —
        //           admin-ACTING → vikar → PRIMARY → inactive escalation); AND
        //       (b) the employee's OWN unit's designated leaders (unit_leaders on E.unit_id) + the
        //           ACTIVE vikars-of-those-leaders covering today — the INVERSE of the S105
        //           `unit_led_members` CTE (a single-table E.unit_id lookup, NO ancestor walk; the
        //           LOCKED D5 boundary). Self-excluded (a leader never tallies their OWN period).
        //     Each DISTINCT candidate is then GATED by the SAME centralized "edge OR unit-leader"
        //     predicate the action endpoints + the my-reports dashboard authorize through
        //     (IsEffectiveApproverOrUnitLeaderAsync — active + LeaderOrAbove + single-effective-edge
        //     OR direct-unit-leader + same-Organisation), so a role-revoked / inactive / bare-row
        //     candidate is NOT tallied (their dashboard would be empty too) and the tile counts MATCH
        //     each manager's my-reports surface EXACTLY (no tile↔dashboard drift).
        //
        //     SEMANTIC SHIFT (vs S105): a pending employee now counts toward MULTIPLE managers' tiles
        //     — the edge manager AND each authorized unit-leader / their vikar — so Σ(tiles) ≥ pending
        //     count (NO LONGER "tallied EXACTLY once"). This is correct: every approver who CAN act on
        //     the period sees it on their tile. This is candidate ENUMERATION, not a gate swap: gating
        //     the single edge manager through the unit-leader predicate would add NOTHING (he is not a
        //     unit-leader of the employee unless he holds the row). It propagates to the roster read's
        //     reused pendingCountByManager → the medarbejder-page tiles shift accordingly (intended).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var pendingCountByManager = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var employeeId in pendingEmployeeIds)
        {
            // Build the DISTINCT candidate-approver set for this pending employee.
            var candidates = new HashSet<string>(StringComparer.Ordinal);

            var (edgeManagerId, _, _) = await _reportingLineRepo.ResolveDesignatedApproverAsync(
                employeeId, ct, asOf: today);
            if (edgeManagerId is not null)
                candidates.Add(edgeManagerId);

            foreach (var unitApprover in await QueryUnitLeaderApproverCandidatesAsync(employeeId, today, ct))
                candidates.Add(unitApprover);

            // Tally each candidate who is an AUTHORIZED approver (edge OR unit-leader, full floors).
            foreach (var candidate in candidates)
            {
                var authorized = await _designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                    candidate, employeeId, asOf: today, ct: ct);
                if (!authorized)
                    continue;

                pendingCountByManager.TryGetValue(candidate, out var n);
                pendingCountByManager[candidate] = n + 1;
            }
        }

        return new TreePeriodStatusProjection(employees, pendingCountByManager);
    }

    /// <summary>
    /// S106 / TASK-10604 — the candidate unit-leader approvers of <paramref name="employeeId"/> at
    /// <paramref name="today"/>: the designated leaders of the employee's OWN unit
    /// (<c>unit_leaders.unit_id = users.unit_id</c>) PLUS the active <c>manager_vikar</c> stand-ins of
    /// those leaders (covering <paramref name="today"/>). This is the INVERSE of the S105
    /// <c>unit_led_members</c> CTE (there: leader → members; here: member → its unit's leaders/vikars),
    /// STRICTLY single-table <c>E.unit_id</c>-bounded — NO ancestor walk over
    /// <c>units.parent_unit_id</c> (the LOCKED D5 boundary). The employee themselves is excluded
    /// (a leader never tallies their OWN period — the S105 segregation-of-duties rule). A NULL
    /// <c>E.unit_id</c> yields the empty set. The returned ids are a SUPERSET candidate list — the
    /// caller applies the full floors via the shared edge-OR-unit-leader predicate.
    /// </summary>
    private async Task<List<string>> QueryUnitLeaderApproverCandidatesAsync(
        string employeeId, DateOnly today, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            -- (a) the employee's OWN unit's designated leaders (single-table unit_leaders on
            --     E.unit_id — NO ancestor walk; the LOCKED D5 boundary), self-excluded.
            SELECT ul.user_id AS approver_id
            FROM users e
            JOIN unit_leaders ul ON ul.unit_id = e.unit_id
            WHERE e.user_id = @employeeId
              AND e.unit_id IS NOT NULL
              AND ul.user_id <> @employeeId
            UNION
            -- (b) the ACTIVE vikars of those leaders, covering @today (the INVERSE of the S105
            --     unit_led_members path-3), self-excluded.
            SELECT mv.vikar_user_id AS approver_id
            FROM users e
            JOIN unit_leaders ul ON ul.unit_id = e.unit_id
            JOIN manager_vikar mv ON mv.absent_approver_id = ul.user_id
                 AND mv.effective_to IS NULL
                 AND mv.until_date >= @today
            WHERE e.user_id = @employeeId
              AND e.unit_id IS NOT NULL
              AND mv.vikar_user_id <> @employeeId
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("today", today);

        var result = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(reader.GetString(0));
        return result;
    }

    // ──────────────────────────────────────────────────────────────────────
    //  S75-7500 R1-R3 — the consolidated medarbejder-roster read (read-only)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// S75-7500 (R1-R3) — the consolidated medarbejder-roster read backing the redesigned
    /// Medarbejder-administration <b>structural tree</b> (FE Phase 2). For every ACTIVE user whose
    /// <c>primary_org_id</c> sits within the styrelse subtree rooted at
    /// <paramref name="treeRootPathPrefix"/> (the tree-root org's <c>materialized_path</c>) it
    /// returns one enriched roster row, plus the styrelse <c>pendingCountByManager</c> tally REUSED
    /// from <see cref="GetPeriodStatusProjectionForTreeAsync"/> unchanged.
    ///
    /// <para>
    /// <b>The tree is STRUCTURAL, not effective-approver routed (R2).</b> Each row's
    /// <c>structuralApproverId</c> is the person's <em>raw active PRIMARY</em>
    /// <c>reporting_lines.manager_id</c> (a LEFT JOIN, <c>relationship='PRIMARY' AND effective_to
    /// IS NULL</c>) — the assigned leder, NOT a <c>ResolveDesignatedApproverAsync</c> result. The
    /// no-resolver rule applies to the TREE KEY/construction; the ONLY per-person resolver use is
    /// the pre-existing, bounded <c>pendingCountByManager</c> tally (resolved per PENDING employee
    /// only), which is reused as-is.
    /// </para>
    ///
    /// <para>
    /// <b>Composition (one styrelse-bounded, set-based query for the roster + joins):</b>
    /// <list type="bullet">
    /// <item><description><c>position</c> = the live <c>employee_profiles.position</c> (for FE
    /// search; null when no live profile / unset);</description></item>
    /// <item><description><c>structuralApproverId</c> = the active PRIMARY edge's
    /// <c>manager_id</c> (null when the person has no active PRIMARY approver);</description></item>
    /// <item><description><c>outgoingVikar</c> = the person's OWN active <c>manager_vikar</c> row
    /// (where they are the <c>absent_approver_id</c>, <c>effective_to IS NULL</c>) with the vikar's
    /// resolved <c>display_name</c> — present iff THIS person is an away-manager covered by a vikar;
    /// drives the FE badge + Vikar tile. The DB's <c>uq_manager_vikar_active</c> guarantees at most
    /// one active row per absent approver, so the LEFT JOIN cannot fan-out the roster.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <c>periodStatus</c> reuses <see cref="GetPeriodStatusProjectionForTreeAsync"/>'s
    /// last-closed-month projection (OPEN/SUBMITTED/APPROVED) — the SAME rule + roster scope —
    /// joined by employee id; a roster row with no projected status (defensive) falls back to OPEN.
    /// </para>
    ///
    /// <para>
    /// <b>R3 — the deterministic root/orphan classification (one in-memory pass over the roster):</b>
    /// with no persisted root marker (ADR-027 D9 has none), a person with <em>no</em> active PRIMARY
    /// approver is an <c>isRoot</c> when they are the <c>structuralApproverId</c> of ≥1 other roster
    /// person (a people-hierarchy top), else an <c>isOrphan</c> (a legacy no-approver leaf gap). A
    /// person WITH an active PRIMARY approver is neither. Multiple roots may co-exist in messy/Phase-1
    /// trees (the ADR-027 D9 ≤1-root invariant is a Phase-4 enforcement concern, not enforced here).
    /// </para>
    ///
    /// <para>Read-only / additive — touches no write path and emits no events.</para>
    /// </summary>
    /// <param name="treeRootPathPrefix">The tree-root org's <c>materialized_path</c> (e.g.
    /// <c>/MIN01/STY02/</c>). The roster is scoped via <c>organizations.materialized_path LIKE
    /// prefix || '%'</c> — the same path-prefix idiom as
    /// <see cref="GetPeriodStatusProjectionForTreeAsync"/>.</param>
    public async Task<MedarbejderRosterProjection> GetMedarbejderRosterForTreeAsync(
        string treeRootPathPrefix, CancellationToken ct = default)
    {
        // (1) The structural roster + all joins in ONE styrelse-bounded, set-based query:
        //     every active styrelse user LEFT-JOINed to their active PRIMARY edge (the raw
        //     structuralApproverId — NO resolver), their live employee_profiles (position), and
        //     their OWN active manager_vikar row (outgoingVikar) with the vikar's display name.
        //     (S110 / TASK-11001: the vestigial enhedLabel/primary-org-name display field is gone.)
        var rosterRows = new List<RosterRow>();
        await using (var conn = _connectionFactory.Create())
        {
            await conn.OpenAsync(ct);
            await using var cmd = new NpgsqlCommand(
                """
                SELECT
                    u.user_id                AS employee_id,
                    u.display_name           AS display_name,
                    ep.position              AS position,
                    rl.manager_id            AS structural_approver_id,
                    rl.version               AS primary_reporting_line_version,
                    u.unit_id                AS unit_id,
                    un.name                  AS unit_name,
                    lead.leader_ids          AS leader_ids,
                    mv.vikar_user_id         AS vikar_user_id,
                    vu.display_name          AS vikar_display_name,
                    mv.until_date            AS vikar_until_date,
                    mv.reason                AS vikar_reason
                FROM users u
                JOIN organizations o ON o.org_id = u.primary_org_id
                LEFT JOIN reporting_lines rl
                    ON rl.employee_id = u.user_id
                    AND rl.relationship = 'PRIMARY'
                    AND rl.effective_to IS NULL
                LEFT JOIN employee_profiles ep
                    ON ep.employee_id = u.user_id
                    AND ep.effective_to IS NULL
                LEFT JOIN units un ON un.unit_id = u.unit_id
                -- S106 / TASK-10602 (Reviewer WARNING — avoid the multi-peer-leader fan-out): a unit
                -- has MULTIPLE "sideordnede" leaders, so a naive LEFT JOIN unit_leaders would yield one
                -- row per (employee × leader) and silently MULTIPLY the deliberately fan-out-free roster.
                -- The LATERAL AGGREGATES the leader ids into a single array, preserving ONE row per
                -- employee. NULL u.unit_id → no matching unit_leaders → array_agg returns NULL (→ []).
                LEFT JOIN LATERAL (
                    SELECT array_agg(ul.user_id ORDER BY ul.user_id) AS leader_ids
                    FROM unit_leaders ul
                    WHERE ul.unit_id = u.unit_id
                ) lead ON TRUE
                LEFT JOIN manager_vikar mv
                    ON mv.absent_approver_id = u.user_id
                    AND mv.effective_to IS NULL
                LEFT JOIN users vu ON vu.user_id = mv.vikar_user_id
                WHERE u.is_active = TRUE
                  AND o.materialized_path LIKE @pathPrefix ESCAPE '\'
                ORDER BY u.display_name, u.user_id
                """, conn);
            // Escape LIKE metacharacters in the (system-derived) path so a literal '%' or '_'
            // in an org id/path cannot widen the prefix into a wildcard (cross-styrelse over-match).
            cmd.Parameters.AddWithValue("pathPrefix", EscapeLike(treeRootPathPrefix) + "%");

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var empOrd = reader.GetOrdinal("employee_id");
            var nameOrd = reader.GetOrdinal("display_name");
            var posOrd = reader.GetOrdinal("position");
            var approverOrd = reader.GetOrdinal("structural_approver_id");
            var rlVersionOrd = reader.GetOrdinal("primary_reporting_line_version");
            var unitIdOrd = reader.GetOrdinal("unit_id");
            var unitNameOrd = reader.GetOrdinal("unit_name");
            var leaderIdsOrd = reader.GetOrdinal("leader_ids");
            var vikarIdOrd = reader.GetOrdinal("vikar_user_id");
            var vikarNameOrd = reader.GetOrdinal("vikar_display_name");
            var vikarUntilOrd = reader.GetOrdinal("vikar_until_date");
            var vikarReasonOrd = reader.GetOrdinal("vikar_reason");
            while (await reader.ReadAsync(ct))
            {
                OutgoingVikar? vikar = reader.IsDBNull(vikarIdOrd)
                    ? null
                    : new OutgoingVikar(
                        VikarUserId: reader.GetString(vikarIdOrd),
                        // Defensive: a (theoretical) dangling vikar_user_id falls back to the id.
                        VikarDisplayName: reader.IsDBNull(vikarNameOrd)
                            ? reader.GetString(vikarIdOrd)
                            : reader.GetString(vikarNameOrd),
                        UntilDate: reader.GetFieldValue<DateOnly>(vikarUntilOrd),
                        Reason: reader.GetString(vikarReasonOrd));

                // S106 / TASK-10602 — the unit tag + the etag. leader_ids is a Postgres text[]
                // (NULL → no unit / no leaders → an EMPTY list, never null, for a clean wire array).
                var leaderIds = reader.IsDBNull(leaderIdsOrd)
                    ? (IReadOnlyList<string>)Array.Empty<string>()
                    : reader.GetFieldValue<string[]>(leaderIdsOrd);

                rosterRows.Add(new RosterRow(
                    EmployeeId: reader.GetString(empOrd),
                    DisplayName: reader.GetString(nameOrd),
                    Position: reader.IsDBNull(posOrd) ? null : reader.GetString(posOrd),
                    StructuralApproverId: reader.IsDBNull(approverOrd) ? null : reader.GetString(approverOrd),
                    OutgoingVikar: vikar,
                    UnitId: reader.IsDBNull(unitIdOrd) ? null : reader.GetGuid(unitIdOrd),
                    UnitName: reader.IsDBNull(unitNameOrd) ? null : reader.GetString(unitNameOrd),
                    LeaderIds: leaderIds,
                    // The etag = the active PRIMARY reporting_lines.version (NULL when no active
                    // PRIMARY edge → root/orphan → the FE's "Ret" creates vs supersedes, S99).
                    PrimaryReportingLineVersion: reader.IsDBNull(rlVersionOrd) ? null : reader.GetInt64(rlVersionOrd)));
            }
        }

        // (2) periodStatus + pendingCountByManager: REUSE the existing S74 projection (same roster
        //     scope + same last-closed-month rule). S106 / TASK-10604: that tally now expands to the
        //     per-authorized-approver cardinality (edge manager + each unit-leader / their vikar) — the
        //     roster reuses it unchanged, so the medarbejder-page tiles shift accordingly (intended).
        var statusProjection = await GetPeriodStatusProjectionForTreeAsync(treeRootPathPrefix, ct);
        var statusByEmployee = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in statusProjection.Employees)
            statusByEmployee[e.EmployeeId] = e.Status;

        // (3) R3 root/orphan: one in-memory pass. A person is an effective people-hierarchy parent
        //     iff they appear as some roster person's structuralApproverId. Then: no-approver +
        //     is-a-parent = root; no-approver + parents-no-one = orphan; has-approver = neither.
        var approverIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rosterRows)
            if (r.StructuralApproverId is not null)
                approverIds.Add(r.StructuralApproverId);

        var employees = new List<MedarbejderRosterRow>(rosterRows.Count);
        foreach (var r in rosterRows)
        {
            var hasApprover = r.StructuralApproverId is not null;
            var approvesSomeone = approverIds.Contains(r.EmployeeId);
            var isRoot = !hasApprover && approvesSomeone;
            var isOrphan = !hasApprover && !approvesSomeone;
            employees.Add(new MedarbejderRosterRow(
                EmployeeId: r.EmployeeId,
                DisplayName: r.DisplayName,
                Position: r.Position,
                StructuralApproverId: r.StructuralApproverId,
                // Defensive fallback to OPEN if a roster row has no projected status (rosters are
                // the same scope, so every row should be present).
                PeriodStatus: statusByEmployee.GetValueOrDefault(r.EmployeeId, "OPEN"),
                OutgoingVikar: r.OutgoingVikar,
                IsRoot: isRoot,
                IsOrphan: isOrphan,
                UnitId: r.UnitId,
                UnitName: r.UnitName,
                LeaderIds: r.LeaderIds,
                PrimaryReportingLineVersion: r.PrimaryReportingLineVersion));
        }

        // (4) S106 / TASK-10602 — the DISPLAY-ONLY name resolution. Collect every id the roster
        //     REFERENCES (each row's structuralApproverId = the "Refererer opad til" upward-ref ∪ all
        //     unit leaderIds = the cross-unit-leader chips) and resolve their name/title/unit by id.
        //     This covers a referenced person who is NOT an active in-roster row — an inactive
        //     manager/leader, or (defensively) a leader homed outside the loaded subtree. It is a
        //     by-id name fetch ONLY (no org-scope predicate, no is_active filter) and therefore admits
        //     NObody into scope: it resolves ONLY ids already referenced by the in-scope roster rows.
        var referencedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in rosterRows)
        {
            if (r.StructuralApproverId is not null)
                referencedIds.Add(r.StructuralApproverId);
            foreach (var leaderId in r.LeaderIds)
                referencedIds.Add(leaderId);
        }
        var nameResolution = await ResolvePersonRefsByIdAsync(referencedIds, ct);

        return new MedarbejderRosterProjection(employees, statusProjection.PendingCountByManager, nameResolution);
    }

    /// <summary>
    /// S106 / TASK-10602 — the DISPLAY-ONLY by-id name resolver behind
    /// <see cref="MedarbejderRosterProjection.NameResolution"/>. Given the set of ids the roster
    /// REFERENCES (managers + unit leaders), returns each one's display name + live position + unit
    /// name. A pure <c>user_id = ANY(@ids)</c> lookup — NO org-scope predicate and NO
    /// <c>is_active</c> filter (so it resolves an inactive / out-of-subtree referenced person too).
    /// This admits NObody into scope: it only labels ids the in-scope roster already references.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, ResolvedPersonRef>> ResolvePersonRefsByIdAsync(
        IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        var result = new Dictionary<string, ResolvedPersonRef>(StringComparer.Ordinal);
        if (ids.Count == 0)
            return result;

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT
                u.user_id       AS user_id,
                u.display_name  AS display_name,
                ep.position     AS position,
                un.name         AS unit_name
            FROM users u
            LEFT JOIN employee_profiles ep
                ON ep.employee_id = u.user_id
                AND ep.effective_to IS NULL
            LEFT JOIN units un ON un.unit_id = u.unit_id
            WHERE u.user_id = ANY(@ids)
            """, conn);
        cmd.Parameters.AddWithValue("ids", ids.ToArray());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var idOrd = reader.GetOrdinal("user_id");
        var nameOrd = reader.GetOrdinal("display_name");
        var posOrd = reader.GetOrdinal("position");
        var unitOrd = reader.GetOrdinal("unit_name");
        while (await reader.ReadAsync(ct))
        {
            var userId = reader.GetString(idOrd);
            result[userId] = new ResolvedPersonRef(
                UserId: userId,
                DisplayName: reader.GetString(nameOrd),
                Position: reader.IsDBNull(posOrd) ? null : reader.GetString(posOrd),
                UnitName: reader.IsDBNull(unitOrd) ? null : reader.GetString(unitOrd));
        }
        return result;
    }

    /// <summary>Internal pre-classification roster row (S75-7500; S106 unit tag). Holds the joined
    /// fields before the R3 root/orphan pass folds in <c>isRoot</c>/<c>isOrphan</c>.</summary>
    private sealed record RosterRow(
        string EmployeeId,
        string DisplayName,
        string? Position,
        string? StructuralApproverId,
        OutgoingVikar? OutgoingVikar,
        Guid? UnitId,
        string? UnitName,
        IReadOnlyList<string> LeaderIds,
        long? PrimaryReportingLineVersion);

    /// <summary>
    /// Maps a raw <c>approval_periods.status</c> (or <c>null</c> = no closed period) to the
    /// R11a FE 3-state badge. See <see cref="GetPeriodStatusProjectionForTreeAsync"/>.
    /// </summary>
    internal static string ProjectStatus(string? rawStatus) => rawStatus switch
    {
        "APPROVED" => "APPROVED",
        "SUBMITTED" or "EMPLOYEE_APPROVED" => "SUBMITTED",
        // DRAFT, REJECTED, null (no closed period), and any unexpected value → OPEN.
        _ => "OPEN",
    };

    // ──────────────────────────────────────────────────────────────────────
    //  S74-7404 R11b — server-side person-search (read-only, paginated)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// S74-7404 R11b — the server-side person-search backing the approver/person picker (the
    /// prototype's client-side 60-cap is not the production answer at 2000+ employees). A REAL
    /// paginated DB query (a shared <c>matched</c> CTE feeding a scalar <c>COUNT(*)</c> total +
    /// the <c>LIMIT/OFFSET</c> page in one round-trip — NOT load-all-then-filter; the scalar count
    /// reports the true total even on a valid EMPTY trailing page, which a <c>COUNT(*) OVER()</c>
    /// window over the page rows would report as <c>0</c>). Each matched user carries their primary-org
    /// name (S110 / TASK-11001 removed the vestigial <c>enhedLabel</c> display field — the
    /// <c>employee_profiles.enhed_label</c> column was dropped in S103 and the field duplicated the org
    /// name). Read-only / additive.
    ///
    /// <list type="bullet">
    /// <item><description><b>Match:</b> case-insensitive substring on <c>display_name</c> OR
    /// <c>username</c> (ILIKE <c>%q%</c>). An empty/whitespace <paramref name="q"/> matches all
    /// (in-scope) users — the picker's initial page.</description></item>
    /// <item><description><b>Scope filter:</b> <paramref name="accessibleOrgIds"/> is the actor's
    /// RBAC org-scope as resolved by <c>OrgScopeValidator.GetAccessibleOrgsAsync</c> — a
    /// <c>null</c> value is the GLOBAL/GlobalAdmin "no filter" sentinel (unrestricted), an EMPTY
    /// list admits nobody (the no-scope/Employee case the endpoint already 403s), and otherwise
    /// only users whose <c>primary_org_id = ANY(...)</c> are returned.</description></item>
    /// <item><description><b>Self + descendant exclusion:</b> <paramref name="excludedUserIds"/>
    /// (self ∪ descendants, the caller computes via
    /// <c>ReportingLineRepository.GetDescendantIdsAsync</c> — the cycle-prevention mirror for the
    /// picker) are filtered out with <c>user_id &lt;&gt; ALL(...)</c>.</description></item>
    /// <item><description><b>Pagination:</b> stable ORDER BY display_name, user_id; LIMIT/OFFSET.
    /// <paramref name="total"/> is the full match count BEFORE the page slice.</description></item>
    /// </list>
    /// </summary>
    /// <param name="q">The (possibly empty) search term.</param>
    /// <param name="accessibleOrgIds"><c>null</c> ⇒ unrestricted (GLOBAL); otherwise the set of
    /// org_ids the actor may see.</param>
    /// <param name="excludedUserIds">User ids to exclude server-side (self + descendants).</param>
    /// <param name="limit">Page size (already clamped by the caller to a sane cap).</param>
    /// <param name="offset">Page offset (>= 0).</param>
    public async Task<(IReadOnlyList<PersonSearchHit> Items, int Total)> SearchPeopleAsync(
        string q,
        IReadOnlyList<string>? accessibleOrgIds,
        IReadOnlyCollection<string> excludedUserIds,
        int limit,
        int offset,
        CancellationToken ct = default)
    {
        // GLOBAL (null) = no org filter; empty list = admit nobody.
        var unrestricted = accessibleOrgIds is null;
        if (!unrestricted && accessibleOrgIds!.Count == 0)
            return (Array.Empty<PersonSearchHit>(), 0);

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var term = (q ?? string.Empty).Trim();
        var pattern = "%" + EscapeLike(term) + "%";

        // One round-trip, ONE statement, three CTEs. The filter is written ONCE (`matched`) and
        // feeds BOTH the total (`total`, an always-one-row COUNT) and the page slice (`page`,
        // LIMIT/OFFSET). The final SELECT does `total LEFT JOIN page ON TRUE`, so:
        //   • when the page has rows → one result row per page row, each carrying total_count;
        //   • when the page is EMPTY (a valid trailing page, offset >= total) → the LEFT JOIN
        //     keeps the single `total` row with NULL page columns, so the true count STILL comes
        //     back (user_id is NULL → the reader skips it as a non-item row).
        // This is what the old `COUNT(*) OVER()` window could not do: that window was attached to
        // the page rows, so an empty trailing page returned NO row and total wrongly read as 0.
        // The org-scope and exclusion predicates are bound parameters (a NULL accessibleOrgIds
        // skips the org clause). The org/profile joins live in `page` so they cost nothing for the
        // count, and the whole thing is ONE logical operation (no load-all-then-filter).
        await using var cmd = new NpgsqlCommand(
            """
            WITH matched AS (
                SELECT u.user_id, u.display_name, u.primary_org_id
                FROM users u
                WHERE u.is_active = TRUE
                  AND (@term = '' OR u.display_name ILIKE @pattern ESCAPE '\' OR u.username ILIKE @pattern ESCAPE '\')
                  AND (@unrestricted OR u.primary_org_id = ANY(@orgIds))
                  AND (cardinality(@excludedIds) = 0 OR u.user_id <> ALL(@excludedIds))
            ),
            total AS (
                SELECT COUNT(*) AS total_count FROM matched
            ),
            page AS (
                SELECT
                    m.user_id,
                    m.display_name,
                    o.org_name        AS primary_org_name
                FROM matched m
                JOIN organizations o ON o.org_id = m.primary_org_id
                ORDER BY m.display_name, m.user_id
                LIMIT @limit OFFSET @offset
            )
            SELECT
                t.total_count        AS total_count,
                p.user_id            AS user_id,
                p.display_name       AS display_name,
                p.primary_org_name   AS primary_org_name
            FROM total t
            LEFT JOIN page p ON TRUE
            ORDER BY p.display_name, p.user_id
            """, conn);
        cmd.Parameters.AddWithValue("term", term);
        cmd.Parameters.AddWithValue("pattern", pattern);
        cmd.Parameters.AddWithValue("unrestricted", unrestricted);
        cmd.Parameters.AddWithValue("orgIds", (object?)accessibleOrgIds?.ToArray() ?? Array.Empty<string>());
        cmd.Parameters.AddWithValue("excludedIds", excludedUserIds.ToArray());
        cmd.Parameters.AddWithValue("limit", limit);
        cmd.Parameters.AddWithValue("offset", offset);

        // ONE result set: every row carries total_count; a row with a NULL user_id is the
        // empty-page sentinel (the `total LEFT JOIN page ON TRUE` row that survives when the page
        // slice is empty) — read its total but DON'T add it as an item.
        var items = new List<PersonSearchHit>();
        var total = 0;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var totalOrd = reader.GetOrdinal("total_count");
        var userOrd = reader.GetOrdinal("user_id");
        while (await reader.ReadAsync(ct))
        {
            total = (int)reader.GetInt64(totalOrd); // identical on every row
            if (reader.IsDBNull(userOrd))
                continue; // the empty-page sentinel row — total only, no item.
            items.Add(new PersonSearchHit(
                UserId: reader.GetString(userOrd),
                DisplayName: reader.GetString(reader.GetOrdinal("display_name")),
                PrimaryOrgName: reader.GetString(reader.GetOrdinal("primary_org_name"))));
        }
        return (items, total);
    }

    /// <summary>
    /// S106 / TASK-10603 — the scope-bounded people SEARCH for the merged-admin overlay's MEDARBEJDERE
    /// section. A SIBLING of <see cref="SearchPeopleAsync"/> (NEW method — the existing roster/picker
    /// reads are untouched) that ADDS (a) an <c>email</c> match arm and (b) the live
    /// <c>employee_profiles.position</c> + the person's <c>unit_id</c> + <c>primary_org_id</c> so the
    /// endpoint can build the overlay's unit/Organisation PATH + the home-unit name. Case-insensitive
    /// substring (ILIKE <c>%q%</c>) on <c>display_name</c> OR <c>username</c> OR <c>email</c>; an
    /// empty/whitespace <paramref name="q"/> matches ALL in-scope people (mirrors
    /// <see cref="SearchPeopleAsync"/>). ACTIVE users only.
    ///
    /// <para><b>Scope (the SAME admission as SearchPeopleAsync + the forest):</b>
    /// <paramref name="accessibleOrgIds"/> is the actor's accessible-org set —
    /// <c>null</c> = GLOBAL/unrestricted, EMPTY = admit nobody, otherwise
    /// <c>primary_org_id = ANY(...)</c>. A scoped HR gets NO cross-Organisation people (D5: the person
    /// is admitted by their Organisation, never by a unit).</para>
    ///
    /// <para>One round-trip / one statement / three CTEs (matched → total → page) so the count is exact
    /// even on an empty trailing page (the SearchPeopleAsync pattern — a NULL <c>user_id</c> row is the
    /// count-only sentinel).</para>
    /// </summary>
    public async Task<(IReadOnlyList<OverlayPersonRow> Items, int Total)> SearchPeopleForOverlayAsync(
        string q,
        IReadOnlyList<string>? accessibleOrgIds,
        int limit,
        int offset,
        CancellationToken ct = default)
    {
        var unrestricted = accessibleOrgIds is null;
        if (!unrestricted && accessibleOrgIds!.Count == 0)
            return (Array.Empty<OverlayPersonRow>(), 0);

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        var term = (q ?? string.Empty).Trim();
        var pattern = "%" + EscapeLike(term) + "%";

        await using var cmd = new NpgsqlCommand(
            """
            WITH matched AS (
                SELECT u.user_id, u.display_name, u.unit_id, u.primary_org_id
                FROM users u
                WHERE u.is_active = TRUE
                  AND (@term = ''
                       OR u.display_name ILIKE @pattern ESCAPE '\'
                       OR u.username ILIKE @pattern ESCAPE '\'
                       OR (u.email IS NOT NULL AND u.email ILIKE @pattern ESCAPE '\'))
                  AND (@unrestricted OR u.primary_org_id = ANY(@orgIds))
            ),
            total AS (
                SELECT COUNT(*) AS total_count FROM matched
            ),
            page AS (
                SELECT
                    m.user_id,
                    m.display_name,
                    m.unit_id,
                    m.primary_org_id,
                    ep.position AS position
                FROM matched m
                LEFT JOIN employee_profiles ep
                    ON ep.employee_id = m.user_id
                    AND ep.effective_to IS NULL
                ORDER BY m.display_name, m.user_id
                LIMIT @limit OFFSET @offset
            )
            SELECT
                t.total_count        AS total_count,
                p.user_id            AS user_id,
                p.display_name       AS display_name,
                p.position           AS position,
                p.unit_id            AS unit_id,
                p.primary_org_id     AS primary_org_id
            FROM total t
            LEFT JOIN page p ON TRUE
            ORDER BY p.display_name, p.user_id
            """, conn);
        cmd.Parameters.AddWithValue("term", term);
        cmd.Parameters.AddWithValue("pattern", pattern);
        cmd.Parameters.AddWithValue("unrestricted", unrestricted);
        cmd.Parameters.AddWithValue("orgIds", (object?)accessibleOrgIds?.ToArray() ?? Array.Empty<string>());
        cmd.Parameters.AddWithValue("limit", limit);
        cmd.Parameters.AddWithValue("offset", offset);

        var items = new List<OverlayPersonRow>();
        var total = 0;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var totalOrd = reader.GetOrdinal("total_count");
        var userOrd = reader.GetOrdinal("user_id");
        var posOrd = reader.GetOrdinal("position");
        var unitOrd = reader.GetOrdinal("unit_id");
        var orgOrd = reader.GetOrdinal("primary_org_id");
        while (await reader.ReadAsync(ct))
        {
            total = (int)reader.GetInt64(totalOrd); // identical on every row
            if (reader.IsDBNull(userOrd))
                continue; // the empty-page sentinel row — total only, no item.
            items.Add(new OverlayPersonRow(
                UserId: reader.GetString(userOrd),
                DisplayName: reader.GetString(reader.GetOrdinal("display_name")),
                Position: reader.IsDBNull(posOrd) ? null : reader.GetString(posOrd),
                UnitId: reader.IsDBNull(unitOrd) ? null : reader.GetGuid(unitOrd),
                PrimaryOrgId: reader.GetString(orgOrd)));
        }
        return (items, total);
    }

    /// <summary>
    /// Escapes the LIKE/ILIKE wildcard metacharacters (<c>\</c>, <c>%</c>, <c>_</c>) in a raw
    /// user-supplied search term so they are matched literally (the query uses
    /// <c>ESCAPE '\'</c>). Without this, a user typing <c>%</c> or <c>_</c> would inject a
    /// wildcard into the pattern.
    /// </summary>
    private static string EscapeLike(string raw) => raw
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    /// <summary>
    /// Self-managed overload of <see cref="CreateAsync(NpgsqlConnection, NpgsqlTransaction, ApprovalPeriod, CancellationToken)"/>:
    /// opens its own connection (no transaction). For atomic outbox + audit + state mutation
    /// (ADR-018 D3) call the in-transaction sibling.
    /// </summary>
    public async Task<Guid> CreateAsync(ApprovalPeriod period, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        var periodId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version)
            VALUES (@periodId, @employeeId, @orgId, @periodStart, @periodEnd, @periodType, @status, @agreementCode, @okVersion)
            """, conn);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("employeeId", period.EmployeeId);
        cmd.Parameters.AddWithValue("orgId", period.OrgId);
        cmd.Parameters.AddWithValue("periodStart", period.PeriodStart);
        cmd.Parameters.AddWithValue("periodEnd", period.PeriodEnd);
        cmd.Parameters.AddWithValue("periodType", period.PeriodType);
        cmd.Parameters.AddWithValue("status", period.Status);
        cmd.Parameters.AddWithValue("agreementCode", period.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", period.OkVersion);
        await cmd.ExecuteNonQueryAsync(ct);
        return periodId;
    }

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="CreateAsync(ApprovalPeriod, CancellationToken)"/>. Reuses the caller-supplied
    /// <paramref name="conn"/> + <paramref name="tx"/> so the caller can extend the same
    /// transaction across audit + outbox writes (ADR-018 D3 transactional-outbox contract).
    /// The caller commits or rolls back; this method does NOT.
    /// </summary>
    public async Task<Guid> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        ApprovalPeriod period, CancellationToken ct = default)
    {
        var periodId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version)
            VALUES (@periodId, @employeeId, @orgId, @periodStart, @periodEnd, @periodType, @status, @agreementCode, @okVersion)
            """, conn, tx);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("employeeId", period.EmployeeId);
        cmd.Parameters.AddWithValue("orgId", period.OrgId);
        cmd.Parameters.AddWithValue("periodStart", period.PeriodStart);
        cmd.Parameters.AddWithValue("periodEnd", period.PeriodEnd);
        cmd.Parameters.AddWithValue("periodType", period.PeriodType);
        cmd.Parameters.AddWithValue("status", period.Status);
        cmd.Parameters.AddWithValue("agreementCode", period.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", period.OkVersion);
        await cmd.ExecuteNonQueryAsync(ct);
        return periodId;
    }

    public async Task UpdateStatusAsync(
        Guid periodId, string status, string? actorId = null,
        string? rejectionReason = null,
        string? designatedApproverId = null, string? approvalMethod = null,
        CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = BuildUpdateStatusCommand(conn, null, periodId, status, actorId, rejectionReason, designatedApproverId, approvalMethod);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="UpdateStatusAsync(Guid, string, string?, string?, string?, string?, CancellationToken)"/>.
    /// </summary>
    public async Task UpdateStatusAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid periodId, string status, string? actorId = null,
        string? rejectionReason = null,
        string? designatedApproverId = null, string? approvalMethod = null,
        CancellationToken ct = default)
    {
        await using var cmd = BuildUpdateStatusCommand(conn, tx, periodId, status, actorId, rejectionReason, designatedApproverId, approvalMethod);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// S78 R2 — the CONDITIONAL in-tx status transition. Identical to the in-tx
    /// <see cref="UpdateStatusAsync(NpgsqlConnection, NpgsqlTransaction, Guid, string, string?, string?, string?, string?, CancellationToken)"/>
    /// overload but the UPDATE additionally guards <c>status = ANY(allowedSourceStates)</c>, so it ONLY
    /// transitions a row that is still in an expected source state.
    ///
    /// <para>
    /// <b>S78 BLOCKER 1 — the locked-in previous status.</b> The UPDATE captures the row's pre-update
    /// status ATOMICALLY (a <c>FROM (SELECT status … FOR UPDATE)</c> snapshot + <c>RETURNING</c>), so the
    /// caller records the status that was actually present at the moment the conditional UPDATE committed —
    /// NOT a stale pre-tx read that a concurrent committed transition may have already moved past. The
    /// returned value is:
    /// <list type="bullet">
    /// <item><description>the <b>old status string</b> (e.g. <c>"SUBMITTED"</c>, <c>"APPROVED"</c>) when
    /// the transition won the race (exactly one row matched the allowed source set); the caller uses THIS
    /// for the audit <c>previous_status</c> / the event <c>PreviousStatus</c> — accurate even when a
    /// concurrent transition committed between the pre-tx read and the locked UPDATE;</description></item>
    /// <item><description><c>null</c> when a concurrent transaction already moved the row out of every
    /// allowed source state (0 rows — the loser of a double-transition; the caller returns a clean 409).</description></item>
    /// </list>
    /// This MUST be the FIRST mutation in the action tx so a <c>null</c> (0-row) result short-circuits
    /// with NO FallbackTraversalWarning enqueue, NO audit row, and NO action outbox event written.
    /// </para>
    /// </summary>
    public async Task<string?> TryUpdateStatusConditionalAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid periodId, string status, IReadOnlyList<string> allowedSourceStates, string? actorId = null,
        string? rejectionReason = null,
        string? designatedApproverId = null, string? approvalMethod = null,
        CancellationToken ct = default)
    {
        await using var cmd = BuildUpdateStatusCommand(
            conn, tx, periodId, status, actorId, rejectionReason, designatedApproverId, approvalMethod,
            allowedSourceStates);
        // The conditional form RETURNs the captured pre-update status (BLOCKER 1). A NULL/absent scalar
        // means 0 rows matched the allowed source set → the caller returns a clean 409.
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is string s ? s : null;
    }

    private static NpgsqlCommand BuildUpdateStatusCommand(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        Guid periodId, string status, string? actorId, string? rejectionReason,
        string? designatedApproverId = null, string? approvalMethod = null,
        IReadOnlyList<string>? allowedSourceStates = null)
    {
        var conditional = allowedSourceStates is { Count: > 0 };

        // The SET clause per target status (the WHERE/RETURNING shape is appended below). The
        // CONDITIONAL path (S78 R2 + BLOCKER 1) aliases the target table as `t`, joins a
        // `FROM (SELECT status … FOR UPDATE)` snapshot of the row's PRE-update status, guards on the
        // allowed source set, and RETURNs that snapshotted old status — so the caller records the
        // status that was actually present at the locked UPDATE, not a stale pre-tx read.
        var setClause = status switch
        {
            "SUBMITTED" => "status = 'SUBMITTED', submitted_at = NOW(), submitted_by = @actorId",
            "EMPLOYEE_APPROVED" => "status = 'EMPLOYEE_APPROVED', employee_approved_at = NOW(), employee_approved_by = @actorId",
            "APPROVED" => "status = 'APPROVED', approved_by = @actorId, approved_at = NOW(), designated_approver_id = @designatedApproverId, approval_method = @approvalMethod",
            "REJECTED" => "status = 'REJECTED', approved_by = @actorId, approved_at = NOW(), rejection_reason = @rejectionReason, designated_approver_id = @designatedApproverId, approval_method = @approvalMethod",
            "DRAFT" => "status = 'DRAFT', submitted_at = NULL, submitted_by = NULL, approved_by = NULL, approved_at = NULL, rejection_reason = NULL, employee_approved_at = NULL, employee_approved_by = NULL",
            _ => throw new ArgumentException($"Invalid status: {status}")
        };

        string sql;
        if (conditional)
        {
            // BLOCKER 1: snapshot the pre-update status in-statement (FOR UPDATE row-locks it before SET
            // applies) and RETURN it. The outer WHERE re-guards the same period AND the allowed source
            // set, so a row already moved out of the allowed states matches 0 rows → no scalar returned →
            // the caller treats null as the 409 loser. `status = ANY(@allowedSourceStates)` is a bound
            // parameter (the set is data, not interpolated SQL).
            sql = $"""
                UPDATE approval_periods AS t
                SET {setClause}
                FROM (SELECT status AS old_status FROM approval_periods WHERE period_id = @periodId FOR UPDATE) AS prev
                WHERE t.period_id = @periodId AND t.status = ANY(@allowedSourceStates)
                RETURNING prev.old_status
                """;
        }
        else
        {
            // The UNCONDITIONAL legacy path (submit / employee-approve / sequential reopen) — unchanged
            // shape, no RETURNING, ExecuteNonQuery by the non-conditional callers.
            sql = $"UPDATE approval_periods SET {setClause} WHERE period_id = @periodId";
        }

        var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("actorId", (object?)actorId ?? DBNull.Value);
        if (status == "REJECTED")
            cmd.Parameters.AddWithValue("rejectionReason", (object?)rejectionReason ?? DBNull.Value);
        if (status is "APPROVED" or "REJECTED")
        {
            cmd.Parameters.AddWithValue("designatedApproverId", (object?)designatedApproverId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("approvalMethod", (object?)approvalMethod ?? DBNull.Value);
        }
        if (conditional)
            cmd.Parameters.AddWithValue("allowedSourceStates", allowedSourceStates!.ToArray());
        return cmd;
    }

    public async Task UpdateDeadlinesAsync(
        Guid periodId, DateOnly? employeeDeadline, DateOnly? managerDeadline, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE approval_periods SET employee_deadline = @employeeDeadline, manager_deadline = @managerDeadline WHERE period_id = @periodId", conn);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("employeeDeadline", (object?)employeeDeadline ?? DBNull.Value);
        cmd.Parameters.AddWithValue("managerDeadline", (object?)managerDeadline ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="UpdateDeadlinesAsync(Guid, DateOnly?, DateOnly?, CancellationToken)"/>.
    /// </summary>
    public async Task UpdateDeadlinesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid periodId, DateOnly? employeeDeadline, DateOnly? managerDeadline, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE approval_periods SET employee_deadline = @employeeDeadline, manager_deadline = @managerDeadline WHERE period_id = @periodId",
            conn, tx);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("employeeDeadline", (object?)employeeDeadline ?? DBNull.Value);
        cmd.Parameters.AddWithValue("managerDeadline", (object?)managerDeadline ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AppendAuditAsync(
        Guid periodId, string action, string actorId, string actorRole,
        string? comment = null, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_audit (period_id, action, actor_id, actor_role, comment)
            VALUES (@periodId, @action, @actorId, @actorRole, @comment)
            """, conn);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        cmd.Parameters.AddWithValue("comment", (object?)comment ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="AppendAuditAsync(Guid, string, string, string, string?, CancellationToken)"/>.
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid periodId, string action, string actorId, string actorRole,
        string? comment = null, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_audit (period_id, action, actor_id, actor_role, comment)
            VALUES (@periodId, @action, @actorId, @actorRole, @comment)
            """, conn, tx);
        cmd.Parameters.AddWithValue("periodId", periodId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        cmd.Parameters.AddWithValue("comment", (object?)comment ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<ApprovalPeriod>> ReadPeriodsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var periods = new List<ApprovalPeriod>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            periods.Add(ReadPeriod(reader));
        return periods;
    }

    private static ApprovalPeriod ReadPeriod(NpgsqlDataReader reader) => new()
    {
        PeriodId = reader.GetGuid(reader.GetOrdinal("period_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        OrgId = reader.GetString(reader.GetOrdinal("org_id")),
        PeriodStart = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("period_start"))),
        PeriodEnd = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("period_end"))),
        PeriodType = reader.GetString(reader.GetOrdinal("period_type")),
        Status = reader.GetString(reader.GetOrdinal("status")),
        SubmittedAt = reader.IsDBNull(reader.GetOrdinal("submitted_at")) ? null : reader.GetDateTime(reader.GetOrdinal("submitted_at")),
        SubmittedBy = reader.IsDBNull(reader.GetOrdinal("submitted_by")) ? null : reader.GetString(reader.GetOrdinal("submitted_by")),
        ApprovedBy = reader.IsDBNull(reader.GetOrdinal("approved_by")) ? null : reader.GetString(reader.GetOrdinal("approved_by")),
        ApprovedAt = reader.IsDBNull(reader.GetOrdinal("approved_at")) ? null : reader.GetDateTime(reader.GetOrdinal("approved_at")),
        RejectionReason = reader.IsDBNull(reader.GetOrdinal("rejection_reason")) ? null : reader.GetString(reader.GetOrdinal("rejection_reason")),
        EmployeeApprovedAt = reader.IsDBNull(reader.GetOrdinal("employee_approved_at")) ? null : reader.GetDateTime(reader.GetOrdinal("employee_approved_at")),
        EmployeeApprovedBy = reader.IsDBNull(reader.GetOrdinal("employee_approved_by")) ? null : reader.GetString(reader.GetOrdinal("employee_approved_by")),
        EmployeeDeadline = reader.IsDBNull(reader.GetOrdinal("employee_deadline")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("employee_deadline"))),
        ManagerDeadline = reader.IsDBNull(reader.GetOrdinal("manager_deadline")) ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("manager_deadline"))),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        DesignatedApproverId = reader.IsDBNull(reader.GetOrdinal("designated_approver_id")) ? null : reader.GetString(reader.GetOrdinal("designated_approver_id")),
        ApprovalMethod = reader.IsDBNull(reader.GetOrdinal("approval_method")) ? null : reader.GetString(reader.GetOrdinal("approval_method")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}

/// <summary>
/// S74-7404 R11a — the per-styrelse period-status projection result
/// (<see cref="ApprovalPeriodRepository.GetPeriodStatusProjectionForTreeAsync"/>). Carries the
/// per-employee last-closed-month status badges and the per-manager pending-count map for the
/// FE tree + filter-tile counts. Read-only / additive.
/// </summary>
/// <param name="Employees">One row per active employee in the styrelse subtree, with their
/// projected last-closed-month status.</param>
/// <param name="PendingCountByManager">manager user_id → number of that manager's effective
/// reports currently holding a SUBMITTED/EMPLOYEE_APPROVED period awaiting them.</param>
public sealed record TreePeriodStatusProjection(
    IReadOnlyList<EmployeePeriodStatus> Employees,
    IReadOnlyDictionary<string, int> PendingCountByManager);

/// <summary>
/// One employee's last-closed-month period status badge (S74-7404 R11a). <paramref name="Status"/>
/// is the FE 3-state projection: <c>OPEN</c> / <c>SUBMITTED</c> / <c>APPROVED</c>.
/// </summary>
public sealed record EmployeePeriodStatus(
    string EmployeeId,
    string DisplayName,
    string Status);

/// <summary>
/// S75-7500 (R1-R3) — the consolidated medarbejder-roster projection the structural Medarbejder-
/// administration tree consumes (<see cref="ApprovalPeriodRepository.GetMedarbejderRosterForTreeAsync"/>).
/// One enriched row per active styrelse user + the styrelse <c>pendingCountByManager</c> tally
/// REUSED from <see cref="TreePeriodStatusProjection"/> unchanged. Read-only / additive.
/// </summary>
/// <param name="Employees">The full styrelse roster, each enriched with the structural approver
/// (raw PRIMARY edge), position, last-closed-month status, the outgoing-vikar marker,
/// the deterministic root/orphan flags (R3), and the S106 unit tag (unit id/name + the unit's
/// leaders + the nullable primary-reporting-line etag). (S110 / TASK-11001 removed the vestigial
/// enhedLabel display field.)</param>
/// <param name="PendingCountByManager">manager user_id → number of that manager's effective reports
/// currently holding a pending period — the EXISTING S74 tally, now (S106 / TASK-10604) expanded
/// to the per-authorized-approver cardinality (edge manager + each unit-leader / their vikar).</param>
/// <param name="NameResolution">S106 / TASK-10602 — a DISPLAY-ONLY by-id name lookup over the ids
/// the roster REFERENCES (every row's <c>structuralApproverId</c> ∪ all <c>leaderIds</c>), so the FE
/// can render the "Refererer opad til" upward-reference + the cross-unit-leader chips even when the
/// referenced person is NOT an active in-roster row (an inactive manager/leader, or — defensively —
/// a person outside the loaded subtree). Keyed by user_id. This is a by-id name fetch ONLY — it
/// admits NObody into scope (no widening); the roster's <c>materialized_path</c> bound is unchanged.</param>
public sealed record MedarbejderRosterProjection(
    IReadOnlyList<MedarbejderRosterRow> Employees,
    IReadOnlyDictionary<string, int> PendingCountByManager,
    IReadOnlyDictionary<string, ResolvedPersonRef> NameResolution);

/// <summary>
/// One enriched medarbejder-roster row (S75-7500 R1; S106 unit tag). The tree keys on
/// <paramref name="StructuralApproverId"/> (the raw active PRIMARY <c>reporting_lines.manager_id</c>
/// — NOT a resolver result). <paramref name="OutgoingVikar"/> is the person's OWN active
/// <c>manager_vikar</c> row (present iff they are an away-manager covered by a vikar).
///
/// <para>S110 / TASK-11001: the vestigial <c>enhedLabel</c> display field (= the primary-org name,
/// the <c>employee_profiles.enhed_label</c> column dropped in S103) is REMOVED — unit-based display
/// is served by the merged-admin unit tag (<paramref name="UnitName"/> + the forest), not a per-row
/// org label.</para>
///
/// <para>S106 / TASK-10602 (Enhedsspor Phase 3a, ADR-038 D2/D4/D6): <paramref name="UnitId"/> /
/// <paramref name="UnitName"/> are the person's single structural unit (NULL when homed directly at
/// the Organisation); <paramref name="LeaderIds"/> are that unit's designated leaders (the
/// <c>unit_leaders</c> rows, AGGREGATED so the multi-peer-leader join never fans the roster out —
/// EMPTY when the person has no unit / the unit has no leaders), so the FE can group people under
/// their unit's leaders and flag a cross-unit exception (the person's reporting manager ∉ their
/// unit's leaders). <paramref name="PrimaryReportingLineVersion"/> is the active PRIMARY
/// <c>reporting_lines.version</c> (the SAME row that surfaces <paramref name="StructuralApproverId"/>)
/// — the FE etag: NULLABLE (a root/orphan has no active PRIMARY edge → null → the FE's "Ret" CREATES
/// vs SUPERSEDES, the S99 distinction). It changes only with the reporting-line row.</para>
/// </summary>
public sealed record MedarbejderRosterRow(
    string EmployeeId,
    string DisplayName,
    string? Position,
    string? StructuralApproverId,
    string PeriodStatus,
    OutgoingVikar? OutgoingVikar,
    bool IsRoot,
    bool IsOrphan,
    Guid? UnitId,
    string? UnitName,
    IReadOnlyList<string> LeaderIds,
    long? PrimaryReportingLineVersion);

/// <summary>
/// S106 / TASK-10602 — one DISPLAY-ONLY resolved person reference for the roster's
/// <see cref="MedarbejderRosterProjection.NameResolution"/> map. Carries the <paramref name="DisplayName"/>
/// + <paramref name="Position"/> (live <c>employee_profiles.position</c>) + <paramref name="UnitName"/>
/// of an id the roster references (a manager / unit-leader) so the FE can label the upward-reference +
/// cross-unit-leader chips without a blank. A pure by-id projection — NO scope is admitted by it.
/// </summary>
public sealed record ResolvedPersonRef(
    string UserId,
    string DisplayName,
    string? Position,
    string? UnitName);

/// <summary>
/// S75-7500 (R1) — the per-away-manager outgoing-vikar marker: the active
/// <c>manager_vikar</c> row where the roster person is the <c>absent_approver_id</c>. Drives the FE
/// vikar badge on the away-manager's row + the Vikar filter tile. <paramref name="UntilDate"/> is
/// the INCLUSIVE "til og med" date; <paramref name="Reason"/> is the CHECK-constrained reason
/// (FERIE/SYGDOM/ORLOV/TJENESTEREJSE/ANDET).
/// </summary>
public sealed record OutgoingVikar(
    string VikarUserId,
    string VikarDisplayName,
    DateOnly UntilDate,
    string Reason);

/// <summary>
/// One person-search hit (S74-7404 R11b,
/// <see cref="ApprovalPeriodRepository.SearchPeopleAsync"/>). The picker displays
/// <paramref name="PrimaryOrgName"/> (S110 / TASK-11001 removed the vestigial <c>enhedLabel</c>
/// field — the <c>employee_profiles.enhed_label</c> column was dropped in S103 and the field was a
/// redundant duplicate of the org name).
/// </summary>
public sealed record PersonSearchHit(
    string UserId,
    string DisplayName,
    string PrimaryOrgName);

/// <summary>
/// S106 / TASK-10603 — one matched person from the merged-admin overlay SEARCH
/// (<see cref="ApprovalPeriodRepository.SearchPeopleForOverlayAsync"/>). Carries the live
/// <paramref name="Position"/> (nullable) + the person's <paramref name="UnitId"/> (nullable =
/// Organisation-homed) + <paramref name="PrimaryOrgId"/> so the endpoint builds the overlay's
/// home-unit name + the Organisation/unit PATH in memory from the cheap unit/org maps (units ≪ people).
/// </summary>
public sealed record OverlayPersonRow(
    string UserId,
    string DisplayName,
    string? Position,
    Guid? UnitId,
    string PrimaryOrgId);

/// <summary>
/// S87-8701 — one roster entry for the leader Teamoversigt aggregate
/// (<see cref="ApprovalPeriodRepository.GetTeamOverviewRosterAsync"/>). One per employee in the
/// leader's designated-act-authority set for the requested month. <paramref name="PeriodId"/> is
/// <c>null</c> when the employee has NO <c>approval_periods</c> row that month (the zero-period
/// DRAFT row — no actions). <paramref name="DisplayName"/> + <paramref name="UsersAgreementCode"/>
/// come from <c>users</c> so a no-period row can still render a name + agreement (the endpoint
/// prefers the PERIOD's agreement_code when a period exists, else this users fallback).
/// </summary>
public sealed record TeamOverviewRosterRow(
    string EmployeeId,
    string DisplayName,
    string UsersAgreementCode,
    Guid? PeriodId);
