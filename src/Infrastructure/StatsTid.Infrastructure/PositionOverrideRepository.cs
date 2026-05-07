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
    /// </summary>
    public async Task<Guid> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        PositionOverrideConfigEntity entity, CancellationToken ct = default)
        => await ExecuteCreateAsync(conn, tx, entity, ct);

    private static async Task<Guid> ExecuteCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        PositionOverrideConfigEntity entity, CancellationToken ct)
    {
        var overrideId = Guid.NewGuid();
        var sql =
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
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
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
        await cmd.ExecuteNonQueryAsync(ct);
        return overrideId;
    }

    public async Task<bool> UpdateAsync(Guid overrideId, PositionOverrideConfigEntity updated, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteUpdateAsync(conn, null, overrideId, updated, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="UpdateAsync(Guid, PositionOverrideConfigEntity, CancellationToken)"/>.
    /// </summary>
    public async Task<bool> UpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, PositionOverrideConfigEntity updated, CancellationToken ct = default)
        => await ExecuteUpdateAsync(conn, tx, overrideId, updated, ct);

    private static async Task<bool> ExecuteUpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        Guid overrideId, PositionOverrideConfigEntity updated, CancellationToken ct)
    {
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
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("overrideId", overrideId);
        cmd.Parameters.AddWithValue("maxFlexBalance", (object?)updated.MaxFlexBalance ?? DBNull.Value);
        cmd.Parameters.AddWithValue("flexCarryoverMax", (object?)updated.FlexCarryoverMax ?? DBNull.Value);
        cmd.Parameters.AddWithValue("normPeriodWeeks", (object?)updated.NormPeriodWeeks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("weeklyNormHours", (object?)updated.WeeklyNormHours ?? DBNull.Value);
        cmd.Parameters.AddWithValue("description", (object?)updated.Description ?? DBNull.Value);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<bool> DeactivateAsync(Guid overrideId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteDeactivateAsync(conn, null, overrideId, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="DeactivateAsync(Guid, CancellationToken)"/>.
    /// </summary>
    public async Task<bool> DeactivateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, CancellationToken ct = default)
        => await ExecuteDeactivateAsync(conn, tx, overrideId, ct);

    private static async Task<bool> ExecuteDeactivateAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        Guid overrideId, CancellationToken ct)
    {
        var sql =
            """
            UPDATE position_override_configs
            SET status = 'INACTIVE', updated_at = NOW()
            WHERE override_id = @overrideId AND status = 'ACTIVE'
            """;
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("overrideId", overrideId);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    /// <summary>
    /// Self-managed overload: opens its own connection and an internal transaction for the
    /// "verify no other ACTIVE for the (agreement_code, ok_version, position_code) triple +
    /// activate" pair. For caller-driven atomic outbox + audit + activate (ADR-018 D3) call
    /// the in-transaction sibling
    /// <see cref="ActivateAsync(NpgsqlConnection, NpgsqlTransaction, Guid, CancellationToken)"/>.
    /// </summary>
    public async Task<bool> ActivateAsync(Guid overrideId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var success = await ActivateAsync(conn, tx, overrideId, ct);
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

    /// <summary>
    /// In-transaction sibling overload of <see cref="ActivateAsync(Guid, CancellationToken)"/>.
    /// Verifies no other ACTIVE override exists for the (agreement_code, ok_version,
    /// position_code) triple and activates the INACTIVE row identified by
    /// <paramref name="overrideId"/>. Returns false on any of: row missing / not INACTIVE,
    /// concurrent ACTIVE present, or zero-rows-updated by the activate UPDATE. Caller is
    /// responsible for rolling back on false return (the activate path runs no UPDATEs that
    /// would need reverting; the no-op short-circuits at the COUNT check).
    /// </summary>
    public async Task<bool> ActivateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid overrideId, CancellationToken ct = default)
    {
        // Get the override being activated to find its (agreement_code, ok_version, position_code)
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
    /// In-transaction sibling overload of
    /// <see cref="AppendAuditAsync(Guid, string, string?, string?, string, string, CancellationToken)"/>.
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
