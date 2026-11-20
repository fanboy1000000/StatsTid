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

    /// <summary>
    /// The version of (<paramref name="agreementCode"/>, <paramref name="okVersion"/>) in force on
    /// <paramref name="day"/> (QUAL-182, ADR-043 D12): the PUBLISHED version (<c>published_at IS NOT NULL</c>,
    /// either status: an ARCHIVED version stays in force for its own days) with the latest
    /// <c>applies_from</c> on or before the day; of two dated the same day, the one published last (NR-P11).
    /// <c>null</c> when no published version applies yet: never the ACTIVE row, a draft or a compiled value.
    /// A DRAFT, even one archived unpublished, never counts (B-5).
    /// </summary>
    public async Task<AgreementConfigEntity?> GetInForceAsync(
        string agreementCode, string okVersion, DateOnly day, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM agreement_configs
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion
              AND published_at IS NOT NULL
              AND applies_from <= @day
            ORDER BY applies_from DESC, published_at DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("day", day);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadEntity(reader) : null;
    }

    /// <summary>
    /// The planner's read (QUAL-182; payroll's <c>AgreementConfigPromotions</c>): every distinct
    /// <c>applies_from</c> of a PUBLISHED version of the key inside <c>(afterExclusive, toInclusive]</c>, in date
    /// order. The lower bound is strict: a version dated the first day of the caller's run is that run's
    /// starting state, not a boundary.
    /// </summary>
    public async Task<IReadOnlyList<DateOnly>> GetPublishedAppliesFromDatesAsync(
        string agreementCode, string okVersion, DateOnly afterExclusive, DateOnly toInclusive,
        CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT DISTINCT applies_from FROM agreement_configs
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion
              AND published_at IS NOT NULL
              AND applies_from > @afterExclusive AND applies_from <= @toInclusive
            ORDER BY applies_from
            """, conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        cmd.Parameters.AddWithValue("afterExclusive", afterExclusive);
        cmd.Parameters.AddWithValue("toInclusive", toInclusive);
        var dates = new List<DateOnly>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            dates.Add(reader.GetFieldValue<DateOnly>(0));
        return dates;
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
            max_flex_balance,
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
            created_by, description, cloned_from_id, applies_from,
            created_at, updated_at
        ) VALUES (
            @configId, @agreementCode, @okVersion, @status,
            @weeklyNormHours, @normPeriodWeeks, @normModel, @annualNormHours,
            @maxFlexBalance,
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
            @createdBy, @description, @clonedFromId, @appliesFrom,
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
    /// In-transaction v3 publish overload — admin-strict ETag/If-Match optimistic-concurrency
    /// (ADR-019 pending). Publishes the DRAFT identified by <paramref name="configId"/> as a dated
    /// version of its (agreement_code, ok_version) that applies from <paramref name="appliesFrom"/>
    /// (QUAL-182, ADR-043 D12), and archives the prior ACTIVE config, which stays in force for its own
    /// days (the dated read, <see cref="GetInForceAsync"/>). Every statement runs on the caller-supplied
    /// <paramref name="conn"/> + <paramref name="tx"/>. The caller commits or rolls back; this method
    /// does NOT.
    ///
    /// <para>
    /// The order (TASK-14818 items 4.4, 4.5, 5; ruling B4): the key lock first — every row of the
    /// (agreement_code, ok_version) <c>FOR UPDATE</c> in id order, so any number of concurrent publishes
    /// of one key take turns; then the draft's status and version (412); then the predecessor, the
    /// latest PUBLISHED version of either status (B-5), read after the lock so it is the committed one;
    /// then the date rules (<see cref="AppliesFromRules"/>): not before the predecessor, and a Monday when
    /// a Monday-only field changes compared with it (a key's first version changes nothing, B3). Only
    /// then the two UPDATEs. The activation stamps <c>published_at = clock_timestamp()</c>, the moment it
    /// takes effect, never the transaction's start (NR-P16), so of two versions dated the same day the
    /// one that took effect last is in force (NR-P11).
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
    /// <exception cref="AppliesFromRefusedException">
    /// Thrown, before anything is written, when <paramref name="appliesFrom"/> precedes the predecessor's
    /// date or misses a Monday the changed fields need.
    /// </exception>
    public async Task<SaveAgreementConfigResult> PublishAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        Guid configId, long expectedVersion, DateOnly appliesFrom, string actorId,
        CancellationToken ct = default)
    {
        // 1. The draft's key, read without a lock (a row's agreement_code and ok_version never change).
        string agreementCode;
        string okVersion;
        await using (var keyCmd = new NpgsqlCommand(
            "SELECT agreement_code, ok_version FROM agreement_configs WHERE config_id = @configId",
            conn, tx))
        {
            keyCmd.Parameters.AddWithValue("configId", configId);
            await using var reader = await keyCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                throw new OptimisticConcurrencyException(
                    $"Agreement config {configId} not found.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            agreementCode = reader.GetString(0);
            okVersion = reader.GetString(1);
        }

        // 2. The key lock (ruling B4) — the first lock this repository takes (the route took the
        //    pay-inputs lock before it).
        await LockKeyAsync(conn, tx, agreementCode, okVersion, ct);

        // 3. The draft under the lock (its row is one of the key's): status + version, and the values the
        //    Monday rule compares.
        AgreementConfigEntity draft;
        await using (var draftCmd = new NpgsqlCommand(
            "SELECT * FROM agreement_configs WHERE config_id = @configId FOR UPDATE",
            conn, tx))
        {
            draftCmd.Parameters.AddWithValue("configId", configId);
            await using var reader = await draftCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                // Defense-in-depth — the row existed at step 1 and no writer deletes one.
                throw new OptimisticConcurrencyException(
                    $"Agreement config {configId} not found.",
                    expectedVersion: expectedVersion,
                    actualVersion: null);
            }
            draft = ReadEntity(reader);
        }

        // Status check — only DRAFT is publishable. Concurrent publish/archive between
        // endpoint pre-check and the lock (S24 Step 7a P1 race) → 412.
        if (draft.Status != AgreementConfigStatus.DRAFT)
        {
            throw new OptimisticConcurrencyException(
                $"Agreement config {configId} is no longer in DRAFT status (current: {draft.Status}); cannot publish — refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: draft.Version);
        }

        // Optimistic-concurrency check.
        if (draft.Version != expectedVersion)
        {
            throw new OptimisticConcurrencyException(
                $"Agreement config {configId} version is {draft.Version}, but caller sent If-Match: \"{expectedVersion}\"; refresh and retry.",
                expectedVersion: expectedVersion,
                actualVersion: draft.Version);
        }

        // 4. The predecessor and the date rules, decided under the key lock.
        var predecessor = await ReadPredecessorAsync(conn, tx, agreementCode, okVersion, ct);
        if (predecessor is not null)
        {
            AppliesFromRules.RequireNotBeforePredecessor(appliesFrom, predecessor.AppliesFrom);
            AppliesFromRules.RequireMondayForChangedFields(appliesFrom, ChangedMondayOnlyFields(draft, predecessor));
        }

        // 5. Archive the current ACTIVE config for the same (agreement_code, ok_version).
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

        // 6. Activate the DRAFT as the version applying from appliesFrom — bump version + return the
        //    post-write snapshot. published_at = clock_timestamp(): the moment of activation, after the
        //    key lock, never NOW() (the transaction's start; NR-P16).
        await using var publishCmd = new NpgsqlCommand(
            """
            UPDATE agreement_configs
            SET status = 'ACTIVE', applies_from = @appliesFrom, published_at = clock_timestamp(),
                updated_at = NOW(), version = version + 1
            WHERE config_id = @configId AND status = 'DRAFT'
            RETURNING *
            """, conn, tx);
        publishCmd.Parameters.AddWithValue("configId", configId);
        publishCmd.Parameters.AddWithValue("appliesFrom", appliesFrom);
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

    /// <summary>
    /// In-transaction v3 archive overload — admin-strict ETag/If-Match optimistic-concurrency
    /// (ADR-019 pending). Reads the current row under <c>SELECT ... FOR UPDATE</c>, validates
    /// <paramref name="expectedVersion"/>, and applies the UPDATE with status='ARCHIVED' +
    /// <c>version = version + 1</c>. Only a DRAFT is archived: an already-ARCHIVED row manifests as
    /// <see cref="OptimisticConcurrencyException"/>, and an ACTIVE one is refused (A-6): under dated
    /// versions, archiving it would bring an older version back into force, so it is replaced by
    /// publishing a successor. Both are decided on the locked status, before the version check (B7).
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
    /// <exception cref="ActiveConfigArchiveRefusedException">
    /// Thrown, before anything is written, when the locked row is ACTIVE.
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

        // 2b. An ACTIVE config is replaced, not archived (A-6): refused whatever the If-Match (B7).
        if (string.Equals(currentStatus, "ACTIVE", StringComparison.Ordinal))
            throw new ActiveConfigArchiveRefusedException(configId);

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
        // endpoint serializes an HONEST audit previousData — the locked read is the authority,
        // not the endpoint's pre-flight read. Only a DRAFT reaches this line (A-6, step 2b).
        return new SaveAgreementConfigResult(
            entity, entity.Version, IsCreated: false, ArchivedId: null,
            PreviousStatus: currentStatus);
    }

    /// <summary>
    /// The key lock (TASK-14818 item 4.4, ruling B4): every row of (<paramref name="agreementCode"/>,
    /// <paramref name="okVersion"/>) <c>FOR UPDATE</c>, in id order, as the first lock this repository takes
    /// in a publish. Every publish of the key takes the same rows in the same order, so they take turns
    /// without deadlocking, and a waiter wakes holding all of them (a row it waited for still belongs to the
    /// key: the key columns never change). Its next statement, at READ COMMITTED, sees the publish it waited
    /// for. In Block C's lock order (S148 PIL) this is "the config row FOR UPDATE", the publish's second
    /// lock: the publish route takes the pay-inputs lock (<see cref="PayInputsLock"/>) first, at its
    /// transaction head, and lands the worklist rows last.
    /// </summary>
    private static async Task LockKeyAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string agreementCode, string okVersion, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT config_id FROM agreement_configs
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion
            ORDER BY config_id
            FOR UPDATE
            """, conn, tx);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            // Each row fetched is locked; reading them all takes the whole key.
        }
    }

    /// <summary>
    /// The predecessor of a new version of the key (A-6, B-5): the latest PUBLISHED version of either status
    /// (<c>published_at IS NOT NULL</c>; a draft, even one archived unpublished, is never one), in the in-force
    /// read's order without its day bound. Read under the key lock.
    /// </summary>
    private static async Task<AgreementConfigEntity?> ReadPredecessorAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string agreementCode, string okVersion, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM agreement_configs
            WHERE agreement_code = @agreementCode AND ok_version = @okVersion
              AND published_at IS NOT NULL
            ORDER BY applies_from DESC, published_at DESC
            LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("okVersion", okVersion);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadEntity(reader) : null;
    }

    /// <summary>
    /// The central config's Monday-only fields that <paramref name="draft"/> changes compared with its
    /// predecessor: the closed list of TASK-14818 item 4.5 (S148-wave4C-briefs-adjudication, ruling 6).
    /// <c>HasOvertime</c>, <c>HasMerarbejde</c> and <c>NormModel</c> because the engine judges a whole week by
    /// its latest evaluated day (<c>WeeklyTimelineCore</c>); <c>WeeklyNormHours</c> on ADR-017 D9a's unretired
    /// premise (<c>NORM_CHECK_WEEKLY</c> is still an aligned window; ADR-043 provisional item 18 lifts it).
    /// Every other member may change on any weekday.
    /// </summary>
    private static List<string> ChangedMondayOnlyFields(AgreementConfigEntity draft, AgreementConfigEntity predecessor)
    {
        var changed = new List<string>();
        if (draft.WeeklyNormHours != predecessor.WeeklyNormHours)
            changed.Add(nameof(AgreementConfigEntity.WeeklyNormHours));
        if (draft.HasOvertime != predecessor.HasOvertime)
            changed.Add(nameof(AgreementConfigEntity.HasOvertime));
        if (draft.HasMerarbejde != predecessor.HasMerarbejde)
            changed.Add(nameof(AgreementConfigEntity.HasMerarbejde));
        if (draft.NormModel != predecessor.NormModel)
            changed.Add(nameof(AgreementConfigEntity.NormModel));
        return changed;
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
        cmd.Parameters.AddWithValue("appliesFrom", entity.AppliesFrom);
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
        AppliesFrom = reader.GetFieldValue<DateOnly>(reader.GetOrdinal("applies_from")),
        Version = reader.GetInt64(reader.GetOrdinal("version")),
    };
}

