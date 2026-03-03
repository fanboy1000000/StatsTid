using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class RoleAssignmentRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public RoleAssignmentRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<RoleAssignment>> GetByUserIdAsync(string userId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM role_assignments WHERE user_id = @userId AND is_active = TRUE", conn);
        cmd.Parameters.AddWithValue("userId", userId);
        return await ReadAssignmentsAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<RoleAssignment>> ReadAssignmentsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var assignments = new List<RoleAssignment>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            assignments.Add(new RoleAssignment
            {
                AssignmentId = reader.GetGuid(reader.GetOrdinal("assignment_id")),
                UserId = reader.GetString(reader.GetOrdinal("user_id")),
                RoleId = reader.GetString(reader.GetOrdinal("role_id")),
                OrgId = reader.IsDBNull(reader.GetOrdinal("org_id")) ? null : reader.GetString(reader.GetOrdinal("org_id")),
                ScopeType = reader.GetString(reader.GetOrdinal("scope_type")),
                AssignedBy = reader.GetString(reader.GetOrdinal("assigned_by")),
                AssignedAt = reader.GetDateTime(reader.GetOrdinal("assigned_at")),
                ExpiresAt = reader.IsDBNull(reader.GetOrdinal("expires_at")) ? null : reader.GetDateTime(reader.GetOrdinal("expires_at")),
                IsActive = reader.GetBoolean(reader.GetOrdinal("is_active"))
            });
        }
        return assignments;
    }
}
