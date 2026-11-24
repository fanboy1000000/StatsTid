using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace StatsTid.Infrastructure;

/// <summary>
/// The two facts HR records about a person (ADR-043 D4): the overtime regime and whether the person
/// is on the flex scheme, in the dated table <c>employee_hr_facts</c> and its
/// <c>employee_hr_facts_audit</c>. Rows are end-exclusive (ADR-018 D8); at most one row per person
/// is open (<c>effective_to</c> null). The two reads open their own connection; every lock and write
/// runs on the caller's connection and transaction, so the endpoint commits the row, its audit row,
/// the <c>EmployeeHrFactsChanged</c> outbox event and the audit-projection row together (ADR-018 D3,
/// ADR-026 D2). Nothing here deletes a row. Every SQL statement is a literal constant.
/// </summary>
public sealed class EmployeeHrFactsRepository
{
    // The audit row's previous_data and new_data: the row serialized camelCase.
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private readonly DbConnectionFactory _connectionFactory;

    public EmployeeHrFactsRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// The row covering <paramref name="day"/> (<c>effective_from &lt;= day</c> and <c>effective_to</c>
    /// null or after it; end-exclusive), or null when none does. One deterministic row: the latest
    /// start wins, the discipline of <see cref="UserAgreementCodeRepository.GetByUserIdAtAsync"/>, since
    /// non-overlap of dated rows is a writer-side invariant only.
    /// </summary>
    public async Task<EmployeeHrFactsRow?> GetCoveringAsync(string employeeId, DateOnly day, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            SELECT * FROM employee_hr_facts
            WHERE employee_id = @employeeId
              AND effective_from <= @day
              AND (effective_to IS NULL OR effective_to > @day)
            ORDER BY effective_from DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        AddTyped(cmd, "day", NpgsqlDbType.Date, day);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>The person's open row, or null when none is open.</summary>
    public async Task<EmployeeHrFactsRow?> GetLiveAsync(string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM employee_hr_facts WHERE employee_id = @employeeId AND effective_to IS NULL", conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    /// <summary>
    /// Locks every row of the person <c>FOR UPDATE</c> and returns the open one, or null when none is
    /// open. The writer takes it after the person's <c>users</c> row, so two writers for one person
    /// queue on that row first and the second one sees what the first committed.
    /// </summary>
    public async Task<EmployeeHrFactsRow?> LockReturningLiveAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM employee_hr_facts WHERE employee_id = @employeeId ORDER BY effective_from FOR UPDATE",
            conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        EmployeeHrFactsRow? live = null;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var row = Read(reader);
            if (row.EffectiveTo is null)
                live = row;
        }
        return live;
    }

