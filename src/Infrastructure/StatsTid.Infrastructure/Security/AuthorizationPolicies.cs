using Microsoft.Extensions.DependencyInjection;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure.Security;

public static class AuthorizationPolicies
{
    public static IServiceCollection AddStatsTidPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy("AdminOnly", policy =>
                policy.RequireClaim(StatsTidClaims.Role, StatsTidRoles.Admin))
            .AddPolicy("ManagerOrAbove", policy =>
                policy.RequireClaim(StatsTidClaims.Role, StatsTidRoles.Admin, StatsTidRoles.Manager))
            .AddPolicy("EmployeeOrAbove", policy =>
                policy.RequireClaim(StatsTidClaims.Role, StatsTidRoles.Admin, StatsTidRoles.Manager, StatsTidRoles.Employee))
            .AddPolicy("Authenticated", policy =>
                policy.RequireAuthenticatedUser());

        return services;
    }
}
