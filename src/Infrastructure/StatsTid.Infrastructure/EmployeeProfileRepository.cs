using System.Globalization;
using Npgsql;
using StatsTid.Infrastructure.Temporal;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S31 / TASK-3102 — Phase 4d-3 Part 1 authoritative store for the employment-profile
/// fields previously sourced from request payloads (TimeEndpoints) or hardcoded constants
/// (ComplianceEndpoints): <c>part_time_fraction</c> and <c>position</c>.
/// (<c>weekly_norm_hours</c> removed in S53 TASK-5306 — norm hours sourced from config chain.)
/// Sibling fields (<c>agreement_code</c>, <c>ok_version</c>, <c>primary_org_id</c>) stay on
/// the <c>users</c> table per S31 refinement Q3 LEAVE and are joined in at read time so
/// <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/> returns a fully-hydrated
/// <see cref="EmploymentProfile"/>. <c>employment_category</c> left that set in S137
/// (ADR-040 D4): it is a DATED column on <c>employee_profiles</c>, written on every row by
/// every production write path. S138 / TASK-13804 tightened it to NOT NULL and RETIRED the
/// S137 <c>COALESCE(ep.employment_category, u.employment_category)</c> read fail-safe — with
/// the category editable per date, <c>users.employment_category</c> is only the CACHE of the
/// row covering TODAY, so falling back to it would mislabel a dated read rather than rescue
/// it. The dated cell is the authority; a missed write now fails its INSERT (23502).
///
/// <para>
/// <b>S31 scope — data-plane only.</b> The repository is consumed by TASK-3107 admin CRUD,
/// TASK-3108 AdminEndpoints POST extension (4-way atomicity), and TASK-3106
/// EmployeeProfileSeeder. ComplianceEndpoints / BalanceEndpoints / TimeEndpoints / RuleEngine
/// remain UNCHANGED — they keep their current sources until S32 cuts them over atomically
/// with planner-snapshot.
/// </para>
///
/// <para>
/// <b>Versioning shape pre-baked, dormant in S31.</b> The schema includes
/// <c>profile_id UUID PRIMARY KEY</c>, <c>effective_from / effective_to</c>,
/// partial-unique-index <c>(employee_id) WHERE effective_to IS NULL</c>, and history-unique-index
/// <c>(employee_id, effective_from)</c> — all S29 WageTypeMapping / S30 EntitlementConfig
/// precedent. S31 reads and writes ONLY live rows (<c>effective_to IS NULL</c>): no
/// supersession routing, no FOR UPDATE locking — the simple
/// <c>UPDATE ... WHERE employee_id = @id AND effective_to IS NULL AND version = @expected</c>
/// path suffices. S32 will add the ADR-020 D2 3-case supersession routing inside
/// <c>SupersedeAndCreateAsync</c> — that work is deliberately not in S31.
/// </para>
///
/// <para>
/// <b>Atomic-outbox contract (ADR-018 D5).</b> <see cref="SupersedeAndCreateAsync"/> and
/// <see cref="CreateAsync"/> are <c>(conn, tx)</c> overloads only — the endpoint or seeder
/// owns the transaction, threading audit + outbox writes into the same atomic unit.
/// <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/> is the convenience
/// self-managed overload for non-tx callers (admin GET handler + seeder bootstrap probe).
/// </para>
///
/// <para>
/// <b>ADR-019 admin-strict If-Match.</b> <see cref="SupersedeAndCreateAsync"/> accepts
/// <c>expectedVersion: long?</c>; when supplied, a mismatch against the live row's
/// <c>version</c> column throws <see cref="OptimisticConcurrencyException"/> for the
/// endpoint to map to 412.
/// </para>
///
/// <para>
/// <b><c>IsPartTime</c> is computed, not stored</b> (refinement cycle 2 absorption).
/// The schema does NOT have an <c>is_part_time</c> column;
/// <see cref="EmploymentProfile.IsPartTime"/> is derived as
/// <c>part_time_fraction &lt; 1.0m</c> when constructing the in-memory profile. This
/// eliminates the drift-burden between schema and SharedKernel shape that the original
/// cycle-1 plan carried.
/// </para>
///
/// <para>
/// <b>S138 / TASK-13801 — temporal editing (ADR-040 D8 as amended 2026-09-02).</b>
/// <see cref="SupersedeAndCreateAsync"/> now records a change at any PAST-OR-TODAY date, routing
/// via the pure <see cref="Temporal.TemporalWriteRouter"/> on a lock-held snapshot of the whole
/// timeline (cases A / B' / C' / E / G / T), with ONE client concurrency token per employee, the
/// S23-shape same-values no-op,
/// <c>employment_category</c> as an editable fourth field, and a <c>users.employment_category</c>
/// cache refresh sourced from the row covering TODAY. The S31/S33 paragraphs above are kept as
/// the history of how the shape got here.
/// </para>
///
/// <para>
/// <b>S141 / TASK-14102 — scheduling a change AHEAD (ADR-040 Increment 4), and the three things
/// that had been quietly relying on it being impossible.</b>
/// <list type="number">
///   <item><description><b>Reads answer "today", not "the open row".</b> Every current-state read in
///     this class now uses the end-exclusive as-of-today predicate. While future-dating was refused,
///     "the row with no end date" and "the row describing today" were necessarily the same row;
///     lifting the refusal separates them, and a read left on the old predicate would have started
///     reporting a not-yet-effective value as current.</description></item>
///   <item><description><b>The client concurrency token is <c>users.version</c></b>, not the open
///     row's own <c>version</c> (owner ruling OQ-3 (a)). One token per aggregate, ADR-019: a per-row
///     token cannot name a timeline that has more than one live-ish row, and the mismatch would have
///     412'd every profile edit forever after a single scheduled change.</description></item>
///   <item><description><b>The delete retires the row covering today AND anything scheduled</b>
///     (owner ruling OQ-5 (a)), by the zero-width-close idiom rather than a hard delete — see
///     <see cref="SoftDeleteTimelineAsync"/>. Left alone it would have stamped an end date on the
///     future row and produced an inverted, empty interval that the database does not forbid, while
///     the employee stayed un-deleted and the audit trail said otherwise.</description></item>
/// </list>
/// A fourth, additive piece: every as-of-today read also returns the NEXT scheduled change
/// (<see cref="GetByEmployeeIdWithScheduledAsync"/>), so no screen has to discover it with a second
/// query and none can show a value without saying another is coming.
/// </para>
/// </summary>
public sealed class EmployeeProfileRepository
{
    private readonly DbConnectionFactory _dbFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// S139 / TASK-13907 — server-"today" seam. <paramref name="timeProvider"/> is OPTIONAL and
    /// defaults to <see cref="TimeProvider.System"/>, so PRODUCTION BEHAVIOUR IS UNCHANGED and the
    /// existing direct test constructions keep compiling. DI fills it from the <c>TimeProvider</c>
    /// singleton registered in <c>Program.cs</c>; a date-sensitive test host may register a FIXED
    /// provider instead, so this repository's dated write paths observe the same "today" the suite
    /// fixes. S139 moved only the SOURCE of the clock; S142 / TASK-14205 (census rows 46 and 47)
    /// moved the DAY DERIVATION, from the UTC calendar day to the Copenhagen business day — see
    /// <see cref="Today"/>.
    /// </summary>
    public EmployeeProfileRepository(DbConnectionFactory dbFactory, TimeProvider? timeProvider = null)
    {
        _dbFactory = dbFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // ------------------------------------------------------------------
    // Reads — convenience self-managed overload + in-transaction overload.
    // Both join with `users` so the returned EmploymentProfile is fully hydrated
    // (S31 fields from employee_profiles + sibling fields from users per Q3 LEAVE).
    // ------------------------------------------------------------------

    /// <summary>
    /// S31 / TASK-3102 — convenience read: returns the employee profile that holds TODAY for
    /// <paramref name="employeeId"/>, fully hydrated with sibling fields from the <c>users</c>
    /// table (<see cref="EmploymentProfile.AgreementCode"/>, <see cref="EmploymentProfile.OkVersion"/>,
    /// <see cref="EmploymentProfile.EmploymentCategory"/>, <see cref="EmploymentProfile.OrgId"/>),
    /// or <c>null</c> if no row covers today for the employee.
    ///
    /// <para>
    /// <see cref="EmploymentProfile.IsPartTime"/> is computed as
    /// <c>part_time_fraction &lt; 1.0m</c> per refinement cycle 2 absorption — there is no
    /// <c>is_part_time</c> column in the schema.
    /// </para>
    ///
    /// <para>
    /// <b>AS-OF-TODAY single-purpose read (S34 / TASK-3413 audit lock; re-based S141 / TASK-14102).</b>
    /// Until S141 this selected the OPEN row (<c>effective_to IS NULL</c>) and called it "current";
    /// with future-dating that row can be a change that has not started yet, so the predicate is now
    /// the end-exclusive <c>effective_from &lt;= today AND (effective_to IS NULL OR effective_to &gt;
    /// today)</c>. See <see cref="ExecuteGetByEmployeeIdAsync"/> for the full reasoning and the clock
    /// choice. <b>Still MUST NOT be used for replay-sensitive (past-period / as-of-date) reads</b> —
    /// "today" is a live read, not a dated one; use
    /// <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentProfileResolver.GetByEmployeeIdAtAsync"/>
    /// (implemented by <c>EmploymentProfileResolver</c>) for dated lookups per ADR-023 D2 +
    /// the S34 PCS-replay cutover (TASK-3406). Sanctioned consumers are admin endpoints
    /// (GET via <see cref="GetByEmployeeIdWithVersionAsync"/>, DELETE via the
    /// <c>(conn, tx)</c> overload) and the S31 seeder's bootstrap probe — none are
    /// replay-sensitive.
    /// </para>
    /// </summary>
    public async Task<EmploymentProfile?> GetByEmployeeIdAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        var hit = await ExecuteGetByEmployeeIdAsync(conn, null, employeeId, Today(), ct);
        return hit?.Profile;
    }

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/>. Reuses the caller-
    /// supplied <paramref name="conn"/> + <paramref name="tx"/> so the read sits inside the
    /// same transaction as a downstream write (ADR-018 D5 atomic-outbox contract). Used by
    /// admin endpoint handlers that need to read-then-emit-event atomically and by
    /// <see cref="SupersedeAndCreateAsync"/>'s internal preflight when constructing audit payloads.
    ///
    /// <para>
    /// <b>AS-OF-TODAY single-purpose read (S34 / TASK-3413 audit lock; re-based S141 / TASK-14102).</b>
    /// Inherits the contract of the self-managed overload. <b>MUST NOT be used for replay-sensitive
    /// (past-period) reads</b>; route those through
    /// <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentProfileResolver.GetByEmployeeIdAtAsync"/>
    /// per ADR-023 D2 + S34 cutover. The only production consumer of this overload is the
    /// admin DELETE handler's pre-delete audit-payload snapshot — and S141 makes that consumer
    /// CORRECT rather than merely dated: it is the snapshot recorded as <c>previous_data</c> on the
    /// soft-delete audit row, so under future-dating the open-row version would have recorded the
    /// values of the row that had not started yet as "the state that was deleted", while the values
    /// actually in force went unrecorded. Answering as-of-today fixes that audit defect (S141 B4)
    /// without the endpoint changing a line.
    /// </para>
    /// </summary>
    public async Task<EmploymentProfile?> GetByEmployeeIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, CancellationToken ct = default)
    {
        var hit = await ExecuteGetByEmployeeIdAsync(conn, tx, employeeId, Today(), ct);
        return hit?.Profile;
    }

    /// <summary>
    /// Step 7a P2 fix — atomic row + version read. The GET endpoint must hand back the row
    /// data and its concurrency token from the SAME snapshot so the ETag it stamps
    /// matches the data it serializes; reading the two in separate statements opens a
    /// concurrency window where the response can carry stale fields with a newer ETag and
    /// the next admin edit would silently overwrite the racing change. Single SELECT;
    /// nullable tuple shape mirrors <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/>.
    ///
    /// <para>
    /// <b>S141 / TASK-14102 — the ROW is now as-of-today and the TOKEN is now <c>users.version</c>
    /// (owner ruling OQ-3 (a)).</b> S140's Step-7a fix coupled body and ETag to one SELECT precisely
    /// so they could never disagree; S141 breaks the coupling's old premise (body and token no longer
    /// live in the same ROW) and re-honours the promise a different way — one SELECT across both
    /// tables. <b>MUST NOT be used for replay-sensitive (past-period) reads</b> — past-period payroll
    /// / PCS-replay paths must use
    /// <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentProfileResolver.GetByEmployeeIdAtAsync"/>
    /// per ADR-023 D2 + S34 cutover. Sole production consumer is the admin GET handler; prefer
    /// <see cref="GetByEmployeeIdWithScheduledAsync"/>, which returns the same pair plus B0's
    /// scheduled change, and which this method is a thin projection of.
    /// </para>
    /// </summary>
    public async Task<(EmploymentProfile Profile, long Version)?> GetByEmployeeIdWithVersionAsync(
        string employeeId, CancellationToken ct = default)
    {
        var hit = await GetByEmployeeIdWithScheduledAsync(employeeId, ct);
        return hit is null ? null : (hit.Profile, hit.Version);
    }

    /// <summary>
    /// S141 / TASK-14102 (refinement B0, owner requirement 2026-09-11) — the same as-of-today read as
    /// <see cref="GetByEmployeeIdWithVersionAsync"/>, PLUS the next change already scheduled after
    /// today.
    ///
    /// <para>
    /// <b>Why this exists (plain language).</b> The owner asked: "should it not be visible to an HR
    /// employee looking at a page, that another has scheduled a change?" Once a change can be dated
    /// ahead, a screen that shows only today's value is not merely incomplete — it is misleading, and
    /// the two worst defects the S141 review found are both instances of it: an edit drawer that
    /// pre-fills a value without saying it is not yet in force, and a today-dated edit that silently
    /// expires on the day the scheduled one begins. Both dissolve once the scheduled change is on the
    /// screen. Carrying it in the READ PAYLOAD rather than leaving each screen to fetch it is the
    /// difference between a requirement and a bolt-on: every present and future consumer gets it, and
    /// nobody re-solves it badly.
    /// </para>
    ///
    /// <para>
    /// <see cref="ProfileAsOfTodayHit.Scheduled"/> is <c>null</c> when nothing is scheduled — which,
    /// until Increment 4's date picker ships, is every employee. A zero-width row is not a scheduled
    /// change (see the shared SQL's comment). One SELECT, so the value, the token and the scheduled
    /// change can never describe three different moments.
    /// </para>
    /// </summary>
    public async Task<ProfileAsOfTodayHit?> GetByEmployeeIdWithScheduledAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteGetByEmployeeIdAsync(conn, null, employeeId, Today(), ct);
    }

    /// <summary>
    /// S137 / TASK-13704 — history read consumed by the S137 payroll planner (TASK-13702):
    /// the <c>effective_from</c> dates of this employee's profile rows STRICTLY INSIDE the
    /// range <c>(afterExclusive, toInclusive]</c>, ascending. Each date is a day on which a
    /// new profile row took effect (history rows included — the history unique index means
    /// closed predecessors exist), i.e. the planner's <c>EmployeeProfileChange</c> segment-
    /// boundary feed per ADR-040 D5: a boundary date is the FIRST day of the NEW segment,
    /// which is exactly a successor row's <c>effective_from</c>.
    ///
    /// <para>
    /// <b>Range semantics (the recurring fencepost hazard, stated so callers don't guess):</b>
    /// <paramref name="afterExclusive"/> is EXCLUDED — the planner passes the period start
    /// here because a row taking effect ON the period start creates no INTERIOR boundary
    /// (the segment already starts there); <paramref name="toInclusive"/> is INCLUDED.
    /// Uniqueness per date is by construction (<c>idx_employee_profiles_history</c> — at
    /// most one row per (employee_id, effective_from)), so no DISTINCT is needed.
    /// </para>
    ///
    /// <para>
    /// <b>Self-managed connection, LIVE + HISTORY rows, read-only, outside locks</b> —
    /// matches the repo's self-managed read style (<see cref="GetByEmployeeIdAsync(string, CancellationToken)"/>):
    /// planning is a pure read that never rides a write transaction. Deliberately NO
    /// <c>effective_to</c> filter: the live row's <c>effective_from</c> is a change date
    /// exactly like a closed predecessor's.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<DateOnly>> GetEffectiveFromDatesAsync(
        string employeeId, DateOnly afterExclusive, DateOnly toInclusive,
        CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT effective_from
            FROM employee_profiles
            WHERE employee_id = @employeeId
              AND effective_from > @afterExclusive
              AND effective_from <= @toInclusive
            ORDER BY effective_from
            """;
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("afterExclusive", afterExclusive);
        cmd.Parameters.AddWithValue("toInclusive", toInclusive);
        var dates = new List<DateOnly>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dates.Add(reader.GetFieldValue<DateOnly>(0));
        return dates;
    }

    /// <summary>
    /// <b>AS-OF-TODAY single-purpose shared codepath (S34 / TASK-3413 audit lock, re-based by
    /// S141 / TASK-14102 B1).</b> Returns the profile that holds TODAY, its aggregate concurrency
    /// token, and — B0 — the next change SCHEDULED after today, if one exists.
    ///
    /// <para>
    /// <b>What changed in S141 and why (plain language first).</b> Until S141 this read selected
    /// <c>ep.effective_to IS NULL</c> — "the row with no end date" — and called the answer "current".
    /// That worked only because every write dated after today was refused, which made "the row with
    /// no end date" and "the row describing today" the same row by accident. Increment 4 lets HR
    /// schedule a change ahead ("part-time from 1 November"), and the moment such a row exists the
    /// open row is the FUTURE one. Left alone, this read would have started reporting a
    /// not-yet-effective fraction and position as the employee's current state — the value would
    /// then have been echoed straight back by the edit drawer and written into force six weeks
    /// early, silently revaluing already-approved holiday. So the predicate now states the question
    /// it always meant: <c>effective_from &lt;= today AND (effective_to IS NULL OR effective_to &gt;
    /// today)</c> — the same end-exclusive shape ADR-018 D9 uses everywhere and the one already
    /// in-tree at <c>HrFollowUpApprovalReadRepository</c>.
    /// </para>
    ///
    /// <para>
    /// <b>The token moved with it (S141 B5 / owner ruling OQ-3 (a)).</b> The version returned here is
    /// <c>users.version</c>, NOT <c>ep.version</c>. One concurrency token per aggregate (ADR-019):
    /// once a timeline can hold more than one live-ish row, a per-ROW token cannot identify the
    /// aggregate — the GET would hand out the row-covering-today's version while the writer checked
    /// the open row's, and every profile edit would 412 forever after a single scheduled change,
    /// with a refresh returning the very token the writer rejects. <c>users.version</c> belongs to no
    /// single row, so it survives a timeline with several. This is not a new design: the sibling
    /// <c>user_agreement_codes</c> timeline already made exactly this choice, with its reasoning
    /// written out (<see cref="UserAgreementCodeRepository.SupersedeAndCreateAsync"/>). Profiles kept
    /// a per-row token only because, before future-dating, the open row WAS the whole aggregate.
    /// </para>
    ///
    /// <para>
    /// <b>Still not for replay.</b> "Today" is a live read. Past-period / as-of-date lookups still go
    /// through
    /// <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentProfileResolver.GetByEmployeeIdAtAsync"/>
    /// per ADR-023 D2 + the S34 cutover; nothing here is replay-safe just because it is now dated.
    /// </para>
    ///
    /// <para>
    /// <b>"today" is the writers' COPENHAGEN business day</b>
    /// (<see cref="Today"/>, via the injected <see cref="TimeProvider"/>) — the SAME day the writers
    /// and the caches use (QUAL-157). That invariant is what this paragraph has always stated: if the
    /// read flipped at a different midnight from the write, a change scheduled for the 1st would be
    /// visible before, or after, the row that produced it took effect. Until S142 both sides were the
    /// UTC day and this paragraph said so, describing the Copenhagen day as deliberately NOT used
    /// here; S142 / TASK-14205 moved BOTH sides together, so the rule is unchanged and only the
    /// calendar it names has moved.
    /// </para>
    /// </summary>
    private static async Task<ProfileAsOfTodayHit?> ExecuteGetByEmployeeIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, DateOnly today, CancellationToken ct)
    {
        // S31 employee_profiles columns are the source of truth for
        // part_time_fraction and position (weekly_norm_hours removed in S53 TASK-5306). The sibling fields (agreement_code, ok_version,
        // primary_org_id) stay on `users` per refinement Q3 LEAVE and are joined in here so
        // the returned EmploymentProfile is consumable by PCS / rule engine paths unchanged.
        // employment_category (S137 / ADR-040 D4 → S138 / TASK-13804): the DATED ep column is
        // the sole source. S137's COALESCE-to-users fail-safe is RETIRED — the column is NOT
        // NULL since S138 (init.sql segment `s138-profile-category-not-null`), so there is
        // nothing to fall back to, and with the category now editable per date the live users
        // column is only the CACHE of the row covering TODAY: falling back to it would
        // mislabel rather than rescue.
        //
        // S140 Step-7a P2 required the body and the ETag to come from ONE SELECT so they can never
        // describe different states. S141 keeps that promise even though the two now come from
        // different TABLES: `u.version` is read in the same statement as the row, and the B0
        // scheduled-change lookup rides along in the same statement too, so a screen can never show
        // today's value stamped with a token taken a moment later.
        //
        // ORDER BY + LIMIT 1 on the covering row is DEFENSIVE, and it is new. The retired
        // `effective_to IS NULL` predicate could not match twice — the partial-unique index
        // idx_employee_profiles_live guaranteed at most one open row. The as-of-today predicate has
        // no such index behind it: non-overlap is a router invariant (TimelineSnapshot.Build throws
        // on an overlap) and the history unique index only forbids two rows with the SAME start, so
        // the DATABASE does not forbid two rows covering today. One row is still the only shape the
        // writers can produce; the LIMIT makes the read deterministic rather than trusting that.
        //
        // B0 (owner requirement): the NEXT row starting after today rides along, so every caller can
        // say "a different value is scheduled, from this date" without a second query. A ZERO-WIDTH
        // row [f, f) is excluded: it covers no day at all and is the retirement trace a soft-delete
        // leaves behind (S141 B4), so reporting it as a scheduled change would show HR a change that
        // was deliberately cancelled.
        //
        // That rule — "starts after today AND is not zero-width" — is stated once, with its full
        // reasoning, in EmploymentTimelineSql.ScheduledRowPredicate (S141 / TASK-14116), which the
        // three LIST reads splice in. The predicate below is character-identical to it and is spelled
        // out here because this statement also SELECTS the scheduled row's values, which the list
        // reads deliberately do not. If the rule ever changes, it changes in both places or the
        // roster and the profile page will disagree about whether a change exists.
        const string sql =
            """
            SELECT
                ep.part_time_fraction,
                ep.position,
                u.version AS aggregate_version,
                u.agreement_code,
                u.ok_version,
                ep.employment_category,
                u.primary_org_id,
                nxt.effective_from      AS scheduled_from,
                nxt.effective_to        AS scheduled_to,
                nxt.part_time_fraction  AS scheduled_fraction,
                nxt.position            AS scheduled_position,
                nxt.employment_category AS scheduled_category
            FROM employee_profiles ep
            INNER JOIN users u ON u.user_id = ep.employee_id
            LEFT JOIN LATERAL (
                SELECT s.effective_from, s.effective_to, s.part_time_fraction,
                       s.position, s.employment_category
                FROM employee_profiles s
                WHERE s.employee_id = ep.employee_id
                  AND s.effective_from > @today
                  AND (s.effective_to IS NULL OR s.effective_to > s.effective_from)
                ORDER BY s.effective_from
                LIMIT 1
            ) nxt ON TRUE
            WHERE ep.employee_id = @employeeId
              AND ep.effective_from <= @today
              AND (ep.effective_to IS NULL OR ep.effective_to > @today)
            ORDER BY ep.effective_from DESC
            LIMIT 1
            """;
        await using var cmd = tx is null
            ? new NpgsqlCommand(sql, conn)
            : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("today", today);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;

        var partTimeFraction = reader.GetDecimal(reader.GetOrdinal("part_time_fraction"));
        var profile = new EmploymentProfile
        {
            EmployeeId = employeeId,
            AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
            OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
            EmploymentCategory = reader.GetString(reader.GetOrdinal("employment_category")),
            // S31 cycle 2 absorption: IsPartTime is computed, not stored. No is_part_time
            // column in the schema.
            IsPartTime = partTimeFraction < 1.0m,
            PartTimeFraction = partTimeFraction,
            Position = reader.IsDBNull(reader.GetOrdinal("position"))
                ? null
                : reader.GetString(reader.GetOrdinal("position")),
            OrgId = reader.GetString(reader.GetOrdinal("primary_org_id")),
        };
        var version = reader.GetInt64(reader.GetOrdinal("aggregate_version"));

        var scheduledFromOrd = reader.GetOrdinal("scheduled_from");
        ScheduledEmployeeProfileChange? scheduled = null;
        if (!reader.IsDBNull(scheduledFromOrd))
        {
            var scheduledToOrd = reader.GetOrdinal("scheduled_to");
            var scheduledPositionOrd = reader.GetOrdinal("scheduled_position");
            var scheduledCategoryOrd = reader.GetOrdinal("scheduled_category");
            scheduled = new ScheduledEmployeeProfileChange(
                EffectiveFrom: reader.GetFieldValue<DateOnly>(scheduledFromOrd),
                EffectiveTo: reader.IsDBNull(scheduledToOrd)
                    ? null
                    : reader.GetFieldValue<DateOnly>(scheduledToOrd),
                PartTimeFraction: reader.GetDecimal(reader.GetOrdinal("scheduled_fraction")),
                Position: reader.IsDBNull(scheduledPositionOrd)
                    ? null
                    : reader.GetString(scheduledPositionOrd),
                EmploymentCategory: reader.IsDBNull(scheduledCategoryOrd)
                    ? null
                    : reader.GetString(scheduledCategoryOrd));
        }

        return new ProfileAsOfTodayHit(profile, version, scheduled);
    }

    /// <summary>
    /// The writers' "today": the COPENHAGEN business day off the injected clock
    /// (QUAL-157 / S139 seam for the SOURCE; S142 / TASK-14205, census row 46, for the CALENDAR).
    ///
    /// <para>
    /// Every business date this class produces or compares against goes through here, which is why
    /// it is one method and not five inlined expressions. Five call sites depend on it and they span
    /// both mechanisms the sprint distinguishes: the as-of-today READS at <c>:164</c>, <c>:194</c>
    /// and <c>:255</c> (which row is in force right now), the dated writer's routing/cache anchor at
    /// <c>:709</c>, and the soft-delete's close-date FALLBACK at <c>:1155</c>, which becomes a row's
    /// <c>effective_to</c> and the emitted event's <c>EffectiveTo</c> — a STORED date, not a view.
    /// On the UTC calendar, all five were a day early for the one-to-two hours between Danish
    /// midnight and UTC midnight (Denmark is UTC+1 CET / UTC+2 CEST), which for the writer means a
    /// row stamped as having ended yesterday.
    /// </para>
    ///
    /// <para>
    /// <b>Business dates only.</b> <c>created_at</c>, <c>updated_at</c> and outbox ordering in this
    /// class stay UTC instants and must never be routed through here — moving one of those would
    /// corrupt the audit chain, which is an inviolable invariant rather than a convention.
    /// </para>
    /// </summary>
    private DateOnly Today() => CopenhagenBusinessDate.Today(_timeProvider);

    // ------------------------------------------------------------------
    // Writes — atomic-outbox (conn, tx) overloads only (ADR-018 D5).
    // S31 scope: UPDATE the live row (no supersession routing); INSERT a fresh row
    // for net-new employees. Both bump nothing — INSERT writes version=1, UPDATE
    // increments by one. Endpoint owns audit + outbox emission.
    // ------------------------------------------------------------------

    /// <summary>
    /// S31 / TASK-3102 — atomic-outbox INSERT overload for a brand-new live profile row, and
    /// since S143 / TASK-14308 the single write path for the two CREATE-A-PERSON routes: the boot
    /// backfill seeder and the admin create-person endpoint. It is deliberately NOT the only INSERT
    /// into <c>employee_profiles</c> in the product — <see cref="SupersedeAndCreateAsync"/> Case A
    /// (and Case T) also produces a net-new live row, via <c>InsertLiveRowAsync</c>, for a
    /// profile-less employee. That is a separate, intended route: it is the DATED WRITER, which locks
    /// the whole timeline and routes through <see cref="Temporal.TemporalWriteRouter"/> because it has
    /// to reason about rows that may already exist; this method is the unconditional first-row insert
    /// and does neither. Consolidating the two would put timeline routing in front of a create that
    /// by definition has no timeline.
    /// Used by TASK-3106 EmployeeProfileSeeder during bootstrap (one row per existing user)
    /// and by TASK-3108 AdminEndpoints POST extension (4-way atomicity: users INSERT +
    /// employee_profiles INSERT + UserCreated outbox + EmployeeProfileCreated outbox, all
    /// in one tx). Writes <c>version = 1</c>, <c>effective_to = NULL</c>, and
    /// <c>effective_from = </c><paramref name="req"/><c>.EffectiveFrom</c>.
    /// Caller commits or rolls back the transaction; endpoint emits the audit row + outbox
    /// event in the same tx after this returns.
    ///
    /// <para>
    /// <b>S143 / TASK-14308 (QUAL-177, owner ruling OQ-4) — one create path, and why the
    /// DATE is an argument.</b> Until S143 there were THREE ways to create a person's first row: this method
    /// (which had no production caller and therefore could drift from reality indefinitely while
    /// its tests kept passing), the boot seeder's own inline INSERT, and the admin create-person
    /// endpoint's own inline INSERT. Consolidating them onto this method required moving the date
    /// OUT of it, because the two real callers need DIFFERENT dates and one of them needs its date
    /// read exactly once:
    /// <list type="bullet">
    ///   <item><description><b>The seeder must anchor at <c>'0001-01-01'</c>, not today.</b> A
    ///     backfill covers employees who already existed, so their HISTORICAL periods must resolve;
    ///     the resolver's predicate is <c>effective_from &lt;= asOfDate</c>, so a today-stamped
    ///     backfill row leaves every pre-deployment date uncovered and PCS/Compliance fail closed
    ///     with a 500 on any historical calculation. S33 found and fixed that defect once already.</description></item>
    ///   <item><description><b>The admin create must stamp today exactly ONCE.</b> The endpoint
    ///     computes one <c>effectiveFrom</c> above its transaction and feeds it to the users row,
    ///     this profile row and the <c>EmployeeProfileCreated</c> event — the S137 owner ruling
    ///     "ONE date for the whole create", made so a midnight straddle between two separate clock
    ///     reads cannot leave those three a day apart. Had this method kept reading the clock, the
    ///     admin path would have acquired a SECOND read inside the very create that exists to have
    ///     one.</description></item>
    /// </list>
    /// Hence: no clock read here. <c>Today()</c> remains the class's single business-date source for
    /// the READS and for the dated writer / soft-delete, which legitimately mean "now".
    /// </para>
    ///
    /// <para>
    /// Returns <c>(profile_id, version=1)</c> for the inserted row — the row's OWN version, which the
    /// callers record in their <c>employee_profile_audit</c> CREATED row.
    /// <b>It is NOT the ETag the 201 hands the client</b> (corrected S143 / TASK-14308; the sentence
    /// here claimed it was, and had been wrong since S138). Since S138 / TASK-13801 the client's
    /// concurrency token is <c>users.version</c> — one token per aggregate, not per row — and the
    /// admin create-person response carries the committed users version, which is 2 rather than 1
    /// because the agreement-code write in the same transaction bumps it
    /// (<c>AdminEndpoints.cs</c>, <c>createdUsersVersion</c>). See
    /// <see cref="SupersedeAndCreateAsync"/>'s "one concurrency token per aggregate" paragraph.
    /// </para>
    /// </summary>
    /// <exception cref="PostgresException">
    /// Thrown on partial-unique-index conflict (<c>idx_employee_profiles_live</c>) when a
    /// live row already exists for <paramref name="req"/><c>.EmployeeId</c>. The admin endpoint
    /// translates <c>SqlState = "23505"</c> to 409 Conflict.
    /// <para>
    /// The SEEDER hits this too, and handles it (corrected S143 / TASK-14308 — this said the seeder
    /// "should never hit this case because it guards on existing rows"). Its guard is a NOT EXISTS
    /// read taken OUTSIDE the per-row transaction, so two application instances starting at the same
    /// moment can both pass it for the same employee; the loser's INSERT loses the race on
    /// <c>idx_employee_profiles_live</c>, and <c>EmployeeProfileSeeder</c> catches the 23505, rolls
    /// that one row back and carries on (it is logged as a skipped concurrent-startup race, not an
    /// error — the winner already wrote the row the loser wanted).
    /// </para>
    /// </exception>
    public async Task<(Guid ProfileId, long Version)> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeProfileCreateRequest req, CancellationToken ct = default)
    {
        // profile_id is generated client-side so the endpoint can include it in the
        // outbox event body (S29 WTM precedent at WageTypeMappingRepository.cs:137).
        // S33 in-flight defect fix, AS AMENDED BY S143 / TASK-14308: effective_from is an
        // EXPLICIT column value rather than the schema DEFAULT '0001-01-01', because under
        // TASK-3302's 3-case routing the first PUT against a default-stamped row triggers Case C
        // cross-day supersession (because '0001-01-01' < today), creating a brand-new successor
        // row at version=1 instead of UPDATE-in-place at version=2. The admin create therefore
        // passes TODAY, which puts the fresh row in the same-day window for any same-day PUT
        // (Case B routing → version bump), matching pre-S33 admin expectations. The BACKFILL
        // seeder passes the '0001-01-01' anchor DELIBERATELY and accepts that Case C routing,
        // because covering historical dates matters more there (see the method doc).
        // S137 / ADR-040 D4 — employment_category is populated same-tx from the users value
        // via the scalar subselect below (same conn+tx, so it sees an uncommitted users row
        // in this transaction; the employee_id FK guarantees the row exists). dated==live is
        // the S137 invariant; users' category is write-once until Increment 3, so
        // copy-from-users == copy-from-predecessor by construction.
        var newProfileId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version, employment_category)
            VALUES (
                @profileId, @employeeId, @partTimeFraction, @position,
                @effectiveFrom::date, NULL, 1,
                (SELECT u.employment_category FROM users u WHERE u.user_id = @employeeId))
            RETURNING profile_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("profileId", newProfileId);
        cmd.Parameters.AddWithValue("employeeId", req.EmployeeId);
        cmd.Parameters.AddWithValue("partTimeFraction", req.PartTimeFraction);
        cmd.Parameters.AddWithValue("position", (object?)req.Position ?? DBNull.Value);
        // S142 / TASK-14205 (census row 47) — a STORED STAMP: this value IS the new profile row's
        // `effective_from`, the first day the employee's profile is in force. Creating an employee
        // at 00:30 Danish time used to stamp the profile as having started YESTERDAY, so where the
        // value MEANS "today" it must be the COPENHAGEN business day.
        // S143 / TASK-14308 — the value now arrives from the CALLER instead of being read here, so
        // "must be the Copenhagen day" became the ADMIN endpoint's obligation (it derives its single
        // `effectiveFrom` from CopenhagenBusinessDate) and NOT the seeder's, whose anchor is a fixed
        // historical date that no calendar applies to. See the method doc for why the two differ.
        //
        // ── BOUND AS TEXT AND CAST (`@effectiveFrom::date`), NOT AS A DateOnly. Read this before
        // "simplifying" it back. Npgsql 8 enables DateTime infinity conversions BY DEFAULT: it maps
        // DateOnly.MinValue (0001-01-01) to Postgres `DATE '-infinity'` and DateOnly.MaxValue to
        // `'infinity'`. The backfill seeder's anchor IS DateOnly.MinValue, so binding it as a
        // DateOnly would silently persist `-infinity` where the column previously held the finite
        // `'0001-01-01'` the schema DEFAULT wrote. Two things break when it does:
        //   (1) ROW/EVENT PARITY — the row would hold `-infinity` while the EmployeeProfileCreated
        //       event carries "0001-01-01" (System.Text.Json has no such special case). The seeder
        //       feeds ONE constant to both precisely so they cannot diverge; the sentinel would
        //       reintroduce the divergence underneath that, which is an auditability defect.
        //   (2) A SPLIT SENTINEL IN ONE COLUMN — every row seeded before this change is finite, so
        //       any `effective_from = DATE '0001-01-01'` comparison would match the old rows and
        //       miss the new ones, and vice versa for `-infinity`.
        // The round trip HIDES this: Npgsql converts `-infinity` back to DateOnly.MinValue on read,
        // so a DateOnly read-back assertion passes over changed data. Tests must assert the stored
        // value in SQL with `isfinite(...)`; see ProfileCreateSingleWritePathTests.
        // The global opt-out (DisableDateTimeInfinityConversions) is deliberately NOT used: it would
        // change persisted semantics repo-wide, and the sibling UserAgreementCodeBackfillSeeder
        // (which writes `-infinity` today) has a test asserting exactly that. The fix stays local to
        // this one write path. The cast is a no-op for every finite date the admin path passes.
        cmd.Parameters.AddWithValue(
            "effectiveFrom", req.EffectiveFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — INSERT ... RETURNING always yields one row on success.
            throw new InvalidOperationException(
                $"CreateAsync produced no row for employee_id='{req.EmployeeId}'.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// S33 / TASK-3302, generalized by S138 / TASK-13801 (ADR-040 D8 as amended 2026-09-02) — the
    /// canonical dated write for employee profiles: records a change AT ANY PAST-OR-TODAY DATE
    /// against the employee's timeline, splitting the row that covers that date. Routing is the
    /// pure <see cref="TemporalWriteRouter"/> (see its case table — A / B' / C' / E / G / T — and
    /// its DB-free matrix tests) applied to the LOCK-HELD snapshot of the whole timeline
    /// (ADR-020 D2: the decision is made on the locked rows, never on a pre-lock read).
    ///
    /// <para>
    /// <b>Plain-language contract.</b> "This person's fraction actually changed on the 10th" is
    /// now a legal write: the row covering the 10th is closed on the 10th and a new row runs from
    /// the 10th to wherever the old row used to end (open, if it was the open row). Editing a row
    /// on its own start date changes it in place. A date in a gap gets a row filling the gap; a
    /// date before the first row gets a row ending where the first begins. Later rows are never
    /// touched.
    /// </para>
    ///
    /// <para>
    /// <b>S141 / TASK-14102 — FUTURE dates are now allowed (ADR-040 Increment 4).</b> "She goes
    /// part-time on 1 November" is a legal write: it routes through the same C' split as any other
    /// date, because the row that covers a future day is the row that is open today. Two consequences
    /// a caller must know, both of which follow from the split rule rather than from anything new:
    /// <list type="bullet">
    ///   <item><description>A TODAY-dated write made while a change is already scheduled produces a
    ///     row <c>[today, scheduledFrom)</c> — a CLOSED row, kind <c>Inserted</c>, not
    ///     <c>Superseded</c> — and the edit therefore EXPIRES on the scheduled date. Every pre-S141
    ///     today-dated caller assumed "from now on" and got it; that assumption now holds only while
    ///     nothing is scheduled.</description></item>
    ///   <item><description>The write revalues absences inside the interval it produced, which is
    ///     bounded by the next row's start. That is correct and must not be suppressed: recorded days
    ///     of holiday are fraction-dependent (ADR-032 D1), so booking made for a period the new
    ///     fraction covers has to follow it.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>One concurrency token per aggregate (S138 Reviewer W1 / Codex B4; RE-BASED by S141 /
    /// TASK-14102 B5 under owner ruling OQ-3 (a)).</b> The client's token is <c>users.version</c> —
    /// what the profile GET's ETag now carries — and <paramref name="expectedVersion"/> is validated
    /// against it. Until S141 the token was the OPEN row's own <c>version</c>, which worked only
    /// because the open row was the whole aggregate; with a scheduled row present, the GET describes
    /// the row covering TODAY while a per-row check would guard the FUTURE row, so every edit would
    /// 412 forever and refreshing would return the very token the check rejects. <c>users.version</c>
    /// belongs to no row and so survives a timeline of any shape — the choice the sibling
    /// <c>user_agreement_codes</c> writer already made and documented.
    /// EVERY timeline write bumps it, including a history-only split, so the ETag stays a monotonic
    /// per-employee marker: two admins backdating against the same ETag serialize on the lock and the
    /// second gets a 412. The rows' own <c>version</c> columns keep their pre-S141 per-row semantics
    /// (the open row is still bumped on every write; history rows are left alone) because
    /// <c>employee_profile_audit</c> and the events narrate those — they are simply no longer what a
    /// client holds. <see cref="SaveEmployeeProfileResult.Version"/> is the aggregate token AFTER the
    /// write in every case and <see cref="SaveEmployeeProfileResult.TimelineVersionBefore"/> the one
    /// before it; the touched row's own version is
    /// <see cref="SaveEmployeeProfileResult.ProducedRowVersion"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Same-values no-op (the S23 shape).</b> When the request equals the covering row
    /// field-for-field (fraction, position, category) the write is a no-op decided INSIDE the
    /// lock AFTER the If-Match check: no row, no version bump,
    /// <see cref="SaveEmployeeProfileResult.IsNoOp"/> set and the current token returned. A stale
    /// caller cannot hide a version mismatch behind an apparent no-op. The endpoints skip events,
    /// revaluation and worklist rows on it. The comparison is against the COVERING row: a
    /// backdated value equal to TODAY's value still writes (it changes history); only a value
    /// equal to the row that already covers the date does nothing.
    /// </para>
    ///
    /// <para>
    /// <b>The fourth field.</b> <c>employment_category</c> is written on every row this method
    /// produces: the request value when supplied, else the covering row's, else (a legacy NULL
    /// cell) the live <c>users</c> value — never NULL post-S137.
    /// </para>
    ///
    /// <para>
    /// <b>Cache rule + token bump (the latter widened in S141).</b> <c>users.employment_category</c>
    /// means "the category as of TODAY". After the row write this method re-reads the row covering
    /// today and writes <c>UPDATE users SET employment_category = &lt;that value, when there is
    /// one&gt;, version = version + 1</c>. <b>The VALUE moves only when today's category moved; the
    /// VERSION moves on every write</b> (S141 B5 — a token that sits still after a fraction edit
    /// cannot detect the concurrent edit it exists to detect). A cache refresh IS a users-row write
    /// under ADR-018 D7 (the admin user DTO exposes the field, so a stale users ETag must 412
    /// afterwards). The cache is never set from the REQUEST: a historical-only correction leaves the
    /// cached value untouched by construction. Not gated on <c>is_active</c> — a departed employee's
    /// cache must be correctable. The repository does not know the actor, so it returns
    /// <see cref="SaveEmployeeProfileResult.UsersVersionBefore"/> / <c>After</c> plus the old/new
    /// value for the endpoint's <c>users_audit</c> row — which, post-S141, is owed on every real
    /// write rather than only on a category change.
    /// </para>
    ///
    /// <para>
    /// <b>Locking.</b> One <c>SELECT … FOR UPDATE</c> over the employee's WHOLE timeline, ordered
    /// open row first then history ascending (<see cref="LockTimelineAsync"/>). Every writer thus
    /// serializes on the open row first — the pre-S138 lock point, re-entrant with callers that
    /// pre-lock it — and gap-inserters serialize on the history rows, so two backdates into the
    /// same gap cannot produce overlapping rows. The unique indexes stay the backstop for the
    /// zero-row race (two Case-A inserts): a unique-violation is re-thrown as
    /// <see cref="ConcurrentSeedConflictException"/> (the S35 catch, generalized to every INSERT path).
    /// </para>
    ///
    /// <para>
    /// <b>Why T is repository-only.</b> A soft-delete leaves closed rows and no open row; a later
    /// write dated after the last close re-creates the open row (case T). The profile PUT keeps its
    /// "no open row → 404" pre-check (the S33 Step-0b BLOCKER-3 ruling: PUT is an EDIT surface and
    /// never creates a net-new row), so an edit can never resurrect a deliberately retired profile;
    /// T serves the re-create callers at the repository level.
    /// </para>
    ///
    /// <para>
    /// <b>Atomic-outbox contract (ADR-018 D5).</b> Caller owns the transaction (ReadCommitted or
    /// stricter); this method writes <c>employee_profiles</c> and, on a cache refresh, <c>users</c>.
    /// The endpoint emits audit + outbox rows in the same tx from the result
    /// (<see cref="SaveEmployeeProfileResult.Outcome"/> / <see cref="SaveEmployeeProfileResult.Kind"/>,
    /// the covering pre-image, the new interval).
    /// </para>
    /// </summary>
    /// <exception cref="TemporalWriteRejectedException">
    /// <see cref="TemporalWriteRejection.PrecedesEmploymentStart"/> when the
    /// caller supplied <c>req.EmploymentStartDate</c> and the date precedes it — a pure
    /// predicate raised BEFORE any lock (cheap, nothing to roll back). There is deliberately no
    /// upper bound (see <see cref="TemporalWriteRouter.PrecedesEmploymentStart"/>), and
    /// <see cref="TemporalWriteRejection.FutureDated"/> is no longer raised at all (S141 / B3).
    /// <see cref="TemporalWriteRejection.NoRecordedEmploymentCategory"/> (S138 Step-5a) is different
    /// and is raised AFTER the timeline lock and the If-Match check, because it can only be decided
    /// once the routed case is known: router case E puts the write before every recorded row, so no
    /// row covers or precedes the date and nothing records which category held then — and the only
    /// remaining source, the live <c>users</c> value, means "as of today" and would mislabel history.
    /// The caller's transaction is rolled back by the endpoint, so the late throw costs a lock, not
    /// correctness. Both map to a date-free 422 — "date-free" is load-bearing, not stylistic: the
    /// employment-start floor's message must never echo the hire date (ADR-040 D7 keeps employment
    /// dates out of every DTO, response and error body).
    /// </exception>
    /// <exception cref="OptimisticConcurrencyException">
    /// <paramref name="expectedVersion"/> non-null and (a) no row covers TODAY
    /// (<c>ActualVersion = null</c> — pre-S141 this read "no open row", the same statement while a
    /// future row could not exist) or (b) <c>users.version</c> differs. Endpoint maps to 412.
    /// </exception>
    /// <exception cref="ConcurrentSeedConflictException">
    /// An INSERT lost a race on <c>idx_employee_profiles_live</c> / <c>idx_employee_profiles_history</c>
    /// (unique-violation 23505). The transaction is aborted; refresh and retry. Endpoint maps to 409.
    /// </exception>
    public async Task<SaveEmployeeProfileResult> SupersedeAndCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeProfileSupersedeRequest req, long? expectedVersion,
        CancellationToken ct = default)
    {
        // "Today" is the COPENHAGEN business day, read via the injected TimeProvider (S139 /
        // TASK-13907 moved the SOURCE onto the DI seam; S142 / TASK-14205 moved the CALENDAR). The
        // endpoints and the S33 today-stamp derive theirs the same way, so caller and repository
        // cannot disagree about which day a write belongs to. The router below stays PURE: `today`
        // is passed IN as a parameter (PAT-025), never read inside it.
        //
        // What `today` decides HERE — worth naming, because it is not the routing case. The router
        // reads `requestFrom` and the locked timeline to choose B'/C'/E/G/T and (see its own comment)
        // does not read `today` at all. This value decides (a) `coveringToday` below, which is the
        // "nothing covers today" 404 branch an If-Match caller hits, and (b) the anchor for the
        // employment-category cache refresh at step 6, which must follow the row IN FORCE rather than
        // the row being written.
        var today = Today();

        // 0. Pure refusals BEFORE any lock — nothing to roll back, nothing to contend on.
        //
        //    S141 / TASK-14102 (B3) — the FUTURE-DATING refusal that stood here is GONE (ADR-040
        //    Increment 4). HR can now record "she goes part-time on 1 November". No new routing case
        //    was needed: a future date's covering row is the row that is open today, so it routes
        //    through the router's C' exactly like any other split — close the covering row at the
        //    requested date and insert [from, covering.oldTo), which is open when the covering row
        //    was. What DID need work is everything that had been quietly relying on the refusal to
        //    make "the open row" and "the row covering today" the same row: the reads above, the
        //    token below, and the delete. The employment-start FLOOR stays; there is deliberately no
        //    ceiling (see TemporalWriteRouter.PrecedesEmploymentStart).
        if (TemporalWriteRouter.PrecedesEmploymentStart(req.EffectiveFrom, req.EmploymentStartDate))
            throw new TemporalWriteRejectedException(TemporalWriteRejection.PrecedesEmploymentStart, "employee profile");

        // 1. Lock the whole timeline (open row first). From here to commit no other writer can
        //    touch this employee's rows, so every decision below is made on locked state.
        var timeline = await LockTimelineAsync(conn, tx, req.EmployeeId, ct);
        var live = timeline.FirstOrDefault(r => r.EffectiveTo is null);
        var coveringToday = timeline.FirstOrDefault(
            r => r.EffectiveFrom <= today && (r.EffectiveTo is null || r.EffectiveTo.Value > today));

        // 1b. Lock the `users` row and read the AGGREGATE TOKEN. Taken AFTER the timeline lock, which
        //     keeps the lock order this method has always had (employee_profiles → users; the cache
        //     refresh at step 6 already locked users last), so no NEW deadlock edge appears — the
        //     same lock is acquired earlier in the same order. Two honest side effects of moving it:
        //     the users row is now held for the whole write rather than just its tail, and a same-
        //     values NO-OP now takes it too (it cannot not: the token must be checked BEFORE the
        //     no-op is decided, or a stale caller could hide a version mismatch behind an apparent
        //     no-op — the S138 rule this preserves).
        var (usersVersionBefore, usersCategoryBefore) =
            await LockUsersRowAsync(conn, tx, req.EmployeeId, ct);

        // 2. Validate the client's token (admin-strict If-Match, ADR-019).
        //
        //    S141 / TASK-14102 (B5, owner ruling OQ-3 (a)) — THE TOKEN IS NOW `users.version`.
        //
        //    Why it had to move, in plain terms: a concurrency token answers "has anyone changed
        //    this since you read it?", and "this" is the employee's profile TIMELINE, not one row of
        //    it. While future-dating was refused, the open row WAS the whole timeline, so the open
        //    row's own version was an accidentally-correct token. Once a scheduled row can exist,
        //    the GET answers about the row covering TODAY while this check asked about the OPEN row
        //    — two different rows — so every profile edit would have returned 412 forever after a
        //    single scheduled change, and refreshing would have handed back exactly the token this
        //    check rejects. `users.version` belongs to no row, so it survives a timeline of any
        //    shape. The sibling agreement-code timeline already made this call, for the same reason
        //    (UserAgreementCodeRepository.SupersedeAndCreateAsync).
        //
        //    The cost, stated rather than hidden: `users.version` is now the ONE token for the whole
        //    employee record — the users row, the agreement timeline and the profile timeline. So a
        //    profile edit invalidates a pending admin users PUT and vice versa, where before they
        //    were independent. That is the meaning of "one token per aggregate" when the aggregate
        //    is the employee, and it is the trade the owner accepted: more honest 412s in exchange
        //    for no read/write token that can ever disagree. A client holding a pre-S141 profile
        //    ETag sees one stale 412 at rollout.
        if (expectedVersion is not null)
        {
            // (a) Degenerate — nothing covers today, so there is no profile to edit. Pre-S141 this
            //     read "no OPEN row", which was the same statement while a future row could not
            //     exist; restated against the day the client is looking at, because under scheduling
            //     an open row can be one that has not started. ActualVersion = null distinguishes
            //     this branch; the endpoint maps it to a 404 (the S31 UpsertAsync shim that used to
            //     do that translation was removed in S142 / TASK-14208 as a caller-less dead path).
            if (coveringToday is null)
            {
                throw new OptimisticConcurrencyException(
                    $"No employee profile covers today for employee_id='{req.EmployeeId}', " +
                    $"but caller sent If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            // (b) The client token.
            if (usersVersionBefore != expectedVersion.Value)
            {
                throw new OptimisticConcurrencyException(
                    $"Employee record version is {usersVersionBefore}, but caller sent " +
                    $"If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                    expectedVersion: expectedVersion,
                    actualVersion: usersVersionBefore);
            }
        }

        // 3. Route on the locked snapshot (pure). The anchor is matched back to its locked row
        //    by start date — a key under idx_employee_profiles_history.
        //    S141: the router no longer has a future-dating branch, so there is no post-route
        //    re-check here either — the second of B3's two sites in this file.
        var decision = TemporalWriteRouter.Decide(
            timeline.Select(r => new TemporalInterval(r.EffectiveFrom, r.EffectiveTo)),
            req.EffectiveFrom, today);
        var anchor = decision.Anchor is { } anchorInterval
            ? timeline.Single(r => r.EffectiveFrom == anchorInterval.From)
            : null;

        // The fourth field: request value, else the value that ACTUALLY HELD at this date.
        //
        // S138 Step-5a (Reviewer WARNING, absorbed): "the value that held" is the covering row
        // when there is one — but cases E (before the first row) and G (inside a gap) have NO
        // covering row, and the INSERT's SQL fallback reads users.employment_category, which
        // since S138 means "the category as of TODAY". Filling a 2024 gap without naming a
        // category would therefore stamp today's category onto a 2024 row: exactly the
        // substitute-live-for-dated MISLABEL that TASK-13804 retired the read-side COALESCE to
        // prevent ("substituting the live value would MISLABEL history instead of rescuing it").
        // So fall back to the temporally PRECEDING row — the value in force immediately before
        // this date — and only then to the SQL's live fallback, which is reachable just for a
        // genuinely EMPTY timeline, where there is no history to mislabel.
        var preceding = anchor ?? timeline
            .Where(r => r.EffectiveFrom <= req.EffectiveFrom)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();
        var category = req.EmploymentCategory ?? preceding?.EmploymentCategory;

        // Case E has rows but none at or before the date, so nothing records what held then.
        // Guessing is the failure mode above; refuse (date-free) and let the caller state it.
        if (category is null && timeline.Count > 0)
        {
            throw new TemporalWriteRejectedException(
                TemporalWriteRejection.NoRecordedEmploymentCategory, "employee profile");
        }

        // 4. Same-values no-op — inside the lock, after If-Match, against the COVERING row.
        //
        // S138 Step-5a (Codex WARNING, absorbed): the no-op MUST NOT swallow a zero-width
        // reopen. After a same-day create + soft-delete the anchor is a `[from, from)` row
        // that covers NO dates; recreating at that date with UNCHANGED values still has real
        // work to do — re-extend the row so the profile exists again (the router's
        // ReopensZeroWidthAnchor decision, ADR-020 D2 Case C). Short-circuiting on value
        // equality alone would report "nothing to do" and leave the employee with no visible
        // profile at all. Equality is about VALUES; this branch is about COVERAGE.
        if (anchor is not null && !decision.ReopensZeroWidthAnchor && IsSameValues(req, anchor))
        {
            // S141 — the token reported on a no-op is the UNCHANGED `users.version`, so the caller's
            // ETag stays valid. `UsersVersionBefore`/`After` stay null: nothing was written, so the
            // endpoint owes no users_audit row (UsersCacheWritten is false).
            return new SaveEmployeeProfileResult(
                anchor.ProfileId, usersVersionBefore, SaveEmployeeProfileOutcome.NoOp)
            {
                Kind = TemporalWriteKind.NoOp,
                IsNoOp = true,
                NewEffectiveFrom = anchor.EffectiveFrom,
                NewEffectiveTo = anchor.EffectiveTo,
                ProducedRowVersion = anchor.Version,
                Covering = anchor,
                TimelineVersionBefore = usersVersionBefore,
            };
        }

        // 5. Execute the case. Every INSERT path shares the unique-violation backstop.
        SaveEmployeeProfileResult result;
        try
        {
            result = decision.Case switch
            {
                TemporalWriteCase.Create or TemporalWriteCase.InsertTrailing
                    => await ExecuteOpenInsertAsync(conn, tx, req, decision, timeline, category, ct),
                TemporalWriteCase.UpdateInPlace
                    => await ExecuteUpdateInPlaceAsync(conn, tx, req, decision, anchor!, live, category, ct),
                TemporalWriteCase.SplitCovering
                    => await ExecuteSplitAsync(conn, tx, req, decision, anchor!, live, category, ct),
                TemporalWriteCase.InsertBeforeFirst or TemporalWriteCase.InsertInGap
                    => await ExecuteGapInsertAsync(conn, tx, req, decision, live, category, ct),
                _ => throw new InvalidOperationException($"Unhandled TemporalWriteCase '{decision.Case}'."),
            };
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new ConcurrentSeedConflictException("employee_profiles", req.EmployeeId);
        }

        // 6. Cache + TOKEN.
        //    • users.employment_category follows the row covering TODAY, never the request — so a
        //      purely historical correction leaves the cached category untouched by construction.
        //    • users.version is bumped UNCONDITIONALLY (S141 / B5). This is the half that had to
        //      change: a token only detects "someone else changed this" if it MOVES on every change.
        //      Pre-S141 the users row was written only when the category moved, which was fine while
        //      the client's token was the profile row's own version (that one did move on every
        //      write). Now that the client holds users.version, leaving it still after a fraction or
        //      position edit would let a second admin overwrite the first with a token they read
        //      BEFORE that edit — a silent lost update, which is precisely the failure the token
        //      exists to prevent. The sibling agreement-code writer already bumps unconditionally
        //      for the same reason.
        //    • CONSEQUENCE, stated because it changes an endpoint's behaviour without changing its
        //      code: `UsersCacheWritten` is now true on EVERY real write, so the profile PUT emits a
        //      users_audit row every time — with previous_data == new_data whenever the category did
        //      not move. That is correct under ADR-019 D8 / ADR-018 D7 (the users row WAS written;
        //      its version transition owes an audit row) and it matches the agreement side, but it
        //      is a real increase in users_audit volume and a reviewer should see it named here.
        var cache = await RefreshEmploymentCategoryCacheAndBumpTokenAsync(
            conn, tx, req.EmployeeId, today, usersVersionBefore, usersCategoryBefore, ct);
        return result with
        {
            // The aggregate token AFTER the write — what the endpoint stamps as the ETag and records
            // as audit version_after (S141 B5: that token is users.version).
            Version = cache.VersionAfter,
            TimelineVersionBefore = usersVersionBefore,
            UsersVersionBefore = cache.VersionBefore,
            UsersVersionAfter = cache.VersionAfter,
            PreviousEmploymentCategoryCache = cache.PreviousValue,
            NewEmploymentCategoryCache = cache.NewValue,
        };
    }

    // S142 / TASK-14208 (owner ruling OQ-4): the S31 `UpsertAsync` shim was DELETED here, not
    // migrated. It dated its write at `DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime)`
    // — the UTC calendar day used as a BUSINESS date, which is exactly the defect S142 removes (a
    // Danish HR user working after midnight is a day ahead of UTC in summer, so the shim would have
    // dated the change YESTERDAY). Nothing called it: the S31 admin PUT handler was cut over to
    // `SupersedeAndCreateAsync` in TASK-3308, and its payload type `EmployeeProfileUpsertRequest`
    // had zero references outside this file. Deleting is the ruling for a dead path carrying a
    // defect shape — carefully migrating a method nobody calls only preserves the shape for a
    // future caller to copy. The live write path is `SupersedeAndCreateAsync`, whose
    // `EffectiveFrom` is supplied BY THE CALLER (the endpoint reads the clock, per refinement
    // Assumption #14), so the repository has no clock dependency for the date at all.

    /// <summary>
    /// S33 / TASK-3303 — soft-delete the employee's profile by stamping
    /// <c>effective_to = @today</c> (the COPENHAGEN business day from the injected
    /// <see cref="TimeProvider"/> since S142 / TASK-14205, bound as a parameter — S139 /
    /// TASK-13907 replaced the former DB-side <c>NOW()::date</c>, which took its calendar from the
    /// database container's own time zone)
    /// under end-exclusive <c>[from, to)</c> semantics
    /// (ADR-018 D9). After this call no row covers today, so the profile is
    /// invisible to <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/>, but every row
    /// remains in the history table for replay determinism (ADR-016 D10).
    ///
    /// <para>
    /// <b>S141 / TASK-14102 — this is now a two-line shim over
    /// <see cref="SoftDeleteTimelineAsync"/>, which is where the contract lives.</b> The 2-tuple
    /// return is preserved so the existing DELETE endpoint compiles and behaves unchanged; callers
    /// that need to AUDIT what the delete retired (owner ruling OQ-5 (a) makes that mandatory) must
    /// call <see cref="SoftDeleteTimelineAsync"/> and read
    /// <see cref="EmployeeProfileSoftDeleteResult.RetiredScheduledRows"/>. Read that method's doc for
    /// what changed and why — in one sentence: the row it closes is the row covering TODAY, not "the
    /// row with no end date", and any change already scheduled is retired with it.
    /// </para>
    ///
    /// <para>
    /// <b>Predecessor <c>version</c> column is UNCHANGED (ADR-023 D8).</b> Soft-delete is a
    /// row-state-change, not a field-mutation: the row "disappears" from live reads via the
    /// partial-unique-index predicate, so bumping <c>version</c> would be redundant. The audit
    /// row emitted by the endpoint (TASK-3308) accordingly records
    /// <c>version_before = version_after = predecessor.version</c> per ADR-019 D8 for DELETE
    /// actions. This is the deliberate semantic divergence from sibling ADR-019 D8 endpoints
    /// (<c>agreement_configs</c>, <c>wage_type_mappings</c>, <c>entitlement_configs</c>) which
    /// all bump <c>version + 1</c> on soft-delete.
    /// </para>
    ///
    /// <para>
    /// <b>404-vs-412 retry semantic divergence.</b> Because no version moves, an admin retry with
    /// stale <c>If-Match: "@expectedVersion"</c> after a
    /// successful soft-delete will hit <b>404 Not Found</b> (no row covers today any more),
    /// <b>NOT 412 Precondition Failed</b>.
    /// This is intentional — soft-delete is idempotent-by-row-disappearance rather than
    /// idempotent-by-version-bump. Sibling ADR-019 D8 endpoints map stale-after-delete to 412
    /// because they bump version + leave the row visible to history-comparing queries;
    /// employee_profiles DELETE chooses row-disappearance per ADR-023 D8. The D-test
    /// <c>SoftDelete_StaleIfMatchAfterSoftDelete_Returns404NotConflict412</c> in TASK-3312 locks
    /// this contract. S141 preserves it by ORDERING the two checks — coverage of today first, the
    /// token second — rather than by the SQL shape that used to imply it.
    /// </para>
    ///
    /// <para>
    /// <b>Atomic-outbox contract (ADR-018 D5).</b> Caller (TASK-3308 endpoint) owns the
    /// transaction; this method only writes to <c>employee_profiles</c>. The endpoint emits
    /// the audit row (with <c>version_before = version_after = predecessor.version</c>) +
    /// <c>EmployeeProfileSoftDeleted</c> outbox event in the same tx after this returns. S141 adds a
    /// second obligation the endpoint owes: a scheduled row that is retired by this call must be
    /// audited too (owner ruling OQ-5 (a)) — a row that was audited into existence must not vanish
    /// unrecorded. This shim cannot report it; <see cref="SoftDeleteTimelineAsync"/> can.
    /// </para>
    /// </summary>
    /// <param name="conn">Caller-owned connection (ADR-018 D5 atomic-outbox contract).</param>
    /// <param name="tx">Caller-owned transaction; this method does not commit or roll back.</param>
    /// <param name="employeeId">Natural key — the <c>employee_id</c> of the profile to soft-delete.</param>
    /// <param name="expectedVersion">S141: the AGGREGATE token (<c>users.version</c>) the caller
    /// asserts — the value the profile GET handed out as its ETag. Pre-S141 this was the live profile
    /// row's own <c>version</c>; see <see cref="SoftDeleteTimelineAsync"/> for why it moved.</param>
    /// <param name="closeDate">S139 / TASK-13907 — the date to stamp into <c>effective_to</c>,
    /// which the caller computes ONCE for the whole request so the row and the
    /// <c>EmployeeProfileSoftDeleted</c> event it describes carry the same date by construction
    /// (ADR-023 D8's shape). OPTIONAL and trailing: when omitted, the repository reads its own
    /// injected <see cref="TimeProvider"/> for the Copenhagen business day — correct for any caller that needs
    /// only one date, and it keeps existing callers and direct test constructions compiling.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>(profile_id, version)</c> of the row that covered today and was closed, where
    /// <c>version</c> is <b>unchanged</b> from that row's value (per ADR-023 D8). The endpoint
    /// records this on the audit row as <c>version_before = version_after = version</c>.
    /// </returns>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when a row covers today for <paramref name="employeeId"/> but <c>users.version</c>
    /// differs from <paramref name="expectedVersion"/>. Endpoint maps to 412 per ADR-019 D2.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no row covers today for
    /// <paramref name="employeeId"/>. Endpoint maps to 404 Not Found. This is also the branch
    /// hit by an admin retry with stale <c>If-Match</c> after a successful soft-delete — see
    /// 404-vs-412 retry semantic divergence above.
    /// </exception>
    public async Task<(Guid ProfileId, long Version)> SoftDeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, long expectedVersion,
        DateOnly? closeDate = null,
        CancellationToken ct = default)
    {
        var result = await SoftDeleteTimelineAsync(
            conn, tx, employeeId, expectedVersion, closeDate, ct);
        return (result.ProfileId, result.Version);
    }

    /// <summary>
    /// S141 / TASK-14102 (refinement B4, owner ruling OQ-5 (a)) — the full soft-delete: retire the
    /// row covering TODAY **and** every row already SCHEDULED after it, in one transaction, and
    /// report what was retired so the caller can audit it.
    ///
    /// <para>
    /// <b>The defect this fixes, in plain language.</b> Deleting a profile used to mean "stamp an end
    /// date on the row with no end date". Once HR can schedule a change ahead, the row with no end
    /// date is the FUTURE one — so the delete stamped today's date onto a row that starts in
    /// November, producing the interval <c>[November, today)</c>: backwards, covering nothing. The
    /// database does not stop it (<c>employee_profiles</c> has no CHECK relating the two dates), the
    /// endpoint reported success, and the audit trail recorded a deletion — while the row that
    /// actually described the employee survived untouched. The person was not deleted, and the
    /// system said they were. Two of its own read families then disagreed with each other: as-of-today
    /// reads still returned the profile, open-row reads returned nothing.
    /// </para>
    ///
    /// <para>
    /// <b>What it does now (owner ruling OQ-5 (a) — "delete both").</b> Both halves go in one
    /// transaction: the row covering today is closed at <paramref name="closeDate"/> as before, and
    /// every row starting after today is retired by the existing <b>zero-width close</b>
    /// (<c>effective_to := effective_from</c>) rather than a hard <c>DELETE</c>. That idiom is
    /// deliberate: no timeline table in this system has ever hard-deleted a row, a zero-width row is
    /// a shape the router already understands and can re-extend (its B' reopen branch), and the row
    /// stays on the timeline to be explained rather than vanishing from it. The owner ruled this
    /// knowing its cost — a colleague's scheduled decision disappears as a side effect of someone
    /// else's delete — and the accepted mitigations are that the disappearance is AUDITED (the
    /// caller's job, from <see cref="EmployeeProfileSoftDeleteResult.RetiredScheduledRows"/>) and
    /// VISIBLE before HR confirms (B0's job, from
    /// <see cref="GetByEmployeeIdWithScheduledAsync"/>).
    /// </para>
    ///
    /// <para>
    /// <b>"Every row after today", not "the scheduled row".</b> The ruling was written for one
    /// scheduled change, which is what the product will usually produce. Nothing prevents two, and
    /// retiring only the first would leave the second standing with nothing covering today — exactly
    /// the coverage hole this method exists to close. So the plural is the implementation and the
    /// singular is the common case.
    /// </para>
    ///
    /// <para>
    /// <b>Concurrency (S141 B5).</b> <paramref name="expectedVersion"/> is matched against
    /// <c>users.version</c>, the aggregate token — the same one the GET hands out and the PUT checks.
    /// Listing only the GET and the PUT as the sites that had to move would have shipped a fix that
    /// left DELETE broken: it was matching the OPEN row's version, so after one scheduled change the
    /// DELETE would have 412'd forever for the same reason the PUT would have. The token is NOT
    /// bumped here: ADR-023 D8 keeps soft-delete a row-state change rather than a field mutation, and
    /// the 404-not-412 retry contract below is preserved by the rows disappearing, not by a version
    /// moving.
    /// </para>
    ///
    /// <para>
    /// <b>Why this now takes the timeline lock</b> where the S33 version was deliberately a single
    /// statement: it has to identify several rows and write several rows, so the version predicate in
    /// a WHERE clause can no longer be the whole concurrency story. Lock order is the same as the
    /// writer's (<c>employee_profiles</c> then <c>users</c>), so no new deadlock edge appears.
    /// </para>
    /// </summary>
    /// <exception cref="KeyNotFoundException">No row covers today (pre-S141: no live row) — 404.
    /// Also the branch an admin retry hits after a successful delete, per the 404-vs-412 divergence
    /// documented on <see cref="SoftDeleteAsync"/>.</exception>
    /// <exception cref="OptimisticConcurrencyException">A row covers today but
    /// <c>users.version</c> differs from <paramref name="expectedVersion"/> — 412.</exception>
    public async Task<EmployeeProfileSoftDeleteResult> SoftDeleteTimelineAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, long expectedVersion,
        DateOnly? closeDate = null,
        CancellationToken ct = default)
    {
        // S139 / TASK-13907 Step-5a W1 — ONE instant per request. The caller computes the
        // request's "today" ONCE and hands it in as `closeDate`, so the row's `effective_to` and
        // the `EmployeeProfileSoftDeleted` event's `EffectiveTo` are THE SAME VALUE by
        // construction, not by two reads that happen to agree. Two separate reads of the same
        // provider are not the same instant: a hair before the Copenhagen midnight the first can land
        // on the 7th and the second on the 8th, and the row would then disagree with the event that describes
        // it — an auditability defect, not a rounding nuisance. This is the same "compute once"
        // rule S137 wrote for the create POST (AdminEndpoints.cs, the `effectiveFrom` comment).
        // The parameter is OPTIONAL and trailing so existing direct constructions and callers
        // keep compiling; when omitted the repository falls back to its own seam read, which is
        // correct for any caller that needs only one date.
        var today = closeDate ?? Today();

        // 1. Lock the whole timeline, then the users row — the writer's order, unchanged.
        var timeline = await LockTimelineAsync(conn, tx, employeeId, ct);
        var covering = timeline.FirstOrDefault(
            r => r.EffectiveFrom <= today && (r.EffectiveTo is null || r.EffectiveTo.Value > today));

        // 2. Nothing covers today → 404, BEFORE any version comparison. Order matters and is the
        //    pre-S141 order: a stale If-Match presented after a successful delete must read as "gone"
        //    (404), not "changed" (412), because soft-delete is idempotent by row-disappearance
        //    rather than by version bump (ADR-023 D8). Pre-S141 the same sentence said "no LIVE row";
        //    under scheduling an open row can be one that has not started, so the test is coverage of
        //    today, which is what "is this profile in force?" always meant.
        if (covering is null)
        {
            throw new KeyNotFoundException(
                $"Employee profile not found for employee_id='{employeeId}'.");
        }

        var (usersVersion, _) = await LockUsersRowAsync(conn, tx, employeeId, ct);
        if (usersVersion != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Employee record version is {usersVersion}, but caller sent " +
                $"If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: usersVersion);
        }

        // 3. Close the row covering today at `today` (end-exclusive, ADR-018 D9 — it no longer covers
        //    today). Its `version` is NOT bumped: ADR-023 D8 treats soft-delete as a row-state change,
        //    and the audit row the endpoint writes records version_before == version_after.
        //    `updated_at = NOW()` stays a DB timestamp on purpose — it is an INSTANT (row-maintenance
        //    metadata), not a business date, and S142 moves business dates only. (The close-stamp
        //    itself has been APP-side since S139 / TASK-13907, so one HR action depends on one clock;
        //    S142 / TASK-14205 makes that clock's calendar the Copenhagen business day.)
        await using (var closeCmd = new NpgsqlCommand(
            """
            UPDATE employee_profiles
               SET effective_to = @today, updated_at = NOW()
             WHERE profile_id = @profileId
            """, conn, tx))
        {
            closeCmd.Parameters.AddWithValue("today", today);
            closeCmd.Parameters.AddWithValue("profileId", covering.ProfileId);
            await closeCmd.ExecuteNonQueryAsync(ct);
        }

        // 4. Retire every SCHEDULED row by the zero-width close. A row already zero-width was retired
        //    before and is skipped, so a repeated delete cannot manufacture phantom "retirements" for
        //    the audit trail to explain.
        var retired = new List<RetiredScheduledProfileRow>();
        foreach (var scheduled in timeline
                     .Where(r => r.EffectiveFrom > today && r.EffectiveTo != r.EffectiveFrom)
                     .OrderBy(r => r.EffectiveFrom))
        {
            await using var retireCmd = new NpgsqlCommand(
                """
                UPDATE employee_profiles
                   SET effective_to = effective_from, updated_at = NOW()
                 WHERE profile_id = @profileId
                """, conn, tx);
            retireCmd.Parameters.AddWithValue("profileId", scheduled.ProfileId);
            await retireCmd.ExecuteNonQueryAsync(ct);
            retired.Add(new RetiredScheduledProfileRow(
                ProfileId: scheduled.ProfileId,
                EffectiveFrom: scheduled.EffectiveFrom,
                PreviousEffectiveTo: scheduled.EffectiveTo,
                PartTimeFraction: scheduled.PartTimeFraction,
                Position: scheduled.Position,
                EmploymentCategory: scheduled.EmploymentCategory,
                Version: scheduled.Version));
        }

        return new EmployeeProfileSoftDeleteResult(
            ProfileId: covering.ProfileId,
            Version: covering.Version,
            UsersVersion: usersVersion,
            EffectiveTo: today,
            Covering: covering,
            RetiredScheduledRows: retired);
    }

    // ------------------------------------------------------------------
    // Private helpers — the S138 timeline lock, the four case executors, the row primitives
    // (insert / update-in-place / close / bump-token) and the users-cache refresh. The S29 WTM
    // AcquireLockAsync / UpdateInPlaceAsync / CloseRowAsync / InsertSupersedingRowAsync triad,
    // generalized from "the open row" to "the whole timeline".
    // ------------------------------------------------------------------

    /// <summary>
    /// Locks EVERY row of the employee's timeline via <c>SELECT … FOR UPDATE</c>, returning them
    /// as pre-images. Order matters and is deliberate: the OPEN row first, then history ascending.
    /// PostgreSQL locks rows in output order, so every writer's first lock is the open row (the
    /// pre-S138 serialization point, re-entrant with endpoint pre-locks on it) and the rest follow
    /// in one shared order — two concurrent writers cannot deadlock on this statement, and two
    /// gap-inserters serialize on the history rows instead of racing to overlapping inserts.
    /// Returns an empty list when the employee has no rows at all (case A).
    /// </summary>
    private static async Task<IReadOnlyList<EmployeeProfileRowPreImage>> LockTimelineAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, CancellationToken ct)
    {
        await using var lockCmd = new NpgsqlCommand(
            """
            SELECT profile_id, part_time_fraction, position, employment_category,
                   effective_from, effective_to, version
            FROM employee_profiles
            WHERE employee_id = @employeeId
            ORDER BY (effective_to IS NULL) DESC, effective_from
            FOR UPDATE
            """, conn, tx);
        lockCmd.Parameters.AddWithValue("employeeId", employeeId);
        var rows = new List<EmployeeProfileRowPreImage>();
        await using var reader = await lockCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new EmployeeProfileRowPreImage(
                ProfileId: reader.GetGuid(0),
                PartTimeFraction: reader.GetDecimal(1),
                Position: reader.IsDBNull(2) ? null : reader.GetString(2),
                EmploymentCategory: reader.IsDBNull(3) ? null : reader.GetString(3),
                EffectiveFrom: reader.GetFieldValue<DateOnly>(4),
                EffectiveTo: reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5),
                Version: reader.GetInt64(6)));
        }
        return rows;
    }

    /// <summary>
    /// The same-values test behind the S23-shape no-op: the request equals the covering row on
    /// every field it carries. An omitted category (<c>null</c>) means "unchanged" by definition.
    /// </summary>
    private static bool IsSameValues(EmployeeProfileSupersedeRequest req, EmployeeProfileRowPreImage covering)
        => covering.PartTimeFraction == req.PartTimeFraction
           && string.Equals(covering.Position, req.Position, StringComparison.Ordinal)
           && (req.EmploymentCategory is null
               || string.Equals(covering.EmploymentCategory, req.EmploymentCategory, StringComparison.Ordinal));

    /// <summary>
    /// Cases A and T — INSERT the (only) open row. Version = max over the employee's rows + 1:
    /// that is 1 on an empty timeline (the S33 Case-A baseline) and strictly above every retired
    /// row's version after a soft-delete, so an ETag captured before the delete can never match
    /// the re-created row (the S33 Step-7a P1 monotonicity rationale, extended to re-creates).
    /// </summary>
    private static async Task<SaveEmployeeProfileResult> ExecuteOpenInsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, EmployeeProfileSupersedeRequest req,
        TemporalWriteDecision decision, IReadOnlyList<EmployeeProfileRowPreImage> timeline,
        string? category, CancellationToken ct)
    {
        var nextVersion = timeline.Count == 0 ? 1L : timeline.Max(r => r.Version) + 1;
        var (newId, newVersion) = await InsertRowAsync(
            conn, tx, req, decision.NewEffectiveFrom, decision.NewEffectiveTo, nextVersion, category, ct);
        return new SaveEmployeeProfileResult(newId, newVersion, SaveEmployeeProfileOutcome.Created)
        {
            Kind = TemporalWriteRouter.KindOf(decision),
            NewEffectiveFrom = decision.NewEffectiveFrom,
            NewEffectiveTo = decision.NewEffectiveTo,
            ProducedRowVersion = newVersion,
        };
    }

    /// <summary>
    /// Case B' — UPDATE the row that starts on the requested date. The row's OWN version moves
    /// only when it is (or, for a reopened zero-width row, becomes) the open row — that version
    /// IS the client token. A history row keeps its version and the token moves on the open row.
    /// </summary>
    private static async Task<SaveEmployeeProfileResult> ExecuteUpdateInPlaceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, EmployeeProfileSupersedeRequest req,
        TemporalWriteDecision decision, EmployeeProfileRowPreImage anchor, EmployeeProfileRowPreImage? live,
        string? category, CancellationToken ct)
    {
        var bumpOwnVersion = anchor.EffectiveTo is null || decision.ProducesOpenRow;
        var (id, ownVersion) = await UpdateRowAsync(
            conn, tx, req, anchor.ProfileId, decision.NewEffectiveTo, bumpOwnVersion, category, ct);
        var token = bumpOwnVersion || live is null
            ? ownVersion
            : await BumpTokenAsync(conn, tx, live.ProfileId, ct);
        return new SaveEmployeeProfileResult(id, token, SaveEmployeeProfileOutcome.Updated)
        {
            Kind = TemporalWriteKind.Updated,
            NewEffectiveFrom = decision.NewEffectiveFrom,
            NewEffectiveTo = decision.NewEffectiveTo,
            ProducedRowVersion = ownVersion,
            Covering = anchor,
            TimelineVersionBefore = live?.Version,
        };
    }

    /// <summary>
    /// Case C' — close the covering row at the requested date (its version untouched: a close is
    /// lifecycle, not a content edit) and INSERT <c>[from, covering.oldTo)</c>. When the covering
    /// row is the OPEN row the successor inherits <c>predecessor.Version + 1</c> (S33 Step-7a P1 —
    /// the token keeps climbing across the supersession); when it is a HISTORY row the inserted row
    /// is a fresh history row at version 1 and the token moves on the open row.
    /// </summary>
    private static async Task<SaveEmployeeProfileResult> ExecuteSplitAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, EmployeeProfileSupersedeRequest req,
        TemporalWriteDecision decision, EmployeeProfileRowPreImage anchor, EmployeeProfileRowPreImage? live,
        string? category, CancellationToken ct)
    {
        await ClosePredecessorAsync(conn, tx, anchor.ProfileId, decision.NewEffectiveFrom, ct);
        var coveringIsOpen = anchor.EffectiveTo is null;
        var newRowVersion = coveringIsOpen ? anchor.Version + 1 : 1L;
        var (newId, producedVersion) = await InsertRowAsync(
            conn, tx, req, decision.NewEffectiveFrom, decision.NewEffectiveTo, newRowVersion, category, ct);
        var token = coveringIsOpen || live is null
            ? producedVersion
            : await BumpTokenAsync(conn, tx, live.ProfileId, ct);
        return new SaveEmployeeProfileResult(newId, token, SaveEmployeeProfileOutcome.Superseded)
        {
            Kind = TemporalWriteRouter.KindOf(decision),
            NewEffectiveFrom = decision.NewEffectiveFrom,
            NewEffectiveTo = decision.NewEffectiveTo,
            ProducedRowVersion = producedVersion,
            Covering = anchor,
            TimelineVersionBefore = live?.Version,
        };
    }

    /// <summary>
    /// Cases E and G — INSERT a history row into a gap (nothing is closed). Version 1 (no
    /// predecessor); the token moves on the open row when one exists.
    /// </summary>
    private static async Task<SaveEmployeeProfileResult> ExecuteGapInsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, EmployeeProfileSupersedeRequest req,
        TemporalWriteDecision decision, EmployeeProfileRowPreImage? live,
        string? category, CancellationToken ct)
    {
        var (newId, producedVersion) = await InsertRowAsync(
            conn, tx, req, decision.NewEffectiveFrom, decision.NewEffectiveTo, 1L, category, ct);
        var token = live is null
            ? producedVersion
            : await BumpTokenAsync(conn, tx, live.ProfileId, ct);
        return new SaveEmployeeProfileResult(newId, token, SaveEmployeeProfileOutcome.Inserted)
        {
            Kind = TemporalWriteRouter.KindOf(decision),
            NewEffectiveFrom = decision.NewEffectiveFrom,
            NewEffectiveTo = decision.NewEffectiveTo,
            ProducedRowVersion = producedVersion,
            TimelineVersionBefore = live?.Version,
        };
    }

    /// <summary>
    /// INSERT one row with the caller-supplied interval and version. <c>profile_id</c> is generated
    /// client-side (S29 WTM precedent) so the endpoint can put it in the outbox event body.
    /// <c>employment_category</c> = the resolved value, else (legacy NULL cell) the live users
    /// value via the scalar subselect (same conn + tx, so an uncommitted users row is visible).
    /// The two unique indexes (open-row partial + history) are the collision backstop; the caller
    /// translates 23505.
    /// </summary>
    private static async Task<(Guid ProfileId, long Version)> InsertRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, EmployeeProfileSupersedeRequest req,
        DateOnly effectiveFrom, DateOnly? effectiveTo, long version, string? category, CancellationToken ct)
    {
        var newProfileId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version, employment_category)
            VALUES (
                @profileId, @employeeId, @partTimeFraction, @position,
                @effectiveFrom, @effectiveTo, @version,
                COALESCE(@employmentCategory,
                         (SELECT u.employment_category FROM users u WHERE u.user_id = @employeeId)))
            RETURNING profile_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("profileId", newProfileId);
        cmd.Parameters.AddWithValue("employeeId", req.EmployeeId);
        cmd.Parameters.AddWithValue("partTimeFraction", req.PartTimeFraction);
        cmd.Parameters.AddWithValue("position", (object?)req.Position ?? DBNull.Value);
        cmd.Parameters.AddWithValue("effectiveFrom", effectiveFrom);
        cmd.Parameters.Add(new NpgsqlParameter("effectiveTo", NpgsqlTypes.NpgsqlDbType.Date)
        {
            Value = effectiveTo is { } to ? to : DBNull.Value,
        });
        cmd.Parameters.AddWithValue("version", version);
        cmd.Parameters.Add(new NpgsqlParameter("employmentCategory", NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = (object?)category ?? DBNull.Value,
        });
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — INSERT ... RETURNING always yields one row on success.
            throw new InvalidOperationException(
                $"InsertRowAsync produced no row for employee_id='{req.EmployeeId}' " +
                $"at effective_from='{effectiveFrom:yyyy-MM-dd}'.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// Case B' — UPDATE the (locked) row that starts on the requested date: refresh the three
    /// fields, set <c>effective_to</c> to the decided end (unchanged for a normal in-place edit;
    /// re-extended for a zero-width row — the ADR-020 D2 Case C reopen), bump its version only
    /// when <paramref name="bumpVersion"/> (the open row — the client token). <c>profile_id</c>
    /// and <c>effective_from</c> are immutable across in-place edits.
    /// </summary>
    private static async Task<(Guid ProfileId, long Version)> UpdateRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, EmployeeProfileSupersedeRequest req,
        Guid profileId, DateOnly? effectiveTo, bool bumpVersion, string? category, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE employee_profiles SET
                part_time_fraction = @partTimeFraction,
                position = @position,
                employment_category = COALESCE(@employmentCategory, employment_category,
                    (SELECT u.employment_category FROM users u WHERE u.user_id = employee_profiles.employee_id)),
                effective_to = @effectiveTo,
                version = version + @versionBump,
                updated_at = NOW()
            WHERE profile_id = @profileId
            RETURNING profile_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("profileId", profileId);
        cmd.Parameters.AddWithValue("partTimeFraction", req.PartTimeFraction);
        cmd.Parameters.AddWithValue("position", (object?)req.Position ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("employmentCategory", NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = (object?)category ?? DBNull.Value,
        });
        cmd.Parameters.Add(new NpgsqlParameter("effectiveTo", NpgsqlTypes.NpgsqlDbType.Date)
        {
            Value = effectiveTo is { } to ? to : DBNull.Value,
        });
        cmd.Parameters.AddWithValue("versionBump", bumpVersion ? 1L : 0L);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"UpdateRowAsync produced no row for profile_id='{profileId}'; " +
                "FOR UPDATE invariant violated.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// Bumps the OPEN row's <c>version</c> without touching its fields — how a history-only write
    /// moves the client token (see the one-token-per-aggregate rule on
    /// <see cref="SupersedeAndCreateAsync"/>). Returns the new token.
    /// </summary>
    private static async Task<long> BumpTokenAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid liveProfileId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE employee_profiles
               SET version = version + 1, updated_at = NOW()
             WHERE profile_id = @profileId
            RETURNING version
            """, conn, tx);
        cmd.Parameters.AddWithValue("profileId", liveProfileId);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException(
                $"BumpTokenAsync found no row for profile_id='{liveProfileId}'; FOR UPDATE invariant violated.");
        }
        return (long)result;
    }

    /// <summary>
    /// Case C' — close the covering row by stamping <c>effective_to = closeDate</c> under
    /// end-exclusive semantics (ADR-018 D9 — its history window becomes
    /// <c>[effective_from, closeDate)</c>). The version column is NOT bumped: a close is a
    /// lifecycle event, not a content edit (mirrors S22 ArchiveProfileAsync + S29 WTM
    /// CloseRowAsync). Caller must already hold the timeline lock.
    /// </summary>
    private static async Task ClosePredecessorAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid profileId, DateOnly closeDate, CancellationToken ct)
    {
        await using var closeCmd = new NpgsqlCommand(
            "UPDATE employee_profiles SET effective_to = @closeDate WHERE profile_id = @profileId",
            conn, tx);
        closeCmd.Parameters.AddWithValue("closeDate", closeDate);
        closeCmd.Parameters.AddWithValue("profileId", profileId);
        await closeCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// S141 / TASK-14102 (B5) — lock the employee's <c>users</c> row and read the AGGREGATE
    /// CONCURRENCY TOKEN (<c>users.version</c>), which is what an admin client holds as the profile
    /// ETag under owner ruling OQ-3 (a).
    ///
    /// <para>
    /// <b>Lock ORDER is deliberate and unchanged.</b> Callers take this AFTER the
    /// <c>employee_profiles</c> timeline lock, which is where the users lock already sat (the cache
    /// refresh at the end of the write held it). Acquiring it earlier in the SAME order adds no new
    /// deadlock edge. Note that the admin users PUT locks in the opposite conventional order
    /// (users → child timeline, the S78 rule) — that is safe only because that handler never takes
    /// the profile timeline lock while holding users; if a future caller ever does both, THIS is the
    /// comment that has to be revisited.
    /// </para>
    /// </summary>
    private static async Task<(long Version, string EmploymentCategory)> LockUsersRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT version, employment_category
            FROM users
            WHERE user_id = @employeeId
            FOR UPDATE
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // The employee_id FK guarantees the users row; reaching here is a programming error.
            throw new InvalidOperationException(
                $"users row for user_id='{employeeId}' not found while reading the aggregate concurrency token.");
        }
        return (reader.GetInt64(0), reader.GetString(1));
    }

    /// <summary>
    /// The cache rule AND the token bump (see <see cref="SupersedeAndCreateAsync"/> step 6): re-read
    /// the category of the row covering TODAY after the write, then write
    /// <c>UPDATE users SET employment_category = &lt;that category, when there is one&gt;,
    /// version = version + 1</c>.
    ///
    /// <para>
    /// <b>The version bump is UNCONDITIONAL; the cached VALUE only moves when today's row moved.</b>
    /// Two different rules, deliberately: the token must change on every timeline write or it cannot
    /// detect a concurrent one (S141 B5), while the cache must never be set from the REQUEST or a
    /// historical-only correction would overwrite today's truth with a 2024 value. When no row covers
    /// today at all — reachable after a soft-delete, and newly reachable when a scheduled row exists
    /// with nothing before it (refinement B8) — the COALESCE keeps the existing cached value rather
    /// than nulling a NOT NULL column. Not gated on <c>is_active</c>: a departed employee's cache
    /// must stay correctable.
    /// </para>
    ///
    /// <para>
    /// Shape mirrors <c>UserAgreementCodeRepository.RefreshAgreementCodeCacheAsync</c>, which made
    /// the same two-rules split first. The caller already holds the users row lock
    /// (<see cref="LockUsersRowAsync"/>) and passes the version AND category it observed under it,
    /// so the before-values this returns are the ones actually replaced — no second read, no window.
    /// </para>
    /// </summary>
    private static async Task<(long VersionBefore, long VersionAfter, string PreviousValue, string NewValue)>
        RefreshEmploymentCategoryCacheAndBumpTokenAsync(
            NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, DateOnly today,
            long usersVersionBefore, string usersCategoryBefore, CancellationToken ct)
    {
        string? todayCategory;
        await using (var todayCmd = new NpgsqlCommand(
            """
            SELECT employment_category
            FROM employee_profiles
            WHERE employee_id = @employeeId
              AND effective_from <= @today
              AND (effective_to IS NULL OR effective_to > @today)
            ORDER BY effective_from DESC
            LIMIT 1
            """, conn, tx))
        {
            todayCmd.Parameters.AddWithValue("employeeId", employeeId);
            todayCmd.Parameters.AddWithValue("today", today);
            var scalar = await todayCmd.ExecuteScalarAsync(ct);
            todayCategory = scalar is null || scalar is DBNull ? null : (string)scalar;
        }

        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE users
               SET employment_category = COALESCE(@employmentCategory, employment_category),
                   version = version + 1,
                   updated_at = NOW()
             WHERE user_id = @employeeId
            RETURNING employment_category, version
            """, conn, tx);
        updateCmd.Parameters.AddWithValue("employeeId", employeeId);
        updateCmd.Parameters.Add(new NpgsqlParameter("employmentCategory", NpgsqlTypes.NpgsqlDbType.Text)
        {
            Value = (object?)todayCategory ?? DBNull.Value,
        });
        await using var updated = await updateCmd.ExecuteReaderAsync(ct);
        if (!await updated.ReadAsync(ct))
        {
            throw new InvalidOperationException(
                $"users cache write for user_id='{employeeId}' matched no row; FOR UPDATE invariant violated.");
        }
        return (usersVersionBefore, updated.GetInt64(1), usersCategoryBefore, updated.GetString(0));
    }
}

// ------------------------------------------------------------------
// Request records. S142 / TASK-14208 removed EmployeeProfileUpsertRequest alongside the
// caller-less UpsertAsync shim it was the payload for (owner ruling OQ-4); Create and
// Supersede are what remain.
// ------------------------------------------------------------------

/// <summary>
/// S31 / TASK-3102 — payload for <see cref="EmployeeProfileRepository.CreateAsync"/>.
/// The S31-authoritative fields plus the natural key; <see cref="Position"/> is
/// nullable per the schema definition (TEXT NULL). Kept separate from
/// <see cref="EmployeeProfileSupersedeRequest"/>, which carries the routing/concurrency fields a
/// net-new INSERT has no use for.
///
/// <para>
/// <b>S143 / TASK-14308 (QUAL-177, owner ruling OQ-4) — <see cref="EffectiveFrom"/> is new, and it
/// is REQUIRED on purpose.</b> This record used to say the INSERT "always uses the schema default
/// <c>'0001-01-01'</c>"; the code had stamped TODAY since S33, so the comment had been false for a
/// hundred sprints — which is exactly what happens to a method nothing in production calls. Now that
/// both real creators (the boot backfill seeder and the admin create-person endpoint) route through
/// <see cref="EmployeeProfileRepository.CreateAsync"/>, and they legitimately need DIFFERENT dates,
/// the date is a stated argument with NO default value. A defaulted parameter would let a future
/// caller inherit a date silently, and the difference between the two dates is load-bearing: see
/// <see cref="EmployeeProfileRepository.CreateAsync"/>'s doc for the two defects (the S33 historical
/// -coverage 500 and the S137 midnight-straddle) that each wrong choice reintroduces.
/// </para>
/// </summary>
public sealed record EmployeeProfileCreateRequest(
    string EmployeeId,
    decimal PartTimeFraction,
    string? Position,
    DateOnly EffectiveFrom);

/// <summary>
/// S33 / TASK-3302 — payload for <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/>.
/// Extends <see cref="EmployeeProfileCreateRequest"/>'s field set with the explicit
/// <see cref="EffectiveFrom"/> date that drives the routing (the endpoint reads the clock per
/// refinement Assumption #14 — no clock dependency in the repo for the DATE; seeders + admin-POST
/// + admin-PUT supply it directly). Flat record (the S29 WTM precedent shape).
///
/// <para>
/// <b>S138 / TASK-13801 additions (trailing, defaulted — every 4-argument construction compiles
/// unchanged).</b> <see cref="EmploymentCategory"/> is the fourth editable field (ADR-040 D4):
/// <c>null</c> means "keep the covering row's category" (the pre-S138 behavior).
/// <see cref="EmploymentStartDate"/> is the caller-supplied employment-start floor: when set, a
/// date before it is refused with <see cref="Temporal.TemporalWriteRejection.PrecedesEmploymentStart"/>
/// (date-free). It is caller-supplied, not read from <c>users</c>, because the admin user-create
/// POST legitimately writes the first row at today for a hire whose start date is in the future.
/// </para>
/// </summary>
public sealed record EmployeeProfileSupersedeRequest(
    string EmployeeId,
    decimal PartTimeFraction,
    string? Position,
    DateOnly EffectiveFrom,
    string? EmploymentCategory = null,
    DateOnly? EmploymentStartDate = null);

/// <summary>
/// S141 / TASK-14102 (refinement B4, owner ruling OQ-5 (a)) — one scheduled profile row that a
/// soft-delete retired, captured as it stood BEFORE the retirement.
///
/// <para>
/// <b>Why this is returned rather than silently discarded.</b> The scheduled change may have been
/// entered by a different HR person, days earlier, as a deliberate saved decision; deleting the
/// profile now destroys it as a side effect of an unrelated action. The owner accepted that cost on
/// condition that the destruction is recorded as deliberately as the creation was — so the caller
/// owes an audit row (and an event) per retired row, and this record carries everything such a row
/// needs: which row, the interval it was going to occupy, and the values it was going to bring.
/// </para>
///
/// <para>
/// <see cref="PreviousEffectiveTo"/> is the end the row had before retirement (<c>null</c> = it was
/// the open row). After retirement every such row is zero-width <c>[EffectiveFrom, EffectiveFrom)</c>.
/// </para>
/// </summary>
public sealed record RetiredScheduledProfileRow(
    Guid ProfileId,
    DateOnly EffectiveFrom,
    DateOnly? PreviousEffectiveTo,
    decimal PartTimeFraction,
    string? Position,
    string? EmploymentCategory,
    long Version);

/// <summary>
/// S141 / TASK-14102 (refinement B4) — what a soft-delete actually did.
/// </summary>
/// <param name="ProfileId">The row that covered TODAY and was closed — the row the endpoint's audit
/// row and <c>EmployeeProfileSoftDeleted</c> event describe.</param>
/// <param name="Version">That row's own <c>version</c>, UNCHANGED (ADR-023 D8: soft-delete is a
/// row-state change, so the audit records <c>version_before == version_after</c>).</param>
/// <param name="UsersVersion">The aggregate token, also unchanged — the delete validates against it
/// but does not move it, so a stale retry reads as "gone" (404) rather than "changed" (412).</param>
/// <param name="EffectiveTo">The date stamped into the closed row: the ONE "today" of this request,
/// so the row and the event describing it carry the same date by construction, not by two clock
/// reads that happen to agree.</param>
/// <param name="Covering">The closed row's full pre-image — the values that were ACTUALLY in force
/// when HR deleted, which is what the audit's <c>previous_data</c> must record. Under future-dating
/// the open row's values are the wrong answer to that question.</param>
/// <param name="RetiredScheduledRows">Every scheduled row retired alongside it, earliest first.
/// EMPTY in the ordinary case (nothing scheduled), which is why a caller must handle the empty list
/// as the norm rather than as an edge case.</param>
public sealed record EmployeeProfileSoftDeleteResult(
    Guid ProfileId,
    long Version,
    long UsersVersion,
    DateOnly EffectiveTo,
    EmployeeProfileRowPreImage Covering,
    IReadOnlyList<RetiredScheduledProfileRow> RetiredScheduledRows);

/// <summary>
/// S141 / TASK-14102 (refinement B0) — a change that is already SCHEDULED to take effect after today:
/// the next profile row starting strictly after today, with the interval it will occupy and the
/// values it will bring.
///
/// <para>
/// <b>Plain language.</b> "From 1 November this person is 0.6 and their title is Department Head."
/// Carried alongside today's values on every read so a screen can say so, rather than showing one
/// number with no indication that another is coming — which is what made the S141 edit-drawer defect
/// possible in the first place.
/// </para>
///
/// <para>
/// <see cref="EffectiveTo"/> is <c>null</c> when the scheduled row is the open one (the ordinary
/// case: a change scheduled to run indefinitely). A non-null value means a FURTHER row follows it, so
/// a caller that wants the whole future must read the timeline, not just this one hop. Zero-width
/// rows are excluded upstream — they are the trace a retired scheduled row leaves, not a change.
/// </para>
/// </summary>
public sealed record ScheduledEmployeeProfileChange(
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal PartTimeFraction,
    string? Position,
    string? EmploymentCategory);

/// <summary>
/// S141 / TASK-14102 — what an as-of-today profile read returns: the profile that holds TODAY, the
/// aggregate concurrency token (<c>users.version</c> — ADR-019 one token per aggregate, owner ruling
/// OQ-3 (a)), and B0's next scheduled change (<c>null</c> when nothing is scheduled).
/// </summary>
/// <param name="Profile">The values in force today.</param>
/// <param name="Version"><c>users.version</c> — what a client holds as an ETag and sends back as
/// <c>If-Match</c>. Deliberately NOT the row's own <c>version</c>: see
/// <see cref="EmployeeProfileRepository.GetByEmployeeIdWithVersionAsync"/>.</param>
/// <param name="Scheduled">The next change dated after today, if any.</param>
public sealed record ProfileAsOfTodayHit(
    EmploymentProfile Profile,
    long Version,
    ScheduledEmployeeProfileChange? Scheduled);

/// <summary>
/// S138 / TASK-13801 — the PRE-IMAGE of one <c>employee_profiles</c> row as it stood under the
/// lock before the write: the fields, the interval and the row's own version. Carried on
/// <see cref="SaveEmployeeProfileResult.Covering"/> so the endpoint sources audit
/// <c>previous_data</c>, the mutation predicate and the Superseded event's predecessor fields from
/// the row that was actually touched — never from the open row (Reviewer discovery 8: for a
/// backdate the two differ, and reading the open row would silently no-op or corrupt the cache).
/// <see cref="EffectiveTo"/> null = it was the open row.
/// </summary>
public sealed record EmployeeProfileRowPreImage(
    Guid ProfileId,
    decimal PartTimeFraction,
    string? Position,
    string? EmploymentCategory,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long Version);

/// <summary>
/// S33 / TASK-3302 — result of <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/>.
/// <see cref="Outcome"/> discriminates the coarse, event-oriented branch (Created / Updated /
/// Superseded, plus S138's Inserted and NoOp) so the endpoint emits the right event type and
/// audit <c>action</c>; the S138 members below carry everything a temporal write additionally
/// needs to narrate. All S138 members are init-only with defaults — the 3-argument construction
/// and every existing reader compile unchanged.
/// </summary>
/// <param name="ProfileId">The <c>profile_id</c> of the row this call produced or edited: a fresh
/// UUID for every INSERT case, the anchor's id for an in-place edit or a no-op.</param>
/// <param name="Version">The per-employee AGGREGATE token AFTER the write — the value the endpoint
/// stamps as ETag and records as audit <c>version_after</c>.
/// <b>S141 / TASK-14102 (B5, owner ruling OQ-3 (a)): that token is now <c>users.version</c></b>, not
/// the open row's <c>version</c> as it was in S138. The change is invisible to the endpoint, which
/// already treated this member as "the token" rather than "a row version" — but it is the reason the
/// GET's ETag and this value still describe the same thing once a scheduled row exists, which a
/// per-row token could not. On a no-op it is the unchanged current <c>users.version</c>. The touched
/// row's own version is <see cref="SaveEmployeeProfileResult.ProducedRowVersion"/>.</param>
/// <param name="Outcome">Which branch the call routed through.</param>
public sealed record SaveEmployeeProfileResult(
    Guid ProfileId,
    long Version,
    SaveEmployeeProfileOutcome Outcome)
{
    /// <summary>S138 — the fine-grained case (A / B' / C'-open / C'-history / E / G / T / no-op).</summary>
    public TemporalWriteKind Kind { get; init; } = DefaultKind(Outcome);

    /// <summary>S138 — true when the request equalled the covering row and nothing was written
    /// (no row, no version bump, no cache write); the endpoint skips audit/events/revaluation/worklist.</summary>
    public bool IsNoOp { get; init; }

    /// <summary>S138 — start of the interval the write produced / edited (the request date; for a
    /// no-op the covering row's start).</summary>
    public DateOnly? NewEffectiveFrom { get; init; }

    /// <summary>S138 — end (exclusive) of that interval; <c>null</c> = the row is open. For C' this
    /// is where the covering row USED to end — the revaluation window and the worklist interval are
    /// <c>[NewEffectiveFrom, NewEffectiveTo)</c>, not <c>[from, ∞)</c> (recon discovery 1).</summary>
    public DateOnly? NewEffectiveTo { get; init; }

    /// <summary>S138 — the produced / edited row's OWN <c>version</c> column (differs from
    /// <see cref="Version"/> only for history-only writes, where the token lives on the open row).</summary>
    public long ProducedRowVersion { get; init; } = Version;

    /// <summary>S138 — the covering row's pre-image (B' / C' / no-op); <c>null</c> for A / E / G / T
    /// where no row covered the date.</summary>
    public EmployeeProfileRowPreImage? Covering { get; init; }

    /// <summary>The aggregate token BEFORE the write (audit <c>version_before</c>) — S141: the
    /// <c>users.version</c> observed under the lock, so it is always set on a real write and on a
    /// no-op. (S138 defined it as the open row's version and left it <c>null</c> when no open row
    /// existed; the aggregate token has no such hole.)</summary>
    public long? TimelineVersionBefore { get; init; }

    /// <summary>S138 — <c>users.version</c> before the cache write; <c>null</c> when the cache was untouched.</summary>
    public long? UsersVersionBefore { get; init; }

    /// <summary>S138 — <c>users.version</c> after the cache write; <c>null</c> when the cache was untouched.</summary>
    public long? UsersVersionAfter { get; init; }

    /// <summary>S138 — the <c>users.employment_category</c> value replaced; <c>null</c> when untouched.</summary>
    public string? PreviousEmploymentCategoryCache { get; init; }

    /// <summary>S138 — the <c>users.employment_category</c> value written (the row covering today's);
    /// <c>null</c> when untouched.</summary>
    public string? NewEmploymentCategoryCache { get; init; }

    /// <summary>S138 — true when this write touched the <c>users</c> row (the endpoint then owes a
    /// <c>users_audit</c> row for the version transition).</summary>
    public bool UsersCacheWritten => UsersVersionAfter is not null;

    private static TemporalWriteKind DefaultKind(SaveEmployeeProfileOutcome outcome) => outcome switch
    {
        SaveEmployeeProfileOutcome.Created => TemporalWriteKind.Created,
        SaveEmployeeProfileOutcome.Updated => TemporalWriteKind.Updated,
        SaveEmployeeProfileOutcome.Superseded => TemporalWriteKind.Superseded,
        SaveEmployeeProfileOutcome.Inserted => TemporalWriteKind.InsertedInGap,
        SaveEmployeeProfileOutcome.NoOp => TemporalWriteKind.NoOp,
        _ => TemporalWriteKind.Created,
    };
}

/// <summary>
/// S33 / TASK-3302 — routing discriminator, read by the endpoints to map each case to its outbox
/// event type and audit <c>action</c>; extended additively in S138 / TASK-13801:
/// <list type="bullet">
///   <item><description><see cref="Created"/> → <c>EmployeeProfileCreated</c> (a new OPEN row with no
///     predecessor closed: case A, and case T's re-create after a trailing gap).</description></item>
///   <item><description><see cref="Updated"/> → <c>EmployeeProfileUpdated</c> (case B': the row starting
///     on the date edited in place — open or history).</description></item>
///   <item><description><see cref="Superseded"/> → <c>EmployeeProfileSuperseded</c> (case C': the covering
///     row closed at the date + a new row <c>[from, oldTo)</c>; <c>NewEffectiveTo</c> tells open from
///     history).</description></item>
///   <item><description><see cref="Inserted"/> (S138) → <c>EmployeeProfileCreated</c> with a closed
///     interval (cases E / G: a history row inserted into a gap; nothing closed — CREATED only).</description></item>
///   <item><description><see cref="NoOp"/> (S138) → nothing emitted (the S23 shape).</description></item>
/// </list>
/// </summary>
public enum SaveEmployeeProfileOutcome
{
    /// <summary>Case A (empty timeline) or T (trailing gap): INSERT produced a brand-new open row.</summary>
    Created,
    /// <summary>Case B': a row started on the requested date; UPDATE-in-place (profile_id and
    /// effective_from unchanged).</summary>
    Updated,
    /// <summary>Case C': the covering row was closed at end-exclusive
    /// <c>effective_to = request.EffectiveFrom</c> (version unchanged) and a new row inserted for
    /// the remainder of its old interval.</summary>
    Superseded,
    /// <summary>S138 — cases E / G: a history row was inserted into a gap; no row was closed.</summary>
    Inserted,
    /// <summary>S138 — the request equalled the covering row; nothing was written.</summary>
    NoOp,
}
