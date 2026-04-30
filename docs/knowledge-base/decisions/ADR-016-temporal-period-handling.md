# ADR-016: Temporal Period Handling — Segmentation Framework

| Field | Value |
|-------|-------|
| **ID** | ADR-016 |
| **Status** | proposed (S20 analysis phase, 2026-04-29; moves to `approved` at sprint completion) |
| **Sprint** | S20 |
| **Domains** | Rule Engine, Payroll Integration, SharedKernel, Data Model |
| **Tags** | segmentation, ok-version, retroactive, manifest, replay, period-handling, planner |
| **Supersedes** | none |
| **Augments** | ADR-013 (no-cascade retroactive corrections), ADR-014 (DB-backed agreement configs), ADR-015 (ComplianceCheckResult), PAT-005 (RuleEngine HTTP-only), PAT-006 (unified rule endpoint response format) |

## Context

StatsTid calculates pay, balances, compliance, and entitlements over **periods** — weekly norm windows, monthly payroll runs, quarterly supplements, annual academic norms. Rules operate on a `(period, profile, config)` triple and assume each is internally consistent for the full span. That assumption breaks whenever a period intersects an **effective-date boundary** in any of its inputs: an OK collective-agreement transition (2026-04-01 OK24→OK26), a `DRAFT → ACTIVE` agreement-config promotion (ADR-014), a position-override `effective_from` (ADR-013, S11/S14), a wage-type-mapping revision, an employee profile change, or a compliance-rule version bump (ADR-015, S16).

Today, periods that straddle these boundaries exhibit three failure modes:

1. **Silent version pinning** — `PeriodCalculationService.CalculateAsync` resolves the OK version from `periodStart` only, logs warnings on per-entry drift, and proceeds. A 2026-03-30 → 2026-04-30 period silently computes the last 30 days under OK24 rules. TASK-1801 (S18) closed the caller-trust vector but intentionally did not change this period-pinning behaviour.
2. **Caller-responsibility asymmetry** — `RetroactiveCorrectionService.RecalculateWithVersionSplitAsync` splits at the OK transition for retroactive corrections only; the forward-calc path doesn't split. Callers are implicitly expected to split, but the contract is undocumented.
3. **Naive segmentation is wrong** — even if the service segmented automatically, splitting a weekly 37h norm across a Wednesday transition produces two partial-window evaluations whose results are meaningless. Rules are not segmentation-safe by construction.

P4 (OK version correctness) in CLAUDE.md states the invariant explicitly. Period-pinning violates P4 in cross-boundary periods. This is the highest-priority correctness gap remaining after S18/S19. Solving it tactically for OK version only (as TASK-1801 attempted) works for one transition; the boundary-source landscape is broader, so a framework is the correct long-term move — narrowed by Step 0b plan review (BLOCKER B3) to sources with auditable effective-dating already in place. Non-dated sources (employee profile, wage-type-mapping, entitlement-policy) get snapshot-at-calculation under this framework, with versioned-history migration as a Phase 4 follow-up.

## Decision

A SharedKernel-pure period planner, multi-axis rule classification at registration, per-family merge with per-rule overrides, event-sourced segment manifest, and runtime invariants on planner output. Eleven sub-decisions:

### D1 — Segmentation lives in SharedKernel as a pure planner

A `PeriodPlanner` lives under `src/SharedKernel/StatsTid.SharedKernel/Segmentation/`, consumed by `Payroll/Services/PeriodCalculationService`. Not in the RuleEngine assembly (PAT-005 forbids non-RuleEngine code from holding RuleEngine references; ADR-015 requires result-family-aware merging on the calling side). Not pushed to caller (would duplicate segmentation per ADR-013-asymmetry across every entry point).

`PlannedCalculation` carries an internal constructor — bypass attempts fail at compile time, the same compile-time-guard pattern as `AddStatsTidJwtAuth(IServiceCollection, IConfiguration, IHostEnvironment)` in TASK-1905. `PeriodCalculationService.CalculateAsync` accepts only a `PlannedCalculation` value as input.

