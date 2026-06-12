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

    /// <summary>
    /// Adds a single project selection — the R14-ALIGNED legacy write (S72 / TASK-7201).
    /// One transaction: (0) acquire the per-employee advisory lock (Step-5a B4 — see
    /// <see cref="AcquireEmployeeLockAsync"/>); (1) initialize the R4
    /// <c>user_skema_preferences</c> container on first write (<c>ON CONFLICT DO NOTHING</c>
    /// preserves an existing <c>initialized_at</c>); (2) INSERT the row APPENDED AT THE END
    /// (<c>MAX(sort_order)+1</c>, so it lands last after the reindex); (3) dense reindex to
    /// 0..n-1 in the R4 deterministic order (<c>sort_order, project_code</c>). Idempotent:
    /// re-adding an already-selected project is a no-op INSERT (the reindex still runs,
    /// harmlessly densifying). Plain un-evented rows per the R4 precedent. Because the lock
    /// precedes every read in this tx, the <c>MAX(sort_order)</c> subquery and the reindex
    /// re-read the LATEST committed state once a competing preference writer releases.
    /// </summary>
    public async Task AddSelectionAsync(string employeeId, Guid projectId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await AcquireEmployeeLockAsync(conn, tx, employeeId, ct);
            await EnsureContainerAsync(conn, tx, employeeId, ct);

            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO user_project_selections (employee_id, project_id, sort_order)
                VALUES (@employeeId, @projectId,
                        (SELECT COALESCE(MAX(sort_order), -1) + 1
                         FROM user_project_selections
                         WHERE employee_id = @employeeId))
                ON CONFLICT DO NOTHING
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("employeeId", employeeId);
                cmd.Parameters.AddWithValue("projectId", projectId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await ReindexSelectionsAsync(conn, tx, employeeId, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Removes a single project selection — the R14-ALIGNED legacy write (S72 / TASK-7201).
    /// One transaction: per-employee advisory lock FIRST (Step-5a B4 — see
    /// <see cref="AcquireEmployeeLockAsync"/>) + container init (a remove IS a preference
    /// write — post-S72 it transitions the employee to the R4 configured state, so removing
    /// the LAST selection deliberately yields ZERO visible rows rather than the legacy
    /// all-projects fallback) + DELETE + dense 0..n-1 reindex of the remaining rows. The
    /// HTTP response contract (204) is unchanged. Plain un-evented rows per the R4 precedent.
    /// </summary>
    public async Task RemoveSelectionAsync(string employeeId, Guid projectId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            await AcquireEmployeeLockAsync(conn, tx, employeeId, ct);
            await EnsureContainerAsync(conn, tx, employeeId, ct);

            await using (var cmd = new NpgsqlCommand(
                """
                DELETE FROM user_project_selections
                WHERE employee_id = @employeeId AND project_id = @projectId
                """, conn, tx))
            {
                cmd.Parameters.AddWithValue("employeeId", employeeId);
                cmd.Parameters.AddWithValue("projectId", projectId);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await ReindexSelectionsAsync(conn, tx, employeeId, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// S72 / Step-5a B4 — per-employee serialization for EVERY preference mutation: the
    /// established ADR-032 D4 employee advisory lock (<c>pg_advisory_xact_lock</c> on
    /// <c>hashtext('employee-' || employeeId)</c> — the SAME key scheme the consumption /
    /// settlement writers acquire), taken FIRST inside the mutation transaction and held to
    /// commit. Without it, a row-preferences PUT's DELETE-and-reinsert can race a legacy
    /// add whose <c>MAX(sort_order)</c> and reindex see the pre-delete snapshot — after the
    /// row-lock waits resolve, the durable table can hold gaps or duplicate
    /// <c>sort_order</c> values (masked by the GET's dense projection, but corrupt on
    /// disk). The lock serializes the writers regardless of which rows each touches, so
    /// every derived read (MAX, reindex ranking) happens against the latest committed
    /// state. (The canonical helper lives in Backend.Api, out of this assembly's scope; the
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

    /// <summary>
    /// R4 container init (first write stamps <c>initialized_at</c> via its DEFAULT; later
    /// writes are no-ops). Lives here — not only in <c>SkemaRowPreferenceRepository</c> —
    /// because the R14-aligned legacy add/remove must initialize the container in the SAME
    /// transaction as their selection write (R13 grants ProjectRepository the
    /// container/reindex writes for its own table's mutations).
    /// </summary>
    private static async Task EnsureContainerAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO user_skema_preferences (employee_id)
            VALUES (@employeeId)
            ON CONFLICT (employee_id) DO NOTHING
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Dense 0..n-1 reindex of ALL the employee's selection rows in the R4 deterministic
    /// order (<c>sort_order, project_code</c>). Covers rows for inactive/other-org projects
    /// too (every selection row has a <c>projects</c> parent via FK) so density is a
    /// whole-table-per-employee invariant, not a visible-subset one.
    /// </summary>
    private static async Task ReindexSelectionsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            WITH ranked AS (
                SELECT ups.project_id,
                       ROW_NUMBER() OVER (ORDER BY ups.sort_order, p.project_code) - 1 AS dense_order
                FROM user_project_selections ups
                INNER JOIN projects p ON p.project_id = ups.project_id
                WHERE ups.employee_id = @employeeId
            )
            UPDATE user_project_selections ups
            SET sort_order = ranked.dense_order
            FROM ranked
            WHERE ups.employee_id = @employeeId
              AND ups.project_id = ranked.project_id
            """, conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await cmd.ExecuteNonQueryAsync(ct);
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
