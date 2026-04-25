# Sprint 20 — Temporal Period Handling

| Field | Value |
|-------|-------|
| **Sprint** | 20 |
| **Status** | planned (analysis-first — no task log until Step 0 analysis is complete) |
| **Start Date** | TBD |
| **End Date** | TBD |
| **Orchestrator Approved** | no |
| **Build Verified** | n/a |
| **Test Verified** | n/a |

## Sprint Goal

Solve the general class of "calculation periods that don't align with the effective-date boundaries of the configuration they consume" — using the OK24→OK26 transition as the driving case, but producing an architecture that handles agreement-config activation, position-override effective dates, wage-type revisions, compliance-rule versioning, and employee-profile changes uniformly.

**This sprint begins with architectural analysis. No implementation tasks are listed. The first sprint activity is to produce an ADR and a task decomposition; implementation tasks are drafted only after that analysis is Orchestrator-approved.**

## Problem Statement

### The core problem

StatsTid calculates pay, balances, compliance, and entitlements over **periods** — weekly norm windows, monthly payroll runs, quarterly supplements, annual academic norms. Every rule in the engine operates on a (period, profile, config) triple and assumes each of those three is internally consistent for the full span of the period.

That assumption breaks whenever a period intersects an **effective-date boundary** in any of its inputs:

- The OK collective agreement changes on 2026-04-01. A monthly period covering 2026-03-30 → 2026-04-30 straddles OK24 and OK26.
- A `DRAFT → ACTIVE` agreement config promotion (ADR-014) takes effect mid-period.
- A position-override policy (ADR-013, S11/S14) has `effective_from = 2026-06-15`. A weekly period containing that date sees two different rule sets.
- A wage-type-mapping revision (S14) lands mid-month.
- An employee switches agreement code, hours, or position mid-period.
- EU WTD compliance rules (ADR-015, S16) are themselves versioned and may be tightened on a specific date.
- Entitlement policy changes (S15) take effect on a policy date that rarely aligns with the employee's ferieår.

Today, when a period spans one of these boundaries, the system exhibits **three failure modes**, depending on which code path is exercised:

1. **Silent version pinning.** `PeriodCalculationService.CalculateAsync` resolves the OK version from `periodStart` only, logs a warning if individual entries disagree, and proceeds — producing output pinned to the period-start version. A 2026-03-30 → 2026-04-30 period silently computes the last 30 days under OK24 rules. TASK-1801 (S18) closed the caller-trust vector but intentionally did not change this period-pinning behaviour.
2. **Caller-responsibility asymmetry.** `RetroactiveCorrectionService.RecalculateWithVersionSplitAsync` *does* split at the OK transition — but only for retroactive corrections. The normal forward-calc path doesn't split. Callers are implicitly expected to split, but the contract is undocumented and unenforced.
3. **Naive segmentation would be wrong.** Even if the service segmented automatically, splitting a weekly 37h norm across a Wednesday transition produces two partial-window evaluations whose results are meaningless. Rules are not segmentation-safe by construction.

### Why this is a general class, not a one-off

Every temporal input to the calculation engine has this shape:

| Boundary source | Current effective-date mechanism | Used by |
|-----------------|----------------------------------|---------|
| OK version | `OkVersionResolver` (SharedKernel, S18) | rule resolution, wage-type lookup |
| Agreement config | `agreement_configs.status` + `active_from` (ADR-014) | `ConfigResolutionService` |
| Position override | `position_override_configs.effective_from` (S14) | `ConfigResolutionService` |
| Wage-type mapping | implicit — current row wins (no effective-dating today) | `PayrollMappingService` |
| EU WTD compliance ruleset | `AgreementRuleConfig` (implicit) | `RestPeriodRule` |
| Entitlement policy | `entitlement_config` (current row wins) | `EntitlementValidator` |
| Employee profile | no effective-dating — last-write-wins on `employees` row | all rule inputs |

The OK version has the most visible failure mode because it flips globally every 2 years. Every other source above has the **same architectural shape** and therefore the same latent failure mode, but fires less predictably. Solving "OK version only" with a special case (as TASK-1801 did) works tactically; solving the general class once is the correct long-term move.

### Why silent pinning is a legal correctness issue, not a cosmetic one

Payroll in the Danish state sector is legally binding under the relevant collective agreement. Supplement rates, overtime thresholds, absence pay factors, pension accrual rates, and compensatory-rest entitlements differ between OK versions and between agreement-config activations. Silent pinning computes the wrong monetary figure on every entry after the transition in a straddling period. The error is small per entry but accumulates across the workforce and is discoverable by employee audit — a real reputational and compliance risk.

