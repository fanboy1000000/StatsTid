# StatsTid Architecture

> Service topology, bounded contexts, and dependency rules for the Danish state sector time-registration and payroll platform.

## Service Topology

Eight Docker services compose the runtime (see [ADR-006](knowledge-base/decisions/ADR-006-eight-service-docker-compose.md)):

| Service | Technology | Port | Responsibility |
|---------|-----------|------|----------------|
| **postgres** | PostgreSQL 16 | 5432 | Event store, all application tables, outbox |
| **backend-api** | .NET 8 Minimal API | 5100 | HTTP endpoints for frontend (auth, time, admin, approval, config, skema, etc.) |
| **rule-engine** | .NET 8 Minimal API | 5200 | Pure deterministic rule evaluation (norm, supplement, overtime, absence, flex, on-call, travel, call-in) |
| **orchestrator** | .NET 8 | 5300 | Weekly calculation pipeline, task dispatch |
| **payroll** | .NET 8 Minimal API | 5400 | Wage type mapping, SLS export, period calculation, retroactive corrections |
| **external** | .NET 8 Minimal API | 5500 | Outbound integrations with circuit breaker and backoff |
| **mock-payroll** | .NET 8 | 5600 | Test double for the payroll target system |
| **mock-external** | .NET 8 | 5700 | Test double for external integration targets |

*Ports shown are the **host** ports published by docker-compose (5100–5700); every container listens internally on **8080**, so service-to-service URLs are `http://<service>:8080`.*

All .NET services share JWT HMAC-SHA256 secrets via Docker environment variables ([ADR-007](knowledge-base/decisions/ADR-007-jwt-auth-rbac-correlation-ids.md)).

```
┌─────────┐      ┌──────────────┐  HTTP   ┌─────────────┐
│ Frontend │─────>│  backend-api │────────>│ rule-engine  │
│ (Vite)   │      │   :5100      │         │   :5200      │
└─────────┘      └──────┬───────┘         └─────────────┘
                        │                        ^
                        │ HTTP                   │ HTTP
                        v                        │
                 ┌──────────────┐         ┌──────┴──────┐
                 │ orchestrator │────────>│   payroll    │
                 │   :5300      │         │   :5400      │
                 └──────────────┘         └──────┬──────┘
                                                 │ HTTP
                                                 v
                 ┌──────────────┐         ┌─────────────┐
                 │ mock-external│<────────│  external    │
                 │   :5700      │         │   :5500      │
                 └──────────────┘         └─────────────┘
                 ┌──────────────┐
                 │ mock-payroll │ (payroll target test double)
                 │   :5600      │
                 └──────────────┘

         All services ──────> postgres :5432
```

## Bounded Contexts

### SharedKernel (`src/SharedKernel/StatsTid.SharedKernel/`)

Cross-cutting types shared by all services. No business logic, no I/O.

