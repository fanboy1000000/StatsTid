using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;
using Testcontainers.PostgreSql;

namespace StatsTid.Tests.Regression;

/// <summary>
/// Regression tests for TASK-1902 (Codex BLOCKER on S18 remediation).
///
/// Background: <c>/api/payroll/calculate-and-export</c> guarded the request
/// with <c>LocalAdminOrAbove</c> + an APPROVED-period match, but the approval
/// guard only matches <c>(employee_id, period)</c>. A LocalAdmin from org A
/// could trigger payroll export for any employee in org B whose period
/// happened to be APPROVED. The S19 fix calls
/// <see cref="OrgScopeValidator.ValidateEmployeeAccessAsync"/> at the endpoint
/// before the approval lookup; these tests pin the three decision branches the
/// SPRINT-19 validation criteria call out:
///
///   1. LocalAdmin in org A, target employee in org B  → rejected (cross-org)
///   2. LocalAdmin in org A, target employee in org A  → accepted (same-org)
///   3. GlobalAdmin (any scope path)                   → accepted (bypass)
///
/// SPRINT-19 originally proposed
/// <c>tests/StatsTid.Tests.Unit/Payroll/CalculateAndExportScopeTests.cs</c>.
/// In practice <c>OrgScopeValidator</c> takes <c>OrganizationRepository</c> and
/// <c>UserRepository</c> directly — both are <c>sealed</c> classes that issue
/// raw Npgsql queries — so a pure-unit test would either require refactoring
/// the validator (out of scope for S19) or mocking the framework, which
/// wouldn't pin the actual scope semantic. The regression suite already runs
/// these branches against a real Postgres container (Testcontainers), so this
/// file lives here and follows the same fixture pattern as
/// <see cref="WageTypeMappingRegressionTests"/>. Pinning the validator covers
/// every endpoint that calls <c>ValidateEmployeeAccessAsync</c>, including
/// <c>/calculate-and-export</c>.
///
/// Requires a running Docker daemon. Without Docker the fixture surfaces the
/// failure clearly rather than silently skipping.
/// </summary>
public sealed class CalculateAndExportScopeTests : IAsyncLifetime
{
    private const string ImageTag = "postgres:16-alpine";

