# Sprint 20 — Temporal Period Handling

| Field | Value |
|-------|-------|
| **Sprint** | 20 |
| **Status** | analysis-phase (Step 0a complete; Step 0b in progress; no task log until ADR + classification + decomposition are user-approved) |
| **Start Date** | 2026-04-28 |
| **End Date** | TBD |
| **Orchestrator Approved** | no (pending Step 0b plan review + analysis deliverables) |
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
- **N1 + R-N2** (timebox analysis) — Planning Entrypoint adds 3-day analysis-phase timebox (2026-05-01 EOD) with default scope-reduction option and explicit exit criteria for "approved."
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

_In-progress walk through the Open Architectural Questions, recorded as decisions are made. Each entry binds the ADR draft. Q9–Q11 remain open at session end 2026-04-28; resume 2026-04-29._

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

### Q9–Q11 — Open

- **Q9**: Where is drift detected and surfaced? (build-time / runtime / both)
- **Q10**: Audit representation of a segmented run — segment manifest shape and persistence.
- **Q11**: Test strategy committed minimum matrix.

---

## Planning Entrypoint

No implementation tasks are defined yet. The sprint begins with the following **analysis-phase deliverables**, produced by the Orchestrator in collaboration with the user before any domain agent is spawned:

1. **Architectural ADR** — answering the eleven open questions above, proposing segmentation placement, the multi-axis rule classification, merge semantics per result family, and the segment-manifest shape.
2. **Rule classification inventory** — every existing rule (and every mode of multi-mode rules) tagged with its `(span, split-behavior, result-family)` triple.
3. **Task decomposition** — the ADR translated into `TASK-20NN` entries with domain agents, file scopes, and validation criteria, added to this sprint log under "## Task Log".
4. **Entropy scan (Step 0a)** — completed 2026-04-28; findings recorded above.
5. **Plan review (Step 0b)** — completed 2026-04-28; findings + resolution recorded above.

Only after items 1–3 are Orchestrator + user approved does Step 2 (Delegate) begin.

### Analysis-Phase Time-box (Step 0b NOTE N1 + R-N2)

If items 1–3 are not Orchestrator + user approved within **3 calendar days** of sprint start (i.e., by **2026-05-01 EOD**), the Orchestrator MUST halt and present the user with a scope-reduction option. Default reduction: ship OK-version-only (no extension framework, no manifest beyond the minimum needed for OK-boundary audit, no agreement-config / position-override extension points), and re-promote the broader framework to a future sprint.

Exit criteria for "approved":
- ADR is committed under `docs/knowledge-base/decisions/` with a chosen answer to every open question (or an explicit "deferred to S21+" with rationale);
- Rule classification inventory exists as a committed artifact (table in the ADR or a separate file under `docs/`);
- TASK-20NN entries appear in this sprint log's Task Log with agents and file scopes assigned;
- User confirms acceptance of the scope shape.

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
