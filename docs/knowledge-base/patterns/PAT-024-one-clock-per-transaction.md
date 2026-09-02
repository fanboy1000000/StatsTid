# PAT-024 — One clock per transaction

| Field | Value |
|-------|-------|
| **ID** | PAT-024 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S137 (TASK-13708) |
| **Domains** | Backend, Infrastructure, any multi-row / row-plus-event write |
| **Tags** | transaction, utcnow, midnight, parity, atomic-outbox, adr-018, adr-040, audit |
| **Origin** | `AdminEndpoints.cs` POST user-create — three independent `DateOnly.FromDateTime(DateTime.UtcNow)` reads in one transaction; TASK-13708 unified two of them |

## The problem (plain language)

When one operation writes several rows (and emits an event) that must all carry "today", it is tempting
to read the clock at each write site. Those reads can straddle midnight: the users row says the hire
date is the 1st, the profile row says its history starts on the 2nd, and the event says something else.
Nothing fails; the records simply disagree, and the disagreement is discovered months later by a replay
or an audit that expects them to match. In S137 this mattered concretely: the hire date and the first
profile row's `effective_from` MUST be the same day so the planner sees one employment edge, not an
unexplained profile boundary.

## The pattern

Compute the operation's date ONCE, before the transaction opens, and thread that single variable into
every row and every event payload the transaction produces. Never read the clock twice for values that
are supposed to be "the same moment". If the operation legitimately needs two different dates, name them
separately and say why.

## Agent Guidance

- Grep a handler for `UtcNow` before editing it; more than one read in the same transaction is a smell.
- Pairs with ADR-018 D3 (atomic outbox — row and event must agree) and ADR-040 (window edges vs profile
  boundaries — dates that define segments must coincide exactly).
- Tests: assert row/row and row/event equality on the same snapshot (one JOIN), not "within a day".
