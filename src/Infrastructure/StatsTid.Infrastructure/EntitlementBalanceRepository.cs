using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class EntitlementBalanceRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public EntitlementBalanceRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<EntitlementBalance>> GetByEmployeeAsync(
        string employeeId, int entitlementYear, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_balances WHERE employee_id = @employeeId AND entitlement_year = @entitlementYear ORDER BY entitlement_type",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        return await ReadBalancesAsync(cmd, ct);
    }

    public async Task<EntitlementBalance?> GetByEmployeeAndTypeAsync(
        string employeeId, string entitlementType, int entitlementYear, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM entitlement_balances WHERE employee_id = @employeeId AND entitlement_type = @entitlementType AND entitlement_year = @entitlementYear",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBalance(reader) : null;
    }

    public async Task UpsertAsync(EntitlementBalance balance, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO entitlement_balances (balance_id, employee_id, entitlement_type, entitlement_year, total_quota, used, planned, carryover_in, updated_at)
              VALUES (@balanceId, @employeeId, @entitlementType, @entitlementYear, @totalQuota, @used, @planned, @carryoverIn, NOW())
              ON CONFLICT (employee_id, entitlement_type, entitlement_year)
              DO UPDATE SET total_quota = @totalQuota, used = @used, planned = @planned, carryover_in = @carryoverIn, updated_at = NOW()",
            conn);
        cmd.Parameters.AddWithValue("balanceId", balance.BalanceId);
        cmd.Parameters.AddWithValue("employeeId", balance.EmployeeId);
        cmd.Parameters.AddWithValue("entitlementType", balance.EntitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", balance.EntitlementYear);
        cmd.Parameters.AddWithValue("totalQuota", balance.TotalQuota);
        cmd.Parameters.AddWithValue("used", balance.Used);
        cmd.Parameters.AddWithValue("planned", balance.Planned);
        cmd.Parameters.AddWithValue("carryoverIn", balance.CarryoverIn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<decimal> AdjustUsedAsync(
        string employeeId, string entitlementType, int entitlementYear, decimal deltaDays, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO entitlement_balances (employee_id, entitlement_type, entitlement_year, total_quota, used, updated_at)
              VALUES (@employeeId, @entitlementType, @entitlementYear, 0, @deltaDays, NOW())
              ON CONFLICT (employee_id, entitlement_type, entitlement_year)
              DO UPDATE SET used = entitlement_balances.used + @deltaDays, updated_at = NOW()
              RETURNING used",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("deltaDays", deltaDays);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (decimal)result!;
    }

    /// <summary>
    /// Atomically checks quota and adjusts the used balance in a single SQL statement.
    /// Returns (success, newUsed). If the adjustment would exceed totalQuota + carryoverIn, returns (false, currentUsed).
    /// Eliminates TOCTOU race condition between validation and adjustment.
    /// </summary>
    public async Task<(bool Success, decimal NewUsed)> CheckAndAdjustAsync(
        string employeeId, string entitlementType, int entitlementYear,
        decimal deltaDays, decimal effectiveQuota, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"WITH current AS (
                SELECT used, carryover_in
                FROM entitlement_balances
                WHERE employee_id = @employeeId AND entitlement_type = @entitlementType AND entitlement_year = @entitlementYear
              )
              UPDATE entitlement_balances
              SET used = entitlement_balances.used + @deltaDays, updated_at = NOW()
              WHERE employee_id = @employeeId AND entitlement_type = @entitlementType AND entitlement_year = @entitlementYear
                AND (SELECT used FROM current) + @deltaDays <= @effectiveQuota + (SELECT carryover_in FROM current)
              RETURNING used",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("entitlementType", entitlementType);
        cmd.Parameters.AddWithValue("entitlementYear", entitlementYear);
        cmd.Parameters.AddWithValue("deltaDays", deltaDays);
        cmd.Parameters.AddWithValue("effectiveQuota", effectiveQuota);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is decimal newUsed)
            return (true, newUsed);

        // Update didn't match — quota would be exceeded. Return current used for error reporting.
        var balance = await GetByEmployeeAndTypeAsync(employeeId, entitlementType, entitlementYear, ct);
        return (false, balance?.Used ?? 0m);
    }

    private static async Task<IReadOnlyList<EntitlementBalance>> ReadBalancesAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var balances = new List<EntitlementBalance>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            balances.Add(ReadBalance(reader));
        return balances;
    }

    private static EntitlementBalance ReadBalance(NpgsqlDataReader reader) => new()
    {
        BalanceId = reader.GetGuid(reader.GetOrdinal("balance_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        EntitlementType = reader.GetString(reader.GetOrdinal("entitlement_type")),
        EntitlementYear = reader.GetInt32(reader.GetOrdinal("entitlement_year")),
        TotalQuota = reader.GetDecimal(reader.GetOrdinal("total_quota")),
        Used = reader.GetDecimal(reader.GetOrdinal("used")),
        Planned = reader.GetDecimal(reader.GetOrdinal("planned")),
        CarryoverIn = reader.GetDecimal(reader.GetOrdinal("carryover_in")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };
}