    // Minimal schema subset — organizations + users only. Column definitions are
    // copy-pasted verbatim from docker/postgres/init.sql:376-408 so a schema drift
    // (e.g. dropping materialized_path NOT NULL) will surface as test failure
    // here rather than at runtime in production. We deliberately do NOT execute
    // the full init.sql — it pulls in pgcrypto + 27 unrelated tables, which
    // would slow the suite without adding signal for this test.
    private const string SchemaDdl = """
        CREATE TABLE IF NOT EXISTS organizations (
            org_id              TEXT        PRIMARY KEY,
            org_name            TEXT        NOT NULL,
            org_type            TEXT        NOT NULL CHECK (org_type IN ('MINISTRY', 'STYRELSE', 'AFDELING', 'TEAM')),
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
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;

    // Two parallel ministry subtrees so cross-org cases are unambiguous: a
    // LocalAdmin scoped to MIN_A cannot reach MIN_B by any descendant chain.
    private const string OrgA = "MIN_A";
    private const string OrgB = "MIN_B";
    private const string OrgAPath = "/MIN_A/";
    private const string OrgBPath = "/MIN_B/";

    // Target employees — one per ministry. The test names make the cross-org /
    // same-org distinction explicit; the IDs themselves carry no scope meaning.
    private const string EmployeeInOrgA = "EMP_A";
    private const string EmployeeInOrgB = "EMP_B";

    private PostgreSqlContainer _container = null!;
    private OrgScopeValidator _validator = null!;

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

        var factory = new DbConnectionFactory(_container.GetConnectionString());
        var orgs = new OrganizationRepository(factory);
        var users = new UserRepository(factory);
        _validator = new OrgScopeValidator(orgs, users, NullLogger<OrgScopeValidator>.Instance);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private static async Task SeedAsync(NpgsqlConnection conn)
    {
        // Two root-level ministries with disjoint materialized paths.
        await ExecAsync(conn,
            "INSERT INTO organizations(org_id, org_name, org_type, materialized_path) " +
            "VALUES (@id, @name, 'MINISTRY', @path)",
            ("id", OrgA), ("name", "Ministry A"), ("path", OrgAPath));
        await ExecAsync(conn,
            "INSERT INTO organizations(org_id, org_name, org_type, materialized_path) " +
            "VALUES (@id, @name, 'MINISTRY', @path)",
            ("id", OrgB), ("name", "Ministry B"), ("path", OrgBPath));

        // One employee per ministry, primary_org_id pointing at their own.
        await ExecAsync(conn,
            "INSERT INTO users(user_id, username, password_hash, display_name, primary_org_id) " +
            "VALUES (@id, @uname, 'unused', @dn, @org)",
            ("id", EmployeeInOrgA), ("uname", "emp_a"), ("dn", "Employee A"), ("org", OrgA));
        await ExecAsync(conn,
            "INSERT INTO users(user_id, username, password_hash, display_name, primary_org_id) " +
            "VALUES (@id, @uname, 'unused', @dn, @org)",
            ("id", EmployeeInOrgB), ("uname", "emp_b"), ("dn", "Employee B"), ("org", OrgB));
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (n, v) in parameters)
            cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }

    private static ActorContext LocalAdminScopedTo(string orgId) =>
        new(
            ActorId: "ADMIN_USR",
            ActorRole: StatsTidRoles.LocalAdmin,
            CorrelationId: Guid.NewGuid(),
            OrgId: orgId,
            Scopes: new[] { new RoleScope(StatsTidRoles.LocalAdmin, orgId, "ORG_AND_DESCENDANTS") });

    private static ActorContext GlobalAdmin() =>
        new(
            ActorId: "ROOT_USR",
            ActorRole: StatsTidRoles.GlobalAdmin,
            CorrelationId: Guid.NewGuid(),
            OrgId: null,
            Scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });

    // -----------------------------------------------------------------------
    // Branch 1: LocalAdmin from org A, target employee in org B → rejected.
    //
    // This is the exact attack vector Codex flagged: pre-fix, LocalAdmin role +
    // APPROVED-period match was sufficient. ValidateEmployeeAccessAsync must
    // resolve the target's org (MIN_B) and check the actor's MIN_A scope path
    // does not cover it.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CrossOrgAdmin_Rejected()
    {
        var actor = LocalAdminScopedTo(OrgA);

        var (allowed, reason) = await _validator.ValidateEmployeeAccessAsync(actor, EmployeeInOrgB);

        Assert.False(allowed);
        Assert.Equal("Actor scope does not cover target organization", reason);
    }

    // -----------------------------------------------------------------------
    // Branch 2: LocalAdmin from org A, target employee in org A → accepted.
    //
    // Pins that the fix didn't over-rotate: same-org admin must still pass.
    // ORG_AND_DESCENDANTS scope on /MIN_A/ covers /MIN_A/ itself.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task SameOrgAdmin_Accepted()
    {
        var actor = LocalAdminScopedTo(OrgA);

        var (allowed, reason) = await _validator.ValidateEmployeeAccessAsync(actor, EmployeeInOrgA);

        Assert.True(allowed);
        Assert.Null(reason);
    }

    // -----------------------------------------------------------------------
    // Branch 3: GlobalAdmin → accepted regardless of target org.
    //
    // GLOBAL scope short-circuits the per-org check entirely. Pin this against
    // an employee in org B (the foreign subtree from the cross-org test) so a
    // future regression that accidentally tightens GLOBAL handling surfaces.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task GlobalAdmin_AcceptedAcrossOrgs()
    {
        var actor = GlobalAdmin();

        var (allowed, reason) = await _validator.ValidateEmployeeAccessAsync(actor, EmployeeInOrgB);

        Assert.True(allowed);
        Assert.Null(reason);
    }

    // -----------------------------------------------------------------------
    // Defence-in-depth pin: target user that doesn't exist must deny rather
    // than silently allow. ValidateEmployeeAccessAsync resolves the target via
    // UserRepository before checking scope; a missing row historically returned
    // null, and the validator must treat that as "no access" not "unconstrained".
    // -----------------------------------------------------------------------
    [Fact]
    public async Task UnknownTargetEmployee_Rejected()
    {
        var actor = LocalAdminScopedTo(OrgA);

        var (allowed, reason) = await _validator.ValidateEmployeeAccessAsync(actor, "EMP_DOES_NOT_EXIST");

        Assert.False(allowed);
        Assert.Equal("Target employee not found", reason);
    }
}
