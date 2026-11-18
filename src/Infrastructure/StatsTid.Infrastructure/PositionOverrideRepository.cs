using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class PositionOverrideRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public PositionOverrideRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<PositionOverrideConfigEntity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM position_override_configs ORDER BY agreement_code, ok_version, position_code", conn);
        return await ReadEntitiesAsync(cmd, ct);
    }

    public async Task<PositionOverrideConfigEntity?> GetByIdAsync(Guid overrideId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM position_override_configs WHERE override_id = @overrideId", conn);
        cmd.Parameters.AddWithValue("overrideId", overrideId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadEntity(reader) : null;
    }

    public async Task<PositionOverrideConfigEntity?> GetActiveAsync(
        string agreementCode, string okVersion, string positionCode, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM position_override_configs
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion
              AND position_code = @positionCode AND status = 'ACTIVE'
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("positionCode", positionCode);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadEntity(reader) : null;
    }

    public async Task<IReadOnlyList<PositionOverrideConfigEntity>> GetByAgreementAsync(
        string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM position_override_configs
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion
            ORDER BY position_code, status
            """, conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        return await ReadEntitiesAsync(cmd, ct);
    }

    /// <summary>
    /// The override in force for the key (<paramref name="agreementCode"/>, <paramref name="okVersion"/>,
    /// <paramref name="positionCode"/>) on <paramref name="day"/> (NR-P8, QUAL-182, ADR-043 D12): the key's
    /// newest version row on or before the day, across every override id of the key, ordered by
    /// <c>applies_from</c> then <c>recorded_at</c>, both descending (NR-P11; <c>version_id</c> breaks an exact
    /// tie). An ACTIVE version gives its values; an INACTIVE one, or no version, gives no override
    /// (<c>null</c>). The returned entity carries the head's identity, description, timestamps and ETag
    /// version, and the version row's state, values and date.
    /// </summary>
    public async Task<PositionOverrideConfigEntity?> GetInForceAsync(
        string agreementCode, string okVersion, string positionCode, DateOnly day, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT h.override_id, h.agreement_code, h.ok_version, h.position_code,
                   h.status AS status, h.max_flex_balance, h.norm_period_weeks, h.weekly_norm_hours,
                   h.created_by, h.created_at, h.updated_at, h.description, v.applies_from, h.version
            FROM position_override_config_versions v
            JOIN position_override_configs h ON h.override_id = v.override_id
            WHERE h.agreement_code = @agreementCode AND h.ok_version = @okVersion
              AND h.position_code = @positionCode
              AND @day IS NOT NULL
            ORDER BY v.applies_from DESC, v.recorded_at DESC, v.version_id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("positionCode", positionCode);
        cmd.Parameters.AddWithValue("day", day);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        var version = ReadEntity(reader);
        return string.Equals(version.Status, "ACTIVE", StringComparison.Ordinal) ? version : null;
    }

    /// <summary>
    /// The planner's read (QUAL-182; payroll's <c>PositionOverrideEffectiveDates</c>): every distinct
    /// <c>applies_from</c> of a version row (ACTIVE or INACTIVE) of the key inside
    /// <c>(afterExclusive, toInclusive]</c>, in date order. The lower bound is strict: a version dated the
    /// first day of the caller's run is that run's starting state, not a boundary.
    /// </summary>
    public async Task<IReadOnlyList<DateOnly>> GetVersionAppliesFromDatesAsync(
        string agreementCode, string okVersion, string positionCode,
        DateOnly afterExclusive, DateOnly toInclusive, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT DISTINCT v.applies_from
            FROM position_override_config_versions v
            JOIN position_override_configs h ON h.override_id = v.override_id
            WHERE h.agreement_code = @agreementCode AND h.ok_version = @okVersion
              AND h.position_code = @positionCode
              AND v.applies_from > @afterExclusive AND v.applies_from <= @toInclusive
            ORDER BY v.applies_from
            """, conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("positionCode", positionCode);
        cmd.Parameters.AddWithValue("afterExclusive", afterExclusive);
        cmd.Parameters.AddWithValue("toInclusive", toInclusive);
        var dates = new List<DateOnly>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dates.Add(reader.GetFieldValue<DateOnly>(0));
        return dates;
    }

    /// <summary>
    /// In-transaction create (ADR-018 D3 transactional-outbox contract): the caller extends the same
    /// transaction across audit + outbox writes and commits or rolls back. Writes the head row and its
    /// first version row, dated <paramref name="appliesFrom"/> (see <see cref="CreateReturningAsync"/>),
    /// and returns the new override id.
    /// </summary>
    public async Task<Guid> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        PositionOverrideConfigEntity entity, DateOnly appliesFrom, CancellationToken ct = default)
        => (await CreateReturningAsync(conn, tx, entity, appliesFrom, ct)).OverrideId;

    /// <summary>
    /// The shared INSERT statement body (S118 / TASK-11800): both create overloads write through it.
    /// Timestamps stay DB-computed <c>NOW()</c> literals; <c>version</c> is deliberately ABSENT
    /// (DB DEFAULT 1); status is hard-wired <c>'ACTIVE'</c>.
    /// </summary>
    private const string InsertSql =
        """
        INSERT INTO position_override_configs (
            override_id, agreement_code, ok_version, position_code, status,
            max_flex_balance, norm_period_weeks, weekly_norm_hours,
            applies_from, created_by, description, created_at, updated_at
        ) VALUES (
            @overrideId, @agreementCode, @okVersion, @positionCode, 'ACTIVE',
            @maxFlexBalance, @normPeriodWeeks, @weeklyNormHours,
            @appliesFrom, @createdBy, @description, NOW(), NOW()
        )
        RETURNING *
        """;

    /// <summary>
    /// S118 / TASK-11800 (owner ruling #1): the in-transaction create returning the full entity, sourced
    /// inside the same transaction BEFORE the caller's audit/outbox/audit-projection appends (ADR-018 D3).
    ///
    /// <para>
    /// A dated write (NR-P8, NR-P21; TASK-14818 items 4.4, 4.5, 7): the key lock first
    /// (<see cref="LockKeyAsync"/>: the key's advisory lock, then every head of the key <c>FOR UPDATE</c> in
    /// id order; ruling B4, S148 Step 5a ruling 1.1), then the date rules against the key's newest version
    /// (not before it; a Monday when the in-force weekly norm changes, the closed list's one override
    /// field), then the head row dated <paramref name="appliesFrom"/>, then its version row. The head's
    /// <c>applies_from</c> is always <paramref name="appliesFrom"/>, never the entity's member. Creates of a
    /// key take turns on the advisory lock even when the key has no head, so a later create's date rules see
    /// every committed version; one that passes them while another override of the key is ACTIVE is refused
    /// by the single-ACTIVE index (23505, the route's 409). The caller commits or rolls back; this method
    /// does NOT.
    /// </para>
    /// </summary>
    /// <exception cref="AppliesFromRefusedException">Thrown before anything is written.</exception>
    public async Task<PositionOverrideConfigEntity> CreateReturningAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        PositionOverrideConfigEntity entity, DateOnly appliesFrom, CancellationToken ct = default)
    {
        await LockKeyAsync(conn, tx, entity.AgreementCode, entity.OkVersion, entity.PositionCode, ct);
        await RequireDateRulesAsync(
            conn, tx, entity.AgreementCode, entity.OkVersion, entity.PositionCode,
            appliesFrom, newWeeklyNormHours: entity.WeeklyNormHours, ct);

        var overrideId = Guid.NewGuid();
        PositionOverrideConfigEntity created;
        await using (var cmd = new NpgsqlCommand(InsertSql, conn, tx))
        {
            AddCreateParameters(cmd, overrideId, entity, appliesFrom);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // Defense-in-depth — an INSERT ... RETURNING that inserted zero rows would have
                // thrown already; this is structurally unreachable.
                throw new InvalidOperationException(
                    $"CreateReturningAsync produced no row for override_id={overrideId}; INSERT ... RETURNING invariant violated.");
            }
            created = ReadEntity(reader);
        }

        await InsertVersionAsync(conn, tx, created, recordedBy: entity.CreatedBy, ct);
        return created;
    }

    /// <summary>
    /// Binds the create INSERT's parameters. <c>applies_from</c> is the explicit date the write supplies
    /// (<paramref name="appliesFrom"/>), never <see cref="PositionOverrideConfigEntity.AppliesFrom"/>: an
    /// unset member is <c>0001-01-01</c> and would make the column's missing DEFAULT a no-op.
    /// </summary>
    private static void AddCreateParameters(
        NpgsqlCommand cmd, Guid overrideId, PositionOverrideConfigEntity entity, DateOnly appliesFrom)
    {
        cmd.Parameters.AddWithValue("overrideId", overrideId);
        cmd.Parameters.AddWithValue("agreementCode", entity.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", entity.OkVersion);
        cmd.Parameters.AddWithValue("positionCode", entity.PositionCode);
        cmd.Parameters.AddWithValue("maxFlexBalance", (object?)entity.MaxFlexBalance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("normPeriodWeeks", (object?)entity.NormPeriodWeeks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("weeklyNormHours", (object?)entity.WeeklyNormHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("appliesFrom", appliesFrom);
        cmd.Parameters.AddWithValue("createdBy", entity.CreatedBy);
        cmd.Parameters.AddWithValue("description", (object?)entity.Description ?? DBNull.Value);
    }

    /// <summary>
    /// In-transaction v3 update overload — admin-strict ETag/If-Match optimistic-concurrency
    /// (ADR-019 pending, mirrors S22 ADR-018 D7 + S25 / TASK-2503 AgreementConfig v3 pattern).
    /// Reads the current row under <c>SELECT ... FOR UPDATE</c>, validates
    /// <paramref name="expectedVersion"/> against the stored <c>version</c>, and applies the
    /// UPDATE with <c>version = version + 1</c> in a single SET clause. Status must be ACTIVE
    /// — otherwise the call manifests as <see cref="OptimisticConcurrencyException"/> with the
    /// actual current state surfaced (the endpoint maps this to a 412 body).
    ///
    /// <para>
    /// A dated write (NR-P8, NR-P21): the key lock first (<see cref="LockKeyAsync"/>, ruling B4), then the
    /// status and version (412), then the date rules against the key's newest version, then the head row
    /// dated <paramref name="appliesFrom"/> and its version row (recorded by <paramref name="actorId"/>).
    /// The caller commits or rolls back; this method does NOT.
    /// </para>
    /// </summary>
    /// <returns>
    /// <see cref="SavePositionOverrideResult"/> with the updated entity (post-write snapshot),
    /// the new <c>version</c> (= prior version + 1), <c>IsCreated: false</c>, and
    /// <c>Status</c> = <c>"ACTIVE"</c> (Update only runs against ACTIVE rows).
    /// </returns>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when the row is missing, no longer ACTIVE, or its <c>version</c> column does
    /// not equal <paramref name="expectedVersion"/>.
    /// </exception>
    /// <exception cref="AppliesFromRefusedException">Thrown before anything is written.</exception>
    public async Task<SavePositionOverrideResult> UpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, long expectedVersion, PositionOverrideConfigEntity updated,
        DateOnly appliesFrom, string actorId,
        CancellationToken ct = default)
    {
        var key = await ReadKeyAndLockAsync(conn, tx, overrideId, expectedVersion, ct);

        // 1. SELECT FOR UPDATE — capture status + version under the caller tx (the row is already held by
        //    the key lock).
        long currentVersion;
        string currentStatus;
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT version, status FROM position_override_configs WHERE override_id = @overrideId FOR UPDATE",
            conn, tx))
        {
            lockCmd.Parameters.AddWithValue("overrideId", overrideId);
            await using var reader = await lockCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new OptimisticConcurrencyException(
                    $"Position override {overrideId} not found.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            currentVersion = reader.GetInt64(0);
            currentStatus = reader.GetString(1);
        }

        // 2. Status check — only ACTIVE rows are editable. Concurrent deactivate between
        //    endpoint pre-check and our FOR UPDATE → 412.
        if (!string.Equals(currentStatus, "ACTIVE", StringComparison.Ordinal))
        {
            throw new OptimisticConcurrencyException(
                $"Position override {overrideId} is no longer ACTIVE (current: {currentStatus}); refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 3. Optimistic-concurrency check.
        if (currentVersion != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Position override {overrideId} version is {currentVersion}, but caller sent If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 4. The date rules, under the key lock: the write leaves the override ACTIVE with the new weekly norm.
        await RequireDateRulesAsync(
            conn, tx, key.AgreementCode, key.OkVersion, key.PositionCode,
            appliesFrom, newWeeklyNormHours: updated.WeeklyNormHours, ct);

        // 5. UPDATE with version-bump in the same SET clause; the head takes the write's date.
        var updateSql =
            """
            UPDATE position_override_configs SET
                max_flex_balance = @maxFlexBalance,
                norm_period_weeks = @normPeriodWeeks,
                weekly_norm_hours = @weeklyNormHours,
                description = @description,
                applies_from = @appliesFrom,
                updated_at = NOW(),
                version = version + 1
            WHERE override_id = @overrideId AND status = 'ACTIVE'
            RETURNING *
            """;
        PositionOverrideConfigEntity entity;
        await using (var updateCmd = new NpgsqlCommand(updateSql, conn, tx))
        {
            updateCmd.Parameters.AddWithValue("overrideId", overrideId);
            updateCmd.Parameters.AddWithValue("maxFlexBalance", (object?)updated.MaxFlexBalance ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("normPeriodWeeks", (object?)updated.NormPeriodWeeks ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("weeklyNormHours", (object?)updated.WeeklyNormHours ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("description", (object?)updated.Description ?? DBNull.Value);
            updateCmd.Parameters.AddWithValue("appliesFrom", appliesFrom);
            await using var updReader = await updateCmd.ExecuteReaderAsync(ct);
            if (!await updReader.ReadAsync(ct))
            {
                // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
                throw new InvalidOperationException(
                    $"UpdateAsync produced no row for override_id={overrideId} at expected version {expectedVersion}; FOR UPDATE invariant violated.");
            }
            entity = ReadEntity(updReader);
        }

        // 6. The version row, after the head write, in the same transaction (NR-P21).
        await InsertVersionAsync(conn, tx, entity, recordedBy: actorId, ct);
        return new SavePositionOverrideResult(entity, entity.Version, IsCreated: false, Status: entity.Status);
    }

    /// <summary>
    /// In-transaction v3 deactivate overload — admin-strict ETag/If-Match optimistic-concurrency
    /// (ADR-019 pending). Reads the current row under <c>SELECT ... FOR UPDATE</c>, validates
    /// <paramref name="expectedVersion"/> against the stored <c>version</c>, and transitions
    /// status from ACTIVE → INACTIVE with <c>version = version + 1</c>.
    ///
    /// <para>
    /// A dated fact, "no override from <paramref name="appliesFrom"/>" (NR-P8): the key lock first, then
    /// status and version (412), then the date rules (switching off an override that sets the weekly norm
    /// changes the in-force norm, so it needs a Monday), then the head and its INACTIVE version row.
    /// </para>
    /// </summary>
    /// <returns>
    /// <see cref="SavePositionOverrideResult"/> with the deactivated entity (post-write
    /// snapshot), the new <c>version</c>, <c>IsCreated: false</c>, and
    /// <c>Status</c> = <c>"INACTIVE"</c>.
    /// </returns>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when the row is missing, already INACTIVE, or its <c>version</c> column does
    /// not equal <paramref name="expectedVersion"/>.
    /// </exception>
    /// <exception cref="AppliesFromRefusedException">Thrown before anything is written.</exception>
    public async Task<SavePositionOverrideResult> DeactivateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, long expectedVersion, DateOnly appliesFrom, string actorId,
        CancellationToken ct = default)
    {
        var key = await ReadKeyAndLockAsync(conn, tx, overrideId, expectedVersion, ct);

        // 1. SELECT FOR UPDATE — capture status + version under the caller tx.
        long currentVersion;
        string currentStatus;
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT version, status FROM position_override_configs WHERE override_id = @overrideId FOR UPDATE",
            conn, tx))
        {
            lockCmd.Parameters.AddWithValue("overrideId", overrideId);
            await using var reader = await lockCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new OptimisticConcurrencyException(
                    $"Position override {overrideId} not found.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            currentVersion = reader.GetInt64(0);
            currentStatus = reader.GetString(1);
        }

        // 2. Status check — only ACTIVE → INACTIVE is meaningful. Already-INACTIVE → 412.
        if (!string.Equals(currentStatus, "ACTIVE", StringComparison.Ordinal))
        {
            throw new OptimisticConcurrencyException(
                $"Position override {overrideId} is not ACTIVE (current: {currentStatus}); cannot deactivate — refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 3. Optimistic-concurrency check.
        if (currentVersion != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Position override {overrideId} version is {currentVersion}, but caller sent If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 4. The date rules, under the key lock: the write leaves no override, so no weekly norm.
        await RequireDateRulesAsync(
            conn, tx, key.AgreementCode, key.OkVersion, key.PositionCode,
            appliesFrom, newWeeklyNormHours: null, ct);

        // 5. UPDATE with status='INACTIVE' + version-bump + the write's date; RETURN the post-write snapshot.
        PositionOverrideConfigEntity entity;
        await using (var updateCmd = new NpgsqlCommand(
            """
            UPDATE position_override_configs
            SET status = 'INACTIVE', applies_from = @appliesFrom, updated_at = NOW(), version = version + 1
            WHERE override_id = @overrideId AND status = 'ACTIVE'
            RETURNING *
            """, conn, tx))
        {
            updateCmd.Parameters.AddWithValue("overrideId", overrideId);
            updateCmd.Parameters.AddWithValue("appliesFrom", appliesFrom);
            await using var updReader = await updateCmd.ExecuteReaderAsync(ct);
            if (!await updReader.ReadAsync(ct))
            {
                // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
                throw new InvalidOperationException(
                    $"DeactivateAsync produced no row for override_id={overrideId} at expected version {expectedVersion}; FOR UPDATE invariant violated.");
            }
            entity = ReadEntity(updReader);
        }

        // 6. The INACTIVE version row, after the head write, in the same transaction (NR-P21).
        await InsertVersionAsync(conn, tx, entity, recordedBy: actorId, ct);
        return new SavePositionOverrideResult(entity, entity.Version, IsCreated: false, Status: entity.Status);
    }

    /// <summary>
    /// In-transaction v3 activate overload — admin-strict ETag/If-Match optimistic-concurrency
    /// (ADR-019 pending). Transitions status from INACTIVE → ACTIVE on the row identified by
    /// <paramref name="overrideId"/> with <c>version = version + 1</c>. Reads the current row
    /// under <c>SELECT ... FOR UPDATE</c> and validates <paramref name="expectedVersion"/>.
    ///
    /// <para>
    /// Note: the partial-unique-index <c>WHERE status='ACTIVE'</c> enforces "at most one
    /// ACTIVE per (agreement_code, ok_version, position_code)". A concurrent activation of a
    /// sibling override for the same triple manifests as <see cref="PostgresException"/> with
    /// SQL state 23505 (unique violation) on the UPDATE — distinct from
    /// <see cref="OptimisticConcurrencyException"/>. The endpoint catches 23505 and maps it
    /// to 409 Conflict (different race class than row-version concurrency).
    /// </para>
    ///
    /// <para>
    /// A dated fact, "this override again from <paramref name="appliesFrom"/>" (NR-P8): the key lock first,
    /// then status and version (412), then the date rules (switching on an override that sets the weekly
    /// norm changes the in-force norm, so it needs a Monday), then the head and its ACTIVE version row.
    /// </para>
    /// </summary>
    /// <returns>
    /// <see cref="SavePositionOverrideResult"/> with the activated entity (post-write
    /// snapshot), the new <c>version</c>, <c>IsCreated: false</c>, and
    /// <c>Status</c> = <c>"ACTIVE"</c>.
    /// </returns>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when the row is missing, not INACTIVE, or its <c>version</c> column does not
    /// equal <paramref name="expectedVersion"/>.
    /// </exception>
    /// <exception cref="AppliesFromRefusedException">Thrown before anything is written.</exception>
    public async Task<SavePositionOverrideResult> ActivateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, long expectedVersion, DateOnly appliesFrom, string actorId,
        CancellationToken ct = default)
    {
        var key = await ReadKeyAndLockAsync(conn, tx, overrideId, expectedVersion, ct);

        // 1. SELECT FOR UPDATE — capture status + version, and the weekly norm the activation brings back,
        //    under the caller tx.
        long currentVersion;
        string currentStatus;
        decimal? weeklyNormHours;
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT version, status, weekly_norm_hours FROM position_override_configs WHERE override_id = @overrideId FOR UPDATE",
            conn, tx))
        {
            lockCmd.Parameters.AddWithValue("overrideId", overrideId);
            await using var reader = await lockCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new OptimisticConcurrencyException(
                    $"Position override {overrideId} not found.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            currentVersion = reader.GetInt64(0);
            currentStatus = reader.GetString(1);
            weeklyNormHours = reader.IsDBNull(2) ? null : reader.GetDecimal(2);
        }

        // 2. Status check — only INACTIVE → ACTIVE is meaningful. Already-ACTIVE → 412.
        if (!string.Equals(currentStatus, "INACTIVE", StringComparison.Ordinal))
        {
            throw new OptimisticConcurrencyException(
                $"Position override {overrideId} is not INACTIVE (current: {currentStatus}); cannot activate — refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 3. Optimistic-concurrency check.
        if (currentVersion != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Position override {overrideId} version is {currentVersion}, but caller sent If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 4. The date rules, under the key lock: the write leaves this override ACTIVE with its weekly norm.
        await RequireDateRulesAsync(
            conn, tx, key.AgreementCode, key.OkVersion, key.PositionCode,
            appliesFrom, newWeeklyNormHours: weeklyNormHours, ct);

        // 5. UPDATE with status='ACTIVE' + version-bump + the write's date; RETURN the post-write snapshot.
        //    The partial-unique-index `WHERE status='ACTIVE'` may fire 23505 here when another
        //    override of the same (agreement, ok, position) triple is ACTIVE — the endpoint
        //    catches and maps to 409.
        PositionOverrideConfigEntity entity;
        await using (var updateCmd = new NpgsqlCommand(
            """
            UPDATE position_override_configs
            SET status = 'ACTIVE', applies_from = @appliesFrom, updated_at = NOW(), version = version + 1
            WHERE override_id = @overrideId AND status = 'INACTIVE'
            RETURNING *
            """, conn, tx))
        {
            updateCmd.Parameters.AddWithValue("overrideId", overrideId);
            updateCmd.Parameters.AddWithValue("appliesFrom", appliesFrom);
            await using var updReader = await updateCmd.ExecuteReaderAsync(ct);
            if (!await updReader.ReadAsync(ct))
            {
                // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
                throw new InvalidOperationException(
                    $"ActivateAsync produced no row for override_id={overrideId} at expected version {expectedVersion}; FOR UPDATE invariant violated.");
            }
            entity = ReadEntity(updReader);
        }

        // 6. The ACTIVE version row, after the head write, in the same transaction (NR-P21).
        await InsertVersionAsync(conn, tx, entity, recordedBy: actorId, ct);
        return new SavePositionOverrideResult(entity, entity.Version, IsCreated: false, Status: entity.Status);
    }

    /// <summary>
    /// The key of an existing override, read without a lock (a head's agreement_code, ok_version and
    /// position_code never change, SEC-034), then the key lock. A missing head is the 412 it always was.
    /// </summary>
    private static async Task<(string AgreementCode, string OkVersion, string PositionCode)> ReadKeyAndLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid overrideId, long expectedVersion, CancellationToken ct)
    {
        (string AgreementCode, string OkVersion, string PositionCode) key;
        await using (var keyCmd = new NpgsqlCommand(
            "SELECT agreement_code, ok_version, position_code FROM position_override_configs WHERE override_id = @overrideId",
            conn, tx))
        {
            keyCmd.Parameters.AddWithValue("overrideId", overrideId);
            await using var reader = await keyCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new OptimisticConcurrencyException(
                    $"Position override {overrideId} not found.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            key = (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        }

        await LockKeyAsync(conn, tx, key.AgreementCode, key.OkVersion, key.PositionCode, ct);
        return key;
    }

    /// <summary>
    /// The key lock of (agreement, OK version, position) (TASK-14818 item 4.4, ruling B4; S148 Step 5a
    /// ruling 1.1), an override write's first lock, taken by all four writers. Two statements, in order:
    /// <list type="number">
    /// <item>A transaction-scoped advisory lock named after the key,
    /// <c>hashtext('position-override-' || agreement || '|' || OK version || '|' || position)</c>, in the
    /// single-key space (never the two-integer space). It exists even when the key has no row, so the first
    /// writers of a key take turns too; a row lock alone would let a create that passed the date rules
    /// commit after a later-dated create and switch-off, leaving a version dated before committed ones.
    /// It is a statement of its own, so each statement after it, at READ COMMITTED, takes a fresh snapshot
    /// and sees the write the waiter waited for.</item>
    /// <item>Every head of the key <c>FOR UPDATE</c>, in id order (the rows the write then changes; every
    /// writer takes them in the same order, so they cannot deadlock).</item>
    /// </list>
    /// <para>
    /// The lock order of an override write (Block C, PLAN-s148, ruling 1.1): (1) the pay-inputs lock
    /// (148, 1) at the route head, TASK-14823's, which these routes do not take yet; (2) this advisory lock;
    /// (3) the heads <c>FOR UPDATE</c>; (4) the head write, then the version row; (5) the audit, outbox and
    /// projection rows, then TASK-14823's worklist rows. Only override writers take the
    /// <c>position-override-</c> lock, and a transaction holding it takes no other advisory lock afterwards,
    /// so a 32-bit hash collision with another lock family costs a wait, never a deadlock.
    /// </para>
    /// </summary>
    private static async Task LockKeyAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string agreementCode, string okVersion, string positionCode, CancellationToken ct)
    {
        await using (var advisoryCmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('position-override-' || @agreementCode || '|' || @okVersion || '|' || @positionCode))",
            conn, tx))
        {
            advisoryCmd.Parameters.AddWithValue("agreementCode", agreementCode);
            advisoryCmd.Parameters.AddWithValue("okVersion", okVersion);
            advisoryCmd.Parameters.AddWithValue("positionCode", positionCode);
            await advisoryCmd.ExecuteScalarAsync(ct);
        }

        await using var cmd = new NpgsqlCommand(
            """
            SELECT override_id FROM position_override_configs
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion AND position_code = @positionCode
            ORDER BY override_id
            FOR UPDATE
            """, conn, tx);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("positionCode", positionCode);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            // Each row fetched is locked; reading them all takes the whole key.
        }
    }

    /// <summary>
    /// The date rules of an override write, decided under the key lock against the key's newest version
    /// row, across every override id of the key (NR-P8; TASK-14818 items 4.4, 4.5): the new date may not
    /// precede it (an equal date is accepted, the later write then wins), and when the in-force weekly
    /// norm changes the date must be a Monday. The base is that newest version's state: ACTIVE gives its
    /// weekly norm; INACTIVE, or no version at all, counts as "no value" (B3), so creating, switching off or
    /// switching on an override that sets the weekly norm is a change. <c>WeeklyNormHours</c> is the closed
    /// list's only override field; <c>MaxFlexBalance</c> and <c>NormPeriodWeeks</c> change on any weekday.
    /// </summary>
    /// <param name="newWeeklyNormHours">The weekly norm the write leaves in force: the override's value when
    /// it leaves it ACTIVE, <c>null</c> when it leaves no override.</param>
    private static async Task RequireDateRulesAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string agreementCode, string okVersion, string positionCode,
        DateOnly appliesFrom, decimal? newWeeklyNormHours, CancellationToken ct)
    {
        DateOnly? newestDate = null;
        decimal? baseWeeklyNormHours = null;
        await using (var cmd = new NpgsqlCommand(
            """
            SELECT v.applies_from, v.state, v.weekly_norm_hours
            FROM position_override_config_versions v
            JOIN position_override_configs h ON h.override_id = v.override_id
            WHERE h.agreement_code = @agreementCode AND h.ok_version = @okVersion
              AND h.position_code = @positionCode
            ORDER BY v.applies_from DESC, v.recorded_at DESC, v.version_id DESC
            LIMIT 1
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("agreementCode", agreementCode);
            cmd.Parameters.AddWithValue("okVersion", okVersion);
            cmd.Parameters.AddWithValue("positionCode", positionCode);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                newestDate = reader.GetFieldValue<DateOnly>(0);
                var active = string.Equals(reader.GetString(1), "ACTIVE", StringComparison.Ordinal);
                baseWeeklyNormHours = active && !reader.IsDBNull(2) ? reader.GetDecimal(2) : null;
            }
        }

        if (newestDate is { } predecessorDate)
            AppliesFromRules.RequireNotBeforePredecessor(appliesFrom, predecessorDate);

        if (baseWeeklyNormHours != newWeeklyNormHours)
        {
            AppliesFromRules.RequireMondayForChangedFields(
                appliesFrom, new[] { nameof(PositionOverrideConfigEntity.WeeklyNormHours) });
        }
    }

    /// <summary>
    /// The version row of a write (NR-P8, NR-P21), inserted by the writer itself after its head write, in
    /// the same transaction, from the head as written: so the head always equals the key's newest version.
    /// <c>recorded_at</c> is never supplied: its column default, <c>clock_timestamp()</c>, stamps the moment
    /// of the insert, after the key lock (NR-P16).
    /// </summary>
    private static async Task InsertVersionAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, PositionOverrideConfigEntity head, string recordedBy,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO position_override_config_versions
                (override_id, applies_from, state, max_flex_balance, norm_period_weeks, weekly_norm_hours, recorded_by)
            VALUES
                (@overrideId, @appliesFrom, @state, @maxFlexBalance, @normPeriodWeeks, @weeklyNormHours, @recordedBy)
            """, conn, tx);
        cmd.Parameters.AddWithValue("overrideId", head.OverrideId);
        cmd.Parameters.AddWithValue("appliesFrom", head.AppliesFrom);
        cmd.Parameters.AddWithValue("state", head.Status);
        cmd.Parameters.AddWithValue("maxFlexBalance", (object?)head.MaxFlexBalance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("normPeriodWeeks", (object?)head.NormPeriodWeeks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("weeklyNormHours", (object?)head.WeeklyNormHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("recordedBy", recordedBy);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction v2 audit overload (atomic-outbox primitive — preserved unchanged
    /// across the S25 / TASK-2504 v3 migration). Used by the Create endpoint and by S24
    /// ForcedRollbackHarness consumers; does NOT populate version_before / version_after
    /// (those columns are nullable per TASK-2501 schema migration). New mutating endpoints
    /// (Update / Activate / Deactivate) call the v3 sibling that captures the version
    /// transition.
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, string action, string? previousData, string? newData,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO position_override_config_audit (override_id, action, previous_data, new_data, actor_id, actor_role)
            VALUES (@overrideId, @action, @previousData::jsonb, @newData::jsonb, @actorId, @actorRole)
            """, conn, tx);
        AddAuditParameters(cmd, overrideId, action, previousData, newData, actorId, actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction v3 audit overload (S25 / TASK-2504 + ADR-019 pending). Writes the
    /// version-transition pair (<paramref name="versionBefore"/>, <paramref name="versionAfter"/>)
    /// into the new <c>version_before</c> / <c>version_after</c> columns added by TASK-2501.
    /// Closes the audit-replay gap where the v2 audit captured *what* changed but not
    /// *which version transition produced this state*.
    ///
    /// <para>
    /// <paramref name="versionBefore"/> is nullable so first-create paths (POST /create) can
    /// pass <c>null</c> while UPDATE / Activate / Deactivate paths pass the prior version.
    /// <paramref name="versionAfter"/> is the post-mutation version sourced from the v3
    /// repo's <see cref="SavePositionOverrideResult.Version"/>.
    /// </para>
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, string action, string? previousData, string? newData,
        string actorId, string actorRole,
        long? versionBefore, long versionAfter,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO position_override_config_audit
                (override_id, action, previous_data, new_data, actor_id, actor_role,
                 version_before, version_after)
            VALUES (@overrideId, @action, @previousData::jsonb, @newData::jsonb, @actorId, @actorRole,
                    @versionBefore, @versionAfter)
            """, conn, tx);
        AddAuditParameters(cmd, overrideId, action, previousData, newData, actorId, actorRole);
        cmd.Parameters.AddWithValue("versionBefore", (object?)versionBefore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("versionAfter", versionAfter);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddAuditParameters(
        NpgsqlCommand cmd, Guid overrideId, string action, string? previousData, string? newData,
        string actorId, string actorRole)
    {
        cmd.Parameters.AddWithValue("overrideId", overrideId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("previousData", (object?)previousData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("newData", (object?)newData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
    }

    private static async Task<IReadOnlyList<PositionOverrideConfigEntity>> ReadEntitiesAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var entities = new List<PositionOverrideConfigEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            entities.Add(ReadEntity(reader));
        return entities;
    }

    private static PositionOverrideConfigEntity ReadEntity(NpgsqlDataReader reader) => new()
    {
        OverrideId = reader.GetGuid(reader.GetOrdinal("override_id")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        PositionCode = reader.GetString(reader.GetOrdinal("position_code")),
        Status = reader.GetString(reader.GetOrdinal("status")),
        MaxFlexBalance = reader.IsDBNull(reader.GetOrdinal("max_flex_balance")) ? null : reader.GetDecimal(reader.GetOrdinal("max_flex_balance")),
        NormPeriodWeeks = reader.IsDBNull(reader.GetOrdinal("norm_period_weeks")) ? null : reader.GetInt32(reader.GetOrdinal("norm_period_weeks")),
        WeeklyNormHours = reader.IsDBNull(reader.GetOrdinal("weekly_norm_hours")) ? null : reader.GetDecimal(reader.GetOrdinal("weekly_norm_hours")),
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
        AppliesFrom = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("applies_from")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
    };
}

/// <summary>
/// Result of a save operation on <see cref="PositionOverrideRepository"/> (TASK-2502 / Phase 2
/// per-surface SaveResult — mirrors <c>SaveProfileResult</c> from
/// <see cref="LocalAgreementProfileRepository"/>). Phase 2 repo work (TASK-2504) wires the
/// repository to return this shape from its Save / state-transition paths; Phase 3 endpoint
/// migration consumes the post-mutation <see cref="Version"/> for the ETag response header and
/// the <see cref="Status"/> for the response payload.
/// </summary>
/// <param name="Override">The persisted position-override entity (post-mutation snapshot).</param>
/// <param name="Version">The authoritative row-version after the save — first-insert is <c>1</c>;
/// each in-place UPDATE bumps by one. The wire ETag is <c>"&lt;version&gt;"</c> (RFC 7232 quoted)
/// per ADR-018 D7.</param>
/// <param name="IsCreated"><c>true</c> when this save inserted a new row (POST-style create);
/// <c>false</c> when it updated an existing row (PUT-style edit / state transition).</param>
/// <param name="Status">The post-mutation status of the override (<c>ACTIVE</c> /
/// <c>INACTIVE</c>) — surfaced for state-transition responses (activate/deactivate flows) so
/// the endpoint can compose the response payload without re-reading from the DB.</param>
public sealed record SavePositionOverrideResult(
    PositionOverrideConfigEntity Override,
    long Version,
    bool IsCreated,
    string Status);
