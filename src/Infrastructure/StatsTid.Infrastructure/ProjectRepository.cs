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

    /// <summary>
    /// The employee's selected projects (catalog ∩ selections by construction — the JOIN
    /// filters to the org's ACTIVE projects, so stale selections for deactivated /
    /// other-org projects never surface).
    ///
    /// <para>S72 / TASK-7201 (R4) + Step-5a B2: the ordering is CONDITIONAL on the R4
    /// container state, which the CALLER passes (the endpoint already knows it — this
    /// repository deliberately does not query <c>user_skema_preferences</c> itself):</para>
    /// <list type="bullet">
    ///   <item><paramref name="orderByUserPreference"/> = true (container EXISTS): the
    ///     PER-USER <c>ups.sort_order</c> with the deterministic <c>p.project_code</c>
    ///     tiebreak — the user's chosen order, frozen against later org reorders.</item>
    ///   <item><paramref name="orderByUserPreference"/> = false (container-less): the LIVE
    ///     org order <c>p.sort_order, p.project_code</c> — byte-identical to the pre-S72
    ///     read EVEN AFTER an admin reorders org sort_order post-migration (the TASK-7200
    ///     backfilled <c>ups.sort_order</c> is a one-shot SNAPSHOT and is deliberately NOT
    ///     consulted here; ordering by it would diverge from pre-S72 on the first admin
    ///     reorder — the Step-5a B2 finding).</item>
    /// </list>
    /// </summary>
    public async Task<IReadOnlyList<Project>> GetSelectedByEmployeeAsync(
        string employeeId, string orgId, bool orderByUserPreference, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        // The ORDER BY is selected from two fixed literals (never user input).
        var orderBy = orderByUserPreference
            ? "ORDER BY ups.sort_order, p.project_code"
            : "ORDER BY p.sort_order, p.project_code";
        await using var cmd = new NpgsqlCommand(
            $"""
            SELECT p.project_id, p.org_id, p.project_code, p.project_name, p.is_active, p.sort_order, p.created_by, p.created_at
            FROM projects p
            INNER JOIN user_project_selections ups ON p.project_id = ups.project_id
            WHERE ups.employee_id = @employeeId
              AND p.org_id = @orgId
              AND p.is_active = TRUE
            {orderBy}
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("orgId", orgId);
        return await ReadProjectsAsync(cmd, ct);
    }

    // S147 / TASK-14710 (QUAL-230 T5a; rulings R-23, R-33) — AddSelectionAsync and
    // RemoveSelectionAsync, the S72 R14-aligned single-row legacy writes behind the deprecated
    // POST / DELETE /api/projects/{orgId}/select/{projectId}, were RETIRED with those routes: no
    // screen had called them since the S72 R9 sweep, and they keyed on the caller with no subject
    // row, so the leaver's self-service write floor had nothing to read. The one selection writer
    // is PUT /api/skema/{employeeId}/row-preferences (SkemaRowPreferenceRepository.ReplaceAsync).

    /// <summary>
    /// S72 / Step-5a B4 — per-employee serialization for EVERY preference mutation: the
    /// established ADR-032 D4 employee advisory lock (<c>pg_advisory_xact_lock</c> on
    /// <c>hashtext('employee-' || employeeId)</c> — the SAME key scheme the consumption /
    /// settlement writers acquire), taken FIRST inside the mutation transaction and held to
    /// commit. It was added (S72) because a row-preferences PUT could race the legacy
    /// single-row add, whose <c>MAX(sort_order)</c> and reindex read the pre-delete snapshot.
    /// That add was retired with its route in S147 (TASK-14710, the note above this summary),
    /// so today the lock serialises concurrent row-preferences PUTs for one employee, the only
    /// selection writer. (The canonical helper lives in Backend.Api, out of this assembly's scope; the
    /// lock VALUE is what matters for mutual exclusion, so the identical SQL is inlined —
    /// the established <c>VacationSettlementService</c> precedent.)
    /// </summary>
    internal static async Task AcquireEmployeeLockAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<HashSet<Guid>> GetSelectionIdsAsync(string employeeId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT project_id FROM user_project_selections WHERE employee_id = @employeeId", conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        var ids = new HashSet<Guid>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            ids.Add(reader.GetGuid(0));
        return ids;
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
