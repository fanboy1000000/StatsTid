using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure.Security;

public static class AuthorizationPolicies
{
    public static IServiceCollection AddStatsTidPolicies(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

        services.AddAuthorizationBuilder()
            // Global admin only — no org scope needed
            .AddPolicy("GlobalAdminOnly", policy =>
                policy.Requirements.Add(new ScopeRequirement(
                    requireOrgScope: false,
                    StatsTidRoles.GlobalAdmin)))
            // Local admin or global admin
            .AddPolicy("LocalAdminOrAbove", policy =>
                policy.Requirements.Add(new ScopeRequirement(
                    requireOrgScope: true,
                    StatsTidRoles.GlobalAdmin, StatsTidRoles.LocalAdmin)))
            // HR, local admin, or global admin
            .AddPolicy("HROrAbove", policy =>
                policy.Requirements.Add(new ScopeRequirement(
                    requireOrgScope: true,
                    StatsTidRoles.GlobalAdmin, StatsTidRoles.LocalAdmin, StatsTidRoles.LocalHR)))
            // Leader and above
            .AddPolicy("LeaderOrAbove", policy =>
                policy.Requirements.Add(new ScopeRequirement(
                    requireOrgScope: true,
                    StatsTidRoles.GlobalAdmin, StatsTidRoles.LocalAdmin, StatsTidRoles.LocalHR, StatsTidRoles.LocalLeader)))
            // Employee and above (all authenticated roles)
            .AddPolicy("EmployeeOrAbove", policy =>
                policy.Requirements.Add(new ScopeRequirement(
                    requireOrgScope: false,
                    StatsTidRoles.GlobalAdmin, StatsTidRoles.LocalAdmin, StatsTidRoles.LocalHR, StatsTidRoles.LocalLeader, StatsTidRoles.Employee)))
            // Any authenticated user
            .AddPolicy("Authenticated", policy =>
                policy.RequireAuthenticatedUser());

        return services;
    }
}
