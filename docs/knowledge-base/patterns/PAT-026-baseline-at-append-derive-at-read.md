# PAT-026 — Baseline-at-append, derive-at-read (an honest diagnostic list over mutable upstream facts)

| Field | Value |
|-------|-------|
| **ID** | PAT-026 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S138 (TASK-13803 — the HR backdate diagnostic worklist) |
| **Domains** | Backend, Infrastructure, Data Model |
| **Tags** | worklist, diagnostic, derived-state, content-hash, settlement-sequence, adr-013, adr-033, adr-034, jsonb |
| **Origin** | `src/Infrastructure/StatsTid.Infrastructure/HrBackdateWorklistRepository.cs` (`BackdateWorklistDerivation`); table `hr_backdate_worklist` |

## The problem (plain language)

A diagnostic list ("these exported months are now stale") is only useful if it is HONEST about whether each
item has been dealt with. The tempting design stores the operator's verb ("RECALCULATED") as the truth. But
the upstream fact — payroll's export record, a holiday year's settlement — can move without the operator
touching the list, or the operator can click "recalculated" without payroll having changed. And when a
second correction lands on a month that is already listed, a single scalar "trigger" on the row loses the
second trigger's kind and date, so a per-trigger diagnosis (this trigger's recalculation is blocked by
QUAL-150; that one is fine) becomes impossible.

## The pattern

1. **Capture the upstream fact's value PER TRIGGER at append time.** Each trigger appended to a row carries
   the fact as it was when THAT trigger landed — the export record's `content_hash` for an exported-month
   row, the active settlement's `sequence` + state for a settled-year row. Never inherit the first trigger's
   baseline: a correction between two appends would otherwise make the later trigger look already handled.
2. **Derive "has it moved since?" at READ time against the live value.** `recalculatedSince` = the export
   record's current hash ≠ the trigger's baseline (corrections advance the hash in place, so no timestamp is
   needed); `reversedSince` = the highest current settlement sequence exceeds the baseline OR the baseline
   row's state is now REVERSED (true after both the correct reverse-then-re-settle and a bare reversal). The
   row-level flag is the CONJUNCTION over triggers, so a late trigger keeps the row honest.
3. **Record the operator's verb ALONGSIDE, never INSTEAD.** `resolution` (RECALCULATED / DISMISSED) with a
   reason and an If-Match-guarded write is the human's statement; the derived flags are the system's. A reader
   sees both and can tell "dismissed but payroll never moved" from "recalculated and the hash proves it".
4. **Triggers are a set, not a scalar** (`triggers JSONB` array of `{kind, eventId, effectiveFrom, appendedAt,
   actorId, baseline…}`), with the row keyed on the thing it is ABOUT (one open row per exported month, one
   per settled year — partial UNIQUE `WHERE resolved_at IS NULL`) and insert-or-append on conflict.

## What was given up, and why

- A foreign key into the Payroll-owned `payroll_export_records` — a Backend table must not hold Payroll's
  future DDL hostage (ADR-034); the export id is a REFERENCE column, and the hash comparison is what ties them.
- Storing derived flags — they would go stale the moment the upstream moved; deriving costs one read of the
  export record / settlement rows per listed row, which is cheap at HR-list scale.

## Agent Guidance

- Any list about facts owned elsewhere (exports, settlements, approvals) follows this shape: baseline per
  trigger at append, derive at read, record the verb alongside.
- When a row can accumulate causes, model them as a set from day one; retrofitting a scalar is a migration.
