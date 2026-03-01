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
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = settings.Issuer,
                ValidateAudience = true,
                ValidAudience = settings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SigningKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        });

        services.AddAuthorization();

        return services;
    }
}
