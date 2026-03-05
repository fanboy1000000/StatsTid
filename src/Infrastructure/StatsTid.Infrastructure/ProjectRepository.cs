using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

public sealed class ProjectRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public ProjectRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<Project>> GetByOrgAsync(string orgId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT * FROM projects WHERE org_id = @orgId AND is_active = TRUE ORDER BY sort_order, project_code", conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        return await ReadProjectsAsync(cmd, ct);
    }

    public async Task<Guid> CreateAsync(Project project, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        var projectId = Guid.NewGuid();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO projects (project_id, org_id, project_code, project_name, is_active, sort_order, created_by)
            VALUES (@projectId, @orgId, @projectCode, @projectName, @isActive, @sortOrder, @createdBy)
            """, conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("orgId", project.OrgId);
        cmd.Parameters.AddWithValue("projectCode", project.ProjectCode);
        cmd.Parameters.AddWithValue("projectName", project.ProjectName);
        cmd.Parameters.AddWithValue("isActive", project.IsActive);
        cmd.Parameters.AddWithValue("sortOrder", project.SortOrder);
        cmd.Parameters.AddWithValue("createdBy", project.CreatedBy);
        await cmd.ExecuteNonQueryAsync(ct);
        return projectId;
    }

    public async Task UpdateAsync(Guid projectId, string projectName, int sortOrder, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE projects SET project_name = @projectName, sort_order = @sortOrder WHERE project_id = @projectId", conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        cmd.Parameters.AddWithValue("projectName", projectName);
        cmd.Parameters.AddWithValue("sortOrder", sortOrder);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task DeactivateAsync(Guid projectId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "UPDATE projects SET is_active = FALSE WHERE project_id = @projectId", conn);
        cmd.Parameters.AddWithValue("projectId", projectId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<Project>> ReadProjectsAsync(NpgsqlCommand cmd, CancellationToken ct)
    {
        var projects = new List<Project>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            projects.Add(ReadProject(reader));
        return projects;
    }

    private static Project ReadProject(NpgsqlDataReader reader) => new()
    {
        ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
        OrgId = reader.GetString(reader.GetOrdinal("org_id")),
        ProjectCode = reader.GetString(reader.GetOrdinal("project_code")),
        ProjectName = reader.GetString(reader.GetOrdinal("project_name")),
        IsActive = reader.GetBoolean(reader.GetOrdinal("is_active")),
        SortOrder = reader.GetInt32(reader.GetOrdinal("sort_order")),
        CreatedBy = reader.GetString(reader.GetOrdinal("created_by")),
        CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
    };
}
