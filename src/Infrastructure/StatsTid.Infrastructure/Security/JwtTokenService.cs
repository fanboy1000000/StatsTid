using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure.Security;

public sealed class JwtTokenService
{
    private readonly JwtSettings _settings;

    public JwtTokenService(JwtSettings settings)
    {
        _settings = settings;
    }

    public string GenerateToken(string employeeId, string name, string role, string agreementCode,
        string? orgId = null, IReadOnlyList<RoleScope>? scopes = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, employeeId),
            new Claim(JwtRegisteredClaimNames.Name, name),
            new Claim(StatsTidClaims.Role, role),
            new Claim(StatsTidClaims.EmployeeId, employeeId),
            new Claim(StatsTidClaims.AgreementCode, agreementCode),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };

        if (orgId is not null)
        {
            claims.Add(new Claim(StatsTidClaims.OrgId, orgId));
        }

        if (scopes is { Count: > 0 })
        {
            var scopeArray = scopes.Select(s => new { role = s.Role, org_id = s.OrgId, scope_type = s.ScopeType });
            var scopesJson = JsonSerializer.Serialize(scopeArray);
            claims.Add(new Claim(StatsTidClaims.Scopes, scopesJson, JsonClaimValueTypes.JsonArray));
        }

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_settings.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