### D2 — Rules declare a `(span, split-behavior, family)` triple at registration

`RuleRegistry.Register` gains required parameters: `span`, `splitBehavior`, `family`. Multi-mode rules (e.g., `NormCheckRule` operating in WEEKLY / MULTI-WEEK / ANNUAL_ACTIVITY modes) decompose into separately-registered rules; the dispatcher selects by classification + period, not by intra-rule branching. Compile-time enforcement that no rule reaches the planner unclassified.

The taxonomy is multi-axis (Step 0b BLOCKER B1):
- **Span**: `entry` / `window` / `period` / `cross-period` — where the rule's natural computation window sits relative to the calculation period.
- **Split behavior**: `segment-safe` / `aligned-window` / `mergeable` / `reject` — what MUST happen when a segmentation boundary falls inside the rule's evaluation span.
- **Family**: `calculation` (`CalculationResult`, PAT-006) / `compliance` (`ComplianceCheckResult`, ADR-015) — what kind of artifact the rule produces.

Full classification inventory below.

### D3 — Per-family merge with per-rule overrides

Two standalone mergers in SharedKernel: `CalculationResultMerger` and `ComplianceCheckResultMerger`. Pure static methods; no `MergeWith` on the result types themselves.

Default `MergeStrategy` derived from the `(span, splitBehavior, family)` triple:
- `(entry, segment-safe, calculation)` → `Concatenate`
- `(window, aligned-window, *)` → `RejectIfMultipleSegments` (planner contract violation if reached)
- `(period, mergeable, calculation)` → no default; registration MUST supply an override (compile-time enforcement when `splitBehavior: Mergeable`)
- `(*, *, compliance)` → `UnionDedupe` always

Per-rule override via optional `mergeStrategy:` parameter on `Register`; required when default is undefined.

### D4 — Default-reject at invalid segmentation boundaries; opt-in upstream alignment

`PlannerOptions.AllowUpstreamAlignment` defaults to `false`. When `false` and an `aligned-window` rule disagrees with the period boundary, the planner returns a structured error (NOT a `PlannedCalculation`) listing the offending rule(s).

When `true` and there's disagreement, the planner **shrinks** (not expands) the period to the rule's natural window edge and audit-annotates the manifest with `boundary_realigned`. Expansion is more surprising than shrink and ruled out.

`(reject, *, *)` rules ignore the flag — period straddle is always 4xx. `reject` is a first-class option (Step 0b BLOCKER B2); the framework does not pre-commit to "evaluate union with dominant config" as the only fallback.

### D5 — Implementation scope: OK-version end-to-end + extension points

In scope:
- Architecture & ADR for temporal segmentation, applicable to **implemented** boundary sources (OK version, agreement-config, position-override, EU WTD compliance ruleset).
- Rule classification under multi-axis taxonomy applied to all existing rules.
- Implementation covering OK-version boundary end-to-end, with extension points demonstrated for other implemented sources.
- Planner introduced; OK-boundary calculations route through it for named entry points (`/calculate-and-export`, `/export-period`, `RetroactiveCorrectionService`). Full retirement of `PeriodCalculationService` is OUT of scope (Step 0b WARNING W2).
- Mixed-version export (TASK-1903 absorbed): per-line OK-version stamping at the export boundary; replaces `OkVersionBoundary.ResolveProfile`'s collapse behavior at `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:325-339`. Per-line stamping consumes ONLY segment-resolved OK/config context produced by the planner — wage-type-mapping effective-dating does not leak in (Step 0b WARNING W3).

### D5b — Non-effective-dated boundary sources: snapshot-at-calculation

Wage-type-mapping, entitlement-policy, and employee-profile have no historical timeline (last-write-wins). Each registered rule declares a `SnapshotContract` (which non-dated tables / profile fields it reads); the planner gathers snapshots at calculation time and writes them to the segment manifest. Replay against a manifest reads from the manifest's snapshots, not the live DB — replay determinism becomes a property we can claim and verify; recomputation determinism stays unclaimed (we never had it).

