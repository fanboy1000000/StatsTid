using Npgsql;
using NpgsqlTypes;
using StatsTid.SharedKernel.Calendar;

namespace StatsTid.Infrastructure;

// ═══════════════════════════════════════════════════════════════════════════════════════════
// S140 / TASK-14004 (refinement B2) — the APPROVAL / LIFECYCLE / ORGANISATION reads behind the
// HR follow-up surface. READ-ONLY: this file contains no INSERT, UPDATE or DELETE, and it is
// the only place the six processes below are computed.
//
// PLAIN-LANGUAGE WHAT. Employees register a month and send it; their leader approves it; only
// then may it go to payroll. Each month is stamped with two deadlines when its period row is
// created (month-end + 2 for the employee, month-end + 5 for the leader — the provisional
// institutional defaults in InstitutionalDeadlines). Until S140 NOTHING read those stored
// deadlines: the organisation page's "efter frist" ("past deadline") tile counted every manager
// with anything PENDING and captioned it "past deadline", a departing employee's unapproved
// final month sat unnoticed, and an approved month reached payroll only when somebody
// remembered to call the export endpoint. These reads make each of those visible and honest:
//
//   HRP-012  past deadline        — employee late (never sent / reopened / rejected past +2)
//                                   counted SEPARATELY from approver late (sent, past +5)
//   HRP-011  leaver's final month — the month containing an employment end date, not approved
//   HRP-022  approved not exported— an approved month with no payroll export record
//   HRP-013  orphan employees     — nobody structurally approves them (cross-organisation roll-up)
//   HRP-014  expired delegations  — a stand-in (vikar) the expiry sweep closed in the last 30 days
//   HRP-015  cannot register      — employed today but no agreement-code row covers today
//
// THREE RULES THIS FILE OBEYS EVERYWHERE, because breaking any of them is how a diagnostic list
// starts lying:
//
//  1. ORG SCOPE IS THE SUBJECT'S **CURRENT** HOME (`users.primary_org_id`) — never
//     `approval_periods.org_id` and never `manager_vikar.organisation_id`. Those two are stamped
//     at send / delegation time and DRIFT when a person transfers: scoping on them would show the
//     OLD organisation's HR a transferred employee's months and HIDE them from the new one. The
//     accessible-org set arrives from `OrgScopeValidator.GetAccessibleOrgsAsync(actor, LocalHR)`:
//     `null` = a GlobalAdmin's unrestricted read, an EMPTY set = the endpoint 403s before calling
//     in here (never an empty 200, which would hide a scope problem as "nothing to do").
//
//  2. ONE DATE PER REQUEST (PAT-028). `today` is the Copenhagen business day, computed ONCE in
//     the endpoint and threaded in as a parameter — which is why this repository holds no
//     TimeProvider at all. No statement in this file uses CURRENT_DATE / NOW() for a business
//     date; every date crosses the wire as a bound parameter, so a fixed test clock actually moves
//     these reads.
//
//  3. EVERY ITEM CARRIES ITS AGE ANCHOR AND WHERE THAT ANCHOR CAME FROM (`stored` when the period
//     row's deadline column holds it, `computed` when the row predates those columns and the
//     ratified default was derived). A NULL stored deadline is NEVER silently treated as "on
//     time" — that silent pass is precisely the overclaim this sprint exists to fix.
// ═══════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// The four classifications the shared (employee × month) enumeration can be asked for. One SQL
/// statement serves all four (parameter-driven predicates, the <c>HrBackdateWorklistRepository</c>
/// idiom), which is what makes the HRP-011 / HRP-022 partition EXACT by construction rather than by
/// two predicates that happen to agree: 011 is "not APPROVED", 022 is "APPROVED but not exported".
/// </summary>
public static class HrApprovalMonthKinds
{
    /// <summary>HRP-012, the employee's court: a covered month past <c>employee_deadline</c> with no row, or DRAFT (a reopen), or REJECTED.</summary>
    public const string EmployeeLate = "EMPLOYEE_LATE";

    /// <summary>HRP-012, the leader's court: SUBMITTED or EMPLOYEE_APPROVED past <c>manager_deadline</c> (both are leader-approvable).</summary>
    public const string ApproverLate = "APPROVER_LATE";

    /// <summary>HRP-011: the month containing an employment END date that has passed, when that month is missing or not APPROVED.</summary>
    public const string LeaverFinalMonth = "LEAVER_FINAL";

    /// <summary>HRP-022: an APPROVED month with no <c>payroll_export_records</c> row (a READ-ONLY cross-context lookup, ADR-034 D4).</summary>
    public const string ApprovedNotExported = "NOT_EXPORTED";
}

