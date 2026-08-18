using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Orchestrator.Services;
using StatsTid.SharedKernel.Security;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// SEC-021 (owner ruling: Option A) — end-to-end read-authorization tests for
/// <c>GET /api/orchestrator/tasks/{id}</c>, booting the REAL Orchestrator host in-process
/// (<see cref="WebApplicationFactory{TEntryPoint}"/>, marked by <see cref="OrchestratorControlLoop"/>)
/// against a real Postgres (Testcontainers).
///
/// <para>
/// <b>The finding, in plain terms:</b> the endpoint fetched a task by id and returned it to ANY
/// authenticated <c>EmployeeOrAbove</c> caller with NO ownership/scope check — an IDOR (any user
/// could read any task by id). The fix adds a per-task scope check: a non-admin sees a task only
/// if the task's subject employee is within the caller's org scope; a GlobalAdmin sees any task.
/// Every denial and the not-found case return <b>404</b> (indistinguishable — a read IDOR must
/// not confirm a task's existence to an unauthorized caller).
/// </para>
///
/// <para>
/// <b>RED-before-green:</b> before the fix the handler was
/// <c>return task is not null ? Results.Ok(task) : Results.NotFound();</c>, so the out-of-scope
/// caller in <see cref="OutOfScopeLocalAdmin_Returns404"/> received <b>200 + the task</b>. That
/// exact request now returns 404.
/// </para>
///
/// <para>
/// <b>The terminated-subject defect fix (dual-lens review):</b>
/// <see cref="GlobalAdmin_TerminatedSubject_Returns200"/> vs
/// <see cref="NonAdminInScope_TerminatedSubject_Returns404"/> prove the GlobalAdmin bypass is
/// decided from the ACTOR's claims BEFORE any subject resolution: a task about a since-terminated
/// (<c>is_active = FALSE</c>) employee is readable by a GlobalAdmin, but a non-admin — even one
/// whose scope would cover the subject's org — is denied, because the shared
/// <c>ValidateEmployeeAccessAsync</c> resolves the ACTIVE subject and turns a leaver into
/// "Target employee not found". Tasks persist; employees don't.
/// </para>
///
/// <para>
/// <b>Docker:</b> the host boots without a container (no background services; the DB connection
/// is created lazily per request), but every asserted path except the no-token 401 reads a
/// persisted task, so the class is Docker-gated as a whole.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class OrchestratorTaskReadAuthorizationTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";
    internal const string DevSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    // organizations + users columns copied verbatim from CalculateAndExportScopeTests (which in
    // turn mirror docker/postgres/init.sql) — UserRepository.ReadUser reads the birth_date /
    // employment_*_date / end_date_deactivated columns, so a subset schema must carry them.
    // orchestrator_tasks mirrors init.sql:108-119 (the columns GetTaskAsync SELECTs).
    private const string SchemaDdl = """
        CREATE TABLE IF NOT EXISTS organizations (
            org_id              TEXT        PRIMARY KEY,
            org_name            TEXT        NOT NULL,
            org_type            TEXT        NOT NULL CHECK (org_type IN ('MAO', 'ORGANISATION')),
            parent_org_id       TEXT        REFERENCES organizations(org_id),
            materialized_path   TEXT        NOT NULL,
            agreement_code      TEXT        NOT NULL DEFAULT 'AC',
            ok_version          TEXT        NOT NULL DEFAULT 'OK24',
            is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS users (
            user_id             TEXT        PRIMARY KEY,
            username            TEXT        NOT NULL UNIQUE,
            password_hash       TEXT        NOT NULL,
            display_name        TEXT        NOT NULL,
            email               TEXT,
            primary_org_id      TEXT        NOT NULL REFERENCES organizations(org_id),
            agreement_code      TEXT        NOT NULL DEFAULT 'AC',
            ok_version          TEXT        NOT NULL DEFAULT 'OK24',
            employment_category TEXT        NOT NULL DEFAULT 'Standard',
            is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            birth_date              DATE,
            employment_start_date   DATE,
            employment_end_date     DATE,
            end_date_deactivated    BOOLEAN NOT NULL DEFAULT FALSE
        );

        CREATE TABLE IF NOT EXISTS orchestrator_tasks (
            task_id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            task_type       TEXT        NOT NULL,
            status          TEXT        NOT NULL DEFAULT 'pending',
            input_data      JSONB,
            output_data     JSONB,
            assigned_agent  TEXT,
            created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            started_at      TIMESTAMPTZ,
            completed_at    TIMESTAMPTZ,
            error_message   TEXT
        );
        """;

    // Two disjoint ministries: an ORG_ONLY scope on MIN_A can never reach MIN_B (exact-match
    // coverage under the flat role-scope model, ADR-035).
    private const string OrgA = "MIN_A";
    private const string OrgB = "MIN_B";
    private const string OrgAPath = "/MIN_A/";
    private const string OrgBPath = "/MIN_B/";

    private const string SubjectActive = "SUBJECT_A";      // active employee in MIN_A
    private const string SubjectTerminated = "SUBJECT_TERM"; // terminated (is_active=false) employee in MIN_A

    private PostgreSqlContainer _container = null!;
    private OrchestratorHostFactory _factory = null!;

    // Persisted-task ids, captured at seed time.
    private Guid _taskActiveSubject;      // weekly-calculation whose subject is SubjectActive
    private Guid _taskTerminatedSubject;  // weekly-calculation whose subject is SubjectTerminated

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage(ImageTag)
            .WithDatabase("statstid_test")
            .WithUsername("statstid")
            .WithPassword("statstid_test")
            .Build();

        await _container.StartAsync();

        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using (var schemaCmd = new NpgsqlCommand(SchemaDdl, conn))
                await schemaCmd.ExecuteNonQueryAsync();
            await SeedAsync(conn);
        }

        _factory = new OrchestratorHostFactory(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private async Task SeedAsync(NpgsqlConnection conn)
    {
        await ExecAsync(conn,
            "INSERT INTO organizations(org_id, org_name, org_type, materialized_path) VALUES (@id, @name, 'MAO', @path)",
            ("id", OrgA), ("name", "Ministry A"), ("path", OrgAPath));
        await ExecAsync(conn,
            "INSERT INTO organizations(org_id, org_name, org_type, materialized_path) VALUES (@id, @name, 'MAO', @path)",
            ("id", OrgB), ("name", "Ministry B"), ("path", OrgBPath));

        await ExecAsync(conn,
            "INSERT INTO users(user_id, username, password_hash, display_name, primary_org_id, is_active) " +
            "VALUES (@id, @uname, 'unused', @dn, @org, TRUE)",
            ("id", SubjectActive), ("uname", "subject_a"), ("dn", "Subject Active"), ("org", OrgA));
        // Terminated leaver: is_active = FALSE. UserRepository.GetByIdAsync filters is_active=TRUE,
        // so ValidateEmployeeAccessAsync resolves this to "Target employee not found" — the exact
        // condition the GlobalAdmin bypass must sidestep.
        await ExecAsync(conn,
            "INSERT INTO users(user_id, username, password_hash, display_name, primary_org_id, is_active) " +
            "VALUES (@id, @uname, 'unused', @dn, @org, FALSE)",
            ("id", SubjectTerminated), ("uname", "subject_term"), ("dn", "Subject Terminated"), ("org", OrgA));

        _taskActiveSubject = await InsertTaskAsync(conn,
            "weekly-calculation", $$"""{"employeeId":"{{SubjectActive}}","agreementCode":"AC"}""");
        _taskTerminatedSubject = await InsertTaskAsync(conn,
            "weekly-calculation", $$"""{"employeeId":"{{SubjectTerminated}}","agreementCode":"AC"}""");
    }

    private static async Task<Guid> InsertTaskAsync(NpgsqlConnection conn, string taskType, string inputDataJson)
    {
        var id = Guid.NewGuid();
        await ExecAsync(conn,
            "INSERT INTO orchestrator_tasks(task_id, task_type, status, input_data) " +
            "VALUES (@id, @type, 'completed', @input::jsonb)",
            ("id", id), ("type", taskType), ("input", inputDataJson));
        return id;
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in parameters)
            cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    // ------------------------------------------------------------------------
    // Tests
    // ------------------------------------------------------------------------

    /// <summary>In-scope non-admin (LocalAdmin scoped to the subject's org) → 200 + the task.</summary>
    [Fact]
    public async Task InScopeLocalAdmin_Returns200()
    {
        var response = await GetTaskAsync(
            _taskActiveSubject,
            MintToken(employeeId: "ADMIN_A", role: StatsTidRoles.LocalAdmin,
                scopes: new[] { new RoleScope(StatsTidRoles.LocalAdmin, OrgA, "ORG_ONLY") }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_taskActiveSubject.ToString(), body.GetProperty("taskId").GetGuid().ToString());
    }

    /// <summary>Out-of-scope non-admin (LocalAdmin scoped to a DISJOINT org) → 404. This is the
    /// pre-fix IDOR: the same request returned 200 + the task before Option A.</summary>
    [Fact]
    public async Task OutOfScopeLocalAdmin_Returns404()
    {
        var response = await GetTaskAsync(
            _taskActiveSubject,
            MintToken(employeeId: "ADMIN_B", role: StatsTidRoles.LocalAdmin,
                scopes: new[] { new RoleScope(StatsTidRoles.LocalAdmin, OrgB, "ORG_ONLY") }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>GlobalAdmin (role claim only, no org scopes) → 200 for any task.</summary>
    [Fact]
    public async Task GlobalAdmin_Returns200()
    {
        var response = await GetTaskAsync(
            _taskActiveSubject,
            MintToken(employeeId: "ROOT", role: StatsTidRoles.GlobalAdmin, scopes: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Defect resolution: GlobalAdmin reads a task whose subject is a TERMINATED employee
    /// → 200. The bypass is decided from the actor's claims BEFORE subject resolution, so the
    /// leaver's <c>is_active=FALSE</c> row (which the shared validator cannot resolve) is irrelevant.</summary>
    [Fact]
    public async Task GlobalAdmin_TerminatedSubject_Returns200()
    {
        var response = await GetTaskAsync(
            _taskTerminatedSubject,
            MintToken(employeeId: "ROOT", role: StatsTidRoles.GlobalAdmin, scopes: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Contrast to the above: a non-admin whose scope WOULD cover the subject's org is
    /// still denied a terminated subject (→ 404), because <c>ValidateEmployeeAccessAsync</c>
    /// resolves the ACTIVE subject and finds none. Proves the bypass is genuinely GlobalAdmin-only
    /// and that the non-admin path fails closed on an unresolvable subject.</summary>
    [Fact]
    public async Task NonAdminInScope_TerminatedSubject_Returns404()
    {
        var response = await GetTaskAsync(
            _taskTerminatedSubject,
            MintToken(employeeId: "ADMIN_A", role: StatsTidRoles.LocalAdmin,
                scopes: new[] { new RoleScope(StatsTidRoles.LocalAdmin, OrgA, "ORG_ONLY") }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>No token → 401 challenge, proving the 404s above are per-task access decisions on a
    /// policy-guarded endpoint, not an artifact of an open route.</summary>
    [Fact]
    public async Task NoToken_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/orchestrator/tasks/{_taskActiveSubject}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>A GlobalAdmin requesting a non-existent id → 404, the same status a denied caller
    /// gets — the deliberate existence-hiding posture (a legitimate not-found and an access-deny
    /// are indistinguishable to the caller).</summary>
    [Fact]
    public async Task UnknownTaskId_Returns404()
    {
        var response = await GetTaskAsync(
            Guid.NewGuid(),
            MintToken(employeeId: "ROOT", role: StatsTidRoles.GlobalAdmin, scopes: null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpResponseMessage> GetTaskAsync(Guid taskId, string token)
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orchestrator/tasks/{taskId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static string MintToken(string employeeId, string role, IReadOnlyList<RoleScope>? scopes)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevSigningKey,
            ExpirationMinutes = 60,
        });

        return tokenService.GenerateToken(
            employeeId: employeeId,
            name: employeeId,
            role: role,
            agreementCode: "AC",
            orgId: scopes is { Count: > 0 } ? scopes[0].OrgId : null,
            scopes: scopes);
    }

    /// <summary>Boots the real Orchestrator host in-process. Injects the Testcontainers connection
    /// string (<c>ConnectionStrings:EventStore</c>) and the JWT settings into HOST configuration —
    /// both fire BEFORE Program.cs reads them (for <c>DbConnectionFactory</c> and
    /// <c>AddStatsTidJwtAuth</c> respectively). Mirrors SEC-023's <c>ExternalHostFactory</c>.</summary>
    private sealed class OrchestratorHostFactory(string connectionString)
        : WebApplicationFactory<OrchestratorControlLoop>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:EventStore"] = connectionString,
                    ["Jwt:SigningKey"] = DevSigningKey,
                    ["Jwt:Issuer"] = "statstid",
                    ["Jwt:Audience"] = "statstid",
                }));

            return base.CreateHost(builder);
        }
    }
}
