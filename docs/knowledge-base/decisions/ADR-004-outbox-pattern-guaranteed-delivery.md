# [ADR-004] Outbox pattern for guaranteed delivery

| Field | Value |
|-------|-------|
| **ID** | ADR-004 |
| **Category** | decision |
| **Status** | **superseded by [ADR-018](ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md) (2026-05-03)** |
| **Sprint** | Sprint 1 (approved); never implemented |
| **Date** | 2026-01-15 |
| **Domains** | Infrastructure, API Integration |
| **Tags** | outbox-pattern, delivery-guarantee, integration, superseded |

> **Supersession note (2026-05-03):** This ADR was approved in Sprint 1 but never implemented. The `outbox_events` table specified here was not added to `init.sql`; per-event delivery to integrations actually happens via direct HTTP calls in S5+ payroll/external integration code, not through an outbox processor. ADR-018 implements the original commitment AND adds state-change-to-event-store atomicity (the residual partial-failure window S21 cycle-2 surfaced), which ADR-004 did not address. Treat ADR-018 as the authoritative outbox design; ADR-004 remains for historical record only.

## Context
The system must send events to external integrations (payroll, external APIs) reliably. If an event is persisted but the integration call fails, the event must not be lost. Conversely, if the integration succeeds but the database transaction fails, the event must not be sent.

## Decision
Use the transactional outbox pattern: events are written to an outbox table within the same database transaction as the domain event. A separate processor reads the outbox and dispatches to external systems with at-least-once delivery semantics.

## Rationale
- Ensures atomicity between event persistence and outbound dispatch intent
- Avoids distributed transaction complexity (no 2PC)
- Supports retry and idempotency patterns
- Aligns with Priority #5 (integration isolation and delivery guarantees)

## Consequences
- An `outbox` table must exist in PostgreSQL alongside event tables
- A background processor must poll or subscribe to the outbox
- External integrations must handle duplicate deliveries (idempotency)
- Outbox entries must track delivery status (pending, delivered, failed)

## Agent Guidance
- **API Integration Agent**: All outbound messages must go through the outbox. Never send directly from domain event handlers.
- **Payroll Integration Agent**: Payroll exports use the same outbox mechanism for delivery guarantees.
- **Infrastructure agents**: The outbox processor is an infrastructure concern — keep it separate from domain logic.
