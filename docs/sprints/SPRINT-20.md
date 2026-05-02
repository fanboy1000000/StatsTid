# Sprint 20 — Temporal Period Handling

| Field | Value |
|-------|-------|
| **Sprint** | 20 |
| **Status** | analysis-phase complete (Step 0a, Step 0b, ADR-016, classification inventory, task decomposition all user-approved 2026-04-29); ready for Step 2 (Delegate) |
| **Start Date** | 2026-04-28 |
| **End Date** | TBD |
| **Orchestrator Approved** | yes (analysis-phase deliverables 1–3 approved 2026-04-29; implementation begins at Step 2) |
| **Build Verified** | n/a (no implementation yet) |
| **Test Verified** | n/a (no implementation yet) |

## Sprint Goal

Solve the general class of "calculation periods that don't align with the effective-date boundaries of the configuration they consume" — using the OK24→OK26 transition as the driving case, but producing an architecture that handles agreement-config activation, position-override effective dates, wage-type revisions, compliance-rule versioning, and employee-profile changes uniformly.

**This sprint begins with architectural analysis. No implementation tasks are listed. The first sprint activity is to produce an ADR and a task decomposition; implementation tasks are drafted only after that analysis is Orchestrator-approved.**

## Entropy Scan Findings

_Sprint 20 Step 0a, 2026-04-28._

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | No KB entries reference paths that moved in b4fc670; `docs/SECURITY.md:82` referenced the old `Infrastructure/Security/ActorContext.cs` path — fixed pre-sprint. `docs/sprints/SPRINT-6/7/9/18/19.md` and `docs/reviews/codex-2026-04-18.md` retain old paths but are point-in-time historical records and not updated. |
| Pattern compliance spot-check | CLEAN | (a) PAT-005 (RuleEngine HTTP-only): CLEAN — no `using StatsTid.RuleEngine` from non-RuleEngine code. (b) FAIL-001 (`FindFirst("scopes")`): CLEAN. (c) Hardcoded `http://localhost`: CLEAN — only in `launchSettings.json`. (d) `RequireAuthorization` coverage: 92 endpoints, 86 calls (5 unauthenticated `/health` + 1 `/login` accounted for; same as S19). (e) **DEBT carried — RESOLVED**: S18 Codex Rec #2 (RuleEngine.Api/Program.cs `using StatsTid.Infrastructure`) closed by post-S19 commit b4fc670 — RuleEngine now references SharedKernel + Auth only. |
| Orphan detection | CLEAN | S19 + post-S19 new files (`OrchestratorScopeHelpers`, `OkVersionCanonicalization`, `CalculateAndExportScopeTests`, `OrchestratorScopeEnforcementTests`, `OkVersionCanonicalizationTests`, the 7 files in `src/Auth/StatsTid.Auth/`) all referenced. |
| Documentation drift | DRIFT — fixed | (a) `docs/SECURITY.md:82` referenced the moved `ActorContext.cs` path — fixed in this scan. (b) `docs/ARCHITECTURE.md` had no entry for the new `StatsTid.Auth` bounded context and Hard Rule #1 said "Rule Engine depends ONLY on SharedKernel" — both updated to reflect the post-b4fc670 assembly graph. MEMORY.md deferred items list inspected — no items completed by S19 work that should be removed. |
| Quality grade review | deferred | No domain quality changes since S19 — existing grades hold. Will reassess at S20 sprint end after analysis deliverables land. |

## Plan Review (Step 0b)

_First sprint to use Step 0b (added in commit 50808fc). Trigger: MANDATORY — plan touches P3 (auditability), P4 (OK-version correctness; primary focus), P6 (payroll integration). Both lenses ran in parallel against this plan on 2026-04-28._

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P3 + P4 + P6) |
| **External Codex** | invoked 2026-04-28 — cycle 1, 4 BLOCKER + 4 WARNING + 2 NOTE |
| **Internal Reviewer** | invoked 2026-04-28 — cycle 1, 0 BLOCKER + 3 WARNING + 3 NOTE |
| **BLOCKERs resolved before Step 1** | yes (2026-04-28) — all 4 BLOCKERs + all 7 WARNINGs + 4 of 5 NOTEs applied; see Resolution below |

### Findings (cycle 1)

**External Codex (BLOCKER):**
- **B1 — Rule taxonomy not disjoint or exhaustive** (Rule Classification Sub-Problem / Q2–Q4): Four classes overlap — `NormCheckRule` spans weekly/4-week/annual; compliance rules return `ComplianceCheckResult` not `CalculationResult`; `FlexBalanceRule` has carry-in state; "cross-cutting" is a different dimension than temporal locality. Recommended fix: split into multi-axis taxonomy: `evaluation span` (entry/window/period/cross-period) × `split behavior` (segment-safe / aligned-window / mergeable / reject) + a separate `result family` axis for payroll vs compliance.
- **B2 — Q4 prose pre-commits to a bad design**: Plan's "must either align with window boundaries or use dominant/primary config" excludes the valid third option of rejecting the period and forcing caller alignment. Recommended fix: rewrite as problem statement only; keep `reject` as a first-class option in the ADR.
- **B3 — Boundary-source uniformity claim is unsupportable**: Plan promises a uniform architecture for employee-profile / entitlement / wage-type-mapping, but the table itself notes those sources have NO effective-dating today (`last-write-wins` / `current row wins`). Deterministic replay (P3) is impossible without a historical timeline. Recommended fix: narrow S20 to sources with auditable effective dating now (OK version, agreement-config, position-override), and add an explicit ADR question for non-dated sources: "unsupported, snapshot-at-calculation, or introduce versioned history first?"
- **B4 — Audit representation gap**: Plan says segmentation must be reproducible and auditable but never asks HOW a segmented run is represented as ONE auditable artifact. Recommended fix: add an open question on a "segment manifest": segment boundaries, boundary cause, config/profile/version IDs per segment, per-rule merge decisions, stable linkage back to the single exported result.

**External Codex (WARNING):**
- **W1 — KB citations are materially wrong**: ADR-015 is about `ComplianceCheckResult` as a separate return type, NOT "compliance rule classification." PAT-006 does NOT say "all rules return CalculationResult-compatible shapes" because ADR-015 explicitly carves compliance out. Some referenced KB filenames at the end of the plan do not match files on disk. Recommended fix: correct the citations; stop using ADR-015 as taxonomy precedent; plan to add a new S20-produced KB entry for temporal-locality classification.
- **W2 — Migration scope too broad**: "Migration of PeriodCalculationService to the new segmentation model" reads like a whole-sprint migration. Recommended fix: scope to "introduce planner + route OK-boundary calls through it for named entry points"; defer full retirement of `PeriodCalculationService` until the architecture proves out.
- **W3 — TASK-1903 risks pulling in wage-type effective-dating**: Per-line OK-version stamping is only safely bounded if it reuses already-resolved segment metadata. As written, it could leak into wage-type effective-dating. Recommended fix: state explicitly that S20 stamps export lines from segment-resolved OK/config context only; wage-type-mapping effective-dating stays out of scope unless separately versioned.
- **W4 — Q10 test matrix is too loose**: Minimum committed matrix not bounded tightly. Recommended fix: commit at least: one rule per locality/split-behavior class, one valid + one invalid boundary scenario, one deterministic replay case, one merged-audit case, the mixed-version export case.

**External Codex (NOTE):**
- **N1 — Time-box analysis-first**: Justified, but add explicit exit criteria and a timebox for Step 0b so the sprint doesn't become open-ended architecture work.
- **N2 — Convert Q7 framing**: From "is N>2 needed?" to "how is arbitrary segment count represented and tested?" If the design is a general framework, it must support arbitrary N from day one.

**Internal Reviewer (WARNING):**
- **R-W1 — Q6 has a non-viable option**: "Leave both `RecalculateWithVersionSplitAsync` and the new planner" is a pseudo-option once classification work lands — determinism (P3) requires ONE canonical segmenter, otherwise retroactive corrections diverge from forward-calc on the same boundary. Recommended fix: edit Q6 to two options (retire after migration / adapt to thin-wrap). Add constraint: ADR-013 no-cascade semantics must move INTO the new planner as a merge-policy choice; they may not remain duplicated.
- **R-W2 — Architectural-shape claim overstated** (converges with Codex B3): Add a column to the boundary-source table: "Effective-dating mechanism status" (implemented / schema-change-required / no-mechanism). Narrow in-scope to sources with mechanism already in place.
- **R-W3 — Q1 should commit at plan time on segmentation owner**: PAT-005 + PAT-006 already eliminate two of four placement options (cannot live inside RuleEngine because callers are HTTP-only; "pushed to caller" duplicates segmentation per ADR-013 asymmetry). Viable: SharedKernel pure helper consumed by Payroll's `PeriodCalculationService` vs new coordination service. Recommended fix: narrow Q1 to those two; pre-commit "not in RuleEngine assembly" as a closed constraint.

**Internal Reviewer (NOTE):**
- **R-N1 — Cite S18/S19 helper-extraction precedent**: Add `OkVersionCanonicalization` (S19 post-cleanup) to "Context and Existing Partial Solutions" as the testing-shape precedent — pure helper + thin service wrapper gave 8 unit-testable branches; same shape applies to per-rule merge contracts here.
- **R-N2 — Time-box analysis** (converges with Codex N1): "If items 1–3 are not Orchestrator+user approved within 3 calendar days of sprint start, halt and present a scope-reduction option."
- **R-N3 — TASK-1903 absorption is tight**: No fix needed; commendation only.

### Convergence Signal

Two findings appear in BOTH lenses, which is a strong correctness signal:
- **B3 + R-W2** — boundary-source table over-promises uniformity for non-dated sources
- **N1 + R-N2** — analysis-first sprint needs an explicit timebox

The plan as written would, at minimum, force a mid-sprint pivot once the rule-taxonomy assumption was tested against actual rule code (B1) or once the audit-representation question came up at export time (B4).

### Resolution

User-approved 2026-04-28 to apply all BLOCKER + WARNING fixes plus 4 of 5 NOTEs. R-N3 (TASK-1903 absorption is tight) was a commendation only; no edit needed. Plan edits applied:

