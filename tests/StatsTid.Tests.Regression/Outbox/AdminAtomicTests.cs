using System.Data;
using Npgsql;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S26 / TASK-2608 forced-rollback tests for Phase 2 / TASK-2605a + TASK-2605b's 6
/// converted Admin endpoints. Two sub-shapes are covered (one test each):
///
/// <list type="bullet">
///   <item><description>Sub-shape (i) — full-tx-wrap (e.g. <c>POST /api/admin/organizations</c>
///   at AdminEndpoints.cs L143): a previously direct-INSERT-then-eventStore.AppendAsync
///   site is wrapped in a new explicit tx so the state INSERT and the outbox enqueue
///   commit atomically (<see cref="OrganizationCreate_OutboxFails_RollsBack"/>).</description></item>
///   <item><description>Sub-shape (ii) — existing tx + emission moved inside (e.g.
///   <c>POST /api/admin/users/{userId}/roles</c> at AdminEndpoints.cs L559): the
///   role_assignments INSERT and role_assignment_audit INSERT were already in a single
///   tx; the outbox enqueue migrates from post-commit AppendAsync to in-tx
///   EnqueueAsync so all three commit together (<see cref="RoleAssignmentGranted_OutboxFails_RollsBack"/>).</description></item>
/// </list>
///
/// <para>
/// Direct-orchestration shape mirroring <see cref="ApprovalAtomicTests"/> — the test does
/// NOT boot the full <c>Backend.Api</c> stack against a Testcontainer (HTTP-surface harness
/// deferred to Phase 4d per S24 carry-forward). The inline INSERT statements mirror the
/// production endpoint's column lists byte-for-byte; schema drift between this test and
/// AdminEndpoints.cs must be mirrored here.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AdminAtomicTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private ForcedRollbackHarness.ThrowingOutboxEnqueue _outbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        await ApplyAdminSchemaAsync(_harness.ConnectionString);
        _outbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// Sub-shape (i) — full-tx-wrap for an INSERT-only endpoint. Mirrors AdminEndpoints.cs
    /// L143 (POST /api/admin/organizations): inline organizations INSERT + outbox enqueue
    /// inside a single explicit tx; throw on outbox.EnqueueAsync rolls the INSERT back.
    /// </summary>
    [Fact]
    public async Task OrganizationCreate_OutboxFails_RollsBack()
    {
        var orgId = "ORG_FR_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var streamId = $"org-{orgId}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                await using (var cmd = new NpgsqlCommand(
                    """
                    INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path, agreement_code, ok_version, is_active, created_at, updated_at)
                    VALUES (@orgId, @orgName, @orgType, NULL, @materializedPath, 'AC', 'OK24', TRUE, NOW(), NOW())
                    """, conn, tx))
                {
                    cmd.Parameters.AddWithValue("orgId", orgId);
                    cmd.Parameters.AddWithValue("orgName", "Forced-Rollback Org");
                    cmd.Parameters.AddWithValue("orgType", "STYRELSE");
                    cmd.Parameters.AddWithValue("materializedPath", $"/{orgId}/");
                    await cmd.ExecuteNonQueryAsync();
                }

                var @event = new OrganizationCreated
                {
                    OrgId = orgId,
                    OrgName = "Forced-Rollback Org",
                    OrgType = "STYRELSE",
                    MaterializedPath = $"/{orgId}/",
                    AgreementCode = "AC",
                    OkVersion = "OK24",
                };
                await _outbox.EnqueueAsync(conn, tx, streamId, @event);

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Assert: ZERO rows in organizations for the test orgId + ZERO in events + ZERO in outbox.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "organizations",
            $"org_id = '{orgId}'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

    /// <summary>
    /// Sub-shape (ii) — existing tx already wraps state + audit; only the emission moves
    /// inside. Mirrors AdminEndpoints.cs L559 (POST /api/admin/users/{userId}/roles): the
    /// role_assignments INSERT + role_assignment_audit INSERT + outbox enqueue ride a
    /// single explicit tx; throw on outbox.EnqueueAsync rolls all three back. This is the
    /// load-bearing sub-shape — pre-S26 the audit row + role_assignments row committed
    /// while the emission ran post-commit, leaving the canonical event store divergent
    /// from the audit table on a process crash between commit and AppendAsync.
    /// </summary>
    [Fact]
    public async Task RoleAssignmentGranted_OutboxFails_RollsBack()
    {
        // Seed an org + a user + a role for the FK chain.
        var orgId = "ORG_FR_RA_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var userId = "USR_FR_RA_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        const string roleId = "EMPLOYEE";
        await SeedRoleAssignmentFixturesAsync(orgId, userId, roleId);

        var assignmentId = Guid.NewGuid();
        var streamId = $"user-{userId}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                // Step 1: role_assignments INSERT — mirrors AdminEndpoints.cs L578 verbatim.
                await using (var assignCmd = new NpgsqlCommand(
                    """
                    INSERT INTO role_assignments (assignment_id, user_id, role_id, org_id, scope_type, assigned_by, assigned_at, expires_at, is_active)
                    VALUES (@assignmentId, @userId, @roleId, @orgId, 'ORG_ONLY', 'tester', NOW(), NULL, TRUE)
                    """, conn, tx))
                {
                    assignCmd.Parameters.AddWithValue("assignmentId", assignmentId);
                    assignCmd.Parameters.AddWithValue("userId", userId);
                    assignCmd.Parameters.AddWithValue("roleId", roleId);
                    assignCmd.Parameters.AddWithValue("orgId", orgId);
                    await assignCmd.ExecuteNonQueryAsync();
                }

                // Step 2: role_assignment_audit INSERT — mirrors AdminEndpoints.cs L594 verbatim
                // (column shape `audit_id, assignment_id, action, performed_by, performed_at, details`
                // matches what production code emits today).
                await using (var auditCmd = new NpgsqlCommand(
                    """
                    INSERT INTO role_assignment_audit (audit_id, assignment_id, action, performed_by, performed_at, details)
                    VALUES (@auditId, @assignmentId, 'GRANT', 'tester', NOW(), 'Forced-rollback test')
                    """, conn, tx))
                {
                    auditCmd.Parameters.AddWithValue("auditId", Guid.NewGuid());
                    auditCmd.Parameters.AddWithValue("assignmentId", assignmentId);
                    await auditCmd.ExecuteNonQueryAsync();
                }

                // Step 3: outbox.EnqueueAsync — throws InvalidOperationException, the
                // outer catch rolls the tx back; the audit row + role_assignments row
                // must NOT persist.
                var @event = new RoleAssignmentGranted
                {
                    AssignmentId = assignmentId,
                    UserId = userId,
                    RoleId = roleId,
                    OrgId = orgId,
                    ScopeType = "ORG_ONLY",
                };
                await _outbox.EnqueueAsync(conn, tx, streamId, @event);

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Assert: ZERO rows in role_assignments + ZERO in role_assignment_audit +
        // ZERO in events + ZERO in outbox. All three (state, audit, outbox) atomic.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "role_assignments",
            $"assignment_id = '{assignmentId}'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "role_assignment_audit",
            $"assignment_id = '{assignmentId}'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
    }

    // ── Schema + seeders ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Schema extension for the <c>users</c>, <c>roles</c>, <c>role_assignments</c>, and
    /// <c>role_assignment_audit</c> tables — not in
    /// <see cref="ForcedRollbackHarness.ForcedRollbackSchema"/> because S24's harness only
    /// covered Phase-2 endpoint tables. Column shapes mirror <c>docker/postgres/init.sql</c>
    /// (users, roles, role_assignments) plus the production endpoint's audit-INSERT column
    /// list (action, performed_by, performed_at — what AdminEndpoints.cs L594 actually
    /// executes today). Idempotent.
    /// </summary>
    private const string AdminSchema = """
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

        CREATE TABLE IF NOT EXISTS roles (
            role_id             TEXT        PRIMARY KEY,
            role_name           TEXT        NOT NULL,
            description         TEXT,
            hierarchy_level     INT         NOT NULL,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS role_assignments (
            assignment_id       UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            user_id             TEXT        NOT NULL REFERENCES users(user_id),
            role_id             TEXT        NOT NULL REFERENCES roles(role_id),
            org_id              TEXT        REFERENCES organizations(org_id),
            scope_type          TEXT        NOT NULL CHECK (scope_type IN ('GLOBAL', 'ORG_ONLY', 'ORG_AND_DESCENDANTS')),
            assigned_by         TEXT        NOT NULL,
            assigned_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            expires_at          TIMESTAMPTZ,
            is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
            UNIQUE (user_id, role_id, org_id)
        );

        CREATE TABLE IF NOT EXISTS role_assignment_audit (
            audit_id            UUID        PRIMARY KEY,
            assignment_id       UUID        NOT NULL,
            action              TEXT        NOT NULL,
            performed_by        TEXT        NOT NULL,
            performed_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            details             TEXT
        );

        INSERT INTO roles (role_id, role_name, hierarchy_level)
        VALUES ('EMPLOYEE', 'Employee', 50)
        ON CONFLICT (role_id) DO NOTHING;
        """;

    private static async Task ApplyAdminSchemaAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(AdminSchema, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task SeedRoleAssignmentFixturesAsync(string orgId, string userId, string roleId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // Seed the FK parent org first.
        await using (var seedOrg = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, materialized_path, agreement_code, ok_version)
            VALUES (@orgId, 'Forced-Rollback RA Org', 'STYRELSE', @path, 'AC', 'OK24')
            ON CONFLICT (org_id) DO NOTHING
            """, conn))
        {
            seedOrg.Parameters.AddWithValue("orgId", orgId);
            seedOrg.Parameters.AddWithValue("path", $"/{orgId}/");
            await seedOrg.ExecuteNonQueryAsync();
        }
        // Seed the user.
        await using (var seedUser = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id, agreement_code, ok_version)
            VALUES (@userId, @username, 'x', 'Forced-Rollback User', @orgId, 'AC', 'OK24')
            ON CONFLICT (user_id) DO NOTHING
            """, conn))
        {
            seedUser.Parameters.AddWithValue("userId", userId);
            seedUser.Parameters.AddWithValue("username", userId.ToLowerInvariant());
            seedUser.Parameters.AddWithValue("orgId", orgId);
            await seedUser.ExecuteNonQueryAsync();
        }
        // The role row was seeded by AdminSchema (idempotent INSERT).
        _ = roleId; // documented-but-unused: roleId references the seeded EMPLOYEE row.
    }
}
