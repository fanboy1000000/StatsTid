using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Unit.Security;

public class JwtTokenServiceTests
{
    private static JwtSettings TestSettings => new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = "TestKey_MustBeAtLeast32BytesLong_ForHmacSha256!",
        ExpirationMinutes = 60
    };

    private readonly JwtTokenService _sut = new(TestSettings);

    [Fact]
    public void GenerateToken_ContainsCorrectClaims()
    {
        var token = _sut.GenerateToken("EMP001", "Test User", "Employee", "AC");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("EMP001", jwt.Subject);
        Assert.Equal("Test User", jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Name).Value);
        Assert.Equal("Employee", jwt.Claims.First(c => c.Type == StatsTidClaims.Role).Value);
        Assert.Equal("EMP001", jwt.Claims.First(c => c.Type == StatsTidClaims.EmployeeId).Value);
        Assert.Equal("AC", jwt.Claims.First(c => c.Type == StatsTidClaims.AgreementCode).Value);
    }

    [Fact]
    public void GenerateToken_HasCorrectExpiration()
    {
        var before = DateTime.UtcNow;
        var token = _sut.GenerateToken("EMP001", "Test User", "Employee", "HK");
        var after = DateTime.UtcNow;

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var expectedEarliestExpiry = before.AddMinutes(TestSettings.ExpirationMinutes);
        var expectedLatestExpiry = after.AddMinutes(TestSettings.ExpirationMinutes);

        Assert.InRange(jwt.ValidTo, expectedEarliestExpiry.AddSeconds(-1), expectedLatestExpiry.AddSeconds(1));
    }

    [Fact]
    public void GenerateToken_ValidatesWithCorrectKey()
    {
        var token = _sut.GenerateToken("EMP001", "Test User", "Employee", "PROSA");

        var handler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TestSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = TestSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSettings.SigningKey))
        };

        var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);

        Assert.NotNull(principal);
        Assert.NotNull(validatedToken);
    }

    [Fact]
    public void GenerateToken_InvalidKey_FailsValidation()
    {
        var token = _sut.GenerateToken("EMP001", "Test User", "Employee", "AC");

        var handler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TestSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = TestSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("WrongKey_MustAlsoBeAtLeast32BytesLong_ForTest!!"))
        };

        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            handler.ValidateToken(token, validationParameters, out _));
    }

    [Fact]
    public void GenerateToken_HasCorrectIssuerAndAudience()
    {
        var token = _sut.GenerateToken("EMP001", "Test User", "Employee", "HK");

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal("test-issuer", jwt.Issuer);
        Assert.Contains("test-audience", jwt.Audiences);
    }
}
