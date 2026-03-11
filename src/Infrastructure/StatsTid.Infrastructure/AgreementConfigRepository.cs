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
        var configId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
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
                @createdBy, @description, @clonedFromId,
                NOW(), NOW()
            )
            """, conn);
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("status", status);
        AddConfigParameters(cmd, entity);
        cmd.Parameters.AddWithValue("createdBy", entity.CreatedBy);
        cmd.Parameters.AddWithValue("description", (object?)entity.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("clonedFromId", (object?)entity.ClonedFromId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
        return configId;
    }

    public async Task<bool> UpdateDraftAsync(Guid configId, AgreementConfigEntity updated, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
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
                description = @description,
                updated_at = NOW()
            WHERE config_id = @configId AND status = 'DRAFT'
            """, conn);
        cmd.Parameters.AddWithValue("configId", configId);
        AddConfigParameters(cmd, updated);
        cmd.Parameters.AddWithValue("description", (object?)updated.Description ?? DBNull.Value);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
    }

    public async Task<Guid?> PublishAsync(Guid configId, string actorId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            // Get the config being published to find its agreement_code and ok_version
            Guid? archivedId = null;
            await using (var getCmd = new NpgsqlCommand(
                "SELECT agreement_code, ok_version FROM agreement_configs WHERE config_id = @configId", conn, tx))
            {
                getCmd.Parameters.AddWithValue("configId", configId);
                await using var reader = await getCmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                {
                    await tx.RollbackAsync(ct);
                    return null;
                }
                var agreementCode = reader.GetString(0);
                var okVersion = reader.GetString(1);
                await reader.CloseAsync();

                // Archive the current ACTIVE config for the same (code, version)
                await using var archiveCmd = new NpgsqlCommand(
                    """
                    UPDATE agreement_configs
                    SET status = 'ARCHIVED', archived_at = NOW(), updated_at = NOW()
                    WHERE agreement_code = @agreementCode AND ok_version = @okVersion AND status = 'ACTIVE'
                    RETURNING config_id
                    """, conn, tx);
                archiveCmd.Parameters.AddWithValue("agreementCode", agreementCode);
                archiveCmd.Parameters.AddWithValue("okVersion", okVersion);
                var result = await archiveCmd.ExecuteScalarAsync(ct);
                if (result is Guid archivedGuid)
                    archivedId = archivedGuid;
            }

            // Set this config to ACTIVE
            await using var publishCmd = new NpgsqlCommand(
                """
                UPDATE agreement_configs
                SET status = 'ACTIVE', published_at = NOW(), updated_at = NOW()
                WHERE config_id = @configId AND status = 'DRAFT'
                """, conn, tx);
            publishCmd.Parameters.AddWithValue("configId", configId);
            var publishedRows = await publishCmd.ExecuteNonQueryAsync(ct);

            if (publishedRows == 0)
            {
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

    public async Task<bool> ArchiveAsync(Guid configId, string actorId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE agreement_configs
            SET status = 'ARCHIVED', archived_at = NOW(), updated_at = NOW()
            WHERE config_id = @configId AND status != 'ARCHIVED'
            """, conn);
        cmd.Parameters.AddWithValue("configId", configId);
        var rows = await cmd.ExecuteNonQueryAsync(ct);
        return rows > 0;
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
        cmd.Parameters.AddWithValue("configId", configId);
        cmd.Parameters.AddWithValue("action", action);
        cmd.Parameters.AddWithValue("previousData", (object?)previousData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("newData", (object?)newData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
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
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at")),
        PublishedAt = reader.IsDBNull(reader.GetOrdinal("published_at")) ? null : reader.GetDateTime(reader.GetOrdinal("published_at")),
        ArchivedAt = reader.IsDBNull(reader.GetOrdinal("archived_at")) ? null : reader.GetDateTime(reader.GetOrdinal("archived_at")),
        ClonedFromId = reader.IsDBNull(reader.GetOrdinal("cloned_from_id")) ? null : reader.GetGuid(reader.GetOrdinal("cloned_from_id")),
        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
    };
}
