using Npgsql;
using StatsTid.Infrastructure.Temporal;
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
/// <b>Atomic-outbox contract (ADR-018 D5).</b> <see cref="UpsertAsync"/> and
/// <see cref="CreateAsync"/> are <c>(conn, tx)</c> overloads only — the endpoint or seeder
/// owns the transaction, threading audit + outbox writes into the same atomic unit.
/// <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/> is the convenience
/// self-managed overload for non-tx callers (admin GET handler + seeder bootstrap probe).
/// </para>
///
/// <para>
/// <b>ADR-019 admin-strict If-Match.</b> <see cref="UpsertAsync"/> accepts
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
/// timeline (cases A / B' / C' / E / G / T), with ONE client concurrency token per employee (the
/// open row's version, bumped on every timeline write), the S23-shape same-values no-op,
/// <c>employment_category</c> as an editable fourth field, and a <c>users.employment_category</c>
/// cache refresh sourced from the row covering TODAY. The S31/S33 paragraphs above are kept as
/// the history of how the shape got here.
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
    /// fixes. The DAY DERIVATION is unchanged — still the UTC day
    /// (<c>DateOnly.FromDateTime(GetUtcNow().UtcDateTime)</c>), matching the endpoints' validators
    /// and the frontend's <c>toISOString().slice(0,10)</c>; only the SOURCE of the clock moved.
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
    /// S31 / TASK-3102 — convenience read: returns the live (open) employee profile for
    /// <paramref name="employeeId"/>, fully hydrated with sibling fields from the <c>users</c>
    /// table (<see cref="EmploymentProfile.AgreementCode"/>, <see cref="EmploymentProfile.OkVersion"/>,
    /// <see cref="EmploymentProfile.EmploymentCategory"/>, <see cref="EmploymentProfile.OrgId"/>),
    /// or <c>null</c> if no live row exists for the employee.
    ///
    /// <para>
    /// <see cref="EmploymentProfile.IsPartTime"/> is computed as
    /// <c>part_time_fraction &lt; 1.0m</c> per refinement cycle 2 absorption — there is no
    /// <c>is_part_time</c> column in the schema.
    /// </para>
    ///
    /// <para>
    /// <b>LIVE-only single-purpose read (S34 / TASK-3413 audit lock).</b> Returns the CURRENT
    /// <see cref="EmploymentProfile"/> for the given employee, sourced via the partial-unique-
    /// index predicate <c>WHERE ep.effective_to IS NULL</c>; the <c>u.agreement_code</c> JOIN
    /// is a LIVE read off the <c>users</c> tail. <b>MUST NOT be used for replay-sensitive
    /// (past-period / as-of-date) reads</b> — use
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
        var hit = await ExecuteGetByEmployeeIdAsync(conn, null, employeeId, ct);
        return hit?.Profile;
    }

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/>. Reuses the caller-
    /// supplied <paramref name="conn"/> + <paramref name="tx"/> so the read sits inside the
    /// same transaction as a downstream write (ADR-018 D5 atomic-outbox contract). Used by
    /// admin endpoint handlers that need to read-then-emit-event atomically and by
    /// <see cref="UpsertAsync"/>'s internal preflight when constructing audit payloads.
    ///
    /// <para>
    /// <b>LIVE-only single-purpose read (S34 / TASK-3413 audit lock).</b> Inherits the
    /// LIVE-only contract of the self-managed overload — the underlying SQL filters
    /// <c>WHERE ep.effective_to IS NULL</c> and JOINs <c>u.agreement_code</c> off the LIVE
    /// <c>users</c> tail. <b>MUST NOT be used for replay-sensitive (past-period) reads</b>;
    /// route those through
    /// <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentProfileResolver.GetByEmployeeIdAtAsync"/>
    /// per ADR-023 D2 + S34 cutover. The only production consumer of this overload is the
    /// admin DELETE handler's pre-delete audit-payload snapshot (live row about to be
    /// soft-deleted) — that is a LIVE-state need by construction.
    /// </para>
    /// </summary>
    public async Task<EmploymentProfile?> GetByEmployeeIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, CancellationToken ct = default)
    {
        var hit = await ExecuteGetByEmployeeIdAsync(conn, tx, employeeId, ct);
        return hit?.Profile;
    }

    /// <summary>
    /// Step 7a P2 fix — atomic row + version read. The GET endpoint must hand back the row
    /// data and its <c>version</c> from the SAME live snapshot so the ETag it stamps
    /// matches the data it serializes; reading the two in separate statements opens a
    /// concurrency window where the response can carry stale fields with a newer ETag and
    /// the next admin edit would silently overwrite the racing change. Single SELECT;
    /// nullable tuple shape mirrors <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/>.
    ///
    /// <para>
    /// <b>LIVE-only single-purpose read (S34 / TASK-3413 audit lock).</b> The <c>version</c>
    /// returned here is the LIVE row's optimistic-concurrency token used to stamp the
    /// response ETag for admin If-Match round-trips. The read uses the partial-unique-index
    /// predicate <c>WHERE ep.effective_to IS NULL</c> and JOINs <c>u.agreement_code</c> off
    /// the LIVE <c>users</c> tail. <b>MUST NOT be used for replay-sensitive (past-period)
    /// reads</b> — past-period payroll / PCS-replay paths must use
    /// <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentProfileResolver.GetByEmployeeIdAtAsync"/>
    /// per ADR-023 D2 + S34 cutover. Sole production consumer is the admin GET handler.
    /// </para>
    /// </summary>
    public async Task<(EmploymentProfile Profile, long Version)?> GetByEmployeeIdWithVersionAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteGetByEmployeeIdAsync(conn, null, employeeId, ct);
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
    /// <b>LIVE-only single-purpose shared codepath (S34 / TASK-3413 audit lock).</b> The
    /// SQL below filters <c>WHERE ep.effective_to IS NULL</c> (the partial-unique-index
    /// predicate) and JOINs <c>u.agreement_code</c> off the LIVE <c>users</c> tail; the
    /// result is the CURRENT employee profile only. <b>This helper MUST NOT be reused
    /// for replay-sensitive (past-period / as-of-date) reads</b> — the
    /// <c>effective_to IS NULL</c> predicate is non-negotiable here. Past-period lookups
    /// go through
    /// <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentProfileResolver.GetByEmployeeIdAtAsync"/>,
    /// which uses the end-exclusive predicate
    /// <c>effective_from &lt;= asOfDate AND (effective_to IS NULL OR effective_to &gt; asOfDate)</c>
    /// and sources <c>agreement_code</c> from the dated <c>user_agreement_codes</c> table
    /// per ADR-023 D2 + S34 cutover.
    /// </summary>
    private static async Task<(EmploymentProfile Profile, long Version)?> ExecuteGetByEmployeeIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, CancellationToken ct)
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
        // mislabel rather than rescue. (This read is live-row-only, so the two still agree
        // here by the cache rule — the change is about which one is AUTHORITATIVE.)
        // `ep.version` joins in
        // the row's optimistic-concurrency token for callers that need it on the ETag header
        // (Step 7a P2 fix — same-snapshot read kills the GET race against concurrent admin
        // edits).
        const string sql =
            """
            SELECT
                ep.part_time_fraction,
                ep.position,
                ep.version,
                u.agreement_code,
                u.ok_version,
                ep.employment_category,
                u.primary_org_id
            FROM employee_profiles ep
            INNER JOIN users u ON u.user_id = ep.employee_id
            WHERE ep.employee_id = @employeeId
              AND ep.effective_to IS NULL
            """;
        await using var cmd = tx is null
            ? new NpgsqlCommand(sql, conn)
            : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
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
        var version = reader.GetInt64(reader.GetOrdinal("version"));
        return (profile, version);
    }

    // ------------------------------------------------------------------
    // Writes — atomic-outbox (conn, tx) overloads only (ADR-018 D5).
    // S31 scope: UPDATE the live row (no supersession routing); INSERT a fresh row
    // for net-new employees. Both bump nothing — INSERT writes version=1, UPDATE
    // increments by one. Endpoint owns audit + outbox emission.
    // ------------------------------------------------------------------

    /// <summary>
    /// S31 / TASK-3102 — atomic-outbox INSERT overload for a brand-new live profile row.
    /// Used by TASK-3106 EmployeeProfileSeeder during bootstrap (one row per existing user)
    /// and by TASK-3108 AdminEndpoints POST extension (4-way atomicity: users INSERT +
    /// employee_profiles INSERT + UserCreated outbox + EmployeeProfileCreated outbox, all
    /// in one tx). Writes <c>version = 1</c>, <c>effective_from = '0001-01-01'</c> (schema
    /// default; pre-baked versioning column is dormant in S31), <c>effective_to = NULL</c>.
    /// Caller commits or rolls back the transaction; endpoint emits the audit row + outbox
    /// event in the same tx after this returns.
    ///
    /// <para>
    /// Returns <c>(profile_id, version=1)</c> for the inserted row. The endpoint sets
    /// the wire ETag to <c>"1"</c> on the 201 response.
    /// </para>
    /// </summary>
    /// <exception cref="PostgresException">
    /// Thrown on partial-unique-index conflict (<c>idx_employee_profiles_live</c>) when a
    /// live row already exists for <paramref name="req"/><c>.EmployeeId</c>. The caller
    /// (endpoint) is expected to translate <c>SqlState = "23505"</c> to 409 Conflict; the
    /// seeder should never hit this case because it guards on existing rows.
    /// </exception>
    public async Task<(Guid ProfileId, long Version)> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeProfileCreateRequest req, CancellationToken ct = default)
    {
        // profile_id is generated client-side so the endpoint can include it in the
        // outbox event body (S29 WTM precedent at WageTypeMappingRepository.cs:137).
        // S33 in-flight defect fix: stamp effective_from = today (UTC) instead of using
        // the schema DEFAULT '0001-01-01' (S31 placeholder). Under TASK-3302's new
        // 3-case routing, the first PUT against a default-seeded row would trigger
        // Case C cross-day supersession (because '0001-01-01' < today), creating a
        // brand-new successor row at version=1 instead of UPDATE-in-place at version=2.
        // Stamping today makes the freshly-created row sit in the same-day window for
        // any same-day PUT (Case B routing → version bump), matching pre-S33 admin
        // expectations.
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
                @effectiveFrom, NULL, 1,
                (SELECT u.employment_category FROM users u WHERE u.user_id = @employeeId))
            RETURNING profile_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("profileId", newProfileId);
        cmd.Parameters.AddWithValue("employeeId", req.EmployeeId);
        cmd.Parameters.AddWithValue("partTimeFraction", req.PartTimeFraction);
        cmd.Parameters.AddWithValue("position", (object?)req.Position ?? DBNull.Value);
        cmd.Parameters.AddWithValue(
            "effectiveFrom", DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime));
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
    /// touched. FUTURE dates are refused (owner ruling — see <see cref="TemporalWriteRouter"/>).
    /// </para>
    ///
    /// <para>
    /// <b>One concurrency token per aggregate (S138 Reviewer W1 / Codex B4).</b> The client's
    /// token is the OPEN row's <c>version</c> — what the profile GET's ETag carries — and
    /// <paramref name="expectedVersion"/> is validated against it (a history row's version is
    /// never issued to a client, so an If-Match on it would have no meaning). EVERY timeline
    /// write, including a history-only split, bumps the open row's version, so the ETag is a
    /// monotonic per-employee TIMELINE version: two admins backdating against the same ETag
    /// serialize on the lock and the second gets a 412 — both succeed only as sequential retries
    /// with refreshed ETags. History rows' own <c>version</c> column is left untouched (bumping it
    /// would mint tokens nobody holds). <see cref="SaveEmployeeProfileResult.Version"/> is
    /// therefore the token AFTER the write in every case; the touched row's own version is
    /// <see cref="SaveEmployeeProfileResult.ProducedRowVersion"/>. Audit
    /// <c>version_before/after</c> on <c>employee_profile_audit</c> record this token.
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
    /// <b>Cache rule.</b> <c>users.employment_category</c> means "the category as of TODAY".
    /// After the row write this method re-reads the row covering today and, only when its category
    /// differs from the cached value, writes <c>UPDATE users SET employment_category, version =
    /// version + 1</c> — a cache refresh IS a users-row write under ADR-018 D7 (the admin user DTO
    /// exposes the field, so a stale users ETag must 412 afterwards). The cache is never set from
    /// the REQUEST: a historical-only correction leaves it untouched by construction, and a
    /// fraction-only change that leaves the category alone does not touch <c>users</c> at all. The
    /// write is not gated on <c>is_active</c> — a departed employee's cache must be correctable.
    /// The repository does not know the actor, so it returns
    /// <see cref="SaveEmployeeProfileResult.UsersVersionBefore"/> / <c>After</c> plus the old/new
    /// value for the endpoint's <c>users_audit</c> row.
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
    /// <see cref="TemporalWriteRejection.FutureDated"/> when <paramref name="req"/><c>.EffectiveFrom</c>
    /// is after today (UTC); <see cref="TemporalWriteRejection.PrecedesEmploymentStart"/> when the
    /// caller supplied <c>req.EmploymentStartDate</c> and the date precedes it. Those two are pure
    /// predicates raised BEFORE any lock (cheap, nothing to roll back).
    /// <see cref="TemporalWriteRejection.NoRecordedEmploymentCategory"/> (S138 Step-5a) is different
    /// and is raised AFTER the timeline lock and the If-Match check, because it can only be decided
    /// once the routed case is known: router case E puts the write before every recorded row, so no
    /// row covers or precedes the date and nothing records which category held then — and the only
    /// remaining source, the live <c>users</c> value, means "as of today" and would mislabel history.
    /// The caller's transaction is rolled back by the endpoint, so the late throw costs a lock, not
    /// correctness. All three map to a date-free 422.
    /// </exception>
    /// <exception cref="OptimisticConcurrencyException">
    /// <paramref name="expectedVersion"/> non-null and (a) no open row exists
    /// (<c>ActualVersion = null</c>) or (b) the open row's <c>version</c> differs. Endpoint maps to 412.
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
        // "Today" is UTC, read via the injected TimeProvider — the endpoints' validators and the
        // S33 today-stamp use the same clock (S139 / TASK-13907 moved the SOURCE of that clock
        // onto the DI seam; the day it yields is unchanged). The router below stays PURE: `today`
        // is passed IN as a parameter (PAT-025), never read inside it.
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        // 0. Pure refusals BEFORE any lock — nothing to roll back, nothing to contend on.
        if (TemporalWriteRouter.IsFutureDated(req.EffectiveFrom, today))
            throw new TemporalWriteRejectedException(TemporalWriteRejection.FutureDated, "employee profile");
        if (TemporalWriteRouter.PrecedesEmploymentStart(req.EffectiveFrom, req.EmploymentStartDate))
            throw new TemporalWriteRejectedException(TemporalWriteRejection.PrecedesEmploymentStart, "employee profile");

        // 1. Lock the whole timeline (open row first). From here to commit no other writer can
        //    touch this employee's rows, so every decision below is made on locked state.
        var timeline = await LockTimelineAsync(conn, tx, req.EmployeeId, ct);
        var live = timeline.FirstOrDefault(r => r.EffectiveTo is null);

        // 2. Validate the aggregate token (admin-strict If-Match, ADR-019) against the OPEN row.
        if (expectedVersion is not null)
        {
            if (live is null)
            {
                // Caller asserted a current version, but there is no open row → degenerate
                // mismatch (412). ActualVersion = null distinguishes this branch.
                throw new OptimisticConcurrencyException(
                    $"No live employee profile exists for employee_id='{req.EmployeeId}', " +
                    $"but caller sent If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            if (live.Version != expectedVersion.Value)
            {
                throw new OptimisticConcurrencyException(
                    $"Employee profile version is {live.Version}, but caller sent " +
                    $"If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                    expectedVersion: expectedVersion,
                    actualVersion: live.Version);
            }
        }

        // 3. Route on the locked snapshot (pure). The anchor is matched back to its locked row
        //    by start date — a key under idx_employee_profiles_history.
        var decision = TemporalWriteRouter.Decide(
            timeline.Select(r => new TemporalInterval(r.EffectiveFrom, r.EffectiveTo)),
            req.EffectiveFrom, today);
        if (decision.Case == TemporalWriteCase.RejectedFutureDated)
        {
            // Unreachable after step 0; kept so the router remains the single authority.
            throw new TemporalWriteRejectedException(TemporalWriteRejection.FutureDated, "employee profile");
        }
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
            return new SaveEmployeeProfileResult(
                anchor.ProfileId, live?.Version ?? anchor.Version, SaveEmployeeProfileOutcome.NoOp)
            {
                Kind = TemporalWriteKind.NoOp,
                IsNoOp = true,
                NewEffectiveFrom = anchor.EffectiveFrom,
                NewEffectiveTo = anchor.EffectiveTo,
                ProducedRowVersion = anchor.Version,
                Covering = anchor,
                TimelineVersionBefore = live?.Version,
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

        // 6. Cache rule — users.employment_category follows the row covering TODAY, never the request.
        var cache = await RefreshEmploymentCategoryCacheAsync(conn, tx, req.EmployeeId, today, ct);
        return cache is null
            ? result
            : result with
            {
                UsersVersionBefore = cache.Value.VersionBefore,
                UsersVersionAfter = cache.Value.VersionAfter,
                PreviousEmploymentCategoryCache = cache.Value.PreviousValue,
                NewEmploymentCategoryCache = cache.Value.NewValue,
            };
    }

    /// <summary>
    /// S31 / TASK-3102 — atomic-outbox UPDATE overload for the live row of an existing
    /// employee profile. Used by TASK-3107 admin PUT handler. Caller threads audit + outbox
    /// emission into the same transaction.
    ///
    /// <para>
    /// <b>S33 / TASK-3302 refactor — now a thin shim that delegates to
    /// <see cref="SupersedeAndCreateAsync"/> with
    /// <c>EffectiveFrom = today</c> (UTC, via the injected <see cref="TimeProvider"/>).</b> The 2-tuple return
    /// shape <c>(ProfileId, Version)</c> is preserved for backwards compatibility with
    /// existing S31 callers (<see cref="EmployeeProfileEndpoints"/> PUT handler); the
    /// underlying method's <see cref="SaveEmployeeProfileResult.Outcome"/> is discarded
    /// here but routes correctly under the hood — for instance, when today's date is later
    /// than the predecessor's <c>effective_from</c>, this shim will silently route through
    /// Case C (cross-day supersession) rather than Case B (same-day in-place edit). The
    /// TASK-3308 endpoint cutover will call <see cref="SupersedeAndCreateAsync"/> directly
    /// to read <c>Outcome</c> and emit the correct event type.
    /// </para>
    ///
    /// <para>
    /// <b>S31-compatible exception contract.</b> The S31 endpoint (PUT) caught both
    /// <see cref="OptimisticConcurrencyException"/> (412) and <see cref="KeyNotFoundException"/>
    /// (404). The shim translates "no live row + non-null <paramref name="expectedVersion"/>"
    /// — which <see cref="SupersedeAndCreateAsync"/> raises as
    /// <see cref="OptimisticConcurrencyException"/> with <c>ActualVersion = null</c> — back
    /// to <see cref="KeyNotFoundException"/> so the existing endpoint surface continues to
    /// return 404 in that case unchanged.
    /// </para>
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no live row exists for <paramref name="req"/><c>.EmployeeId</c> and
    /// <paramref name="expectedVersion"/> is non-null. Preserves the S31 endpoint contract
    /// (PUT against a non-existent employee profile → 404).
    /// </exception>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when a live row exists and <paramref name="expectedVersion"/> does not match
    /// its <c>version</c>. Endpoint maps to 412 per ADR-019.
    /// </exception>
    /// <exception cref="InvalidProfileSupersessionException">
    /// Not thrown via this shim under normal use — the shim always passes
    /// <c>EffectiveFrom = today</c> (UTC, via the injected <see cref="TimeProvider"/>), which is
    /// never earlier than the predecessor's <c>effective_from</c> (unless the clock is misconfigured).
    /// </exception>
    public async Task<(Guid ProfileId, long Version)> UpsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeProfileUpsertRequest req, long? expectedVersion,
        CancellationToken ct = default)
    {
        var supersedeRequest = new EmployeeProfileSupersedeRequest(
            EmployeeId: req.EmployeeId,
            PartTimeFraction: req.PartTimeFraction,
            Position: req.Position,
            EffectiveFrom: DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime));
        try
        {
            var result = await SupersedeAndCreateAsync(conn, tx, supersedeRequest, expectedVersion, ct);
            return (result.ProfileId, result.Version);
        }
        catch (OptimisticConcurrencyException ex) when (ex.ActualVersion is null && expectedVersion is not null)
        {
            // S31 endpoint contract: "no live row + If-Match supplied" → 404, not 412.
            // SupersedeAndCreateAsync raises this as OCE-with-null-actual; translate back
            // to KeyNotFoundException so the existing PUT handler's catch block is preserved.
            throw new KeyNotFoundException(
                $"Employee profile not found for employee_id='{req.EmployeeId}'.", ex);
        }
    }

    /// <summary>
    /// S33 / TASK-3303 — soft-delete the live employee profile row by stamping
    /// <c>effective_to = @today</c> (the UTC day from the injected <see cref="TimeProvider"/>,
    /// bound as a parameter — S139 / TASK-13907 replaced the former DB-side <c>NOW()::date</c>)
    /// under end-exclusive <c>[from, to)</c> semantics
    /// (ADR-018 D9). After this call, the row no longer satisfies the partial-unique-index
    /// <c>idx_employee_profiles_live</c> predicate (<c>WHERE effective_to IS NULL</c>) and is
    /// invisible to <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/>, but remains
    /// in the history table for replay determinism (ADR-016 D10).
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
    /// <b>404-vs-412 retry semantic divergence.</b> Because the predecessor row's version is
    /// unchanged, an admin retry with stale <c>If-Match: "@expectedVersion"</c> after a
    /// successful soft-delete will hit <b>404 Not Found</b> (the partial-unique-index
    /// <c>WHERE effective_to IS NULL</c> matches no live row), <b>NOT 412 Precondition Failed</b>.
    /// This is intentional — soft-delete is idempotent-by-row-disappearance rather than
    /// idempotent-by-version-bump. Sibling ADR-019 D8 endpoints map stale-after-delete to 412
    /// because they bump version + leave the row visible to history-comparing queries;
    /// employee_profiles DELETE chooses row-disappearance per ADR-023 D8. The D-test
    /// <c>SoftDelete_StaleIfMatchAfterSoftDelete_Returns404NotConflict412</c> in TASK-3312 locks
    /// this contract.
    /// </para>
    ///
    /// <para>
    /// <b>SQL contract (binding — no <c>version + 1</c> clause).</b>
    /// <code>
    /// UPDATE employee_profiles
    ///    SET effective_to = @today, updated_at = NOW()
    ///  WHERE employee_id = @employeeId
    ///    AND effective_to IS NULL
    ///    AND version = @expectedVersion
    /// RETURNING profile_id, version
    /// </code>
    /// <c>@today</c> is the APP-side UTC day (S139 / TASK-13907), bound as a <c>DateOnly</c> that
    /// Npgsql maps to <c>date</c> — so it is day-granular by type, where the retired
    /// <c>NOW()::date</c> was day-granular by cast. Behaviour-preserving: the Postgres session
    /// time zone is UTC everywhere this runs, so <c>NOW()::date</c> already yielded the UTC day.
    /// The <c>updated_at = NOW()</c> half deliberately stays a DB timestamp (a row-maintenance
    /// stamp, not a temporal boundary). The SQL is single-statement because the
    /// version predicate handles the race (no <c>SELECT ... FOR UPDATE</c> needed — unlike
    /// <see cref="SupersedeAndCreateAsync"/> which has 3-case routing to resolve under the lock).
    /// </para>
    ///
    /// <para>
    /// <b>Atomic-outbox contract (ADR-018 D5).</b> Caller (TASK-3308 endpoint) owns the
    /// transaction; this method only writes to <c>employee_profiles</c>. The endpoint emits
    /// the audit row (with <c>version_before = version_after = predecessor.version</c>) +
    /// <c>EmployeeProfileSoftDeleted</c> outbox event in the same tx after this returns.
    /// </para>
    ///
    /// <para>
    /// <b>Exception distinguishing pattern (S31 precedent at UpsertAsync L446-453).</b> After
    /// the UPDATE fails to match a row, this method probes the live row's <c>version</c>
    /// column to distinguish 404 (no live row) from 412 (live row, version mismatch):
    /// <list type="bullet">
    ///   <item><description>Probe returns <c>null</c> → no live row exists → throws
    ///     <see cref="KeyNotFoundException"/>. Endpoint maps to 404.</description></item>
    ///   <item><description>Probe returns a value (the live row's actual version, different
    ///     from <paramref name="expectedVersion"/>) → throws
    ///     <see cref="OptimisticConcurrencyException"/> with the actual version. Endpoint
    ///     maps to 412 per ADR-019 D2.</description></item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="conn">Caller-owned connection (ADR-018 D5 atomic-outbox contract).</param>
    /// <param name="tx">Caller-owned transaction; this method does not commit or roll back.</param>
    /// <param name="employeeId">Natural key — the <c>employee_id</c> of the live profile row to soft-delete.</param>
    /// <param name="expectedVersion">The <c>version</c> column value the caller asserts is
    /// currently stored on the live row. The UPDATE's <c>AND version = @expectedVersion</c>
    /// predicate enforces optimistic concurrency under ADR-019 admin-strict If-Match.</param>
    /// <param name="closeDate">S139 / TASK-13907 — the date to stamp into <c>effective_to</c>,
    /// which the caller computes ONCE for the whole request so the row and the
    /// <c>EmployeeProfileSoftDeleted</c> event it describes carry the same date by construction
    /// (ADR-023 D8's shape). OPTIONAL and trailing: when omitted, the repository reads its own
    /// injected <see cref="TimeProvider"/> for the UTC day — correct for any caller that needs
    /// only one date, and it keeps existing callers and direct test constructions compiling.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <c>(profile_id, version)</c> of the soft-deleted row, where <c>version</c> is
    /// <b>unchanged</b> from the predecessor's value (per ADR-023 D8). The endpoint records
    /// this on the audit row as <c>version_before = version_after = version</c>.
    /// </returns>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when a live row exists for <paramref name="employeeId"/> but its <c>version</c>
    /// column differs from <paramref name="expectedVersion"/>. <c>ExpectedVersion</c> is set
    /// to <paramref name="expectedVersion"/>; <c>ActualVersion</c> is set to the live row's
    /// actual version. Endpoint maps to 412 Precondition Failed per ADR-019 D2.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no live row (<c>effective_to IS NULL</c>) exists for
    /// <paramref name="employeeId"/>. Endpoint maps to 404 Not Found. This is also the branch
    /// hit by an admin retry with stale <c>If-Match</c> after a successful soft-delete (the
    /// row "disappeared" from live reads per the partial-unique-index predicate — see
    /// 404-vs-412 retry semantic divergence above).
    /// </exception>
    public async Task<(Guid ProfileId, long Version)> SoftDeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, long expectedVersion,
        DateOnly? closeDate = null,
        CancellationToken ct = default)
    {
        // S139 / TASK-13907 Step-5a W1 — ONE instant per request. The caller computes the
        // request's "today" ONCE and hands it in as `closeDate`, so the row's `effective_to` and
        // the `EmployeeProfileSoftDeleted` event's `EffectiveTo` are THE SAME VALUE by
        // construction, not by two reads that happen to agree. Two separate reads of the same
        // provider are not the same instant: at 23:59:59.9 UTC the first can land on the 7th and
        // the second on the 8th, and the row would then disagree with the event that describes
        // it — an auditability defect, not a rounding nuisance. This is the same "compute once"
        // rule S137 wrote for the create POST (AdminEndpoints.cs, the `effectiveFrom` comment).
        // The parameter is OPTIONAL and trailing so existing direct constructions and callers
        // keep compiling; when omitted the repository falls back to its own seam read, which is
        // correct for any caller that needs only one date.
        var today = closeDate ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        // 1. Single-statement UPDATE with row-disappearance semantic — no version bump
        //    (ADR-023 D8: soft-delete is row-state-change, not field-mutation; the partial-
        //    unique-index `idx_employee_profiles_live` makes the row "disappear" from live
        //    reads, so bumping version would be redundant). The `AND version = @expectedVersion`
        //    predicate enforces optimistic concurrency without needing a separate
        //    `SELECT ... FOR UPDATE` step — unlike SupersedeAndCreateAsync's 3-case routing,
        //    soft-delete has no branching that needs the lock to be held across multiple
        //    statements.
        //
        //    S139 / TASK-13907 — the close-stamp is now the APP-side UTC day, bound as `@today`
        //    from the `today` local above, where it used to be the DB-side `NOW()::date`. WHY:
        //    this request reads "today" in the endpoint too (the future-dating validator, and the
        //    SoftDeleted event's EffectiveTo), so a DB-side read made one HR action depend on two
        //    clocks — and a fixed test clock could not move the database's. The endpoint now
        //    computes that date ONCE and passes it in as `closeDate`, so the row and the event it
        //    describes carry the same date by construction.
        //    BEHAVIOUR-PRESERVING: the Postgres session time zone is UTC wherever this runs
        //    (compose, init.sql and the Testcontainers builder set no override), so `NOW()::date`
        //    already produced the UTC day. `effective_to` stays day-granular because Npgsql maps
        //    DateOnly to `date`. `updated_at = NOW()` stays a DB timestamp on purpose: it is
        //    row-maintenance metadata, not a temporal boundary anyone reasons about.
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE employee_profiles
               SET effective_to = @today, updated_at = NOW()
             WHERE employee_id = @employeeId
               AND effective_to IS NULL
               AND version = @expectedVersion
            RETURNING profile_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("today", today);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            // Happy path: UPDATE matched exactly one row (partial-unique-index guarantees ≤1).
            // Returned version is UNCHANGED from predecessor per ADR-023 D8.
            return (reader.GetGuid(0), reader.GetInt64(1));
        }
        // The reader must be disposed before we can issue the probe SELECT on the same
        // connection (Npgsql forbids overlapping commands on a single connection).
        await reader.DisposeAsync();

        // 2. UPDATE matched no row. Probe to distinguish 404 (no live row) from 412 (live
        //    row exists, version differs) per S31 UpsertAsync precedent. This second read
        //    sits inside the same tx so it sees the same snapshot as the failed UPDATE — no
        //    chance of a TOCTOU window mis-classifying a concurrent insert as a 404.
        await using var probeCmd = new NpgsqlCommand(
            """
            SELECT version FROM employee_profiles
            WHERE employee_id = @employeeId
              AND effective_to IS NULL
            """, conn, tx);
        probeCmd.Parameters.AddWithValue("employeeId", employeeId);
        var probeResult = await probeCmd.ExecuteScalarAsync(ct);
        if (probeResult is null || probeResult is DBNull)
        {
            // No live row → 404. This branch is also hit by an admin retry with stale
            // If-Match after a successful soft-delete (row disappeared per partial-unique-
            // index predicate; ADR-023 D8 row-disappearance idempotency — see XML doc above).
            throw new KeyNotFoundException(
                $"Employee profile not found for employee_id='{employeeId}'.");
        }
        var actualVersion = (long)probeResult;
        // Live row exists but version differs → 412 per ADR-019 D2 admin-strict If-Match.
        throw new OptimisticConcurrencyException(
            $"Employee profile version is {actualVersion}, but caller sent " +
            $"If-Match: \"{expectedVersion}\"; refresh and retry.",
            expectedVersion: expectedVersion,
            actualVersion: actualVersion);
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
    /// The cache rule (see <see cref="SupersedeAndCreateAsync"/>): re-read the category of the row
    /// covering TODAY after the write and, only if it differs from <c>users.employment_category</c>,
    /// write the cache with a <c>users.version</c> bump. Returns <c>null</c> when nothing was
    /// written — no row covers today (post soft-delete), the covering cell is a legacy NULL (which
    /// by the S137 COALESCE contract already means "same as users"), or the value is unchanged.
    /// Not gated on <c>is_active</c>. Locks the users row (<c>FOR UPDATE</c>) before comparing so
    /// the before-value the endpoint audits is the one actually replaced.
    /// </summary>
    private static async Task<(long VersionBefore, long VersionAfter, string PreviousValue, string NewValue)?>
        RefreshEmploymentCategoryCacheAsync(
            NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, DateOnly today, CancellationToken ct)
    {
        string? todayCategory;
        await using (var todayCmd = new NpgsqlCommand(
            """
            SELECT employment_category
            FROM employee_profiles
            WHERE employee_id = @employeeId
              AND effective_from <= @today
              AND (effective_to IS NULL OR effective_to > @today)
            """, conn, tx))
        {
            todayCmd.Parameters.AddWithValue("employeeId", employeeId);
            todayCmd.Parameters.AddWithValue("today", today);
            var scalar = await todayCmd.ExecuteScalarAsync(ct);
            todayCategory = scalar is null || scalar is DBNull ? null : (string)scalar;
        }
        if (todayCategory is null) return null;

        string cachedCategory;
        long cachedVersion;
        await using (var usersCmd = new NpgsqlCommand(
            """
            SELECT employment_category, version
            FROM users
            WHERE user_id = @employeeId
            FOR UPDATE
            """, conn, tx))
        {
            usersCmd.Parameters.AddWithValue("employeeId", employeeId);
            await using var reader = await usersCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // The employee_id FK guarantees the users row; reaching here is a programming error.
                throw new InvalidOperationException(
                    $"users row for user_id='{employeeId}' not found while refreshing the employment_category cache.");
            }
            cachedCategory = reader.GetString(0);
            cachedVersion = reader.GetInt64(1);
        }
        if (string.Equals(cachedCategory, todayCategory, StringComparison.Ordinal)) return null;

        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE users
               SET employment_category = @employmentCategory,
                   version = version + 1,
                   updated_at = NOW()
             WHERE user_id = @employeeId
            RETURNING version
            """, conn, tx);
        updateCmd.Parameters.AddWithValue("employeeId", employeeId);
        updateCmd.Parameters.AddWithValue("employmentCategory", todayCategory);
        var newVersion = await updateCmd.ExecuteScalarAsync(ct);
        if (newVersion is null || newVersion is DBNull)
        {
            throw new InvalidOperationException(
                $"users cache write for user_id='{employeeId}' matched no row; FOR UPDATE invariant violated.");
        }
        return (cachedVersion, (long)newVersion, cachedCategory, todayCategory);
    }
}

// ------------------------------------------------------------------
// Request records — Created + Upsert kept separate today for forward-compat with S32
// where Create may take an explicit effective_from (cross-day supersession routing
// per ADR-020 D2). In S31 the shapes are identical.
// ------------------------------------------------------------------

/// <summary>
/// S31 / TASK-3102 — payload for <see cref="EmployeeProfileRepository.UpsertAsync"/>.
/// All three S31-authoritative fields plus the natural key. <see cref="Position"/> is
/// nullable per the schema definition (TEXT NULL).
/// </summary>
public sealed record EmployeeProfileUpsertRequest(
    string EmployeeId,
    decimal PartTimeFraction,
    string? Position);

/// <summary>
/// S31 / TASK-3102 — payload for <see cref="EmployeeProfileRepository.CreateAsync"/>.
/// Kept separate from <see cref="EmployeeProfileUpsertRequest"/> for forward-compat: S32
/// will extend this with an explicit <c>EffectiveFrom</c> field once supersession routing
/// is added. In S31, INSERTs always use the schema default <c>'0001-01-01'</c>.
/// </summary>
public sealed record EmployeeProfileCreateRequest(
    string EmployeeId,
    decimal PartTimeFraction,
    string? Position);

/// <summary>
/// S33 / TASK-3302 — payload for <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/>.
/// Extends <see cref="EmployeeProfileUpsertRequest"/>'s field set with the explicit
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
/// <param name="Version">S138: the per-employee TIMELINE token AFTER the write — the OPEN row's
/// <c>version</c> — in every case (the value the endpoint stamps as ETag and records as audit
/// <c>version_after</c>). It coincides with the produced row's version for A / B'-on-open /
/// C'-on-open (the pre-S138 cases, unchanged) and for T; for history-only writes it is the open
/// row's bumped version. On a no-op it is the unchanged current token. When no open row exists at
/// all (a history-only write after a soft-delete) it falls back to the produced row's version.</param>
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

    /// <summary>S138 — the open row's version BEFORE the write (audit <c>version_before</c>);
    /// <c>null</c> when no open row existed.</summary>
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
