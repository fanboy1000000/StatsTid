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
        // S29 / TASK-2904: admin-list current-row-only filter (effective_to IS NULL).
        // History rows must not leak into the admin UI per ADR-020 Implications §8.
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM wage_type_mappings
            WHERE effective_to IS NULL
            ORDER BY agreement_code, ok_version, time_type, position
            """, conn);
        return await ReadMappingsAsync(cmd, ct);
    }

    public async Task<WageTypeMapping?> GetByKeyAsync(
        string timeType, string okVersion, string agreementCode, string position, CancellationToken ct = default)
    {
        // S29 / TASK-2904: admin natural-key probe — returns the currently-open row only
        // (effective_to IS NULL), at most one per natural key per idx_wtm_natural_key_open.
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM wage_type_mappings
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
              AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("timeType", timeType);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("position", position ?? "");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadMapping(reader) : null;
    }

    /// <summary>
    /// S29 / TASK-2904 — replay-deterministic dated read (ADR-020 D1 + Implications §7).
    /// Returns the row whose effective range <c>[effective_from, effective_to)</c> covers
    /// <paramref name="asOfDate"/>, or <c>null</c> if no row was effective at that date.
    ///
    /// <para>
    /// Replicates the <c>(position = @position OR position = '')</c> position-fallback semantic
    /// from <see cref="PayrollMappingService"/>'s current-row lookup (cycle 1 R-B1 absorption,
    /// LOCKED at refinement L62-86). When <paramref name="position"/> is non-empty, both the
    /// position-specific row AND the generic-fallback row are candidates — <c>ORDER BY (position = '') ASC</c>
    /// puts the position-specific row first; <c>LIMIT 1</c> picks it. When <paramref name="position"/>
    /// is empty, the predicate folds to <c>position = ''</c> only (same as forward-calc for
    /// generic-only lookup).
    /// </para>
    ///
    /// <para>
    /// Combined with the dated predicate (<c>effective_from &lt;= asOfDate AND
    /// (effective_to IS NULL OR effective_to &gt; asOfDate)</c>), the candidate set at
    /// <paramref name="asOfDate"/> mathematically matches what forward-calc consumed: any
    /// position-specific row added later (effective_from &gt; asOfDate) is filtered out, so
    /// the generic-fallback row that forward-calc actually used is what replay returns.
    /// </para>
    /// </summary>
    public async Task<WageTypeMapping?> GetByKeyAtAsync(
        string timeType, string okVersion, string agreementCode, string position,
        DateOnly asOfDate, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT time_type, wage_type, ok_version, agreement_code, position, description,
                   mapping_id, effective_from, effective_to, version
            FROM wage_type_mappings
            WHERE time_type = @timeType
              AND ok_version = @okVersion
              AND agreement_code = @agreementCode
              AND (position = @position OR position = '')
              AND effective_from <= @asOfDate
              AND (effective_to IS NULL OR effective_to > @asOfDate)
            ORDER BY (position = '') ASC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("timeType", timeType);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("position", position ?? "");
        cmd.Parameters.AddWithValue("asOfDate", asOfDate);
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
        // S29 / TASK-2904: bind effective-dating columns (mapping_id surrogate PK +
        // effective_from). mapping_id is generated client-side when Empty (mirrors
        // LocalAgreementProfileRepository.SupersedeAndCreateAsync L312 precedent);
        // effective_to is NULL (currently open) — at most one open row per natural
        // key per idx_wtm_natural_key_open.
        var newMappingId = mapping.MappingId == Guid.Empty ? Guid.NewGuid() : mapping.MappingId;
        var sql =
            """
            INSERT INTO wage_type_mappings (
                mapping_id, time_type, wage_type, ok_version, agreement_code, position,
                description, effective_from, effective_to)
            VALUES (
                @mappingId, @timeType, @wageType, @okVersion, @agreementCode, @position,
                @description, @effectiveFrom, NULL)
            """;
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("mappingId", newMappingId);
        cmd.Parameters.AddWithValue("timeType", mapping.TimeType);
        cmd.Parameters.AddWithValue("wageType", mapping.WageType);
        cmd.Parameters.AddWithValue("okVersion", mapping.OkVersion);
        cmd.Parameters.AddWithValue("agreementCode", mapping.AgreementCode);
        cmd.Parameters.AddWithValue("position", mapping.Position ?? "");
        cmd.Parameters.AddWithValue("description", (object?)mapping.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("effectiveFrom", mapping.EffectiveFrom);
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
    /// (ADR-019). Single PUT entry point for all mutations on the natural key.
    ///
    /// <para>
    /// S29 / TASK-2904: now a thin shim that delegates to
    /// <see cref="SupersedeAndCreateAsync"/> with
    /// <c>EffectiveFrom = predecessor.EffectiveFrom</c> for the same-day path. Refinement
    /// Assumption #15: dispatch logic (same-day vs cross-day) lives in the repo, not the
    /// endpoint. Mirrors S22 single-PUT-entry-point shape at
    /// <see cref="LocalAgreementProfileRepository.SupersedeAndCreateAsync"/>. Cross-day
    /// callers use <see cref="SupersedeAndCreateAsync"/> directly with their own
    /// <c>EffectiveFrom</c> (TASK-2908 PUT cross-day path).
    /// </para>
    /// </summary>
    /// <returns>
    /// <see cref="SaveWageTypeMappingResult"/> with the updated mapping (post-write snapshot),
    /// the new <c>version</c> (= prior version + 1), and <c>IsCreated: false</c>.
    /// </returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no currently-open row exists for the supplied natural key.
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
        // Lock the currently-open row to read its EffectiveFrom (drives the same-day vs
        // cross-day routing inside SupersedeAndCreateAsync) AND to surface a clean 404 when
        // there is no open row to update.
        var predecessor = await AcquireLockAsync(
            conn, tx, mapping.TimeType, mapping.OkVersion, mapping.AgreementCode, mapping.Position ?? "", ct);
        if (predecessor is null)
        {
            throw new KeyNotFoundException(
                $"Wage type mapping not found for (time_type='{mapping.TimeType}', " +
                $"ok_version='{mapping.OkVersion}', agreement_code='{mapping.AgreementCode}', " +
                $"position='{mapping.Position ?? string.Empty}').");
        }

        // v3 UpdateAsync surface = same-day-by-default. Cross-day callers go through
        // SupersedeAndCreateAsync directly with the new EffectiveFrom; the v3 PUT same-day
        // path (TASK-2908) keeps the existing endpoint shape. WageTypeMapping is a class
        // (not a record) so we object-init a fresh instance with the predecessor's
        // EffectiveFrom carried over.
        var sameDay = new WageTypeMapping
        {
            TimeType = mapping.TimeType,
            WageType = mapping.WageType,
            OkVersion = mapping.OkVersion,
            AgreementCode = mapping.AgreementCode,
            Position = mapping.Position ?? "",
            Description = mapping.Description,
            Version = mapping.Version,
            MappingId = predecessor.MappingId,
            EffectiveFrom = predecessor.EffectiveFrom,
            EffectiveTo = predecessor.EffectiveTo,
        };
        return await SupersedeAndCreateInternalAsync(
            conn, tx, sameDay, expectedVersion, predecessor, ct);
    }

    /// <summary>
    /// In-transaction supersession + create overload (ADR-020 D2 + S22 precedent at
    /// <see cref="LocalAgreementProfileRepository.SupersedeAndCreateAsync"/>). Routes the PUT
    /// flow on the caller-supplied <c>newMapping.EffectiveFrom</c>:
    /// <list type="bullet">
    ///   <item><description>Same-day (<c>newMapping.EffectiveFrom == predecessor.EffectiveFrom</c>):
    ///     in-place UPDATE on the open row, version bump (S22 D9 same-day pattern).</description></item>
    ///   <item><description>Cross-day (<c>newMapping.EffectiveFrom &gt; predecessor.EffectiveFrom</c>):
    ///     close the open row at <c>effective_to = newMapping.EffectiveFrom</c>, then INSERT
    ///     a new open row at <c>(effective_from = newMapping.EffectiveFrom, effective_to = NULL,
    ///     version = 1)</c>. Single tx; S25 ETag/If-Match contract preserved on the version
    ///     check against the predecessor.</description></item>
    /// </list>
    ///
    /// <para>
    /// No clock dependency in the repo (S22 precedent inherited): routing branches purely on
    /// caller-supplied dates. Endpoint layer reads the clock per refinement Assumption #14.
    /// </para>
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when no currently-open row exists for <paramref name="newMapping"/>'s natural key.
    /// </exception>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when the open row's <c>version</c> does not equal
    /// <paramref name="expectedCurrentVersion"/>.
    /// </exception>
    /// <exception cref="InvalidProfileSupersessionException">
    /// Thrown when <c>newMapping.EffectiveFrom &lt; predecessor.EffectiveFrom</c> (backdate
    /// rejected per ADR-018 D9 strict-less under end-exclusive).
    /// </exception>
    public async Task<SaveWageTypeMappingResult> SupersedeAndCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        WageTypeMapping newMapping, long? expectedCurrentVersion,
        CancellationToken ct = default)
    {
        var predecessor = await AcquireLockAsync(
            conn, tx, newMapping.TimeType, newMapping.OkVersion, newMapping.AgreementCode, newMapping.Position ?? "", ct);
        if (predecessor is null)
        {
            throw new KeyNotFoundException(
                $"Wage type mapping not found for (time_type='{newMapping.TimeType}', " +
                $"ok_version='{newMapping.OkVersion}', agreement_code='{newMapping.AgreementCode}', " +
                $"position='{newMapping.Position ?? string.Empty}').");
        }
        return await SupersedeAndCreateInternalAsync(
            conn, tx, newMapping, expectedCurrentVersion, predecessor, ct);
    }

    /// <summary>
    /// Internal core of the supersede-and-create flow. Caller has already AcquireLockAsync'd
    /// the predecessor and verified it exists. Validates the optimistic-concurrency token,
    /// rejects backdates, then routes same-day UPDATE-in-place vs cross-day close+insert.
    /// </summary>
    private static async Task<SaveWageTypeMappingResult> SupersedeAndCreateInternalAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        WageTypeMapping newMapping, long? expectedCurrentVersion,
        WageTypeMapping predecessor, CancellationToken ct)
    {
        // 1. Optimistic-concurrency check — caller's If-Match must match the stored version.
        if (expectedCurrentVersion is null || predecessor.Version != expectedCurrentVersion.Value)
        {
            throw new OptimisticConcurrencyException(
                $"Wage type mapping version is {predecessor.Version}, but caller sent " +
                $"If-Match: \"{expectedCurrentVersion?.ToString() ?? "<none>"}\"; refresh and retry.",
                expectedVersion: expectedCurrentVersion,
                actualVersion: predecessor.Version);
        }

        // 2. Backdate guard (ADR-018 D9 strict-less under end-exclusive). A new mapping
        //    cannot start before its predecessor — there is no valid history window for the
        //    predecessor in that case.
        if (newMapping.EffectiveFrom < predecessor.EffectiveFrom)
        {
            throw new InvalidProfileSupersessionException(
                $"Cannot supersede with effective_from {newMapping.EffectiveFrom:yyyy-MM-dd} " +
                $"earlier than predecessor's effective_from {predecessor.EffectiveFrom:yyyy-MM-dd}.");
        }

        // 3. Same-day edit (S22 D9 MODIFIED branch) — UPDATE-in-place with version bump.
        if (newMapping.EffectiveFrom == predecessor.EffectiveFrom)
        {
            return await UpdateInPlaceAsync(conn, tx, newMapping, predecessor.Version, ct);
        }

        // 4. Cross-day edit — close predecessor at end-exclusive newMapping.EffectiveFrom,
        //    then INSERT new open row at version 1.
        await CloseRowAsync(conn, tx, predecessor.MappingId, newMapping.EffectiveFrom, ct);

        var newMappingId = newMapping.MappingId == Guid.Empty ? Guid.NewGuid() : newMapping.MappingId;
        var inserted = await InsertSupersedingRowAsync(conn, tx, newMapping, newMappingId, ct);
        return new SaveWageTypeMappingResult(inserted, inserted.Version, IsCreated: false);
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
    /// S29 / TASK-2904 — soft-delete via <c>effective_to = closeDate</c> on the currently-open
    /// row (ADR-020 D2). Replaces the v3 hard <c>DeleteAsync</c> overload — replay determinism
    /// requires the closed row to remain queryable via <see cref="GetByKeyAtAsync"/> for any
    /// <c>asOfDate</c> within its prior effective range.
    ///
    /// <para>
    /// Reads the current open row under <c>SELECT ... FOR UPDATE</c>, validates
    /// <paramref name="expectedVersion"/> against the stored <c>version</c>, and stamps
    /// <c>effective_to = closeDate</c>. The version column is NOT bumped (S22 precedent at
    /// <see cref="LocalAgreementProfileRepository.ArchiveProfileAsync"/>: archive is a
    /// lifecycle close, not a content edit).
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>true</c> when exactly one row was closed; <c>false</c> when no open row was found at
    /// the supplied natural key (endpoint maps to 404).
    /// </returns>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when the row's <c>version</c> column does not equal
    /// <paramref name="expectedVersion"/>.
    /// </exception>
    public async Task<bool> SoftDeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string timeType, string okVersion, string agreementCode, string position,
        long expectedVersion, DateOnly closeDate,
        CancellationToken ct = default)
    {
        // 1. SELECT FOR UPDATE the open row (effective_to IS NULL) + version under the caller tx.
        long currentVersion;
        await using (var lockCmd = new NpgsqlCommand(
            """
            SELECT version FROM wage_type_mappings
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
              AND effective_to IS NULL
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
                // No open row → 404. Distinct from OptimisticConcurrencyException (412).
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

        // 3. Soft-close: stamp effective_to = closeDate on the (still-locked) open row.
        await using var closeCmd = new NpgsqlCommand(
            """
            UPDATE wage_type_mappings SET effective_to = @closeDate
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
              AND effective_to IS NULL
            """, conn, tx);
        closeCmd.Parameters.AddWithValue("closeDate", closeDate);
        closeCmd.Parameters.AddWithValue("timeType", timeType);
        closeCmd.Parameters.AddWithValue("okVersion", okVersion);
        closeCmd.Parameters.AddWithValue("agreementCode", agreementCode);
        closeCmd.Parameters.AddWithValue("position", position ?? "");
        var rows = await closeCmd.ExecuteNonQueryAsync(ct);
        if (rows == 0)
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"SoftDeleteAsync closed 0 rows for (time_type='{timeType}', ok_version='{okVersion}', " +
                $"agreement_code='{agreementCode}', position='{position ?? string.Empty}') at expected " +
                $"version {expectedVersion}; FOR UPDATE invariant violated.");
        }
        return true;
    }

    public async Task<IReadOnlyList<WageTypeMapping>> GetByAgreementAsync(
        string agreementCode, string okVersion, CancellationToken ct = default)
    {
        // S29 / TASK-2904: admin-list current-row-only filter (effective_to IS NULL) per
        // ADR-020 Implications §8 — history rows must not leak into the admin UI.
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM wage_type_mappings
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion
              AND effective_to IS NULL
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
        // S29 / TASK-2904: effective-dating columns (mapping_id surrogate PK + range bounds).
        MappingId = reader.GetGuid(reader.GetOrdinal("mapping_id")),
        EffectiveFrom = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_from")),
        EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
            ? null
            : reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_to")),
    };

    /// <summary>
    /// Locks the currently-open row (effective_to IS NULL) for the supplied natural key via
    /// <c>SELECT ... FOR UPDATE</c>. Returns the locked row as a full
    /// <see cref="WageTypeMapping"/>, or <c>null</c> when no open row exists. Concurrent
    /// writers attempting the same lock serialize here. Mirrors S22 precedent at
    /// <see cref="LocalAgreementProfileRepository.AcquireLockAsync"/>.
    /// </summary>
    private static async Task<WageTypeMapping?> AcquireLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string timeType, string okVersion, string agreementCode, string position,
        CancellationToken ct)
    {
        await using var lockCmd = new NpgsqlCommand(
            """
            SELECT * FROM wage_type_mappings
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
              AND effective_to IS NULL
            FOR UPDATE
            """, conn, tx);
        lockCmd.Parameters.AddWithValue("timeType", timeType);
        lockCmd.Parameters.AddWithValue("okVersion", okVersion);
        lockCmd.Parameters.AddWithValue("agreementCode", agreementCode);
        lockCmd.Parameters.AddWithValue("position", position ?? "");
        await using var reader = await lockCmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadMapping(reader) : null;
    }

    /// <summary>
    /// Same-day UPDATE-in-place path (S22 D9 MODIFIED branch). Updates wage_type +
    /// description, bumps version by one; mapping_id, effective_from, effective_to are
    /// immutable across in-place edits. Caller must already hold the row lock acquired via
    /// <see cref="AcquireLockAsync"/>.
    /// </summary>
    private static async Task<SaveWageTypeMappingResult> UpdateInPlaceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        WageTypeMapping mapping, long expectedVersion, CancellationToken ct)
    {
        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE wage_type_mappings SET
                wage_type = @wageType,
                description = @description,
                version = version + 1
            WHERE time_type = @timeType AND ok_version = @okVersion AND agreement_code = @agreementCode
              AND position = @position
              AND effective_to IS NULL
              AND version = @expectedVersion
            RETURNING *
            """, conn, tx);
        updateCmd.Parameters.AddWithValue("wageType", mapping.WageType);
        updateCmd.Parameters.AddWithValue("description", (object?)mapping.Description ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("timeType", mapping.TimeType);
        updateCmd.Parameters.AddWithValue("okVersion", mapping.OkVersion);
        updateCmd.Parameters.AddWithValue("agreementCode", mapping.AgreementCode);
        updateCmd.Parameters.AddWithValue("position", mapping.Position ?? "");
        updateCmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        await using var reader = await updateCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"UpdateInPlaceAsync produced no row for (time_type='{mapping.TimeType}', " +
                $"ok_version='{mapping.OkVersion}', agreement_code='{mapping.AgreementCode}', " +
                $"position='{mapping.Position ?? string.Empty}') at expected version {expectedVersion}; " +
                "FOR UPDATE invariant violated.");
        }
        var entity = ReadMapping(reader);
        return new SaveWageTypeMappingResult(entity, entity.Version, IsCreated: false);
    }

    /// <summary>
    /// Closes the supplied row by stamping <c>effective_to = closeDate</c> under end-exclusive
    /// semantics (ADR-018 D8 — predecessor's history window becomes
    /// <c>[predecessor.effective_from, closeDate)</c>). Caller must already hold the row lock.
    /// </summary>
    private static async Task CloseRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid mappingId, DateOnly closeDate, CancellationToken ct)
    {
        await using var closeCmd = new NpgsqlCommand(
            "UPDATE wage_type_mappings SET effective_to = @closeDate WHERE mapping_id = @mappingId",
            conn, tx);
        closeCmd.Parameters.AddWithValue("closeDate", closeDate);
        closeCmd.Parameters.AddWithValue("mappingId", mappingId);
        await closeCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts a new currently-open row (effective_to NULL) at version 1 with the supplied
    /// <paramref name="newMappingId"/>. Cross-day supersession path — caller has already
    /// closed the predecessor at the new row's <c>effective_from</c>. RETURNING * yields the
    /// post-write snapshot.
    /// </summary>
    private static async Task<WageTypeMapping> InsertSupersedingRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        WageTypeMapping newMapping, Guid newMappingId, CancellationToken ct)
    {
        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO wage_type_mappings (
                mapping_id, time_type, wage_type, ok_version, agreement_code, position,
                description, effective_from, effective_to, version)
            VALUES (
                @mappingId, @timeType, @wageType, @okVersion, @agreementCode, @position,
                @description, @effectiveFrom, NULL, 1)
            RETURNING *
            """, conn, tx);
        insertCmd.Parameters.AddWithValue("mappingId", newMappingId);
        insertCmd.Parameters.AddWithValue("timeType", newMapping.TimeType);
        insertCmd.Parameters.AddWithValue("wageType", newMapping.WageType);
        insertCmd.Parameters.AddWithValue("okVersion", newMapping.OkVersion);
        insertCmd.Parameters.AddWithValue("agreementCode", newMapping.AgreementCode);
        insertCmd.Parameters.AddWithValue("position", newMapping.Position ?? "");
        insertCmd.Parameters.AddWithValue("description", (object?)newMapping.Description ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("effectiveFrom", newMapping.EffectiveFrom);
        await using var reader = await insertCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — INSERT ... RETURNING * always yields one row on success.
            throw new InvalidOperationException(
                $"InsertSupersedingRowAsync produced no row for (time_type='{newMapping.TimeType}', " +
                $"ok_version='{newMapping.OkVersion}', agreement_code='{newMapping.AgreementCode}', " +
                $"position='{newMapping.Position ?? string.Empty}', " +
                $"effective_from='{newMapping.EffectiveFrom:yyyy-MM-dd}').");
        }
        return ReadMapping(reader);
    }
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
