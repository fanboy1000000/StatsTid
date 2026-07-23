using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class AgreementConfigRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public AgreementConfigRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AgreementConfigEntity>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM agreement_configs ORDER BY agreement_code, ok_version, status", conn);
        return await ReadEntitiesAsync(cmd, ct);
    }

    public async Task<AgreementConfigEntity?> GetByIdAsync(Guid configId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM agreement_configs WHERE config_id = @configId", conn);
        cmd.Parameters.AddWithValue("configId", configId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadEntity(reader) : null;
    }

    public async Task<AgreementConfigEntity?> GetActiveAsync(string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM agreement_configs WHERE agreement_code = @agreementCode AND ok_version = @okVersion AND status = 'ACTIVE' LIMIT 1", conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadEntity(reader) : null;
    }

    public async Task<IReadOnlyList<AgreementConfigEntity>> GetByStatusAsync(string status, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM agreement_configs WHERE status = @status ORDER BY agreement_code, ok_version", conn);
        cmd.Parameters.AddWithValue("status", status);
        return await ReadEntitiesAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<AgreementConfigEntity>> GetByAgreementAsync(string agreementCode, string okVersion, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM agreement_configs WHERE agreement_code = @agreementCode AND ok_version = @okVersion ORDER BY status, created_at DESC", conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        return await ReadEntitiesAsync(cmd, ct);
    }

    public async Task<Guid> CreateAsync(AgreementConfigEntity entity, CancellationToken ct = default)
        => await CreateAsync(entity, "DRAFT", ct);

    public async Task<Guid> CreateAsync(AgreementConfigEntity entity, string status, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteCreateAsync(conn, null, entity, status, ct);
    }

    /// <summary>
    /// In-transaction sibling overload of <see cref="CreateAsync(AgreementConfigEntity, CancellationToken)"/>.
    /// Reuses the caller-supplied <paramref name="conn"/> + <paramref name="tx"/> so the
    /// caller can extend the same transaction across audit + outbox writes
    /// (ADR-018 D3 transactional-outbox contract). The caller commits or rolls back; this
    /// method does NOT.
    ///
    /// <para>
    /// Atomic-outbox primitive (S24 ForcedRollbackHarness consumer): preserved unchanged
    /// across the S25 / TASK-2503 v3 migration. Create endpoints + clone endpoint write
    /// version=1 (DB DEFAULT); the wire ETag for the 201 response is a static <c>"1"</c>.
    /// </para>
    /// </summary>
    public async Task<Guid> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        AgreementConfigEntity entity, CancellationToken ct = default)
        => await ExecuteCreateAsync(conn, tx, entity, "DRAFT", ct);

    /// <summary>
    /// In-transaction sibling overload accepting an explicit <paramref name="status"/>.
    /// </summary>
    public async Task<Guid> CreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        AgreementConfigEntity entity, string status, CancellationToken ct = default)
        => await ExecuteCreateAsync(conn, tx, entity, status, ct);

    /// <summary>
    /// The shared INSERT statement body (S118 / TASK-11800): both <see cref="ExecuteCreateAsync"/>
    /// (fire-and-forget, returns the generated id) and <see cref="CreateReturningAsync"/>
    /// (<c>INSERT … RETURNING *</c>) build from this single const so the two paths can never
    /// drift column-wise. Timestamps stay DB-computed <c>NOW()</c> literals; <c>version</c> is
    /// deliberately ABSENT (DB DEFAULT 1) — byte-identical INSERT semantics on both paths.
    /// </summary>
    private const string InsertSql =
        """
        INSERT INTO agreement_configs (
            config_id, agreement_code, ok_version, status,
            weekly_norm_hours, norm_period_weeks, norm_model, annual_norm_hours,
            max_flex_balance, flex_carryover_max,
            has_overtime, has_merarbejde, overtime_threshold_50, overtime_threshold_100,
            evening_supplement_enabled, night_supplement_enabled, weekend_supplement_enabled, holiday_supplement_enabled,
            evening_start, evening_end, night_start, night_end,
            evening_rate, night_rate, weekend_saturday_rate, weekend_sunday_rate, holiday_rate,
            on_call_duty_enabled, on_call_duty_rate,
            call_in_work_enabled, call_in_minimum_hours, call_in_rate,
            travel_time_enabled, working_travel_rate, non_working_travel_rate,
            max_daily_hours, minimum_rest_hours, rest_period_derogation_allowed,
            weekly_max_hours_reference_period, voluntary_unsocial_hours_allowed,
            default_compensation_model, employee_compensation_choice,
            max_overtime_hours_per_period, overtime_requires_pre_approval,
            created_by, description, cloned_from_id,
            created_at, updated_at
        ) VALUES (
            @configId, @agreementCode, @okVersion, @status,
            @weeklyNormHours, @normPeriodWeeks, @normModel, @annualNormHours,
            @maxFlexBalance, @flexCarryoverMax,
            @hasOvertime, @hasMerarbejde, @overtimeThreshold50, @overtimeThreshold100,
            @eveningSupplementEnabled, @nightSupplementEnabled, @weekendSupplementEnabled, @holidaySupplementEnabled,
            @eveningStart, @eveningEnd, @nightStart, @nightEnd,
            @eveningRate, @nightRate, @weekendSaturdayRate, @weekendSundayRate, @holidayRate,
            @onCallDutyEnabled, @onCallDutyRate,
            @callInWorkEnabled, @callInMinimumHours, @callInRate,
            @travelTimeEnabled, @workingTravelRate, @nonWorkingTravelRate,
            @maxDailyHours, @minimumRestHours, @restPeriodDerogationAllowed,
            @weeklyMaxHoursReferencePeriod, @voluntaryUnsocialHoursAllowed,
            @defaultCompensationModel, @employeeCompensationChoice,
            @maxOvertimeHoursPerPeriod, @overtimeRequiresPreApproval,
            @createdBy, @description, @clonedFromId,
            NOW(), NOW()
        )
        """;

    private static async Task<Guid> ExecuteCreateAsync(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        AgreementConfigEntity entity, string status, CancellationToken ct)
    {
        var configId = Guid.NewGuid();
        await using var cmd = tx is null ? new NpgsqlCommand(InsertSql, conn) : new NpgsqlCommand(InsertSql, conn, tx);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("status", status);
        AddConfigParameters(cmd, entity);
        cmd.Parameters.AddWithValue("createdBy", entity.CreatedBy);
        cmd.Parameters.AddWithValue("description", (object?)entity.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("clonedFromId", (object?)entity.ClonedFromId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
        return configId;
    }

    /// <summary>
    /// S118 / TASK-11800 (SPRINT-118 owner ruling #1, the dead-branch class): in-transaction
    /// RETURNING-ENTITY SIBLING of <see cref="CreateAsync(NpgsqlConnection, NpgsqlTransaction, AgreementConfigEntity, CancellationToken)"/>.
    /// Same INSERT (the shared <see cref="InsertSql"/> const — DB-computed <c>NOW()</c>
    /// timestamps, <c>version</c> via DB DEFAULT 1) with <c>RETURNING *</c> appended, hydrated
    /// through the existing <see cref="ReadEntity"/> reader mapper. The create endpoints'
    /// post-commit re-read (and its structurally-fallible <c>created is not null</c> fork)
    /// ceases to exist: the 201 body is ALWAYS the full entity, sourced inside the same
    /// transaction, BEFORE the audit/outbox/audit-projection appends (order unchanged —
    /// ADR-018 D3). Status is DRAFT (the only status the create/clone endpoints write).
    ///
    /// <para>
    /// ALL pre-existing <c>CreateAsync</c> overloads stay SIGNATURE-IDENTICAL (Step-0b Codex
    /// W1 / Reviewer W1 — TxContractTests, the atomic suites, the seeder, and the concurrency
    /// suites consume them). The caller commits or rolls back; this method does NOT.
    /// </para>
    /// </summary>
    public async Task<AgreementConfigEntity> CreateReturningAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        AgreementConfigEntity entity, CancellationToken ct = default)
    {
        var configId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(InsertSql + "\nRETURNING *", conn, tx);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("status", "DRAFT");
        AddConfigParameters(cmd, entity);
        cmd.Parameters.AddWithValue("createdBy", entity.CreatedBy);
        cmd.Parameters.AddWithValue("description", (object?)entity.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("clonedFromId", (object?)entity.ClonedFromId ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            // Defense-in-depth — an INSERT ... RETURNING that inserted zero rows would have
            // thrown already; this is structurally unreachable.
            throw new InvalidOperationException(
                $"CreateReturningAsync produced no row for config_id={configId}; INSERT ... RETURNING invariant violated.");
        }
        return ReadEntity(reader);
    }

    public async Task<bool> UpdateDraftAsync(Guid configId, AgreementConfigEntity updated, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteSelfManagedUpdateDraftAsync(conn, configId, updated, ct);
    }

    private static async Task<bool> ExecuteSelfManagedUpdateDraftAsync(
        NpgsqlConnection conn,
        Guid configId, AgreementConfigEntity updated, CancellationToken ct)
    {
        // Self-managed (no caller tx) — preserved unchanged from pre-S25; legacy callers
        // (seeders, internal tooling) continue to use this best-effort path. The v3
        // in-transaction sibling enforces ETag/If-Match optimistic concurrency for HTTP
        // admin endpoints.
        var sql =
            """
            UPDATE agreement_configs SET
                weekly_norm_hours = @weeklyNormHours,
                norm_period_weeks = @normPeriodWeeks,
                norm_model = @normModel,
                annual_norm_hours = @annualNormHours,
                max_flex_balance = @maxFlexBalance,
                flex_carryover_max = @flexCarryoverMax,
                has_overtime = @hasOvertime,
                has_merarbejde = @hasMerarbejde,
                overtime_threshold_50 = @overtimeThreshold50,
                overtime_threshold_100 = @overtimeThreshold100,
                evening_supplement_enabled = @eveningSupplementEnabled,
                night_supplement_enabled = @nightSupplementEnabled,
                weekend_supplement_enabled = @weekendSupplementEnabled,
                holiday_supplement_enabled = @holidaySupplementEnabled,
                evening_start = @eveningStart,
                evening_end = @eveningEnd,
                night_start = @nightStart,
                night_end = @nightEnd,
                evening_rate = @eveningRate,
                night_rate = @nightRate,
                weekend_saturday_rate = @weekendSaturdayRate,
                weekend_sunday_rate = @weekendSundayRate,
                holiday_rate = @holidayRate,
                on_call_duty_enabled = @onCallDutyEnabled,
                on_call_duty_rate = @onCallDutyRate,
                call_in_work_enabled = @callInWorkEnabled,
                call_in_minimum_hours = @callInMinimumHours,
                call_in_rate = @callInRate,
                travel_time_enabled = @travelTimeEnabled,
                working_travel_rate = @workingTravelRate,
                non_working_travel_rate = @nonWorkingTravelRate,
                max_daily_hours = @maxDailyHours,
                minimum_rest_hours = @minimumRestHours,
                rest_period_derogation_allowed = @restPeriodDerogationAllowed,
                weekly_max_hours_reference_period = @weeklyMaxHoursReferencePeriod,
                voluntary_unsocial_hours_allowed = @voluntaryUnsocialHoursAllowed,
                default_compensation_model = @defaultCompensationModel,
                employee_compensation_choice = @employeeCompensationChoice,
                max_overtime_hours_per_period = @maxOvertimeHoursPerPeriod,
                overtime_requires_pre_approval = @overtimeRequiresPreApproval,
                description = @description,
                updated_at = NOW()
            WHERE config_id = @configId AND status = 'DRAFT'
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("configId", configId);
        AddConfigParameters(cmd, updated);
        cmd.Parameters.AddWithValue("description", (object?)updated.Description ?? DBNull.Value);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    /// <summary>
    /// In-transaction v3 update overload — admin-strict ETag/If-Match optimistic-concurrency
    /// (ADR-019 pending, mirrors S22 ADR-018 D7 pattern). Reads the current row under
    /// <c>SELECT ... FOR UPDATE</c>, validates <paramref name="expectedVersion"/> against the
    /// stored <c>version</c>, and applies the UPDATE with <c>version = version + 1</c> in a
    /// single SET clause. Status must be DRAFT — otherwise the call manifests as
    /// <see cref="OptimisticConcurrencyException"/> with the actual current state surfaced
    /// in the exception (the endpoint maps this to a 412 body).
    ///
    /// <para>
    /// Replaces the v2 overload <c>(conn, tx, configId, updated, ct) → bool</c>. The caller
    /// commits or rolls back; this method does NOT.
    /// </para>
    /// </summary>
    /// <returns>
    /// <see cref="SaveAgreementConfigResult"/> with the updated entity (post-write snapshot),
    /// the new <c>version</c> (= prior version + 1), <c>IsCreated: false</c>, and
    /// <c>ArchivedId: null</c>.
    /// </returns>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when the row is missing, no longer in DRAFT, or its <c>version</c> column does
    /// not equal <paramref name="expectedVersion"/>.
    /// </exception>
    public async Task<SaveAgreementConfigResult> UpdateDraftAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid configId, long expectedVersion, AgreementConfigEntity updated,
        CancellationToken ct = default)
    {
        // 1. SELECT FOR UPDATE the target row + status + version under the caller tx so
        //    concurrent supersessions serialize on this lock. Returns the lockable triple
        //    needed for the optimistic-concurrency check + the routing decision.
        long currentVersion;
        string currentStatus;
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT version, status FROM agreement_configs WHERE config_id = @configId FOR UPDATE",
            conn, tx))
        {
            lockCmd.Parameters.AddWithValue("configId", configId);
            await using var reader = await lockCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // Row missing — surface as concurrency mismatch (caller may have raced an
                // archive that turned into a hard delete in some future migration, or the
                // ID was stale to begin with). Endpoint maps to 412.
                throw new OptimisticConcurrencyException(
                    $"Agreement config {configId} not found.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            currentVersion = reader.GetInt64(0);
            currentStatus = reader.GetString(1);
        }

        // 2. Status check — only DRAFT is editable. A non-DRAFT row at this point means a
        //    concurrent publish/archive between the endpoint's pre-check and our FOR UPDATE.
        //    Map to OptimisticConcurrencyException so the caller's 412 path surfaces both
        //    the version AND the (changed) state.
        if (!string.Equals(currentStatus, "DRAFT", StringComparison.Ordinal))
        {
            throw new OptimisticConcurrencyException(
                $"Agreement config {configId} is no longer in DRAFT status (current: {currentStatus}); refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 3. Optimistic-concurrency check — caller's If-Match must match the stored version.
        if (currentVersion != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Agreement config {configId} version is {currentVersion}, but caller sent If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 4. UPDATE with version-bump in the same SET clause. WHERE status='DRAFT' is
        //    defense-in-depth (the FOR UPDATE under RepeatableRead/ReadCommitted prevents
        //    concurrent state change between step 1 and now).
        var updateSql =
            """
            UPDATE agreement_configs SET
                weekly_norm_hours = @weeklyNormHours,
                norm_period_weeks = @normPeriodWeeks,
                norm_model = @normModel,
                annual_norm_hours = @annualNormHours,
                max_flex_balance = @maxFlexBalance,
                flex_carryover_max = @flexCarryoverMax,
                has_overtime = @hasOvertime,
                has_merarbejde = @hasMerarbejde,
                overtime_threshold_50 = @overtimeThreshold50,
                overtime_threshold_100 = @overtimeThreshold100,
                evening_supplement_enabled = @eveningSupplementEnabled,
                night_supplement_enabled = @nightSupplementEnabled,
                weekend_supplement_enabled = @weekendSupplementEnabled,
                holiday_supplement_enabled = @holidaySupplementEnabled,
                evening_start = @eveningStart,
                evening_end = @eveningEnd,
                night_start = @nightStart,
                night_end = @nightEnd,
                evening_rate = @eveningRate,
                night_rate = @nightRate,
                weekend_saturday_rate = @weekendSaturdayRate,
                weekend_sunday_rate = @weekendSundayRate,
                holiday_rate = @holidayRate,
                on_call_duty_enabled = @onCallDutyEnabled,
                on_call_duty_rate = @onCallDutyRate,
                call_in_work_enabled = @callInWorkEnabled,
                call_in_minimum_hours = @callInMinimumHours,
                call_in_rate = @callInRate,
                travel_time_enabled = @travelTimeEnabled,
                working_travel_rate = @workingTravelRate,
                non_working_travel_rate = @nonWorkingTravelRate,
                max_daily_hours = @maxDailyHours,
                minimum_rest_hours = @minimumRestHours,
                rest_period_derogation_allowed = @restPeriodDerogationAllowed,
                weekly_max_hours_reference_period = @weeklyMaxHoursReferencePeriod,
                voluntary_unsocial_hours_allowed = @voluntaryUnsocialHoursAllowed,
                default_compensation_model = @defaultCompensationModel,
                employee_compensation_choice = @employeeCompensationChoice,
                max_overtime_hours_per_period = @maxOvertimeHoursPerPeriod,
                overtime_requires_pre_approval = @overtimeRequiresPreApproval,
                description = @description,
                updated_at = NOW(),
                version = version + 1
            WHERE config_id = @configId AND status = 'DRAFT'
            RETURNING *
            """;
        await using var updateCmd = new NpgsqlCommand(updateSql, conn, tx);
        updateCmd.Parameters.AddWithValue("configId", configId);
        AddConfigParameters(updateCmd, updated);
        updateCmd.Parameters.AddWithValue("description", (object?)updated.Description ?? DBNull.Value);
        await using var updReader = await updateCmd.ExecuteReaderAsync(ct);
        if (!await updReader.ReadAsync(ct))
        {
            // Defense-in-depth — the FOR UPDATE lock should make this unreachable.
            throw new InvalidOperationException(
                $"UpdateDraftAsync produced no row for config_id={configId} at expected version {expectedVersion}; FOR UPDATE invariant violated.");
        }
        var entity = ReadEntity(updReader);
        return new SaveAgreementConfigResult(entity, entity.Version, IsCreated: false, ArchivedId: null);
    }

    /// <summary>
    /// Self-managed overload: opens its own connection and an internal transaction for the
    /// archive-prior-ACTIVE + activate-DRAFT pair. For a caller-driven atomic outbox + audit +
    /// publish (ADR-018 D3) call the in-transaction sibling
    /// <see cref="PublishAsync(NpgsqlConnection, NpgsqlTransaction, Guid, long, string, CancellationToken)"/>.
    ///
    /// Returns the prior-ACTIVE config_id that was archived (null if there was no prior
    /// ACTIVE OR the publish was a no-op because the target config was missing / not in
    /// DRAFT). On no-op the internal transaction is rolled back so the archive write is
    /// reverted — matches the pre-S24 atomic semantic.
    /// </summary>
    public async Task<Guid?> PublishAsync(Guid configId, string actorId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var (archivedId, published) = await ExecuteSelfManagedPublishAsync(conn, tx, configId, actorId, ct);
            if (!published)
            {
                // Config not found OR config not DRAFT — preserve pre-S24 atomic semantic by
                // rolling back the (potentially) archived prior-ACTIVE update so the
                // database is left untouched. Endpoint observes null and surfaces 409.
                await tx.RollbackAsync(ct);
                return null;
            }
            await tx.CommitAsync(ct);
            return archivedId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// In-transaction v3 publish overload — admin-strict ETag/If-Match optimistic-concurrency
    /// (ADR-019 pending). Atomically archives the prior ACTIVE config (if any) for the same
    /// (agreement_code, ok_version) and activates the DRAFT identified by
    /// <paramref name="configId"/>. Both UPDATEs run on the caller-supplied
    /// <paramref name="conn"/> + <paramref name="tx"/>. The caller commits or rolls back;
    /// this method does NOT.
    ///
    /// <para>
    /// Replaces the v2 overload that returned <c>(Guid? ArchivedId, bool Published)</c>
    /// (S24 Step 7a P1 fix). The "Published == false" branch of that tuple now manifests
    /// as <see cref="OptimisticConcurrencyException"/> — the caller's 412 path surfaces both
    /// the (Expected, Actual) version AND the changed state. ArchivedId carries on
    /// <see cref="SaveAgreementConfigResult"/> for the publish-event payload.
    /// </para>
    /// </summary>
    /// <returns>
    /// <see cref="SaveAgreementConfigResult"/> with the activated entity, the new
    /// <c>version</c> (= prior version + 1), <c>IsCreated: false</c>, and
    /// <c>ArchivedId</c> = the prior-ACTIVE config_id (or null if none existed).
    /// </returns>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when the row is missing, not in DRAFT, or its <c>version</c> column does not
    /// equal <paramref name="expectedVersion"/>.
    /// </exception>
    public async Task<SaveAgreementConfigResult> PublishAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid configId, long expectedVersion, string actorId,
        CancellationToken ct = default)
    {
        // 1. SELECT FOR UPDATE the target row to acquire its lock + capture identity +
        //    status + version in one shot.
        string agreementCode;
        string okVersion;
        long currentVersion;
        string currentStatus;
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT agreement_code, ok_version, version, status FROM agreement_configs WHERE config_id = @configId FOR UPDATE",
            conn, tx))
        {
            lockCmd.Parameters.AddWithValue("configId", configId);
            await using var reader = await lockCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new OptimisticConcurrencyException(
                    $"Agreement config {configId} not found.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            agreementCode = reader.GetString(0);
            okVersion = reader.GetString(1);
            currentVersion = reader.GetInt64(2);
            currentStatus = reader.GetString(3);
        }

        // 2. Status check — only DRAFT is publishable. Concurrent publish/archive between
        //    endpoint pre-check and our FOR UPDATE (S24 Step 7a P1 race) → 412.
        if (!string.Equals(currentStatus, "DRAFT", StringComparison.Ordinal))
        {
            throw new OptimisticConcurrencyException(
                $"Agreement config {configId} is no longer in DRAFT status (current: {currentStatus}); cannot publish — refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 3. Optimistic-concurrency check.
        if (currentVersion != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Agreement config {configId} version is {currentVersion}, but caller sent If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 4. Archive the current ACTIVE config for the same (agreement_code, ok_version).
        //    RETURNING the archived row's new version too — the publish endpoint uses it to
        //    emit a matching ARCHIVED audit row (versionBefore = version-1, versionAfter =
        //    version) + AgreementConfigArchived outbox event in the same tx (ADR-019 D1).
        Guid? archivedId = null;
        long? archivedVersion = null;
        await using (var archiveCmd = new NpgsqlCommand(
            """
            UPDATE agreement_configs
            SET status = 'ARCHIVED', archived_at = NOW(), updated_at = NOW(), version = version + 1
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion AND status = 'ACTIVE'
            RETURNING config_id, version
            """, conn, tx))
        {
            archiveCmd.Parameters.AddWithValue("agreementCode", agreementCode);
            archiveCmd.Parameters.AddWithValue("okVersion", okVersion);
            await using var archiveReader = await archiveCmd.ExecuteReaderAsync(ct);
            if (await archiveReader.ReadAsync(ct))
            {
                archivedId = archiveReader.GetGuid(0);
                archivedVersion = archiveReader.GetInt64(1);
            }
        }

        // 5. Activate the DRAFT — bump version + return the post-write snapshot.
        await using var publishCmd = new NpgsqlCommand(
            """
            UPDATE agreement_configs
            SET status = 'ACTIVE', published_at = NOW(), updated_at = NOW(), version = version + 1
            WHERE config_id = @configId AND status = 'DRAFT'
            RETURNING *
            """, conn, tx);
        publishCmd.Parameters.AddWithValue("configId", configId);
        await using var publishReader = await publishCmd.ExecuteReaderAsync(ct);
        if (!await publishReader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"PublishAsync produced no row for config_id={configId} at expected version {expectedVersion}; FOR UPDATE invariant violated.");
        }
        var entity = ReadEntity(publishReader);
        return new SaveAgreementConfigResult(
            entity, entity.Version, IsCreated: false,
            ArchivedId: archivedId, ArchivedVersion: archivedVersion);
    }

    private static async Task<(Guid? ArchivedId, bool Published)> ExecuteSelfManagedPublishAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid configId, string actorId, CancellationToken ct)
    {
        // Self-managed path — preserved unchanged from pre-S25 (no version bump). Legacy
        // callers (PublishAsync(Guid, …) entry) continue to use this best-effort path; HTTP
        // admin endpoints use the v3 sibling that enforces ETag/If-Match optimistic
        // concurrency.
        string agreementCode;
        string okVersion;
        string status;
        await using (var getCmd = new NpgsqlCommand(
            "SELECT agreement_code, ok_version, status FROM agreement_configs WHERE config_id = @configId",
            conn, tx))
        {
            getCmd.Parameters.AddWithValue("configId", configId);
            await using var reader = await getCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return (null, false);
            agreementCode = reader.GetString(0);
            okVersion = reader.GetString(1);
            status = reader.GetString(2);
        }
        if (status != "DRAFT")
            return (null, false);

        Guid? archivedId = null;
        await using (var archiveCmd = new NpgsqlCommand(
            """
            UPDATE agreement_configs
            SET status = 'ARCHIVED', archived_at = NOW(), updated_at = NOW()
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion AND status = 'ACTIVE'
            RETURNING config_id
            """, conn, tx))
        {
            archiveCmd.Parameters.AddWithValue("agreementCode", agreementCode);
            archiveCmd.Parameters.AddWithValue("okVersion", okVersion);
            var result = await archiveCmd.ExecuteScalarAsync(ct);
            if (result is Guid archivedGuid)
                archivedId = archivedGuid;
        }

        await using var publishCmd = new NpgsqlCommand(
            """
            UPDATE agreement_configs
            SET status = 'ACTIVE', published_at = NOW(), updated_at = NOW()
            WHERE config_id = @configId AND status = 'DRAFT'
            """, conn, tx);
        publishCmd.Parameters.AddWithValue("configId", configId);
        var publishedRows = await publishCmd.ExecuteNonQueryAsync(ct);

        return (archivedId, publishedRows > 0);
    }

    public async Task<bool> ArchiveAsync(Guid configId, string actorId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        return await ExecuteSelfManagedArchiveAsync(conn, configId, actorId, ct);
    }

    private static async Task<bool> ExecuteSelfManagedArchiveAsync(
        NpgsqlConnection conn,
        Guid configId, string actorId, CancellationToken ct)
    {
        // Self-managed path — preserved unchanged from pre-S25 (no version bump). Legacy
        // callers (internal tooling) continue to use this best-effort path; HTTP admin
        // endpoints use the v3 sibling that enforces ETag/If-Match optimistic concurrency.
        var sql =
            """
            UPDATE agreement_configs
            SET status = 'ARCHIVED', archived_at = NOW(), updated_at = NOW()
            WHERE config_id = @configId AND status != 'ARCHIVED'
            """;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("configId", configId);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    /// <summary>
    /// In-transaction v3 archive overload — admin-strict ETag/If-Match optimistic-concurrency
    /// (ADR-019 pending). Reads the current row under <c>SELECT ... FOR UPDATE</c>, validates
    /// <paramref name="expectedVersion"/>, and applies the UPDATE with status='ARCHIVED' +
    /// <c>version = version + 1</c>. Already-ARCHIVED rows manifest as
    /// <see cref="OptimisticConcurrencyException"/>.
    /// </summary>
    /// <returns>
    /// <see cref="SaveAgreementConfigResult"/> with the archived entity, the new
    /// <c>version</c>, <c>IsCreated: false</c>, <c>ArchivedId: null</c>, and
    /// <c>PreviousStatus</c> = the FOR-UPDATE-locked pre-archive status (S121 / TASK-12100).
    /// </returns>
    /// <exception cref="OptimisticConcurrencyException">
    /// Thrown when the row is missing, already ARCHIVED, or its <c>version</c> column does
    /// not equal <paramref name="expectedVersion"/>.
    /// </exception>
    public async Task<SaveAgreementConfigResult> ArchiveAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid configId, long expectedVersion, string actorId,
        CancellationToken ct = default)
    {
        // 1. SELECT FOR UPDATE — capture status + version under the caller tx.
        long currentVersion;
        string currentStatus;
        await using (var lockCmd = new NpgsqlCommand(
            "SELECT version, status FROM agreement_configs WHERE config_id = @configId FOR UPDATE",
            conn, tx))
        {
            lockCmd.Parameters.AddWithValue("configId", configId);
            await using var reader = await lockCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new OptimisticConcurrencyException(
                    $"Agreement config {configId} not found.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            currentVersion = reader.GetInt64(0);
            currentStatus = reader.GetString(1);
        }

        // 2. Already-archived check — prevent double-archive.
        if (string.Equals(currentStatus, "ARCHIVED", StringComparison.Ordinal))
        {
            throw new OptimisticConcurrencyException(
                $"Agreement config {configId} is already ARCHIVED.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 3. Optimistic-concurrency check.
        if (currentVersion != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Agreement config {configId} version is {currentVersion}, but caller sent If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: currentVersion);
        }

        // 4. UPDATE with status='ARCHIVED' + version-bump; RETURN the post-write snapshot.
        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE agreement_configs
            SET status = 'ARCHIVED', archived_at = NOW(), updated_at = NOW(), version = version + 1
            WHERE config_id = @configId AND status != 'ARCHIVED'
            RETURNING *
            """, conn, tx);
        updateCmd.Parameters.AddWithValue("configId", configId);
        await using var updReader = await updateCmd.ExecuteReaderAsync(ct);
        if (!await updReader.ReadAsync(ct))
        {
            // Defense-in-depth — unreachable while FOR UPDATE holds the lock.
            throw new InvalidOperationException(
                $"ArchiveAsync produced no row for config_id={configId} at expected version {expectedVersion}; FOR UPDATE invariant violated.");
        }
        var entity = ReadEntity(updReader);
        // S121 / TASK-12100: surface the FOR-UPDATE-locked pre-archive status so the archive
        // endpoint can serialize an HONEST audit previousData (ACTIVE or DRAFT) — the locked
        // read is the authority, not the endpoint's pre-flight read (Step-0b convergent
        // BLOCKER both lenses). Result-shape extension only; SQL/locking/version semantics
        // byte-unchanged.
        return new SaveAgreementConfigResult(
            entity, entity.Version, IsCreated: false, ArchivedId: null,
            PreviousStatus: currentStatus);
    }

    public async Task AppendAuditAsync(
        Guid configId, string action, string? previousData, string? newData,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO agreement_config_audit (config_id, action, previous_data, new_data, actor_id, actor_role)
            VALUES (@configId, @action, @previousData::jsonb, @newData::jsonb, @actorId, @actorRole)
            """, conn);
        AddAuditParameters(cmd, configId, action, previousData, newData, actorId, actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction v2 audit overload (atomic-outbox primitive — preserved unchanged
    /// across the S25 / TASK-2503 v3 migration). Used by the Create endpoints and by S24
    /// ForcedRollbackHarness consumers; does NOT populate version_before / version_after
    /// (those columns are nullable per TASK-2501 schema migration). New mutating endpoints
    /// (Update / Publish / Archive) call the v3 sibling that captures the version
    /// transition.
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid configId, string action, string? previousData, string? newData,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO agreement_config_audit (config_id, action, previous_data, new_data, actor_id, actor_role)
            VALUES (@configId, @action, @previousData::jsonb, @newData::jsonb, @actorId, @actorRole)
            """, conn, tx);
        AddAuditParameters(cmd, configId, action, previousData, newData, actorId, actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// In-transaction v3 audit overload (S25 / TASK-2503 + ADR-019 pending). Writes the
    /// version-transition pair (<paramref name="versionBefore"/>, <paramref name="versionAfter"/>)
    /// into the new <c>version_before</c> / <c>version_after</c> columns added by TASK-2501.
    /// Closes the audit-replay gap where the v2 audit captured *what* changed but not
    /// *which version transition produced this state*.
    ///
    /// <para>
    /// <paramref name="versionBefore"/> is nullable so first-create paths (POST /create) can
    /// pass <c>null</c> while UPDATE paths pass the prior version. <paramref name="versionAfter"/>
    /// is the post-mutation version sourced from the v3 repo's
    /// <see cref="SaveAgreementConfigResult.Version"/>.
    /// </para>
    /// </summary>
    public async Task AppendAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid configId, string action, string? previousData, string? newData,
        string actorId, string actorRole,
        long? versionBefore, long versionAfter,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO agreement_config_audit
                (config_id, action, previous_data, new_data, actor_id, actor_role,
                 version_before, version_after)
            VALUES (@configId, @action, @previousData::jsonb, @newData::jsonb, @actorId, @actorRole,
                    @versionBefore, @versionAfter)
            """, conn, tx);
        AddAuditParameters(cmd, configId, action, previousData, newData, actorId, actorRole);
        cmd.Parameters.AddWithValue("versionBefore", (object?)versionBefore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("versionAfter", versionAfter);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void AddAuditParameters(
        NpgsqlCommand cmd, Guid configId, string action, string? previousData, string? newData,
        string actorId, string actorRole)
    {
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("previousData", (object?)previousData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("newData", (object?)newData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
    }

    private static void AddConfigParameters(NpgsqlCommand cmd, AgreementConfigEntity entity)
    {
        cmd.Parameters.AddWithValue("agreementCode", entity.AgreementCode);
        cmd.Parameters.AddWithValue("okVersion", entity.OkVersion);
        cmd.Parameters.AddWithValue("weeklyNormHours", entity.WeeklyNormHours);
        cmd.Parameters.AddWithValue("normPeriodWeeks", entity.NormPeriodWeeks);
        cmd.Parameters.AddWithValue("normModel", entity.NormModel.ToString());
        cmd.Parameters.AddWithValue("annualNormHours", entity.AnnualNormHours);
        cmd.Parameters.AddWithValue("maxFlexBalance", entity.MaxFlexBalance);
        cmd.Parameters.AddWithValue("flexCarryoverMax", entity.FlexCarryoverMax);
        cmd.Parameters.AddWithValue("hasOvertime", entity.HasOvertime);
        cmd.Parameters.AddWithValue("hasMerarbejde", entity.HasMerarbejde);
        cmd.Parameters.AddWithValue("overtimeThreshold50", entity.OvertimeThreshold50);
        cmd.Parameters.AddWithValue("overtimeThreshold100", entity.OvertimeThreshold100);
        cmd.Parameters.AddWithValue("eveningSupplementEnabled", entity.EveningSupplementEnabled);
        cmd.Parameters.AddWithValue("nightSupplementEnabled", entity.NightSupplementEnabled);
        cmd.Parameters.AddWithValue("weekendSupplementEnabled", entity.WeekendSupplementEnabled);
        cmd.Parameters.AddWithValue("holidaySupplementEnabled", entity.HolidaySupplementEnabled);
        cmd.Parameters.AddWithValue("eveningStart", entity.EveningStart);
        cmd.Parameters.AddWithValue("eveningEnd", entity.EveningEnd);
        cmd.Parameters.AddWithValue("nightStart", entity.NightStart);
        cmd.Parameters.AddWithValue("nightEnd", entity.NightEnd);
        cmd.Parameters.AddWithValue("eveningRate", entity.EveningRate);
        cmd.Parameters.AddWithValue("nightRate", entity.NightRate);
        cmd.Parameters.AddWithValue("weekendSaturdayRate", entity.WeekendSaturdayRate);
        cmd.Parameters.AddWithValue("weekendSundayRate", entity.WeekendSundayRate);
        cmd.Parameters.AddWithValue("holidayRate", entity.HolidayRate);
        cmd.Parameters.AddWithValue("onCallDutyEnabled", entity.OnCallDutyEnabled);
        cmd.Parameters.AddWithValue("onCallDutyRate", entity.OnCallDutyRate);
        cmd.Parameters.AddWithValue("callInWorkEnabled", entity.CallInWorkEnabled);
        cmd.Parameters.AddWithValue("callInMinimumHours", entity.CallInMinimumHours);
        cmd.Parameters.AddWithValue("callInRate", entity.CallInRate);
        cmd.Parameters.AddWithValue("travelTimeEnabled", entity.TravelTimeEnabled);
        cmd.Parameters.AddWithValue("workingTravelRate", entity.WorkingTravelRate);
        cmd.Parameters.AddWithValue("nonWorkingTravelRate", entity.NonWorkingTravelRate);
        cmd.Parameters.AddWithValue("maxDailyHours", entity.MaxDailyHours);
        cmd.Parameters.AddWithValue("minimumRestHours", entity.MinimumRestHours);
        cmd.Parameters.AddWithValue("restPeriodDerogationAllowed", entity.RestPeriodDerogationAllowed);
        cmd.Parameters.AddWithValue("weeklyMaxHoursReferencePeriod", entity.WeeklyMaxHoursReferencePeriod);
        cmd.Parameters.AddWithValue("voluntaryUnsocialHoursAllowed", entity.VoluntaryUnsocialHoursAllowed);
        cmd.Parameters.AddWithValue("defaultCompensationModel", entity.DefaultCompensationModel);
        cmd.Parameters.AddWithValue("employeeCompensationChoice", entity.EmployeeCompensationChoice);
        cmd.Parameters.AddWithValue("maxOvertimeHoursPerPeriod", entity.MaxOvertimeHoursPerPeriod);
        cmd.Parameters.AddWithValue("overtimeRequiresPreApproval", entity.OvertimeRequiresPreApproval);
    }

    private static async Task<IReadOnlyList<AgreementConfigEntity>> ReadEntitiesAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var entities = new List<AgreementConfigEntity>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            entities.Add(ReadEntity(reader));
        return entities;
    }

    private static AgreementConfigEntity ReadEntity(NpgsqlDataReader reader) => new()
    {
        ConfigId = reader.GetGuid(reader.GetOrdinal("config_id")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        Status = Enum.Parse<AgreementConfigStatus>(reader.GetString(reader.GetOrdinal("status"))),
        WeeklyNormHours = reader.GetDecimal(reader.GetOrdinal("weekly_norm_hours")),
        NormPeriodWeeks = reader.GetInt32(reader.GetOrdinal("norm_period_weeks")),
        NormModel = Enum.Parse<NormModel>(reader.GetString(reader.GetOrdinal("norm_model"))),
        AnnualNormHours = reader.GetDecimal(reader.GetOrdinal("annual_norm_hours")),
        MaxFlexBalance = reader.GetDecimal(reader.GetOrdinal("max_flex_balance")),
        FlexCarryoverMax = reader.GetDecimal(reader.GetOrdinal("flex_carryover_max")),
        HasOvertime = reader.GetBoolean(reader.GetOrdinal("has_overtime")),
        HasMerarbejde = reader.GetBoolean(reader.GetOrdinal("has_merarbejde")),
        OvertimeThreshold50 = reader.GetDecimal(reader.GetOrdinal("overtime_threshold_50")),
        OvertimeThreshold100 = reader.GetDecimal(reader.GetOrdinal("overtime_threshold_100")),
        EveningSupplementEnabled = reader.GetBoolean(reader.GetOrdinal("evening_supplement_enabled")),
        NightSupplementEnabled = reader.GetBoolean(reader.GetOrdinal("night_supplement_enabled")),
        WeekendSupplementEnabled = reader.GetBoolean(reader.GetOrdinal("weekend_supplement_enabled")),
        HolidaySupplementEnabled = reader.GetBoolean(reader.GetOrdinal("holiday_supplement_enabled")),
        EveningStart = reader.GetInt32(reader.GetOrdinal("evening_start")),
        EveningEnd = reader.GetInt32(reader.GetOrdinal("evening_end")),
        NightStart = reader.GetInt32(reader.GetOrdinal("night_start")),
        NightEnd = reader.GetInt32(reader.GetOrdinal("night_end")),
        EveningRate = reader.GetDecimal(reader.GetOrdinal("evening_rate")),
        NightRate = reader.GetDecimal(reader.GetOrdinal("night_rate")),
        WeekendSaturdayRate = reader.GetDecimal(reader.GetOrdinal("weekend_saturday_rate")),
        WeekendSundayRate = reader.GetDecimal(reader.GetOrdinal("weekend_sunday_rate")),
        HolidayRate = reader.GetDecimal(reader.GetOrdinal("holiday_rate")),
        OnCallDutyEnabled = reader.GetBoolean(reader.GetOrdinal("on_call_duty_enabled")),
        OnCallDutyRate = reader.GetDecimal(reader.GetOrdinal("on_call_duty_rate")),
        CallInWorkEnabled = reader.GetBoolean(reader.GetOrdinal("call_in_work_enabled")),
        CallInMinimumHours = reader.GetDecimal(reader.GetOrdinal("call_in_minimum_hours")),
        CallInRate = reader.GetDecimal(reader.GetOrdinal("call_in_rate")),
        TravelTimeEnabled = reader.GetBoolean(reader.GetOrdinal("travel_time_enabled")),
        WorkingTravelRate = reader.GetDecimal(reader.GetOrdinal("working_travel_rate")),
        NonWorkingTravelRate = reader.GetDecimal(reader.GetOrdinal("non_working_travel_rate")),
        MaxDailyHours = reader.GetDecimal(reader.GetOrdinal("max_daily_hours")),
        MinimumRestHours = reader.GetDecimal(reader.GetOrdinal("minimum_rest_hours")),
        RestPeriodDerogationAllowed = reader.GetBoolean(reader.GetOrdinal("rest_period_derogation_allowed")),
        WeeklyMaxHoursReferencePeriod = reader.GetInt32(reader.GetOrdinal("weekly_max_hours_reference_period")),
        VoluntaryUnsocialHoursAllowed = reader.GetBoolean(reader.GetOrdinal("voluntary_unsocial_hours_allowed")),
        DefaultCompensationModel = reader.GetString(reader.GetOrdinal("default_compensation_model")),
        EmployeeCompensationChoice = reader.GetBoolean(reader.GetOrdinal("employee_compensation_choice")),
        MaxOvertimeHoursPerPeriod = reader.GetDecimal(reader.GetOrdinal("max_overtime_hours_per_period")),
        OvertimeRequiresPreApproval = reader.GetBoolean(reader.GetOrdinal("overtime_requires_pre_approval")),
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
        PublishedAt = reader.IsDBNull(reader.GetOrdinal("published_at")) ? null : reader.GetDateTime(reader.GetOrdinal("published_at")),
        ArchivedAt = reader.IsDBNull(reader.GetOrdinal("archived_at")) ? null : reader.GetDateTime(reader.GetOrdinal("archived_at")),
        ClonedFromId = reader.IsDBNull(reader.GetOrdinal("cloned_from_id")) ? null : reader.GetGuid(reader.GetOrdinal("cloned_from_id")),
        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
    };
}

/// <summary>
/// Result of a save operation on <see cref="AgreementConfigRepository"/> (TASK-2502 / Phase 2
/// per-surface SaveResult — mirrors <c>SaveProfileResult</c> from
/// <see cref="LocalAgreementProfileRepository"/>). The S25 / TASK-2503 v3 mutating overloads
/// (<see cref="AgreementConfigRepository.UpdateDraftAsync(NpgsqlConnection, NpgsqlTransaction, Guid, long, AgreementConfigEntity, CancellationToken)"/>,
/// <see cref="AgreementConfigRepository.PublishAsync(NpgsqlConnection, NpgsqlTransaction, Guid, long, string, CancellationToken)"/>,
/// <see cref="AgreementConfigRepository.ArchiveAsync(NpgsqlConnection, NpgsqlTransaction, Guid, long, string, CancellationToken)"/>)
/// return this shape; endpoints set <c>ETag: "&lt;Version&gt;"</c> on the response and feed
/// <see cref="ArchivedId"/> into the publish-event payload.
/// </summary>
/// <param name="Config">The persisted agreement config entity (post-mutation snapshot).</param>
/// <param name="Version">The authoritative row-version after the save — first-insert is <c>1</c>;
/// each in-place UPDATE bumps by one. The wire ETag is <c>"&lt;version&gt;"</c> (RFC 7232 quoted)
/// per ADR-018 D7.</param>
/// <param name="IsCreated"><c>true</c> when this save inserted a new row (POST-style create);
/// <c>false</c> when it updated an existing row (PUT-style edit / Publish / Archive).</param>
/// <param name="ArchivedId">When the save executed the publish path, the <c>config_id</c> of the
/// prior-ACTIVE config that was archived as a side-effect (S24 Step 7a P1 semantic — preserves
/// the publish-archives-prior-ACTIVE atomicity for downstream audit). <c>null</c> on Update /
/// Archive paths or when no prior ACTIVE existed before publish.</param>
/// <param name="ArchivedVersion">When <see cref="ArchivedId"/> is non-null, the archived row's
/// new <c>version</c> after the publish-side archive UPDATE. The matching audit row's
/// <c>version_before</c> is <c>(ArchivedVersion - 1)</c> and <c>version_after</c> is
/// <c>ArchivedVersion</c> (per ADR-019 D8). Required by the publish endpoint to emit the
/// second (ARCHIVED) audit row + outbox event mandated by ADR-019 D1.</param>
/// <param name="PreviousStatus">S121 / TASK-12100 (sanctioned result-member extension): on the
/// v3 Archive path, the FOR-UPDATE-locked row's status BEFORE the archive UPDATE
/// (<c>ACTIVE</c> or <c>DRAFT</c> — the direct-archive surface admits both). The archive
/// endpoint uses it to serialize the audit row's <c>previous_data</c> from the locked truth
/// rather than the racy pre-flight read. <c>null</c> on all other construction sites
/// (Create / Update / Publish paths — default preserves those sites unchanged).</param>
public sealed record SaveAgreementConfigResult(
    AgreementConfigEntity Config,
    long Version,
    bool IsCreated,
    Guid? ArchivedId,
    long? ArchivedVersion = null,
    string? PreviousStatus = null);