P4 (version correctness) in CLAUDE.md states this invariant explicitly. The period-pinning behaviour violates P4 in the specific case of cross-boundary periods. This is the highest-priority correctness gap remaining after Sprint 18.

## Context and Existing Partial Solutions

Work already in place that a proper solution must build on or reconcile with:

- **`src/SharedKernel/StatsTid.SharedKernel/Calendar/OkVersionResolver.cs`** — date→version resolver. Extracted from three duplicate copies in S18 (Sprint 18 TASK-1801 / Orchestrator follow-up). Canonical.
- **`RetroactiveCorrectionService.RecalculateWithVersionSplitAsync`** (in `src/Integrations/StatsTid.Integrations.Payroll/Services/`) — already splits a period at the OK transition and runs calculation per segment for retroactive corrections. The pattern exists; it's not generalized.
- **`PeriodCalculationService.CalculateAsync`** — logs per-entry/per-absence drift warnings (TASK-1801 addition) but does not act on them. Needs to become the central segmented caller or be retired in favour of whatever replaces it.
- **`ConfigResolutionService`** — already has "DB → Position Override → Local Override" precedence and respects `effective_from` on a single lookup. Not date-aware within a period span.
- **ADR-013 (Retroactive Corrections, No Cascade)** — establishes the no-cascade rule for retroactive OK splits. Any new segmentation architecture must preserve this.
- **ADR-014 (DB-Backed Agreement Configs)** — establishes the DRAFT/ACTIVE/ARCHIVED lifecycle; a new segmenter must consume `active_from` correctly.
- **ADR-015 (Compliance Rule Classification)** — precedent for "rules carry metadata about how they behave." A temporal-locality classification follows the same pattern.
- **PAT-005 (PeriodCalculationService HTTP)** — forbids direct function calls from Payroll into RuleEngine. Any segmenter that calls the rule engine per-segment must still go via HTTP.
- **PAT-006 (Unified CalculationResult)** — all rules return `CalculationResult`-compatible shapes. Merging split results across segments is a `CalculationResult` merge, not a per-rule merge.

## The Rule Classification Sub-Problem

The naive "split the period, run rules twice, concatenate results" approach is wrong because rules have different temporal locality. Before any implementation is drafted, rules must be classified:

- **Per-entry rules** — operate independently on each `TimeEntry` / `AbsenceEntry`. Splitting the period at any date is safe.
  - Examples: `SupplementCalcRule`, `OnCallDutyRule`, `TravelTimeRule`, `CallInWorkRule`.

- **Window-local rules** — operate on a bounded window (typically weekly or daily) that is conceptually independent of the calculation period. Splitting a window mid-span produces garbage. A split must either align with window boundaries or the window must be evaluated on the union of segments using some "dominant" configuration.
  - Examples: `NormCheckRule` (37h weekly + 4-week multi-week + annual), `OvertimeCalcRule` (weekly threshold), `RestPeriodRule` (daily 11h + weekly rest).

- **Period-aggregate rules** — accumulate facts across the full period and produce a single result. Splitting forces a merge decision per rule: sum, max, "first-wins", "last-wins", or "reject if split is detected."
  - Examples: `EntitlementValidator` (annual quotas), `OvertimeGovernanceRule` (period-level threshold), multi-week norm averaging.

- **Cross-cutting rules** — operate on the raw event timeline, independent of segmentation.
  - Examples: audit-trail emission, compliance-warning surfacing (ADR-015).

Without this classification, any segmentation architecture will produce silent miscomputation in some subset of rules. **Classification is a prerequisite, not an incidental detail.**

## Legal & Correctness Constraints (must not regress)

1. **P2 — Rule engine purity.** Rules remain pure functions. Segmentation coordinates rule invocation; it does not push state into rules.
2. **P3 — Event-sourcing determinism.** Same (events, configuration at timestamp) must always produce the same output. Segmentation must be deterministic and reproducible from the event store.
3. **P4 — OK version correctness.** Every entry is computed under the OK version that applied on its date. No silent pinning.
4. **P6 — Payroll traceability.** SLS export continues to carry full `SourceRuleId` + `SourceTimeType` traceability (PAT-005). Segmentation must not collapse information.
5. **ADR-013 — No cascade for retroactive corrections.** A retroactive split at the OK boundary does not trigger downstream recomputation beyond the affected period.
6. **Audit continuity.** Every segmented calculation is auditable end-to-end: which segments, which configs per segment, which merge rule was applied per result.

## Open Architectural Questions (to answer at sprint start)

These must be resolved — and documented in an ADR — **before** a task decomposition is drafted:

