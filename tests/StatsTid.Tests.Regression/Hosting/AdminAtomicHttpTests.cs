using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Hosting;

/// <summary>
/// S133 / TASK-13312 (QUAL-016) — WIRE-DRIVEN conversions of the two Admin atomic-outbox proofs:
/// organization-create (<c>POST /api/admin/organizations</c>) and role-grant
/// (<c>POST /api/admin/roles/grant</c>, the mutation behind the <c>/users/{id}/roles</c> resource).
/// Replaces the rollback methods of the legacy hand-mirror <c>Outbox.AdminAtomicTests</c> (now
/// retired), which INLINED the endpoints' INSERTs in the test body and so proved only the copy.
/// Each test here drives the REAL route through a throwing-outbox host
/// (<see cref="StatsTidWebApplicationFactory.WithThrowingOutbox"/>) and pins that the whole write
/// (state [+ audit] + outbox + projection) rolls back (PAT-019).
///
/// <para><b>Auth/seed cost (the pricier admin end):</b> both routes are policy
/// <c>LocalAdminOrAbove</c> with <c>requireOrgScope:true</c> — so, UNLIKE the GlobalAdminOnly
/// config/wage/position endpoints, a bare role token is REJECTED by the policy. The token here
/// therefore carries a <c>GLOBAL</c>-typed <c>RoleScope</c> with role GlobalAdmin; that one scope
/// satisfies the policy AND the in-handler <c>HasGlobalScope</c> (MAO-create gate) AND
/// <c>OrgScopeValidator.ValidateOrgAccessAsync</c> (the role-grant org check). The role-grant test
/// also seeds a target user directly (the grant validates the user exists) whose primary org is the
/// init.sql-seeded ORGANISATION <c>STY01</c> (a valid ORG_ONLY grant target — a MAO would be rejected).</para>
///
/// <para><b>Witness keying:</b> org-create uses an explicit test-unique <c>org_id</c>, so its
/// state/outbox/event witnesses key on that id / its <c>org-{id}</c> stream (nothing legitimate is
/// on that stream — the create is the only op and it rolled back). Role-grant's assignment_id is
/// server-generated and never returned, so its state witness keys on the (test-unique) user_id,
/// its audit witness on the details-JSON userId, and its outbox/event on the <c>user-{userId}</c>
/// stream (clean — the user was seeded via direct SQL, which emits no event).</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AdminAtomicHttpTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string SeededOrganisationId = "STY01"; // init.sql-seeded ORGANISATION (valid grant target)

    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        _ = _factory.CreateClient(); // boot base-host seeders before any throwing host derives
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ─────────────────────────────────── ORGANIZATION CREATE ─────────────────────────────────

    /// <summary>
    /// <c>POST /api/admin/organizations</c> creating a root MAO (no parent). Falsifiability: the
    /// handler INSERTs the organizations row then <c>EnqueueAndReturnIdAsync(conn,tx)</c> +
    /// audit-projection in ONE explicit tx, then <c>CommitAsync</c>, under a
    /// <c>catch { Rollback; throw }</c>. The throwing outbox faults the enqueue (an
    /// InvalidOperationException, NOT the 23505 the retry loop swallows) → the org row rolls back
    /// and the 5xx surfaces. Goes RED if the route is deleted, if <c>CommitAsync</c> moves before
    /// the enqueue, or if the INSERT is taken out of the shared (conn,tx).
    /// </summary>
    [Fact]
    public async Task OrganizationCreate_RealEndpoint_OutboxThrows_RollsBackWholeCreate()
    {
        var orgId = "ORG_FR_HTTP_" + Rand();
        var streamId = $"org-{orgId}";

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var rsp = await client.PostAsJsonAsync("/api/admin/organizations", new
        {
            orgId,
            orgName = "Forced-Rollback HTTP Org",
            orgType = "MAO", // root ⇒ no parent ⇒ HasGlobalScope gate (satisfied by the GLOBAL scope)
        });

        AssertServerError(rsp);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "organizations", $"org_id = '{orgId}'");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
    }

    // ───────────────────────────────────────── ROLE GRANT ────────────────────────────────────

    /// <summary>
    /// <c>POST /api/admin/roles/grant</c> (the role-assignment mutation). Falsifiability: the
    /// handler INSERTs role_assignments + role_assignment_audit + <c>EnqueueAndReturnIdAsync(conn,tx)</c>
    /// + audit-projection in ONE tx, then <c>CommitAsync</c>, under <c>catch { Rollback; throw }</c>
    /// — this is the load-bearing sub-shape (pre-S26 the audit + assignment committed while the emit
    /// ran post-commit). The throwing outbox faults the enqueue → all three rows roll back and the
    /// 5xx surfaces. Goes RED if the route is deleted, if <c>CommitAsync</c> moves before the
    /// enqueue, or if any of the three INSERTs leaves the shared (conn,tx).
    /// </summary>
    [Fact]
    public async Task RoleGrant_RealEndpoint_OutboxThrows_RollsBackWholeGrant()
    {
        var userId = "USR_FR_HTTP_" + Rand();
        await SeedUserAsync(userId, SeededOrganisationId);
        var streamId = $"user-{userId}";

        using var throwingHost = _factory.WithThrowingOutbox();
        var client = ThrowingClient(throwingHost);
        var rsp = await client.PostAsJsonAsync("/api/admin/roles/grant", new
        {
            userId,
            roleId = "EMPLOYEE",
            orgId = SeededOrganisationId,
            scopeType = "ORG_ONLY",
        });

        AssertServerError(rsp);

        // No role_assignments row for the (test-unique) user, no GRANTED audit row (its details
        // JSON carries the user id), no outbox/event on the user stream.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "role_assignments", $"user_id = '{userId}'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "role_assignment_audit",
            $"action = 'GRANTED' AND details::text LIKE '%{userId}%'");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(_harness.ConnectionString, streamId);
        await ForcedRollbackHarness.AssertNoEventRowAsync(_harness.ConnectionString, streamId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static string Rand() => Guid.NewGuid().ToString("N").Substring(0, 8);

    private static void AssertServerError(HttpResponseMessage rsp) =>
        Assert.True((int)rsp.StatusCode >= 500,
            $"expected a 5xx from the escaped outbox throw, got {(int)rsp.StatusCode}");

    /// <summary>
    /// Seeds a target user (for the role-grant) directly via SQL — the grant only requires the user
    /// to EXIST; a direct INSERT (no event) keeps the <c>user-{userId}</c> stream clean for the
    /// stream-keyed outbox/event witnesses. Primary org = the init.sql-seeded ORGANISATION STY01.
    /// </summary>
    private async Task SeedUserAsync(string userId, string primaryOrgId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, primary_org_id, agreement_code, ok_version)
            VALUES (@userId, @username, 'x', 'Forced-Rollback HTTP User', @orgId, 'AC', 'OK24')
            ON CONFLICT (user_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("username", userId.ToLowerInvariant());
        cmd.Parameters.AddWithValue("orgId", primaryOrgId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static HttpClient ThrowingClient(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintGlobalAdminToken("admin_qual016"));
        return client;
    }

    /// <summary>
    /// A JWT claiming <c>role=GlobalAdmin</c> WITH a single <c>GLOBAL</c>-typed
    /// <see cref="RoleScope"/> (role GlobalAdmin). The <c>LocalAdminOrAbove</c> policy has
    /// <c>requireOrgScope:true</c>, so a bare role token would be rejected — the GLOBAL scope both
    /// satisfies the policy and admits every in-handler gate (<c>HasGlobalScope</c> for the MAO
    /// create, <c>ValidateOrgAccessAsync</c> for the role grant's org check).
    /// </summary>
    private static string MintGlobalAdminToken(string actorId)
    {
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        return tokenService.GenerateToken(
            employeeId: actorId,
            name: actorId,
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }
}