/// <summary>
/// Result of a save operation on <see cref="AgreementConfigRepository"/> (TASK-2502 / Phase 2
/// per-surface SaveResult — mirrors <c>SaveProfileResult</c> from
/// <see cref="LocalAgreementProfileRepository"/>). The S25 / TASK-2503 v3 mutating overloads
/// (<see cref="AgreementConfigRepository.UpdateDraftAsync(NpgsqlConnection, NpgsqlTransaction, Guid, long, AgreementConfigEntity, CancellationToken)"/>,
/// <see cref="AgreementConfigRepository.PublishAsync(NpgsqlConnection, NpgsqlTransaction, Guid, long, DateOnly, string, CancellationToken)"/>,
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
/// v3 Archive path, the FOR-UPDATE-locked row's status BEFORE the archive UPDATE (always
/// <c>DRAFT</c>: an ACTIVE row is refused, A-6). The archive endpoint uses it to serialize the
/// audit row's <c>previous_data</c> from the locked truth rather than the racy pre-flight read.
/// <c>null</c> on all other construction sites (Create / Update / Publish paths).</param>
public sealed record SaveAgreementConfigResult(
    AgreementConfigEntity Config,
    long Version,
    bool IsCreated,
    Guid? ArchivedId,
    long? ArchivedVersion = null,
    string? PreviousStatus = null);

