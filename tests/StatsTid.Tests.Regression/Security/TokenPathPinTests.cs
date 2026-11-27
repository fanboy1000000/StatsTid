using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// The login pins L-1 .. L-10 (S149 TASK-14901): how a login token is validated by the REAL middleware
/// (<see cref="JwtValidationSetup.AddStatsTidJwtAuth"/>) and what the application then sees through
/// <see cref="ActorContextExtensions.GetActorContext"/>, written down as executable facts on the
/// pre-refresh runtime so that every later move of the token library must reproduce them unchanged.
///
/// <para>Kind: every fact is <b>R</b> (a regression pin, an existing behaviour kept). Non-vacuity is proven
/// by named mutations of <c>JwtValidationSetup.cs</c> (M-5 audience, M-6 claim mapping, M-7 clock skew).
/// The L-9a and L-9b literals are CAPTURED outcomes of the pre-refresh runtime; they detect movement and
/// have no mutation.</para>
///
/// <para>Non-Docker: an in-memory test server, no container. This file is frozen from the commit CI-W0 runs
/// green on; it names no runtime version (ADR-045).</para>
/// </summary>
public sealed class TokenPathPinTests
{
    private const string Issuer = "statstid";
    private const string Audience = "statstid";
    private const string Key = "TokenPathPin_SigningKey_AtLeast32Bytes_Long!!";

    private static readonly RoleScope[] Scopes =
    {
        new RoleScope("LOCAL_HR", "STY01", "ORG_ONLY"),
        new RoleScope("LOCAL_LEADER", "STY02", "ORG_ONLY"),
    };

    private sealed class Host : IAsyncDisposable
    {
        public WebApplication App = null!;
        public HttpClient Client = null!;
        public int ProbeHits;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }

    private static async Task<Host> StartAsync(string signingKey = Key)
    {
        var host = new Host();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = signingKey });
        builder.Services.AddStatsTidJwtAuth(builder.Configuration, builder.Environment);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/probe", (HttpContext ctx) =>
        {
            Interlocked.Increment(ref host.ProbeHits);
            return Results.Json(ctx.GetActorContext());
        }).RequireAuthorization();
        app.MapGet("/claims", (HttpContext ctx) => Results.Json(new
        {
            HasSub = ctx.User.FindFirst("sub") is not null,
            HasNameIdentifier = ctx.User.FindFirst(ClaimTypes.NameIdentifier) is not null,
            HasRole = ctx.User.FindFirst("role") is not null,
            IsInRole = ctx.User.IsInRole("LOCAL_HR"),
        })).RequireAuthorization();
        await app.StartAsync();

        host.App = app;
        host.Client = app.GetTestClient();
        return host;
    }

    private static JwtSettings Settings(string key = Key, int minutes = 60) =>
        new() { Issuer = Issuer, Audience = Audience, SigningKey = key, ExpirationMinutes = minutes };

    private static string LegacyToken(string key = Key) =>
        new JwtTokenService(Settings(key)).GenerateToken("emp001", "Test Person", "LOCAL_HR", "AC", "STY01", Scopes);

    private static string HandlerToken(
        string issuer = Issuer, string audience = Audience, string key = Key,
        DateTime? notBefore = null, DateTime? expires = null)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", "emp001"),
                new Claim("name", "Test Person"),
                new Claim("role", "LOCAL_HR"),
                new Claim("employee_id", "emp001"),
                new Claim("agreement_code", "AC"),
                new Claim("org_id", "STY01"),
                new Claim("scopes", JsonSerializer.Serialize(Scopes)),
            }),
            NotBefore = notBefore,
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static async Task<HttpResponseMessage> GetAsync(Host host, string token, string path = "/probe")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await host.Client.SendAsync(request);
    }

    private static async Task<ActorContext> ReadActorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actor = await response.Content.ReadFromJsonAsync<ActorContext>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(actor);
        return actor!;
    }

    private static void AssertMintedActor(ActorContext actor)
    {
        Assert.Equal("emp001", actor.ActorId);
        Assert.Equal("LOCAL_HR", actor.ActorRole);
        Assert.Equal("STY01", actor.OrgId);
        Assert.NotNull(actor.Scopes);
        Assert.Equal(2, actor.Scopes!.Length);
        Assert.Equal(Scopes[0], actor.Scopes[0]);
        Assert.Equal(Scopes[1], actor.Scopes[1]);
    }

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>An HS256 token built without the signing library: the test encodes header and payload and
    /// computes the signature with HMACSHA256. Valid for one hour.</summary>
    private static string HandBuiltHs256Token(byte[] keyBytes)
    {
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var header = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var payload = B64Url(Encoding.UTF8.GetBytes(
            "{\"sub\":\"emp001\",\"role\":\"LOCAL_HR\",\"iss\":\"statstid\",\"aud\":\"statstid\",\"exp\":" + exp + "}"));
        var signingInput = Encoding.ASCII.GetBytes(header + "." + payload);
        using var hmac = new HMACSHA256(keyBytes);
        return header + "." + payload + "." + B64Url(hmac.ComputeHash(signingInput));
    }

    /// <summary>Kind R. L-1: a token from the legacy handler (<see cref="JwtTokenService"/>) passes and the
    /// probe sees every minted field, including both scopes.</summary>
    [Fact]
    public async Task L1_LegacyHandlerToken_PassesAndActorContextEqualsMinted()
    {
        await using var host = await StartAsync();
        var actor = await ReadActorAsync(await GetAsync(host, LegacyToken()));
        AssertMintedActor(actor);
    }

    /// <summary>Kind R. L-2: claim types arrive unmapped (<c>sub</c>, <c>role</c>); the role check works.
    /// Red conditions: M-6 (<c>MapInboundClaims = true</c>) makes <c>sub</c> arrive as NameIdentifier.</summary>
    [Fact]
    public async Task L2_ClaimTypesArriveUnmapped()
    {
        await using var host = await StartAsync();
        var response = await GetAsync(host, LegacyToken(), "/claims");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("hasSub").GetBoolean());
        Assert.False(body.GetProperty("hasNameIdentifier").GetBoolean());
        Assert.True(body.GetProperty("hasRole").GetBoolean());
        Assert.True(body.GetProperty("isInRole").GetBoolean());
    }

    /// <summary>Kind R. L-3: a wrong issuer is refused with 401 and the probe is not reached.</summary>
    [Fact]
    public async Task L3_WrongIssuer_Is401()
    {
        await using var host = await StartAsync();
        var response = await GetAsync(host, HandlerToken(issuer: "someone-else"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.ProbeHits);
    }

    /// <summary>Kind R. L-4: a wrong audience is refused with 401.
    /// Red conditions: M-5 (<c>ValidateAudience = false</c>).</summary>
    [Fact]
    public async Task L4_WrongAudience_Is401()
    {
        await using var host = await StartAsync();
        var response = await GetAsync(host, HandlerToken(audience: "someone-else"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.ProbeHits);
    }

    /// <summary>Kind R. L-5: expired 5 minutes ago is 401 (beyond the 1-minute skew).
    /// Red conditions: M-7 (<c>ClockSkew = 10 min</c>).</summary>
    [Fact]
    public async Task L5_ExpiredFiveMinutesAgo_Is401()
    {
        await using var host = await StartAsync();
        var now = DateTime.UtcNow;
        var response = await GetAsync(host, HandlerToken(notBefore: now.AddMinutes(-60), expires: now.AddMinutes(-5)));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.ProbeHits);
    }

    /// <summary>Kind R. L-6: expired 30 seconds ago is still 200 (inside the 1-minute skew).</summary>
    [Fact]
    public async Task L6_ExpiredThirtySecondsAgo_Is200()
    {
        await using var host = await StartAsync();
        var now = DateTime.UtcNow;
        var response = await GetAsync(host, HandlerToken(notBefore: now.AddMinutes(-60), expires: now.AddSeconds(-30)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, host.ProbeHits);
    }

    /// <summary>Kind R. L-7: a token signed with a different key is 401.</summary>
    [Fact]
    public async Task L7_DifferentSigningKey_Is401()
    {
        await using var host = await StartAsync();
        var response = await GetAsync(host, HandlerToken(key: "A_Completely_Different_Key_Of_Ample_Length_!!"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.ProbeHits);
    }

    /// <summary>Kind R. L-8: an unsigned token (<c>alg: none</c>) is 401.</summary>
    [Fact]
    public async Task L8_AlgNone_Is401()
    {
        await using var host = await StartAsync();
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var header = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        var payload = B64Url(Encoding.UTF8.GetBytes(
            "{\"sub\":\"emp001\",\"role\":\"GLOBAL_ADMIN\",\"iss\":\"statstid\",\"aud\":\"statstid\",\"exp\":" + exp + "}"));
        var response = await GetAsync(host, header + "." + payload + ".");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, host.ProbeHits);
    }

    private const bool L9A_Mints31 = false;
    private const bool L9A_Mints32 = true;

    /// <summary>Kind R. L-9a: the key-length minimum AT ISSUANCE on the pre-refresh runtime, through the
    /// legacy minting path. CAPTURED outcome (movement detector, no mutation).</summary>
    [Fact]
    public void L9a_KeyLengthAtIssuance_CapturedOutcome()
    {
        Assert.Equal(L9A_Mints31, Mints(new string('k', 31)));
        Assert.Equal(L9A_Mints32, Mints(new string('k', 32)));
    }

    private static bool Mints(string key)
    {
        try { LegacyToken(key); return true; }
        catch (ArgumentException) { return false; }
    }

    private const HttpStatusCode L9B_31_Status = HttpStatusCode.Unauthorized;
    private const int L9B_31_ProbeHits = 0;
    private const HttpStatusCode L9B_32_Status = HttpStatusCode.OK;
    private const int L9B_32_ProbeHits = 1;

    /// <summary>Kind R (movement detector, no mutation). L-9b: the key-length minimum AT THE VALIDATOR.
    /// Hand-built HS256 tokens (no signing library) signed with a 31-byte and a 32-byte key, each sent to a
    /// host configured with the matching key. CAPTURED literals: status and whether the probe was reached.</summary>
    [Fact]
    public async Task L9b_KeyLengthAtValidator_CapturedOutcome()
    {
        var key31 = new string('k', 31);
        var key32 = new string('k', 32);

        await using (var h31 = await StartAsync(key31))
        {
            var r = await GetAsync(h31, HandBuiltHs256Token(Encoding.UTF8.GetBytes(key31)));
            Assert.Equal(L9B_31_Status, r.StatusCode);
            Assert.Equal(L9B_31_ProbeHits, h31.ProbeHits);
        }
        await using (var h32 = await StartAsync(key32))
        {
            var r = await GetAsync(h32, HandBuiltHs256Token(Encoding.UTF8.GetBytes(key32)));
            Assert.Equal(L9B_32_Status, r.StatusCode);
            Assert.Equal(L9B_32_ProbeHits, h32.ProbeHits);
        }
    }

    /// <summary>Kind R. L-10: a token from <see cref="JsonWebTokenHandler"/> (the Smoke helpers' path) gives
    /// the same <see cref="ActorContext"/> as L-1, field by field.</summary>
    [Fact]
    public async Task L10_JsonWebTokenHandlerToken_GivesSameActorContextAsLegacy()
    {
        await using var host = await StartAsync();
        var legacy = await ReadActorAsync(await GetAsync(host, LegacyToken()));
        var modern = await ReadActorAsync(await GetAsync(host, HandlerToken()));
        AssertMintedActor(modern);
        Assert.Equal(legacy.ActorId, modern.ActorId);
        Assert.Equal(legacy.ActorRole, modern.ActorRole);
        Assert.Equal(legacy.OrgId, modern.OrgId);
        Assert.Equal(legacy.Scopes, modern.Scopes);
    }
}
