using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class OvertimePreApprovalRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public OvertimePreApprovalRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<OvertimePreApproval?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM overtime_pre_approvals WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadApproval(reader) : null;
    }

    public async Task<IReadOnlyList<OvertimePreApproval>> GetByEmployeeAndPeriodAsync(
        string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT * FROM overtime_pre_approvals
              WHERE employee_id = @employeeId AND period_start <= @periodEnd AND period_end >= @periodStart
              ORDER BY created_at DESC",
            conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("periodStart", periodStart);
        cmd.Parameters.AddWithValue("periodEnd", periodEnd);
        return await ReadApprovalsAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<OvertimePreApproval>> GetPendingByEmployeesAsync(
        IEnumerable<string> employeeIds, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"SELECT * FROM overtime_pre_approvals
              WHERE employee_id = ANY(@employeeIds) AND status = 'PENDING'
              ORDER BY created_at DESC",
            conn);
        cmd.Parameters.AddWithValue("employeeIds", employeeIds.ToArray());
        return await ReadApprovalsAsync(cmd, ct);
    }

    public async Task CreateAsync(OvertimePreApproval approval, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO overtime_pre_approvals (id, employee_id, period_start, period_end, max_hours, status, reason, created_at)
              VALUES (@id, @employeeId, @periodStart, @periodEnd, @maxHours, @status, @reason, NOW())",
            conn);
        cmd.Parameters.AddWithValue("id", approval.Id);
        cmd.Parameters.AddWithValue("employeeId", approval.EmployeeId);
        cmd.Parameters.AddWithValue("periodStart", approval.PeriodStart);
        cmd.Parameters.AddWithValue("periodEnd", approval.PeriodEnd);
        cmd.Parameters.AddWithValue("maxHours", approval.MaxHours);
        cmd.Parameters.AddWithValue("status", approval.Status);
        cmd.Parameters.AddWithValue("reason", (object?)approval.Reason ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateStatusAsync(
        Guid id, string status, string? approvedBy, string? reason, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            @"UPDATE overtime_pre_approvals
              SET status = @status, approved_by = @approvedBy, approved_at = NOW(), reason = @reason
              WHERE id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("status", status);
        cmd.Parameters.AddWithValue("approvedBy", (object?)approvedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<OvertimePreApproval>> ReadApprovalsAsync(
        NpgsqlCommand cmd, CancellationToken ct)
    {
        var approvals = new List<OvertimePreApproval>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            approvals.Add(ReadApproval(reader));
        return approvals;
    }

    private static OvertimePreApproval ReadApproval(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        PeriodStart = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("period_start"))),
        PeriodEnd = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("period_end"))),
        MaxHours = reader.GetDecimal(reader.GetOrdinal("max_hours")),
        ApprovedBy = reader.IsDBNull(reader.GetOrdinal("approved_by")) ? null : reader.GetString(reader.GetOrdinal("approved_by")),
        ApprovedAt = reader.IsDBNull(reader.GetOrdinal("approved_at")) ? null : reader.GetDateTime(reader.GetOrdinal("approved_at")),
        Status = reader.GetString(reader.GetOrdinal("status")),
        Reason = reader.IsDBNull(reader.GetOrdinal("reason")) ? null : reader.GetString(reader.GetOrdinal("reason")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}
