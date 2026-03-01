# [DEP-003] EventSerializer must register all event types

| Field | Value |
|-------|-------|
| **ID** | DEP-003 |
| **Category** | dependency |
| **Status** | approved |
| **Sprint** | Sprint 1 |
| **Date** | 2026-01-15 |
| **Domains** | Infrastructure, Data Model |
| **Tags** | serialization, events, type-map, cross-domain, dependency |

## Dependency
**From**: Infrastructure (`src/Infrastructure/**/EventSerializer.cs`)
**To**: Data Model (`src/SharedKernel/**/Events/**`)

The EventSerializer maintains an explicit type discriminator map for polymorphic JSON serialization. Every event type defined by the Data Model Agent must be registered in this map, or deserialization will fail at runtime.

## Impact
- Adding a new event type without registering it causes silent serialization failures
- Renaming or removing an event type breaks deserialization of existing stored events
- This is the most common cross-domain coordination failure point in the system

## Coordination Protocol
1. Data Model Agent creates new event types in `src/SharedKernel/**/Events/**`
2. Data Model Agent MUST also update the type map in `EventSerializer.cs` (this file is in its scope)
3. Test & QA Agent includes round-trip serialization tests for every event type
4. If deserialization tests fail, check the type map first

## Agent Guidance
- **Data Model Agent**: When creating a new event, ALWAYS add it to the EventSerializer type map in the same change. This is a mandatory step, not optional.
- **Test & QA Agent**: Include a test that verifies every event type in the system has a corresponding entry in the type map.
- **All agents**: If you encounter `JsonException` or "unknown type discriminator" errors, the most likely cause is a missing type map entry.
