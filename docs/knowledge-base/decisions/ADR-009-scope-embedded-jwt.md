# ADR-009: Role Scopes Embedded in JWT Token

| Field | Value |
|-------|-------|
| **Status** | approved |
| **Sprint** | S8 |
| **Domains** | Security, Infrastructure |
| **Tags** | jwt, rbac, scopes, authorization, stateless |

## Context
The new 5-role system requires scope-aware authorization (e.g., "LocalHR for Ministry X and descendants"). Scope resolution requires joining role_assignments with organizations. Two approaches: (a) embed scopes in JWT, (b) look up scopes on every request.

## Decision
Embed role scopes in the JWT token as a `scopes` claim containing a JSON array of `RoleScope` objects (Role, OrgId, ScopeType). Authorization decisions can be made statelessly from JWT claims alone.

## Rationale
- Role changes are infrequent in the Danish state sector (monthly at most)
- Embedding scopes avoids a DB roundtrip on every API request
- JWT is already the authentication mechanism — adding claims is low-cost
- Token re-issue (via re-login or refresh) handles scope changes

## Consequences
- JWT size increases (typically 200-400 bytes for 1-3 scope entries)
- Role changes require token re-issue — not instant
- Scopes must be serialized as `RoleScope` directly (PascalCase) to match deserialization

## Agent Guidance
- Use `JwtTokenService.GenerateToken(... orgId, scopes)` with `RoleScope` list
- Deserialize in `ActorContext.GetActorContext()` or `ScopeAuthorizationHandler`
- CRITICAL: Serialize `RoleScope` directly, never via anonymous types (case mismatch)
