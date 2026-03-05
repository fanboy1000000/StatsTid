using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class AbsenceTypeVisibilityRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public AbsenceTypeVisibilityRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<AbsenceTypeVisibility>> GetByOrgAsync(string orgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM absence_type_visibility WHERE org_id = @orgId", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);

        var results = new List<AbsenceTypeVisibility>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new AbsenceTypeVisibility
            {
                Id = reader.GetGuid(reader.GetOrdinal("id")),
                OrgId = reader.GetString(reader.GetOrdinal("org_id")),
                AbsenceType = reader.GetString(reader.GetOrdinal("absence_type")),
                IsHidden = reader.GetBoolean(reader.GetOrdinal("is_hidden")),
                SetBy = reader.GetString(reader.GetOrdinal("set_by"))
            });
        }
        return results;
    }

    public async Task SetVisibilityAsync(
        string orgId, string absenceType, bool isHidden, string actorId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO absence_type_visibility (org_id, absence_type, is_hidden, set_by, set_at)
            VALUES (@orgId, @absenceType, @isHidden, @setBy, NOW())
            ON CONFLICT (org_id, absence_type)
            DO UPDATE SET is_hidden = @isHidden, set_by = @setBy, set_at = NOW()
            """, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        cmd.Parameters.AddWithValue("absenceType", absenceType);
        cmd.Parameters.AddWithValue("isHidden", isHidden);
        cmd.Parameters.AddWithValue("setBy", actorId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
