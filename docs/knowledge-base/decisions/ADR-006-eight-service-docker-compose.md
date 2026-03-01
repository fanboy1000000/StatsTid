# [ADR-006] 8-service Docker Compose architecture

| Field | Value |
|-------|-------|
| **ID** | ADR-006 |
| **Category** | decision |
| **Status** | approved |
| **Sprint** | Sprint 1 |
| **Date** | 2026-01-15 |
| **Domains** | Infrastructure |
| **Tags** | docker, microservices, architecture |

## Context
The system consists of multiple bounded contexts (backend API, rule engine, orchestrator, payroll, external integrations) that need to be independently deployable and testable. Mock services are needed for external dependencies during development.

## Decision
Use Docker Compose with 8 services: `postgres`, `backend-api`, `rule-engine`, `orchestrator`, `payroll`, `external`, `mock-payroll`, `mock-external`.

## Rationale
- Each bounded context runs as its own service, enforcing isolation at the process level
- Mock services allow development and testing without real external dependencies
- Docker Compose provides a single-command development environment
- Service isolation prevents accidental coupling between domains

## Consequences
- Each service has its own Dockerfile and startup configuration
- Shared configuration (e.g., JWT keys) uses Docker Compose YAML anchors (x-jwt-env)
- Health checks are configured per service for dependency ordering
- All services share the PostgreSQL instance but use logical isolation

## Agent Guidance
- **All agents**: Your service must be independently buildable. Do not create cross-service compile-time dependencies.
- **Infrastructure agents**: Docker Compose changes require Orchestrator approval.
- **Security Agent**: JWT configuration uses Docker env var anchors — changes affect all services simultaneously.