**Long-term commitment**: option (c) — versioned history — committed to ROADMAP.md Phase 4 as "Versioned History for Non-Dated Boundary Sources" with three sub-sprints (wage-type-mapping, entitlement-policy, employee-profile). Hard dependency on S20's `SnapshotContract` + manifest. Locking (c) onto the roadmap prevents (b) from quietly becoming permanent.

### D6 — Retire `RecalculateWithVersionSplitAsync`

`RecalculateWithVersionSplitAsync` deletes outright. Confirmed only-internal-caller (single file: `RetroactiveCorrectionService.cs`); no external API to coordinate with. `RecalculateAsync` (public) keeps its signature; internal split logic replaced by a planner call.

ADR-013's no-cascade semantics for retroactive corrections move INTO the planner as an explicit merge-policy choice; they may not remain duplicated.

### D7 — List-based merge for arbitrary N segments

`PlannedCalculation.Segments` is `IReadOnlyList<PlannedSegment>`; length ≥ 1; no special-case branches for N=1 or N=2. `PlannedSegment` carries `(StartDate, EndDate, BoundaryCause, SegmentSnapshot)`.

Merge contract is **list-based**: `MergeStrategy.Apply(IReadOnlyList<TResult> segments)` — strategy operates on the whole list. Sidesteps merge-associativity invariant; no class of bug from fold order. Property-based associativity tests deferred.

### D8 — Always-invoke planner; perf budget 5/20/50 ms

Every `CalculateAsync` call goes through `Planner.Plan()` first, including non-straddling periods. Manifest is constructed for every calculation — uniform audit + replay determinism for both straddling and non-straddling cases.

Budget: ≤ 5 ms p50, ≤ 20 ms p95, hard 50 ms p99 ceiling on planner overhead for a non-straddling 1-employee 1-period call. Measurement: a regression test in the test matrix instruments planner wall-clock and asserts under-budget. Caching out of scope for S20.

### D9 — Drift detection: compile-time + Constraint Validator + runtime invariants

Three layers:
- **Compile-time**: `PlannedCalculation` ctor is `internal`; `InternalsVisibleTo` only the planner test project. `PeriodCalculationService.CalculateAsync` accepts only `PlannedCalculation`.
- **Static analysis**: Constraint Validator rule extends to flag any new code under `src/Integrations/` that posts to `/api/rules/evaluate` without routing through `PeriodCalculationService`.
- **Runtime invariants** asserted in `PlannedCalculation`'s internal ctor:
  - `Segments.Count ≥ 1`.
  - Segments sorted, non-overlapping, contiguous, `Segments.First().Start == period.Start && Segments.Last().End == period.End`.
  - For every rule with a `SnapshotContract`: every intersecting segment carries a snapshot.
  - Every rule has a non-null resolved `MergeStrategy`.
  - Violation throws `PlannerInvariantViolation : InvalidOperationException` with structured message.

Wire-level contract checks at the RuleEngine HTTP boundary and reflection seals deferred — PAT-005's convention-based enforcement has held for ~10 sprints; closed in-process deployable does not warrant the ceremony.

### D10 — Segment manifest: event-sourced + indexed projection

`SegmentManifest` is a record type in SharedKernel (not the same shape as `PlannedCalculation`); built from a `PlannedCalculation` via a mapping ctor.

**Event**: `SegmentManifestCreated` registered in `EventSerializer` (event count 43 → 44; DEP-003 update). `manifest_id` = event `Id`. (D10 amendment 2026-04-30: original spec said 34 → 35 based on a stale ADR-016 draft; the registry had grown across S6/S9/S12/S14/S15/S16/S17 to 43 entries by S20 dispatch — TASK-2007 verification confirmed 43 → 44.)

