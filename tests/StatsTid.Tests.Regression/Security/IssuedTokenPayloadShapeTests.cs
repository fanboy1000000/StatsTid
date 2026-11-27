using System.Text;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Regression.Security;

/// <summary>
/// The payload of a token minted by the REAL <see cref="JwtTokenService"/>: its exact set of keys, the JSON
/// kind of each value, and the stable values. The browser decodes this payload itself
/// (<c>frontend/src/lib/jwt.ts</c>), so the shape is a contract that the server-side login pins do not cover.
///
/// <para>Kind: <b>R</b>, a regression pin (an existing shape kept). Non-vacuity is the named mutation M-PS:
/// issuing <c>scopes</c> with <c>ClaimValueTypes.String</c> in <c>JwtTokenService.cs</c> turns the claim into a
/// JSON string, and the <c>scopes</c> kind assert goes red. The volatile values (<c>iat</c>, <c>exp</c>) are
/// asserted as numbers only.</para>
///
/// <para>Non-Docker: no host, only the token service.</para>
/// </summary>
public sealed class IssuedTokenPayloadShapeTests
{
    private static JsonElement DecodePayload(string token)
    {
        var part = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(part))).RootElement;
    }

    [Fact]
    public void IssuedTokenPayloadShape()
    {
        var scopes = new[]
        {
            new RoleScope("LOCAL_HR", "STY01", "ORG_ONLY"),
            new RoleScope("LOCAL_LEADER", "STY02", "ORG_ONLY"),
        };
        var service = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid", Audience = "statstid",
            SigningKey = "IssuedPayloadShape_SigningKey_AtLeast32Bytes!", ExpirationMinutes = 60,
        });

        var payload = DecodePayload(service.GenerateToken("emp001", "Test Person", "LOCAL_HR", "AC", "STY01", scopes));

        var keys = payload.EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { "agreement_code", "aud", "employee_id", "exp", "iat", "iss", "name", "org_id", "role", "scopes", "sub" },
            keys);

        foreach (var name in new[] { "sub", "name", "role", "employee_id", "agreement_code", "org_id", "iss", "aud" })
            Assert.Equal(JsonValueKind.String, payload.GetProperty(name).ValueKind);
        Assert.Equal(JsonValueKind.Number, payload.GetProperty("iat").ValueKind);
        Assert.Equal(JsonValueKind.Number, payload.GetProperty("exp").ValueKind);

        Assert.Equal("emp001", payload.GetProperty("sub").GetString());
        Assert.Equal("Test Person", payload.GetProperty("name").GetString());
        Assert.Equal("LOCAL_HR", payload.GetProperty("role").GetString());
        Assert.Equal("emp001", payload.GetProperty("employee_id").GetString());
        Assert.Equal("AC", payload.GetProperty("agreement_code").GetString());
        Assert.Equal("STY01", payload.GetProperty("org_id").GetString());
        Assert.Equal("statstid", payload.GetProperty("iss").GetString());
        Assert.Equal("statstid", payload.GetProperty("aud").GetString());

        var scopesClaim = payload.GetProperty("scopes");
        Assert.Equal(JsonValueKind.Array, scopesClaim.ValueKind);
        Assert.Equal(2, scopesClaim.GetArrayLength());
        foreach (var element in scopesClaim.EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Object, element.ValueKind);
            Assert.Equal(new[] { "OrgId", "Role", "ScopeType" },
                element.EnumerateObject().Select(p => p.Name).OrderBy(k => k, StringComparer.Ordinal).ToArray());
        }
        Assert.Equal("LOCAL_HR", scopesClaim[0].GetProperty("Role").GetString());
        Assert.Equal("STY01", scopesClaim[0].GetProperty("OrgId").GetString());
        Assert.Equal("ORG_ONLY", scopesClaim[0].GetProperty("ScopeType").GetString());
        Assert.Equal("LOCAL_LEADER", scopesClaim[1].GetProperty("Role").GetString());
    }
}
