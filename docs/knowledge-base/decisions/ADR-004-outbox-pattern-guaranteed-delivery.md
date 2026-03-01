# [ADR-004] Outbox pattern for guaranteed delivery

| Field | Value |
|-------|-------|
| **ID** | ADR-004 |
| **Category** | decision |
| **Status** | approved |
| **Sprint** | Sprint 1 |
| **Date** | 2026-01-15 |
| **Domains** | Infrastructure, API Integration |
| **Tags** | outbox-pattern, delivery-guarantee, integration |

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
