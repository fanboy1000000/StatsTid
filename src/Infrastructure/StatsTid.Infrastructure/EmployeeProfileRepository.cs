using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S31 / TASK-3102 — Phase 4d-3 Part 1 authoritative store for the three employment-profile
/// fields previously sourced from request payloads (TimeEndpoints) or hardcoded constants
/// (ComplianceEndpoints): <c>weekly_norm_hours</c>, <c>part_time_fraction</c>, and <c>position</c>.
/// Sibling fields (<c>agreement_code</c>, <c>ok_version</c>, <c>employment_category</c>,
/// <c>primary_org_id</c>) stay on the <c>users</c> table per S31 refinement Q3 LEAVE and are
/// joined in at read time so <see cref="GetByEmployeeIdAsync(string, CancellationToken)"/>
/// returns a fully-hydrated <see cref="EmploymentProfile"/>.
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
/// </summary>
public sealed class EmployeeProfileRepository
{
    private readonly DbConnectionFactory _dbFactory;

    public EmployeeProfileRepository(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
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
    /// </summary>
    public async Task<(EmploymentProfile Profile, long Version)?> GetByEmployeeIdWithVersionAsync(
        string employeeId, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteGetByEmployeeIdAsync(conn, null, employeeId, ct);
    }

    private static async Task<(EmploymentProfile Profile, long Version)?> ExecuteGetByEmployeeIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string employeeId, CancellationToken ct)
    {
        // S31 employee_profiles columns are the source of truth for weekly_norm_hours,
        // part_time_fraction, and position. The sibling fields (agreement_code, ok_version,
        // employment_category, primary_org_id) stay on `users` per refinement Q3 LEAVE and
        // are joined in here so the returned EmploymentProfile is consumable by PCS / rule
        // engine paths unchanged. `ep.version` joins in the row's optimistic-concurrency
        // token for callers that need it on the ETag header (Step 7a P2 fix — same-snapshot
        // read kills the GET race against concurrent admin edits).
        const string sql =
            """
            SELECT
                ep.weekly_norm_hours,
                ep.part_time_fraction,
                ep.position,
                ep.version,
                u.agreement_code,
                u.ok_version,
                u.employment_category,
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
            WeeklyNormHours = reader.GetDecimal(reader.GetOrdinal("weekly_norm_hours")),
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
        var newProfileId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, weekly_norm_hours, part_time_fraction, position,
                effective_from, effective_to, version)
            VALUES (
                @profileId, @employeeId, @weeklyNormHours, @partTimeFraction, @position,
                DEFAULT, NULL, 1)
            RETURNING profile_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("profileId", newProfileId);
        cmd.Parameters.AddWithValue("employeeId", req.EmployeeId);
        cmd.Parameters.AddWithValue("weeklyNormHours", req.WeeklyNormHours);
        cmd.Parameters.AddWithValue("partTimeFraction", req.PartTimeFraction);
        cmd.Parameters.AddWithValue("position", (object?)req.Position ?? DBNull.Value);
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
    /// S33 / TASK-3302 — ADR-020 D2 3-case routing under <c>SELECT ... FOR UPDATE</c>.
    /// This is the canonical write path for employee profiles; <see cref="UpsertAsync"/>
    /// is now a thin shim that delegates here with <c>EffectiveFrom = today (UTC)</c>.
    ///
    /// <para>
    /// <b>Routing</b> (decided after acquiring a row-level lock on the live row, if any,
    /// for <c>req.EmployeeId</c> via <c>SELECT ... FOR UPDATE</c>):
    /// <list type="bullet">
    ///   <item><description><b>Case A — Created.</b> No live row exists. Allowed only when
    ///     <paramref name="expectedVersion"/> is <c>null</c> (seeder / admin-POST path).
    ///     INSERT a fresh row at <c>(effective_from = req.EffectiveFrom, effective_to = NULL,
    ///     version = 1)</c>. Returns <see cref="SaveEmployeeProfileOutcome.Created"/>.</description></item>
    ///   <item><description><b>Case B — Updated.</b> Live row exists and its
    ///     <c>effective_from</c> equals <paramref name="req"/><c>.EffectiveFrom</c>. UPDATE
    ///     in-place: refresh fields, bump <c>version = version + 1</c>, stamp
    ///     <c>updated_at = NOW()</c>; <c>profile_id</c> and <c>effective_from</c> are immutable.
    ///     Returns <see cref="SaveEmployeeProfileOutcome.Updated"/>.</description></item>
    ///   <item><description><b>Case C — Superseded.</b> Live row exists and its
    ///     <c>effective_from</c> is strictly earlier than <paramref name="req"/><c>.EffectiveFrom</c>.
    ///     Close the predecessor by stamping <c>effective_to = req.EffectiveFrom</c>
    ///     (end-exclusive, ADR-018 D9 — predecessor's history window becomes
    ///     <c>[predecessor.effective_from, req.EffectiveFrom)</c>; <b>version unchanged</b>),
    ///     then INSERT a new live row at
    ///     <c>(effective_from = req.EffectiveFrom, effective_to = NULL, version = 1)</c>.
    ///     Returns <see cref="SaveEmployeeProfileOutcome.Superseded"/> so the endpoint emits
    ///     <c>EmployeeProfileSuperseded</c> instead of <c>EmployeeProfileUpdated</c>.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Optimistic concurrency (ADR-019 admin-strict If-Match).</b>
    /// When <paramref name="expectedVersion"/> is <b>non-null</b>:
    /// <list type="bullet">
    ///   <item><description>No live row → throws <see cref="OptimisticConcurrencyException"/>
    ///     with <c>ActualVersion = null</c> (caller asserted a current state that does not
    ///     exist; degenerate mismatch).</description></item>
    ///   <item><description>Live row, version differs → throws
    ///     <see cref="OptimisticConcurrencyException"/> with the actual stored version.</description></item>
    /// </list>
    /// When <paramref name="expectedVersion"/> is <b>null</b> (seeder + admin-POST), no
    /// version check is performed; Case A is allowed and Cases B/C proceed unguarded.
    /// </para>
    ///
    /// <para>
    /// <b>Backdate guard.</b> When <paramref name="req"/><c>.EffectiveFrom &lt; predecessor.EffectiveFrom</c>,
    /// throws <see cref="InvalidProfileSupersessionException"/>. Mirrors S29 WTM precedent
    /// at <see cref="WageTypeMappingRepository.SupersedeAndCreateAsync"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Atomic-outbox contract (ADR-018 D5).</b> Caller owns the transaction; this method
    /// only writes to <c>employee_profiles</c>. Endpoint emits the audit row + outbox event
    /// in the same tx after this returns, sourcing the event type from
    /// <see cref="SaveEmployeeProfileResult.Outcome"/> (TASK-3308 cutover).
    /// </para>
    /// </summary>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when <paramref name="expectedVersion"/> is non-null and (a) no live row exists
    /// or (b) the live row's <c>version</c> column differs from <paramref name="expectedVersion"/>.
    /// Endpoint maps to 412 per ADR-019.
    /// </exception>
    /// <exception cref="InvalidProfileSupersessionException">
    /// Thrown when <paramref name="req"/><c>.EffectiveFrom</c> is strictly earlier than the
    /// predecessor's <c>effective_from</c> (backdate rejected per ADR-018 D9 strict-less
    /// under end-exclusive). Endpoint maps to 400/422.
    /// </exception>
    public async Task<SaveEmployeeProfileResult> SupersedeAndCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeProfileSupersedeRequest req, long? expectedVersion,
        CancellationToken ct = default)
    {
        // 1. SELECT ... FOR UPDATE the live row (if any). The partial-unique-index
        //    `idx_employee_profiles_live` guarantees at most one matching row; the row-level
        //    lock serializes concurrent writers attempting to supersede or update the same
        //    employee's live profile. Mirrors S29 WTM precedent at
        //    WageTypeMappingRepository.AcquireLockAsync (L611-635).
        var predecessorNullable = await AcquireLockAsync(conn, tx, req.EmployeeId, ct);

        // 2. Case A — no live row.
        if (predecessorNullable is null)
        {
            if (expectedVersion is not null)
            {
                // Caller asserted a current version, but there is no live row → degenerate
                // mismatch (412). ActualVersion = null distinguishes this branch from the
                // "live row exists, version differs" branch in Case B/C.
                throw new OptimisticConcurrencyException(
                    $"No live employee profile exists for employee_id='{req.EmployeeId}', " +
                    $"but caller sent If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            var (newProfileId, newVersion) = await InsertLiveRowAsync(conn, tx, req, ct);
            return new SaveEmployeeProfileResult(
                newProfileId, newVersion, SaveEmployeeProfileOutcome.Created);
        }

        // Hoist out of the nullable tuple now that we've eliminated the null branch — C#
        // flow-analysis doesn't propagate property access through `?` on value-type tuples.
        var predecessor = predecessorNullable.Value;

        // 3. Predecessor exists. Validate optimistic concurrency (when If-Match supplied).
        if (expectedVersion is not null && predecessor.Version != expectedVersion.Value)
        {
            throw new OptimisticConcurrencyException(
                $"Employee profile version is {predecessor.Version}, but caller sent " +
                $"If-Match: \"{expectedVersion.Value}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: predecessor.Version);
        }

        // 4. Backdate guard (ADR-018 D9 strict-less under end-exclusive). A new row cannot
        //    start before its predecessor — there is no valid history window for the
        //    predecessor in that case. Mirrors S29 WTM precedent at
        //    WageTypeMappingRepository.SupersedeAndCreateInternalAsync (L331-336).
        if (req.EffectiveFrom < predecessor.EffectiveFrom)
        {
            throw new InvalidProfileSupersessionException(
                $"Cannot supersede employee profile for employee_id='{req.EmployeeId}' " +
                $"with effective_from {req.EffectiveFrom:yyyy-MM-dd} earlier than " +
                $"predecessor's effective_from {predecessor.EffectiveFrom:yyyy-MM-dd}.");
        }

        // 5. Case B — same-day edit. UPDATE-in-place with version bump.
        if (req.EffectiveFrom == predecessor.EffectiveFrom)
        {
            var (sameDayProfileId, sameDayVersion) =
                await UpdateInPlaceAsync(conn, tx, req, predecessor.ProfileId, ct);
            return new SaveEmployeeProfileResult(
                sameDayProfileId, sameDayVersion, SaveEmployeeProfileOutcome.Updated);
        }

        // 6. Case C — cross-day edit. Close the predecessor at end-exclusive
        //    `effective_to = req.EffectiveFrom` (version UNCHANGED — close is lifecycle, not
        //    a content edit; mirrors S22 ArchiveProfileAsync semantic), then INSERT new
        //    live row at version=1.
        await ClosePredecessorAsync(conn, tx, predecessor.ProfileId, req.EffectiveFrom, ct);
        var (supersedingProfileId, supersedingVersion) =
            await InsertLiveRowAsync(conn, tx, req, ct);
        return new SaveEmployeeProfileResult(
            supersedingProfileId, supersedingVersion, SaveEmployeeProfileOutcome.Superseded);
    }

    /// <summary>
    /// S31 / TASK-3102 — atomic-outbox UPDATE overload for the live row of an existing
    /// employee profile. Used by TASK-3107 admin PUT handler. Caller threads audit + outbox
    /// emission into the same transaction.
    ///
    /// <para>
    /// <b>S33 / TASK-3302 refactor — now a thin shim that delegates to
    /// <see cref="SupersedeAndCreateAsync"/> with
    /// <c>EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow)</c>.</b> The 2-tuple return
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
    /// <c>EffectiveFrom = today (UTC)</c>, which is never earlier than the predecessor's
    /// <c>effective_from</c> (unless the system clock is misconfigured).
    /// </exception>
    public async Task<(Guid ProfileId, long Version)> UpsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeProfileUpsertRequest req, long? expectedVersion,
        CancellationToken ct = default)
    {
        var supersedeRequest = new EmployeeProfileSupersedeRequest(
            EmployeeId: req.EmployeeId,
            WeeklyNormHours: req.WeeklyNormHours,
            PartTimeFraction: req.PartTimeFraction,
            Position: req.Position,
            EffectiveFrom: DateOnly.FromDateTime(DateTime.UtcNow));
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

    // ------------------------------------------------------------------
    // Private helpers — shared by SupersedeAndCreateAsync's three routing branches.
    // Mirrors S29 WageTypeMappingRepository's AcquireLockAsync / UpdateInPlaceAsync /
    // CloseRowAsync / InsertSupersedingRowAsync triad.
    // ------------------------------------------------------------------

    /// <summary>
    /// Locks the live row (effective_to IS NULL) for <paramref name="employeeId"/> via
    /// <c>SELECT ... FOR UPDATE</c>. Returns the locked row's <c>profile_id</c>, current
    /// <c>version</c>, and <c>effective_from</c> — the three pieces of state
    /// <see cref="SupersedeAndCreateAsync"/> needs to route Cases A/B/C and validate
    /// optimistic concurrency. Returns <c>null</c> when no live row exists (Case A).
    /// Mirrors S29 WTM precedent at
    /// <see cref="WageTypeMappingRepository.AcquireLockAsync"/>.
    /// </summary>
    private static async Task<(Guid ProfileId, long Version, DateOnly EffectiveFrom)?> AcquireLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, CancellationToken ct)
    {
        await using var lockCmd = new NpgsqlCommand(
            """
            SELECT profile_id, version, effective_from
            FROM employee_profiles
            WHERE employee_id = @employeeId
              AND effective_to IS NULL
            FOR UPDATE
            """, conn, tx);
        lockCmd.Parameters.AddWithValue("employeeId", employeeId);
        await using var reader = await lockCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (
            reader.GetGuid(0),
            reader.GetInt64(1),
            reader.GetFieldValue<DateOnly>(2));
    }

    /// <summary>
    /// Case A (Create) + Case C (Supersede) shared path — INSERT a fresh live row at
    /// version=1 with the caller-supplied <c>effective_from</c>. <c>profile_id</c> is
    /// generated client-side (S29 WTM precedent at L137 + S31 CreateAsync at L217) so the
    /// endpoint can include it in the outbox event body. The partial-unique-index
    /// <c>idx_employee_profiles_live</c> guarantees at most one open row per employee;
    /// in Case C the caller has already closed the predecessor under the same tx.
    /// </summary>
    private static async Task<(Guid ProfileId, long Version)> InsertLiveRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeProfileSupersedeRequest req, CancellationToken ct)
    {
        var newProfileId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, weekly_norm_hours, part_time_fraction, position,
                effective_from, effective_to, version)
            VALUES (
                @profileId, @employeeId, @weeklyNormHours, @partTimeFraction, @position,
                @effectiveFrom, NULL, 1)
            RETURNING profile_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("profileId", newProfileId);
        cmd.Parameters.AddWithValue("employeeId", req.EmployeeId);
        cmd.Parameters.AddWithValue("weeklyNormHours", req.WeeklyNormHours);
        cmd.Parameters.AddWithValue("partTimeFraction", req.PartTimeFraction);
        cmd.Parameters.AddWithValue("position", (object?)req.Position ?? DBNull.Value);
        cmd.Parameters.AddWithValue("effectiveFrom", req.EffectiveFrom);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — INSERT ... RETURNING always yields one row on success.
            throw new InvalidOperationException(
                $"InsertLiveRowAsync produced no row for employee_id='{req.EmployeeId}' " +
                $"at effective_from='{req.EffectiveFrom:yyyy-MM-dd}'.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// Case B (Updated) — same-day UPDATE-in-place. Targets the (still-locked) live row by
    /// its <paramref name="profileId"/>; refreshes the three S31-authoritative fields,
    /// bumps <c>version = version + 1</c>, stamps <c>updated_at = NOW()</c>;
    /// <c>profile_id</c> and <c>effective_from</c> are immutable across same-day edits.
    /// Mirrors S29 WTM precedent at
    /// <see cref="WageTypeMappingRepository.UpdateInPlaceAsync"/>.
    /// </summary>
    private static async Task<(Guid ProfileId, long Version)> UpdateInPlaceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EmployeeProfileSupersedeRequest req, Guid profileId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE employee_profiles SET
                weekly_norm_hours = @weeklyNormHours,
                part_time_fraction = @partTimeFraction,
                position = @position,
                version = version + 1,
                updated_at = NOW()
            WHERE profile_id = @profileId
            RETURNING profile_id, version
            """, conn, tx);
        cmd.Parameters.AddWithValue("profileId", profileId);
        cmd.Parameters.AddWithValue("weeklyNormHours", req.WeeklyNormHours);
        cmd.Parameters.AddWithValue("partTimeFraction", req.PartTimeFraction);
        cmd.Parameters.AddWithValue("position", (object?)req.Position ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"UpdateInPlaceAsync produced no row for profile_id='{profileId}'; " +
                "FOR UPDATE invariant violated.");
        }
        return (reader.GetGuid(0), reader.GetInt64(1));
    }

    /// <summary>
    /// Case C (Supersede) — close the predecessor by stamping
    /// <c>effective_to = closeDate</c> under end-exclusive semantics (ADR-018 D9 —
    /// predecessor's history window becomes <c>[predecessor.effective_from, closeDate)</c>).
    /// The version column is NOT bumped: close is a lifecycle event, not a content edit
    /// (mirrors S22 ArchiveProfileAsync + S29 WTM CloseRowAsync). Caller must already hold
    /// the row lock acquired via <see cref="AcquireLockAsync"/>.
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
    decimal WeeklyNormHours,
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
    decimal WeeklyNormHours,
    decimal PartTimeFraction,
    string? Position);

