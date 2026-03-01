# StatsTid Roadmap

> Technology stack and strategic phases. See [SYSTEM_TARGET.md](SYSTEM_TARGET.md) for product definition, [CLAUDE.md](CLAUDE.md) for governance.

## Technology Stack

- **Backend**: C# / .NET 8 (Minimal APIs)
- **Frontend**: React + TypeScript (stub in Sprint 1)
- **Event Store**: PostgreSQL with custom event tables (via Npgsql, no EF Core)
- **Containerization**: Docker Compose (8 services)
- **Testing**: xUnit
- **Serialization**: System.Text.Json with polymorphic type handling
- **Architecture**: Event sourcing, outbox pattern, CQRS-lite
- **Rule Engine**: Pure functions, no I/O, deterministic, version-aware (OK24+)

## Phase Progression

| Sprint | Title | Key Deliverables | Tests |
|--------|-------|------------------|-------|
| Sprint 1 | Foundation | 8-service Docker skeleton, event sourcing, first rule | 12 |
| Sprint 2 | Rule Engine Expansion | Absence/flex/supplement logic, OK version transitions, frontend scaffold | 74 |
| Sprint 3 | Security & Compliance | JWT auth, RBAC, audit logging, correlation IDs, input validation, CI/CD | 103 |
| Sprint 4+ | _Upcoming_ | — | — |

## Architecture Decisions

See [docs/knowledge-base/INDEX.md](docs/knowledge-base/INDEX.md) for the full structured decision log (ADR, PAT, DEP, RES entries).

## Sprint Execution Logs

See [docs/sprints/INDEX.md](docs/sprints/INDEX.md) for the formal sprint log with validation evidence and traceability.
