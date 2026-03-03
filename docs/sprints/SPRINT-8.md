# Sprint 8 — RBAC with Organizational Hierarchy

| Field | Value |
|-------|-------|
| **Sprint** | 8 |
| **Status** | complete |
| **Start Date** | 2026-03-03 |
| **End Date** | 2026-03-03 |
| **Orchestrator Approved** | yes — 2026-03-03 |
| **Build Verified** | yes — `dotnet build` 0 warnings 0 errors |
| **Test Verified** | yes — 164 unit + 15 regression = 179 total passing |

## Sprint Goal
Implement the foundation for a 5-role RBAC system with organizational hierarchy, scope-aware authorization, and DB-backed authentication to support the Danish state multi-org structure.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (new bounded context for identity/org, does not affect rule engine or payroll)
- [x] P2 — Rule engine determinism maintained (zero changes to rule engine code)
- [x] P3 — Event sourcing append-only semantics respected (8 new event types, all extend DomainEventBase)
- [ ] P4 — OK version correctness (not directly tested this sprint)
- [ ] P5 — Integration isolation and delivery guarantees (not touched)
- [ ] P6 — Payroll integration correctness (not touched)
- [x] P7 — Security and access control (new 5-role system, scope-aware auth, JWT with scopes)
- [x] P8 — CI/CD enforcement (build + test pass)
- [ ] P9 — Usability and UX (deferred to Sprint 10)

## Task Log

### TASK-801 — New SharedKernel Models

| Field | Value |
|-------|-------|
| **ID** | TASK-801 |
| **Status** | complete |
| **Agent** | Data Model |
| **Components** | SharedKernel/Models |
| **KB Refs** | PAT-001 |
| **Reviewer Audit** | performed — no model-specific findings |
| **Orchestrator Approved** | yes — 2026-03-03 |

**Description**: Created 5 new immutable models: Organization, User, RoleAssignment, LocalConfiguration, ApprovalPeriod.

**Validation Criteria**:
- [x] All models use init-only properties (PAT-001)
- [x] Build passes

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Models/Organization.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Models/User.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Models/RoleAssignment.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Models/LocalConfiguration.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Models/ApprovalPeriod.cs` — new

---

### TASK-802 — New Domain Events + EventSerializer Registration

| Field | Value |
|-------|-------|
| **ID** | TASK-802 |
| **Status** | complete |
| **Agent** | Data Model |
| **Components** | SharedKernel/Events, Infrastructure/EventSerializer |
| **KB Refs** | PAT-004, DEP-003 |
| **Reviewer Audit** | performed — no event-specific findings |
| **Orchestrator Approved** | yes — 2026-03-03 |

**Description**: Created 8 new events extending DomainEventBase, registered all in EventSerializer type map.

**Validation Criteria**:
- [x] All events extend DomainEventBase with EventType override (PAT-004)
- [x] All 8 events registered in EventSerializer (DEP-003)
- [x] Serialization round-trip tests pass

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Events/OrganizationCreated.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/UserCreated.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/RoleAssignmentGranted.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/RoleAssignmentRevoked.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/LocalConfigurationChanged.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/PeriodSubmitted.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/PeriodApproved.cs` — new
- `src/SharedKernel/StatsTid.SharedKernel/Events/PeriodRejected.cs` — new
- `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` — 8 new type registrations

---

### TASK-803 — Updated Security Infrastructure (5-Role RBAC)

| Field | Value |
|-------|-------|
| **ID** | TASK-803 |
| **Status** | complete |
| **Agent** | Security |
| **Components** | SharedKernel/Security, Infrastructure/Security |
| **KB Refs** | ADR-007, ADR-008, ADR-009 |
| **Reviewer Audit** | performed — BLOCKER found and fixed (JWT scope serialization mismatch) |
| **Orchestrator Approved** | yes — 2026-03-03 |

**Description**: Replaced 4 flat roles with 5 org-scoped roles. Created scope-aware authorization handler with materialized path matching. Added RoleScope value object and ScopeRequirement.

**Validation Criteria**:
- [x] 5 new roles defined with hierarchy levels
- [x] Legacy backward-compatible aliases for old role names
- [x] Scope-aware authorization policies (6 policies)
- [x] RoleScope.CoversOrg handles GLOBAL, ORG_AND_DESCENDANTS, ORG_ONLY
- [x] Existing endpoints compile without changes

**Files Changed**:
- `src/SharedKernel/StatsTid.SharedKernel/Security/StatsTidRoles.cs` — 5 roles + legacy + hierarchy
- `src/SharedKernel/StatsTid.SharedKernel/Security/StatsTidClaims.cs` — OrgId, Scopes claims
- `src/SharedKernel/StatsTid.SharedKernel/Security/RoleScope.cs` — new value object
- `src/Infrastructure/StatsTid.Infrastructure/Security/ScopeRequirement.cs` — new
- `src/Infrastructure/StatsTid.Infrastructure/Security/ScopeAuthorizationHandler.cs` — new
- `src/Infrastructure/StatsTid.Infrastructure/Security/AuthorizationPolicies.cs` — 6 scope-aware policies
- `src/Infrastructure/StatsTid.Infrastructure/Security/ActorContext.cs` — OrgId + Scopes (backward compatible)

---

### TASK-804 — Updated JwtTokenService

