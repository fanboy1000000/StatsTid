# Sprint 3 — Security, Audit, Validation, CI/CD

| Field | Value |
|-------|-------|
| **Sprint** | 3 |
| **Status** | complete |
| **Start Date** | 2026-02-10 |
| **End Date** | 2026-02-21 |
| **Orchestrator Approved** | yes — 2026-02-21 |
| **Build Verified** | yes — `dotnet build` 0 warnings 0 errors |
| **Test Verified** | yes — 97 unit + 6 regression = 103 total passing |

## Sprint Goal
Add JWT authentication with RBAC across all services. Implement correlation IDs for cross-service tracing. Create append-only audit log. Add input validation. Create regression test suite. Set up CI/CD pipeline. Extend frontend with login flow. All security work must preserve rule engine purity (P2).

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved
- [x] P2 — Rule engine determinism maintained (zero changes to Rules/*.cs — security is infrastructure-only)
- [x] P3 — Event sourcing append-only semantics respected (audit log is append-only)
- [x] P4 — OK version correctness (entry-date resolution) — regression tests confirm
- [x] P5 — Integration isolation and delivery guarantees — regression tests confirm
- [x] P6 — Payroll integration correctness (traceability chain) — regression tests confirm
- [x] P7 — Security and access control (JWT, RBAC, audit, ownership enforcement)
- [x] P8 — CI/CD enforcement (GitHub Actions workflow)
- [x] P9 — Usability and UX (login page, auth-aware frontend)

## Task Log

### TASK-301 — Implement JWT authentication shared across services

| Field | Value |
|-------|-------|
| **ID** | TASK-301 |
| **Status** | complete |
| **Agent** | Security & Compliance |
| **Components** | SharedKernel (Security), Infrastructure (Security) |
| **KB Refs** | ADR-007 |
| **Orchestrator Approved** | yes — 2026-02-14 |

**Description**: Implemented JWT token generation and validation using HMAC-SHA256 with a shared secret. Created JwtTokenService for token generation, JwtValidationSetup for configuring validation across all services. Shared key distributed via Docker Compose YAML anchor (`x-jwt-env`). Health endpoints excluded from authentication.

**Validation Criteria**:
- [x] JWT tokens generated with claims (EmployeeId, Role, Agreement)
- [x] Token validation configured on all 5 API services
- [x] Shared key via Docker env vars
- [x] Health endpoints remain unauthenticated
- [x] Token expiry enforced

**Files Changed**:
- `src/SharedKernel/**/Security/JwtSettings.cs` — JWT configuration model
- `src/SharedKernel/**/Security/StatsTidRoles.cs` — Admin, Manager, Employee, ReadOnly
- `src/SharedKernel/**/Security/StatsTidClaims.cs` — Custom claim types
- `src/Infrastructure/**/Security/JwtTokenService.cs` — Token generation
- `src/Infrastructure/**/Security/JwtValidationSetup.cs` — Validation middleware setup
- `docker/docker-compose.yml` — Added `x-jwt-env` anchor and env vars to all services

---

### TASK-302 — Implement RBAC authorization policies

| Field | Value |
|-------|-------|
| **ID** | TASK-302 |
| **Status** | complete |
| **Agent** | Security & Compliance |
| **Components** | Infrastructure (Security), Backend API |
| **KB Refs** | ADR-007 |
| **Orchestrator Approved** | yes — 2026-02-14 |

**Description**: Created role-based authorization policies (AdminOnly, ManagerOrAbove, EmployeeOrAbove, Authenticated). Applied policies to all API endpoints. Implemented ownership enforcement — employees can only POST time entries for their own EmployeeId.

**Validation Criteria**:
- [x] Four authorization policies defined and applied
- [x] Ownership enforcement on Employee POST operations
- [x] Admin can access all endpoints
- [x] ReadOnly cannot perform state-changing operations

**Files Changed**:
- `src/Infrastructure/**/Security/AuthorizationPolicies.cs` — Policy definitions
- `src/Backend/StatsTid.Backend.Api/Program.cs` — Policies applied to endpoints

---

### TASK-303 — Implement correlation IDs and audit logging

| Field | Value |
|-------|-------|
| **ID** | TASK-303 |
| **Status** | complete |
| **Agent** | Security & Compliance, Data Model |
| **Components** | Infrastructure (Security), SharedKernel (Models), Infrastructure |
| **KB Refs** | ADR-007, PAT-004 |
| **Orchestrator Approved** | yes — 2026-02-17 |

**Description**: Added correlation ID middleware that generates/propagates a unique ID per request for cross-service tracing. Created append-only audit log table in PostgreSQL with INSERT-only repository. Extended DomainEventBase with ActorId, ActorRole, and nullable CorrelationId fields (backward compatible). Created ActorContext for extracting identity from JWT claims.

**Validation Criteria**:
- [x] Correlation ID generated per request, propagated in headers
- [x] Audit log table is append-only (no UPDATE/DELETE)
- [x] DomainEventBase extended with ActorId, ActorRole, CorrelationId
- [x] CorrelationId nullable for backward compatibility with Sprint 1-2 events
- [x] ActorContext extracts identity from JWT claims

**Files Changed**:
- `src/Infrastructure/**/Security/CorrelationIdMiddleware.cs` — Correlation ID generation/propagation
- `src/Infrastructure/**/Security/AuditLoggingMiddleware.cs` — Request-level audit logging
- `src/Infrastructure/**/Security/ActorContext.cs` — JWT claim extraction
- `src/Infrastructure/AuditLogRepository.cs` — Append-only audit persistence
- `src/SharedKernel/**/Models/AuditLogEntry.cs` — Audit entry model
- `src/SharedKernel/**/Events/DomainEventBase.cs` — Added ActorId, ActorRole, CorrelationId
- `docker/postgres/init.sql` — Added audit_log table

---

### TASK-304 — Implement input validation

| Field | Value |
|-------|-------|
| **ID** | TASK-304 |
| **Status** | complete |
| **Agent** | Security & Compliance |
| **Components** | Backend API |
| **KB Refs** | — |
| **Orchestrator Approved** | yes — 2026-02-17 |

**Description**: Created RequestValidator with validation methods for time entry, absence, and calculation requests. Validates required fields, date ranges, enum values, and string lengths. Returns structured validation errors.

**Validation Criteria**:
- [x] All POST endpoints validate input before processing
- [x] Structured error responses for invalid requests
- [x] Date range validation (start before end)
- [x] String length limits enforced

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Validation/RequestValidator.cs` — Validation logic
- `src/Backend/StatsTid.Backend.Api/Program.cs` — Validation integrated into endpoints

---

### TASK-305 — Implement login endpoint and frontend auth

| Field | Value |
|-------|-------|
| **ID** | TASK-305 |
| **Status** | complete |
| **Agent** | Security & Compliance, UX |
| **Components** | Backend API, Frontend |
| **KB Refs** | ADR-007 |
| **Orchestrator Approved** | yes — 2026-02-19 |

**Description**: Created login endpoint in Backend API with hardcoded test users (6 users across Admin, Manager, Employee, ReadOnly roles and AC, HK, PROSA agreements). Created frontend LoginPage with token storage and useAuth hook for authenticated API calls.

**Validation Criteria**:
- [x] Login endpoint returns JWT on valid credentials
- [x] 6 test users covering all roles and agreements
- [x] Frontend stores token and includes in API calls
- [x] Frontend redirects unauthenticated users to login

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Contracts/LoginRequest.cs` — Login request DTO
- `src/Backend/StatsTid.Backend.Api/Contracts/LoginResponse.cs` — Login response DTO
- `src/Backend/StatsTid.Backend.Api/Program.cs` — Login endpoint
- `frontend/src/pages/LoginPage.tsx` — Login page component
- `frontend/src/hooks/useAuth.ts` — Auth hook with token management

---

### TASK-306 — Create CI/CD pipeline

| Field | Value |
|-------|-------|
| **ID** | TASK-306 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Components** | CI/CD |
| **KB Refs** | — |
| **Orchestrator Approved** | yes — 2026-02-19 |

**Description**: Created GitHub Actions workflow for CI. Runs on push and PR to master. Steps: checkout, .NET 8 setup, restore, build, unit tests, regression tests. Smoke tests excluded (require Docker Compose).

**Validation Criteria**:
- [x] Workflow triggers on push and PR to master
- [x] Build step succeeds
- [x] Unit and regression tests run and pass
- [x] Smoke tests excluded from CI

**Files Changed**:
- `.github/workflows/ci.yml` — CI pipeline definition

---

### TASK-307 — Create regression test suite and expand unit tests

| Field | Value |
|-------|-------|
| **ID** | TASK-307 |
| **Status** | complete |
| **Agent** | Test & QA |
| **Components** | Tests |
| **KB Refs** | ADR-002, ADR-003, PAT-002, RES-001, ADR-007 |
| **Orchestrator Approved** | yes — 2026-02-21 |

**Description**: Created regression test project with 6 tests proving determinism, OK version transitions, and AC vs HK/PROSA behavioral separation. Added 23 new unit tests for Sprint 3 features (JWT token service, correlation ID middleware, actor context, request validation).

**Validation Criteria**:
- [x] Regression tests prove determinism (same input → same output)
- [x] Regression tests prove OK version transitions (OK24 → OK26 boundary)
- [x] Regression tests prove AC vs HK/PROSA behavioral divergence
- [x] Unit tests cover JWT token generation and validation
- [x] Unit tests cover correlation ID middleware
- [x] Unit tests cover input validation
- [x] All 103 tests green (97 unit + 6 regression)

**Files Changed**:
- `tests/StatsTid.Tests.Regression/RegressionTests.cs` — 6 regression tests
- `tests/StatsTid.Tests.Regression/StatsTid.Tests.Regression.csproj` — Project file
- `tests/StatsTid.Tests.Unit/Security/JwtTokenServiceTests.cs` — JWT tests
- `tests/StatsTid.Tests.Unit/Security/CorrelationIdMiddlewareTests.cs` — Middleware tests
- `tests/StatsTid.Tests.Unit/Security/ActorContextTests.cs` — Actor context tests
- `tests/StatsTid.Tests.Unit/Events/DomainEventBaseActorTests.cs` — Event actor field tests
- `tests/StatsTid.Tests.Unit/Validation/RequestValidatorTests.cs` — Validation tests

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified | Regression tests confirm AC/HK/PROSA behavioral separation; zero changes to Rules/*.cs |
| Wage type mappings produce correct SLS codes | verified | No payroll mapping changes; existing mappings confirmed stable |
| Overtime/supplement calculations are deterministic | verified | 6 regression tests prove determinism across OK versions and agreement types |
| Absence effects on norm/flex/pension are correct | verified | No absence rule changes; existing tests still pass |
| Retroactive recalculation produces stable results | verified | OK version entry-date resolution confirmed via regression tests |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 97 | all passing |
| Regression tests | 6 | all passing |
| Smoke tests | 4 | N/A (requires Docker) |
| **Total** | 103 | — |

## Sprint Retrospective

**What went well**: Security layer added without any changes to rule engine code — rule engine purity (P2) fully preserved. Regression test suite provides confidence for future changes. Correlation IDs enable cross-service debugging. CI/CD pipeline automates build and test.

**What to improve**: Test users are hardcoded — need proper user management. Frontend auth is basic — needs token refresh, session timeout. Smoke tests not in CI (require Docker Compose). Audit log queries not yet exposed via API.

**Knowledge produced**: ADR-007, PAT-004 (extended with actor tracking)