/// <summary>
/// S33 / TASK-3302 — payload for <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/>.
/// Extends <see cref="EmployeeProfileUpsertRequest"/>'s field set with the explicit
/// <see cref="EffectiveFrom"/> date that drives ADR-020 D2 3-case routing (same-day vs
/// cross-day vs net-new). The endpoint reads the clock per refinement Assumption #14 (no
/// clock dependency in the repo); seeders + admin-POST + admin-PUT supply the date
/// directly. Used to be a candidate for inheritance from
/// <see cref="EmployeeProfileUpsertRequest"/>, but records-with-inheritance complicates the
/// downstream <c>with</c>-expression ergonomics — flat record is the S29 WTM precedent shape.
/// </summary>
public sealed record EmployeeProfileSupersedeRequest(
    string EmployeeId,
    decimal WeeklyNormHours,
    decimal PartTimeFraction,
    string? Position,
    DateOnly EffectiveFrom);

/// <summary>
/// S33 / TASK-3302 — result of <see cref="EmployeeProfileRepository.SupersedeAndCreateAsync"/>.
/// <see cref="Outcome"/> discriminates which of the ADR-020 D2 3-case branches fired so the
/// endpoint can emit the correct event type — <c>EmployeeProfileCreated</c>,
/// <c>EmployeeProfileUpdated</c>, or <c>EmployeeProfileSuperseded</c> — and stamp the right
/// audit <c>action</c> column (CREATED / UPDATED / SUPERSEDED).
/// </summary>
/// <param name="ProfileId">The <c>profile_id</c> of the row this call produced. In Case A
/// (Created) and Case C (Superseded) this is a freshly-generated UUID for the new live row;
/// in Case B (Updated) it is the predecessor's unchanged <c>profile_id</c>.</param>
/// <param name="Version">The post-write <c>version</c> column value on the row identified
/// by <see cref="ProfileId"/>. Case A → 1; Case B → <c>prior + 1</c>; Case C → 1 (the new
/// live row starts at version 1; the closed predecessor's version is unchanged but is not
/// the row this result describes).</param>
/// <param name="Outcome">Which ADR-020 D2 branch the call routed through.</param>
public sealed record SaveEmployeeProfileResult(
    Guid ProfileId,
    long Version,
    SaveEmployeeProfileOutcome Outcome);