/// <summary>
/// An archive of an ACTIVE agreement config, refused (A-6, TASK-14818 item 8): under dated versions,
/// archiving the active version would silently bring an older one back into force, so it is replaced by
/// publishing a successor. Thrown by <see cref="AgreementConfigRepository.ArchiveAsync"/> on the locked
/// status, before anything is written; the archive endpoint maps it to 409.
/// </summary>
public sealed class ActiveConfigArchiveRefusedException : Exception
{
    public ActiveConfigArchiveRefusedException(Guid configId)
        : base($"Agreement config {configId} is ACTIVE; an active configuration is replaced by publishing a successor, not archived.")
    {
        ConfigId = configId;
    }

    public Guid ConfigId { get; }
}

/// <summary>
/// One refused field of a configuration write's applies-from date, in the profile PUT's error shape
/// (ADR-017 D9a: <c>field</c>, <c>code</c>, <c>nearestValid</c> as ISO dates).
/// </summary>
public sealed record AppliesFromFieldError(string Field, string Code, IReadOnlyList<DateOnly> NearestValid);

/// <summary>
/// A configuration write's applies-from date, refused inside its transaction after the key lock
/// (TASK-14818 items 4.4, 4.5; ADR-043 D12): <c>BEFORE_PREDECESSOR</c> or <c>NOT_MONDAY_ALIGNED</c>. Thrown
/// before any row is written, so the caller's rollback leaves nothing; the agreement-config publish and
/// the four position-override writes map it to 400.
/// </summary>
public sealed class AppliesFromRefusedException : Exception
{
    /// <summary>The field the date's own errors name: the requests' <c>appliesFrom</c> member.</summary>
    public const string DateField = "AppliesFrom";