| Field | Value |
|-------|-------|
| **ID** | TASK-804 |
| **Status** | complete |
| **Agent** | Security |
| **Components** | Infrastructure/Security |
| **KB Refs** | ADR-009 |
| **Reviewer Audit** | performed — BLOCKER fixed (PascalCase serialization) |
| **Orchestrator Approved** | yes — 2026-03-03 |

**Description**: Added optional orgId and scopes parameters to GenerateToken. Embeds org_id and serialized RoleScope array in JWT claims.

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/Security/JwtTokenService.cs` — optional org/scope params

---

### TASK-805 — PostgreSQL Schema Additions + Seed Data

| Field | Value |
|-------|-------|
| **ID** | TASK-805 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Components** | PostgreSQL schema |
| **KB Refs** | ADR-008 |
| **Reviewer Audit** | performed — WARNING on seed passwords (documented) |
| **Orchestrator Approved** | yes — 2026-03-03 |

**Description**: Added 8 new tables (organizations, users, roles, role_assignments + 4 audit/config tables) and seed data for test org hierarchy (Finansministeriet), 7 test users, and role assignments.

**Files Changed**:
- `docker/postgres/init.sql` — 8 new tables, indexes, seed data

---

### TASK-806 — New Repositories

| Field | Value |
|-------|-------|
| **ID** | TASK-806 |
| **Status** | complete |
| **Agent** | Security |
| **Components** | Infrastructure |
| **KB Refs** | ADR-001 |
| **Reviewer Audit** | performed — WARNING on role ordering + expiry filter (both fixed) |
| **Orchestrator Approved** | yes — 2026-03-03 |

**Description**: Created OrganizationRepository, UserRepository, RoleAssignmentRepository using Npgsql pattern.

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/OrganizationRepository.cs` — new
- `src/Infrastructure/StatsTid.Infrastructure/UserRepository.cs` — new
- `src/Infrastructure/StatsTid.Infrastructure/RoleAssignmentRepository.cs` — new (with hierarchy ordering + expiry filter)

---

### TASK-807 — DB-Backed Login Endpoint

| Field | Value |
|-------|-------|
| **ID** | TASK-807 |
| **Status** | complete |
| **Agent** | Security |
| **Components** | Backend API |
| **KB Refs** | ADR-007, ADR-009 |
| **Reviewer Audit** | performed — no endpoint-specific findings |
| **Orchestrator Approved** | yes — 2026-03-03 |

**Description**: Dual-mode login (Auth:UseDatabase config flag). DB path uses BCrypt password verification, loads role assignments, generates JWT with org + scopes. Legacy path updated with new role names.

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Program.cs` — dual-mode login, new role names, repository registration
- `src/Backend/StatsTid.Backend.Api/Contracts/LoginResponse.cs` — OrgId field
- `src/Backend/StatsTid.Backend.Api/StatsTid.Backend.Api.csproj` — BCrypt.Net-Next dependency

---

### TASK-808 — Sprint 8 Unit Tests

| Field | Value |
|-------|-------|
| **ID** | TASK-808 |
| **Status** | complete |
| **Agent** | Test & QA |
| **Components** | Tests |
| **KB Refs** | — |
| **Reviewer Audit** | skipped (test-only) |
| **Orchestrator Approved** | yes — 2026-03-03 |

**Description**: 21 new tests: RoleScope (5), StatsTidRoles hierarchy (4), JWT with scopes (3), domain models (7), event serialization (2).

**Files Changed**:
- `tests/StatsTid.Tests.Unit/Security/Sprint8SecurityTests.cs` — new (21 tests)

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule changes in Sprint 8 |
| Wage type mappings produce correct SLS codes | N/A | No payroll changes |
| Overtime/supplement calculations are deterministic | verified | Rule engine untouched; 15 regression tests confirm |
| Absence effects on norm/flex/pension are correct | N/A | No absence changes |
| Retroactive recalculation produces stable results | N/A | No recalculation changes |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 164 | all passing |
| Regression tests | 15 | all passing |
| Smoke tests | 4 | N/A (requires Docker) |
| **Total** | 179 | — |

## Reviewer Findings

| Severity | Finding | Resolution |
|----------|---------|------------|
| BLOCKER | JWT scope serialization used snake_case anonymous types, causing PascalCase RoleScope deserialization to silently fail | Fixed: serialize RoleScope directly |
| WARNING | Primary role selection used unordered query result | Fixed: ORDER BY hierarchy_level ASC via roles join |
| WARNING | Expired role assignments not filtered | Fixed: added `expires_at > NOW()` filter |
| WARNING | Identical seed passwords undocumented | Fixed: added password documentation comment |
| WARNING | GetDescendantsAsync opens two connections | Deferred: acceptable for current usage pattern |
| NOTE | ScopeAuthorizationHandler does role-only gating, not org-scoped | Tracked: endpoint-level enforcement deferred to Sprint 9 |
| NOTE | DateTime.UtcNow defaults in model init properties | Noted: not a correctness issue for DB-populated models |
| NOTE | Legacy role aliases create ambiguous switch pattern | Noted: will be removed when all consumers migrated |

## Sprint Retrospective

**What went well**: Multi-agent parallel execution worked efficiently. Reviewer caught a critical BLOCKER (JWT serialization mismatch) that would have caused silent auth failures in production.

**What to improve**: Agents in worktrees should commit their own changes. The Orchestrator had to commit and merge manually. Also, agents working in isolation can produce code mismatches (e.g., positional record constructor vs object initializer syntax).

**Knowledge produced**: ADR-008 (materialized path), ADR-009 (scope-embedded JWT), ADR-010 (local config merge at service layer)