    /// <summary>Inserts an open row at <paramref name="version"/> and returns it as stored.</summary>
    public async Task<EmployeeHrFactsRow> InsertAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, string? overtimeRegime, bool? onFlexScheme,
        DateOnly effectiveFrom, long version, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_hr_facts (employee_id, overtime_regime, on_flex_scheme, effective_from, version)
            VALUES (@employeeId, @overtimeRegime, @onFlexScheme, @effectiveFrom, @version)
            RETURNING *
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        AddTyped(cmd, "overtimeRegime", NpgsqlDbType.Text, overtimeRegime);
        AddTyped(cmd, "onFlexScheme", NpgsqlDbType.Boolean, onFlexScheme);
        AddTyped(cmd, "effectiveFrom", NpgsqlDbType.Date, effectiveFrom);
        cmd.Parameters.AddWithValue("version", version);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Read(reader);
    }

    /// <summary>Sets the two values of a row (<c>version</c> + 1) and returns it as stored. The caller
    /// holds the row's lock.</summary>
    public async Task<EmployeeHrFactsRow> UpdateInPlaceAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid factsId, string? overtimeRegime, bool? onFlexScheme,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE employee_hr_facts
            SET overtime_regime = @overtimeRegime, on_flex_scheme = @onFlexScheme,
                version = version + 1, updated_at = now()
            WHERE facts_id = @factsId
            RETURNING *
            """, conn, tx);
        AddTyped(cmd, "overtimeRegime", NpgsqlDbType.Text, overtimeRegime);
        AddTyped(cmd, "onFlexScheme", NpgsqlDbType.Boolean, onFlexScheme);
        cmd.Parameters.AddWithValue("factsId", factsId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException($"HR facts row {factsId} was not found under its row lock.");
        return Read(reader);
    }

    /// <summary>Ends a row on <paramref name="effectiveTo"/> (end-exclusive). Its version is unchanged:
    /// the successor carries the next version. The caller holds the row's lock.</summary>
    public async Task CloseAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, Guid factsId, DateOnly effectiveTo, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "UPDATE employee_hr_facts SET effective_to = @effectiveTo, updated_at = now() WHERE facts_id = @factsId",
            conn, tx);
        AddTyped(cmd, "effectiveTo", NpgsqlDbType.Date, effectiveTo);
        cmd.Parameters.AddWithValue("factsId", factsId);
        if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException($"HR facts row {factsId} was not found under its row lock.");
    }

    /// <summary>One <c>employee_hr_facts_audit</c> row (CREATED, UPDATED or SUPERSEDED) on the row
    /// <paramref name="after"/>: the row before the write (null on a create) and after, as JSON, with
    /// the version pair.</summary>
    public async Task InsertAuditAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string action,
        EmployeeHrFactsRow? before, EmployeeHrFactsRow after,
        string actorId, string actorRole, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO employee_hr_facts_audit
                (facts_id, employee_id, action, previous_data, new_data,
                 version_before, version_after, actor_id, actor_role)
            VALUES
                (@factsId, @employeeId, @action, @previousData, @newData,
                 @versionBefore, @versionAfter, @actorId, @actorRole)
            """, conn, tx);
        cmd.Parameters.AddWithValue("factsId", after.FactsId);
        cmd.Parameters.AddWithValue("employeeId", after.EmployeeId);
        cmd.Parameters.AddWithValue("action", action);
        AddTyped(cmd, "previousData", NpgsqlDbType.Jsonb,
            before is null ? null : JsonSerializer.Serialize(before, AuditJson));
        AddTyped(cmd, "newData", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(after, AuditJson));
        AddTyped(cmd, "versionBefore", NpgsqlDbType.Bigint, before?.Version);
        cmd.Parameters.AddWithValue("versionAfter", after.Version);
        cmd.Parameters.AddWithValue("actorId", actorId);
        cmd.Parameters.AddWithValue("actorRole", actorRole);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Typed, so a null inserts and compares with the column's type.
    private static void AddTyped(NpgsqlCommand cmd, string name, NpgsqlDbType type, object? value) =>
        cmd.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    private static EmployeeHrFactsRow Read(NpgsqlDataReader reader)
    {
        var regime = reader.GetOrdinal("overtime_regime");
        var flex = reader.GetOrdinal("on_flex_scheme");
        var to = reader.GetOrdinal("effective_to");
        return new EmployeeHrFactsRow(
            FactsId: reader.GetGuid(reader.GetOrdinal("facts_id")),
            EmployeeId: reader.GetString(reader.GetOrdinal("employee_id")),
            OvertimeRegime: reader.IsDBNull(regime) ? null : reader.GetString(regime),
            OnFlexScheme: reader.IsDBNull(flex) ? null : reader.GetBoolean(flex),
            EffectiveFrom: reader.GetFieldValue<DateOnly>(reader.GetOrdinal("effective_from")),
            EffectiveTo: reader.IsDBNull(to) ? null : reader.GetFieldValue<DateOnly>(to),
            Version: reader.GetInt64(reader.GetOrdinal("version")));
    }
}

/// <summary>A stored <c>employee_hr_facts</c> row. A null value is "not recorded";
/// <see cref="EffectiveTo"/> is end-exclusive (ADR-018 D8), null while the row is open.</summary>
public sealed record EmployeeHrFactsRow(
    Guid FactsId,
    string EmployeeId,
    string? OvertimeRegime,
    bool? OnFlexScheme,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long Version);
