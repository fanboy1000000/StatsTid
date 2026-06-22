using Npgsql;
using NpgsqlTypes;

namespace StatsTid.Tests.Regression.PhaseE;

/// <summary>
/// S43 / TASK-4306 — Phase E Test #5 (Schema constraint enforcement, negative).
///
/// <para>
/// Locks the <c>chk_target_org_required_when_tenant</c> CHECK constraint
/// behavior at the schema layer: any attempt to INSERT a row with
/// <c>visibility_scope='TENANT_TARGETED'</c> AND <c>target_org_id IS NULL</c>
/// must fail with PostgresException SqlState 23514 (check_violation),
/// constraint name <c>chk_target_org_required_when_tenant</c>.
/// </para>
///
/// <para>
/// Step 0b cycle 1 absorption: asserts BOTH SqlState AND constraint name
/// (earlier draft only said "Both tests pass" — Codex W3 strengthened the
/// validation surface).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AuditProjectionSchemaConstraintTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await AuditProjectionTestSchema.ApplyAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// INSERT with TENANT_TARGETED + target_org_id=NULL must fail with
    /// SqlState 23514 (check_violation) + constraint name
    /// chk_target_org_required_when_tenant.
    /// </summary>
    [Fact]
    public async Task Insert_TenantTargetedWithoutTargetOrg_FailsWithCheckViolation()
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var conn = new NpgsqlConnection(_harness.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO audit_projection (
                      event_id, outbox_id, event_type, visibility_scope,
                      target_org_id, target_resource_id, actor_id, actor_primary_org_id,
                      occurred_at, correlation_id, details
                  ) VALUES (
                      @eventId, 1, 'TestEvent', 'TENANT_TARGETED',
                      NULL, NULL, NULL, NULL,
                      NOW(), NULL, @details::jsonb
                  )", conn);
            cmd.Parameters.AddWithValue("eventId", Guid.NewGuid());
            cmd.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Jsonb) { Value = "{}" });
            await cmd.ExecuteNonQueryAsync();
        });

        Assert.Equal("23514", ex.SqlState);
        Assert.Equal("chk_target_org_required_when_tenant", ex.ConstraintName);
    }

    /// <summary>
    /// Mirror positive: TENANT_TARGETED + target_org_id NOT NULL passes
    /// (sanity check the CHECK constraint isn't over-rejecting).
    /// </summary>
    [Fact]
    public async Task Insert_TenantTargetedWithTargetOrg_Succeeds()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // Pre-seed the FK target.
        await using (var orgCmd = new NpgsqlCommand(
            @"INSERT INTO organizations (org_id, org_name, org_type, materialized_path)
              VALUES ('ORG_TEST_OK', 'OK Org', 'ORGANISATION', '/ORG_TEST_OK/')
              ON CONFLICT DO NOTHING", conn))
        {
            await orgCmd.ExecuteNonQueryAsync();
        }

        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO audit_projection (
                  event_id, outbox_id, event_type, visibility_scope,
                  target_org_id, target_resource_id, actor_id, actor_primary_org_id,
                  occurred_at, correlation_id, details
              ) VALUES (
                  @eventId, 1, 'TestEvent', 'TENANT_TARGETED',
                  'ORG_TEST_OK', NULL, NULL, NULL,
                  NOW(), NULL, @details::jsonb
              )", conn);
        cmd.Parameters.AddWithValue("eventId", Guid.NewGuid());
        cmd.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Jsonb) { Value = "{}" });
        var rows = await cmd.ExecuteNonQueryAsync();
        Assert.Equal(1, rows);
    }

    /// <summary>
    /// Global rows (GLOBAL_TENANT_VISIBLE or GLOBAL_ADMIN_ONLY) accept NULL
    /// target_org_id (the CHECK constraint's OR branch).
    /// </summary>
    [Fact]
    public async Task Insert_GlobalScopeWithoutTargetOrg_Succeeds()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO audit_projection (
                  event_id, outbox_id, event_type, visibility_scope,
                  target_org_id, target_resource_id, actor_id, actor_primary_org_id,
                  occurred_at, correlation_id, details
              ) VALUES (
                  @eventId, 1, 'TestEvent', 'GLOBAL_TENANT_VISIBLE',
                  NULL, NULL, NULL, NULL,
                  NOW(), NULL, @details::jsonb
              )", conn);
        cmd.Parameters.AddWithValue("eventId", Guid.NewGuid());
        cmd.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Jsonb) { Value = "{}" });
        var rows = await cmd.ExecuteNonQueryAsync();
        Assert.Equal(1, rows);
    }
}
