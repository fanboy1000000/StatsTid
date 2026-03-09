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

Defined in `src/Infrastructure/StatsTid.Infrastructure/Security/ActorContext.cs`.

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
