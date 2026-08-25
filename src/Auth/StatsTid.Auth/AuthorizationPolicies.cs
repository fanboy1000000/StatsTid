using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.DependencyInjection;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Auth;

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

        // QUAL-009 / SEC-038 — the policy-denial trace. Replace the framework's default
        // IAuthorizationMiddlewareResultHandler with a decorator that OBSERVES every final
        // authorization decision and, on a denial, emits a structured/redacted/correlation-linked
        // log (and annotates the request for the before-authz audit-row middleware). It delegates
        // ALL actual result handling to the built-in handler, so the allow/deny outcome is
        // unchanged. Registered HERE — after AddAuthorizationBuilder (which registers the default
        // via TryAddSingleton) — so this explicit registration wins the last-one-wins resolution.
        // Wiring it into AddStatsTidPolicies (rather than each Program.cs) means EVERY host that
        // adds the StatsTid policies gets the trace by construction; no host can be missed, and a
        // future host inherits it for free.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, DenialLoggingAuthorizationResultHandler>();

        return services;
    }
}
