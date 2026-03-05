using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure.Security;

public static class JwtValidationSetup
{
    public static IServiceCollection AddStatsTidJwtAuth(this IServiceCollection services, IConfiguration configuration)
    {
        // Prevent .NET from remapping JWT claim names (e.g. "role" → ClaimTypes.Role)
        // so our custom claims (StatsTidClaims.Role, etc.) are preserved as-is.
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

        var settings = new JwtSettings
        {
            Issuer = configuration["Jwt:Issuer"] ?? "statstid",
            Audience = configuration["Jwt:Audience"] ?? "statstid",
            SigningKey = configuration["Jwt:SigningKey"] ?? "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!",
            ExpirationMinutes = int.TryParse(configuration["Jwt:ExpirationMinutes"], out var mins) ? mins : 480
        };

        services.AddSingleton(settings);

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = settings.Issuer,
                ValidateAudience = true,
                ValidAudience = settings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                NameClaimType = "sub",
                RoleClaimType = "role"
            };
        });

        services.AddAuthorization();

        return services;
    }
}
