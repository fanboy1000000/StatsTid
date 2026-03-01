# [ADR-007] JWT auth with RBAC and correlation IDs

| Field | Value |
|-------|-------|
| **ID** | ADR-007 |
| **Category** | decision |
| **Status** | approved |
| **Sprint** | Sprint 3 |
| **Date** | 2026-02-15 |
| **Domains** | Security, Infrastructure |
| **Tags** | jwt, rbac, authentication, authorization, correlation-id, audit |

## Context
The system needs authentication, authorization, and request traceability across all services. State sector requirements demand audit trails with actor identification and role-based access control.

## Decision
- JWT tokens using HMAC-SHA256 with a shared key across all services
- Role-based access control with four roles: Admin, Manager, Employee, ReadOnly
- Correlation IDs on every request for cross-service traceability
- Append-only audit log in PostgreSQL tracking all state-changing operations
- Ownership enforcement: Employees can only access their own data

## Rationale
- HMAC-SHA256 with shared key is simple and sufficient for inter-service auth in a single deployment
- RBAC maps directly to Danish state organizational hierarchy
- Correlation IDs enable end-to-end tracing without distributed tracing infrastructure
- Append-only audit log satisfies legal auditability requirements
- Aligns with Priority #7 (security and access control) while not compromising higher priorities

## Consequences
- JWT key is shared via Docker Compose env vars (YAML anchor `x-jwt-env`)
- Health endpoints must remain unauthenticated (Docker health checks)
- All domain events must include ActorId and ActorRole fields
- Audit log table has no UPDATE/DELETE — only INSERT
- New API endpoints must specify their authorization policy

## Agent Guidance
- **Security Agent**: Maintain the four-role hierarchy. New policies must be added to `AuthorizationPolicies.cs`.
- **Data Model Agent**: All events must include ActorId, ActorRole, and optional CorrelationId fields.
- **All service agents**: Use `[Authorize]` on all endpoints except health checks. Follow existing patterns for claim extraction.