- **Models/** -- Immutable domain models and value objects (init-only properties per [PAT-001](knowledge-base/patterns/PAT-001-immutable-models-init-only.md))
- **Events/** -- Domain events extending `DomainEventBase` with actor tracking ([PAT-004](knowledge-base/patterns/PAT-004-domain-events-extend-base-with-actor-tracking.md))
- **Interfaces/** -- Repository and service contracts
- **Security/** -- `StatsTidRoles`, `RoleScope`, role hierarchy constants
- **Calendar/** -- Danish holiday calendar, work-day calculations ([DEP-001](knowledge-base/dependencies/DEP-001-rule-engine-depends-on-sharedkernel-calendar.md))
- **Config/** -- `CentralAgreementConfigs` (static source of truth), `PositionOverrideConfigs`

### Auth (`src/Auth/StatsTid.Auth/`)

Pure auth primitives shared by all API services. No DB dependency. Extracted from `Infrastructure/Security/` in commit b4fc670 so the RuleEngine purity invariant (ADR-002) can be enforced by the .NET assembly graph rather than by inspection.

- `ActorContext` + `GetActorContext` extension on `HttpContext`
- `JwtValidationSetup.AddStatsTidJwtAuth` (JWT bearer config; honors both `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT`)
- `AuthorizationPolicies.AddStatsTidPolicies` (6 named policies: `GlobalAdminOnly`, `LocalAdminOrAbove`, `HROrAbove`, `LeaderOrAbove`, `EmployeeOrAbove`, `Authenticated`)
- `JwtTokenService` (token issuance — used only by Backend's `/api/auth/login`)
- `ScopeAuthorizationHandler` + `ScopeRequirement` (role + scope authorization)
- `CorrelationIdMiddleware` (request correlation ID propagation)

`OrgScopeValidator` and `AuditLoggingMiddleware` stay in `Infrastructure/Security/` because they depend on `OrganizationRepository` / `UserRepository` / `AuditLogRepository` (DB-bound).

### RuleEngine (`src/RuleEngine/StatsTid.RuleEngine.Api/`)

Pure deterministic rule evaluation. Zero I/O, zero database access ([ADR-002](knowledge-base/decisions/ADR-002-pure-function-rule-engine.md)). Project references SharedKernel + Auth only — Infrastructure is structurally unreachable.

- **Rules/** -- `NormCheckRule`, `SupplementRule`, `OvertimeRule`, `AbsenceRule`, `FlexBalanceRule`, `OnCallDutyRule`, `CallInWorkRule`, `TravelTimeRule`
- **Services/** -- `AgreementConfigProvider` (delegates to `CentralAgreementConfigs`), `RuleRegistry`
- OK version resolved by entry date, not current date ([ADR-003](knowledge-base/decisions/ADR-003-ok-version-resolved-by-entry-date.md))
- All endpoints return `CalculationResult`-compatible responses ([PAT-006](knowledge-base/patterns/PAT-006-unified-rule-endpoint-response-format.md))

### Backend API (`src/Backend/StatsTid.Backend.Api/`)

HTTP gateway for the frontend. Endpoint groups organized by domain:

| Group | Responsibility |
|-------|----------------|
| `AuthEndpoints` | Login, token refresh |
| `TimeEndpoints` | Time registration CRUD |
| `AdminEndpoints` | Org, user, role management (8 CRUD endpoints) |
| `ApprovalEndpoints` | Two-step period approval ([ADR-012](knowledge-base/decisions/ADR-012-two-step-approval-flow.md)) |
| `ConfigEndpoints` | Local config with central constraint validation |
| `SkemaEndpoints` | Monthly spreadsheet data |
| `ProjectEndpoints` | Project management per org unit |
| `BalanceEndpoints` | Employee balance summary |
| `AgreementConfigEndpoints` | Agreement config lifecycle (GlobalAdmin) |
| `PositionOverrideEndpoints` | Position override management |
| `WageTypeMappingEndpoints` | Wage type mapping administration |

### Infrastructure (`src/Infrastructure/StatsTid.Infrastructure/`)

Persistence, security services, and cross-cutting infrastructure.

- **Repositories/** -- Npgsql-based (no EF Core): `EventStoreRepository`, `OrganizationRepository`, `UserRepository`, `RoleAssignmentRepository`, `LocalConfigurationRepository`, `ApprovalPeriodRepository`, `ProjectRepository`, `AgreementConfigRepository`, etc.
- **Security/** -- `OrgScopeValidator` (org-scope enforcement on all endpoints)
- **Services/** -- `ConfigResolutionService` (central + position override + local merge per [ADR-010](knowledge-base/decisions/ADR-010-local-config-merge-at-service-layer.md)), `AgreementConfigSeeder`
- **EventSerializer** -- Explicit type map registration for all domain events ([DEP-003](knowledge-base/dependencies/DEP-003-event-serializer-must-register-all-types.md))

### Integrations/Payroll (`src/Integrations/StatsTid.Integrations.Payroll/`)

Payroll export pipeline, isolated from the rule engine.

- **PeriodCalculationService** -- Calls Rule Engine via HTTP, aggregates results ([PAT-005](knowledge-base/patterns/PAT-005-period-calculation-service-http-rule-evaluation.md))
- **PayrollMappingService** -- Maps rule outputs to SLS wage type codes (position-aware precedence)
- **SlsExportFormatter** -- Pipe-delimited SLS file output (`InvariantCulture` for determinism)
- **RetroactiveCorrectionService** -- OK version split recalculation, correction export with HC|/C|/TC| prefixes ([ADR-013](knowledge-base/decisions/ADR-013-retroactive-corrections-single-period-no-cascade.md))

### Integrations/External (`src/Integrations/StatsTid.Integrations.External/`)

Outbound integrations to external systems. Async, event-driven, idempotent.

- Circuit breaker and exponential backoff for resilience
- External failures never impact the deterministic core
- Delivery tracking via outbox pattern ([ADR-004](knowledge-base/decisions/ADR-004-outbox-pattern-guaranteed-delivery.md))

### Frontend (`frontend/`)

React 18 SPA with TypeScript and Vite ([ADR-011](knowledge-base/decisions/ADR-011-frontend-design-system-and-component-strategy.md)).

- **components/** -- Design system (IBM Plex Sans, `#066b43` primary [oes.dk green, re-skinned S57], CSS Modules + custom properties)
- **pages/** -- Skema (monthly spreadsheet), Min Tid (employee hub), admin pages, approval dashboard
- **contexts/** -- `AuthContext` (JWT decode, role scopes, agreement code)
- **hooks/** -- `useSkema`, `useProjects`, `useAgreementConfigs`, etc. (timer retired S56/ADR-028)
- **lib/** -- `apiClient` (typed `ApiResult<T>`), `roles.ts` (role hierarchy + `hasMinRole()`)
- Guards: `RequireAuth` (redirect to login) + `RequireRole` (minimum role check)

## Dependency Rules

```
Types (SharedKernel)
  └── Config (CentralAgreementConfigs, PositionOverrideConfigs)
       └── Repository (Infrastructure)
            └── Service (ConfigResolutionService, PeriodCalculationService)
                 └── Runtime (Backend API, Orchestrator)
                      └── UI (Frontend)
```

**Hard rules:**

1. **Rule Engine depends ONLY on SharedKernel + Auth** ([DEP-001](knowledge-base/dependencies/DEP-001-rule-engine-depends-on-sharedkernel-calendar.md)). No database, no HTTP calls, no file I/O. Enforced by the assembly graph since b4fc670: `StatsTid.RuleEngine.Api.csproj` references SharedKernel + Auth only, so any DB-touching type is unreachable at compile time.
2. **Backend and Payroll call Rule Engine via HTTP only** -- never direct function calls ([PAT-005](knowledge-base/patterns/PAT-005-period-calculation-service-http-rule-evaluation.md)).
3. **Payroll depends on Rule Engine output types** ([DEP-002](knowledge-base/dependencies/DEP-002-payroll-depends-on-rule-engine-outputs.md)), not its internals.
4. **EventSerializer requires explicit type map registration** for every domain event ([DEP-003](knowledge-base/dependencies/DEP-003-event-serializer-must-register-all-types.md)).
5. **Frontend communicates with Backend API only** via relative `/api/` paths proxied by Vite in development and Docker networking in production.
6. **External integration failures must never impact the deterministic core** -- circuit breakers and outbox guarantee isolation.

## Technology Stack

| Layer | Technology | Notes |
|-------|-----------|-------|
| Backend services | .NET 8 Minimal APIs, C# 12 | No controllers, no MVC |
| Database | PostgreSQL 16, Npgsql | No EF Core ([ADR-001](knowledge-base/decisions/ADR-001-event-sourcing-postgresql-npgsql.md)) |
| Event sourcing | Append-only event store + outbox | [ADR-001](knowledge-base/decisions/ADR-001-event-sourcing-postgresql-npgsql.md), [ADR-004](knowledge-base/decisions/ADR-004-outbox-pattern-guaranteed-delivery.md) |
| Serialization | System.Text.Json with explicit type map | [ADR-005](knowledge-base/decisions/ADR-005-explicit-type-map-polymorphic-serialization.md) |
| Authentication | JWT HMAC-SHA256 with scope-embedded claims | [ADR-007](knowledge-base/decisions/ADR-007-jwt-auth-rbac-correlation-ids.md), [ADR-009](knowledge-base/decisions/ADR-009-scope-embedded-jwt.md) |
| Org hierarchy | Materialized path in PostgreSQL | [ADR-008](knowledge-base/decisions/ADR-008-materialized-path-org-hierarchy.md) |
| Frontend | React 18 + TypeScript + Vite | [ADR-011](knowledge-base/decisions/ADR-011-frontend-design-system-and-component-strategy.md) |
| Styling | CSS Modules + CSS custom properties | designsystem.dk-inspired tokens |
| Orchestration | Docker Compose | [ADR-006](knowledge-base/decisions/ADR-006-eight-service-docker-compose.md) |
| Testing | xUnit (.NET), vitest + @testing-library/react (frontend) | see [docs/sprints/INDEX.md](sprints/INDEX.md) for current counts (3269 at S128) |

## Configuration Patterns

**Service discovery:** Inter-service URLs configured via `IConfiguration["ServiceUrls:RuleEngine"]` (etc.), set through Docker environment variables.

**Agreement config resolution chain** ([ADR-010](knowledge-base/decisions/ADR-010-local-config-merge-at-service-layer.md), [ADR-014](knowledge-base/decisions/ADR-014-agreement-configs-database-backed.md)):

```
Central config (DB, with static fallback)
  └── Position override (PositionOverrideConfigs)
       └── Local override (per org unit, DB-stored)
            = Effective config passed to Rule Engine
```

- Central configs are DB-backed with lifecycle: `DRAFT -> ACTIVE -> ARCHIVED` ([ADR-014](knowledge-base/decisions/ADR-014-agreement-configs-database-backed.md))
- Static `CentralAgreementConfigs` used as seed data and defense-in-depth fallback
- Config merging happens at the service layer (`ConfigResolutionService`), never inside the rule engine

**Period approval flow** ([ADR-012](knowledge-base/decisions/ADR-012-two-step-approval-flow.md)):

```
DRAFT -> EMPLOYEE_APPROVED -> APPROVED
                           -> REJECTED
APPROVED -> REOPEN -> DRAFT (manager can reopen)
```

## Key Architectural Decisions

All architectural decision records (ADR), validated patterns (PAT), cross-domain dependencies
(DEP), failure analyses (FAIL), and research resolutions (RES) are indexed in
**[knowledge-base/INDEX.md](knowledge-base/INDEX.md)** — the complete, CI-checked index
(`tools/check_docs.py` fails the `docs` job if an entry on disk is missing from it). Consult the
index, not any per-document copy: an earlier revision of this file hand-maintained a subset of
these tables, which silently froze at ADR-014 while the KB grew past ADR-038 — the pointer
replaced the tables so this file can no longer drift.

Foundational reading order for a new contributor: ADR-001 (event sourcing), ADR-002 (pure-function
rule engine), ADR-018 (transactional outbox), ADR-007/009 (JWT + scope model), ADR-038 (the
as-built org/unit hierarchy).
