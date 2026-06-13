using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class EntitlementConfigRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public EntitlementConfigRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    // ------------------------------------------------------------------
    // Legacy reads — preserved unchanged for existing callers (SkemaEndpoints.cs:313,
    // BalanceEndpoints.cs:120). TASK-3008 migrates these to GetByTypeAtAsync /
    // GetByAgreementAtAsync; until then the legacy methods route at the natural key with
    // no effective-date filter. Note: after the S30 schema migration there may be multiple
    // history rows per natural key, so these legacy methods can match a closed (superseded)
    // row. Phase 4d-2 contract: ALL non-trivial reads MUST go through the dated overloads.
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<EntitlementConfig>> GetByAgreementAsync(
        string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_configs WHERE agreement_code = @agreementCode AND ok_version = @okVersion ORDER BY entitlement_type",
            conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        return await ReadConfigsAsync(cmd, ct);
    }

    /// <summary>
    /// Returns all currently-open entitlement config rows (<c>effective_to IS NULL</c>) for the
    /// supplied (agreement_code, ok_version) pair, ordered by entitlement_type. Used by the
    /// agreement-config GET by-ID endpoint to inline entitlements in the response (Phase 5
    /// frontend integration). Mirrors <see cref="GetAllAsync"/> open-row filter but scoped to a
    /// single agreement+version pair (at most 5 rows: VACATION / SPECIAL_HOLIDAY / CARE_DAY /
    /// CHILD_SICK / SENIOR_DAY).
    /// </summary>
    public async Task<IReadOnlyList<EntitlementConfig>> GetOpenByAgreementAsync(
        string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM entitlement_configs
            WHERE agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_to IS NULL
            ORDER BY entitlement_type
            """, conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        return await ReadConfigsAsync(cmd, ct);
    }

    public async Task<EntitlementConfig?> GetByTypeAsync(
        string entitlementType, string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_configs WHERE entitlement_type = @entitlementType AND agreement_code = @agreementCode AND ok_version = @okVersion",
            conn);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConfig(reader) : null;
    }

    public async Task<IReadOnlyList<EntitlementConfig>> GetAllAsync(CancellationToken ct = default)
    {
        // S30 / TASK-3003: admin-list current-row-only filter (effective_to IS NULL).
        // History rows must not leak into the admin UI per ADR-021 D2 (S29 WTM precedent).
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM entitlement_configs
            WHERE effective_to IS NULL
            ORDER BY agreement_code, ok_version, entitlement_type
            """, conn);
        return await ReadConfigsAsync(cmd, ct);
    }

    // ------------------------------------------------------------------
    // S30 / TASK-3003 — dated reads (ADR-021 D2 + ADR-016 D5b "fifth pattern":
    // export-time / consumption-time effective-date lookup). Replay-deterministic:
    // returns the row whose [effective_from, effective_to) covers asOfDate.
    // ------------------------------------------------------------------

    /// <summary>
    /// S30 / TASK-3003 — replay-deterministic dated read (ADR-021 D2 + ADR-016 D5b).
    /// Returns the row whose effective range <c>[effective_from, effective_to)</c> covers
    /// <paramref name="asOfDate"/>, or <c>null</c> if no row was effective at that date.
    /// Mirrors <see cref="WageTypeMappingRepository.GetByKeyAtAsync"/> shape.
    /// </summary>
    public async Task<EntitlementConfig?> GetByTypeAtAsync(
        string entitlementType, string agreementCode, string okVersion,
        DateOnly asOfDate, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteGetByTypeAtAsync(conn, null, entitlementType, agreementCode, okVersion, asOfDate, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of
    /// <see cref="GetByTypeAtAsync(string, string, string, DateOnly, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> so the
    /// caller can read inside the same transaction as a downstream write (ADR-018 D3/D5
    /// atomic-outbox contract). Used by the two-step consumption pattern (TASK-3008) when
    /// a quota check + balance adjust must observe the same dated config row.
    /// </summary>
    public async Task<EntitlementConfig?> GetByTypeAtAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string entitlementType, string agreementCode, string okVersion,
        DateOnly asOfDate, CancellationToken ct = default)
        => await ExecuteGetByTypeAtAsync(conn, tx, entitlementType, agreementCode, okVersion, asOfDate, ct);

    private static async Task<EntitlementConfig?> ExecuteGetByTypeAtAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        string entitlementType, string agreementCode, string okVersion,
        DateOnly asOfDate, CancellationToken ct)
    {
        var sql =
            """
            SELECT * FROM entitlement_configs
            WHERE entitlement_type = @entitlementType
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_from <= @asOfDate
              AND (effective_to IS NULL OR effective_to > @asOfDate)
            LIMIT 1
            """;
        await using var cmd = tx is null ? new NpgsqlCommand(sql, conn) : new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("asOfDate", asOfDate);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConfig(reader) : null;
    }

    /// <summary>
    /// S30 / TASK-3003 — dated bulk read for an agreement+ok pair. Returns the rows whose
    /// effective range covers <paramref name="asOfDate"/> (≤5 rows: VACATION / SPECIAL_HOLIDAY
    /// / CARE_DAY / CHILD_SICK / SENIOR_DAY). Mirrors
    /// <see cref="GetByAgreementAsync"/> shape but adds the effective-date filter.
    /// </summary>
    public async Task<IReadOnlyList<EntitlementConfig>> GetByAgreementAtAsync(
        string agreementCode, string okVersion, DateOnly asOfDate, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM entitlement_configs
            WHERE agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_from <= @asOfDate
              AND (effective_to IS NULL OR effective_to > @asOfDate)
            ORDER BY entitlement_type
            """, conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("asOfDate", asOfDate);
        return await ReadConfigsAsync(cmd, ct);
    }

    /// <summary>
    /// S30 / TASK-3003 — convenience read: returns the live (open) row for a natural key,
    /// i.e. <c>effective_to IS NULL</c>. Used by the two-step consumption pattern (TASK-3008)
    /// to derive <c>ResetMonth</c> → entitlement-year-start, then re-read at that date via
    /// <see cref="GetByTypeAtAsync(string, string, string, DateOnly, CancellationToken)"/>.
    /// </summary>
    public async Task<EntitlementConfig?> GetCurrentOpenAsync(
        string entitlementType, string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM entitlement_configs
            WHERE entitlement_type = @entitlementType
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_to IS NULL
            """, conn);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConfig(reader) : null;
    }

    // ------------------------------------------------------------------
    // S30 / TASK-3003 — atomic-outbox primitives (ADR-018 D5 (conn, tx) contract).
    // Locks the currently-open row under SELECT ... FOR UPDATE, then dispatches the
    // ADR-020 D2 3-case routing inside SupersedeAndCreateAsync. Soft-delete closes
    // the open row. Mirrors WageTypeMappingRepository shape verbatim.
    // ------------------------------------------------------------------

    /// <summary>
    /// S30 / TASK-3003 — locks the currently-open row (<c>effective_to IS NULL</c>) for the
    /// supplied natural key via <c>SELECT ... FOR UPDATE</c>. Returns the locked row as a full
    /// <see cref="EntitlementConfig"/>, or <c>null</c> when no open row exists. Concurrent
    /// writers attempting the same lock serialize here. Mirrors S29 WTM precedent at
    /// <see cref="WageTypeMappingRepository.AcquireLockAsync"/>.
    /// </summary>
    public async Task<EntitlementConfig?> AcquireLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string entitlementType, string agreementCode, string okVersion,
        CancellationToken ct = default)
    {
        await using var lockCmd = new NpgsqlCommand(
            """
            SELECT * FROM entitlement_configs
            WHERE entitlement_type = @entitlementType
              AND agreement_code = @agreementCode
              AND ok_version = @okVersion
              AND effective_to IS NULL
            FOR UPDATE
            """, conn, tx);
        lockCmd.Parameters.AddWithValue("entitlementType", entitlementType);
        lockCmd.Parameters.AddWithValue("agreementCode", agreementCode);
        lockCmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await lockCmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadConfig(reader) : null;
    }

    /// <summary>
    /// S30 / TASK-3003 — atomic-outbox supersession + create overload (ADR-020 D2 3-case
    /// routing under the lock acquired by <see cref="AcquireLockAsync"/>). Mirrors S29 WTM
    /// precedent at <see cref="WageTypeMappingRepository.SupersedeAndCreateAsync"/>.
    ///
    /// <para><b>Case A</b> — no predecessor (<paramref name="predecessor"/> is <c>null</c>):
    /// fresh INSERT of <paramref name="newConfig"/> at version 1, effective_from = caller-supplied
    /// (typically today). <c>IsCreated: true</c>.</para>
    ///
    /// <para><b>Case B</b> — cross-day supersession (<c>predecessor.EffectiveFrom &lt;
    /// newConfig.EffectiveFrom</c>): close predecessor at <c>effective_to = newConfig.EffectiveFrom</c>
    /// (end-exclusive per ADR-018 D8), then INSERT a new open row at version 1. Single tx.
    /// <c>IsCreated: false</c>, <c>SupersededConfigId</c> populated.</para>
    ///
    /// <para><b>Case C</b> — same-day edit (<c>predecessor.EffectiveFrom ==
    /// newConfig.EffectiveFrom</c>): in-place UPDATE on the open row, version bump (S22 D9
    /// pattern). The optimistic-concurrency token <paramref name="expectedCurrentVersion"/>
    /// must match <c>predecessor.Version</c>. <c>IsCreated: false</c>, <c>SupersededConfigId</c>
    /// is <c>null</c>.</para>
    ///
    /// <para>
    /// No clock dependency in the repo (S22 precedent inherited): routing branches purely on
    /// caller-supplied dates. Endpoint layer reads the clock and supplies
    /// <c>newConfig.EffectiveFrom</c>.
    /// </para>
    /// </summary>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when a predecessor exists and its <c>version</c> does not equal
    /// <paramref name="expectedCurrentVersion"/>. Endpoint maps to 412.
    /// </exception>
    /// <exception cref="InvalidProfileSupersessionException">
    /// Thrown when <c>newConfig.EffectiveFrom &lt; predecessor.EffectiveFrom</c> (backdate
    /// rejected per ADR-018 D9 strict-less under end-exclusive).
    /// </exception>
    public async Task<SaveEntitlementConfigResult> SupersedeAndCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EntitlementConfig newConfig, EntitlementConfig? predecessor,
        long? expectedCurrentVersion,
        CancellationToken ct = default)
    {
        // Case A — no predecessor (fresh create).
        if (predecessor is null)
        {
            var newId = newConfig.ConfigId == Guid.Empty ? Guid.NewGuid() : newConfig.ConfigId;
            var inserted = await InsertOpenRowAsync(conn, tx, newConfig, newId, ct);
            return new SaveEntitlementConfigResult(inserted, inserted.Version, IsCreated: true, SupersededConfigId: null);
        }

        // 1. Optimistic-concurrency check — caller's If-Match must match the stored version.
        if (expectedCurrentVersion is null || predecessor.Version != expectedCurrentVersion.Value)
        {
            throw new OptimisticConcurrencyException(
                $"Entitlement config version is {predecessor.Version}, but caller sent " +
                $"If-Match: \"{expectedCurrentVersion?.ToString() ?? "<none>"}\"; refresh and retry.",
                expectedVersion: expectedCurrentVersion,
                actualVersion: predecessor.Version);
        }

        // 2. Backdate guard (ADR-018 D9 strict-less under end-exclusive). A new row cannot
        //    start before its predecessor — there is no valid history window in that case.
        if (newConfig.EffectiveFrom < predecessor.EffectiveFrom)
        {
            throw new InvalidProfileSupersessionException(
                $"Cannot supersede with effective_from {newConfig.EffectiveFrom:yyyy-MM-dd} " +
                $"earlier than predecessor's effective_from {predecessor.EffectiveFrom:yyyy-MM-dd}.");
        }

        // Case C — same-day edit (S22 D9 MODIFIED branch). UPDATE-in-place with version bump.
        if (newConfig.EffectiveFrom == predecessor.EffectiveFrom)
        {
            var updated = await UpdateInPlaceAsync(conn, tx, newConfig, predecessor.ConfigId, predecessor.Version, ct);
            return new SaveEntitlementConfigResult(updated, updated.Version, IsCreated: false, SupersededConfigId: null);
        }

        // Case B — cross-day supersession. Close predecessor at end-exclusive
        // newConfig.EffectiveFrom, then INSERT new open row at version 1.
        await CloseRowAsync(conn, tx, predecessor.ConfigId, newConfig.EffectiveFrom, ct);
        var newConfigId = newConfig.ConfigId == Guid.Empty ? Guid.NewGuid() : newConfig.ConfigId;
        var supersedingRow = await InsertOpenRowAsync(conn, tx, newConfig, newConfigId, ct);
        return new SaveEntitlementConfigResult(
            supersedingRow, supersedingRow.Version, IsCreated: false, SupersededConfigId: predecessor.ConfigId);
    }

    /// <summary>
    /// S30 / TASK-3003 — soft-delete via <c>effective_to = closeDate</c> on the currently-open
    /// row (ADR-021 D2; S29 WTM precedent). Replay determinism requires the closed row to
    /// remain queryable via <see cref="GetByTypeAtAsync(string, string, string, DateOnly, CancellationToken)"/>
    /// for any <c>asOfDate</c> within its prior effective range.
    ///
    /// <para>
    /// Caller must already hold the row lock via <see cref="AcquireLockAsync"/>. The version
    /// column is NOT bumped (S22 precedent: lifecycle close, not a content edit). Returns the
    /// closed config so the caller can build the audit / event body.
    /// </para>
    /// </summary>
    public async Task<EntitlementConfig> SoftDeleteAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EntitlementConfig predecessor, DateOnly closeDate,
        CancellationToken ct = default)
    {
        await using var closeCmd = new NpgsqlCommand(
            """
            UPDATE entitlement_configs
            SET effective_to = @closeDate
            WHERE config_id = @configId
              AND effective_to IS NULL
            RETURNING *
            """, conn, tx);
        closeCmd.Parameters.AddWithValue("closeDate", closeDate);
        closeCmd.Parameters.AddWithValue("configId", predecessor.ConfigId);
        await using var reader = await closeCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock acquired by
            // AcquireLockAsync at the same natural key.
            throw new InvalidOperationException(
                $"SoftDeleteAsync closed 0 rows for config_id={predecessor.ConfigId} " +
                $"(entitlement_type='{predecessor.EntitlementType}', " +
                $"agreement_code='{predecessor.AgreementCode}', ok_version='{predecessor.OkVersion}'); " +
                "FOR UPDATE invariant violated.");
        }
        return ReadConfig(reader);
    }

    // ------------------------------------------------------------------
    // S30 / TASK-3003 — audit insert primitive (ADR-018 D5 + ADR-019 D8 version-transition).
    // Mirrors WageTypeMappingRepository.AppendAuditAsync v3 shape: writes version_before /
    // version_after columns so audit-replay can reconstruct which version-transition produced
    // each state. CREATE paths pass version_before = null; UPDATE/SUPERSEDE pass prior version;
    // DELETE passes the pre-deletion version on both columns.
    // ------------------------------------------------------------------

    /// <summary>
    /// S30 / TASK-3003 — in-transaction audit insert (ADR-018 D5 atomic-outbox primitive +
    /// ADR-019 D8 version-transition columns). The action set includes <c>SUPERSEDED</c>
    /// per the s30-d2 audit-table CHECK at <c>init.sql:1154</c>. Mirrors S29 WTM precedent at
    /// <see cref="WageTypeMappingRepository.AppendAuditAsync(NpgsqlConnection, NpgsqlTransaction, string, string, string, string, string, string?, string?, string, string, long?, long, CancellationToken)"/>.
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid configId, string entitlementType, string agreementCode, string okVersion,
        string action, string? previousData, string? newData,
        long? versionBefore, long? versionAfter,
        string actorId, string actorRole,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_config_audit
                (config_id, entitlement_type, agreement_code, ok_version, action,
                 previous_data, new_data,
                 version_before, version_after,
                 actor_id, actor_role)
            VALUES
                (@configId, @entitlementType, @agreementCode, @okVersion, @action,
                 @previousData::jsonb, @newData::jsonb,
                 @versionBefore, @versionAfter,
                 @actorId, @actorRole)
            """, conn, tx);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("previousData", (object?)previousData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("newData", (object?)newData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("versionBefore", (object?)versionBefore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("versionAfter", (object?)versionAfter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ------------------------------------------------------------------
    // Private helpers — mirror S29 WTM private-helper layout (InsertSupersedingRow / CloseRow /
    // UpdateInPlace). Each takes the already-acquired (conn, tx) and assumes the caller holds
    // any necessary row lock.
    // ------------------------------------------------------------------

    /// <summary>
    /// Inserts a new currently-open row (<c>effective_to NULL</c>) at version 1. Used by both
    /// Case A (fresh create) and Case B (cross-day supersession after CloseRowAsync). Caller
    /// supplies <paramref name="configId"/>; the repo never silently generates inside this
    /// helper (the public surface handles Empty → NewGuid).
    /// </summary>
    private static async Task<EntitlementConfig> InsertOpenRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EntitlementConfig newConfig, Guid configId, CancellationToken ct)
    {
        await using var insertCmd = new NpgsqlCommand(
            """
            INSERT INTO entitlement_configs (
                config_id, entitlement_type, agreement_code, ok_version,
                annual_quota, accrual_model, reset_month, carryover_max,
                pro_rate_by_part_time, is_per_episode, min_age, description,
                full_day_only,
                effective_from, effective_to, version)
            VALUES (
                @configId, @entitlementType, @agreementCode, @okVersion,
                @annualQuota, @accrualModel, @resetMonth, @carryoverMax,
                @proRateByPartTime, @isPerEpisode, @minAge, @description,
                @fullDayOnly,
                @effectiveFrom, NULL, 1)
            RETURNING *
            """, conn, tx);
        insertCmd.Parameters.AddWithValue("configId", configId);
        insertCmd.Parameters.AddWithValue("entitlementType", newConfig.EntitlementType);
        insertCmd.Parameters.AddWithValue("agreementCode", newConfig.AgreementCode);
        insertCmd.Parameters.AddWithValue("okVersion", newConfig.OkVersion);
        insertCmd.Parameters.AddWithValue("annualQuota", newConfig.AnnualQuota);
        insertCmd.Parameters.AddWithValue("accrualModel", newConfig.AccrualModel);
        insertCmd.Parameters.AddWithValue("resetMonth", newConfig.ResetMonth);
        insertCmd.Parameters.AddWithValue("carryoverMax", newConfig.CarryoverMax);
        insertCmd.Parameters.AddWithValue("proRateByPartTime", newConfig.ProRateByPartTime);
        insertCmd.Parameters.AddWithValue("isPerEpisode", newConfig.IsPerEpisode);
        insertCmd.Parameters.AddWithValue("minAge", (object?)newConfig.MinAge ?? DBNull.Value);
        insertCmd.Parameters.AddWithValue("description", (object?)newConfig.Description ?? DBNull.Value);
        // S73 / TASK-7301 (R2): the flag threads the full config surface — supersession (Case B)
        // routes through this INSERT, so version-survival holds by construction.
        insertCmd.Parameters.AddWithValue("fullDayOnly", newConfig.FullDayOnly);
        insertCmd.Parameters.AddWithValue("effectiveFrom", newConfig.EffectiveFrom);
        await using var reader = await insertCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — INSERT ... RETURNING * always yields one row on success.
            throw new InvalidOperationException(
                $"InsertOpenRowAsync produced no row for (entitlement_type='{newConfig.EntitlementType}', " +
                $"agreement_code='{newConfig.AgreementCode}', ok_version='{newConfig.OkVersion}', " +
                $"effective_from='{newConfig.EffectiveFrom:yyyy-MM-dd}').");
        }
        return ReadConfig(reader);
    }

    /// <summary>
    /// Closes the supplied row by stamping <c>effective_to = closeDate</c> under end-exclusive
    /// semantics (ADR-018 D8). Caller must already hold the row lock acquired via
    /// <see cref="AcquireLockAsync"/>.
    /// </summary>
    private static async Task CloseRowAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid configId, DateOnly closeDate, CancellationToken ct)
    {
        await using var closeCmd = new NpgsqlCommand(
            "UPDATE entitlement_configs SET effective_to = @closeDate WHERE config_id = @configId",
            conn, tx);
        closeCmd.Parameters.AddWithValue("closeDate", closeDate);
        closeCmd.Parameters.AddWithValue("configId", configId);
        await closeCmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Same-day UPDATE-in-place path (S22 D9 MODIFIED branch). Updates the mutable config
    /// fields, bumps version by one; config_id, effective_from, effective_to are immutable
    /// across in-place edits. Caller must already hold the row lock acquired via
    /// <see cref="AcquireLockAsync"/>. <c>WHERE version = @expectedVersion</c> is a defense-
    /// in-depth check — the caller already validated optimistic-concurrency above.
    /// </summary>
    private static async Task<EntitlementConfig> UpdateInPlaceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        EntitlementConfig newConfig, Guid predecessorConfigId, long expectedVersion,
        CancellationToken ct)
    {
        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE entitlement_configs SET
                annual_quota = @annualQuota,
                accrual_model = @accrualModel,
                reset_month = @resetMonth,
                carryover_max = @carryoverMax,
                pro_rate_by_part_time = @proRateByPartTime,
                is_per_episode = @isPerEpisode,
                min_age = @minAge,
                description = @description,
                full_day_only = @fullDayOnly,
                version = version + 1
            WHERE config_id = @configId
              AND effective_to IS NULL
              AND version = @expectedVersion
            RETURNING *
            """, conn, tx);
        updateCmd.Parameters.AddWithValue("configId", predecessorConfigId);
        updateCmd.Parameters.AddWithValue("expectedVersion", expectedVersion);
        updateCmd.Parameters.AddWithValue("annualQuota", newConfig.AnnualQuota);
        updateCmd.Parameters.AddWithValue("accrualModel", newConfig.AccrualModel);
        updateCmd.Parameters.AddWithValue("resetMonth", newConfig.ResetMonth);
        updateCmd.Parameters.AddWithValue("carryoverMax", newConfig.CarryoverMax);
        updateCmd.Parameters.AddWithValue("proRateByPartTime", newConfig.ProRateByPartTime);
        updateCmd.Parameters.AddWithValue("isPerEpisode", newConfig.IsPerEpisode);
        updateCmd.Parameters.AddWithValue("minAge", (object?)newConfig.MinAge ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("description", (object?)newConfig.Description ?? DBNull.Value);
        // S73 / TASK-7301 (R2): Case C same-day edits carry the flag too — the DB CHECK
        // (entitlement_configs_full_day_only_types) backstops any caller that tries to flip a
        // CARE_DAY/SENIOR_DAY row to FALSE.
        updateCmd.Parameters.AddWithValue("fullDayOnly", newConfig.FullDayOnly);
        await using var reader = await updateCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock acquired by
            // AcquireLockAsync at the same natural key + the caller already validated the
            // optimistic-concurrency token.
            throw new InvalidOperationException(
                $"UpdateInPlaceAsync produced no row for config_id={predecessorConfigId} " +
                $"at expected version {expectedVersion}; FOR UPDATE invariant violated.");
        }
        return ReadConfig(reader);
    }

    private static async Task<IReadOnlyList<EntitlementConfig>> ReadConfigsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var configs = new List<EntitlementConfig>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            configs.Add(ReadConfig(reader));
        return configs;
    }

    private static EntitlementConfig ReadConfig(NpgsqlDataReader reader) => new()
    {
        ConfigId = reader.GetGuid(reader.GetOrdinal("config_id")),
        EntitlementType = reader.GetString(reader.GetOrdinal("entitlement_type")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        AnnualQuota = reader.GetDecimal(reader.GetOrdinal("annual_quota")),
        AccrualModel = reader.GetString(reader.GetOrdinal("accrual_model")),
        ResetMonth = reader.GetInt32(reader.GetOrdinal("reset_month")),
        CarryoverMax = reader.GetDecimal(reader.GetOrdinal("carryover_max")),
        ProRateByPartTime = reader.GetBoolean(reader.GetOrdinal("pro_rate_by_part_time")),
        IsPerEpisode = reader.GetBoolean(reader.GetOrdinal("is_per_episode")),
        MinAge = reader.IsDBNull(reader.GetOrdinal("min_age")) ? null : reader.GetInt32(reader.GetOrdinal("min_age")),
        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
        // S73 / TASK-7301 (R2): full-day-only flag, added by the s73-full-day-only-schema segment.
        FullDayOnly = reader.GetBoolean(reader.GetOrdinal("full_day_only")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        // S30 / TASK-3003: effective-dating + row-version columns added by
        // s30-d2-ec-effective-dating + s25-d2-2-version migrations.
        Version = reader.GetInt64(reader.GetOrdinal("version")),
        EffectiveFrom = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_from")),
        EffectiveTo = reader.IsDBNull(reader.GetOrdinal("effective_to"))
            ? null
            : reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_to")),
    };
}

/// <summary>
/// Result of a save operation on <see cref="EntitlementConfigRepository"/> (S30 / TASK-3003;
/// mirrors <see cref="SaveWageTypeMappingResult"/> from S25 / TASK-2502). The endpoint sets
/// <c>ETag: "&lt;Version&gt;"</c> on the response. <c>SupersededConfigId</c> is non-null only
/// on the Case B cross-day path so the endpoint can emit the second
/// <c>EntitlementConfigSuperseded</c> audit/event for the closed row (mirrors S25
/// agreement-configs publish-supersession dual-emission per ADR-019 D1).
/// </summary>
/// <param name="Config">The persisted entitlement config (post-mutation snapshot).</param>
/// <param name="Version">The authoritative row-version after the save — first-insert is <c>1</c>;
/// each in-place UPDATE bumps by one; cross-day supersession resets to <c>1</c> on the new row.
/// The wire ETag is <c>"&lt;version&gt;"</c> (RFC 7232 quoted) per ADR-018 D7.</param>
/// <param name="IsCreated"><c>true</c> when this save inserted a fresh row with no predecessor
/// (POST-style create); <c>false</c> when it updated or superseded an existing row.</param>
/// <param name="SupersededConfigId"><c>null</c> for Case A + Case C; the closed predecessor's
/// <c>config_id</c> for Case B cross-day supersession. Endpoint uses this to emit the
/// dual-emission second audit + event row at predecessor identity.</param>
public sealed record SaveEntitlementConfigResult(
    EntitlementConfig Config,
    long Version,
    bool IsCreated,
    Guid? SupersededConfigId);
