# [PAT-004] Domain events extend DomainEventBase with actor tracking

| Field | Value |
|-------|-------|
| **ID** | PAT-004 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | Sprint 1, Sprint 3 |
| **Date** | 2026-02-15 |
| **Domains** | Data Model |
| **Tags** | events, domain-events, actor-tracking, audit |

## Context
All state changes are represented as domain events for event sourcing. Sprint 3 added security requirements — every event must record who performed the action for audit purposes.

## Decision
All domain events must extend `DomainEventBase`, which provides:
- `EventId` (Guid)
- `Timestamp` (DateTimeOffset)
- `ActorId` (string) — added in Sprint 3
- `ActorRole` (string) — added in Sprint 3
- `CorrelationId` (string?, nullable) — added in Sprint 3, backward compatible

## Rationale
- Base class ensures consistent metadata across all events
- Actor tracking satisfies state sector audit requirements
- CorrelationId is nullable for backward compatibility with Sprint 1-2 events
- Common base enables generic event processing (serialization, storage, projection)

## Consequences
- Every new event type must inherit from `DomainEventBase`
- Event creation must include ActorId and ActorRole (typically from JWT claims)
- CorrelationId should be set when available but is not required
- Existing events from before Sprint 3 have null CorrelationId — code must handle this

## Agent Guidance
- **Data Model Agent**: Every new event MUST extend `DomainEventBase`. Include all base fields in constructors.
- **Security Agent**: Actor fields are populated from the authenticated user context — ensure middleware sets these correctly.
- **Test & QA Agent**: All event creation in tests must include ActorId and ActorRole values.
