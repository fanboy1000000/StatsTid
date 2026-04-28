using System;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure.Security;

public static class JwtValidationSetup
{
    // Dev-only fallback signing key. MUST NOT be used outside Development.
    // The fallback is intentionally weak and well-known — any non-Development
    // environment without an explicit Jwt:SigningKey must fail fast at startup.
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";

    public static IServiceCollection AddStatsTidJwtAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Prevent .NET from remapping JWT claim names (e.g. "role" → ClaimTypes.Role)
        // so our custom claims (StatsTidClaims.Role, etc.) are preserved as-is.
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

        var configuredSigningKey = configuration["Jwt:SigningKey"];
        // IHostEnvironment.IsDevelopment() honors both ASPNETCORE_ENVIRONMENT and
        // DOTNET_ENVIRONMENT (TASK-1905). Reading raw env vars would miss the latter.
        var isDevelopment = environment.IsDevelopment();

        string signingKey;
        if (!string.IsNullOrWhiteSpace(configuredSigningKey))
        {
            signingKey = configuredSigningKey;
        }
        else if (isDevelopment)
        {
            // Dev convenience: allow the well-known fallback so local runs don't need secrets.
            signingKey = DevFallbackSigningKey;
        }
        else
        {
            // Fail fast — refusing to start is safer than silently accepting tokens
            // signed with a well-known dev key in a non-Development environment.
            throw new InvalidOperationException(
                "Jwt:SigningKey configuration is missing. A signing key must be explicitly configured " +
                "outside the Development environment (current EnvironmentName="
                + environment.EnvironmentName + "). The dev fallback key is only permitted when " +
                "the host environment is Development (set ASPNETCORE_ENVIRONMENT or DOTNET_ENVIRONMENT).");
        }

        var settings = new JwtSettings
        {
            Issuer = configuration["Jwt:Issuer"] ?? "statstid",
            Audience = configuration["Jwt:Audience"] ?? "statstid",
            SigningKey = signingKey,
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
