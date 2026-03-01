# [ADR-005] Explicit type map for polymorphic event serialization

| Field | Value |
|-------|-------|
| **ID** | ADR-005 |
| **Category** | decision |
| **Status** | approved |
| **Sprint** | Sprint 1 |
| **Date** | 2026-01-15 |
| **Domains** | Data Model, Infrastructure |
| **Tags** | serialization, events, type-map, system-text-json |

## Context
Events are stored as JSON in PostgreSQL and must be deserialized back to their concrete C# types. System.Text.Json requires explicit configuration for polymorphic deserialization — it cannot auto-discover types by convention.

## Decision
Maintain an explicit type discriminator map in `EventSerializer` that maps string type names to concrete event types. Every new event type must be registered in this map.

## Rationale
- Explicit mapping avoids reflection-based discovery which is fragile and non-deterministic
- String discriminators are stable across assembly renames and refactoring
- The map serves as a living registry of all event types in the system
- Aligns with Priority #3 (event sourcing and auditability) — events must always be deserializable

## Consequences
- Adding a new event type requires updating the type map in `EventSerializer`
- Forgetting to register a type will cause deserialization failures (caught at runtime)
- The type map is a cross-domain dependency (see DEP-003)

## Agent Guidance
- **Data Model Agent**: When creating a new event type, you MUST also add it to the EventSerializer type map.
- **Test & QA Agent**: Include serialization round-trip tests for every event type.
- **All agents**: If you encounter a `JsonException` during event deserialization, check the type map first.