/// <summary>Where an item's age anchor came from.</summary>
public static class HrDeadlineSources
{
    /// <summary>The period row's own <c>employee_deadline</c> / <c>manager_deadline</c> column.</summary>
    public const string Stored = "stored";

    /// <summary>
    /// Derived from <see cref="InstitutionalDeadlines"/> because no row exists yet, or because the
    /// row predates the deadline columns and holds NULL. Flagged, never passed off as on time.
    /// </summary>
    public const string Computed = "computed";
}

/// <summary>
/// One (employee × month) item of the shared enumeration — the row shape HRP-011, HRP-012 and
/// HRP-022 all return.
///
/// <para>Deliberately carries NO employment date (ADR-040 D7). A leaver row names the YEAR and
/// MONTH its final month falls in, which is the process's subject; the end date itself never
/// crosses this boundary.</para>
/// </summary>
/// <param name="PeriodStatus">The persisted status, or <c>NONE</c> when no period row exists at all
/// — "never sent" has no row (the send flow is the sole production writer), so it cannot be found
/// by querying rows and there is no <c>OPEN</c> status to look for.</param>
/// <param name="AgeAnchor">The date this item's age is measured FROM: the employee deadline for
/// <see cref="HrApprovalMonthKinds.EmployeeLate"/>, otherwise the manager deadline (which is also
/// the ratified payroll export cutoff).</param>
/// <param name="DaysPastAnchor">
/// <c>today − AgeAnchor</c> in days. POSITIVE means overdue. HRP-012 lists only overdue months, so
/// it is always positive there; HRP-011 and HRP-022 list every qualifying month ("open = all"), so
/// a not-yet-due item legitimately carries zero or a negative value.
/// </param>
/// <param name="DeadlineSource">One of <see cref="HrDeadlineSources"/>.</param>
public sealed record HrApprovalMonthItem(
    string EmployeeId,
    string DisplayName,
    string OrgId,
    int Year,
    int Month,
    string PeriodStatus,
    Guid? PeriodId,
    DateOnly AgeAnchor,
    int DaysPastAnchor,
    string DeadlineSource);

/// <summary>
/// One orphan employee (HRP-013) — nobody structurally approves them, so nobody CAN approve their
/// month. Carries no age anchor: there is no date on which an employee "became" an orphan, and the
/// process has no ratified deadline (register row HRP-013 is NOT READY), so nothing here may be
/// aged or coloured.
/// </summary>
public sealed record HrOrphanEmployeeItem(
    string EmployeeId,
    string DisplayName,
    string OrgId,
    string? UnitName);

/// <summary>
/// One stand-in (vikar) delegation the expiry sweep closed (HRP-014a).
/// </summary>
/// <param name="ExpiredOn">The <c>effective_to</c> the sweep wrote — the first day no longer covered.</param>
/// <param name="ExpiredAt">When the closing event was recorded (the age anchor).</param>
/// <param name="ApproverHasActiveCover">
/// TRUE when the absent approver has an ACTIVE <c>manager_vikar</c> row again — i.e. the expiry was
/// followed by a fresh delegation and this approver is NOT uncovered today. Reported per item so
/// the raw expiry count (a historical fact) can never be mistaken for the "still uncovered" count.
/// </param>
public sealed record HrExpiredDelegationItem(
    Guid VikarId,
    string AbsentApproverId,
    string AbsentApproverName,
    string VikarUserId,
    string? VikarUserName,
    string OrgId,
    string? UnitName,
    string? Reason,
    DateOnly? UntilDate,
    DateOnly? ExpiredOn,
    DateOnly ExpiredAt,
    int DaysSinceExpiry,
    bool ApproverHasActiveCover);

/// <summary>
/// One employee who cannot register (HRP-015): employed today, with an <c>employee_profiles</c> row
/// covering today, but NO <c>user_agreement_codes</c> row covering today — so a pro-rated absence
/// registration answers 422 <c>employment_profile_missing</c> and HR is never told.
/// </summary>
/// <param name="GapSince">
/// The first day of the current uncovered stretch — the day the employee's last agreement-code row
/// stopped covering (intervals are end-EXCLUSIVE, ADR-018 D9), or the covering profile row's own
/// start when the employee never had an agreement-code row at all. NULL when the only available
/// anchor is the <c>0001-01-01</c> history-backfill sentinel: "the gap cannot be dated" is reported
/// honestly rather than as an absurd age.
/// </param>
public sealed record HrCannotRegisterItem(
    string EmployeeId,
    string DisplayName,
    string OrgId,
    string? UnitName,
    DateOnly? GapSince,
    int? DaysSinceGapStart);

