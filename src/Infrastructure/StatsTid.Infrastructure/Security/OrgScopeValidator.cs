using Microsoft.Extensions.Logging;
using StatsTid.Auth;
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
    /// S70 / TASK-7003 (SPRINT-70 R9b, ADR-033 slice 3a) — terminated-INCLUSIVE employee-access
    /// validation for the R9c allowlist surfaces ONLY (the employment-end-date set/clear endpoint,
    /// the settlement manual-resolve endpoint, the reconcile-payout endpoint, and the year-overview
    /// read). Resolves the target via
    /// <see cref="UserRepository.GetByIdIncludingTerminatedAsync(string, CancellationToken)"/> so a
    /// deactivated leaver IS addressable (the S68 B2 fix), then applies the EXISTING
    /// <see cref="RoleScope.CoversOrg"/>/<c>PrimaryOrgId</c> subtree check unchanged. The shared
    /// <see cref="ValidateEmployeeAccessAsync"/> (and its active-only target resolution) is
    /// untouched — every non-allowlisted caller still cannot see a terminated employee.
    ///
    /// <para>
    /// <b>NO own-data branch exists on this surface (decision, R9b).</b> The Employee own-data
    /// short-circuit is deliberately ABSENT: an Employee-only actor is denied outright, never
    /// granted by ownership. Rationale: (1) without the role gate first, an Employee's ORG_ONLY
    /// scope could satisfy the subtree loop for a same-org COLLEAGUE — the existing validator
    /// prevents that exact privilege escalation via its own-data-only branch, and this validator
    /// must be at least as strict; (2) the R9e matrix pins terminated-SELF behavior on the
    /// EXISTING validator (own-data branch passes, the endpoint then 404s on the filtered
    /// <c>GetByIdAsync</c>), not on this one. On the allowlisted write surfaces the
    /// <c>HROrAbove</c> endpoint policy already excludes Employee tokens; this branch is the
    /// defense-in-depth for any future read surface wired here.
    /// </para>
    ///
    /// <para>
    /// <b>HROrAbove gate (R9b), enforced at the moment it matters:</b> when the resolved target is
    /// TERMINATED (<c>is_active = FALSE</c>), the actor's role must be LocalHR or above
    /// (<see cref="StatsTidRoles.IsAtLeast"/> vs <see cref="StatsTidRoles.LocalHR"/> — the same
    /// GlobalAdmin/LocalAdmin/LocalHR set the <c>HROrAbove</c> endpoint policy admits). For an
    /// ACTIVE target this validator is deliberately behavior-identical to
    /// <see cref="ValidateEmployeeAccessAsync"/>'s non-Employee path (scope subtree only, no HR
    /// floor): the year-overview surface is <c>EmployeeOrAbove</c>-policy and an in-scope
    /// LocalLeader's access to an ACTIVE subordinate is pre-existing, pinned behavior
    /// (<c>YearOverviewTests.Auth_LeaderInScope_Returns200</c>) that R9c's wiring must not
    /// regress — the HR floor R9b adds is the gate on TERMINATED-employee data, which is the only
    /// data this validator unlocks beyond the existing one. On the resolve/reconcile-payout
    /// surfaces the endpoint policy enforces HROrAbove for ALL targets regardless.
    /// </para>
    ///
    /// <para>
    /// <b>R9f1 per-scope floor (Step-5a hardening, Codex B1, 2026-06-10):</b> the R9b
    /// primary-role gate alone is spoofable by a MIXED-role JWT — an actor holding LocalHR in a
    /// DISJOINT org plus a LocalLeader scope covering the target carries primary role LocalHR,
    /// passes the gate, and would then be admitted by the Leader scope. So for a TERMINATED
    /// target the scope that ADMITS must ITSELF be LocalHR or above
    /// (<see cref="StatsTidRoles.IsAtLeast"/> on <c>scope.Role</c>), in BOTH the GLOBAL branch
    /// and the <see cref="RoleScope.CoversOrg"/> branch. A below-floor scope simply never admits
    /// a terminated target (the loop continues; if no scope qualifies, the final subtree Deny
    /// fires). The early primary-role gate is KEPT as defense-in-depth/fail-fast. For an ACTIVE
    /// target the floor is inert — behavior remains identical to
    /// <see cref="ValidateEmployeeAccessAsync"/>'s non-Employee path (any covering scope, no
    /// role floor).
    /// </para>
    /// </summary>
    public async Task<(bool Allowed, string? Reason)> ValidateEmployeeAccessIncludingTerminatedAsync(
        ActorContext actor, string targetEmployeeId, CancellationToken ct = default)
    {
        // No own-data branch (see XML doc): Employee-only actors are denied outright.
        if (IsEmployeeOnly(actor))
            return Deny(actor, targetEmployeeId, "Employee role cannot use the terminated-inclusive access surface");

        if (actor.Scopes is null || actor.Scopes.Length == 0)
            return Deny(actor, targetEmployeeId, "No scopes assigned");

        // Terminated-INCLUSIVE target resolution — the single difference from the shared
        // validator's read (which filters is_active = TRUE and turns a leaver into
        // "Target employee not found").
        var targetUser = await _userRepository.GetByIdIncludingTerminatedAsync(targetEmployeeId, ct);
        if (targetUser is null)
            return Deny(actor, targetEmployeeId, "Target employee not found");

        // R9b HROrAbove gate on terminated-employee data (fail-closed BEFORE the scope loop, so
        // a non-HR actor holding a GLOBAL scope is still refused a terminated target here).
        // Kept as defense-in-depth/fail-fast — the per-scope R9f1 floor inside the loop is the
        // gate that actually binds privilege to the ADMITTING scope.
        if (!targetUser.IsActive &&
            (actor.ActorRole is null || !StatsTidRoles.IsAtLeast(actor.ActorRole, StatsTidRoles.LocalHR)))
        {
            return Deny(actor, targetEmployeeId, "Terminated-employee access requires LocalHR or above");
        }

        var targetOrg = await _organizationRepository.GetByIdAsync(targetUser.PrimaryOrgId, ct);
        if (targetOrg is null)
            return Deny(actor, targetEmployeeId, "Target organization not found");

        // CoversOrg/PrimaryOrgId subtree check — identical to ValidateEmployeeAccessAsync for an
        // ACTIVE target; for a TERMINATED target each scope must ALSO clear the R9f1 per-scope
        // HROrAbove floor below before it can admit.
        var orgPathCache = new Dictionary<string, string?>(StringComparer.Ordinal);
        var terminatedTarget = !targetUser.IsActive;

        foreach (var scope in actor.Scopes)
        {
            // R9f1 per-scope floor (Step-5a hardening, Codex B1): for a TERMINATED target the
            // scope that ADMITS must itself be LocalHR or above — the R9b primary-role gate
            // above is necessary but NOT sufficient (a mixed-role JWT with LocalHR in a disjoint
            // org + a LocalLeader scope covering the target carries primary role LocalHR yet
            // would be admitted here by the Leader scope). Applies to BOTH the GLOBAL branch and
            // the CoversOrg branch; a below-floor scope simply never admits a terminated target
            // (continue — the final Deny fires if no scope qualifies). Inert for ACTIVE targets:
            // any covering scope admits, no role floor (pinned leader behavior preserved).
            if (terminatedTarget && !StatsTidRoles.IsAtLeast(scope.Role, StatsTidRoles.LocalHR))
                continue;

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
    /// S44 / TASK-4405 — ADR-026 D5 audit-visibility surface read-path scope resolution.
    /// Returns the set of org_ids the actor can access for scope-by-target queries
    /// against <c>audit_projection</c>.
    ///
    /// <para>Per-role contract:</para>
    /// <list type="bullet">
    ///   <item><description><b>GlobalAdmin</b> (any scope with <c>ScopeType == "GLOBAL"</c>) →
    ///   returns <c>null</c> sentinel meaning "no filter" — caller treats as unrestricted.
    ///   Used by <c>AuditProjectionRepository.QueryByOrgScopeAsync</c> to include
    ///   <c>visibility_scope = 'GLOBAL_ADMIN_ONLY'</c> rows.</description></item>
    ///   <item><description><b>LocalAdmin / Manager</b> (scopes with non-null
    ///   <c>OrgId</c>) → returns the union of materialized-path descendants of each
    ///   scope org. Allows scope-by-target queries to filter
    ///   <c>target_org_id = ANY(@accessibleOrgIds)</c>.</description></item>
    ///   <item><description><b>Employee / no scopes</b> → returns empty list. Caller
    ///   treats as 403 at endpoint layer.</description></item>
    /// </list>
    ///
    /// <para>Signature takes full <see cref="ActorContext"/> per S44 Step 4 cycle 1
    /// Reviewer W3 absorption (matches the sibling <see cref="ValidateEmployeeAccessAsync"/>
    /// + <see cref="ValidateOrgAccessAsync"/> shape; both read role + scopes).</para>
    /// </summary>
    public async Task<IReadOnlyList<string>?> GetAccessibleOrgsAsync(
        ActorContext actor, CancellationToken ct = default)
    {
        if (actor.Scopes is null || actor.Scopes.Length == 0)
            return Array.Empty<string>();

        // GlobalAdmin short-circuit — any GLOBAL scope means "see everything".
        if (actor.Scopes.Any(s => string.Equals(s.ScopeType, "GLOBAL", StringComparison.Ordinal)))
            return null;

        // Non-global: collect materialized-path descendants of each scope org.
        var accessibleOrgIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in actor.Scopes)
        {
            if (scope.OrgId is null)
                continue;

            // Include the scope org itself + its descendants (GetDescendantsAsync
            // returns the subtree rooted at orgId per materialized-path predicate).
            var descendants = await _organizationRepository.GetDescendantsAsync(scope.OrgId, ct);
            foreach (var org in descendants)
                accessibleOrgIds.Add(org.OrgId);
        }

        return accessibleOrgIds.ToArray();
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