- **B1** (rule taxonomy not disjoint) — "## The Rule Classification Sub-Problem" rewritten with multi-axis taxonomy (`span × split-behavior × result-family`), each axis explicitly defined with examples and a worked-deliverable description.
- **B2** (Q4 prose locked in degrade-by-default) — Q4 rewritten as problem statement with four behavior options (a–d); `reject` is first-class. Original "must align with window OR dominant config" prose removed.
- **B3 + R-W2** (boundary uniformity overstated) — boundary-source table column renamed to "Effective-dating mechanism status" with `implemented` / `schema change required` / `no mechanism` values per source. "Why this is a general class" prose narrowed to the implemented rows; new Q5b added asking how non-dated sources behave under the framework.
- **B4** (audit representation gap) — new Q10 added committing the ADR to a "segment manifest" with explicit invariants (boundaries, cause, configs/profiles per segment, merge decisions, linkage to exported result).
- **W1** (KB citations wrong) — ADR-015 reframed as "ComplianceCheckResult as Separate Return Type" with the implication for the result-family axis; PAT-006 reframed as calculation-only with ADR-015's carve-out called out; broken filenames in References (`ADR-013-...-no-cascade`, `ADR-014-db-backed-...`, `PAT-005-period-calculation-http`, etc.) corrected.
- **W2** (migration scope too broad) — Scope Boundary "In scope" rewritten: introduce planner + route OK-boundary calls through it for named entry points; full retirement of `PeriodCalculationService` explicitly out of scope.
- **W3** (TASK-1903 wage-type leak risk) — TASK-1903 absorption bullet adds explicit bound: per-line stamping uses ONLY segment-resolved OK/config; wage-type-mapping effective-dating stays out and falls under Q5b if encountered mid-period.
- **W4** (Q10 test matrix loose) — Q11 (renumbered from Q10) commits the minimum matrix: one rule per `(span × split-behavior)` combination, valid+invalid scenario per implemented source, deterministic-replay case, merged-audit case, mixed-version export case.
- **R-W1** (Q6 pseudo-option) — "leave both" removed; Q6 is now `retire` or `adapt to thin-wrap`. ADR-013 no-cascade-must-move constraint added explicitly.
- **R-W3** (Q1 narrow now) — Q1 pre-commits "NOT in RuleEngine assembly" and "NOT pushed to caller" as closed constraints; remaining choice is SharedKernel pure planner vs. new coordination service.
- **N1 + R-N2** (timebox analysis) — exit criteria for "approved" added to Planning Entrypoint. The wall-clock "3 calendar days / 2026-05-01 EOD" framing originally accepted in cycle 1 was rejected on user feedback 2026-04-29 — it borrows a multi-developer-sprint coordination model that doesn't fit a solo, async project. Exit criteria (committed deliverables that demonstrate convergence) are the load-bearing protection; default scope-reduction option remains available, triggered by orchestrator+user judgment rather than elapsed days.
- **N2** (N>2 segments framing) — Q7 reframed from "is N>2 needed?" to "how is arbitrary segment count represented and tested?" — including merge associativity.
- **R-N1** (S19 helper precedent) — `OkVersionCanonicalization` added to "Context and Existing Partial Solutions" as the testing-shape precedent for per-rule merge contracts.

Step 0b cycle 1 closed. Plan is now ready for analysis-phase deliverables (ADR + classification + task decomposition).

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

### Why this is a general class — and where the analogy ends

Every temporal input to the calculation engine has the same shape only when there is a historical timeline to segment over. Without a per-row `effective_from` (or equivalent versioned history), there is nothing for a segmenter to consume — the source is "snapshot at calculation time" by construction. Step 0b plan review (2026-04-28) flagged this distinction; the table below makes it explicit:

| Boundary source | Effective-dating mechanism status | Used by |
|-----------------|-----------------------------------|---------|
| OK version | **implemented** — `OkVersionResolver` (SharedKernel, S18) | rule resolution, wage-type lookup |
| Agreement config | **implemented** — `agreement_configs.status` + `active_from` (ADR-014) | `ConfigResolutionService` |
| Position override | **implemented** — `position_override_configs.effective_from` (S14) | `ConfigResolutionService` |
| Wage-type mapping | **schema change required** — current row wins, no `effective_from` column | `PayrollMappingService` |
| EU WTD compliance ruleset | **implemented (implicit)** — `AgreementRuleConfig` per-version | `RestPeriodRule` |
| Entitlement policy | **schema change required** — current row wins | `EntitlementValidator` |
| Employee profile | **no mechanism** — last-write-wins on `employees` row, no historical timeline | all rule inputs |

S20's segmentation framework is sound for sources in the **implemented** rows. Sources in the **schema change required** rows can be added to the framework only after a separate effective-dating migration; the **no mechanism** row (employee profile) needs a deeper question answered first — see Open Question 5b.

The OK version has the most visible failure mode because it flips globally every 2 years and the mechanism already exists. The other implemented sources fire less predictably but share the same architecture. Solving "OK version only" tactically (as TASK-1801 did) works for one transition; solving the general class for the implemented rows is the correct long-term move. Treating non-implemented rows as if they were the same shape would bake an unsupportable determinism claim into the framework — Step 0b plan review BLOCKER B3.

### Why silent pinning is a legal correctness issue, not a cosmetic one

Payroll in the Danish state sector is legally binding under the relevant collective agreement. Supplement rates, overtime thresholds, absence pay factors, pension accrual rates, and compensatory-rest entitlements differ between OK versions and between agreement-config activations. Silent pinning computes the wrong monetary figure on every entry after the transition in a straddling period. The error is small per entry but accumulates across the workforce and is discoverable by employee audit — a real reputational and compliance risk.

P4 (version correctness) in CLAUDE.md states this invariant explicitly. The period-pinning behaviour violates P4 in the specific case of cross-boundary periods. This is the highest-priority correctness gap remaining after Sprint 18.

## Context and Existing Partial Solutions

Work already in place that a proper solution must build on or reconcile with:

- **`src/SharedKernel/StatsTid.SharedKernel/Calendar/OkVersionResolver.cs`** — date→version resolver. Extracted from three duplicate copies in S18 (Sprint 18 TASK-1801 / Orchestrator follow-up). Canonical.
- **`src/Integrations/StatsTid.Integrations.Payroll/Services/OkVersionCanonicalization.cs`** — pure helper extracted post-S19 (commit 9684dbf+) that resolves canonical OK version and reports caller-vs-resolver drift. Establishes the **testing-shape precedent** for S20: pure helper + thin service wrapper exposes per-branch behavior in unit tests (8 helper-branch tests in `tests/StatsTid.Tests.Unit/Payroll/OkVersionCanonicalizationTests.cs`). The same shape applies to per-rule merge contracts in this sprint — pin invariants on a pure surface, not on the service that calls it.
- **`RetroactiveCorrectionService.RecalculateWithVersionSplitAsync`** (in `src/Integrations/StatsTid.Integrations.Payroll/Services/`) — already splits a period at the OK transition and runs calculation per segment for retroactive corrections. The pattern exists; it's not generalized. Determinism (P3) requires that this code path NOT remain duplicated alongside the new planner — see Open Question 6.
- **`PeriodCalculationService.CalculateAsync`** — logs per-entry/per-absence drift warnings (TASK-1801 addition) but does not act on them. Becomes the central segmented caller for OK-boundary periods in S20; full retirement is out of scope (W2).
- **`ConfigResolutionService`** — already has "DB → Position Override → Local Override" precedence and respects `effective_from` on a single lookup. Not date-aware within a period span.
- **ADR-013 (Retroactive Corrections, Single Period, No Cascade)** — establishes the no-cascade rule for retroactive OK splits. Any new segmentation architecture must preserve this AND must not leave it duplicated in `RecalculateWithVersionSplitAsync` (Q6).
- **ADR-014 (DB-Backed Agreement Configs)** — establishes the DRAFT/ACTIVE/ARCHIVED lifecycle; a new segmenter must consume `active_from` correctly.
- **ADR-015 (`ComplianceCheckResult` as Separate Return Type)** — establishes that compliance rules return `ComplianceCheckResult`, NOT `CalculationResult`. Relevant to S20 because it forces the rule-classification taxonomy to carry a `result family` axis (payroll vs compliance) — they cannot be merged by a single result-type contract. Citation correction from Step 0b plan review (W1).
- **PAT-005 (`PeriodCalculationService` HTTP-only)** — forbids direct function calls from Payroll into RuleEngine. Any segmenter that calls the rule engine per-segment must still go via HTTP. Combined with PAT-006, this eliminates "segmentation logic inside RuleEngine" as a placement option (Q1).
- **PAT-006 (Unified Rule Endpoint Response Format)** — calculation-rule endpoints return `CalculationResult`-compatible shapes; ADR-015 carves compliance out into `ComplianceCheckResult`. Merging split results is therefore a per-result-family merge, not a single-shape `CalculationResult` merge. Citation correction from Step 0b plan review (W1).

## The Rule Classification Sub-Problem

The naive "split the period, run rules twice, concatenate results" approach is wrong because rules differ along multiple independent dimensions. Step 0b plan review (BLOCKER B1) flagged that a single-axis taxonomy (per-entry / window-local / period-aggregate / cross-cutting) is neither disjoint nor exhaustive: `NormCheckRule` operates in three modes (weekly / 4-week multi-week / annual); compliance rules return `ComplianceCheckResult` not `CalculationResult` (ADR-015); `FlexBalanceRule` carries forward state between periods. The taxonomy must be **multi-axis** before classification is meaningful.

The ADR produced by this sprint must classify each existing rule along three independent axes:

### Axis 1 — Evaluation span

Where the rule's "natural" computation window sits relative to the calculation period:

| Span | Definition | Example |
|------|------------|---------|
| **entry** | Per-`TimeEntry` / per-`AbsenceEntry`; no window aggregation | `SupplementCalcRule`, `OnCallDutyRule`, `TravelTimeRule`, `CallInWorkRule` |
| **window** | Bounded window inside the period (daily / weekly) | `RestPeriodRule` (daily 11h, weekly 48h), `NormCheckRule.Weekly`, `OvertimeCalcRule.Weekly` |
| **period** | Whole calculation period; produces one aggregated result | `OvertimeGovernanceRule`, `NormCheckRule.MultiWeek`, `NormCheckRule.Annual` |
| **cross-period** | State carries forward across period boundaries | `FlexBalanceRule` (carry-in flex), entitlement-balance accruals |

Multi-mode rules occupy multiple cells (e.g., `NormCheckRule` is window AND period). Classification records each mode separately.