    public AppliesFromRefusedException(IReadOnlyList<AppliesFromFieldError> errors)
        : base("The applies-from date was refused: " + string.Join(", ", errors.Select(e => $"{e.Field} {e.Code}")))
    {
        Errors = errors;
    }

    public IReadOnlyList<AppliesFromFieldError> Errors { get; }
}

/// <summary>
/// The two date rules a dated configuration write decides under its key lock (TASK-14818 items 4.4, 4.5),
/// shared by <see cref="AgreementConfigRepository"/> and <see cref="PositionOverrideRepository"/>. The
/// third rule, "never in the future", needs no database and is the endpoints'
/// (<c>ProfileAlignmentValidator.ValidateEffectiveFromTemporality</c>, R-18).
/// </summary>
internal static class AppliesFromRules
{
    /// <summary>
    /// "Not before the predecessor" (A-6, AC-C2q): a new version may not precede the newest version of its
    /// key. An equal date is accepted, and the later write is then the one in force (NR-P11).
    /// </summary>
    internal static void RequireNotBeforePredecessor(DateOnly appliesFrom, DateOnly predecessorDate)
    {
        if (appliesFrom < predecessorDate)
        {
            throw new AppliesFromRefusedException(new[]
            {
                new AppliesFromFieldError(
                    AppliesFromRefusedException.DateField, "BEFORE_PREDECESSOR", new[] { predecessorDate }),
            });
        }
    }

    /// <summary>
    /// The Monday rule (A-16): every field of <paramref name="changedFields"/> (the caller's closed list,
    /// TASK-14818 item 4.5) changes the weekly basis, so its new value must start on a Monday. Refused per
    /// field with the local profile's own computation and code (ADR-017 D9a,
    /// <see cref="LocalAgreementProfileAlignmentPolicies"/>): <c>NOT_MONDAY_ALIGNED</c>, nearest valid
    /// [previous Monday, next Monday].
    /// </summary>
    internal static void RequireMondayForChangedFields(DateOnly appliesFrom, IReadOnlyList<string> changedFields)
    {
        // shortcut(QUAL-BF-007): the Monday rule for week-read fields (A-16); upgrade when stage 1 removes the weekly basis
        if (changedFields.Count == 0)
            return;

        var monday = LocalAgreementProfileAlignmentPolicies.ByFieldName["WeeklyNormHours"](appliesFrom);
        if (monday.IsAligned)
            return;

        throw new AppliesFromRefusedException(changedFields
            .Select(field => new AppliesFromFieldError(field, monday.ErrorCode!, monday.NearestValidDates!))
            .ToList());
    }
}
