using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class WageTypeMappingRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public WageTypeMappingRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<WageTypeMapping>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM wage_type_mappings ORDER BY agreement_code, ok_version, time_type, position", conn);
        return await ReadMappingsAsync(cmd, ct);
    }

    public async Task<WageTypeMapping?> GetByKeyAsync(
        string timeType, string okVersion, string agreementCode, string position, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM wage_type_mappings
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
            """, conn);
        cmd.Parameters.AddWithValue("timeType", timeType);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("position", position ?? "");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadMapping(reader) : null;
    }

    public async Task<bool> CreateAsync(WageTypeMapping mapping, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteCreateAsync(conn, null, mapping, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="CreateAsync(WageTypeMapping, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> so the
    /// caller can extend the same transaction across audit + outbox writes
    /// (ADR-018 D3 transactional-outbox contract). The caller commits or rolls back.
    ///
    /// <para>
    /// Atomic-outbox primitive (S24 ForcedRollbackHarness consumer): preserved unchanged
    /// across the S25 / TASK-2505 v3 migration. Create endpoint writes version=1 (DB
    /// DEFAULT); the wire ETag for the 201 response is a static <c>"1"</c>.
    /// </para>
    /// </summary>
    public async Task<bool> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        WageTypeMapping mapping, CancellationToken ct = default)
        => await ExecuteCreateAsync(conn, tx, mapping, ct);

    private static async Task<bool> ExecuteCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        WageTypeMapping mapping, CancellationToken ct)
    {
        var sql =
            """
            INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description)
            VALUES (@timeType, @wageType, @okVersion, @agreementCode, @position, @description)
            """;
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("timeType", mapping.TimeType);
        cmd.Parameters.AddWithValue("wageType", mapping.WageType);
        cmd.Parameters.AddWithValue("okVersion", mapping.OkVersion);
        cmd.Parameters.AddWithValue("agreementCode", mapping.AgreementCode);
        cmd.Parameters.AddWithValue("position", mapping.Position ?? "");
        cmd.Parameters.AddWithValue("description", (object?)mapping.Description ?? DBNull.Value);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<bool> UpdateAsync(WageTypeMapping mapping, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteSelfManagedUpdateAsync(conn, mapping, ct);
    }

    private static async Task<bool> ExecuteSelfManagedUpdateAsync(
        NpgsqlConnection conn,
        WageTypeMapping mapping, CancellationToken ct)
    {
        // Self-managed (no caller tx) — preserved unchanged from pre-S25; legacy callers
        // (seeders, internal tooling) continue to use this best-effort path. The v3
        // in-transaction sibling enforces ETag/If-Match optimistic concurrency for HTTP
        // admin endpoints.
        var sql =
            """
            UPDATE wage_type_mappings SET
                wage_type = @wageType,
                description = @description
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("wageType", mapping.WageType);
        cmd.Parameters.AddWithValue("description", (object?)mapping.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("timeType", mapping.TimeType);
        cmd.Parameters.AddWithValue("okVersion", mapping.OkVersion);
        cmd.Parameters.AddWithValue("agreementCode", mapping.AgreementCode);
        cmd.Parameters.AddWithValue("position", mapping.Position ?? "");
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    /// <summary>
    /// In-transaction v3 update overload — admin-strict ETag/If-Match optimistic-concurrency
    /// (ADR-019 pending, mirrors S22 ADR-018 D7 pattern). Reads the current row under
    /// <c>SELECT ... FOR UPDATE</c>, validates <paramref name="expectedVersion"/> against the
    /// stored <c>version</c>, and applies the UPDATE with <c>version = version + 1</c> in a
    /// single SET clause.
    ///
    /// <para>
    /// Replaces the v2 overload <c>(conn, tx, mapping, ct) → bool</c>. The caller commits or
    /// rolls back; this method does NOT.
    /// </para>
    /// </summary>
    /// <returns>
    /// <see cref="SaveWageTypeMappingResult"/> with the updated mapping (post-write snapshot),
    /// the new <c>version</c> (= prior version + 1), and <c>IsCreated: false</c>.
    /// </returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no row exists for the supplied composite key. Endpoint maps to 404.
    /// </exception>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when the row's <c>version</c> column does not equal
    /// <paramref name="expectedVersion"/>.
    /// </exception>
    public async Task<SaveWageTypeMappingResult> UpdateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        WageTypeMapping mapping, long expectedVersion,
        CancellationToken ct = default)
    {
        // 1. SELECT FOR UPDATE the target row + version under the caller tx so concurrent
        //    edits serialize on this lock.
        long currentVersion;
        await using (var lockCmd = new NpgsqlCommand(
            """
            SELECT version FROM wage_type_mappings
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
            FOR UPDATE
            """, conn, tx))
        {
            lockCmd.Parameters.AddWithValue("timeType", mapping.TimeType);
            lockCmd.Parameters.AddWithValue("okVersion", mapping.OkVersion);
            lockCmd.Parameters.AddWithValue("agreementCode", mapping.AgreementCode);
            lockCmd.Parameters.AddWithValue("position", mapping.Position ?? "");
            await using var reader = await lockCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // Row missing — surface as a missing-key error. Endpoint maps to 404 (the
                // composite key is part of the request body, so this is "not found", not a
                // concurrency mismatch).
                throw new KeyNotFoundException(
                    $"Wage type mapping not found for (time_type='{mapping.TimeType}', " +
                    $"ok_version='{mapping.OkVersion}', agreement_code='{mapping.AgreementCode}', " +
                    $"position='{mapping.Position ?? string.Empty}').");
            }
            currentVersion = reader.GetInt64(0);
        }

        // 2. Optimistic-concurrency check — caller's If-Match must match the stored version.
        if (currentVersion != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Wage type mapping version is {currentVersion}, but caller sent If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 3. UPDATE with version-bump in the same SET clause; RETURN the post-write snapshot.
        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE wage_type_mappings SET
                wage_type = @wageType,
                description = @description,
                version = version + 1
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
            RETURNING *
            """, conn, tx);
        updateCmd.Parameters.AddWithValue("wageType", mapping.WageType);
        updateCmd.Parameters.AddWithValue("description", (object?)mapping.Description ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("timeType", mapping.TimeType);
        updateCmd.Parameters.AddWithValue("okVersion", mapping.OkVersion);
        updateCmd.Parameters.AddWithValue("agreementCode", mapping.AgreementCode);
        updateCmd.Parameters.AddWithValue("position", mapping.Position ?? "");
        await using var updReader = await updateCmd.ExecuteReaderAsync(ct);
        if (!await updReader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"UpdateAsync produced no row for (time_type='{mapping.TimeType}', " +
                $"ok_version='{mapping.OkVersion}', agreement_code='{mapping.AgreementCode}', " +
                $"position='{mapping.Position ?? string.Empty}') at expected version {expectedVersion}; " +
                "FOR UPDATE invariant violated.");
        }
        var entity = ReadMapping(updReader);
        return new SaveWageTypeMappingResult(entity, entity.Version, IsCreated: false);
    }

    public async Task<bool> DeleteAsync(
        string timeType, string okVersion, string agreementCode, string position, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteSelfManagedDeleteAsync(conn, timeType, okVersion, agreementCode, position, ct);
    }

    private static async Task<bool> ExecuteSelfManagedDeleteAsync(
        NpgsqlConnection conn,
        string timeType, string okVersion, string agreementCode, string position, CancellationToken ct)
    {
        // Self-managed (no caller tx) — preserved unchanged from pre-S25; legacy callers
        // (seeders, internal tooling) continue to use this best-effort path. The v3
        // in-transaction sibling enforces ETag/If-Match optimistic concurrency for HTTP
        // admin endpoints.
        var sql =
            """
            DELETE FROM wage_type_mappings
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("timeType", timeType);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("position", position ?? "");
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    /// <summary>
    /// In-transaction v3 delete overload — admin-strict ETag/If-Match optimistic-concurrency
    /// (ADR-019 pending). Reads the current row under <c>SELECT ... FOR UPDATE</c>, validates
    /// <paramref name="expectedVersion"/> against the stored <c>version</c>, and deletes the
    /// row. NOT a <see cref="SaveWageTypeMappingResult"/> — there is no post-mutation entity
    /// to wrap once the row is gone (per SPRINT-25.md L301).
    ///
    /// <para>
    /// Replaces the v2 overload <c>(conn, tx, ..., ct) → bool</c>. The caller commits or
    /// rolls back; this method does NOT.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> when the row was deleted; <c>false</c> when the row was not found at the
    /// supplied composite key (endpoint maps to 404).
    /// </returns>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when the row's <c>version</c> column does not equal
    /// <paramref name="expectedVersion"/>.
    /// </exception>
    public async Task<bool> DeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string timeType, string okVersion, string agreementCode, string position,
        long expectedVersion,
        CancellationToken ct = default)
    {
        // 1. SELECT FOR UPDATE the target row + version under the caller tx.
        long currentVersion;
        await using (var lockCmd = new NpgsqlCommand(
            """
            SELECT version FROM wage_type_mappings
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
            FOR UPDATE
            """, conn, tx))
        {
            lockCmd.Parameters.AddWithValue("timeType", timeType);
            lockCmd.Parameters.AddWithValue("okVersion", okVersion);
            lockCmd.Parameters.AddWithValue("agreementCode", agreementCode);
            lockCmd.Parameters.AddWithValue("position", position ?? "");
            await using var reader = await lockCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // Row missing — surface as not-found (endpoint → 404). Distinct from
                // OptimisticConcurrencyException which surfaces as 412.
                return false;
            }
            currentVersion = reader.GetInt64(0);
        }

        // 2. Optimistic-concurrency check.
        if (currentVersion != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Wage type mapping version is {currentVersion}, but caller sent If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 3. DELETE the row.
        await using var deleteCmd = new NpgsqlCommand(
            """
            DELETE FROM wage_type_mappings
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
            """, conn, tx);
        deleteCmd.Parameters.AddWithValue("timeType", timeType);
        deleteCmd.Parameters.AddWithValue("okVersion", okVersion);
        deleteCmd.Parameters.AddWithValue("agreementCode", agreementCode);
        deleteCmd.Parameters.AddWithValue("position", position ?? "");
        var rows = await deleteCmd.ExecuteNonQueryAsync(ct);
        if (rows == 0)
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"DeleteAsync deleted 0 rows for (time_type='{timeType}', ok_version='{okVersion}', " +
                $"agreement_code='{agreementCode}', position='{position ?? string.Empty}') at expected " +
                $"version {expectedVersion}; FOR UPDATE invariant violated.");
        }
        return true;
    }

    public async Task<IReadOnlyList<WageTypeMapping>> GetByAgreementAsync(
        string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM wage_type_mappings
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion
            ORDER BY time_type, position
            """, conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        return await ReadMappingsAsync(cmd, ct);
    }

    public async Task AppendAuditAsync(
        string timeType, string okVersion, string agreementCode, string position,
        string action, string? previousData, string? newData,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mapping_audit (time_type, ok_version, agreement_code, position, action, previous_data, new_data, actor_id, actor_role)
            VALUES (@timeType, @okVersion, @agreementCode, @position, @action, @previousData::jsonb, @newData::jsonb, @actorId, @actorRole)
            """, conn);
        AddAuditParameters(cmd, timeType, okVersion, agreementCode, position, action, previousData, newData, actorId, actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction v2 audit overload (atomic-outbox primitive — preserved unchanged
    /// across the S25 / TASK-2505 v3 migration). Used by the Create endpoint and by S24
    /// ForcedRollbackHarness consumers; does NOT populate version_before / version_after
    /// (those columns are nullable per TASK-2501 schema migration). New mutating endpoints
    /// (Update / Delete) call the v3 sibling that captures the version transition.
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string timeType, string okVersion, string agreementCode, string position,
        string action, string? previousData, string? newData,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mapping_audit (time_type, ok_version, agreement_code, position, action, previous_data, new_data, actor_id, actor_role)
            VALUES (@timeType, @okVersion, @agreementCode, @position, @action, @previousData::jsonb, @newData::jsonb, @actorId, @actorRole)
            """, conn, tx);
        AddAuditParameters(cmd, timeType, okVersion, agreementCode, position, action, previousData, newData, actorId, actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction v3 audit overload (S25 / TASK-2505 + ADR-019 pending). Writes the
    /// version-transition pair (<paramref name="versionBefore"/>, <paramref name="versionAfter"/>)
    /// into the new <c>version_before</c> / <c>version_after</c> columns added by TASK-2501.
    /// Closes the audit-replay gap where the v2 audit captured *what* changed but not
    /// *which version transition produced this state*.
    ///
    /// <para>
    /// <paramref name="versionBefore"/> is nullable so first-create paths (POST /create) can
    /// pass <c>null</c> while UPDATE/DELETE paths pass the prior version. <paramref name="versionAfter"/>
    /// is the post-mutation version sourced from the v3 repo's
    /// <see cref="SaveWageTypeMappingResult.Version"/> on UPDATE; on DELETE the caller passes
    /// the pre-deletion version (the row is gone — there is no post-version).
    /// </para>
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string timeType, string okVersion, string agreementCode, string position,
        string action, string? previousData, string? newData,
        string actorId, string actorRole,
        long? versionBefore, long versionAfter,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mapping_audit
                (time_type, ok_version, agreement_code, position, action,
                 previous_data, new_data, actor_id, actor_role,
                 version_before, version_after)
            VALUES (@timeType, @okVersion, @agreementCode, @position, @action,
                    @previousData::jsonb, @newData::jsonb, @actorId, @actorRole,
                    @versionBefore, @versionAfter)
            """, conn, tx);
        AddAuditParameters(cmd, timeType, okVersion, agreementCode, position, action, previousData, newData, actorId, actorRole);
        cmd.Parameters.AddWithValue("versionBefore", (object?)versionBefore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("versionAfter", versionAfter);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddAuditParameters(
        NpgsqlCommand cmd,
        string timeType, string okVersion, string agreementCode, string position,
        string action, string? previousData, string? newData,
        string actorId, string actorRole)
    {
        cmd.Parameters.AddWithValue("timeType", timeType);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("position", position ?? "");
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("previousData", (object?)previousData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("newData", (object?)newData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
    }

    private static async Task<IReadOnlyList<WageTypeMapping>> ReadMappingsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var mappings = new List<WageTypeMapping>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            mappings.Add(ReadMapping(reader));
        return mappings;
    }

    private static WageTypeMapping ReadMapping(NpgsqlDataReader reader) => new()
    {
        TimeType = reader.GetString(reader.GetOrdinal("time_type")),
        WageType = reader.GetString(reader.GetOrdinal("wage_type")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        Position = reader.GetString(reader.GetOrdinal("position")),
        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
    };
}

/// <summary>
/// Result of a save operation on <see cref="WageTypeMappingRepository"/> (TASK-2502 / Phase 2
/// per-surface SaveResult — mirrors <c>SaveProfileResult</c> from
/// <see cref="LocalAgreementProfileRepository"/>). The S25 / TASK-2505 v3 mutating overload
/// (<see cref="WageTypeMappingRepository.UpdateAsync(NpgsqlConnection, NpgsqlTransaction, WageTypeMapping, long, CancellationToken)"/>)
/// returns this shape; endpoints set <c>ETag: "&lt;Version&gt;"</c> on the response.
/// </summary>
/// <param name="Mapping">The persisted wage-type-mapping (post-mutation snapshot).</param>
/// <param name="Version">The authoritative row-version after the save — first-insert is <c>1</c>;
/// each in-place UPDATE bumps by one. The wire ETag is <c>"&lt;version&gt;"</c> (RFC 7232 quoted)
/// per ADR-018 D7.</param>
/// <param name="IsCreated"><c>true</c> when this save inserted a new row (POST-style create);
/// <c>false</c> when it updated an existing row (PUT-style edit).</param>
public sealed record SaveWageTypeMappingResult(
    WageTypeMapping Mapping,
    long Version,
    bool IsCreated);