### Axis 2 — Split behavior at a boundary

What MUST happen when a segmentation boundary falls inside the rule's evaluation span:

| Behavior | Definition |
|----------|------------|
| **segment-safe** | Run independently on each segment; concatenate results. Default for entry-span rules |
| **aligned-window** | Run only if the boundary aligns with the rule's natural window edge; otherwise reject or align upstream |
| **mergeable** | Run on each segment; combine results via a per-rule merge function (sum / max / first-wins / last-wins) |
| **reject** | Disallow segmentation entirely for this rule; require the caller to pre-align periods OR the planner to refuse the period with 4xx |

`reject` is a **first-class** option (Step 0b BLOCKER B2). The plan does NOT pre-commit to "evaluate union with dominant config" as the only fallback for window rules.

### Axis 3 — Result family

What kind of artifact the rule produces, since the result-type system already differs between calculation and compliance rules (ADR-015):

| Family | Return type | Merge contract |
|--------|-------------|----------------|
| **calculation** | `CalculationResult` (PAT-006) | Per-rule merge function over segments; line items concatenate |
| **compliance** | `ComplianceCheckResult` (ADR-015) | Violations and warnings union across segments; segments may produce duplicates that the merge must dedupe |

A single segmenter cannot collapse these into one path; the merge contract is family-specific.

### Classification deliverable

