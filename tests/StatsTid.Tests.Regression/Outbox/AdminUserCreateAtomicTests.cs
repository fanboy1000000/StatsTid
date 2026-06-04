using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S31 / TASK-3110 D-tests — 4-way atomicity contract on
/// <c>POST /api/admin/users</c> (TASK-3108 AdminEndpoints extension). The extended
/// handler commits four operations in a single transaction per ADR-018 D3:
///
/// <list type="bullet">
///   <item><description>(1) <c>users</c> INSERT</description></item>
///   <item><description>(2) <c>employee_profiles</c> INSERT (S31 invariant: every
///   active user has exactly one live profile row)</description></item>
///   <item><description>(3) <c>UserCreated</c> outbox event on stream
///   <c>user-{userId}</c></description></item>
///   <item><description>(4) <c>EmployeeProfileCreated</c> outbox event on stream
///   <c>employee-profile-{userId}</c></description></item>
/// </list>
///
/// <para>
/// Two tests: a happy-path 4-way emit (POST succeeds → all four operations land) and
/// a negative duplicate-username path (POST is rejected by the pre-flight 409 → NO
/// new rows in <c>users</c> or <c>employee_profiles</c>, NO new outbox events on
/// either stream). The negative case is the load-bearing atomicity pin: a partial
/// rollback (e.g. only users INSERTed) would manifest here as a leaked row or event.
/// </para>
///
/// <para>
/// The pre-flight existence check in <c>AdminEndpoints.cs:319-327</c> runs OUTSIDE
/// the transaction and short-circuits before <c>BeginTransactionAsync</c> — the
/// duplicate-username test therefore pins the "no leaked state" invariant via the
/// short-circuit path. A genuine in-tx rollback (e.g. on outbox throw) is covered
/// by <see cref="AdminAtomicTests"/>' related sub-shape (i) test against
/// <c>POST /api/admin/organizations</c> + S26 <c>TxContractTests</c>.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AdminUserCreateAtomicTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Happy path — POST /api/admin/users emits all four operations atomically.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task AdminUserCreate_AtomicallyCreatesProfileRowAndEmitsEvent()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // Unique userId per test run — keeps xUnit parallel test execution clean.
        var newUserId = "emp_s31_atomic_" + Guid.NewGuid().ToString("N").Substring(0, 8);

        var body = new
        {
            userId = newUserId,
            username = newUserId,
            password = "TestPassword123!",
            displayName = "S31 Atomic Test User",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        };

        var rsp = await client.PostAsJsonAsync("/api/admin/users", body);
        Assert.Equal(HttpStatusCode.Created, rsp.StatusCode);

        // ── DB assertions: all four operations landed.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // (1) users row
        await using (var usersCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users WHERE user_id = @userId", conn))
        {
            usersCmd.Parameters.AddWithValue("userId", newUserId);
            Assert.Equal(1L, Convert.ToInt64(await usersCmd.ExecuteScalarAsync()));
        }

        // (2) employee_profiles row with S31 defaults (part_time_fraction=1.000,
        //     position=NULL, version=1).
        // S53/TASK-5306 (a7aee58): employee_profiles.weekly_norm_hours removed
        // (universal 37h norm); column dropped from SELECT, ordinals shift down by one.
        await using (var profileCmd = new NpgsqlCommand(
            """
            SELECT part_time_fraction, position, version
            FROM employee_profiles
            WHERE employee_id = @employeeId AND effective_to IS NULL
            """, conn))
        {
            profileCmd.Parameters.AddWithValue("employeeId", newUserId);
            await using var reader = await profileCmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(),
                $"Expected one live employee_profiles row for '{newUserId}'.");
            Assert.Equal(1.000m, reader.GetDecimal(0));
            Assert.True(reader.IsDBNull(1), "Position should default to NULL for new users.");
            Assert.Equal(1L, reader.GetInt64(2));
            // Partial-unique-index guarantees exactly one live row.
            Assert.False(await reader.ReadAsync(),
                $"Expected exactly one live row for '{newUserId}', found more than one.");
        }

        // (3) UserCreated outbox event on stream user-{userId}.
        var userStreamId = $"user-{newUserId}";
        await using (var userEventCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @streamId AND event_type = 'UserCreated'
            """, conn))
        {
            userEventCmd.Parameters.AddWithValue("streamId", userStreamId);
            Assert.Equal(1L, Convert.ToInt64(await userEventCmd.ExecuteScalarAsync()));
        }

        // (4) EmployeeProfileCreated outbox event on stream employee-profile-{userId}.
        var profileStreamId = $"employee-profile-{newUserId}";
        await using (var profileEventCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM outbox_events
            WHERE stream_id = @streamId AND event_type = 'EmployeeProfileCreated'
            """, conn))
        {
            profileEventCmd.Parameters.AddWithValue("streamId", profileStreamId);
            Assert.Equal(1L, Convert.ToInt64(await profileEventCmd.ExecuteScalarAsync()));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Negative — duplicate username pre-flight 409: NO rows + NO outbox events.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task AdminUserCreate_OnDuplicateUsername_RollsBackAllFourOperations()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintAdminToken());

        // First, create a baseline user that we can collide against. Use a unique id so
        // sibling parallel tests don't collide on this row.
        var seedUserId = "emp_s31_dup_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var seedBody = new
        {
            userId = seedUserId,
            username = seedUserId,
            password = "TestPassword123!",
            displayName = "S31 Dup-Seed User",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        };
        var seedRsp = await client.PostAsJsonAsync("/api/admin/users", seedBody);
        Assert.Equal(HttpStatusCode.Created, seedRsp.StatusCode);

        // Now POST a SECOND user that re-uses the same username. New userId, same
        // username → endpoint pre-flight at AdminEndpoints.cs:319-327 returns 409.
        var collidingUserId = "emp_s31_collide_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var collideBody = new
        {
            userId = collidingUserId,    // DIFFERENT from seed's userId
            username = seedUserId,       // SAME as seed's username → triggers 409
            password = "AnotherPassword!",
            displayName = "S31 Collision Attempt",
            email = (string?)null,
            primaryOrgId = "STY01",
            agreementCode = "AC",
            okVersion = "OK24",
        };

        var collideRsp = await client.PostAsJsonAsync("/api/admin/users", collideBody);
        Assert.Equal(HttpStatusCode.Conflict, collideRsp.StatusCode);

        // ── Atomicity assertions: the colliding userId must have left no trace
        //    in any of the four mutation surfaces.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();

        // (1) NO users row with the colliding userId.
        await using (var usersCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users WHERE user_id = @userId", conn))
        {
            usersCmd.Parameters.AddWithValue("userId", collidingUserId);
            Assert.Equal(0L, Convert.ToInt64(await usersCmd.ExecuteScalarAsync()));
        }

        // (2) NO employee_profiles row with the colliding employeeId.
        await using (var profileCmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM employee_profiles
            WHERE employee_id = @employeeId
            """, conn))
        {
            profileCmd.Parameters.AddWithValue("employeeId", collidingUserId);
            Assert.Equal(0L, Convert.ToInt64(await profileCmd.ExecuteScalarAsync()));
        }

        // (3) NO UserCreated outbox event on stream user-{collidingUserId}.
        var userStreamId = $"user-{collidingUserId}";
        await using (var userEventCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @streamId", conn))
        {
            userEventCmd.Parameters.AddWithValue("streamId", userStreamId);
            Assert.Equal(0L, Convert.ToInt64(await userEventCmd.ExecuteScalarAsync()));
        }

        // (4) NO EmployeeProfileCreated outbox event on stream
        //     employee-profile-{collidingUserId}.
        var profileStreamId = $"employee-profile-{collidingUserId}";
        await using (var profileEventCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @streamId", conn))
        {
            profileEventCmd.Parameters.AddWithValue("streamId", profileStreamId);
            Assert.Equal(0L, Convert.ToInt64(await profileEventCmd.ExecuteScalarAsync()));
        }

        // Defense in depth — the seed user's row is still present (proves the 409
        // path didn't accidentally cascade a delete on the colliding-username row).
        await using (var seedSurvivesCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM users WHERE user_id = @userId", conn))
        {
            seedSurvivesCmd.Parameters.AddWithValue("userId", seedUserId);
            Assert.Equal(1L, Convert.ToInt64(await seedSurvivesCmd.ExecuteScalarAsync()));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string MintAdminToken()
    {
        var settings = new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        };
        var tokenService = new JwtTokenService(settings);
        return tokenService.GenerateToken(
            employeeId: "ADMIN_S31_QA",
            name: "S31 QA Admin",
            role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC",
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }
}
