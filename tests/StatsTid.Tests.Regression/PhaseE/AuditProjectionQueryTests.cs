using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Regression.PhaseE;

/// <summary>
/// S44 / TASK-4406 — ADR-026 D5 audit-visibility-surface read-path D-tests.
/// Covers both:
/// <list type="bullet">
///   <item><description><c>OrgScopeValidator.GetAccessibleOrgsAsync</c>
///   per-role contract (GlobalAdmin → null sentinel; LocalAdmin →
///   materialized-path descendants; Employee → empty list).</description></item>
///   <item><description><c>AuditProjectionRepository.QueryByOrgScopeAsync</c>
///   3-tier visibility filter (TENANT_TARGETED scope-by-target +
///   GLOBAL_TENANT_VISIBLE always + GLOBAL_ADMIN_ONLY only for GlobalAdmin).</description></item>
/// </list>
/// Per Step 4 cycle 1 absorption — TASK-4405 unit-test deferred here
/// because OrganizationRepository is sealed/concrete and the in-tree
/// pattern is integration-via-Docker harness.
/// </summary>
[Trait("Category", "Docker")]
public sealed class AuditProjectionQueryTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private OrgScopeValidator _orgScopeValidator = null!;
    private AuditProjectionRepository _auditRepo = null!;

    // Org subtree:  MIN01
    //                ├── STY01
    //                │     └── AFD01
    //                └── STY02
    // ORG_EXTERNAL  (different subtree; LocalAdmin@MIN01 should NOT cover)
    private const string MinId = "MIN01";
    private const string Sty01Id = "STY01";
    private const string Afd01Id = "AFD01";
    private const string Sty02Id = "STY02";
    private const string ExternalId = "ORG_EXTERNAL";

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await Outbox.OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await AuditProjectionTestSchema.ApplyAsync(_harness.ConnectionString);

        // Seed organizations with materialized paths matching ValidateOrgAccessAsync
        // / GetDescendantsAsync semantic. Paths are root-anchored "/X/Y/Z/" with
        // trailing slash so LIKE 'parentPath%' includes the parent + descendants.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO organizations (org_id, org_name, org_type, parent_org_id, materialized_path) VALUES
                  ('MIN01', 'Ministry 01',    'MINISTRY', NULL,    '/MIN01/'),
                  ('STY01', 'Styrelse 01',    'STYRELSE', 'MIN01', '/MIN01/STY01/'),
                  ('AFD01', 'Afdeling 01',    'AFDELING', 'STY01', '/MIN01/STY01/AFD01/'),
                  ('STY02', 'Styrelse 02',    'STYRELSE', 'MIN01', '/MIN01/STY02/'),
                  ('ORG_EXTERNAL', 'External', 'MINISTRY', NULL,   '/ORG_EXTERNAL/')
                ON CONFLICT DO NOTHING", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        _auditRepo = new AuditProjectionRepository(_harness.Factory);
        _orgScopeValidator = new OrgScopeValidator(
            new OrganizationRepository(_harness.Factory),
            new UserRepository(_harness.Factory),
            NullLogger<OrgScopeValidator>.Instance);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // -------------------------------------------------------------------
    // GetAccessibleOrgsAsync — per-role contract coverage
    // -------------------------------------------------------------------

    [Fact]
    public async Task GetAccessibleOrgs_GlobalAdminScope_ReturnsNullSentinel()
    {
        var actor = new ActorContext(
            "global-admin-1",
            StatsTidRoles.GlobalAdmin,
            Guid.NewGuid(),
            null,
            new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
        var result = await _orgScopeValidator.GetAccessibleOrgsAsync(actor);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAccessibleOrgs_LocalAdminAtMIN01_ReturnsSubtreeDescendants()
    {
        var actor = new ActorContext(
            "local-admin-1",
            StatsTidRoles.LocalAdmin,
            Guid.NewGuid(),
            MinId,
            new[] { new RoleScope(StatsTidRoles.LocalAdmin, MinId, "ORG_AND_DESCENDANTS") });
        var result = await _orgScopeValidator.GetAccessibleOrgsAsync(actor);
        Assert.NotNull(result);
        // Subtree: MIN01 + STY01 + AFD01 + STY02 = 4 orgs; ORG_EXTERNAL excluded
        Assert.Equal(4, result!.Count);
        Assert.Contains(MinId, result);
        Assert.Contains(Sty01Id, result);
        Assert.Contains(Afd01Id, result);
        Assert.Contains(Sty02Id, result);
        Assert.DoesNotContain(ExternalId, result);
    }

    [Fact]
    public async Task GetAccessibleOrgs_EmployeeScope_ReturnsEmpty()
    {
        // Employee with no scopes assigned — caller treats as 403.
        var actor = new ActorContext(
            "employee-1",
            StatsTidRoles.Employee,
            Guid.NewGuid(),
            Sty01Id,
            null);
        var result = await _orgScopeValidator.GetAccessibleOrgsAsync(actor);
        Assert.NotNull(result);
        Assert.Empty(result!);
    }

    // -------------------------------------------------------------------
    // QueryByOrgScopeAsync — 3-tier visibility filter
    // -------------------------------------------------------------------

    [Fact]
    public async Task QueryByOrgScope_GlobalAdminSentinel_IncludesAllThreeTiers()
    {
        await SeedNineAuditRowsAsync();

        var (rows, totalCount) = await _auditRepo.QueryByOrgScopeAsync(
            accessibleOrgIds: null, // GlobalAdmin sentinel
            filter: new AuditQueryFilter(),
            page: 1,
            pageSize: 100);

        Assert.Equal(9L, totalCount);
        Assert.Equal(9, rows.Count);
        Assert.Contains(rows, r => r.VisibilityScope == "TENANT_TARGETED");
        Assert.Contains(rows, r => r.VisibilityScope == "GLOBAL_TENANT_VISIBLE");
        Assert.Contains(rows, r => r.VisibilityScope == "GLOBAL_ADMIN_ONLY");
    }

    [Fact]
    public async Task QueryByOrgScope_LocalAdminAtMIN01_IncludesScopeTargetedPlusGlobalVisible_ExcludesAdminOnly()
    {
        await SeedNineAuditRowsAsync();

        // LocalAdmin@MIN01 sees: all TENANT_TARGETED rows whose target_org_id is in the MIN01 subtree
        // + all GLOBAL_TENANT_VISIBLE rows, but NOT GLOBAL_ADMIN_ONLY rows.
        // Seeded: 3 TENANT_TARGETED (MIN01, STY01, ORG_EXTERNAL) + 3 GLOBAL_TENANT_VISIBLE + 3 GLOBAL_ADMIN_ONLY
        // Visible to MIN01 LocalAdmin: 2 TENANT (MIN01, STY01) + 3 GLOBAL_TENANT = 5
        var subtreeOrgIds = new[] { MinId, Sty01Id, Afd01Id, Sty02Id };
        var (rows, totalCount) = await _auditRepo.QueryByOrgScopeAsync(
            accessibleOrgIds: subtreeOrgIds,
            filter: new AuditQueryFilter(),
            page: 1,
            pageSize: 100);

        Assert.Equal(5L, totalCount);
        Assert.Equal(5, rows.Count);
        Assert.Equal(2, rows.Count(r => r.VisibilityScope == "TENANT_TARGETED"));
        Assert.Equal(3, rows.Count(r => r.VisibilityScope == "GLOBAL_TENANT_VISIBLE"));
        Assert.DoesNotContain(rows, r => r.VisibilityScope == "GLOBAL_ADMIN_ONLY");
        Assert.DoesNotContain(rows, r => r.TargetOrgId == ExternalId);
    }

    [Fact]
    public async Task QueryByOrgScope_EmptyScope_ReturnsEmptyPageWithoutDbHit()
    {
        await SeedNineAuditRowsAsync();

        var (rows, totalCount) = await _auditRepo.QueryByOrgScopeAsync(
            accessibleOrgIds: Array.Empty<string>(),
            filter: new AuditQueryFilter(),
            page: 1,
            pageSize: 100);

        Assert.Equal(0L, totalCount);
        Assert.Empty(rows);
    }

    // -------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------

    /// <summary>
    /// Seeds 9 audit_projection rows: 3 TENANT_TARGETED (one each at MIN01,
    /// STY01, ORG_EXTERNAL) + 3 GLOBAL_TENANT_VISIBLE + 3 GLOBAL_ADMIN_ONLY.
    /// </summary>
    private async Task SeedNineAuditRowsAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        for (int i = 0; i < 3; i++)
        {
            // TENANT_TARGETED — one per (MIN01, STY01, ORG_EXTERNAL)
            var targetOrg = i switch
            {
                0 => MinId,
                1 => Sty01Id,
                _ => ExternalId,
            };
            await InsertAuditRowAsync(conn, "TENANT_TARGETED", targetOrg, $"TestEventTenant{i}");

            // GLOBAL_TENANT_VISIBLE — target_org_id NULL
            await InsertAuditRowAsync(conn, "GLOBAL_TENANT_VISIBLE", null, $"TestEventGlobalTenant{i}");

            // GLOBAL_ADMIN_ONLY — target_org_id NULL
            await InsertAuditRowAsync(conn, "GLOBAL_ADMIN_ONLY", null, $"TestEventGlobalAdmin{i}");
        }
    }

    private static long _nextOutboxId = 1000;

    private static async Task InsertAuditRowAsync(
        NpgsqlConnection conn, string visibilityScope, string? targetOrgId, string eventType)
    {
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO audit_projection (
                  event_id, outbox_id, event_type, visibility_scope,
                  target_org_id, target_resource_id, actor_id, actor_primary_org_id,
                  occurred_at, correlation_id, details
              ) VALUES (
                  @eventId, @outboxId, @eventType, @visibilityScope,
                  @targetOrgId, 'test-resource', 'test-actor', NULL,
                  NOW(), NULL, @details::jsonb
              )", conn);
        cmd.Parameters.AddWithValue("eventId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("outboxId", Interlocked.Increment(ref _nextOutboxId));
        cmd.Parameters.AddWithValue("eventType", eventType);
        cmd.Parameters.AddWithValue("visibilityScope", visibilityScope);
        cmd.Parameters.AddWithValue("targetOrgId", (object?)targetOrgId ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Jsonb) { Value = "{}" });
        await cmd.ExecuteNonQueryAsync();
    }
}
