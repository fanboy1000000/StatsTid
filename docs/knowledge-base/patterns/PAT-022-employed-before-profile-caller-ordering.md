# PAT-022 — Ask "employed?" before "which profile?" (ADR-040 D10 caller ordering)

| Field | Value |
|-------|-------|
| **ID** | PAT-022 |
| **Category** | pattern |
| **Status** | approved |
| **Sprint** | S137 (time-control Increment 2) |
| **Domains** | Payroll, Backend, SharedKernel (Segmentation), Test |
| **Tags** | adr-040, d10, employment-window, typed-segments, not-employed, short-circuit, flex-carry, compliance, profile-resolver, pre-hire-500 |
| **Origin** | TASK-13702 (`PeriodCalculationService` per-segment loop) + TASK-13703 (`ComplianceEndpoints` month read); proposed by both agents independently |

## The problem (plain language)

Since Sprint 136 an employee's profile history starts at their hire date. Any code that asks "what
profile applied on date X?" for a date BEFORE the hire gets the resolver's honest answer — *no row* —
and, on the fail-closed paths (payroll calculation, the compliance check), that answer becomes an HTTP
500. Before Sprint 136 nobody noticed, because profile rows started at the beginning of time. The
question itself is the bug: for a day the person was not employed there is no profile to ask about.

## The pattern

**Consult the employment window FIRST, and only resolve a profile for days inside it.** Two shapes:

1. **Segmented calculation (the payroll planner).** The planner types every segment EMPLOYED or
   NOT_EMPLOYED from the resolver-supplied windows (`IEmploymentWindowResolver.GetWindowsAsync`).
   In the per-segment loop, the skip branch is the **FIRST statement** — before any
   `IEmploymentProfileResolver` call, before any rule-engine call, before any export mapping. Three
   rules keep the skip clean:
   - a skipped segment contributes an **EMPTY** result list and an **EMPTY** export-line list at its
     own index — never a synthesized zero row (the merge step seeds rule order from the first
     non-empty segment, and a fake `FLEX_BALANCE` row would corrupt the flex carry);
   - the flex carry passes through untouched (an empty list yields a null delta);
   - any "all evaluations failed" denominator is `budget(first EVALUATED segment) × evaluatedSegmentCount`,
     never `× plan.Segments.Count` — otherwise a skipped segment 0 zeroes the budget and a skipped
     middle segment over-states it, and a calculation whose every real call failed returns Success.
2. **Month-scoped reads (the compliance check, and any future month endpoint).** Read the windows
   over the month, take the **union** of (window ∩ month), and resolve the profile at the union's
   FIRST employed day (spells-proof: a future list of spells needs no consumer change). If the
   union is EMPTY, return the endpoint's **existing wire shape** with nothing in it ("nothing to
   check") and make NO resolver / rule-engine / projection call. The check's geometry stays
   whole-month; only the profile as-of date and the empty short-circuit change.

## Why not fix the resolver instead?

`IEmploymentProfileResolver`'s ADR-023 D3 contract — *null on no covering row; fail-loud when a
profile row has no agreement row* — is a data-integrity assertion and stays. Making the resolver
"tolerant" would hide seeding bugs. Ordering the questions correctly costs the caller one read and
keeps every contract honest.

## Agent Guidance

- Adding a new "skip this segment" condition to the payroll loop? Re-check the three rules above
  and run `EmploymentWindowSegmentSkipTests`.
- Adding a month-scoped employee read? Follow shape 2; never widen the wire DTO with employment
  dates (ADR-040 D7).
- Unit-testing `PeriodCalculationService` without Postgres: reuse
  `tests/StatsTid.Tests.Unit/Payroll/EmploymentWindowPcsFixture.cs` (recording rule-engine stub,
  in-memory `IEventStore`, counting profile resolver, fake window resolver). It CANNOT produce
  export lines or non-zero flex deltas — `PayrollMappingService` is sealed and DB-backed — those
  pins belong in Docker-gated regression tests.
