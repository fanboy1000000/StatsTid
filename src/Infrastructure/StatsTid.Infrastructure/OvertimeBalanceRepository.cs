using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class OvertimeBalanceRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public OvertimeBalanceRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<OvertimeBalance?> GetByEmployeeAndYearAsync(
        string employeeId, int periodYear, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM overtime_balances WHERE employee_id = @employeeId AND period_year = @periodYear",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("periodYear", periodYear);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadBalance(reader) : null;
    }

    public async Task UpsertAsync(OvertimeBalance balance, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO overtime_balances (balance_id, employee_id, agreement_code, period_year, accumulated, paid_out, afspadsering_used, compensation_model, updated_at)
              VALUES (@balanceId, @employeeId, @agreementCode, @periodYear, @accumulated, @paidOut, @afspadseringUsed, @compensationModel, NOW())
              ON CONFLICT (employee_id, period_year)
              DO UPDATE SET accumulated = @accumulated, paid_out = @paidOut, afspadsering_used = @afspadseringUsed, compensation_model = @compensationModel, updated_at = NOW()",
            conn);
        cmd.Parameters.AddWithValue("balanceId", balance.BalanceId);
        cmd.Parameters.AddWithValue("employeeId", balance.EmployeeId);
        cmd.Parameters.AddWithValue("agreementCode", balance.AgreementCode);
        cmd.Parameters.AddWithValue("periodYear", balance.PeriodYear);
        cmd.Parameters.AddWithValue("accumulated", balance.Accumulated);
        cmd.Parameters.AddWithValue("paidOut", balance.PaidOut);
        cmd.Parameters.AddWithValue("afspadseringUsed", balance.AfspadseringUsed);
        cmd.Parameters.AddWithValue("compensationModel", balance.CompensationModel);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<decimal> AdjustAccumulatedAsync(
        string employeeId, int periodYear, string agreementCode, decimal deltaHours, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO overtime_balances (employee_id, agreement_code, period_year, accumulated, updated_at)
              VALUES (@employeeId, @agreementCode, @periodYear, @deltaHours, NOW())
              ON CONFLICT (employee_id, period_year)
              DO UPDATE SET accumulated = overtime_balances.accumulated + @deltaHours, updated_at = NOW()
              RETURNING accumulated",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("agreementCode", agreementCode);
        cmd.Parameters.AddWithValue("periodYear", periodYear);
        cmd.Parameters.AddWithValue("deltaHours", deltaHours);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (decimal)result!;
    }

    public async Task<(bool Success, decimal NewPaidOut)> AdjustPaidOutAsync(
        string employeeId, int periodYear, decimal deltaHours, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"UPDATE overtime_balances
              SET paid_out = overtime_balances.paid_out + @deltaHours, updated_at = NOW()
              WHERE employee_id = @employeeId AND period_year = @periodYear
                AND overtime_balances.paid_out + overtime_balances.afspadsering_used + @deltaHours <= overtime_balances.accumulated
              RETURNING paid_out",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("periodYear", periodYear);
        cmd.Parameters.AddWithValue("deltaHours", deltaHours);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is decimal newPaidOut)
            return (true, newPaidOut);
        return (false, 0m);
    }

    public async Task<(bool Success, decimal NewAfspadseringUsed)> AdjustAfspadseringAsync(
        string employeeId, int periodYear, decimal deltaHours, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"UPDATE overtime_balances
              SET afspadsering_used = overtime_balances.afspadsering_used + @deltaHours, updated_at = NOW()
              WHERE employee_id = @employeeId AND period_year = @periodYear
                AND overtime_balances.paid_out + overtime_balances.afspadsering_used + @deltaHours <= overtime_balances.accumulated
              RETURNING afspadsering_used",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("periodYear", periodYear);
        cmd.Parameters.AddWithValue("deltaHours", deltaHours);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is decimal newAfspadseringUsed)
            return (true, newAfspadseringUsed);
        return (false, 0m);
    }

    private static OvertimeBalance ReadBalance(NpgsqlDataReader reader) => new()
    {
        BalanceId = reader.GetGuid(reader.GetOrdinal("balance_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        PeriodYear = reader.GetInt32(reader.GetOrdinal("period_year")),
        Accumulated = reader.GetDecimal(reader.GetOrdinal("accumulated")),
        PaidOut = reader.GetDecimal(reader.GetOrdinal("paid_out")),
        AfspadseringUsed = reader.GetDecimal(reader.GetOrdinal("afspadsering_used")),
        CompensationModel = reader.GetString(reader.GetOrdinal("compensation_model")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };
}
