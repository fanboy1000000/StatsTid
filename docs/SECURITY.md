# StatsTid Security Reference

> JWT patterns, RBAC model, scope validation, and known gotchas for agent context.

## Authentication

- **Algorithm**: JWT HMAC-SHA256, shared secret across all 5 API services via Docker environment variables
- **Token configuration**: `MapInboundClaims = false`, `NameClaimType = "sub"`, `RoleClaimType = "role"`
- **Dual-mode login**: `Auth:UseDatabase` config flag toggles between hardcoded dev credentials and DB-backed BCrypt password verification
- **Reference**: [ADR-007](knowledge-base/decisions/ADR-007-jwt-auth-rbac-correlation-ids.md)

## Custom JWT Claims

All claim constants are defined in `src/SharedKernel/StatsTid.SharedKernel/Security/StatsTidClaims.cs`.

| Constant | Claim Name | Description |
|----------|------------|-------------|
| `EmployeeId` | `employee_id` | Unique employee identifier |
| `Role` | `role` | Primary role string (e.g. `GlobalAdmin`, `Employee`) |
| `AgreementCode` | `agreement_code` | Collective agreement code (e.g. `AC`, `HK`, `PROSA`) |
| `OrgId` | `org_id` | Primary organization unit ID |
| `Scopes` | `scopes` | JSON array of `RoleScope` objects (see below) |

## RBAC Model

### 5 Roles

Defined in `src/SharedKernel/StatsTid.SharedKernel/Security/StatsTidRoles.cs`.

| Role | Hierarchy Level | Description |
|------|:-:|-------------|
| `GlobalAdmin` | 1 | System-wide administration |
| `LocalAdmin` | 2 | Organization unit administration |
| `LocalHR` | 3 | HR functions within an organization unit |
| `LocalLeader` | 4 | Team/department leader |
| `Employee` | 5 | Standard employee |

**`GetHierarchyLevel(role)`** returns the numeric level (lower = higher privilege). Unknown roles return `int.MaxValue`.

**`IsAtLeast(actualRole, requiredRole)`** returns `true` when `GetHierarchyLevel(actualRole) <= GetHierarchyLevel(requiredRole)`. This is the canonical backend role comparison.

### Legacy Role Mappings

For backward compatibility with existing events and audit logs:

| Legacy Role | Maps to Level |
|-------------|:-:|
| `Admin` | 1 |
| `Manager` | 4 |
| `ReadOnly` | 6 |

These aliases remain in `StatsTidRoles` and will be removed once all consumers migrate.

### Frontend Role Check

Defined in `frontend/src/lib/roles.ts`:

- **`ROLE_LEVELS`**: `Record<string, number>` mapping role names to hierarchy levels (same values as backend)
- **`hasMinRole(userRole, minRole)`**: Returns `true` when `ROLE_LEVELS[userRole] <= ROLE_LEVELS[minRole]`. Returns `false` for `null` or unknown roles (defaults to 99 for unknown user roles, 0 for unknown min roles).
- **`RequireRole` guard component**: Wraps routes, renders `ForbiddenPage` if `hasMinRole` fails.

## Scope Validation

### RoleScope Structure

Defined in `src/SharedKernel/StatsTid.SharedKernel/Security/RoleScope.cs`:

```csharp
public sealed record RoleScope(string Role, string? OrgId, string ScopeType)
```

| Field | Values | Description |
|-------|--------|-------------|
| `Role` | Any `StatsTidRoles` constant | The role granted in this scope |
| `OrgId` | Organization ID or `null` | The org unit this scope applies to (`null` for GLOBAL) |
| `ScopeType` | `GLOBAL`, `ORG_ONLY`, `ORG_AND_DESCENDANTS` | Breadth of the scope |

Embedded in JWT as the `scopes` claim. Reference: [ADR-009](knowledge-base/decisions/ADR-009-scope-embedded-jwt.md).

### ActorContext Extraction

Defined in `src/Auth/StatsTid.Auth/ActorContext.cs` (moved from `src/Infrastructure/StatsTid.Infrastructure/Security/` in commit b4fc670; see ARCHITECTURE.md "Auth" section).

```csharp
public sealed record ActorContext(
    string? ActorId, string? ActorRole, Guid CorrelationId,
    string? OrgId = null, RoleScope[]? Scopes = null);
```

Extraction logic (`GetActorContext` extension on `HttpContext`):