1. **Where does segmentation logic live?** SharedKernel (pure, callable from any service)? Rule Engine (inside the pure calculation boundary)? A new coordination service like `PeriodPlanner`? Or pushed out to the caller (orchestrator / `/calculate-and-export`) with a strict single-segment contract on `PeriodCalculationService`?
2. **How do rules declare temporal locality?** Attribute on the rule class, metadata on the rule definition in a registry, convention by naming, or explicit classification table separate from the rule?
3. **What merge semantics apply per rule class?** Per-class merge function vs per-rule merge override. Does `CalculationResult` grow a `merge(other: CalculationResult)` contract, or does merging happen outside the result shape?
4. **Reject vs degrade on invalid segmentation.** If a window-local rule would be evaluated on a partial window, does the service (a) reject the period with 400 and force the caller to present aligned windows, (b) evaluate on the whole span using a designated "primary" segment's config with audit annotation, or (c) a per-rule choice?
5. **Scope of this sprint's implementation.** Does it cover all temporal boundaries listed in the "boundary source" table, or just OK version plus an extensible framework for the rest? (Recommendation: framework + OK, with agreement-config and position-override as fast-follow tasks if time permits.)
6. **Backward compatibility with `RecalculateWithVersionSplitAsync`.** Retire in favour of the new planner, adapt it to use the planner under the hood, or leave both and document the overlap?
7. **Migration for OK26→OK28 (2028).** Is the answer an operational runbook or an architectural guarantee? If architectural, does the planner need to handle N>2 segments today even though today's data only needs 2?
8. **Performance budget.** What overhead is acceptable on the happy path (non-straddling periods, which are ≥99% of calls)? Answer informs whether the planner is always invoked or lazily used.
9. **Where is drift detected and surfaced?** If a caller does the wrong thing, what prevents it from reaching the rule engine? Build-time type safety, runtime contract enforcement, or both?
10. **Test strategy.** A full matrix (every rule × every segmentation scenario × every boundary type) is large. What subset is the sprint-committed regression coverage, and what is deferred as exhaustive-testing debt?

## Scope Boundary

### In scope
- Architecture & ADR for temporal segmentation applicable to all boundary sources in the table above.
- Rule temporal-locality classification scheme, applied to all existing rules.
- Implementation covering at least the OK version boundary end-to-end, with extension points demonstrated.
- Migration of `PeriodCalculationService` to the new segmentation model.
- Regression tests covering the committed subset of the rule × scenario matrix.

### Out of scope
- Rewriting any rule's internal logic. This sprint changes coordination and metadata, not calculation semantics.
- Rewriting the event store or replay mechanism.
- Moving to full effective-dated configuration for every config table (if chosen as a follow-up, it is a later sprint).
- Retiring event sourcing, CQRS-lite, or the pure rule engine.
- UI changes beyond surfacing any new audit information the architecture requires.

## Planning Entrypoint

No implementation tasks are defined yet. The sprint begins with the following **analysis-phase deliverables**, produced by the Orchestrator in collaboration with the user before any domain agent is spawned:

1. **Architectural ADR** — answering the ten open questions above, proposing segmentation placement, rule classification, and merge semantics.
2. **Rule classification inventory** — every existing rule tagged with its temporal locality class.
3. **Task decomposition** — the ADR translated into `TASK-20NN` entries with domain agents, file scopes, and validation criteria, added to this sprint log under "## Task Log".
4. **Entropy scan (Step 0a)** — run at the actual sprint start date, recorded in the header above.

Only after items 1–3 are Orchestrator-approved does Step 2 (Delegate) begin.

## References

- [CLAUDE.md](../../CLAUDE.md) — priority order (P2, P4 driving this sprint)
- [ROADMAP.md](../../ROADMAP.md) — Phase 3i placement
- [docs/knowledge-base/decisions/ADR-003-ok-version-resolved-by-entry-date.md](../knowledge-base/decisions/ADR-003-ok-version-resolved-by-entry-date.md)
- [docs/knowledge-base/decisions/ADR-013-retroactive-corrections-no-cascade.md](../knowledge-base/decisions/ADR-013-retroactive-corrections-no-cascade.md)
- [docs/knowledge-base/decisions/ADR-014-db-backed-agreement-configs.md](../knowledge-base/decisions/ADR-014-db-backed-agreement-configs.md)
- [docs/knowledge-base/decisions/ADR-015-compliance-rule-classification.md](../knowledge-base/decisions/ADR-015-compliance-rule-classification.md)
- [docs/knowledge-base/patterns/PAT-005-period-calculation-http.md](../knowledge-base/patterns/PAT-005-period-calculation-http.md)
- [docs/knowledge-base/patterns/PAT-006-unified-calculation-result.md](../knowledge-base/patterns/PAT-006-unified-calculation-result.md)
- [SPRINT-18.md](SPRINT-18.md) — TASK-1801 closed the caller-trust vector; this sprint closes the period-pinning vector it intentionally left behind.
