using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace StatsTid.Infrastructure;

/// <summary>
/// An institution's customary days (ADR-043 D9): the table <c>customary_days</c> and its
/// <c>customary_days_audit</c>. The two reads open their own connection; every write and lock runs
/// on the caller's connection and transaction, so the endpoint commits the row, its audit row, the
/// <c>CustomaryDayChanged</c> outbox event and the audit-projection row together (ADR-018 D3,
/// ADR-026 D2). Withdrawal and reinstatement are dated facts, never deletions: nothing here deletes
/// a row or edits a row's values other than its end. Every SQL statement is a literal constant.
/// </summary>
public sealed class CustomaryDayRepository
{
    // The audit row's previous_data and new_data: the row serialized camelCase.
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly DbConnectionFactory _connectionFactory;

    public CustomaryDayRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>Every row of the organisation, withdrawn ones included, ordered by
    /// <c>valid_from</c>, then the day (recurring days by month and day, then single dates).</summary>
    public async Task<IReadOnlyList<CustomaryDayRow>> GetByOrgAsync(string orgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM customary_days
            WHERE org_id = @orgId
            ORDER BY valid_from, recurring_month, recurring_day, single_date, custom_id
            """, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);

        var rows = new List<CustomaryDayRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(Read(reader));
        return rows;
    }

    /// <summary>True when at least one <c>agreement_configs</c> row carries exactly this code
    /// (compared by the column's equality, case-sensitive); the scope a custom may be narrowed to.
    /// A code containing NUL is false without a query: PostgreSQL text cannot hold NUL and refuses
    /// such a parameter (22021), so no row carries it.</summary>
    public async Task<bool> AgreementCodeExistsAsync(string agreementCode, CancellationToken ct = default)
    {
        if (agreementCode.Contains('\0'))
            return false;

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM agreement_configs WHERE agreement_code = @agreementCode)", conn);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    /// <summary>
    /// Locks the organisation's own row <c>FOR NO KEY UPDATE</c> while it is active, the first
    /// statement of every customs write; false when there is no active row to lock, and the caller
    /// then refuses the write. Two writers of one organisation's customs queue here, so the overlap
    /// check that follows (READ COMMITTED) sees what the earlier writer committed. <c>NO KEY UPDATE</c>
    /// does not conflict with the <c>FOR KEY SHARE</c> a foreign-key check takes, so inserts elsewhere
    /// that reference the organisation do not wait on it.
    /// <para>The scope check found the organisation active, but outside this transaction. The
    /// soft-delete locks the same row <c>FOR UPDATE</c> (<c>OrganizationRepository.LockActiveByIdAsync</c>),
    /// which conflicts with this lock: a delete that commits first is seen here, because READ
    /// COMMITTED re-checks <c>is_active</c> on the row it waited for, and a delete that comes second
    /// waits for this write's commit. So no customs write commits on an organisation already
    /// soft-deleted.</para>
    /// </summary>
    public async Task<bool> LockOrganisationAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string orgId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM organizations WHERE org_id = @orgId AND is_active = TRUE FOR NO KEY UPDATE", conn, tx);
        cmd.Parameters.AddWithValue("orgId", orgId);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    /// <summary>Locks one custom <c>FOR UPDATE</c>, only when it belongs to <paramref name="orgId"/>;
    /// null otherwise, so a row is reachable only through its own organisation's address.</summary>
    public async Task<CustomaryDayRow?> LockByIdAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string orgId, Guid customId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM customary_days WHERE custom_id = @customId AND org_id = @orgId FOR UPDATE", conn, tx);
        cmd.Parameters.AddWithValue("customId", customId);
        cmd.Parameters.AddWithValue("orgId", orgId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>
    /// The overlap guard: the id of a row of the same organisation, the same day (the same single
    /// date, or the same month and day) and the same agreement scope (both null, or equal) that is
    /// still in force on or after <see cref="NewCustomaryDay.ValidFrom"/> (<c>valid_to</c> null or
    /// later; end-exclusive, ADR-018 D8); null when there is none. The candidate is open-ended, so any
    /// such row overlaps it. Called under the organisation's row lock.
    /// </summary>
    public async Task<Guid?> FindOverlapAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, NewCustomaryDay candidate, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT custom_id FROM customary_days
            WHERE org_id = @orgId
              AND agreement_code IS NOT DISTINCT FROM @agreementCode
              AND single_date IS NOT DISTINCT FROM @singleDate
              AND recurring_month IS NOT DISTINCT FROM @recurringMonth
              AND recurring_day IS NOT DISTINCT FROM @recurringDay
              AND (valid_to IS NULL OR valid_to > @validFrom)
            ORDER BY valid_from, custom_id
            LIMIT 1
            """, conn, tx);
        cmd.Parameters.AddWithValue("orgId", candidate.OrgId);
        AddTyped(cmd, "agreementCode", NpgsqlDbType.Text, candidate.AgreementCode);
        AddTyped(cmd, "singleDate", NpgsqlDbType.Date, candidate.SingleDate);
        AddTyped(cmd, "recurringMonth", NpgsqlDbType.Smallint, (short?)candidate.RecurringMonth);
        AddTyped(cmd, "recurringDay", NpgsqlDbType.Smallint, (short?)candidate.RecurringDay);
        AddTyped(cmd, "validFrom", NpgsqlDbType.Date, candidate.ValidFrom);
        return await cmd.ExecuteScalarAsync(ct) is Guid id ? id : null;
    }

    /// <summary>Inserts a new open row (<c>version</c> 1, <c>valid_to</c> null) and returns it as stored.</summary>
    public async Task<CustomaryDayRow> InsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, NewCustomaryDay row, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO customary_days
                (org_id, agreement_code, recurring_month, recurring_day, single_date, kind, part,
                 part_from_time, part_hours, paid_mode, valid_from, created_by)
            VALUES
                (@orgId, @agreementCode, @recurringMonth, @recurringDay, @singleDate, @kind, @part,
                 @partFromTime, @partHours, @paidMode, @validFrom, @createdBy)
            RETURNING *
            """, conn, tx);
        cmd.Parameters.AddWithValue("orgId", row.OrgId);
        AddTyped(cmd, "agreementCode", NpgsqlDbType.Text, row.AgreementCode);
        AddTyped(cmd, "recurringMonth", NpgsqlDbType.Smallint, (short?)row.RecurringMonth);
        AddTyped(cmd, "recurringDay", NpgsqlDbType.Smallint, (short?)row.RecurringDay);
        AddTyped(cmd, "singleDate", NpgsqlDbType.Date, row.SingleDate);
        cmd.Parameters.AddWithValue("kind", row.Kind);
        cmd.Parameters.AddWithValue("part", row.Part);
        AddTyped(cmd, "partFromTime", NpgsqlDbType.Time, row.PartFromTime);
        AddTyped(cmd, "partHours", NpgsqlDbType.Numeric, row.PartHours);
        AddTyped(cmd, "paidMode", NpgsqlDbType.Text, row.PaidMode);
        AddTyped(cmd, "validFrom", NpgsqlDbType.Date, row.ValidFrom);
        cmd.Parameters.AddWithValue("createdBy", row.CreatedBy);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Read(reader);
    }

    /// <summary>Sets the end of an open row of the organisation (<c>version</c> + 1) and returns it as
    /// stored; null when no open row of that organisation has the id.</summary>
    public async Task<CustomaryDayRow?> WithdrawAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string orgId, Guid customId, DateOnly validTo,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE customary_days
            SET valid_to = @validTo, version = version + 1, updated_at = now()
            WHERE custom_id = @customId AND org_id = @orgId AND valid_to IS NULL
            RETURNING *
            """, conn, tx);
        AddTyped(cmd, "validTo", NpgsqlDbType.Date, validTo);
        cmd.Parameters.AddWithValue("customId", customId);
        cmd.Parameters.AddWithValue("orgId", orgId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>One <c>customary_days_audit</c> row (CREATED, WITHDRAWN or REINSTATED) on the row
    /// <paramref name="after"/>: the state before (null on a create) and after, as JSON, with the
    /// version pair.</summary>
    public async Task InsertAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string action,
        CustomaryDayRow? before, CustomaryDayRow after, long? versionBefore,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO customary_days_audit
                (custom_id, org_id, action, previous_data, new_data,
                 version_before, version_after, actor_id, actor_role)
            VALUES
                (@customId, @orgId, @action, @previousData, @newData,
                 @versionBefore, @versionAfter, @actorId, @actorRole)
            """, conn, tx);
        cmd.Parameters.AddWithValue("customId", after.CustomId);
        cmd.Parameters.AddWithValue("orgId", after.OrgId);
        cmd.Parameters.AddWithValue("action", action);
        AddTyped(cmd, "previousData", NpgsqlDbType.Jsonb,
            before is null ? null : JsonSerializer.Serialize(before, AuditJson));
        AddTyped(cmd, "newData", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(after, AuditJson));
        AddTyped(cmd, "versionBefore", NpgsqlDbType.Bigint, versionBefore);
        cmd.Parameters.AddWithValue("versionAfter", after.Version);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Typed, so a null compares and inserts with the column's type.
    private static void AddTyped(NpgsqlCommand cmd, string name, NpgsqlDbType type, object? value) =>
        cmd.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    private static CustomaryDayRow Read(NpgsqlDataReader reader)
    {
        int? NullableSmallint(string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetInt16(ordinal);
        }
        T? NullableValue<T>(string column) where T : struct
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);
        }
        string? NullableString(string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        return new CustomaryDayRow(
            CustomId: reader.GetGuid(reader.GetOrdinal("custom_id")),
            OrgId: reader.GetString(reader.GetOrdinal("org_id")),
            AgreementCode: NullableString("agreement_code"),
            RecurringMonth: NullableSmallint("recurring_month"),
            RecurringDay: NullableSmallint("recurring_day"),
            SingleDate: NullableValue<DateOnly>("single_date"),
            Kind: reader.GetString(reader.GetOrdinal("kind")),
            Part: reader.GetString(reader.GetOrdinal("part")),
            PartFromTime: NullableValue<TimeOnly>("part_from_time"),
            PartHours: NullableValue<decimal>("part_hours"),
            PaidMode: NullableString("paid_mode"),
            ValidFrom: reader.GetFieldValue<DateOnly>(reader.GetOrdinal("valid_from")),
            ValidTo: NullableValue<DateOnly>("valid_to"),
            Version: reader.GetInt64(reader.GetOrdinal("version")),
            CreatedBy: reader.GetString(reader.GetOrdinal("created_by")),
            CreatedAt: reader.GetDateTime(reader.GetOrdinal("created_at")),
            UpdatedAt: reader.GetDateTime(reader.GetOrdinal("updated_at")));
    }
}

/// <summary>A stored <c>customary_days</c> row. The day is either recurring
/// (<see cref="RecurringMonth"/> + <see cref="RecurringDay"/>) or a <see cref="SingleDate"/>;
/// <see cref="ValidTo"/> is end-exclusive (ADR-018 D8), null while the custom is in force.</summary>
public sealed record CustomaryDayRow(
    Guid CustomId,
    string OrgId,
    string? AgreementCode,
    int? RecurringMonth,
    int? RecurringDay,
    DateOnly? SingleDate,
    string Kind,
    string Part,
    TimeOnly? PartFromTime,
    decimal? PartHours,
    string? PaidMode,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    long Version,
    string CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>The values of a row to insert, already validated by the endpoint: a create's request, or
/// a reinstatement's copy of the withdrawn row from a new <see cref="ValidFrom"/>.</summary>
public sealed record NewCustomaryDay(
    string OrgId,
    string? AgreementCode,
    int? RecurringMonth,
    int? RecurringDay,
    DateOnly? SingleDate,
    string Kind,
    string Part,
    TimeOnly? PartFromTime,
    decimal? PartHours,
    string? PaidMode,
    DateOnly ValidFrom,
    string CreatedBy);