**Projection** in PostgreSQL: new table `segment_manifests`:
- `manifest_id UUID PRIMARY KEY`, `period_start DATE NOT NULL`, `period_end DATE NOT NULL`, `employee_id UUID NOT NULL`, `calculation_kind TEXT NOT NULL` (`forward-calc` / `retroactive-correction` / `replay`), `boundary_cause_summary TEXT[] NOT NULL` (deduped), `created_at TIMESTAMPTZ NOT NULL`, `segments_jsonb JSONB NOT NULL`.
- Indexes: `(employee_id, period_start)`; GIN on `boundary_cause_summary`.
- Rebuildable from events.

**Linkage** (amended 2026-04-29 during Phase 1 — `payroll_export_lines` is an in-memory C# model, not a persisted DB table; original D10 spec wrongly assumed DB-side per-line linkage. The audit chain below covers the same need without inventing new persistence.):
- `CalculationResult.ManifestId Guid` (additive; PAT-006 amendment) — in-memory + event payload.
- `audit_log.payload_jsonb` calculation entries carry `manifest_id` — additive, queryable.
- SLS export file format carries per-line OK-version stamping in file content (TASK-2010) — file-side, not DB-side.
- `segment_manifests` projection (queryable manifest history) joined to `audit_log` via `manifest_id` is the audit query path; per-line DB persistence of export lines is explicitly out of scope and recorded as a deferred question (revisit if/when product needs query-by-line history).

**Replay**: `PeriodCalculationService.ReplayAsync(Guid manifestId, CancellationToken)` returns a `CalculationResult` with the **same** `ManifestId` (replay does NOT mint a new manifest). Internal `PlannedCalculation.FromManifest(SegmentManifest)` ctor; subject to identical D9 invariants. Rules read snapshots from `SegmentSnapshot` in the reconstructed plan, NOT from the live DB. **Recomputation** (fresh snapshots, new manifest) is a separate operation; replay and recomputation never conflated.

**Migration**: `CREATE TABLE segment_manifests` + indexes only. **No** `ALTER TABLE payroll_export_lines` — that table does not exist in the schema today. **No backfill** required (no per-line column was added).

### D11 — Test strategy: committed minimum matrix

Cell coverage binds to the classification inventory below: one test per populated `(span, split-behavior)` cell. Floor: ≥ 4 distinct cells exercised. The inventory raises this to ≥ 5 (see below).

Counted minimum (22 new tests):
- `(span × split-behavior)` cells (≥ 4) — `tests/StatsTid.Tests.Unit/Segmentation/PlannerCellTests.cs`
- D9 invariant negatives (4) — `tests/StatsTid.Tests.Unit/Segmentation/PlannedCalculationInvariantTests.cs`
- D10 manifest creation (1) + replay (1) + projection rebuild (1) + replay-vs-recomputation (1) — `tests/StatsTid.Tests.Regression/Segmentation/Manifest*.cs`
- Boundary scenarios per implemented source × (valid + invalid) (8) — `BoundaryScenarioTests.cs`
- Mixed-version export (TASK-1903) (1) — `tests/StatsTid.Tests.Regression/Payroll/MixedVersionExportTests.cs`
- D8 perf budget assertion (1) — `PlannerPerfBudgetTests.cs`

Q7's three committed scenarios slot into boundary scenarios: 2-segment OK transition (OK-version valid); 3-segment OK + agreement-config simultaneous (agreement-config valid); 4-segment synthetic position-override (position-override valid).

Out of S20: full rule × scenario × boundary exhaustive matrix; frontend tests; property-based merge associativity tests; restructure of existing 35-test regression suite.

## Rule Classification Inventory

Every existing rule (and every mode of multi-mode rules) tagged with its `(span, split-behavior, family)` triple. Cells the planner cannot trivially split (`aligned-window`, `reject`) are flagged. `EntitlementValidationRule` is a request-validator, not a period rule — explicitly out of segmentation scope.

| # | Rule (mode) | Span | Split-behavior | Family | Default merge | Per-rule notes |
|---|---|---|---|---|---|---|
| 1 | `SupplementRule` | entry | segment-safe | calculation | Concatenate | Date-stamped per-entry |
| 2 | `OnCallDutyRule` | entry | segment-safe | calculation | Concatenate | Filters `ActivityType=ON_CALL` |
| 3 | `CallInWorkRule` | entry | segment-safe | calculation | Concatenate | Min-hours guarantee per entry |
| 4 | `TravelTimeRule` | entry | segment-safe | calculation | Concatenate | Filters TRAVEL_WORK / TRAVEL_NON_WORK |
| 5 | `AbsenceRule` | entry | segment-safe | calculation | Concatenate | Per-absence date-stamped |
| 6 | `RestPeriodRule.MAX_DAILY_HOURS` | window (day) | segment-safe | compliance | UnionDedupe | Day is atomic at date-aligned boundaries |
| 7 | `OvertimeRule` | window (week) | aligned-window | calculation | RejectIfMultipleSegments | Period must be ≤ 1 week or week-aligned |
| 8 | `NormCheckRule.WEEKLY` | window (week) | aligned-window | calculation | RejectIfMultipleSegments | Default 1-week norm window |
| 9 | `RestPeriodRule.DAILY_REST` | window (day-pair) | aligned-window | compliance | RejectIfMultipleSegments | Pair straddles boundary if boundary mid-pair |
| 10 | `RestPeriodRule.WEEKLY_REST` | window (7-day sliding) | aligned-window | compliance | RejectIfMultipleSegments | 7-day sliding window |
| 11 | `NormCheckRule.MULTI_WEEK` | period (2/4/8/12 weeks) | mergeable | calculation | **Per-rule override required** | Sum hours per segment; recompute norm per segment-config; sum & report |
| 12 | `NormCheckRule.ANNUAL_ACTIVITY` | period (annual, pro-rated) | mergeable | calculation | **Per-rule override required** | Pro-rate annual norm by segment days; sum |
| 13 | `RestPeriodRule.WEEKLY_MAX_HOURS_48H` | period | mergeable | compliance | UnionDedupe | Per-segment 48h average; segment-config supplies cap |
| 14 | `OvertimeGovernanceRule.MaxOvertime` | period | mergeable | compliance | UnionDedupe | Per-segment ceiling check |
| 15 | `OvertimeGovernanceRule.PreApproval` | period | mergeable | compliance | UnionDedupe | Pre-approval state may change at boundary |
| 16 | `FlexBalanceRule` | cross-period | mergeable | calculation | **Per-rule override required: chained-carry** | Segment k+1's `previousBalance` = segment k's `NewBalance` |
| 17 | `EntitlementValidationRule` | n/a | not-segmented | special (`ValidateEntitlementResponse`) | n/a | Request-validator; not period-based; outside framework |

Populated `(span, split-behavior)` pairs: `(entry, segment-safe)`, `(window, segment-safe)`, `(window, aligned-window)`, `(period, mergeable)`, `(cross-period, mergeable)` — **5 distinct pairs**, exceeding the D11 floor of 4.

Multi-mode decomposition (D2): `NormCheckRule` registers as 3 rules (`NORM_CHECK_WEEKLY`, `NORM_CHECK_MULTIWEEK`, `NORM_CHECK_ANNUAL`); `RestPeriodRule` registers as 4 rules (`REST_PERIOD_MAX_DAILY`, `REST_PERIOD_DAILY_REST`, `REST_PERIOD_WEEKLY_REST`, `REST_PERIOD_48H_CEILING`); `OvertimeGovernanceRule` registers as 2 rules. Total registered rules after refactor: **16 segmentation-classified + 1 out-of-scope (`EntitlementValidationRule`) = 17 total rules** (TASK-2006 verification 2026-04-30; original "20" figure in this ADR was an arithmetic miscount — 5 entry-segment-safe + 1 window-segment-safe-compliance + 4 window-aligned-window + 5 period-mergeable + 1 cross-period-mergeable = 16 segmentation-classified).

## Rationale

1. **P3 (event-sourcing determinism)** drives D10 (event + projection) and the D9 runtime invariants. A manifest with `SnapshotContract`-gathered data is the only honest claim of replay determinism for non-dated sources.
2. **P4 (OK version correctness)** is the headline correctness goal. D5 + D6 + the mixed-version export work close the silent-pinning vector and the forward/retroactive asymmetry.
3. **PAT-005 + PAT-006 + ADR-015** together eliminate single-axis taxonomy and same-result-shape merging. D2's multi-axis triple and D3's per-family mergers respect those constraints.
4. **ADR-013's no-cascade rule** moves into the planner via D6 — semantics preserved, duplication eliminated. Determinism (P3) requires ONE canonical segmenter.
5. **Step 0b BLOCKER B3** narrowed the framework to implemented effective-dated sources; D5b honors that by introducing `SnapshotContract` for non-dated sources and locking versioned-history migration onto Phase 4. Without that lock, snapshot-at-calculation could quietly become permanent and re-introduce non-determinism.
6. **D8's always-invoke planner** prevents a class of latent bugs where the manifest exists for some calculations and not others. Uniform audit shape > performance optimization for a non-hot path.

## Consequences

**Schema changes**:
- New table `segment_manifests` (8 columns + 2 indexes).
- (No column added to `payroll_export_lines` — that table does not exist as DB persistence; D10 amendment 2026-04-29.)

**Code changes (new types in SharedKernel)**:
- `Segmentation/PeriodPlanner.cs` — pure planner.
- `Segmentation/PlannedCalculation.cs` — internal ctor; D9 invariants asserted on construction.
- `Segmentation/PlannedSegment.cs` — `(StartDate, EndDate, BoundaryCause, SegmentSnapshot)`.
- `Segmentation/SegmentManifest.cs` — persisted form.
- `Segmentation/SnapshotContract.cs` — rule-declared snapshot scope for non-dated sources.
- `Segmentation/CalculationResultMerger.cs` + `ComplianceCheckResultMerger.cs`.
- `Segmentation/MergeStrategy.cs` — list-based contract.
- `Segmentation/PlannerOptions.cs` — `AllowUpstreamAlignment`.
- `Segmentation/PlannerInvariantViolation.cs`.

**Code changes (RuleEngine)**:
- `RuleRegistry.Register` signature gains `span`, `splitBehavior`, `family`, optional `mergeStrategy`. Compile-time enforcement on `Mergeable` without default.
- `NormCheckRule`, `RestPeriodRule`, `OvertimeGovernanceRule` decompose to multi-mode-registered entries.

**Code changes (Payroll Integration)**:
- `PeriodCalculationService.CalculateAsync(PlannedCalculation, …)` — sole entry point.
- `PeriodCalculationService.ReplayAsync(Guid manifestId, …)` — new replay primitive.
- `RetroactiveCorrectionService.RecalculateWithVersionSplitAsync` deleted; `RecalculateAsync` rewritten on top of planner.
- `OkVersionBoundary.ResolveProfile` collapse logic at `Program.cs:325-339` replaced by per-line OK-version stamping driven by manifest.
- `PayrollMappingService` and SLS export carry `manifest_id` end-to-end.

**Audit / observability**:
- `audit_log` calculation entries carry `manifest_id` in payload (additive, no schema change).
- `EventSerializer` registers `SegmentManifestCreated` (count 34 → 35, DEP-003 update).
- `CalculationResult.ManifestId` (additive PAT-006 amendment).

**Test obligations** (D11, floor 22 new tests; classification inventory may raise the cell-test count beyond 4).

**Follow-up sprints**:
- **Phase 4 — Versioned History for Non-Dated Boundary Sources** (3 sub-sprints): wage-type-mapping, entitlement-policy, employee-profile. Hard dep on S20's `SnapshotContract` + manifest. Each source moves from snapshot-at-calculation to effective-dated lookup; existing manifests stay replayable.
- **Exhaustive testing matrix** — full rule × scenario × boundary coverage as a follow-up sprint after S20 ships.
- **`PeriodCalculationService` retirement** — full replacement deferred (Step 0b WARNING W2). S20 routes named entry points through the planner; subsequent sprint can fully retire the legacy service.

**Risks**:
- Multi-mode rule decomposition (D2) is a real refactor of `NormCheckRule`, `RestPeriodRule`, `OvertimeGovernanceRule`. Mitigation: each decomposed mode is a separately-registered rule with focused unit tests; existing test coverage for the consolidated rules anchors the refactor.
- D5b's snapshot-at-calculation creates a "looks like determinism" pitfall. Mitigation: ADR explicitly distinguishes replay determinism (claimed, verified by D11 tests) from recomputation determinism (unclaimed); Phase 4 lock prevents indefinite drift.
- D8's always-invoke planner adds overhead to ~99% non-straddling calls. Mitigation: D8 perf-budget regression test catches order-of-magnitude regressions; caching is a follow-up if needed.

## Alternatives Considered

1. **Tactical OK-version-only fix** (extend TASK-1801's caller-trust pattern). Rejected — does not solve agreement-config or position-override boundaries; reproduces single-axis-taxonomy mistake; doesn't deliver auditable manifest.
2. **Single-axis taxonomy** (per-entry / window / period / cross-cutting only). Rejected — Step 0b BLOCKER B1 demonstrated overlap and exhaustiveness failures; ADR-015's compliance carve-out forces a separate result-family axis.
3. **Pushed-to-caller segmentation**. Rejected — duplicates segmentation per ADR-013-asymmetry; can't enforce ADR-013's no-cascade in one place.
4. **Segmentation inside RuleEngine assembly**. Rejected — PAT-005 forbids non-RuleEngine code from holding RuleEngine references; merge logic is family-aware (PAT-006 + ADR-015) and belongs on the calling side.
5. **Lazy planner invocation** (only on straddling periods). Rejected — bifurcates audit shape (some calls have manifests, some don't); breaks uniform replay determinism.
6. **Non-event-sourced manifest** (table-only, no event). Rejected — incoherent with priority 3; projection should be derivable from events.
7. **Single normalized 3-table schema** for manifest persistence. Rejected — over-normalizes for queries we won't run; hybrid `(indexed columns + JSONB segments)` matches actual access patterns.
8. **Wire-level HMAC seal at RuleEngine boundary**. Deferred — PAT-005 convention-based enforcement has held for ~10 sprints; closed in-process deployable doesn't warrant the ceremony.
9. **Versioned-history migrations folded into S20**. Rejected — would expand S20 from 1 sprint to 1 + 3 sub-sprints; D5b's snapshot-at-calculation + Phase 4 lock is the tighter scoping.

## References

- [SPRINT-20.md](../../sprints/SPRINT-20.md) — sprint log, Q1–Q11 decision log, Step 0a + 0b records
- [ROADMAP.md](../../../ROADMAP.md) — Phase 3h (S20) + Phase 4 versioned-history follow-up
- [ADR-002](ADR-002-pure-function-rule-engine.md) — rule purity (P2)
- [ADR-003](ADR-003-ok-version-resolved-by-entry-date.md) — OK-version resolution semantics
- [ADR-013](ADR-013-retroactive-corrections-single-period-no-cascade.md) — no-cascade semantics; moves into planner per D6
- [ADR-014](ADR-014-agreement-configs-database-backed.md) — DB-backed configs; planner consumes `active_from`
- [ADR-015](ADR-015-compliance-check-result-pattern.md) — `ComplianceCheckResult` separate return type; drives D2 family axis
- [PAT-005](../patterns/PAT-005-period-calculation-service-http-rule-evaluation.md) — RuleEngine HTTP-only
- [PAT-006](../patterns/PAT-006-unified-rule-endpoint-response-format.md) — calculation rules return `CalculationResult`-compatible (carve-out per ADR-015)
- [SPRINT-18.md](../../sprints/SPRINT-18.md) — TASK-1801 closed caller-trust vector; this sprint closes the period-pinning vector
- [SPRINT-19.md](../../sprints/SPRINT-19.md) — `OkVersionCanonicalization` testing-shape precedent (pure helper + thin service wrapper)
