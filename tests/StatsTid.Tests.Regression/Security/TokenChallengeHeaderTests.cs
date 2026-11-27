using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StatsTid.Auth;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// The whole <c>WWW-Authenticate</c> header of every kind of refused login token, exact, as written by the
/// REAL middleware (<see cref="JwtValidationSetup.AddStatsTidJwtAuth"/>) with its default
/// <c>IncludeErrorDetails</c>.
///
/// <para>The status of a refusal is pinned by the login pins; this file pins the explanation text beside it
/// (<c>error_description</c>). Nothing in StatsTid reads that text, so a change to it is not a change of any
/// access decision. It is pinned so that no library move can change it silently. <b>A red here after a later
/// library move means "report and rule", not "the move is wrong"</b>: read the actual header, decide whether
/// the new text is acceptable, and re-capture it deliberately (plan "final + 2", ruling R-1). The wrong-audience
/// facts carry the one change already accepted: the audience in a refusal is no longer repeated back
/// (<c>The audience '(null)' is invalid</c>); the issuer still is. Expiry and not-before timestamps are
/// computed from the <c>exp</c> and <c>nbf</c> each token sets, formatted with the invariant culture the way
/// JwtBearer formats the <c>DateTime</c> its exception carries.</para>
///
/// <para>Kind: <b>R</b>, a regression pin of the post-upgrade text, captured as literals on the library the
/// repository now uses. Non-vacuity is the named mutation M-CH: <c>IncludeErrorDetails = false</c> in
/// <c>JwtValidationSetup.cs</c> removes every <c>error_description</c>, and every fact that asserts one goes
/// red. Tokens are hand-built, so identical bytes reach every library version.</para>
///
/// <para>Non-Docker: an in-memory test server.</para>
/// </summary>
public sealed class TokenChallengeHeaderTests
{
    private const string Issuer = "statstid";
    private const string Audience = "statstid";
    private const string Key = "TokenChallenge_SigningKey_AtLeast32Bytes_Long!";
    private const string WrongKey = "A_Completely_Different_Key_Of_Ample_Length_!!";

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static long InAnHour => DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();

    private static string Stamp(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime.ToString(CultureInfo.InvariantCulture);

    /// <summary>An HS256 token built without the signing library; the parameters set the claims under test.</summary>
    private static string Token(
        string key = Key, string iss = Issuer, string aud = Audience,
        long? exp = null, bool includeExp = true, long? nbf = null, string? kid = null, string alg = "HS256")
    {
        var header = B64Url(Encoding.UTF8.GetBytes(
            "{\"alg\":\"" + alg + "\",\"typ\":\"JWT\"" + (kid is null ? "" : ",\"kid\":\"" + kid + "\"") + "}"));
        var json = "{\"sub\":\"emp001\",\"role\":\"LOCAL_HR\",\"iss\":\"" + iss + "\",\"aud\":\"" + aud + "\""
            + (includeExp ? ",\"exp\":" + (exp ?? InAnHour) : "")
            + (nbf is { } n ? ",\"nbf\":" + n : "")
            + "}";
        var payload = B64Url(Encoding.UTF8.GetBytes(json));
        if (alg == "none") return header + "." + payload + ".";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return header + "." + payload + "." + B64Url(hmac.ComputeHash(Encoding.ASCII.GetBytes(header + "." + payload)));
    }

    private static async Task<(HttpStatusCode Status, string Header)> ChallengeAsync(
        string? token, string hostKey = Key)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = hostKey,
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience,
        });
        builder.Services.AddStatsTidJwtAuth(builder.Configuration, builder.Environment);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/probe", () => Results.Ok()).RequireAuthorization();
        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            var request = new HttpRequestMessage(HttpMethod.Get, "/probe");
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await client.SendAsync(request);
            var header = string.Join(" | ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));
            return (response.StatusCode, header);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private const string InvalidToken = "Bearer error=\"invalid_token\"";

    private static string Described(string description) =>
        InvalidToken + ", error_description=\"" + description + "\"";

    private static async Task AssertChallenge(string? token, string expected, string hostKey = Key)
    {
        var (status, header) = await ChallengeAsync(token, hostKey);
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(expected, header);
    }

    [Fact]
    public Task NoToken() => AssertChallenge(null, "Bearer");

    [Fact]
    public Task MalformedToken() => AssertChallenge("not-a-jwt", InvalidToken);

    [Fact]
    public Task L3_WrongIssuer() => AssertChallenge(Token(iss: "someone-else"), Described("The issuer 'someone-else' is invalid"));

    [Fact]
    public Task L4_WrongAudience() => AssertChallenge(Token(aud: "someone-else"), Described("The audience '(null)' is invalid"));

    [Fact]
    public Task L5_ExpiredFiveMinutesAgo()
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        return AssertChallenge(Token(exp: exp, nbf: exp - 3600), Described($"The token expired at '{Stamp(exp)}'"));
    }

    [Fact]
    public Task L5b_ExpiredHundredSecondsAgo()
    {
        var exp = DateTimeOffset.UtcNow.AddSeconds(-100).ToUnixTimeSeconds();
        return AssertChallenge(Token(exp: exp, nbf: exp - 3600), Described($"The token expired at '{Stamp(exp)}'"));
    }

    [Fact]
    public Task L7_DifferentSigningKey() => AssertChallenge(Token(key: WrongKey), Described("The signature key was not found"));

    [Fact]
    public Task L8_AlgNone() => AssertChallenge(Token(alg: "none"), Described("The signature is invalid"));

    [Fact]
    public Task L9b_ThirtyOneByteKey()
    {
        var key31 = new string('k', 31);
        return AssertChallenge(Token(key: key31), Described("The signature key was not found"), hostKey: key31);
    }

    [Fact]
    public Task L12_NoExp() => AssertChallenge(Token(includeExp: false), Described("The token has no expiration"));

    [Fact]
    public Task L13_FutureNbf()
    {
        var nbf = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        return AssertChallenge(Token(nbf: nbf), Described($"The token is not valid before '{Stamp(nbf)}'"));
    }

    [Fact]
    public Task Kid_WrongKey_WrongAudience() => AssertChallenge(Token(key: WrongKey, kid: "k1", aud: "someone-else"), Described("The audience '(null)' is invalid"));

    [Fact]
    public Task Kid_WrongKey_WrongIssuer() => AssertChallenge(Token(key: WrongKey, kid: "k1", iss: "someone-else"), Described("The issuer 'someone-else' is invalid"));

    [Fact]
    public Task Kid_WrongKey_Expired()
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        return AssertChallenge(Token(key: WrongKey, kid: "k1", exp: exp, nbf: exp - 3600), Described($"The token expired at '{Stamp(exp)}'"));
    }
}
