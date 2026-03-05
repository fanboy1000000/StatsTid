using Microsoft.Extensions.Logging;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Infrastructure.Security;

/// <summary>
/// Service-layer org-scope validator. Called explicitly from API endpoints
/// to enforce organization-based access control using the actor's RoleScope claims.
/// </summary>
public sealed class OrgScopeValidator
{
    private readonly OrganizationRepository _organizationRepository;
    private readonly UserRepository _userRepository;
    private readonly ILogger<OrgScopeValidator> _logger;

    public OrgScopeValidator(
        OrganizationRepository organizationRepository,
        UserRepository userRepository,
        ILogger<OrgScopeValidator> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    /// <summary>
    /// Validates whether the actor has access to a specific employee's data.
    /// Employee role can only access own data. Higher roles require org-scope coverage.
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> ValidateEmployeeAccessAsync(
        ActorContext actor, string targetEmployeeId, CancellationToken ct = default)
    {
        // Ownership check: Employee accessing own data
        if (IsEmployeeOnly(actor))
        {
            if (string.Equals(actor.ActorId, targetEmployeeId, StringComparison.Ordinal))
                return (true, null);

            return Deny(actor, targetEmployeeId, "Employee can only access own data");
        }

        // Higher roles: resolve target employee's org and check scope coverage
        if (actor.Scopes is null || actor.Scopes.Length == 0)
            return Deny(actor, targetEmployeeId, "No scopes assigned");

        var targetUser = await _userRepository.GetByIdAsync(targetEmployeeId, ct);
        if (targetUser is null)
            return Deny(actor, targetEmployeeId, "Target employee not found");

        var targetOrg = await _organizationRepository.GetByIdAsync(targetUser.PrimaryOrgId, ct);
        if (targetOrg is null)
            return Deny(actor, targetEmployeeId, "Target organization not found");

        // Cache for scope org path lookups within this request
        var orgPathCache = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var scope in actor.Scopes)
        {
            if (scope.ScopeType == "GLOBAL")
                return (true, null);

            if (scope.OrgId is null)
                continue;

            if (!orgPathCache.TryGetValue(scope.OrgId, out var scopeOrgPath))
            {
                var scopeOrg = await _organizationRepository.GetByIdAsync(scope.OrgId, ct);
                scopeOrgPath = scopeOrg?.MaterializedPath;
                orgPathCache[scope.OrgId] = scopeOrgPath;
            }

            if (scope.CoversOrg(targetOrg.MaterializedPath, scopeOrgPath))
                return (true, null);
        }

        return Deny(actor, targetEmployeeId, "Actor scope does not cover target organization");
    }

    /// <summary>
    /// Validates whether the actor has access to a specific organization.
    /// Checks actor scopes against the target organization's materialized path.
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> ValidateOrgAccessAsync(
        ActorContext actor, string targetOrgId, CancellationToken ct = default)
    {
        var targetOrg = await _organizationRepository.GetByIdAsync(targetOrgId, ct);
        if (targetOrg is null)
            return Deny(actor, targetOrgId, "Organization not found");

        if (actor.Scopes is null || actor.Scopes.Length == 0)
            return Deny(actor, targetOrgId, "No scopes assigned");

        // Cache for scope org path lookups within this request
        var orgPathCache = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var scope in actor.Scopes)
        {
            if (scope.ScopeType == "GLOBAL")
                return (true, null);

            if (scope.OrgId is null)
                continue;

            if (!orgPathCache.TryGetValue(scope.OrgId, out var scopeOrgPath))
            {
                var scopeOrg = await _organizationRepository.GetByIdAsync(scope.OrgId, ct);
                scopeOrgPath = scopeOrg?.MaterializedPath;
                orgPathCache[scope.OrgId] = scopeOrgPath;
            }

            if (scope.CoversOrg(targetOrg.MaterializedPath, scopeOrgPath))
                return (true, null);
        }

        return Deny(actor, targetOrgId, "Actor scope does not cover target organization");
    }

    /// <summary>
    /// Returns true if the actor only holds the Employee role (no higher-privilege scopes).
    /// </summary>
    private static bool IsEmployeeOnly(ActorContext actor)
    {
        // Check explicit role first
        if (string.Equals(actor.ActorRole, StatsTidRoles.Employee, StringComparison.Ordinal))
            return true;

        // If scopes are present, check if all scopes are Employee-level
        if (actor.Scopes is { Length: > 0 })
            return actor.Scopes.All(s =>
                string.Equals(s.Role, StatsTidRoles.Employee, StringComparison.Ordinal));

        // No role, no scopes — treat as Employee (least privilege)
        return actor.ActorRole is null;
    }

    private (bool Allowed, string? Reason) Deny(ActorContext actor, string target, string reason)
    {
        _logger.LogWarning(
            "Access denied: Actor={ActorId} Role={ActorRole} Target={Target} Reason={Reason} CorrelationId={CorrelationId}",
            actor.ActorId, actor.ActorRole, target, reason, actor.CorrelationId);
        return (false, reason);
    }
}
