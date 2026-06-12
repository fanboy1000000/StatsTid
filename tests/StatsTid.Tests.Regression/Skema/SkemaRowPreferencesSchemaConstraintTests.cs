using Npgsql;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Skema;

/// <summary>
/// S72 / TASK-7200 — DB-level integrity floors for the Skema-redesign per-user
/// row-preference schema (SPRINT-72 pinned rule R4). Pattern precedent:
/// <see cref="Settlement.Slice3bSchemaConstraintTests"/> (S71). The endpoints and the
/// repository built by TASK-7201 enforce R4 in code, but a malformed DIRECT write must not
/// be able to persist an impossible preference row. Proven here, legal + illegal direction
/// for every constraint the S72 segment lands:
///
/// <list type="bullet">
///   <item><b>user_skema_preferences</b> (the R4 configured-state container) — exactly one
///     row per employee (PK on <c>employee_id</c>); the employee must exist (FK to
///     <c>users(user_id)</c>); <c>initialized_at</c> self-populates via its DEFAULT.</item>
///   <item><b>user_absence_selections</b> — at most one row per (employee, absence_type)
///     (composite PK); the employee must exist (FK to <c>users(user_id)</c>);
///     <c>sort_order</c> defaults to 0 when omitted and persists when explicit.</item>
///   <item><b>user_project_selections.sort_order</b> — the S72 additive column: an INSERT
///     using the pre-S72 column shape (exactly what
///     <c>ProjectRepository.AddSelectionAsync</c> writes — <c>employee_id, project_id</c>
///     only) takes the DEFAULT 0, which is what makes the ALTER additive for every census
///     caller; an explicit value persists; DUPLICATE sort_order values across an
///     employee's rows are legal by design (R4: the legacy backfill copies
///     <c>projects.sort_order</c>, duplicates expected — readers tiebreak
///     <c>ORDER BY sort_order, project_code</c>).</item>
/// </list>
///
/// <para>Postgres surfaces FK violations as SQLSTATE 23503, unique/PK violations as 23505
/// (both via <see cref="PostgresException"/>).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class SkemaRowPreferencesSchemaConstraintTests : IAsyncLifetime
{
    private const string OrgId = "STY01";
    private const string FkViolation = "23503";
    private const string UniqueViolation = "23505";

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ───────────────── user_skema_preferences — the R4 container ─────────────────

    [Fact]
    public async Task Container_OneRowPerEmployee_23505()
    {
        var emp = await SeedEmployeeAsync();
        await InsertContainerAsync(emp);

        // The container is a presence marker — a second row for the same employee
        // is meaningless and the PK rejects it.
        var ex = await Assert.ThrowsAsync<PostgresException>(() => InsertContainerAsync(emp));
        Assert.Equal(UniqueViolation, ex.SqlState);
    }

    [Fact]
    public async Task Container_RequiresExistingUser_23503()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertContainerAsync("emp_s72_does_not_exist"));
        Assert.Equal(FkViolation, ex.SqlState);
    }

    [Fact]
    public async Task Container_InitializedAt_SelfPopulates()
    {
        var emp = await SeedEmployeeAsync();
        await InsertContainerAsync(emp);

        var initializedAt = await ScalarAsync(
            "SELECT initialized_at FROM user_skema_preferences WHERE employee_id = @p0", emp);
        Assert.IsType<DateTime>(initializedAt);
    }

    // ───────────────── user_absence_selections ─────────────────

    [Fact]
    public async Task AbsenceSelections_OneRowPerEmployeeAndType_23505()
    {
        var emp = await SeedEmployeeAsync();
        await InsertAbsenceSelectionAsync(emp, "VACATION", sortOrder: 0);

        // Same (employee, type) again — the composite PK rejects it.
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAbsenceSelectionAsync(emp, "VACATION", sortOrder: 1));
        Assert.Equal(UniqueViolation, ex.SqlState);

        // A different type for the same employee, and the same type for a different
        // employee, are each their own row.
        await InsertAbsenceSelectionAsync(emp, "SICK", sortOrder: 1);
        var other = await SeedEmployeeAsync();
        await InsertAbsenceSelectionAsync(other, "VACATION", sortOrder: 0);
    }

    [Fact]
    public async Task AbsenceSelections_RequireExistingUser_23503()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAbsenceSelectionAsync("emp_s72_does_not_exist", "VACATION", sortOrder: 0));
        Assert.Equal(FkViolation, ex.SqlState);
    }

    [Fact]
    public async Task AbsenceSelections_SortOrder_DefaultsToZero_AndPersistsExplicit()
    {
        var emp = await SeedEmployeeAsync();

        // Omitting sort_order takes the DEFAULT 0.
        await InsertAbsenceSelectionAsync(emp, "VACATION", sortOrder: null);
        Assert.Equal(0, await ReadAbsenceSortOrderAsync(emp, "VACATION"));

        // An explicit value persists (the modal's dense reindex write shape).
        await InsertAbsenceSelectionAsync(emp, "SICK", sortOrder: 7);
        Assert.Equal(7, await ReadAbsenceSortOrderAsync(emp, "SICK"));
    }

    // ───────────────── user_project_selections.sort_order ─────────────────

    [Fact]
    public async Task ProjectSelections_LegacyWriteShape_TakesSortOrderDefaultZero()
    {
        // The exact pre-S72 column shape ProjectRepository.AddSelectionAsync writes
        // (employee_id, project_id — no sort_order). The DEFAULT 0 is what makes the
        // S72 ALTER additive for the R14 legacy add endpoint: the write succeeds
        // unchanged and the row gets a deterministic order value.
        var emp = await SeedEmployeeAsync();
        var projectId = await InsertProjectAsync(orgSortOrder: 10);

        await InsertProjectSelectionLegacyShapeAsync(emp, projectId);
        Assert.Equal(0, await ReadProjectSortOrderAsync(emp, projectId));
    }

    [Fact]
    public async Task ProjectSelections_ExplicitSortOrder_PersistsAndAdmitsDuplicates()
    {
        var emp = await SeedEmployeeAsync();
        var first = await InsertProjectAsync(orgSortOrder: 10);
        var second = await InsertProjectAsync(orgSortOrder: 20);
        var third = await InsertProjectAsync(orgSortOrder: 20);

        await InsertProjectSelectionAsync(emp, first, sortOrder: 5);
        Assert.Equal(5, await ReadProjectSortOrderAsync(emp, first));

        // R4: duplicate sort_order values across one employee's rows are LEGAL by
        // design (the legacy backfill copies projects.sort_order, which may repeat);
        // readers tiebreak deterministically — no unique constraint exists here.
        await InsertProjectSelectionAsync(emp, second, sortOrder: 20);
        await InsertProjectSelectionAsync(emp, third, sortOrder: 20);
        Assert.Equal(20, await ReadProjectSortOrderAsync(emp, second));
        Assert.Equal(20, await ReadProjectSortOrderAsync(emp, third));
    }

    // ─────────────────────────────── helpers ───────────────────────────────

    private async Task<string> SeedEmployeeAsync()
    {
        var employeeId = "emp_s72_chk_" + Guid.NewGuid().ToString("N")[..8];
        await RegressionSeed.SeedEmployeeAsync(_harness.ConnectionString, employeeId, OrgId, "AC", "OK24");
        return employeeId;
    }

    private async Task InsertContainerAsync(string employeeId)
    {
        await ExecAsync(
            "INSERT INTO user_skema_preferences (employee_id) VALUES (@p0)", employeeId);
    }

    /// <summary>
    /// Inserts an absence selection. <paramref name="sortOrder"/> null = OMIT the column
    /// so the schema DEFAULT applies.
    /// </summary>
    private async Task InsertAbsenceSelectionAsync(string employeeId, string absenceType, int? sortOrder)
    {
        if (sortOrder is null)
        {
            await ExecAsync(
                "INSERT INTO user_absence_selections (employee_id, absence_type) VALUES (@p0, @p1)",
                employeeId, absenceType);
            return;
        }

        await ExecAsync(
            """
            INSERT INTO user_absence_selections (employee_id, absence_type, sort_order)
            VALUES (@p0, @p1, @p2)
            """,
            employeeId, absenceType, sortOrder.Value);
    }

    private async Task<Guid> InsertProjectAsync(int orgSortOrder)
    {
        var code = "S72-" + Guid.NewGuid().ToString("N")[..8];
        var result = await ScalarAsync(
            """
            INSERT INTO projects (org_id, project_code, project_name, sort_order, created_by)
            VALUES (@p0, @p1, @p2, @p3, 'test')
            RETURNING project_id
            """,
            OrgId, code, "S72 schema test project", orgSortOrder);
        return (Guid)result!;
    }

    /// <summary>The exact pre-S72 write shape of <c>ProjectRepository.AddSelectionAsync</c>
    /// (ProjectRepository.cs:93) — sort_order omitted, DEFAULT applies.</summary>
    private async Task InsertProjectSelectionLegacyShapeAsync(string employeeId, Guid projectId)
    {
        await ExecAsync(
            """
            INSERT INTO user_project_selections (employee_id, project_id)
            VALUES (@p0, @p1)
            ON CONFLICT DO NOTHING
            """,
            employeeId, projectId);
    }

    private async Task InsertProjectSelectionAsync(string employeeId, Guid projectId, int sortOrder)
    {
        await ExecAsync(
            """
            INSERT INTO user_project_selections (employee_id, project_id, sort_order)
            VALUES (@p0, @p1, @p2)
            """,
            employeeId, projectId, sortOrder);
    }

    private async Task<int> ReadAbsenceSortOrderAsync(string employeeId, string absenceType)
    {
        return Convert.ToInt32(await ScalarAsync(
            """
            SELECT sort_order FROM user_absence_selections
            WHERE employee_id = @p0 AND absence_type = @p1
            """,
            employeeId, absenceType));
    }

    private async Task<int> ReadProjectSortOrderAsync(string employeeId, Guid projectId)
    {
        return Convert.ToInt32(await ScalarAsync(
            """
            SELECT sort_order FROM user_project_selections
            WHERE employee_id = @p0 AND project_id = @p1
            """,
            employeeId, projectId));
    }

    private async Task ExecAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<object?> ScalarAsync(string sql, params object[] args)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        for (var i = 0; i < args.Length; i++)
            cmd.Parameters.AddWithValue("p" + i, args[i]);
        return await cmd.ExecuteScalarAsync();
    }
}
