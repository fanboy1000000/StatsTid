using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using Xunit.Sdk;

namespace StatsTid.Tests.Regression.Contracts;

/// <summary>
/// S118 / TASK-11802 — the FIRST endpoint tests for <c>POST /api/auth/login</c>
/// (<c>LoginResponse</c>, typed in retrofit Pass 5 / TASK-11800). BOTH handler branches are
/// proven against the ONE declared contract:
///
/// <list type="bullet">
///   <item><description><b>The in-memory branch</b> (<c>Auth:UseDatabase=false</c> — now an
///     EXPLICIT opt-in via <see cref="InMemoryAuthWebApplicationFactory"/> after the SEC-020
///     fail-closed default flip, S130): the dictionary-user path, whose serialized body pins
///     <c>orgId: null</c> (the nullable member is PRESENT-and-null, never omitted — the
///     required/nullable orthogonality).</description></item>
///   <item><description><b>The DB branch</b> (the PRODUCTION auth path — BCrypt verify +
///     role-assignment scopes + the canonical <c>user_agreement_codes</c> read): UNREACHABLE
///     under <see cref="StatsTidWebApplicationFactory"/> (it only overrides the connection
///     string), so it is driven through <see cref="DbAuthWebApplicationFactory"/> — the
///     Step-0b Reviewer W4 AUTHORIZED tests-only scaffolding. The login user is created
///     through the REAL admin user-create endpoint (server-side BCrypt hashing) + the REAL
///     role grant — no SQL-faked credentials; the only SQL seed is the ORGANISATION input row
///     the user FK-requires. The DB branch serves <c>orgId</c> POPULATED (the user's
///     primary org) — both nullable states of the same declared member, live.</description></item>
/// </list>
///
/// <para><b>Seed disjointness:</b> a FRESH testcontainer per test; org <c>S118LORG</c> +
/// user <c>s118l_user</c> + actor <c>s118l_gadmin</c> are S118-fresh (no overlap with any
/// suite; the in-memory branch's <c>admin01</c> is the PRODUCT's hard-coded dictionary user,
/// not a seed). Matcher + Support consumed AS-IS.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class S118LoginSpecRuntimeTests : IAsyncLifetime
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string LoginPath = "/api/auth/login";

    private const string Org = "S118LORG";
    private const string DbUser = "s118l_user";
    private const string DbPassword = "S118!Kodeord1";

    /// <summary>The EXACT 5 camelCase members of <c>LoginResponse</c>.</summary>
    private static readonly string[] LoginKeys = { "token", "expiresAt", "employeeId", "role", "orgId" };

    private TestFixtures.DockerHarness _harness = null!;
    private JsonElement _spec;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _spec = SpecRuntimeTestSupport.LoadCommittedSpec();
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Branch 1 — the in-memory path (Auth:UseDatabase=false, the explicit-opt-in factory).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The dictionary-user branch under the EXPLICIT-opt-in
    /// <see cref="InMemoryAuthWebApplicationFactory"/> (post-SEC-020 the flag must be forced to
    /// <c>false</c> at host-config time — the default is now <c>true</c>): the matcher proves the
    /// declared <c>LoginResponse</c> against the real 200, and the serialized body pins
    /// <c>orgId: null</c> — the nullable member is PRESENT-and-null (required fidelity), never
    /// dropped. Exact 5-member key set.</summary>
    [Fact]
    public async Task Login_Post200_InMemoryBranch_OrgIdServedNull()
    {
        await using var factory = new InMemoryAuthWebApplicationFactory(_harness.ConnectionString);
        using var anonymous = factory.CreateClient(); // login is anonymous — no bearer token

        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, anonymous,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, LoginPath,
                """{ "username": "admin01", "password": "admin" }"""),
            LoginPath, "post");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, LoginKeys, "login 200 (in-memory branch)");
        Assert.Equal(JsonValueKind.Null, root.GetProperty("orgId").ValueKind); // THE pin: null-but-PRESENT
        Assert.Equal("admin01", root.GetProperty("employeeId").GetString());
        Assert.Equal(StatsTidRoles.GlobalAdmin, root.GetProperty("role").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("token").GetString()));
        Assert.Equal(JsonValueKind.String, root.GetProperty("expiresAt").ValueKind);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  SEC-020 (S130) — the fail-closed boundary, proven by BEHAVIOR.
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The load-bearing SEC-020 proof: the DEFAULT <see cref="StatsTidWebApplicationFactory"/>
    /// sets NO <c>Auth:UseDatabase</c> override, so after the fail-closed default flip
    /// (<c>Program.cs:371</c> now defaults the flag to <c>true</c>) login routes to the
    /// DB/BCrypt branch — the hardcoded in-memory admin is unreachable by omission. Although
    /// <c>admin01</c> IS seeded by <c>init.sql</c> (applied in <see cref="InitializeAsync"/>),
    /// its stored hash is BCrypt of <c>"password"</c>, so verifying the in-memory table's
    /// <c>"admin"</c> secret FAILS ⇒ <b>401 Unauthorized</b> — never a 200, never a GlobalAdmin
    /// token. This pins the security boundary by observable behaviour, not just the config literal.
    /// </summary>
    [Fact]
    public async Task Login_Post_DefaultFactory_FailsClosed_HardcodedAdminCreds_Return401()
    {
        // Default factory = no auth override ⇒ the new fail-closed default (DB/BCrypt) is in force.
        await using var factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        using var anonymous = factory.CreateClient(); // login is anonymous — no bearer token

        using var response = await anonymous.SendAsync(
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, LoginPath,
                """{ "username": "admin01", "password": "admin" }"""));

        // The in-memory admin01/"admin" = GlobalAdmin path must NOT be served: DB-branch BCrypt
        // verify of "admin" against the seeded hash-of-"password" fails ⇒ 401 (fail-closed).
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ════════════════════════════════════════════════════════════════════════════════
    //  Branch 2 — the DB path (Auth:UseDatabase=true, the AUTHORIZED derived factory).
    // ════════════════════════════════════════════════════════════════════════════════

    /// <summary>The PRODUCTION auth branch, end-to-end real: the user is created through the
    /// REAL admin user-create POST (server-side BCrypt hash) and granted EMPLOYEE through the
    /// REAL role-grant POST; login then BCrypt-verifies and mints from role-assignment scopes.
    /// The same declared contract matches, with <c>orgId</c> POPULATED (the other nullable
    /// state, live) and <c>role</c> derived from the granted assignment.</summary>
    [Fact]
    public async Task Login_Post200_DbBranch_ProductionAuthPath_OrgIdPopulated()
    {
        await using var factory = new DbAuthWebApplicationFactory(_harness.ConnectionString);
        _ = factory.CreateClient(); // boot seeders

        await SeedOrganisationAsync(); // INPUT row only — the user's FK/ORGANISATION-check target

        using (var admin = AdminClient(factory))
        {
            await PostOkAsync(admin, "/api/admin/users",
                $$"""
                {
                  "userId": "{{DbUser}}", "username": "{{DbUser}}", "password": "{{DbPassword}}",
                  "displayName": "S118 Login Bruger", "email": "s118l_user@test.dk",
                  "primaryOrgId": "{{Org}}", "agreementCode": "AC", "okVersion": "OK24"
                }
                """);
            await PostOkAsync(admin, "/api/admin/roles/grant",
                $$"""{ "userId": "{{DbUser}}", "roleId": "EMPLOYEE", "orgId": "{{Org}}", "scopeType": "ORG_ONLY" }""");
        }

        using var anonymous = factory.CreateClient(); // login is anonymous
        var body = await SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync(
            _spec, anonymous,
            SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, LoginPath,
                $$"""{ "username": "{{DbUser}}", "password": "{{DbPassword}}" }"""),
            LoginPath, "post");

        var root = JsonDocument.Parse(body).RootElement;
        S118ContractAssert.AssertExactKeySet(root, LoginKeys, "login 200 (DB branch)");
        Assert.Equal(Org, root.GetProperty("orgId").GetString());   // the POPULATED nullable state, live
        Assert.Equal(DbUser, root.GetProperty("employeeId").GetString());
        Assert.Equal(StatsTidRoles.Employee, root.GetProperty("role").GetString()); // from the REAL grant
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("token").GetString()));
    }

    // ─────────────────────────────── the AUTHORIZED derived factory ───────────────────────────────

    /// <summary>
    /// The Step-0b Reviewer W4 AUTHORIZED tests-only scaffolding: a derived factory flipping
    /// <c>Auth:UseDatabase=true</c> so the production DB-auth branch of the login endpoint is
    /// reachable under test. <see cref="StatsTidWebApplicationFactory"/> is sealed, so this
    /// mirrors its TASK-3001b HOST-configuration timing verbatim — Program.cs reads BOTH
    /// <c>ConnectionStrings:EventStore</c> (L11-12) and <c>Auth:UseDatabase</c> (L371) off
    /// <c>builder.Configuration</c> BEFORE <c>Build()</c>, and only host configuration lands
    /// early enough for those reads (ConfigureWebHost-time app configuration was empirically
    /// too late — the TASK-3001 diagnosis). Test-side config only; no product change.
    /// </summary>
    private sealed class DbAuthWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public DbAuthWebApplicationFactory(string connectionString)
            => _connectionString = connectionString;

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:EventStore"] = _connectionString,
                    ["Auth:UseDatabase"] = "true",
                }));
            return base.CreateHost(builder);
        }
    }

    /// <summary>
    /// SEC-020 (S130) — the SYMMETRIC counterpart of <see cref="DbAuthWebApplicationFactory"/>:
    /// it forces <c>Auth:UseDatabase=false</c> so the (now explicit-opt-in) in-memory login branch
    /// is reachable under test AFTER the fail-closed default flip (<c>Program.cs:371</c> now
    /// defaults the flag to <c>true</c>). Before the flip this case rode the factory default and
    /// needed no override; now it MUST inject <c>false</c> — and it must do so at the SAME
    /// HOST-configuration layer the true-factory uses (<see cref="IHostBuilder.ConfigureHostConfiguration"/>
    /// via <c>CreateHost</c>), because <c>Program.cs</c> reads <c>Auth:UseDatabase</c> at L371 off
    /// <c>builder.Configuration</c> BEFORE <c>Build()</c>. Per the documented TASK-3001 gotcha,
    /// <c>ConfigureAppConfiguration</c>/<c>UseSetting</c>/<c>WithWebHostBuilder</c> fire TOO LATE and
    /// would SILENTLY NO-OP — the read would already have seen the new default <c>true</c>, quietly
    /// routing this "in-memory" case through the DB branch. Test-side config only; no product change.
    /// </summary>
    private sealed class InMemoryAuthWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;

        public InMemoryAuthWebApplicationFactory(string connectionString)
            => _connectionString = connectionString;

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(cfg => cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:EventStore"] = _connectionString,
                    ["Auth:UseDatabase"] = "false",
                }));
            return base.CreateHost(builder);
        }
    }

    // ─────────────────────────────── clients / seeds ───────────────────────────────

    /// <summary>A GlobalAdmin client for the derived factory (mirrors the Support helper's JWT
    /// minting — Support's own helper is typed to <see cref="StatsTidWebApplicationFactory"/>
    /// and is consumed AS-IS elsewhere; the S117 ActorClient precedent).</summary>
    private static HttpClient AdminClient(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: "s118l_gadmin", name: "s118l_gadmin", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: Org,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, "/", "GLOBAL") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task PostOkAsync(HttpClient client, string url, string jsonBody)
    {
        using var response = await client.SendAsync(SpecRuntimeTestSupport.JsonRequest(HttpMethod.Post, url, jsonBody));
        var body = await response.Content.ReadAsStringAsync();
        var status = (int)response.StatusCode;
        if (status is < 200 or >= 300)
            throw new XunitException($"POST {url} returned {status}: {body}");
    }

    /// <summary>The ORGANISATION input row the DB-branch user FK-requires (the user itself and
    /// its role are created through the REAL endpoints — never SQL-faked).</summary>
    private async Task SeedOrganisationAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, parent_org_id,
                                       materialized_path, agreement_code, ok_version)
            VALUES ('S118LORG', 'S118 Login Styrelse', 'ORGANISATION', NULL, '/S118LORG/', 'AC', 'OK24')
            ON CONFLICT DO NOTHING
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
