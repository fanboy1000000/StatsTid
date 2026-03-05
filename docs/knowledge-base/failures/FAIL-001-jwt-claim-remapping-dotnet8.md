# FAIL-001 — .NET 8 JWT Claim Remapping Silently Breaks Custom Claims

| Field | Value |
|-------|-------|
| **ID** | FAIL-001 |
| **Category** | failure |
| **Status** | resolved |
| **Sprint** | S9 |
| **Domains** | Security, Infrastructure |
| **Tags** | jwt, claims, dotnet8, authentication, debugging |
| **Date** | 2026-03-05 |

## Context

After Sprint 9 Docker rebuild (fresh volumes), all authenticated endpoints returned HTTP 403 despite valid JWT tokens. The `ScopeAuthorizationHandler` could not find the `"role"` claim in the user's ClaimsPrincipal.

## What Happened

.NET 8's JWT bearer handler internally uses `JsonWebTokenHandler` (not `JwtSecurityTokenHandler`). By default, it remaps standard JWT claim names to long XML-namespace URIs:

- `"role"` → `"http://schemas.microsoft.com/ws/2008/06/identity/claims/role"`
- `"sub"` → `"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"`

Our `ScopeAuthorizationHandler` and `ActorContext` use `FindFirst("role")` and `FindFirst("sub")` — the short names. After remapping, these return `null`, causing 403 on every request.

## What We Tried That Didn't Work

1. **`JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear()`** — This only affects the legacy `JwtSecurityTokenHandler`. .NET 8's `AddJwtBearer()` uses `JsonWebTokenHandler` internally, so clearing the legacy handler's map has no effect.

## What Fixed It

Two changes in `JwtValidationSetup.cs`:

```csharp
// 1. Disable claim remapping on the JWT bearer handler itself
options.MapInboundClaims = false;

// 2. Tell the handler which claims map to Name and Role
options.TokenValidationParameters = new TokenValidationParameters
{
    // ... existing params ...
    NameClaimType = "sub",
    RoleClaimType = "role"
};
```

Also updated `ActorContext.cs` to check `"sub"` as the primary claim for ActorId (before falling back to `ClaimTypes.NameIdentifier`).

## Why This Matters

- This is a **silent failure** — no exceptions, no logs, just 403 on every request
- The issue may have been latent since Sprint 3 (JWT auth introduction) but only manifested after a fresh Docker volume rebuild
- Any future custom JWT claims will also be remapped unless `MapInboundClaims = false` is set

## Agent Guidance

- **Always** set `options.MapInboundClaims = false` when using custom JWT claims in .NET 8
- **Always** set `NameClaimType` and `RoleClaimType` in `TokenValidationParameters` when disabling inbound claim mapping
- When debugging 403 errors with valid tokens, check claim names first — use breakpoints or logging to inspect `ClaimsPrincipal.Claims` for unexpected long-form URIs
- `JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear()` is kept as a belt-and-suspenders measure but is not sufficient alone