The S20 ADR includes a table assigning every existing rule (including each multi-mode rule's modes) a triple `(span, split-behavior, family)`. The triple drives:

- whether the planner can split the rule's window at all,
- which merge function applies after segmented evaluation,
- whether the caller must pre-align the period or accept a 4xx.

**Classification is a prerequisite, not an incidental detail. The plan does not commit to a specific assignment for any rule — that is the ADR's job.**

## Legal & Correctness Constraints (must not regress)

1. **P2 — Rule engine purity.** Rules remain pure functions. Segmentation coordinates rule invocation; it does not push state into rules.
2. **P3 — Event-sourcing determinism.** Same (events, configuration at timestamp) must always produce the same output. Segmentation must be deterministic and reproducible from the event store.
3. **P4 — OK version correctness.** Every entry is computed under the OK version that applied on its date. No silent pinning.
4. **P6 — Payroll traceability.** SLS export continues to carry full `SourceRuleId` + `SourceTimeType` traceability (PAT-005). Segmentation must not collapse information.
5. **ADR-013 — No cascade for retroactive corrections.** A retroactive split at the OK boundary does not trigger downstream recomputation beyond the affected period.
6. **Audit continuity.** Every segmented calculation is auditable end-to-end: which segments, which configs per segment, which merge rule was applied per result.

## Open Architectural Questions (to answer at sprint start)

These must be resolved — and documented in an ADR — **before** a task decomposition is drafted. Step 0b plan review (2026-04-28) narrowed several of these from the original framing; the closed pre-commits are noted explicitly so the ADR does not relitigate them.

1. **Where does segmentation logic live?**
   - **Closed pre-commit (Step 0b R-W3)**: NOT in the RuleEngine assembly. PAT-005 requires HTTP-only access from non-RuleEngine code, and PAT-006 + ADR-015 already require result-family-aware merging on the calling side.
   - **Closed pre-commit (Step 0b W2)**: The choice does NOT include "pushed to caller" — that duplicates segmentation per ADR-013-asymmetry across every entry point.
   - **Open**: SharedKernel pure planner (`Calendar/PeriodPlanner.cs`-style helper) consumed by `Payroll/Services/PeriodCalculationService`, OR a new coordination service that owns segmentation and is itself the HTTP-callable surface? Trade-off: pure helper preserves PAT-005 with no new service; coordination service is a clearer ownership boundary but adds a deployable.

2. **How do rules declare their `(span, split-behavior, family)` triple?** Attribute on the rule class, metadata on the rule definition in a registry, convention by naming, or explicit classification table separate from the rule?

3. **What merge semantics apply per result family and per rule?** Per-family merge function (one for `CalculationResult`, one for `ComplianceCheckResult`) with per-rule overrides? Does `CalculationResult` grow a `merge(other)` contract, or does merging happen outside the result shape via a planner-owned merger?

4. **Behavior at an invalid segmentation boundary.** If a window-local rule's window straddles a boundary, what choices does the planner support? Options to evaluate in the ADR (NOT pre-decided):
   - **(a) reject** — return 4xx and force the caller to present aligned periods;
   - **(b) align upstream** — round the period boundary to the nearest window edge before segmenting;
   - **(c) dominant-config evaluation** — evaluate on the union under one segment's config, with audit annotation;
   - **(d) per-rule choice** — the rule's classification triple selects the behavior.

   Reject MUST remain a first-class option in the ADR (Step 0b BLOCKER B2 — the original prose pre-committed to (b) or (c) only).

5. **Scope of this sprint's implementation.** Does it cover all temporal boundaries in the boundary-source table, or just OK version plus an extensible framework for the rest? Recommendation: framework + OK version end-to-end, with agreement-config and position-override as fast-follow tasks if time permits. Wage-type-mapping and entitlement-policy are excluded — see 5b.

   **5b. (Step 0b BLOCKER B3) Behavior for non-effective-dated boundary sources.** Wage-type-mapping, entitlement-policy, and employee-profile have no historical timeline today (last-write-wins / current row wins). Three options were considered:
   - **(a) unsupported** — planner refuses periods that span any change. Rejected: would refuse most real periods (every employee profile edit becomes a "spans a change" scenario).
   - **(b) snapshot-at-calculation** — take a snapshot of the current row at calculation time, embed in segment manifest. Re-run with same manifest → deterministic replay; recomputation without manifest → fresh snapshot, may differ. **Decided for S20** (2026-04-28).
   - **(c) introduce versioned history first** — schema migrations adding `effective_from` per source. Out of S20 scope (would expand 1 sprint to 1 + 3+).

   **Decision**: S20 implements (b). Each registered rule declares a `SnapshotContract` (which non-dated tables / profile fields it reads); the planner gathers those snapshots at calculation time and writes them to the segment manifest. (b) is honest about the tradeoff: replay determinism becomes a property we can claim and verify; recomputation determinism stays unclaimed (we never had it).

   **Long-term commitment** (option c, recorded in [ROADMAP.md](../../ROADMAP.md) Phase 4 on 2026-04-28): "Versioned History for Non-Dated Boundary Sources" tracks three sub-sprints (wage-type-mapping, entitlement-policy, employee-profile). Each source moves from (b) to a proper effective-dated lookup; existing manifests stay replayable. Hard dependency on S20's `SnapshotContract` + segment manifest being in place. Locking (c) into the roadmap now prevents (b) from quietly becoming permanent.

6. **Backward compatibility with `RecalculateWithVersionSplitAsync` (Step 0b R-W1).** Two options only:
   - **(a) retire** after migration once the new planner is the canonical segmenter;
   - **(b) adapt** to thin-wrap the new planner so retroactive corrections route through identical logic.

   "Leave both and document the overlap" is NOT a viable option — determinism (P3) requires ONE canonical segmenter, otherwise retroactive corrections diverge from forward-calc on the same boundary.

   **Constraint**: ADR-013's no-cascade semantics for retroactive corrections must move INTO the new planner as an explicit merge-policy choice; they may not remain duplicated.

7. **Arbitrary segment count (Step 0b NOTE N2).** How is a period with N>2 segments represented and tested? If the design is a general framework, it must support arbitrary N from day one — there is no "today's data only needs 2" exemption that wouldn't make this not-a-framework. Question covers: planner data model, merge associativity (does `merge(merge(a,b),c) == merge(a, merge(b,c))`?), and N>2 test scenarios.

8. **Performance budget.** What overhead is acceptable on the happy path (non-straddling periods, ≥99% of calls)? Answer informs whether the planner is always invoked or lazily used.

9. **Where is drift detected and surfaced?** If a caller does the wrong thing (wrong period bounds, wrong profile snapshot, manually pre-segmented input), what prevents it from reaching the rule engine? Build-time type safety, runtime contract enforcement, or both?

10. **Audit representation of a segmented run (Step 0b BLOCKER B4).** A segmented calculation must be one auditable artifact. The ADR must commit to a "segment manifest" or equivalent that captures: segment boundaries, boundary cause (OK transition / agreement-config promotion / position-override / etc.), the config + profile + version identifiers used per segment, the merge decisions per rule, and stable linkage back to the single exported result. This is a P3 / P6 invariant — without it, segmentation is auditable in implementation but not in artifact.

11. **Test strategy (Step 0b WARNING W4).** The committed minimum regression matrix is:
    - One rule per `(span × split-behavior)` combination that exists in the current rule set;
    - One valid boundary scenario AND one invalid boundary scenario per implemented boundary source;
    - One deterministic-replay case demonstrating that `(events, configuration manifest)` reproduces output;
    - One merged-audit case demonstrating that the segment manifest reconstructs the per-segment story;
    - The mixed-version export case (TASK-1903 absorption).

    The full rule × scenario × boundary matrix beyond this is exhaustive-testing debt deferred to a follow-up sprint.

## Scope Boundary

### In scope
- Architecture & ADR for temporal segmentation, applicable to the **implemented** rows of the boundary-source table (OK version, agreement-config, position-override, EU WTD compliance ruleset).
- Rule classification under the multi-axis taxonomy (`span × split-behavior × result-family`), applied to all existing rules.
- Implementation covering the **OK version boundary end-to-end**, with extension points demonstrated for other implemented boundary sources.
- **Introduce the planner and route OK-boundary calculations through it for named entry points** (`/calculate-and-export`, `/export-period`, `RetroactiveCorrectionService`). Full retirement / rewrite of `PeriodCalculationService` is OUT of scope (Step 0b WARNING W2) — the existing service routes through the planner; replacement is a follow-up.
- **Segment manifest** as a persisted audit artifact (Open Question 10) — a single auditable record per segmented calculation.
- **Mixed-version export boundary** (absorbed from Sprint 19's TASK-1903 on 2026-04-25): `OkVersionBoundary.ResolveProfile` in `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:325-339` currently collapses a multi-version `CalculationResult` to a single version. The new framework must produce per-line OK-version stamping at the export boundary so straddling periods export correctly.
   - **Bound (Step 0b WARNING W3)**: per-line stamping consumes ONLY segment-resolved OK / config context produced by the planner. Wage-type-mapping effective-dating is NOT in scope and must not leak in via this fix; if a wage-type-mapping change happens mid-period, it falls under Open Question 5b (non-dated source behavior).
   - The internal-Reviewer WARNING about `/calculate-and-export` not using the same boundary helper as `/export` / `/export-period` is part of the same fix surface.
- Regression tests covering the committed minimum matrix from Open Question 11, including the mixed-version export case that was originally TASK-1903.

### Out of scope
- Rewriting any rule's internal logic. This sprint changes coordination and metadata, not calculation semantics.
- Rewriting the event store or replay mechanism.
- Moving non-dated configuration sources (wage-type-mapping, entitlement-policy, employee-profile) to effective-dated history. Their behavior under the framework is decided by Open Question 5b but the schema migrations themselves are follow-up sprints.
- Full retirement of `PeriodCalculationService` — see In scope above.
- Retiring event sourcing, CQRS-lite, or the pure rule engine.
- UI changes beyond surfacing any new audit information the architecture requires.

## Decision Log (Q1–Q11 Resolution)

_Walk through the Open Architectural Questions, recorded as decisions are made. Each entry binds the ADR draft. Q1–Q11 closed 2026-04-29; analysis-phase Decision Log complete._

### Q1 — Segmentation logic placement
**Decision (2026-04-28)**: A — SharedKernel pure planner.
**Pre-commits**:
- The planner lives under `src/SharedKernel/StatsTid.SharedKernel/` (likely `Calendar/` or a new `Segmentation/` namespace).
- `PeriodCalculationService.CalculateAsync` signature changes to accept a typed `PlannedCalculation` value as input. The constructor of `PlannedCalculation` is internal to the planner — callers cannot construct one directly. Bypass attempts fail at compile time (same compile-time-guard pattern as `AddStatsTidJwtAuth(IServiceCollection, IConfiguration, IHostEnvironment)` in TASK-1905).
- Same Constraint Validator rule that catches direct rule-engine HTTP calls applies symmetrically here — out-of-band paths to `/api/rules/evaluate` must obtain their `(period, profile)` tuple from a `PlannedCalculation`.

### Q2 — Rule classification declaration
**Decision (2026-04-28)**: B (full) — required parameter on `RuleRegistry.Register`.
**Implications**:
- Multi-mode rules decompose to separately-registered rules. `NormCheckRule` becomes 3 registered rules (`NORM_CHECK_WEEKLY`, `NORM_CHECK_MULTIWEEK`, `NORM_CHECK_ANNUAL`); the dispatcher selects by classification + period, not by intra-rule branching.
- `RuleRegistry.Register` signature gains required parameters: `span`, `splitBehavior`, `family`. Compile-time enforcement that no rule reaches the planner unclassified.
- This is a real refactor of `NormCheckRule` and any other multi-mode rule; in S20 scope.

### Q3 — Merge semantics
**Decision (2026-04-28)**: B + per-rule override at registration.
**Pre-commits**:
- Two standalone mergers in SharedKernel: `CalculationResultMerger` and `ComplianceCheckResultMerger`. Pure static methods; no `MergeWith` on the result types themselves.
- Default `MergeStrategy` derived from the `(span, splitBehavior)` triple:
  - `(entry, segment-safe, calculation)` → `Concatenate`
  - `(window, aligned-window, *)` → `RejectIfMultipleSegments` (planner contract violation if reached)
  - `(period, mergeable, calculation)` → no default; registration MUST supply an override (compile-time enforcement when `splitBehavior: Mergeable`)
  - `(*, *, compliance)` → `UnionDedupe` always
- Per-rule override is an optional `mergeStrategy:` parameter on `Register`; required when default is undefined.

### Q4 — Behavior at invalid segmentation boundary
**Decision (2026-04-28)**: Default-reject; opt-in `PlannerOptions.AllowUpstreamAlignment` per call.
**Pre-commits**:
- `PlannerOptions.AllowUpstreamAlignment` defaults to `false`. When `false` and an `aligned-window` rule disagrees with the period boundary, the planner returns a structured error (NOT a `PlannedCalculation`) listing the offending rule(s).
- When `true` and there's disagreement, the planner **shrinks** (not expands) the period to the rule's natural window edge and audit-annotates the manifest with `boundary_realigned`. Expansion is more surprising than shrink and ruled out.
- `(reject, *, *)` rules ignore the flag — period straddle is always 4xx.

### Q5 — Implementation scope (this sprint)
**Decision (2026-04-28)**: Tightened scope per the table in the "## Scope Boundary > In scope" section above.
- OK version: end-to-end (boundary detection, planner segmentation, classification, merge, manifest, mixed-version export stamping).
- Agreement-config: ONE end-to-end path proves the framework extends.
- Position-override + EU WTD: extension points only; no required e2e path.
- Wage-type-mapping + entitlement + employee-profile: snapshot-at-calculation only (Q5b).

### Q5b — Non-effective-dated boundary sources
**Decision (2026-04-28)**: (b) snapshot-at-calculation for S20.
**Long-term commitment**: option (c) — versioned history — committed to ROADMAP.md Phase 4 as "Versioned History for Non-Dated Boundary Sources" with three sub-sprints (wage-type-mapping, entitlement-policy, employee-profile). Hard dependency on S20's `SnapshotContract` + manifest. Locking (c) on the roadmap prevents (b) from quietly becoming permanent.

### Q6 — `RecalculateWithVersionSplitAsync` backward compatibility
**Decision (2026-04-28)**: (a) retire.
**Pre-commits**:
- `RecalculateWithVersionSplitAsync` deletes outright. Confirmed only-internal-caller (single file: `RetroactiveCorrectionService.cs`); no external API to coordinate with.
- `RecalculateAsync` (public) keeps its signature; internal split logic replaced by a planner call.
- ADR-013's no-cascade semantics move INTO the planner as an explicit merge-policy choice; not duplicated.

### Q7 — Arbitrary segment count: representation and tests
**Decision (2026-04-28)**: list-based merge, arbitrary-N `PlannedCalculation`, three committed test scenarios.
**Pre-commits**:
- `PlannedCalculation.Segments` is `IReadOnlyList<PlannedSegment>`; length ≥ 1; no special-case branches for N=1 or N=2.
- `PlannedSegment` carries `(StartDate, EndDate, BoundaryCause, SegmentSnapshot)`.
- Merge contract is **list-based**: `MergeStrategy.Apply(IReadOnlyList<TResult> segments)` — strategy operates on the whole list. Sidesteps merge-associativity invariant; no class of bug from fold order. Property-based associativity tests deferred.
- Committed scenarios:
  1. **2 segments** — canonical OK transition, period 2026-03-25 → 2026-04-30.
  2. **3 segments, 2 distinct causes** — OK transition + agreement-config DRAFT→ACTIVE simultaneous; period covering 03-25 to 04-30 with config promotion 04-15.
  3. **1 synthetic 4-segment test** — verifies framework handles arbitrary N; multiple position-override `effective_from` dates within one period or similar.

### Q8 — Performance budget on the happy path
**Decision (2026-04-28)**: A — always invoke planner. Budget 5/20/50 ms.
**Pre-commits**:
- Every `CalculateAsync` call goes through `Planner.Plan()` first, including non-straddling periods. Manifest is constructed for every calculation — uniform audit + replay determinism for both straddling and non-straddling cases (avoids the "(B) lazy" regression on Q5b's snapshot contract).
- Budget: ≤5 ms p50, ≤20 ms p95, hard 50 ms p99 ceiling on planner overhead for a non-straddling 1-employee 1-period call.
- Measurement: a regression test in the Q11 committed minimum matrix instruments planner wall-clock and asserts under-budget. Order-of-magnitude regression catcher; not perf-suite-grade.
- Caching out of scope for S20. Planner is stateless across calls; if production load proves cost is high, follow-up sprint adds `(period, profile-version, rule-set-version)` keyed caching.

### Q9 — Drift detection placement
**Decision (2026-04-29)**: B — compile-time + Constraint Validator (carry-forward from Q1) + runtime invariants on `PlannedCalculation` construction.
**Rationale**: Compile-time (Q1) covers in-repo bypass; Constraint Validator (Q1) covers new code paths that try to post directly to `/api/rules/evaluate`. Both leave **planner-bug drift** silent — a malformed `PlannedCalculation` flows into RuleEngine and produces deterministically wrong output, breaking P3. Runtime invariants on planner output close that vector. Wire-level contract checks at the RuleEngine HTTP boundary (option C) and reflection seals (option D) deferred: PAT-005's convention-based enforcement has held for ~10 sprints, and a closed in-process deployable does not warrant the ceremony.
**Pre-commits**:
- **Carry-forward from Q1**:
  - `PlannedCalculation` ctor is `internal`; `InternalsVisibleTo` only the planner test project.
  - `PeriodCalculationService.CalculateAsync(PlannedCalculation plan, …)` is the sole entry point into segmented calculation.
  - Constraint Validator rule extends to flag any new code under `src/Integrations/` (or future Payroll-side callers) that posts to `/api/rules/evaluate` without routing through `PeriodCalculationService`.
- **New in Q9 — runtime invariants asserted in `PlannedCalculation`'s internal ctor**:
  - `Segments.Count ≥ 1`.
  - Segments are sorted, non-overlapping, contiguous, and `Segments.First().Start == period.Start && Segments.Last().End == period.End`.
  - For every rule in the rule registry that declares a `SnapshotContract` (Q5b): every segment whose period intersects the contract's read scope carries a snapshot.
  - Every rule has a non-null resolved `MergeStrategy` — default-derived per Q3 from `(span, splitBehavior, family)`, or a per-rule override supplied at registration (Q3 enforces the override at registration time for `Mergeable` rules without a default; the planner-side invariant catches the registry-vs-plan inconsistency case where a rule was registered but no strategy reached the plan).
  - Violation throws `PlannerInvariantViolation : InvalidOperationException` with a structured message naming the rule and the failed invariant. Deterministic — same input always throws or always succeeds.
- **Test coverage**: the Q11 minimum matrix includes one negative case per invariant (planner output construction throws).
- **Explicitly NOT pre-committed (deferred)**:
  - No HMAC seal or per-call token at the RuleEngine HTTP boundary; PAT-005 stays convention-based on the wire.
  - No RuleEngine-side rejection of "unplannered" requests.
  - Reflection-proof sealing (option D) — defends against attackers with in-process execution; not worth the ceremony.

### Q10 — Audit representation: segment manifest shape and persistence
**Decision (2026-04-29)**: D + (ii) + (β) — separate `SegmentManifest` record (not a serialized `PlannedCalculation`); persistence is **event + indexed projection**; projection schema is **hybrid normalized columns + JSONB segments payload**; per-line `manifest_id` linkage on payroll exports; `ReplayAsync(manifestId)` primitive distinct from recomputation.
**Rationale**: SharedKernel domain types stay pure (matches the post-S19 `OkVersionCanonicalization` precedent — pure helper, persistence concerns elsewhere). Event store is the source of truth for P3; projection table is convenience for audit query. Hybrid schema indexes the columns we'll filter on (employee, period, boundary cause) and JSONB-encodes the deep payload that's always read whole — `(α)` over-normalizes for queries we won't run, `(γ)` loses query utility. Replay-vs-recomputation distinction is what makes Q5b option (b) actually deliver determinism: replay reads snapshots from the manifest, not from the live DB.
**Pre-commits**:
- **Type**: `SegmentManifest` record in `src/SharedKernel/StatsTid.SharedKernel/Segmentation/` (or sibling of the planner). Constructed from a `PlannedCalculation` via a mapping ctor; not the same shape.
- **Persistence — event**:
  - New event `SegmentManifestCreated` registered in `EventSerializer`; brings registered event count 34 → 35 (DEP-003 update at sprint end).
  - `manifest_id` = event `Id` (no separate UUID).
- **Persistence — projection**:
  - New table `segment_manifests` with columns: `manifest_id UUID PRIMARY KEY`, `period_start DATE NOT NULL`, `period_end DATE NOT NULL`, `employee_id UUID NOT NULL`, `calculation_kind TEXT NOT NULL` (`forward-calc` / `retroactive-correction` / `replay`), `boundary_cause_summary TEXT[] NOT NULL` (deduped list: `OkTransition`, `AgreementConfigPromotion`, `PositionOverrideEffective`, …), `created_at TIMESTAMPTZ NOT NULL`, `segments_jsonb JSONB NOT NULL`.
  - Indexes: `idx_segment_manifests_employee_period (employee_id, period_start)`; GIN on `boundary_cause_summary`.
  - Rebuildable from events; rebuild script ships as part of S20.
- **Linkage** (amended 2026-04-29 during Phase 1 — `payroll_export_lines` is an in-memory C# model, not a DB table; original spec assumed DB-side persistence that doesn't exist):
  - `CalculationResult.ManifestId Guid` (additive field; PAT-006 amendment recorded in the ADR) — in-memory + event payload.
  - `audit_log` calculation entries carry `manifest_id` in `payload_jsonb` — additive, no schema change, queryable.
  - SLS export file format carries per-line OK-version stamping in file content (TASK-2010) — file-side, not DB-side.
  - DB-side per-line persistence of export lines: explicitly deferred. Audit query path is `segment_manifests` ⨝ `audit_log.payload_jsonb.manifest_id`; no `payroll_export_lines.manifest_id` column.
- **Replay primitive**:
  - `PeriodCalculationService.ReplayAsync(Guid manifestId, CancellationToken)` returns a `CalculationResult` carrying the **same** `ManifestId` (replay does NOT mint a new manifest).
  - Internal `PlannedCalculation.FromManifest(SegmentManifest)` ctor; same `internal` visibility as the regular ctor (Q9). Subject to identical Q9 invariants.
  - Rules read snapshots from `SegmentSnapshot` in the reconstructed plan, NOT from the live DB. Determinism (P3) follows.
  - **Recomputation** (fresh snapshots, new manifest) is a separate operation; the ADR documents the distinction explicitly so callers don't conflate "replay" with "recompute as of today".
- **Migration** (amended 2026-04-29 during Phase 1):
  - Schema changes: `CREATE TABLE segment_manifests` + two indexes. **No** `ALTER TABLE payroll_export_lines` — that table does not exist as DB persistence today.
  - **No backfill** required (no per-line column was added).
- **Out of S20 (deferred)**:
  - No UI surfacing of manifest content (Phase 5 polish — frontend receives `ManifestId` but does not render segment breakdown).
  - No manifest-comparison tooling (replay-vs-replay diff for incident diagnosis); not P3-required.

### Q11 — Test strategy: committed minimum matrix
**Decision (2026-04-29)**: (ii) — cell enumeration via the ADR's classification table (not pre-stated in this plan); floor of 22 new tests; placement and per-bucket counts pre-committed below; full rule × scenario × boundary exhaustive matrix deferred.
**Rationale**: Q2 made `RuleRegistry.Register` carry the classification triple, so the table is a real ADR deliverable. Binding Q11 to that table is the only way the minimum stays correct after the rule re-classification work in the ADR. A floor of 4 distinct cells exercised catches the case where classification accidentally collapses to too few cells; the ADR can raise the floor but cannot lower it. Counts beyond the cells (Q9 invariants, Q10 manifest tests, boundary scenarios, mixed-version export, perf budget) are mechanically derivable from the prior decisions, so they can be pre-stated.
**Pre-commits**:
- **Cell enumeration**: one test per populated `(span, split-behavior)` cell as listed in the S20 ADR's classification table. **Floor: minimum 4 distinct cells exercised.** ADR table can raise the floor; cannot lower it.
- **Q9 invariant negative tests** (4 tests, unit-level): one per invariant — segments empty; segments non-contiguous / overlapping / not covering period exactly; rule with `SnapshotContract` missing snapshot for an intersecting segment; rule with no resolved `MergeStrategy`. Each asserts `PlannerInvariantViolation` thrown with structured message naming the rule and failed invariant.
- **Q10 manifest tests** (4 tests, regression-level / DB-touching):
  - manifest-creation: calculation produces manifest; projection table reflects segments JSONB and indexed columns; `payroll_export_lines.manifest_id` populated.
  - replay: `ReplayAsync(manifestId)` produces `CalculationResult` with same `ManifestId` and bit-equal payload.
  - projection rebuild: truncate `segment_manifests`, replay `SegmentManifestCreated` events, projection reconstructs.
  - replay-vs-recomputation: mutate the live DB after manifest creation; replay still produces the manifest's result, recomputation produces a different one. Locks down Q5b option (b)'s determinism contract.
- **Boundary scenarios** (8 tests, regression-level): 4 implemented sources × (valid + invalid). Sources: OK version, agreement-config, position-override, EU WTD compliance ruleset. Q7's three committed scenarios slot in: 2-segment OK transition → OK-version valid; 3-segment OK + agreement-config simultaneous → agreement-config valid; 4-segment synthetic position-override → position-override valid. "Invalid" cases per source: window-rule boundary alignment violated with `AllowUpstreamAlignment=false`, expecting 4xx per Q4 default-reject. EU WTD valid/invalid: period straddling a compliance-rule version bump.
- **Mixed-version export (TASK-1903 absorbed)** (1 test, regression-level): period 2026-03-25 → 2026-04-30 across OK24/OK26; export lines on dates ≤ 2026-03-31 carry OK24 stamp; lines on ≥ 2026-04-01 carry OK26. Replaces `OkVersionBoundary.ResolveProfile`'s collapse behavior at `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:325-339`.
- **Q8 perf budget assertion** (1 test, unit-level): non-straddling 1-employee 1-period planner overhead under p50 ≤ 5 ms / p95 ≤ 20 ms / p99 hard ceiling 50 ms. Order-of-magnitude regression catcher only.
- **Test placement (file paths)**:
  - `tests/StatsTid.Tests.Unit/Segmentation/PlannerCellTests.cs` — `(span × split-behavior)` cells (≥ 4)
  - `tests/StatsTid.Tests.Unit/Segmentation/PlannedCalculationInvariantTests.cs` — Q9 negatives (4)
  - `tests/StatsTid.Tests.Regression/Segmentation/ManifestCreationTests.cs` (1)
  - `tests/StatsTid.Tests.Regression/Segmentation/ManifestReplayTests.cs` (1)
  - `tests/StatsTid.Tests.Regression/Segmentation/ManifestProjectionRebuildTests.cs` (1)
  - `tests/StatsTid.Tests.Regression/Segmentation/ReplayDeterminismTests.cs` (1)
  - `tests/StatsTid.Tests.Regression/Segmentation/BoundaryScenarioTests.cs` (8)
  - `tests/StatsTid.Tests.Regression/Payroll/MixedVersionExportTests.cs` (1)
  - `tests/StatsTid.Tests.Unit/Segmentation/PlannerPerfBudgetTests.cs` (1)
- **Floor**: 22 new tests (4 cells + 4 Q9 + 4 Q10 + 8 boundary + 1 mixed-version + 1 perf budget); cells beyond 4 add 1:1.
- **Explicitly out of S20 (deferred)**:
  - Full rule × scenario × boundary exhaustive matrix → exhaustive-testing debt; recorded in MEMORY.md deferred items at sprint end.
  - Frontend tests for any S20-related UI (Phase 5 polish; ComplianceWarnings frontend-test debt stays deferred).
  - Property-based merge associativity tests (Q7 sidestepped via list-based merge).
  - Restructure of the existing 35-test regression suite — S20 adds tests, doesn't restructure.

---

## Planning Entrypoint

The sprint began with the following **analysis-phase deliverables**, produced by the Orchestrator in collaboration with the user before any domain agent is spawned. All three approved 2026-04-29; analysis phase closed:

1. **Architectural ADR** — answering the eleven open questions above, proposing segmentation placement, the multi-axis rule classification, merge semantics per result family, and the segment-manifest shape.
2. **Rule classification inventory** — every existing rule (and every mode of multi-mode rules) tagged with its `(span, split-behavior, result-family)` triple.
3. **Task decomposition** — the ADR translated into `TASK-20NN` entries with domain agents, file scopes, and validation criteria, added to this sprint log under "## Task Log".
4. **Entropy scan (Step 0a)** — completed 2026-04-28; findings recorded above.
5. **Plan review (Step 0b)** — completed 2026-04-28; findings + resolution recorded above.

Only after items 1–3 are Orchestrator + user approved does Step 2 (Delegate) begin.

### Analysis-Phase Exit Criteria (Step 0b NOTE N1 + R-N2, wall-clock framing rejected)

Step 0b cycle 1 originally accepted a 3-calendar-day wall-clock timebox for the analysis phase. That framing was rejected on user feedback 2026-04-29 — it borrows a multi-developer-sprint coordination model that doesn't fit a solo, async project, and the "default scope reduction kicks in on date X" rule is artificial pressure with no real trigger. The load-bearing protection against open-ended analysis is the exit criteria below; analysis halts when those criteria are satisfied, not on a calendar.

If exit criteria stall, the default scope-reduction option remains available — triggered by orchestrator+user judgment, not by elapsed days. Default reduction: ship OK-version-only (no extension framework, no manifest beyond the minimum needed for OK-boundary audit, no agreement-config / position-override extension points), and re-promote the broader framework to a future sprint.

Exit criteria for "approved":
- ADR is committed under `docs/knowledge-base/decisions/` with a chosen answer to every open question (or an explicit "deferred to S21+" with rationale);
- Rule classification inventory exists as a committed artifact (table in the ADR or a separate file under `docs/`);
- TASK-20NN entries appear in this sprint log's Task Log with agents and file scopes assigned;
- User confirms acceptance of the scope shape.

## Task Log

_Drafted from ADR-016 and user-approved 2026-04-29. All tasks are `not-started` until Step 2 (Delegate)._

### Task Index

| TASK | Domain / Agent | Phase | Title |
|------|----------------|-------|-------|
| TASK-2001 | Data Model | 1 | Schema migration: `segment_manifests` table + indexes (no `payroll_export_lines` ALTER — D10 amended 2026-04-29) |
| TASK-2002 | Constraint Validator | 1 | Constraint Validator rule: flag direct `/api/rules/evaluate` posts that bypass `PeriodCalculationService` |
| TASK-2003 | Rule Engine (SharedKernel infra) | 2 | SharedKernel `Segmentation/` skeleton: `PlannedCalculation`, `PlannedSegment`, `SegmentManifest`, `SnapshotContract`, `MergeStrategy`, `PlannerOptions`, `PlannerInvariantViolation` — types only, D9 invariants asserted in ctor |
| TASK-2004 | Rule Engine (SharedKernel infra) | 2 | `PeriodPlanner.Plan()` + `PlannedCalculation.FromManifest()` logic — segment detection from boundary sources; manifest-driven replay reconstruction |
| TASK-2005 | Rule Engine (SharedKernel infra) | 2 | `CalculationResultMerger` + `ComplianceCheckResultMerger` — list-based `MergeStrategy.Apply` per ADR-016 D3 defaults table |
| TASK-2006 | Rule Engine | 2 | `RuleRegistry.Register` signature change (`span`, `splitBehavior`, `family`, optional `mergeStrategy`); multi-mode decomposition of `NormCheckRule` (3 modes), `RestPeriodRule` (4 checks), `OvertimeGovernanceRule` (2 checks) — 16 segmentation-classified + 1 out-of-scope = 17 registered rules (corrected from original "11 → 20" miscount during execution 2026-04-30) |
| TASK-2007 | Data Model | 2 | `EventSerializer` registers `SegmentManifestCreated` (count 43 → 44; corrected from original "34 → 35" stale figure during execution 2026-04-30); `CalculationResult.ManifestId Guid` additive field; `audit_log` payload `manifest_id` field |
| TASK-2008 | Payroll | 3 | `PeriodCalculationService.CalculateAsync(PlannedCalculation, …)` signature change + `ReplayAsync(Guid manifestId, …)` primitive; planner invocation on every call (D8 always-on) |
| TASK-2009 | Payroll | 3 | `RetroactiveCorrectionService.RecalculateWithVersionSplitAsync` deletion; `RecalculateAsync` rewrite on top of planner; ADR-013 no-cascade migrates to planner merge-policy |
| TASK-2010 | Payroll | 3 | Per-line OK-version stamping at export boundary (TASK-1903 absorbed): replace `OkVersionBoundary.ResolveProfile` collapse at `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:325-339`; SLS export file content carries per-line OK stamps + manifest_id (file-side; D10 amended 2026-04-29 — no DB column on payroll_export_lines) |
| TASK-2011 | Data Model | 3 | Manifest projection rebuild script: replay `SegmentManifestCreated` events into `segment_manifests` table; deployable as ops tooling |
| TASK-2012 | Test & QA | 4 | Test matrix per ADR-016 D11 — 22 new tests floor (cells + invariants + manifest creation/replay/rebuild/determinism + boundary scenarios + mixed-version export + perf budget) |

### Phase Ordering

- **Phase 1 (parallel-independent)**: TASK-2001, TASK-2002. Schema and CV rule have no upstream dependencies; can run in parallel via `isolation: "worktree"`.
- **Phase 2 (parallel within phase, depends on Phase 1)**: TASK-2003, TASK-2004, TASK-2005, TASK-2006, TASK-2007. TASK-2003 (skeleton types) is the upstream for TASK-2004 / TASK-2005 / TASK-2006 within Phase 2 — those three start once 2003 lands. TASK-2007 is independent and can run alongside 2003.
- **Phase 3 (depends on Phase 2)**: TASK-2008, TASK-2009, TASK-2010, TASK-2011. All four consume the planner + types from Phase 2. Can run in parallel via worktree isolation since each touches a different surface (`PeriodCalculationService` vs `RetroactiveCorrectionService` vs export boundary vs ops script).
- **Phase 4 (depends on Phase 3)**: TASK-2012. Test & QA Agent runs after all production code lands.
- **Phase 5 (Orchestrator)**: build/test validation, Step 5α Constraint Validator over all outputs, Step 5a Internal Reviewer (P1–P4 trigger). **High-risk override applies — Step 5a also invokes `codex review --uncommitted`** (S20 touches P3 + P4 + P6 = three of the high-risk domains: schema, payroll, retroactive). Step 7a external Codex review against pre-S20 HEAD before commit.

### Task Detail

#### TASK-2001 — Schema migration: segment_manifests + indexes
**Agent**: Data Model
**Phase**: 1 — completed 2026-04-29
**Files (write)**:
- `docker/postgres/init.sql` (additive — new table + indexes; spec originally said `infra/postgres/` — corrected to actual canonical path during execution)
**Scope**:
- `CREATE TABLE segment_manifests` (8 columns per ADR-016 D10).
- Two indexes: `idx_segment_manifests_employee_period (employee_id, period_start)`; GIN on `idx_segment_manifests_boundary_cause`.
- **Originally specified `ALTER TABLE payroll_export_lines ADD COLUMN manifest_id UUID NULL` — REMOVED from scope 2026-04-29** when the Data Model agent surfaced that `payroll_export_lines` is an in-memory C# model, not a DB table. ADR-016 D10 amended; linkage now lives in `audit_log.payload_jsonb` + `CalculationResult.ManifestId` + SLS file content.
**Validation**: migration runs cleanly on fresh DB; `dotnet build` clean.
**Cross-domain dependencies**: none — surfaced one architectural amendment (D10 linkage) which was applied to the ADR and downstream tasks before Phase 1 closure.

#### TASK-2002 — Constraint Validator: planner-bypass rule
**Agent**: Constraint Validator
**Phase**: 1
**Files (write)**: Constraint Validator rule definition file (location per existing CV pattern — verify against S18/S19 CV rule files).
**Scope**: Add a static-analysis rule that flags any source file under `src/Integrations/` (and any future Payroll-side caller) that posts to `/api/rules/evaluate` without routing through `PeriodCalculationService.CalculateAsync(PlannedCalculation)`. Allowlist `PeriodCalculationService` itself.
**Validation**: rule fires on a known-bad fixture; rule does not fire on the legitimate `PeriodCalculationService` HTTP call site.
**Cross-domain dependencies**: none (independent of Phase 2 type shapes — the rule is a regex/AST check on call sites).

#### TASK-2003 — SharedKernel Segmentation skeleton types
**Agent**: Rule Engine (SharedKernel infrastructure)
**Phase**: 2 (upstream for 2004–2006)
**Files (write)**:
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/PlannedCalculation.cs` (internal ctor, D9 invariants asserted)
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/PlannedSegment.cs`
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/SegmentManifest.cs`
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/SnapshotContract.cs`
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/MergeStrategy.cs`
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/PlannerOptions.cs`
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/PlannerInvariantViolation.cs`
- `InternalsVisibleTo` attribute on `StatsTid.SharedKernel.csproj` for the planner test project only.
**Scope**: Types only. Empty / placeholder logic where method bodies are required. D9 invariants ARE implemented in `PlannedCalculation`'s internal ctor — they're the contract the type carries.
**Validation**: `dotnet build` clean; types compile; D9 invariant unit tests (placeholders that will be filled out in TASK-2012) at minimum exercise the ctor's throw paths.
**Cross-domain dependencies**: none upstream; downstream blocks TASK-2004, TASK-2005, TASK-2006.

#### TASK-2004 — PeriodPlanner.Plan() + FromManifest()
**Agent**: Rule Engine (SharedKernel infrastructure)
**Phase**: 2 — completed 2026-04-30
**Files (write)**:
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/PeriodPlanner.cs` (also defines `BoundarySources` record at file end)
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/BoundaryDetector.cs`
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/Span.cs` (added during execution — gap in TASK-2003 skeleton; planner is the consumer that defines the input contract)
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/SplitBehavior.cs` (same)
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/Family.cs` (same)
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/RuleClassification.cs` (record consumed by planner, produced by RuleRegistry)
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/PlannedCalculation.cs` (modified — `FromManifest` body delegates to `PeriodPlanner.FromManifest` instead of throwing)
**Scope**: `Plan(employeeId, periodStart, periodEnd, calculationKind, ruleSet, sources, options)` produces a `PlannedCalculation` (segments list from boundary detection, snapshots gathered when any rule's `SnapshotContract` requires it). `FromManifest(SegmentManifest, ruleSet)` reconstructs a `PlannedCalculation` with segments populated from the manifest, re-runs rule-side invariants. Boundary-cause tie-break: OkTransition > AgreementConfigPromotion > PositionOverrideEffective > EuWtdRulesetVersion (highest-impact wins).
**Validation**: build clean; 495/495 unit tests pass; cell tests + D9 invariant negatives in TASK-2012 will exercise the throw paths.
**Cross-domain dependencies**: depends on TASK-2003 (skeleton). Note: TASK-2004 absorbed addition of the four classification contract types (Span/SplitBehavior/Family/RuleClassification) — TASK-2003 skeleton predated them.

#### TASK-2005 — Result mergers (calculation + compliance)
**Agent**: Rule Engine (SharedKernel infrastructure)
**Phase**: 2 — completed 2026-04-30
**Files (write)**:
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/CalculationResultMerger.cs`
- `src/SharedKernel/StatsTid.SharedKernel/Segmentation/ComplianceCheckResultMerger.cs`
**Scope**: List-based `MergeStrategy.Apply(IReadOnlyList<TResult>)` per ADR-016 D3 defaults. `ComplianceViolation` dedup key: `(ViolationType, Date, ActualValue, ThresholdValue, Severity)` as private `readonly record struct`; `Message` (presentation) and `IsVoluntaryExempt` (segment-state-dependent) excluded with rationale documented inline. `UnionDedupe` accepted by ComplianceCheckResultMerger only — calc merger throws `PlannerInvariantViolation` if reached. Empty-list defense in both. Stable order (first-occurrence wins) via `HashSet<DedupKey>`-guarded list.
**Validation**: build clean; 495/495 unit tests pass (mergers are net-new code with no consumers yet — TASK-2008 wires them in).
**Cross-domain dependencies**: depends on TASK-2003.

#### TASK-2006 — RuleRegistry signature + multi-mode decomposition
**Agent**: Rule Engine
**Phase**: 2 — completed 2026-04-30
**Files (write)**:
- `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RuleRegistry.cs` (full rewrite — classification table + Register/GetAll/Get/ResolveDefault; legacy dispatchers retained, EvaluateTimeRule routes the three new norm ids alongside legacy NORM_CHECK_37H alias)
- `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/NormCheckRule.cs` (decompose: added `EvaluateWeekly`/`EvaluateMultiWeek`/`EvaluateAnnual`; legacy `RuleId="NORM_CHECK_37H"` and `Evaluate(...config)` unchanged for backward compat)
- `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/RestPeriodRule.cs` (decompose: added 4 entry points reusing existing `Check*` private helpers; legacy `RuleId="REST_PERIOD_CHECK"` and `Evaluate` unchanged)
- `src/RuleEngine/StatsTid.RuleEngine.Api/Rules/OvertimeGovernanceRule.cs` (decompose: 2 entry points; legacy unchanged)
**Scope**: `Register(ruleId, span, splitBehavior, family, mergeStrategy?, snapshotContract?)`. Runtime error when `splitBehavior: Mergeable` registration omits `mergeStrategy`. Period-mergeable compliance rules use explicit `MergeStrategy.UnionDedupe` (per ADR-016 D3 — compliance defaults to UnionDedupe regardless of split-behavior, correcting an early dispatch error that would have used Custom). Calc-Mergeable rules (MULTIWEEK, ANNUAL, FLEX_BALANCE) use explicit `MergeStrategy.Custom`; the actual Custom delegate wires in TASK-2008/2009 (until then merger throws if Custom is reached without delegate). Legacy ids (`NORM_CHECK_37H`, `REST_PERIOD_CHECK`, `OVERTIME_GOVERNANCE_CHECK`) retained as aliases routing to legacy `Evaluate` entry points — preserves all external callers (orchestrator pipeline, payroll integration, smoke tests, existing unit tests). `EntitlementValidationRule` kept on the legacy `/api/rules/validate-entitlement` HTTP path; not classified.
**Validation**: build clean; 495/495 unit tests pass (no test required modification — the legacy entry points produce bit-exact results).
**Cross-domain dependencies**: depends on TASK-2003 (`MergeStrategy`, `SnapshotContract`) + TASK-2004 (`Span`, `SplitBehavior`, `Family`, `RuleClassification`).

#### TASK-2007 — EventSerializer + CalculationResult amendment
**Agent**: Data Model
**Phase**: 2 — completed 2026-04-30
**Files (write)**:
- `src/SharedKernel/StatsTid.SharedKernel/Events/SegmentManifestCreated.cs` (new — `DomainEventBase` descendant; list-typed properties default to `Array.Empty<T>()` rather than `required` to keep existing reflection-based `EventSerializerCoverageTests` round-trip passing)
- `src/Infrastructure/StatsTid.Infrastructure/EventSerializer.cs` (registered `SegmentManifestCreated`)
- `src/SharedKernel/StatsTid.SharedKernel/Models/CalculationResult.cs` (additive `Guid ManifestId { get; init; }` defaulting `Guid.Empty` — not `required`, not nullable)
- `src/Infrastructure/StatsTid.Infrastructure/Security/AuditLoggingMiddleware.cs` (added `ManifestIdItemKey = "audit:manifest_id"`; new `BuildDetailsPayload(HttpContext)` writes `{"manifest_id":"<guid>"}` into `entry.Details` when key present in `HttpContext.Items` with non-empty Guid; otherwise null → JSON byte-identical to today)
**Scope**: Register `SegmentManifestCreated` event type (event count 43 → 44 — original spec said 34→35 but registry had grown across S6/S9/S12/S14/S15/S16/S17). Additive `ManifestId` on `CalculationResult`. Audit middleware reads `audit:manifest_id` from `HttpContext.Items` (TASK-2008 will populate it from endpoints).
**Validation**: build clean; 495/495 unit tests pass; `EventSerializerCoverageTests` reflection guard passes (every concrete `DomainEventBase` descendant registered); existing `Sprint4ModelTests:147` (only test that constructs `CalculationResult`) passes without modification.
**Cross-domain dependencies**: depends on TASK-2003 (`SegmentManifest` type, `PlannedSegment` type).

#### TASK-2008 — PeriodCalculationService rewrite + ReplayAsync
**Agent**: Payroll
**Phase**: 3
**Files (write)**:
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs`
**Scope**: `CalculateAsync(PlannedCalculation plan, …)` becomes the sole entry point. Internal call goes through `Plan()` for non-`PlannedCalculation` callers and immediately routes back through the new entry point. New `ReplayAsync(Guid manifestId, CancellationToken)` loads `SegmentManifest` from projection (with event-replay fallback), reconstructs via `PlannedCalculation.FromManifest`, runs `CalculateAsync(plan)`, returns `CalculationResult` with same `ManifestId`. Manifest emission happens here (after segments resolved + snapshots gathered + invariants pass).
**Validation**: existing `PeriodCalculationService` tests adapted to new signature; new replay tests in TASK-2012.
**Cross-domain dependencies**: depends on TASK-2004, TASK-2005, TASK-2006, TASK-2007. Pre-Phase-3 status check: ALL Phase 2 tasks must land first.

#### TASK-2009 — RetroactiveCorrectionService rewrite + RecalculateWithVersionSplitAsync deletion
**Agent**: Payroll
**Phase**: 3
**Files (write)**:
- `src/Integrations/StatsTid.Integrations.Payroll/Services/RetroactiveCorrectionService.cs`
**Scope**: Delete `RecalculateWithVersionSplitAsync` outright. Rewrite `RecalculateAsync` (public) on top of `PeriodPlanner.Plan()` + `PeriodCalculationService.CalculateAsync(plan)`. ADR-013's no-cascade rule lives in the planner's merge-policy — verify it does NOT remain duplicated here.
**Validation**: existing retroactive-correction regression tests pass without modification (caller signature unchanged); no `RecalculateWithVersionSplitAsync` references remain in the codebase (grep check).
**Cross-domain dependencies**: depends on TASK-2008. Note: `OkVersionCanonicalization` helper from S19 stays as the OK-resolution input to the planner — it does not get retired by this task; it becomes a planner input.

#### TASK-2010 — Per-line OK-version stamping at export boundary (TASK-1903 absorbed)
**Agent**: Payroll
**Phase**: 3
**Files (write)**:
- `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` (replace `OkVersionBoundary.ResolveProfile` collapse logic at lines 325-339)
- `src/SharedKernel/StatsTid.SharedKernel/Models/PayrollExportLine.cs` (additive `ManifestId Guid` + `OkVersion` per-line fields if not already present)
- `src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollMappingService.cs` (per-line OK propagation; manifest_id propagation)
- `src/Integrations/StatsTid.Integrations.Payroll/Services/SlsExportFormatter.cs` (per-line OK stamp in file content; manifest_id stamp in file header / footer / per-line per SLS spec)
**Scope**: Per-line OK-version stamping derives FROM the segment manifest: each export line knows its segment, segment knows OK version, line stamps OK version. `OkVersionBoundary.ResolveProfile`'s single-version collapse goes away. Per-line stamping consumes ONLY segment-resolved OK / config context — wage-type-mapping effective-dating does NOT leak in (per Step 0b W3 + ADR-016 D5b boundary). Internal-Reviewer concern about `/calculate-and-export` not using the same boundary helper as `/export` / `/export-period` resolved by routing all three through the planner. **Linkage is file-side and in-memory only; D10 amendment 2026-04-29 confirmed `payroll_export_lines` is not a DB table — manifest_id audit linkage lives in `audit_log.payload_jsonb` (TASK-2007) + the SLS export file content, not in a DB column.**
**Validation**: mixed-version export regression test (TASK-2012) — period 2026-03-25 → 2026-04-30 produces lines stamped with the correct per-date OK version; manifest_id present in audit_log entries.
**Cross-domain dependencies**: depends on TASK-2008.

#### TASK-2011 — Manifest projection rebuild script
**Agent**: Data Model
**Phase**: 3
**Files (write)**:
- `infra/postgres/scripts/rebuild_segment_manifests.sql` (or whatever ops-script location; verify)
- A C# console entrypoint or Npgsql batch operation if SQL alone insufficient
**Scope**: Truncate `segment_manifests`, replay all `SegmentManifestCreated` events from the event store into the projection. Idempotent. Documented as ops tooling for projection drift recovery.
**Validation**: TASK-2012's projection-rebuild test exercises this path end-to-end.
**Cross-domain dependencies**: depends on TASK-2007.

#### TASK-2012 — Test matrix (D11 minimum)
**Agent**: Test & QA
**Phase**: 4
**Files (write)**:
- `tests/StatsTid.Tests.Unit/Segmentation/PlannerCellTests.cs` — ≥ 4 cell tests
- `tests/StatsTid.Tests.Unit/Segmentation/PlannedCalculationInvariantTests.cs` — 4 D9 negatives
- `tests/StatsTid.Tests.Regression/Segmentation/ManifestCreationTests.cs` — 1
- `tests/StatsTid.Tests.Regression/Segmentation/ManifestReplayTests.cs` — 1
- `tests/StatsTid.Tests.Regression/Segmentation/ManifestProjectionRebuildTests.cs` — 1
- `tests/StatsTid.Tests.Regression/Segmentation/ReplayDeterminismTests.cs` — 1
- `tests/StatsTid.Tests.Regression/Segmentation/BoundaryScenarioTests.cs` — 8 (4 sources × valid + invalid)
- `tests/StatsTid.Tests.Regression/Payroll/MixedVersionExportTests.cs` — 1
- `tests/StatsTid.Tests.Unit/Segmentation/PlannerPerfBudgetTests.cs` — 1
**Scope**: 22 new tests floor; cells beyond 4 add 1:1 against the ADR-016 classification table (5 distinct populated cells means 5 cell tests). Q7's three committed scenarios slot into boundary scenarios.
**Validation**: all 22+ tests pass; existing 493 unit + 35 regression + 41 frontend tests still pass; no flakiness on Docker-gated regression runs.
**Cross-domain dependencies**: depends on TASK-2008 through TASK-2011 (all production code must land).

### Phase 3 Wave 2 Completion (2026-05-02)

| Task | Wave | Commit | Status |
|------|------|--------|--------|
| TASK-2008 (PCS rewrite + ReplayAsync) | 1 | 5b462ba | complete |
| TASK-2011 (manifest projection rebuild) | 1 | 5b462ba | complete |
| Classifications endpoint + HTTP provider DI | 1b | a6beac2 | complete |
| TASK-2009 (RetroactiveCorrectionService rewrite) | 2 | this commit | complete |
| TASK-2010 (per-line OK-version export stamping) | 2 | this commit | complete |
| Orchestrator follow-up (PCS `MapSegmentToExportLinesAsync` threads `plan.ManifestId` into `PayrollExportLine.ManifestId`) | 2 | this commit | small-tasks-exception |
| Review-driven cleanups (audit-event `ManifestId`, doc-drift fixes) | 2 | this commit | small-tasks-exception |

**Validation (wave 2 + cleanups)**:
- `dotnet build`: 0 errors, 1 CS0618 warning at `Program.cs:170` (the `/calculate-and-export` endpoint is the last surviving customer of the `[Obsolete]` PCS shim — explicit migration breadcrumb; full retirement is OUT-of-scope per Step 0b W2).
- Tests: **501 unit + 25 plain regression** pass (493 → 501 across S20; +8 from Phase 1+2's `PlannerBypassGuardTests` and Phase 3 wave 1b's `HttpRuleClassificationProviderTests`). 11 Docker-gated regression failures are environmental.
- `OkVersionBoundary.ResolveProfile` deleted; no production references remain. `RecalculateWithVersionSplitAsync` deleted; no production references remain.

**Step 5α Constraint Validator**: PASS (8/8 mechanical checks). PayrollExportLine.cs cross-domain (Data Model SharedKernel) authorized in TASK-2010 plan; no other scope violations.

**Step 5a Reviewer Audit (P3+P4+P6 mandatory + high-risk Codex override)**:

Internal Reviewer: 0 BLOCKER, 2 WARNING, 11 NOTE.
Codex (high-risk override): 2 advisory findings ([P1] + [P2] in Codex severity).

Convergent finding (both lenses): `RetroactiveCorrectionRequested` audit event still records 2-version OK pair (current + optional previous) while the planner can produce N segments. **Resolved** in this commit by adding `Guid ManifestId { get; init; } = Guid.Empty;` to the event and populating from `newResult.RuleResults.FirstOrDefault()?.ManifestId` in `RetroactiveCorrectionService.RecalculateAsync` — audit consumers join to `SegmentManifestCreated` for the actual N-segment plan.

**Findings applied in this commit**:
- Convergent audit-event manifest_id (above).
- Doc-drift cleanups: `PCS:273` tense fix; `PCS:383-384` line-ref fix (the `Program.cs:325-339` reference is no longer valid after `OkVersionBoundary` removal); `OkVersionRuntimeRegressionTests.cs:191` comment refresh; `SlsExportFormatter.cs` doc-comment column-count wording.

**Findings deferred (recorded as carry-forward debt)**:
- **Codex [P1]** — `RecalculateAsync` calls the `[Obsolete]` shim, which only hydrates `OkTransitions` in `BoundarySources`. Agreement-config / position-override / EU-WTD boundaries silently miss segmentation in the retro path. **Status**: in-scope per ADR-016 D5 ("OK end-to-end + extension points demonstrated"); non-OK boundary hydration is the Phase 4 "Versioned History for Non-Dated Boundary Sources" sub-sprint trio plus future agreement-config / position-override boundary-source wiring. Not a wave-2 regression: the OK-only hydration matches the pre-S20 capability and the new shim path is strictly additive in coverage (it now runs the planner's invariants on every call).
- **Reviewer WARNING — `manifestId = default` is silently lossy**. `PayrollMappingService.MapCalculationResultAsync` and `SlsExportFormatter.Format` accept `Guid manifestId = default`; today's single production caller path threads correctly, but a future caller could silently emit `Guid.Empty` lines. **Status**: bounded today; revisit during Phase 4 hardening (TASK-2012 can lock the contract via test assertions).
- **Reviewer WARNING — `StampAuditContext` has no call sites in Payroll**. `AuditLoggingMiddleware` is registered only in Backend.Api, not Payroll, so the helper is currently moot for Payroll. **Status**: documentation gap; revisit when the audit chain is wired through the Backend → Payroll proxy in a future sprint.
- **Reviewer NOTE — Two near-equivalent mappers (`MapCalculationResultAsync` vs `MapSegmentToExportLinesAsync`)** with subtle date-column divergence (`PeriodStart=PeriodEnd=lineItem.Date` vs `PeriodStart=segmentStart, PeriodEnd=segmentEnd`). Defer; consider extracting a shared private helper in a follow-up.
- **Reviewer NOTE — Four OK-version resolution sites** (`OkVersionResolver`, `OkVersionCanonicalization`, `MapCalculationResultAsync`, PCS per-segment). Each has a distinct documented purpose; cognitive cost is real but no redundancy. Defer documentation index.
- **Reviewer NOTE — ADR-013 no-cascade enforcement framing**. The doc-comments name "merge-policy" but actual enforcement is the planner's geometric bound (`PeriodPlanner.Plan` never expands the input window) plus `FlexBalanceRule`'s chained-balance hand-off. The merge strategies are the merge mechanism, not the no-cascade enforcement mechanism. Defer doc rewording.
- **Reviewer NOTE — `FlexBalanceRule` chained-carry custom delegate not wired**. `MergeStrategy.Custom` falls back to `FallbackCustomMerge` (Concatenate-with-warning) for FLEX_BALANCE; the in-segment chained-carry via `ExtractFlexDelta + flexBalanceCarry` is what currently preserves correctness. The documented per-rule custom delegate (ADR-016 D11 inventory line "Per-rule override required: chained-carry") is migration-path TODO inside the Custom fallback. Defer; surfaces only when a real OK24/OK26 straddling correction runs (post-2026-04-01) — pre-empted by TASK-2012 boundary-scenario tests.
- **Reviewer NOTE — `Program.cs:170` CS0618 warning** is the last `[Obsolete]` shim customer. **Status**: principled breadcrumb; no migration task currently exists in the plan. Track as a future task entry.
- **Reviewer NOTE — SLS column-count wording** (already fixed in this commit per "applied" above).
- **Reviewer NOTE — `OkVersionBoundary` removal verification** (informational; recorded for completeness — no action needed).

### Risks & Watch-Points

- **Phase 2 throughput** — TASK-2003 is the bottleneck (TASK-2004, TASK-2005, TASK-2006 all wait on it). Mitigation: keep TASK-2003 scoped to skeleton + invariants only; logic lives in TASK-2004.
- **Multi-mode decomposition (TASK-2006) risk** — splitting `NormCheckRule` / `RestPeriodRule` / `OvertimeGovernanceRule` into multiple registered rules touches existing endpoint dispatchers. Mitigation: existing rule unit tests are the safety net; per-mode tests added in TASK-2012 catch regressions.
- **Payroll Phase 3 parallel risk** — TASK-2008/2009/2010 all touch Payroll Integration. Worktree isolation is mandatory; conflicts likely on `Program.cs` and `Services/`. Sequential merge is acceptable if conflicts thrash.
- **High-risk Step 5a override** — S20 hits P3 + P4 + P6 (three high-risk domains). External Codex review at Step 5a is mandatory; budget for one BLOCKER-fix cycle. Halt and prompt user after 2 BLOCKER cycles per workflow rule.
- **`OkVersionCanonicalization` carry-forward** — the S19 helper stays in place; do not retire as part of this sprint.

## References

- [CLAUDE.md](../../CLAUDE.md) — priority order (P2, P4 driving this sprint)
- [ROADMAP.md](../../ROADMAP.md) — Phase 3i placement
- [docs/knowledge-base/decisions/ADR-003-ok-version-resolved-by-entry-date.md](../knowledge-base/decisions/ADR-003-ok-version-resolved-by-entry-date.md)
- [docs/knowledge-base/decisions/ADR-013-retroactive-corrections-single-period-no-cascade.md](../knowledge-base/decisions/ADR-013-retroactive-corrections-single-period-no-cascade.md)
- [docs/knowledge-base/decisions/ADR-014-agreement-configs-database-backed.md](../knowledge-base/decisions/ADR-014-agreement-configs-database-backed.md)
- [docs/knowledge-base/decisions/ADR-015-compliance-check-result-pattern.md](../knowledge-base/decisions/ADR-015-compliance-check-result-pattern.md) — actual subject is `ComplianceCheckResult` as a separate return type, NOT "compliance rule classification" (Step 0b W1 correction)
- [docs/knowledge-base/patterns/PAT-005-period-calculation-service-http-rule-evaluation.md](../knowledge-base/patterns/PAT-005-period-calculation-service-http-rule-evaluation.md)
- [docs/knowledge-base/patterns/PAT-006-unified-rule-endpoint-response-format.md](../knowledge-base/patterns/PAT-006-unified-rule-endpoint-response-format.md) — calculation rules only; ADR-015 carves compliance out (Step 0b W1 correction)
- [SPRINT-18.md](SPRINT-18.md) — TASK-1801 closed the caller-trust vector; this sprint closes the period-pinning vector it intentionally left behind.
