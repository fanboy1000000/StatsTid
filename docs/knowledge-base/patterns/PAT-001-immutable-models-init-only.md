# [PAT-001] Immutable models with init-only properties

| Field | Value |
|-------|-------|
| **ID** | PAT-001 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | Sprint 1 |
| **Date** | 2026-01-15 |
| **Domains** | Data Model |
| **Tags** | immutability, models, value-objects, c-sharp |

## Context
Domain models and events must be immutable to support event sourcing, safe concurrency, and deterministic replay. Mutable models risk accidental state corruption and make reasoning about event streams difficult.

## Decision
All domain models, value objects, DTOs, and events use C# `init`-only properties. No public setters are allowed on domain types.

## Rationale
- `init`-only properties allow object initialization syntax while preventing mutation after construction
- Immutability is a prerequisite for reliable event sourcing (events represent facts that don't change)
- Reduces bugs from accidental mutation in multi-step processing pipelines
- Aligns with C# 9+ best practices for record-like types

## Consequences
- All model properties must use `{ get; init; }` instead of `{ get; set; }`
- State changes require creating new instances rather than modifying existing ones
- Serialization must work with init-only properties (System.Text.Json supports this natively)

## Agent Guidance
- **Data Model Agent**: Every property on models, events, and DTOs must be `{ get; init; }`. Use `record` types where appropriate.
- **All agents**: If you need to "update" a model, create a new instance with the changed values. Never use reflection or unsafe casts to bypass immutability.
