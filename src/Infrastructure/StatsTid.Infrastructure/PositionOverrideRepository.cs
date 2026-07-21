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

    public async Task<Guid> CreateAsync(PositionOverrideConfigEntity entity, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteCreateAsync(conn, null, entity, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="CreateAsync(PositionOverrideConfigEntity, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> so the
    /// caller can extend the same transaction across audit + outbox writes
    /// (ADR-018 D3 transactional-outbox contract). The caller commits or rolls back.
    ///
    /// <para>
    /// Atomic-outbox primitive (S24 ForcedRollbackHarness consumer): preserved unchanged
    /// across the S25 / TASK-2504 v3 migration. Create endpoint writes version=1 (DB
    /// DEFAULT); the wire ETag for the 201 response is a static <c>"1"</c>.
    /// </para>
    /// </summary>
    public async Task<Guid> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        PositionOverrideConfigEntity entity, CancellationToken ct = default)
        => await ExecuteCreateAsync(conn, tx, entity, ct);

    /// <summary>
    /// The shared INSERT statement body (S118 / TASK-11800): both <see cref="ExecuteCreateAsync"/>
    /// and <see cref="CreateReturningAsync"/> build from this single const so the two paths can
    /// never drift column-wise. Timestamps stay DB-computed <c>NOW()</c> literals; <c>version</c>
    /// is deliberately ABSENT (DB DEFAULT 1) — byte-identical INSERT semantics on both paths.
    /// </summary>
    private const string InsertSql =
        """
        INSERT INTO position_override_configs (
            override_id, agreement_code, ok_version, position_code, status,
            max_flex_balance, flex_carryover_max, norm_period_weeks, weekly_norm_hours,
            created_by, description, created_at, updated_at
        ) VALUES (
            @overrideId, @agreementCode, @okVersion, @positionCode, 'ACTIVE',
            @maxFlexBalance, @flexCarryoverMax, @normPeriodWeeks, @weeklyNormHours,
            @createdBy, @description, NOW(), NOW()
        )
        """;

    private static async Task<Guid> ExecuteCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        PositionOverrideConfigEntity entity, CancellationToken ct)
    {
        var overrideId = Guid.NewGuid();
        await using var cmd = tx is null ? new NpgsqlCommand(InsertSql, conn) : new NpgsqlCommand(InsertSql, conn, tx);
        AddCreateParameters(cmd, overrideId, entity);
        await cmd.ExecuteNonQueryAsync(ct);
        return overrideId;
    }

    /// <summary>
    /// S118 / TASK-11800 (SPRINT-118 owner ruling #1, the dead-branch class): in-transaction
    /// RETURNING-ENTITY SIBLING of <see cref="CreateAsync(NpgsqlConnection, NpgsqlTransaction, PositionOverrideConfigEntity, CancellationToken)"/>.
    /// Same INSERT (the shared <see cref="InsertSql"/> const — DB-computed <c>NOW()</c>
    /// timestamps, <c>version</c> via DB DEFAULT 1, status hard-wired <c>'ACTIVE'</c>) with
    /// <c>RETURNING *</c> appended, hydrated through the existing <see cref="ReadEntity"/>
    /// reader mapper. The create endpoint's post-commit re-read (and its structurally-fallible
    /// <c>created is not null</c> fork) ceases to exist: the 201 body is ALWAYS the full
    /// entity, sourced inside the same transaction, BEFORE the audit/outbox/audit-projection
    /// appends (order unchanged — ADR-018 D3).
    ///
    /// <para>
    /// ALL pre-existing <c>CreateAsync</c> overloads stay SIGNATURE-IDENTICAL (Step-0b Codex
    /// W1 / Reviewer W1 — TxContractTests, the atomic suites, the seeder, and the concurrency
    /// suites consume them). The caller commits or rolls back; this method does NOT.
    /// </para>
    /// </summary>
    public async Task<PositionOverrideConfigEntity> CreateReturningAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        PositionOverrideConfigEntity entity, CancellationToken ct = default)
    {
        var overrideId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(InsertSql + "\nRETURNING *", conn, tx);
        AddCreateParameters(cmd, overrideId, entity);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — an INSERT ... RETURNING that inserted zero rows would have
            // thrown already; this is structurally unreachable.
            throw new InvalidOperationException(
                $"CreateReturningAsync produced no row for override_id={overrideId}; INSERT ... RETURNING invariant violated.");
        }
        return ReadEntity(reader);
    }

    private static void AddCreateParameters(
        NpgsqlCommand cmd, Guid overrideId, PositionOverrideConfigEntity entity)
    {
        cmd.Parameters.AddWithValue("overrideId", overrideId);
        cmd.Parameters.AddWithValue("agreementCode", entity.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", entity.OkVersion);
        cmd.Parameters.AddWithValue("positionCode", entity.PositionCode);
        cmd.Parameters.AddWithValue("maxFlexBalance", (object?)entity.MaxFlexBalance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("flexCarryoverMax", (object?)entity.FlexCarryoverMax ?? DBNull.Value);
        cmd.Parameters.AddWithValue("normPeriodWeeks", (object?)entity.NormPeriodWeeks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("weeklyNormHours", (object?)entity.WeeklyNormHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("createdBy", entity.CreatedBy);
        cmd.Parameters.AddWithValue("description", (object?)entity.Description ?? DBNull.Value);
    }

    public async Task<bool> UpdateAsync(Guid overrideId, PositionOverrideConfigEntity updated, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteSelfManagedUpdateAsync(conn, overrideId, updated, ct);
    }

    private static async Task<bool> ExecuteSelfManagedUpdateAsync(
        NpgsqlConnection conn,
        Guid overrideId, PositionOverrideConfigEntity updated, CancellationToken ct)
    {
        // Self-managed (no caller tx) — preserved unchanged from pre-S25; legacy callers
        // (seeders, internal tooling) continue to use this best-effort path. The v3
        // in-transaction sibling enforces ETag/If-Match optimistic concurrency for HTTP
        // admin endpoints.
        var sql =
            """
            UPDATE position_override_configs SET
                max_flex_balance = @maxFlexBalance,
                flex_carryover_max = @flexCarryoverMax,
                norm_period_weeks = @normPeriodWeeks,
                weekly_norm_hours = @weeklyNormHours,
                description = @description,
                updated_at = NOW()
            WHERE override_id = @overrideId AND status = 'ACTIVE'
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("overrideId", overrideId);
        cmd.Parameters.AddWithValue("maxFlexBalance", (object?)updated.MaxFlexBalance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("flexCarryoverMax", (object?)updated.FlexCarryoverMax ?? DBNull.Value);
        cmd.Parameters.AddWithValue("normPeriodWeeks", (object?)updated.NormPeriodWeeks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("weeklyNormHours", (object?)updated.WeeklyNormHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("description", (object?)updated.Description ?? DBNull.Value);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
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
    /// Replaces the v2 overload <c>(conn, tx, overrideId, updated, ct) → bool</c>. The caller
    /// commits or rolls back; this method does NOT.
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
    public async Task<SavePositionOverrideResult> UpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, long expectedVersion, PositionOverrideConfigEntity updated,
        CancellationToken ct = default)
    {
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

        // 4. UPDATE with version-bump in the same SET clause.
        var updateSql =
            """
            UPDATE position_override_configs SET
                max_flex_balance = @maxFlexBalance,
                flex_carryover_max = @flexCarryoverMax,
                norm_period_weeks = @normPeriodWeeks,
                weekly_norm_hours = @weeklyNormHours,
                description = @description,
                updated_at = NOW(),
                version = version + 1
            WHERE override_id = @overrideId AND status = 'ACTIVE'
            RETURNING *
            """;
        await using var updateCmd = new NpgsqlCommand(updateSql, conn, tx);
        updateCmd.Parameters.AddWithValue("overrideId", overrideId);
        updateCmd.Parameters.AddWithValue("maxFlexBalance", (object?)updated.MaxFlexBalance ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("flexCarryoverMax", (object?)updated.FlexCarryoverMax ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("normPeriodWeeks", (object?)updated.NormPeriodWeeks ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("weeklyNormHours", (object?)updated.WeeklyNormHours ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("description", (object?)updated.Description ?? DBNull.Value);
        await using var updReader = await updateCmd.ExecuteReaderAsync(ct);
        if (!await updReader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"UpdateAsync produced no row for override_id={overrideId} at expected version {expectedVersion}; FOR UPDATE invariant violated.");
        }
        var entity = ReadEntity(updReader);
        return new SavePositionOverrideResult(entity, entity.Version, IsCreated: false, Status: entity.Status);
    }

    public async Task<bool> DeactivateAsync(Guid overrideId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteSelfManagedDeactivateAsync(conn, overrideId, ct);
    }

    private static async Task<bool> ExecuteSelfManagedDeactivateAsync(
        NpgsqlConnection conn,
        Guid overrideId, CancellationToken ct)
    {
        // Self-managed path — preserved unchanged from pre-S25 (no version bump). Legacy
        // callers (internal tooling, test seeding) continue to use this best-effort path;
        // HTTP admin endpoints use the v3 sibling that enforces ETag/If-Match optimistic
        // concurrency.
        var sql =
            """
            UPDATE position_override_configs
            SET status = 'INACTIVE', updated_at = NOW()
            WHERE override_id = @overrideId AND status = 'ACTIVE'
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("overrideId", overrideId);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    /// <summary>
    /// In-transaction v3 deactivate overload — admin-strict ETag/If-Match optimistic-concurrency
    /// (ADR-019 pending). Reads the current row under <c>SELECT ... FOR UPDATE</c>, validates
    /// <paramref name="expectedVersion"/> against the stored <c>version</c>, and transitions
    /// status from ACTIVE → INACTIVE with <c>version = version + 1</c>.
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
    public async Task<SavePositionOverrideResult> DeactivateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, long expectedVersion, CancellationToken ct = default)
    {
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

        // 4. UPDATE with status='INACTIVE' + version-bump; RETURN the post-write snapshot.
        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE position_override_configs
            SET status = 'INACTIVE', updated_at = NOW(), version = version + 1
            WHERE override_id = @overrideId AND status = 'ACTIVE'
            RETURNING *
            """, conn, tx);
        updateCmd.Parameters.AddWithValue("overrideId", overrideId);
        await using var updReader = await updateCmd.ExecuteReaderAsync(ct);
        if (!await updReader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"DeactivateAsync produced no row for override_id={overrideId} at expected version {expectedVersion}; FOR UPDATE invariant violated.");
        }
        var entity = ReadEntity(updReader);
        return new SavePositionOverrideResult(entity, entity.Version, IsCreated: false, Status: entity.Status);
    }

    /// <summary>
    /// Self-managed overload: opens its own connection and an internal transaction for the
    /// "verify no other ACTIVE for the (agreement_code, ok_version, position_code) triple +
    /// activate" pair. For caller-driven atomic outbox + audit + activate (ADR-018 D3) call
    /// the in-transaction sibling
    /// <see cref="ActivateAsync(NpgsqlConnection, NpgsqlTransaction, Guid, long, CancellationToken)"/>.
    /// </summary>
    public async Task<bool> ActivateAsync(Guid overrideId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var success = await ExecuteSelfManagedActivateAsync(conn, tx, overrideId, ct);
            if (!success)
            {
                await tx.RollbackAsync(ct);
                return false;
            }
            await tx.CommitAsync(ct);
            return true;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<bool> ExecuteSelfManagedActivateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, CancellationToken ct)
    {
        // Self-managed path — preserved unchanged from pre-S25 (no version bump). Legacy
        // callers (internal tooling, test seeding) continue to use this best-effort path;
        // HTTP admin endpoints use the v3 sibling that enforces ETag/If-Match optimistic
        // concurrency + lets the partial-unique-index fire 23505 on concurrent activation
        // races (caught + mapped to 409 in the endpoint).
        await using (var getCmd = new NpgsqlCommand(
            "SELECT agreement_code, ok_version, position_code FROM position_override_configs WHERE override_id = @overrideId AND status = 'INACTIVE'",
            conn, tx))
        {
            getCmd.Parameters.AddWithValue("overrideId", overrideId);
            await using var reader = await getCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return false;

            var agreementCode = reader.GetString(0);
            var okVersion = reader.GetString(1);
            var positionCode = reader.GetString(2);
            await reader.CloseAsync();

            // Check if another ACTIVE override already exists for this (agreement_code, ok_version, position_code)
            await using var checkCmd = new NpgsqlCommand(
                """
                SELECT COUNT(*) FROM position_override_configs
                WHERE agreement_code = @agreementCode AND ok_version = @okVersion
                  AND position_code = @positionCode AND status = 'ACTIVE'
                """, conn, tx);
            checkCmd.Parameters.AddWithValue("agreementCode", agreementCode);
            checkCmd.Parameters.AddWithValue("okVersion", okVersion);
            checkCmd.Parameters.AddWithValue("positionCode", positionCode);
            var existingCount = (long)(await checkCmd.ExecuteScalarAsync(ct))!;

            if (existingCount > 0)
                return false;
        }

        // Activate the override
        await using var activateCmd = new NpgsqlCommand(
            """
            UPDATE position_override_configs
            SET status = 'ACTIVE', updated_at = NOW()
            WHERE override_id = @overrideId AND status = 'INACTIVE'
            """, conn, tx);
        activateCmd.Parameters.AddWithValue("overrideId", overrideId);
        var rows = await activateCmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
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
    public async Task<SavePositionOverrideResult> ActivateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, long expectedVersion, CancellationToken ct = default)
    {
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

        // 4. UPDATE with status='ACTIVE' + version-bump; RETURN the post-write snapshot.
        //    The partial-unique-index `WHERE status='ACTIVE'` may fire 23505 here on a
        //    concurrent sibling activation for the same (agreement, ok, position) triple
        //    — endpoint catches and maps to 409.
        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE position_override_configs
            SET status = 'ACTIVE', updated_at = NOW(), version = version + 1
            WHERE override_id = @overrideId AND status = 'INACTIVE'
            RETURNING *
            """, conn, tx);
        updateCmd.Parameters.AddWithValue("overrideId", overrideId);
        await using var updReader = await updateCmd.ExecuteReaderAsync(ct);
        if (!await updReader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"ActivateAsync produced no row for override_id={overrideId} at expected version {expectedVersion}; FOR UPDATE invariant violated.");
        }
        var entity = ReadEntity(updReader);
        return new SavePositionOverrideResult(entity, entity.Version, IsCreated: false, Status: entity.Status);
    }

    public async Task AppendAuditAsync(
        Guid overrideId, string action, string? previousData, string? newData,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO position_override_config_audit (override_id, action, previous_data, new_data, actor_id, actor_role)
            VALUES (@overrideId, @action, @previousData::jsonb, @newData::jsonb, @actorId, @actorRole)
            """, conn);
        AddAuditParameters(cmd, overrideId, action, previousData, newData, actorId, actorRole);
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
        FlexCarryoverMax = reader.IsDBNull(reader.GetOrdinal("flex_carryover_max")) ? null : reader.GetDecimal(reader.GetOrdinal("flex_carryover_max")),
        NormPeriodWeeks = reader.IsDBNull(reader.GetOrdinal("norm_period_weeks")) ? null : reader.GetInt32(reader.GetOrdinal("norm_period_weeks")),
        WeeklyNormHours = reader.IsDBNull(reader.GetOrdinal("weekly_norm_hours")) ? null : reader.GetDecimal(reader.GetOrdinal("weekly_norm_hours")),
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
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
