# [ADR-001] Event sourcing with PostgreSQL via Npgsql

| Field | Value |
|-------|-------|
| **ID** | ADR-001 |
| **Category** | decision |
| **Status** | approved |
| **Sprint** | Sprint 1 |
| **Date** | 2026-01-15 |
| **Domains** | Infrastructure, Data Model |
| **Tags** | event-sourcing, postgresql, npgsql |

## Context
The system requires full auditability, replayability, and historical recalculation for Danish state time registration. A persistence strategy was needed that supports immutable event streams and temporal queries.

## Decision
Use PostgreSQL as the event store with raw Npgsql for data access. No ORM (Entity Framework Core is explicitly excluded). Events are stored in custom event tables with append-only semantics.

## Rationale
- PostgreSQL provides ACID guarantees, JSON support, and is well-suited for event tables
- Npgsql gives full control over SQL and avoids ORM abstraction leaks
- EF Core was rejected to maintain explicit control over event serialization and query patterns
- Custom event tables allow precise control over stream structure, versioning, and indexing

## Consequences
- All database access must use raw Npgsql with parameterized queries
- No EF Core migrations — schema changes are managed via `docker/postgres/init.sql`
- Event serialization must be handled explicitly (see ADR-005)
- All queries must be hand-written SQL

## Agent Guidance
- **Data Model Agent**: Events must be designed for append-only storage. Never include mutable state in event payloads.
- **Infrastructure agents**: Use `NpgsqlConnection` directly. Never introduce EF Core or any ORM.
- **All agents**: Database schema changes require Orchestrator approval via `init.sql`.