1. **ActorId**: Reads from `sub`, then `ClaimTypes.NameIdentifier`, then `employee_id` (fallback chain).
2. **ActorRole**: Reads from `role` claim.
3. **OrgId**: Reads from `org_id` claim.
4. **CorrelationId**: Reads from `HttpContext.Items` (set by `CorrelationIdMiddleware`), falls back to `Guid.NewGuid()`.
5. **Scopes**: Uses `FindAll(StatsTidClaims.Scopes)` (NOT `FindFirst`) to collect all scope claims. Parses both JSON arrays `[{...}]` and individual JSON objects `{...}` because JWT bearer middleware splits `JsonArray` claims into separate claim entries.

### OrgScopeValidator

Defined in `src/Infrastructure/StatsTid.Infrastructure/Security/OrgScopeValidator.cs`.

Service-layer enforcement called explicitly from API endpoints. Validates that the actor's `RoleScope` entries grant access to the target employee's organization unit. Uses the materialized path hierarchy (`/MIN01/STY02/`) with `text_pattern_ops` index for descendant matching. Reference: [ADR-008](knowledge-base/decisions/ADR-008-materialized-path-org-hierarchy.md).

### The mixed-role scope-floor invariant (S76 / B1)

**CRITICAL invariant:** an ADMIN scope-admission check must floor *per scope* by role — admission may only be granted by a scope whose `Role` meets the endpoint's policy floor. The original `OrgScopeValidator` methods (`ValidateOrgAccessAsync`, `ValidateEmployeeAccessAsync`, `GetAccessibleOrgsAsync`, `ValidateEmployeeAccessIncludingTerminatedAsync`) and `HasGlobalScope` admitted via ANY covering scope **regardless of that scope's role**. A mixed-role actor (e.g. `LocalAdmin@STY-A` who also holds a non-admin scope in STY-B) therefore passed an admin gate for STY-B via the *non-admin* scope → a cross-styrelse data/write leak.

**The rule:** every admin (LocalAdminOrAbove / HROrAbove / GlobalAdmin) read or write must route through a role-FLOORED scope check, at the floor matching its `RequireAuthorization` policy (LocalAdminOrAbove → `LocalAdmin`; HROrAbove → `LocalHR`; GlobalAdmin gates require a GLOBAL scope at `GlobalAdmin`). The floored variants take a `roleFloor` and skip scopes below it (`!IsAtLeast(scope.Role, roleFloor) → continue`). The no-floor overloads remain for genuinely non-admin callers (Employee/Leader reads admit via any covering scope by design). **Exception, deliberately null-floored:** the leader year-overview reads (`BalanceEndpoints`) — a Leader-in-scope SHOULD see their active report's overview (ADR-027 R9c).

**Lesson:** a scoped/structural review trace cannot enumerate every scope-admission caller — only an external whole-codebase trace found the full set (config/project/global/audit/settlement/end-date writes across 3 review cycles). When changing a scope-admission surface, sweep EVERY caller, not just the diff ([[review-lens-complementarity]]).

### In-lock authorization serialization + the revocation-residual map (S78 / ADR-027 D18)

The approve/reject/reopen endpoints originally checked authorization (org-scope + the designated-edge resolver) on a **separate committed connection BEFORE** the write tx — a check-then-act TOCTOU: a revocation in the sub-second window before commit was not caught. S78 hardened this:

**The pattern (share-the-revoker-lock + in-tx re-evaluation).** True serialization of an authorization re-check against a concurrent revocation requires the action path to **hold the same lock the revoker holds**. The reporting-line write mutators serialize on the `reporting-tree-{treeRoot}` xact advisory; so the approve path acquires THAT advisory first, then re-evaluates the designated-edge authorization (+ re-classifies the REQUIRED-mode `confirmFallback` gate) STRICTLY under it. Because the action tx holds the advisory, a concurrent key-sharing revoke BLOCKS before its commit → the in-tx re-read (even on its own connection) observes the frozen committed state. A revoke committed *after* the pre-tx check but caught by the in-tx re-eval → 403.

**The drift-guard (the tree-key is derived from mutable membership).** The `reporting-tree` key derives from the employee's current `primary_org_id` (mutable). A shared **drift-guarded acquire** derives → acquires the advisory → re-derives under the lock; on drift (a transfer committed mid-acquire) it **rolls back + retries the whole request** on a fresh READ COMMITTED tx (≤3 → a pinned 409). A mid-tx release is impossible (`pg_advisory_xact_lock` is xact-scoped); session locks were rejected (explicit-unlock/cancellation/pooling failure modes). This is applied to EVERY employee-current-root tree mutator + the live transfer endpoint, so all concurrent pairs hold the same current key.