/// <summary>
/// The HR follow-up reads for the approval / employment-lifecycle / organisation processes
/// (HRP-011, 012, 013, 014, 015, 022). Read-only over its own connections; stateless ⇒ singleton.
/// See the file header for the three invariants every method here obeys.
/// </summary>
public sealed class HrFollowUpApprovalReadRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    /// <summary>
    /// The rolling lookback floor, in months, for the three history-scanning reads — owner ruling
    /// OQ-3 (a). The enumeration starts at the first day of the month this many months before the
    /// current month; anything older is not listed.
    ///
    /// <para>WHY a floor at all: a never-sent month has NO row, so "past deadline" is found by
    /// enumerating the months an employment window covers. Without a bound, day one of the feature
    /// would list every pre-adoption month of every long-tenured employee as "never sent" — months
    /// that were handled before the system existed. Anything genuinely older that still matters
    /// reaches payroll through the ADR-013 correction path, not through this list. The alternative
    /// not taken was a per-institution adoption date (the most correct long-term shape), which
    /// needs the same configuration surface the deadlines are waiting for.</para>
    /// </summary>
    public const int LookbackMonths = 12;

    /// <summary>The window, in days, for the HRP-014 expired-delegation read.</summary>
    public const int ExpiredDelegationWindowDays = 30;

    public HrFollowUpApprovalReadRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ── SQL (literal constants only — CA2100 discipline; every date and offset is a parameter) ──

    /// <summary>
    /// THE shared (employee × month) enumeration — one statement, four classifications.
    ///
    /// <para><b>Why an enumeration and not a query over rows.</b> An <c>approval_periods</c> row is
    /// BORN only when a month is sent (the send flow is the sole production writer, and it stamps
    /// both deadlines in the same transaction). The persisted statuses are DRAFT /
    /// EMPLOYEE_APPROVED / SUBMITTED / APPROVED / REJECTED — there is no <c>OPEN</c>. So "never
    /// sent" is the ABSENCE of a row and cannot be found by querying rows. This statement therefore
    /// generates the months each employee's employment window covers, from the rolling floor to the
    /// current month, and LEFT JOINs whatever row exists.</para>
    ///
    /// <para><b>No <c>is_active</c> filter</b>, deliberately: <c>is_active</c> governs login only
    /// (ADR-040 D3), and a leaver's late final month is exactly what HR must see. What bounds the
    /// enumeration is the EMPLOYMENT WINDOW (NULL on either side = unbounded, ADR-040 D2) plus the
    /// floor.</para>
    ///
    /// <para><b>The month join is exact</b> (<c>period_type = 'MONTHLY'</c> and both boundaries),
    /// so a WEEKLY period inside the month can never be mistaken for the month's row.</para>
    /// </summary>
    private const string SelectEmployeeMonthsSql =
        """
        WITH months AS (
            SELECT gs::date                                            AS month_start,
                   (gs + INTERVAL '1 month' - INTERVAL '1 day')::date  AS month_end
            FROM generate_series(
                     @floorMonthStart::timestamp,
                     @currentMonthStart::timestamp,
                     INTERVAL '1 month') AS gs
        ),
        scoped AS (
            SELECT u.user_id, u.display_name, u.primary_org_id,
                   u.employment_start_date, u.employment_end_date
            FROM users u
            WHERE (@allOrgs OR u.primary_org_id = ANY(@orgIds))
        ),
        covered AS (
            SELECT s.user_id, s.display_name, s.primary_org_id, s.employment_end_date,
                   m.month_start, m.month_end
            FROM scoped s
            CROSS JOIN months m
            WHERE (s.employment_start_date IS NULL OR s.employment_start_date <= m.month_end)
              AND (s.employment_end_date   IS NULL OR s.employment_end_date   >= m.month_start)
        ),
        resolved AS (
            SELECT c.user_id                                AS employee_id,
                   c.display_name                           AS display_name,
                   c.primary_org_id                         AS org_id,
                   EXTRACT(YEAR  FROM c.month_start)::int   AS year,
                   EXTRACT(MONTH FROM c.month_start)::int   AS month,
                   c.month_start                            AS month_start,
                   c.month_end                              AS month_end,
                   c.employment_end_date                    AS employment_end_date,
                   ap.period_id                             AS period_id,
                   COALESCE(ap.status, 'NONE'::text)        AS period_status,
                   COALESCE(ap.employee_deadline, c.month_end + @submitDays)  AS eff_employee_deadline,
                   COALESCE(ap.manager_deadline,  c.month_end + @approveDays) AS eff_manager_deadline,
                   -- ::text explicitly: a bare string literal is UNKNOWN-typed, and while
                   -- PostgreSQL coerces an unknown CTE output column to text, saying so removes any
                   -- dependence on that inference for a value the reader pulls as a string.
                   CASE WHEN ap.employee_deadline IS NULL
                        THEN 'computed'::text ELSE 'stored'::text END AS employee_deadline_source,
                   CASE WHEN ap.manager_deadline  IS NULL
                        THEN 'computed'::text ELSE 'stored'::text END AS manager_deadline_source
            FROM covered c
            LEFT JOIN approval_periods ap
                   ON ap.employee_id  = c.user_id
                  AND ap.period_type  = 'MONTHLY'
                  AND ap.period_start = c.month_start
                  AND ap.period_end   = c.month_end
        )
        SELECT r.employee_id, r.display_name, r.org_id, r.year, r.month,
               r.period_id, r.period_status,
               CASE WHEN @kind = 'EMPLOYEE_LATE'
                    THEN r.eff_employee_deadline ELSE r.eff_manager_deadline END        AS age_anchor,
               CASE WHEN @kind = 'EMPLOYEE_LATE'
                    THEN r.employee_deadline_source ELSE r.manager_deadline_source END  AS deadline_source
        FROM resolved r
        WHERE
              -- HRP-012, employee late. DRAFT exists only after a reopen and REJECTED means the ball
              -- is back with the employee, so both are the employee's court alongside "no row at
              -- all". A REJECTED month counts only ONCE month-end + 2 has passed (a leader may
              -- reject before that deadline) and is aged FROM that deadline, not from the rejection.
              (@kind = 'EMPLOYEE_LATE'
                   AND r.eff_employee_deadline < @today
                   AND r.period_status IN ('NONE', 'DRAFT', 'REJECTED'))
              -- HRP-012, approver late. SUBMITTED and EMPLOYEE_APPROVED are both leader-approvable.
           OR (@kind = 'APPROVER_LATE'
                   AND r.period_status IN ('SUBMITTED', 'EMPLOYEE_APPROVED')
                   AND r.eff_manager_deadline < @today)
              -- HRP-011, the leaver's FINAL month only (the owner's ruling). Earlier never-sent
              -- months of the same leaver belong to HRP-012, not here. "Leaver" is the employment
              -- window (ADR-040), never is_active.
           OR (@kind = 'LEAVER_FINAL'
                   AND r.employment_end_date IS NOT NULL
                   AND r.employment_end_date <= @today
                   AND r.employment_end_date >= r.month_start
                   AND r.employment_end_date <= r.month_end
                   AND r.period_status <> 'APPROVED')
              -- HRP-022, approved but never exported. A READ-ONLY cross-context lookup of the
              -- Payroll-owned lock table (ADR-034 D4 permits it; the Payroll service is its SOLE
              -- writer and this file writes nothing anywhere). The partition with HRP-011 above is
              -- exact BY CONSTRUCTION: same rows, complementary status predicates.
           OR (@kind = 'NOT_EXPORTED'
                   AND r.period_status = 'APPROVED'
                   AND NOT EXISTS (
                           SELECT 1
                           FROM payroll_export_records per
                           WHERE per.employee_id = r.employee_id
                             AND per.year  = r.year
                             AND per.month = r.month))
        ORDER BY age_anchor, r.employee_id, r.year, r.month
        """;

    /// <summary>
    /// HRP-013 — the cross-organisation ORPHAN roll-up. Mirrors the per-organisation roster card's
    /// rule exactly (an employee with no active PRIMARY reporting-line edge who is nobody's
    /// approver), which today is computed per organisation with no roll-up; this is that roll-up
    /// over the actor's accessible-org set.
    ///
    /// <para>"Is nobody's approver" is evaluated over the SAME in-scope population, which is sound
    /// because approval authority is same-Organisation bounded (the flat-authority model): if a
    /// person approves anyone, that person's reports live in their own Organisation, which is in
    /// scope whenever they are. <c>is_active = TRUE</c> matches the roster read's population so the
    /// roll-up and the per-organisation card can never disagree.</para>
    /// </summary>
    private const string SelectOrphanEmployeesSql =
        """
        WITH scoped AS (
            SELECT u.user_id, u.display_name, u.primary_org_id, u.unit_id
            FROM users u
            WHERE u.is_active = TRUE
              AND (@allOrgs OR u.primary_org_id = ANY(@orgIds))
        ),
        edges AS (
            SELECT rl.employee_id, rl.manager_id
            FROM reporting_lines rl
            WHERE rl.relationship = 'PRIMARY'
              AND rl.effective_to IS NULL
        )
        SELECT s.user_id     AS employee_id,
               s.display_name,
               s.primary_org_id AS org_id,
               un.name       AS unit_name
        FROM scoped s
        LEFT JOIN units un ON un.unit_id = s.unit_id
        WHERE NOT EXISTS (SELECT 1 FROM edges e WHERE e.employee_id = s.user_id)
          AND NOT EXISTS (
                  SELECT 1
                  FROM edges e2
                  JOIN scoped s2 ON s2.user_id = e2.employee_id
                  WHERE e2.manager_id = s.user_id)
        ORDER BY s.display_name, s.user_id
        """;

    /// <summary>
    /// HRP-014a — stand-in (vikar) delegations the EXPIRY SWEEP closed inside the window.
    ///
    /// <para><b>Why the EVENT and not the table.</b> <c>manager_vikar</c> has no end-reason column,
    /// so a table-only predicate ("closed, and <c>until_date</c> has passed") cannot tell an expiry
    /// from a late MANUAL close. The discriminator lives only on the event: the expiry sweep is the
    /// sole writer of <c>endReason = 'EXPIRED'</c>, while manual closes write <c>REVOKED</c> or
    /// <c>APPROVER_REMOVED</c>. Event payloads are camelCase (the shared event serializer), hence
    /// <c>data->>'endReason'</c>.</para>
    ///
    /// <para><b>Eventual consistency, stated not hidden.</b> The canonical <c>events</c> table is
    /// filled by the outbox publisher one cycle after the domain transaction commits, so a
    /// just-expired delegation appears here within about a minute — the same lag as any event-backed
    /// read. The endpoint's contract says so.</para>
    ///
    /// <para>Org scope is the ABSENT APPROVER's CURRENT <c>users.primary_org_id</c> — never
    /// <c>manager_vikar.organisation_id</c>, which is stamped at delegation time and drifts on a
    /// transfer.</para>
    /// </summary>
    private const string SelectExpiredDelegationsSql =
        """
        SELECT d.vikar_id, d.absent_approver_id, d.absent_approver_name,
               d.vikar_user_id, d.vikar_user_name, d.org_id, d.unit_name,
               d.reason, d.until_date, d.expired_on, d.expired_at,
               d.approver_has_active_cover
        FROM (
            SELECT DISTINCT ON (ev.vikar_id)
                   ev.vikar_id                                  AS vikar_id,
                   ev.absent_approver_id                        AS absent_approver_id,
                   au.display_name                              AS absent_approver_name,
                   ev.vikar_user_id                             AS vikar_user_id,
                   vu.display_name                              AS vikar_user_name,
                   au.primary_org_id                            AS org_id,
                   un.name                                      AS unit_name,
                   mv.reason                                    AS reason,
                   mv.until_date                                AS until_date,
                   mv.effective_to                              AS expired_on,
                   ev.occurred_at                               AS expired_at,
                   (cover.vikar_id IS NOT NULL)                 AS approver_has_active_cover
            FROM (
                SELECT (e.data->>'vikarId')::uuid    AS vikar_id,
                       e.data->>'absentApproverId'   AS absent_approver_id,
                       e.data->>'vikarUserId'        AS vikar_user_id,
                       e.occurred_at                 AS occurred_at
                FROM events e
                WHERE e.event_type = 'ManagerVikarEnded'
                  AND e.data->>'endReason' = 'EXPIRED'
                  AND e.occurred_at >= @since
            ) ev
            JOIN users au ON au.user_id = ev.absent_approver_id
            LEFT JOIN users vu ON vu.user_id = ev.vikar_user_id
            LEFT JOIN units un ON un.unit_id = au.unit_id
            LEFT JOIN manager_vikar mv ON mv.vikar_id = ev.vikar_id
            LEFT JOIN manager_vikar cover
                   ON cover.absent_approver_id = ev.absent_approver_id
                  AND cover.effective_to IS NULL
            WHERE (@allOrgs OR au.primary_org_id = ANY(@orgIds))
            ORDER BY ev.vikar_id, ev.occurred_at
        ) d
        ORDER BY d.expired_at, d.vikar_id
        """;

    /// <summary>
    /// HRP-015 — employees who CANNOT REGISTER: employed today, with an <c>employee_profiles</c> row
    /// covering today, but no <c>user_agreement_codes</c> row covering today. Effective-dated
    /// intervals are end-EXCLUSIVE (ADR-018 D9): <c>effective_from &lt;= today AND (effective_to IS
    /// NULL OR effective_to &gt; today)</c>. So a gap that CLOSED yesterday (a successor row now
    /// covers today) does not appear, and a gap that OPENED today does.
    ///
    /// <para>ONE ROW PER EMPLOYEE (<c>DISTINCT ON</c>) — duplicate gaps are suppressed, per the
    /// register row. LEAVERS are excluded (their inability to register is not a data defect), and
    /// so is anyone whose employment has not started.</para>
    /// </summary>
    private const string SelectCannotRegisterSql =
        """
        SELECT d.employee_id, d.display_name, d.org_id, d.unit_name, d.gap_since
        FROM (
            SELECT DISTINCT ON (u.user_id)
                   u.user_id                                            AS employee_id,
                   u.display_name                                       AS display_name,
                   u.primary_org_id                                     AS org_id,
                   un.name                                              AS unit_name,
                   COALESCE(gap.last_close, ep.effective_from)          AS gap_since
            FROM users u
            JOIN employee_profiles ep
                 ON ep.employee_id = u.user_id
                AND ep.effective_from <= @today
                AND (ep.effective_to IS NULL OR ep.effective_to > @today)
            LEFT JOIN units un ON un.unit_id = u.unit_id
            LEFT JOIN LATERAL (
                SELECT MAX(uac.effective_to) AS last_close
                FROM user_agreement_codes uac
                WHERE uac.user_id = u.user_id
                  AND uac.effective_to IS NOT NULL
                  AND uac.effective_to <= @today
            ) gap ON TRUE
            WHERE (@allOrgs OR u.primary_org_id = ANY(@orgIds))
              AND (u.employment_start_date IS NULL OR u.employment_start_date <= @today)
              AND (u.employment_end_date   IS NULL OR u.employment_end_date   >= @today)
              AND NOT EXISTS (
                      SELECT 1
                      FROM user_agreement_codes live
                      WHERE live.user_id = u.user_id
                        AND live.effective_from <= @today
                        AND (live.effective_to IS NULL OR live.effective_to > @today))
            ORDER BY u.user_id, ep.effective_from
        ) d
        -- Oldest DATABLE gap first. The '0001-01-01' history-backfill sentinel is not a real date,
        -- so it must not sort to the top as "the oldest problem" — NULLIF pushes those rows last.
        ORDER BY NULLIF(d.gap_since, '0001-01-01'::date) NULLS LAST, d.employee_id
        """;

    // ── Reads (self-managed connections; no transaction — nothing here writes) ───────────────

    /// <summary>
    /// The shared (employee × month) enumeration for one classification
    /// (<see cref="HrApprovalMonthKinds"/>), oldest first.
    /// </summary>
    /// <param name="accessibleOrgIds">
    /// The actor's HR-floored accessible-org set. <c>null</c> = GlobalAdmin, unrestricted. An EMPTY
    /// set returns nothing — but the endpoint 403s before reaching here, so this is defence in
    /// depth, not the contract.
    /// </param>
    /// <param name="today">The Copenhagen business day, computed once per request (PAT-028).</param>
    public async Task<IReadOnlyList<HrApprovalMonthItem>> GetEmployeeMonthsAsync(
        string kind,
        IReadOnlyCollection<string>? accessibleOrgIds,
        DateOnly today,
        CancellationToken ct = default)
    {
        if (accessibleOrgIds is { Count: 0 })
            return Array.Empty<HrApprovalMonthItem>();

        // The rolling floor (owner ruling OQ-3 (a)) and the upper bound, derived from the ONE date
        // the caller threaded in — never re-read from a clock here.
        var currentMonthStart = new DateOnly(today.Year, today.Month, 1);
        var floorMonthStart = currentMonthStart.AddMonths(-LookbackMonths);

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectEmployeeMonthsSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("kind", NpgsqlDbType.Text) { Value = kind });
        cmd.Parameters.Add(new NpgsqlParameter("today", NpgsqlDbType.Date) { Value = today });
        cmd.Parameters.Add(new NpgsqlParameter("floorMonthStart", NpgsqlDbType.Date) { Value = floorMonthStart });
        cmd.Parameters.Add(new NpgsqlParameter("currentMonthStart", NpgsqlDbType.Date) { Value = currentMonthStart });
        // The two ratified offsets cross as PARAMETERS, so the statement text carries no literal
        // deadline arithmetic and the computed fallback provably uses the same rule the write paths
        // stamp with.
        cmd.Parameters.Add(new NpgsqlParameter("submitDays", NpgsqlDbType.Integer) { Value = InstitutionalDeadlines.SubmitDays });
        cmd.Parameters.Add(new NpgsqlParameter("approveDays", NpgsqlDbType.Integer) { Value = InstitutionalDeadlines.ApproveDays });
        AddOrgScopeParameters(cmd, accessibleOrgIds);

        var items = new List<HrApprovalMonthItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var employeeOrd = reader.GetOrdinal("employee_id");
        var nameOrd = reader.GetOrdinal("display_name");
        var orgOrd = reader.GetOrdinal("org_id");
        var yearOrd = reader.GetOrdinal("year");
        var monthOrd = reader.GetOrdinal("month");
        var periodOrd = reader.GetOrdinal("period_id");
        var statusOrd = reader.GetOrdinal("period_status");
        var anchorOrd = reader.GetOrdinal("age_anchor");
        var sourceOrd = reader.GetOrdinal("deadline_source");
        while (await reader.ReadAsync(ct))
        {
            var anchor = reader.GetFieldValue<DateOnly>(anchorOrd);
            items.Add(new HrApprovalMonthItem(
                EmployeeId: reader.GetString(employeeOrd),
                DisplayName: reader.GetString(nameOrd),
                OrgId: reader.GetString(orgOrd),
                Year: reader.GetInt32(yearOrd),
                Month: reader.GetInt32(monthOrd),
                PeriodStatus: reader.GetString(statusOrd),
                PeriodId: reader.IsDBNull(periodOrd) ? null : reader.GetGuid(periodOrd),
                AgeAnchor: anchor,
                DaysPastAnchor: today.DayNumber - anchor.DayNumber,
                DeadlineSource: reader.GetString(sourceOrd)));
        }
        return items;
    }

    /// <summary>HRP-013 — the cross-organisation orphan roll-up (see <see cref="SelectOrphanEmployeesSql"/>).</summary>
    public async Task<IReadOnlyList<HrOrphanEmployeeItem>> GetOrphanEmployeesAsync(
        IReadOnlyCollection<string>? accessibleOrgIds, CancellationToken ct = default)
    {
        if (accessibleOrgIds is { Count: 0 })
            return Array.Empty<HrOrphanEmployeeItem>();

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectOrphanEmployeesSql, conn);
        AddOrgScopeParameters(cmd, accessibleOrgIds);

        var items = new List<HrOrphanEmployeeItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var employeeOrd = reader.GetOrdinal("employee_id");
        var nameOrd = reader.GetOrdinal("display_name");
        var orgOrd = reader.GetOrdinal("org_id");
        var unitOrd = reader.GetOrdinal("unit_name");
        while (await reader.ReadAsync(ct))
        {
            items.Add(new HrOrphanEmployeeItem(
                EmployeeId: reader.GetString(employeeOrd),
                DisplayName: reader.GetString(nameOrd),
                OrgId: reader.GetString(orgOrd),
                UnitName: reader.IsDBNull(unitOrd) ? null : reader.GetString(unitOrd)));
        }
        return items;
    }

    /// <summary>
    /// HRP-014a — delegations the expiry sweep closed within
    /// <see cref="ExpiredDelegationWindowDays"/> days of <paramref name="today"/>, oldest first.
    /// The lower bound is UTC midnight of <c>today − 30</c>, so an event recorded at any time of
    /// that day is included and one from the day before is not.
    /// </summary>
    public async Task<IReadOnlyList<HrExpiredDelegationItem>> GetExpiredDelegationsAsync(
        IReadOnlyCollection<string>? accessibleOrgIds, DateOnly today, CancellationToken ct = default)
    {
        if (accessibleOrgIds is { Count: 0 })
            return Array.Empty<HrExpiredDelegationItem>();

        var since = new DateTimeOffset(
            today.AddDays(-ExpiredDelegationWindowDays).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectExpiredDelegationsSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("since", NpgsqlDbType.TimestampTz) { Value = since });
        AddOrgScopeParameters(cmd, accessibleOrgIds);

        var items = new List<HrExpiredDelegationItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var vikarOrd = reader.GetOrdinal("vikar_id");
        var approverOrd = reader.GetOrdinal("absent_approver_id");
        var approverNameOrd = reader.GetOrdinal("absent_approver_name");
        var standInOrd = reader.GetOrdinal("vikar_user_id");
        var standInNameOrd = reader.GetOrdinal("vikar_user_name");
        var orgOrd = reader.GetOrdinal("org_id");
        var unitOrd = reader.GetOrdinal("unit_name");
        var reasonOrd = reader.GetOrdinal("reason");
        var untilOrd = reader.GetOrdinal("until_date");
        var expiredOnOrd = reader.GetOrdinal("expired_on");
        var expiredAtOrd = reader.GetOrdinal("expired_at");
        var coverOrd = reader.GetOrdinal("approver_has_active_cover");
        while (await reader.ReadAsync(ct))
        {
            // The anchor is the Copenhagen calendar day the closing event was recorded on — the
            // same zone every other business date in this file uses. `occurred_at` is TIMESTAMPTZ,
            // which Npgsql surfaces as a UTC DateTime; SpecifyKind makes that explicit so
            // ConvertTimeFromUtc cannot be handed an Unspecified instant and guess.
            var occurredUtc = DateTime.SpecifyKind(
                reader.GetFieldValue<DateTime>(expiredAtOrd), DateTimeKind.Utc);
            var expiredAt = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(occurredUtc, CopenhagenBusinessDate.Zone));
            items.Add(new HrExpiredDelegationItem(
                VikarId: reader.GetGuid(vikarOrd),
                AbsentApproverId: reader.GetString(approverOrd),
                AbsentApproverName: reader.GetString(approverNameOrd),
                VikarUserId: reader.GetString(standInOrd),
                VikarUserName: reader.IsDBNull(standInNameOrd) ? null : reader.GetString(standInNameOrd),
                OrgId: reader.GetString(orgOrd),
                UnitName: reader.IsDBNull(unitOrd) ? null : reader.GetString(unitOrd),
                Reason: reader.IsDBNull(reasonOrd) ? null : reader.GetString(reasonOrd),
                UntilDate: reader.IsDBNull(untilOrd) ? null : reader.GetFieldValue<DateOnly>(untilOrd),
                ExpiredOn: reader.IsDBNull(expiredOnOrd) ? null : reader.GetFieldValue<DateOnly>(expiredOnOrd),
                ExpiredAt: expiredAt,
                DaysSinceExpiry: today.DayNumber - expiredAt.DayNumber,
                ApproverHasActiveCover: !reader.IsDBNull(coverOrd) && reader.GetBoolean(coverOrd)));
        }
        return items;
    }

    /// <summary>
    /// HRP-015 — employees who cannot register (see <see cref="SelectCannotRegisterSql"/>), oldest
    /// datable gap first. The <c>0001-01-01</c> history-backfill sentinel is reported as an UNKNOWN
    /// gap start rather than as an age of two millennia.
    /// </summary>
    public async Task<IReadOnlyList<HrCannotRegisterItem>> GetCannotRegisterAsync(
        IReadOnlyCollection<string>? accessibleOrgIds, DateOnly today, CancellationToken ct = default)
    {
        if (accessibleOrgIds is { Count: 0 })
            return Array.Empty<HrCannotRegisterItem>();

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(SelectCannotRegisterSql, conn);
        cmd.Parameters.Add(new NpgsqlParameter("today", NpgsqlDbType.Date) { Value = today });
        AddOrgScopeParameters(cmd, accessibleOrgIds);

        var items = new List<HrCannotRegisterItem>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var employeeOrd = reader.GetOrdinal("employee_id");
        var nameOrd = reader.GetOrdinal("display_name");
        var orgOrd = reader.GetOrdinal("org_id");
        var unitOrd = reader.GetOrdinal("unit_name");
        var gapOrd = reader.GetOrdinal("gap_since");
        while (await reader.ReadAsync(ct))
        {
            DateOnly? gapSince = reader.IsDBNull(gapOrd) ? null : reader.GetFieldValue<DateOnly>(gapOrd);
            if (gapSince == HistoryBackfillSentinel)
                gapSince = null;
            items.Add(new HrCannotRegisterItem(
                EmployeeId: reader.GetString(employeeOrd),
                DisplayName: reader.GetString(nameOrd),
                OrgId: reader.GetString(orgOrd),
                UnitName: reader.IsDBNull(unitOrd) ? null : reader.GetString(unitOrd),
                GapSince: gapSince,
                DaysSinceGapStart: gapSince is { } since ? today.DayNumber - since.DayNumber : null));
        }
        return items;
    }

    /// <summary>
    /// The <c>0001-01-01</c> value the effective-dated tables use as their "since the beginning of
    /// time" history-backfill default (the S33 lesson). It is a sentinel, not a real date, so it is
    /// never reported as an age.
    /// </summary>
    private static readonly DateOnly HistoryBackfillSentinel = new(1, 1, 1);

    /// <summary>
    /// The ONE org-scope parameter pair every statement in this file uses: <c>@allOrgs</c> (a
    /// GlobalAdmin's unrestricted read) and <c>@orgIds</c> (the HR-floored accessible-org set,
    /// matched against the SUBJECT's CURRENT <c>users.primary_org_id</c>).
    /// </summary>
    private static void AddOrgScopeParameters(NpgsqlCommand cmd, IReadOnlyCollection<string>? accessibleOrgIds)
    {
        cmd.Parameters.Add(new NpgsqlParameter("allOrgs", NpgsqlDbType.Boolean) { Value = accessibleOrgIds is null });
        cmd.Parameters.Add(new NpgsqlParameter("orgIds", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = accessibleOrgIds?.ToArray() ?? Array.Empty<string>(),
        });
    }
}
