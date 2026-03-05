using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class TimerSessionRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public TimerSessionRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TimerSession?> GetActiveByEmployeeAsync(string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM timer_sessions WHERE employee_id = @employeeId AND is_active = TRUE LIMIT 1", conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSession(reader) : null;
    }

    public async Task<TimerSession?> GetByEmployeeDateAsync(string employeeId, DateOnly date, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM timer_sessions WHERE employee_id = @employeeId AND date = @date", conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("date", date);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSession(reader) : null;
    }

    public async Task CheckInAsync(TimerSession session, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO timer_sessions (session_id, employee_id, date, check_in_at, is_active)
            VALUES (@sessionId, @employeeId, @date, @checkInAt, @isActive)
            """, conn);
        cmd.Parameters.AddWithValue("sessionId", session.SessionId);
        cmd.Parameters.AddWithValue("employeeId", session.EmployeeId);
        cmd.Parameters.AddWithValue("date", session.Date);
        cmd.Parameters.AddWithValue("checkInAt", session.CheckInAt);
        cmd.Parameters.AddWithValue("isActive", session.IsActive);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task CheckOutAsync(Guid sessionId, DateTime checkOutAt, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE timer_sessions SET check_out_at = @checkOutAt, is_active = FALSE WHERE session_id = @sessionId", conn);
        cmd.Parameters.AddWithValue("sessionId", sessionId);
        cmd.Parameters.AddWithValue("checkOutAt", checkOutAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static TimerSession ReadSession(NpgsqlDataReader reader) => new()
    {
        SessionId = reader.GetGuid(reader.GetOrdinal("session_id")),
        EmployeeId = reader.GetString(reader.GetOrdinal("employee_id")),
        Date = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("date"))),
        CheckInAt = reader.GetDateTime(reader.GetOrdinal("check_in_at")),
        CheckOutAt = reader.IsDBNull(reader.GetOrdinal("check_out_at")) ? null : reader.GetDateTime(reader.GetOrdinal("check_out_at")),
        IsActive = reader.GetBoolean(reader.GetOrdinal("is_active"))
    };
}