/// <summary>
/// S33 / TASK-3302 — ADR-020 D2 3-case routing discriminator. Read by TASK-3308 endpoint
/// cutover to map each case to its correct outbox event type:
/// <list type="bullet">
///   <item><description><see cref="Created"/> → <c>EmployeeProfileCreated</c> (net-new live row;
///     no predecessor existed).</description></item>
///   <item><description><see cref="Updated"/> → <c>EmployeeProfileUpdated</c> (same-day in-place
///     edit; predecessor's <c>effective_from</c> matched the request's, version bumped).</description></item>
///   <item><description><see cref="Superseded"/> → <c>EmployeeProfileSuperseded</c> (cross-day
///     supersession; predecessor closed at end-exclusive <c>effective_to</c>, new live row
///     at version 1).</description></item>
/// </list>
/// </summary>
public enum SaveEmployeeProfileOutcome
{
    /// <summary>Case A — no live row existed; INSERT produced a brand-new live profile row.</summary>
    Created,
    /// <summary>Case B — live row existed and its <c>effective_from</c> matched the request's;
    /// UPDATE-in-place with version bump (mapping_id and effective_from unchanged).</summary>
    Updated,
    /// <summary>Case C — live row existed at an earlier <c>effective_from</c>; predecessor
    /// closed at end-exclusive <c>effective_to = request.EffectiveFrom</c> (version unchanged),
    /// new live row inserted at version 1.</summary>
    Superseded,
}
