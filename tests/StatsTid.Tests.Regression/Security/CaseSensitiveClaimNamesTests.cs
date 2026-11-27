using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StatsTid.Auth;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// Claim names are matched case-sensitively once a login token has been validated.
///
/// <para>This pins IdentityModel 8's accepted <c>CaseSensitiveClaimsIdentity</c> change (plan P-4 point 7;
/// TASK-14901 ruling R-5). A token that carries <c>Sub</c> and <c>Role</c> (capitalised) and no lowercase
/// <c>sub</c> or <c>role</c> is still authenticated (signature, issuer, audience and lifetime are fine), but
/// <see cref="ActorContextExtensions.GetActorContext"/> finds neither claim: <c>ActorRole</c> is null and
/// <c>ActorId</c> is null. Every lookup in <c>src</c> uses exact casing and every minted claim name is
/// lowercase, so nothing in StatsTid issues such a token; the fact keeps the change visible so a later upgrade
/// cannot silently flip it back. On the pre-refresh token library the same token resolved both claims.</para>
///
/// <para>Kind: <b>R</b>, a regression pin of the post-upgrade behaviour. Non-vacuity is the named mutation
/// M-CS: setting the AppContext switch <c>Microsoft.IdentityModel.Tokens.UseClaimsIdentityType</c> to true
/// before the first validation in the process restores the case-insensitive identity type, and this fact
/// then fails on <c>ActorRole</c>.</para>
///
/// <para>Non-Docker: an in-memory test server. A separate file, because the login-pin file is frozen.</para>
/// </summary>
public sealed class CaseSensitiveClaimNamesTests
{
    private const string Issuer = "statstid";
    private const string Audience = "statstid";
    private const string Key = "CaseSensitiveClaims_SigningKey_AtLeast32Bytes!";

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string MiscasedToken()
    {
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var header = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var json = "{\"Sub\":\"emp001\",\"Role\":\"LOCAL_HR\",\"org_id\":\"STY01\","
            + "\"iss\":\"" + Issuer + "\",\"aud\":\"" + Audience + "\",\"exp\":" + exp + "}";
        var payload = B64Url(Encoding.UTF8.GetBytes(json));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Key));
        var signature = B64Url(hmac.ComputeHash(Encoding.ASCII.GetBytes(header + "." + payload)));
        return header + "." + payload + "." + signature;
    }

    [Fact]
    public async Task MiscasedClaimNames_AuthenticateButAreNotFound()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = Key,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
        });
        builder.Services.AddStatsTidJwtAuth(builder.Configuration, builder.Environment);

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        var probeHits = 0;
        app.MapGet("/probe", (HttpContext ctx) =>
        {
            Interlocked.Increment(ref probeHits);
            return Results.Json(ctx.GetActorContext());
        }).RequireAuthorization();
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MiscasedToken());
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, probeHits);
            var actor = await response.Content.ReadFromJsonAsync<ActorContext>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(actor);
            Assert.Null(actor!.ActorRole);
            Assert.Null(actor.ActorId);
            Assert.Equal("STY01", actor.OrgId);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