**The org-scope half is NOT DB-lockable.** `OrgScopeValidator` reads the actor's scopes from the **JWT `scopes` claim**, not the DB — so a scope "revocation" mutates nothing a DB lock can serialize (the grant lives in the bearer token until expiry). Only the designated-EDGE half is DB-row-based + serializable.

**The NAMED revocation-residual map (NOT serialized by S78 — owner-ruled contained, all NON-corrupting):**
- the **self-service `DELETE /delegate`** (REPEATABLE READ, persisted/own path — does not share the key);
- **role-assignment deactivation** + **user deactivation** (`users.is_active`, flipped by ~4 paths incl. the settlement leaver flip — intractable to fully serialize without tree-locking a background service);
- the **JWT-scope revocation** (token-lifetime — a true fix needs short token TTL / a revocation list, a platform feature);
- the **admin-vikar-REVOKE post-transfer key-mismatch** (the revoke keys on the persisted `manager_vikar.tree_root_org_id` for revoke-safety, so after a transfer it can differ from the employee-current root).

A future "full" pass would bring the role/user-deactivation + self-`/delegate`-DELETE into the lock protocol; the JWT half is a separate token-lifetime platform concern. Closing them would lift Reporting-Line & Approval Routing A−→A.

## Authorization Patterns

- All endpoints MUST chain `.RequireAuthorization()` with one of the defined policies:
  - `"Authenticated"` -- any valid JWT
  - `"EmployeeOrAbove"` -- Employee (level 5) or higher
  - `"LocalAdminOrAbove"` -- LocalAdmin (level 2) or higher
  - `"GlobalAdminOnly"` -- GlobalAdmin (level 1) only
- Scope-embedded JWT enables stateless authorization without DB lookups for role checks ([ADR-009](knowledge-base/decisions/ADR-009-scope-embedded-jwt.md))
- Org hierarchy uses materialized path `/MIN01/STY02/` with `text_pattern_ops` index ([ADR-008](knowledge-base/decisions/ADR-008-materialized-path-org-hierarchy.md))
- `OrgScopeValidator` provides the second layer: even with a valid role, access is denied if the actor's scopes do not cover the target org unit

## Known Gotchas

### FAIL-001: JWT Claim Remapping

.NET 8 JWT bearer middleware maps standard claims by default (e.g. `sub` becomes `ClaimTypes.NameIdentifier`). This silently breaks lookups for custom claim names. **Fix**: Set `MapInboundClaims = false` on `JwtBearerOptions`. Reference: [FAIL-001](knowledge-base/failures/FAIL-001-jwt-claim-remapping-dotnet8.md).

### FindFirst vs FindAll on Array Claims

**CRITICAL**: Microsoft JWT bearer middleware splits `JsonClaimValueTypes.JsonArray` values into individual claims with the same claim type. Calling `FindFirst("scopes")` returns only ONE `RoleScope` object, not the full array. You MUST use `FindAll("scopes")` and parse each claim value separately (handling both `[{...}]` array and `{...}` object formats). This caused HTTP 403 bugs where users with multiple scopes appeared to have only one.

### Anonymous Type Serialization

NEVER serialize `RoleScope` via anonymous types (e.g. `new { role = ..., orgId = ..., scopeType = ... }`). Anonymous types produce camelCase properties by default, but `RoleScope` deserialization expects PascalCase (`Role`, `OrgId`, `ScopeType`). This mismatch causes silent deserialization failures and empty scopes. Always use the typed `RoleScope` record. Lesson from Sprint 6.

## Audit Trail

- **Append-only `audit_log` table** in PostgreSQL: records all security-relevant operations
- **Domain event tracking**: All domain events extend `DomainEventBase` with `ActorId`, `ActorRole`, `CorrelationId` (nullable, backward compatible). Reference: [PAT-004](knowledge-base/patterns/PAT-004-domain-events-extend-base-with-actor-tracking.md)
- **Role assignment audit**: `role_assignment_audit` table tracks all role grant/revoke operations with actor context
- **Correlation IDs**: Every HTTP request gets a `CorrelationId` (from header or auto-generated), threaded through all events and logs for end-to-end traceability
