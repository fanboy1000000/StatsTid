using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class OrganizationRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public OrganizationRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Organization?> GetByIdAsync(string orgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM organizations WHERE org_id = @orgId AND is_active = TRUE", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadOrg(reader) : null;
    }

    public async Task<IReadOnlyList<Organization>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM organizations WHERE is_active = TRUE ORDER BY materialized_path", conn);
        return await ReadOrgsAsync(cmd, ct);
    }

    // S95 / ADR-035 slice 4 — GetDescendantsAsync (the materialized-path subtree walk) was RETIRED
    // here: it lost its only production caller in S93 (OrgScopeValidator.GetAccessibleOrgsAsync now
    // returns the exact assigned org set, no subtree expansion) and is removed with the rest of the
    // tree-WALK machinery in S95.

    private static async Task<IReadOnlyList<Organization>> ReadOrgsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var orgs = new List<Organization>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            orgs.Add(ReadOrg(reader));
        return orgs;
    }

    private static Organization ReadOrg(NpgsqlDataReader reader) => new()
    {
        OrgId = reader.GetString(reader.GetOrdinal("org_id")),
        OrgName = reader.GetString(reader.GetOrdinal("org_name")),
        OrgType = reader.GetString(reader.GetOrdinal("org_type")),
        ParentOrgId = reader.IsDBNull(reader.GetOrdinal("parent_org_id")) ? null : reader.GetString(reader.GetOrdinal("parent_org_id")),
        MaterializedPath = reader.GetString(reader.GetOrdinal("materialized_path")),
        AgreementCode = reader.GetString(reader.GetOrdinal("agreement_code")),
        OkVersion = reader.GetString(reader.GetOrdinal("ok_version")),
        IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
        UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
    };
}
