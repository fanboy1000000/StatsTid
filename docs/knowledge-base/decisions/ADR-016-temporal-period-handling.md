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

**D2 amendment 2026-10-19 (S145 Stage B, TASK-14520 / refinement B1; QUAL-149) — a fifth split behaviour, `timeline-aware`.** In plain terms: since S145 Stage A, overtime, the 37-hour norm check and flex are evaluated week by week across the whole month in one Rule Engine call, each day under that day's own contract (QUAL-186). Such a rule is never cut at a segment boundary, so none of the four original behaviours describes it. The split-behaviour axis gains **`timeline-aware`: "evaluated once across the whole plan, with per-day context; never split"** (`SplitBehavior.TimelineAware`, `SplitBehavior.cs:28-45`). `OVERTIME_CALC`, `NORM_CHECK_37H` and `FLEX_BALANCE` are classified `timeline-aware`, family `calculation` (`RuleRegistry.cs:83-94`). `NORM_CHECK_37H` (the mode-dispatching norm id payroll calls; until S145 an unregistered alias whose merge fell back to `Concatenate` with a warning) is now registered, so the registry classifies 17 rules (inventory below, row 8a). For a timeline-aware rule the span is descriptive only, because the planner reads no span (`RuleRegistry.cs:87-91`): overtime keeps `window` (its week), flex keeps `cross-period` (its carried balance), and `NORM_CHECK_37H` takes `period` (its totals cover the period's own days). The value is appended last to the enum, because the enum crosses the wire as an integer (`GET /api/rules/classifications`). Every other rule keeps its classification.

### D3 — Per-family merge with per-rule overrides

Two standalone mergers in SharedKernel: `CalculationResultMerger` and `ComplianceCheckResultMerger`. Pure static methods; no `MergeWith` on the result types themselves.

Default `MergeStrategy` derived from the `(span, splitBehavior, family)` triple:
- `(entry, segment-safe, calculation)` → `Concatenate`
- `(window, aligned-window, *)` → `RejectIfMultipleSegments` (planner contract violation if reached)
- `(period, mergeable, calculation)` → no default; registration MUST supply an override (compile-time enforcement when `splitBehavior: Mergeable`)
- `(*, *, compliance)` → `UnionDedupe` always

Per-rule override via optional `mergeStrategy:` parameter on `Register`; required when default is undefined.

**D3 amendment 2026-10-19 (S145 Stage B, TASK-14520 / B1; rulings R-68, R-75) — the default merge for a timeline-aware rule.** In plain terms: a timeline-aware rule produces exactly one answer for the whole month, so the merge step has nothing to combine. Its job is to pass that answer through untouched, and to fail loudly if it ever receives two.
- `(*, timeline-aware, calculation)` → **`SinglePlanEvaluation`** (`MergeStrategy.cs:56-64`; `RuleRegistry.cs:188`). One result passes through unchanged, its period figures intact (`Concatenate` would null them). Two or more results is a contract violation, never a refusable month: the merger throws a message-only `PlannerInvariantViolation` naming the rule and the result count, never a date, with `IsSplitRefusal` false, so it cannot be mistaken for the D4 split refusal (`CalculationResultMerger.cs:175-199`).
- `(*, timeline-aware, compliance)` → **no default** (R-68). `ComplianceCheckResultMerger` has no single-plan arm and throws on an unknown kind (`ComplianceCheckResultMerger.cs:62-70`), so a compliance registration must name its strategy, and one registered without it throws (`RuleRegistry.cs:184-190`). This is an exception to "`(*, *, compliance)` → `UnionDedupe` always" above, and not the only one: an aligned-window compliance rule (the two rest rules) already defaults to `RejectIfMultipleSegments` (`RuleRegistry.ResolveDefault`), as the `(window, aligned-window, *)` line says. The "always" predates both (corrected S145, from the O-4 drafting). No compliance rule is timeline-aware today.
- `FLEX_BALANCE`'s per-rule `Custom` override (the chained-carry merge, inventory row 16) is retired: flex takes the default.
- What payroll hands the merger (R-75): for a `SinglePlanEvaluation` rule, the timeline call's one unsplit row; the per-segment cut of that row is kept for wage-type mapping only (`PeriodCalculationService.cs:2494-2506, :2570-2578`). Whether a merger failure should refuse the whole calculation is recorded as an open question under D10 (S145, B.2 review).

### D4 — Default-reject at invalid segmentation boundaries; opt-in upstream alignment

`PlannerOptions.AllowUpstreamAlignment` defaults to `false`. When `false` and an `aligned-window` rule disagrees with the period boundary, the planner returns a structured error (NOT a `PlannedCalculation`) listing the offending rule(s).

When `true` and there's disagreement, the planner **shrinks** (not expands) the period to the rule's natural window edge and audit-annotates the manifest with `boundary_realigned`. Expansion is more surprising than shrink and ruled out.

`(reject, *, *)` rules ignore the flag — period straddle is always 4xx. `reject` is a first-class option (Step 0b BLOCKER B2); the framework does not pre-commit to "evaluate union with dominant config" as the only fallback.

**D4 amendment 2026-09-02 (S137 TASK-13707, owner-ruled — see ADR-040 §Consequences):** the refusal keys on
EVALUATED segments, not on interior boundaries. With ADR-040 D5's typed EMPLOYED/NOT_EMPLOYED segments, a
boundary that merely TRUNCATES evaluation (a hire or leave edge — the NOT_EMPLOYED side evaluates nothing)
is not a split: an `AlignedWindow` or `Reject` rule runs exactly once, over a shorter span, and its result
reaches the merger as a single segment. The planner therefore refuses iff such a rule would be evaluated in
≥ 2 EMPLOYED segments (a profile change while employed; two spells in one month). For callers without
employment windows every segment is EMPLOYED, so the pre-S137 behavior is unchanged: any interior boundary
refuses. The former `HasAnyInteriorBoundary` source-mirror is retired — counting typed segments is complete
by construction over every boundary source. The genuine-split refusal in the live rule set remains the S64
F4-1(b) follow-up, re-registered as QUAL-149.

**D4 amendment 2026-10-19 (S145 Stage B, TASK-14520 and TASK-14522 / refinement B1–B3; rulings R-56, R-73, R-74, R-76) — payroll's split months are paid part by part; the refusal stays as a contract for callers that hand in a refusing rule.** In plain terms: before Stage B a month with a mid-month change in hours, position, category or agreement was refused, because the rule set payroll handed the planner held "never cut in two" rules. Two changes end that for payroll without touching the refusal itself:
1. The three rules payroll evaluates over the whole month are timeline-aware (D2 amendment), and the planner refuses only on `reject` and `aligned-window` (`PeriodPlanner.cs:376-411`).
2. Payroll hands the planner only the classifications of the six rules it evaluates: `NORM_CHECK_37H`, `OVERTIME_CALC` and `FLEX_BALANCE` (one timeline call for the whole plan) and `SUPPLEMENT_CALC`, `ON_CALL_DUTY` and `ABSENCE_CALC` (per segment). None of them refuses (TASK-14522 contract 1, `EvaluatedRuleIds`, `PeriodCalculationService.cs:1761-1807`, applied at `:1749`). The registry's remaining `aligned-window` rules are not evaluated by payroll: `NORM_CHECK_WEEKLY`, now first in registration order and so the rule a refused split names (R-73), and the daily and weekly rest rules.

The refusal itself is unchanged: any caller that hands the planner an `aligned-window` or `reject` rule still gets it when that rule would be evaluated in ≥ 2 EMPLOYED segments (the Rule Engine's whole registry, or a test fixture set). Payroll's calculating routes pay the month part by part: each line is filed under the EMPLOYED segment holding its date and mapped with that segment's dated wage-type key (ADR-020 D1, S145 amendment), and a line dated in no EMPLOYED segment fails with a 500 and nothing written (R-56). What still stops a payroll month comes after planning and before any rule call: the week check (an agreement change inside a settled week, on a day other than its Monday; 422 `reason: agreement-change-mid-week` on each of the four routes that plan a month, `/calculate-and-export`, `/recalculate`, `/export` and `/export-period`, but not `/export-corrections`, which plans nothing; the manual residual QUAL-209); the raw `/export` and `/export-period` routes' own refusal of any month with ≥ 2 EMPLOYED parts, because they map every line under one profile (422 `reason: split-month-single-profile-route`, Q6 (a), R-76; order planner → week check → this, R-74); and the months-in-order 409 on the calculating routes. Neither the shrink stub nor a pro-rating merger was used. **QUAL-149: BUILT** (S145 Stage B, merged `f6fac97b`; evidence: all four Stage B runs matched in CI, S145 C-6/C-7). The S137 amendment's last sentence ("the genuine-split refusal in the live rule set remains … QUAL-149") is superseded. One asymmetry is recorded, not acted on: planning decides whether to gather snapshots from the six-rule set, while a replay's `FromManifest` re-checks snapshot contracts against the whole list; no rule declares a contract today (5a NOTE N-7 on TASK-14522; `PeriodCalculationService.cs:1793-1796`).

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

**D5b reconciliation (S29 / TASK-2910 — wage-type-mapping is the fourth pattern)**: D5b's original framing enumerated three options for handling non-dated boundary sources (snapshot-at-calc / point-in-time / versioned-history). S29 chose a **fourth pattern** not in the original trichotomy for `wage_type_mappings`: **export-time effective-date lookup** (deterministic dated read at the consumer boundary, NOT at the planner boundary). Justification: WTM is consumed only at the export boundary (`PCS.MapSegmentToExportLinesAsync`); planner-level snapshot of the resolved row adds no value when the consumer can do a deterministic dated read using a replay-stable natural-key triple. The triple flows through `SegmentSnapshot.Values["WtmNaturalKey"]` via the ADR-020 D1 planner-enrollment seam — so the segment manifest persists the *inputs* needed to re-resolve the row at replay time (the natural-key triple), not the resolved row itself. At export time, `PayrollMappingService.GetMappingAsync(asOfDate: segment.StartDate)` routes to `WageTypeMappingRepository.GetByKeyAtAsync`, which replicates the existing `(position = @position OR position = '')` position-fallback at the dated predicate `effective_from <= asOfDate AND (effective_to IS NULL OR effective_to > asOfDate)`. ADR-016 D10 replay-determinism is satisfied via the export-time predicate, NOT via D11's manifest-snapshot mechanism. See [ADR-018 D14](ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md#d14--wage-type-mapping-versioned-history-uses-surrogate-uuid-pk--effective-dating--export-time-effective-date-lookup-s29) for the full WTM-specific decision.

**Phase 4d-2 / 4d-3 inheritance question**: whether entitlement_configs (Phase 4d-2) and employees (Phase 4d-3) inherit ADR-020's `RegisterSnapshotContract` API verbatim depends on whether their consumers are similarly export-bounded. If they're consumed only at the export boundary (like WTM), the fourth pattern can be reused — the natural-key triple flows through the planner seam and the consumer does a dated read. If they're consumed at planner time (e.g., entitlement quota checks during forward calculation), actual snapshot consumption is required and the planner-enrollment seam must capture the resolved value, not just the natural key. Phase 4d-2 refinement adjudicates.

**D5b reconciliation (S30 / TASK-3011 — entitlement-policy is the fifth pattern)**: Phase 4d-2 adjudication landed on a fifth pattern — **consumption-time effective-date lookup at HTTP-endpoint boundaries** (NOT at planner time, NOT at export time, NOT via `SegmentManifest` at all). Entitlement consumers are HTTP endpoints (`SkemaEndpoints.MapPost` quota check + `BalanceEndpoints.MapGet` summary) that resolve the entitlement-year-start config directly via a two-step pattern: (a) live read for the open row's `ResetMonth` (frozen per natural key per [ADR-021 D5](ADR-021-entitlement-policy-versioned-history.md#d5--reset_month--accrual_model-are-frozen-from-admin-scope-q1-sub-fork-i)) → (b) derive `entitlementYearStart` → (c) dated read via `EntitlementConfigRepository.GetByTypeAtAsync(asOfDate=entitlementYearStart)`. There is no segment expansion, no `SegmentManifest` participation, no planner enrollment — the consumer reads the dated row at request time and gets replay-stable behavior because the dated predicate is deterministic on `effective_from <= asOfDate AND (effective_to IS NULL OR effective_to > asOfDate)`. The marquee D-test `EntitlementQuotaCheck_UsesYearStartConfig_NotCurrentConfig` proves the invariant: admin edits quota 25 → 27 today; both Skema quota validation and Balance summary report year-Y-start's 25 (not the live 27) for an already-started entitlement year. See [ADR-021 D4](ADR-021-entitlement-policy-versioned-history.md#d4--consumption-time-lookup-two-step-pattern-the-new-pattern) for the full pattern specification including the bulk-read live-filter (Step 7a fix #1 on `BalanceEndpoints`). The five-pattern landscape is now: (1) planner-routed dated lookup via `SegmentManifest` (original D5), (2) local-profile activation via `BoundaryCause.LocalProfileActivation` (ADR-017), (3) atomic outbox-bound effective-date lookup (ADR-018 D14), (4) export-time effective-date lookup with natural-key flowing through snapshot (S29 / WTM), (5) consumption-time effective-date lookup at HTTP-endpoint boundaries (S30 / entitlement-policy).

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
- `manifest_id UUID PRIMARY KEY`, `period_start DATE NOT NULL`, `period_end DATE NOT NULL`, `employee_id TEXT NOT NULL`, `calculation_kind TEXT NOT NULL` (`forward-calc` / `retroactive-correction` / `replay`), `boundary_cause_summary TEXT[] NOT NULL` (deduped), `created_at TIMESTAMPTZ NOT NULL`, `segments_jsonb JSONB NOT NULL`.
- Indexes: `(employee_id, period_start)`; GIN on `boundary_cause_summary`.
- Rebuildable from events.

**D10 amendment 2026-05-01** (during Phase 3 wave 1 review): `employee_id` column type and `SegmentManifest.EmployeeId` / `PlannedCalculation.EmployeeId` / `SegmentManifestCreated.EmployeeId` field types changed from `UUID` / `Guid` to `TEXT` / `string` to align with the rest of the schema (every other employee-id column in `init.sql` — `users.user_id`, `time_entries.employee_id`, `payroll_export_lines.employee_id` (in-memory), etc. — is `TEXT`/`string`). The original D10 spec wrote `UUID` without surfacing the schema-wide `TEXT` convention. Phase 3 wave 1 Reviewer flagged the divergence: the Payroll agent's TASK-2008 implementation had to synthesize a SHA-256-derived UUID for non-Guid string ids (e.g. `"emp-1"` test fixtures), which created an audit-cliff where `segment_manifests.employee_id` could never join to any other employee-keyed table without re-running the same derivation. Fix is forward-only: amend `init.sql` schema (no migration needed since no production DB yet exists with this table), amend SharedKernel Segmentation types, drop the SHA-256 derivation, drop the `::uuid` cast in TASK-2011's projection-rebuild SQL.

**Linkage** (amended 2026-04-29 during Phase 1 — `payroll_export_lines` is an in-memory C# model, not a persisted DB table; original D10 spec wrongly assumed DB-side per-line linkage. The audit chain below covers the same need without inventing new persistence.):
- `CalculationResult.ManifestId Guid` (additive; PAT-006 amendment) — in-memory + event payload.
- `audit_log.payload_jsonb` calculation entries carry `manifest_id` — additive, queryable.
- SLS export file format carries per-line OK-version stamping in file content (TASK-2010) — file-side, not DB-side.
- `segment_manifests` projection (queryable manifest history) joined to `audit_log` via `manifest_id` is the audit query path; per-line DB persistence of export lines is explicitly out of scope and recorded as a deferred question (revisit if/when product needs query-by-line history).

**Replay**: `PeriodCalculationService.ReplayAsync(Guid manifestId, CancellationToken)` returns a `CalculationResult` with the **same** `ManifestId` (replay does NOT mint a new manifest). Internal `PlannedCalculation.FromManifest(SegmentManifest)` ctor; subject to identical D9 invariants. Rules read snapshots from `SegmentSnapshot` in the reconstructed plan, NOT from the live DB. **Recomputation** (fresh snapshots, new manifest) is a separate operation; replay and recomputation never conflated.

**D10 amendment 2026-10-15 (S145 TASK-14513, merged `07337ea`; refinement A7; the Fable seat's R4; rulings R14, R15, R41, R42 and R-11..R-25) — the replay contract: freeze what is data, fingerprint what is code.** In plain terms: a replay is the proof, after the fact, that a payroll figure is what the rules give for the inputs it had. Before S145 the paragraph above was a promise the code did not keep: replay re-resolved the employee's profile from current dated history and took the hours and the opening flex balance from its caller, so a later correction could change what a replay showed. Since S145 a calculation stores what it ran on. Inputs that are **data** are frozen in the manifest or pinned by id; inputs that are **code** (agreement configs, the arithmetic) cannot be stored, so their fingerprint is. A replay re-runs on exactly those inputs and either reproduces the result or refuses by name; it never recomputes from today's data. *(One narrow exception, recorded at S145 Step 7a: the wage-type mapping is a dated table read at replay, not frozen. A mapping edit takes effect from the day it is made, so a past period's codes never change; but a segment that starts on or after the day of an edit, such as a month calculated on its last day with a segment starting that day, would replay with the new codes and would not be refused. Below the register's Medium floor; recorded here so the promise above is not read as absolute.)* This realises this decision's "NOT from the live DB" and D5b's snapshot claim.
- **The plan-level slot.** `SegmentManifest` gains `PlanInputs` (`PlanInputs.cs`): one per plan, beside the per-segment snapshots. It is carried by the event `SegmentManifestCreated.PlanInputs`, the record of truth, and by the projection column `segment_manifests.plan_inputs_jsonb` (nullable; the rebuilder copies the event's own serialized value). It holds:
  - the **frozen pieces** `(from, to, fraction, agreement, position)` for every EMPLOYED day evaluated, the opening week's context days included; both replay lanes build the employment profile from the piece covering a day and never ask the dated resolver;
  - the **settlement cutoff** (R41) and the **opening flex balance**; a replay takes no balance from its caller;
  - the **hours binding**: the event ids of the time-entry and absence rows read (sorted ordinally), the highest `outbox_id` consumed (`AsOfOutboxId`, recorded but not verified, because a rebuild re-stamps it; R-14), and a **lossless digest** of each row set exactly as read (form below);
  - the Rule Engine's **config fingerprints** (one per call) and its **evaluation-model marker**.

  A missing member is a damaged record: every non-nullable member carries `[JsonRequired]`, so decoding throws instead of reading a missing balance as 0 (R-18). Only the cutoff and a piece's position may be absent.
- **Verify, then refuse by name.** `ReplayAsync(manifestId)` takes the manifest id alone, plus the auth header and correlation id for the engine call, restoring this decision's inputs-free signature (R14; `PeriodCalculationService.cs:2125-2242`). In order: load the manifest (the projection first, the event store as fallback); no complete frozen inputs → `replay-manifest-without-frozen-inputs`; build the plan from the manifest and the evaluation inputs from the frozen pieces and cutoff alone; re-read the recorded hours rows **by event id, current or superseded** (ADR-028 D6), where a genuinely absent id → `replay-entries-missing`; recompute the digests → `replay-entries-digest-mismatch` / `replay-absences-digest-mismatch`; evaluate on the frozen opening balance, minting no manifest and no audit event; compare the engine's marker, then its fingerprints → `replay-evaluation-model-changed` / `replay-config-changed`. A refusal writes nothing. A failed engine call returns the refused result, as a forward calculation does (R-6). Replay is evaluation only, never admission (R42): it never consults the prior-month approval, the employment window, the profile or agreement history, or the current hours rows, so it is never refused for what is true today. Its one configuration read is the dated wage-type mapping (`wage_type_mappings`), looked up with each segment's frozen key at the segment's start exactly as the forward run did (`PeriodCalculationService.cs:3924-3926` → `PayrollMappingService.cs:60-61`); mapping edits must be dated today (`WageTypeMappingEndpoints.cs:105-121`, `:424-442`), so a past period maps as it did *(corrected S145 Step 7a, W-2)*.
- **A found row is never substituted (R-17).** The event-store fallback covers only "the projection could not be consulted" and "no row". A row that *is* found but whose stored inputs cannot be decoded is refused by name, and undecodable segments throw; a NULL slot does not fall back to the event either (R-14). Refusing costs nothing, because the projection is one rebuild away, while substituting would hide a damaged projection from the operator who should rebuild it.
- **A read-only cross-context contract on the two hours tables.** Payroll reads the Backend-owned `time_entries_projection` and `absences_projection` through the Infrastructure repositories, on the ADR-034 D4 precedent, and never writes them; `ProjectionHoursReader` is the only Payroll type allowed to name those repositories (R25; `ProjectionHoursReader.cs:7-25`). A calculation reads the employee's **current** rows dated from the day before its opening week's Monday to the period end, in one read-only REPEATABLE READ snapshot (QUAL-200). A replay reads **by event id, regardless of supersession**. The rows are keyed by an immutable `event_id`, no code deletes one, and a re-saved cell's earlier row is marked superseded, not removed (ADR-028 D6), so a replay survives a later re-save.
- **The marker convention (R-21).** The evaluation-model marker (`weekly-timeline-v1`, `WeeklyTimelineCore.cs:134`) names the arithmetic of **every rule endpoint payroll calls**: the timeline endpoint's week-by-week core and the per-segment lane's supplement, on-call and absence rules, although the engine returns it from the timeline call only. That coverage holds by convention: a change to any of those rules' arithmetic, or a rule that starts reading another field of an hours row, bumps the marker. A plan that makes no engine call (no EMPLOYED segment) freezes the dedicated marker `no-engine-call` (`PeriodCalculationService.cs:192-210`).
- **The fingerprint's canonical text is a contract** (transcribed from `ConfigFingerprint.cs:21-38`; pinned exactly by TASK-14506's `Timeline_ConfigFingerprint_*` facts and mutations MU-18 and MU-19). The engine computes it over the configs it actually resolved; payroll records what ran and never re-resolves.
  1. Each (agreement, OK version, position, config) becomes one text block: a header line `config|{AgreementCode}|{OkVersion}|{Position}` (a null position → empty), then one line per public instance property of `AgreementRuleConfig` as `Name=Value`, in ordinal order of the property name. Values: decimals value-normalised in invariant culture (no trailing zeros: `37.0` → `37`), integers invariant, booleans `true`/`false`, enums by name, strings as-is, null as empty. A property of any other type throws, so a future property can never be silently left out.
  2. Identical blocks are counted once; blocks are sorted ordinally by (agreement, OK version, position), then by block text.
  3. The blocks are joined with `\n`, encoded UTF-8 and hashed with SHA-256; the result is lowercase hex (64 characters).
- **The hours digest's canonical form** (R15 as amended by R-13; `HoursDigest.cs:31-44, :57-68`). One line per row: `EventId|Date|StartTime|EndTime|Hours|hex(ActivityType)|VoluntaryUnsocialHours` for entries, `EventId|Date|hex(AbsenceType)|Hours` for absences. The event id in `D` form, the date `yyyy-MM-dd`, times `HH:mm:ss.fffffff` or empty, hours an invariant value-normalised decimal (no rounding), booleans `true`/`false`, and the two typed texts as the uppercase hex of their UTF-8 bytes, so a `|` or a newline typed into free text cannot make two different row lists hash alike. Lines are sorted ordinally, joined with `\n` and hashed with SHA-256 (lowercase hex). A column that changes after a row is written (`superseded_by_outbox_id`; the vacation revaluation's `feriedage`) is deliberately not in a line.
- **Pre-S145 manifests are refused**, an exception recorded against "existing manifests stay replayable" (Follow-up sprints, below): a manifest without frozen inputs is refused, not replayed. None exists: under the reseed ruling no pre-S145 database is upgraded, and `ReplayAsync` has no production caller. The old arithmetic is not kept to replay old manifests; git keeps the old binary, and the marker names it.

**D10 amendment 2026-10-16 (S145 Stage B, ruling R-57; built by TASK-14522):** For a month with more than one EMPLOYED part, the result's `AgreementCode` and the correction event's `AgreementCode`/`Position` name the FIRST part (R-11/R-12, S145). The manifest's frozen pieces carry every part; readers follow `ManifestId`. Residual: QUAL-211.

*(Context for that amendment: since R-11 and R-12 these labels report the agreement and position the month was evaluated under, taken from the frozen first EMPLOYED piece, never from the request body; and the correction event links its manifest, R-19.)*

**Open question, recorded neutrally at the S145 B.2 review (Codex NOTE = Opus N-6 on TASK-14522, 2026-10-19).** When the merger fails a rule (two results for a single-plan rule, a split reaching `RejectIfMultipleSegments`, or `Custom` without a delegate), payroll records that rule as a failed row (`Success = false`, no lines, the merger's message) and the calculation itself continues as a success (`PeriodCalculationService.cs:2605-2625`). That is existing behaviour, kept by TASK-14522's contract 2. It is unreachable in the live registry: a timeline reply without exactly one row for each of the three timeline rules is refused earlier as a failed engine call (`TimelineReplyDefect`, `:4252-4289`), and the export lines are mapped before the merge, so no paid line depends on it. The question, below the quality register's Medium floor: **should a merger invariant failure refuse the whole calculation, rather than leave a failed report row under `Success`?** Not decided.

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
| 6 | `RestPeriodRule.MAX_DAILY_HOURS` | window (day) | segment-safe | compliance | UnionDedupe | Day is atomic at date-aligned boundaries **only after input normalization (ADR-039, S132)**: a midnight-crossing stint is split per-day BEFORE segmentation, so the per-segment date filter (`PeriodCalculationService.cs:983`, after the normalizer at `:861`) sees clock-correct per-day rows. Enforced by ORDERING (normalize-before-filter) — NOT by rejecting `EndTime≤StartTime` (a normalized pre-half is legitimately `[Start→00:00]`). A month-end crossing's post-midnight half, dated the next month's first day, is dropped by this month's filter and counted by the next month, whose hours read starts the day before its opening Monday (`HoursReadStart`, `:3036-3037`; the reasoning at `:863-876`). *(Corrected S145 Step 7a, N-5: the fail-closed `AssertNoDroppedBoundaryCrossing` guard this row named was retired with that read, TASK-14508 fix-up `37ab341`, QUAL-144; pinned by `MonthEndCrossingTests`, mutation MU-43.)* |
| 7 | `OvertimeRule` | window (week) | aligned-window; **timeline-aware since S145 B1** | calculation | RejectIfMultipleSegments; **SinglePlanEvaluation since S145 B1** | Period must be ≤ 1 week or week-aligned. Never enforced for a one-segment month, which is how payroll came to judge a whole month against one week's norm (QUAL-186). Since S145: evaluated week by week over the whole plan in one timeline call; each week settled and dated on its Sunday (Q3 = F1) |
| 8 | `NormCheckRule.WEEKLY` | window (week) | aligned-window | calculation | RejectIfMultipleSegments | Default 1-week norm window. Since S145 not evaluated by payroll, and the first aligned-window rule in registration order, so the rule a refused split names (R-73) |
| 8a | `NormCheckRule` as `NORM_CHECK_37H` (the mode-dispatching id payroll calls), **registered S145 B1** | period (descriptive) | timeline-aware | calculation | SinglePlanEvaluation | Until S145 an unregistered alias whose merge fell back to `Concatenate`, nulling the norm totals. Its totals cover the period's own EMPLOYED days, as sums of per-day norms (Q2 = A) |
| 9 | `RestPeriodRule.DAILY_REST` | window (day-pair) | aligned-window | compliance | RejectIfMultipleSegments | Pair straddles boundary if boundary mid-pair |
| 10 | `RestPeriodRule.WEEKLY_REST` | window (7-day sliding) | aligned-window | compliance | RejectIfMultipleSegments | 7-day sliding window |
| 11 | `NormCheckRule.MULTI_WEEK` | period (2/4/8/12 weeks) | mergeable | calculation | **Per-rule override required** | Sum hours per segment; recompute norm per segment-config; sum & report |
| 12 | `NormCheckRule.ANNUAL_ACTIVITY` | period (annual, pro-rated) | mergeable | calculation | **Per-rule override required** | Pro-rate annual norm by segment days; sum |
| 13 | `RestPeriodRule.WEEKLY_MAX_HOURS_48H` | period | mergeable | compliance | UnionDedupe | Per-segment 48h average; segment-config supplies cap |
| 14 | `OvertimeGovernanceRule.MaxOvertime` | period | mergeable | compliance | UnionDedupe | Per-segment ceiling check |
| 15 | `OvertimeGovernanceRule.PreApproval` | period | mergeable | compliance | UnionDedupe | Pre-approval state may change at boundary |
| 16 | `FlexBalanceRule` | cross-period | mergeable; **timeline-aware since S145 B1** | calculation | Per-rule override (chained-carry) until S145; **SinglePlanEvaluation since S145 B1** (the `Custom` override retired) | Until S145: segment k+1's `previousBalance` = segment k's `NewBalance`. Since S145: settled once per period, on the last EMPLOYED day, at the cap in force that day, over the period's own days; the next period opens from the clamped `NewBalance` (caller-supplied today, QUAL-188) |
| 17 | `EntitlementValidationRule` | n/a | not-segmented | special (`ValidateEntitlementResponse`) | n/a | Request-validator; not period-based; outside framework |

Populated `(span, split-behavior)` pairs: `(entry, segment-safe)`, `(window, segment-safe)`, `(window, aligned-window)`, `(period, mergeable)`, `(cross-period, mergeable)` — **5 distinct pairs**, exceeding the D11 floor of 4.

Multi-mode decomposition (D2): `NormCheckRule` registers as 3 rules (`NORM_CHECK_WEEKLY`, `NORM_CHECK_MULTIWEEK`, `NORM_CHECK_ANNUAL`); `RestPeriodRule` registers as 4 rules (`REST_PERIOD_MAX_DAILY`, `REST_PERIOD_DAILY_REST`, `REST_PERIOD_WEEKLY_REST`, `REST_PERIOD_48H_CEILING`); `OvertimeGovernanceRule` registers as 2 rules. Total registered rules after refactor: **16 segmentation-classified + 1 out-of-scope (`EntitlementValidationRule`) = 17 total rules** (TASK-2006 verification 2026-04-30; original "20" figure in this ADR was an arithmetic miscount — 5 entry-segment-safe + 1 window-segment-safe-compliance + 4 window-aligned-window + 5 period-mergeable + 1 cross-period-mergeable = 16 segmentation-classified).

**Inventory amendment 2026-10-19 (S145 B1, TASK-14520).** `NORM_CHECK_37H` (row 8a) is now registered, so the registry classifies **17 rules + 1 out-of-scope (`EntitlementValidationRule`) = 18** (`RuleRegistry.cs:74-123`; its own count at `:125-128`). By split behaviour: 5 entry segment-safe + 1 window segment-safe (compliance) + 3 aligned-window (the weekly norm and the two rest rules) + 3 timeline-aware (rows 7, 8a, 16) + 5 period mergeable = 17. Among the populated `(span, split-behavior)` pairs, `(cross-period, mergeable)` is now empty, and `timeline-aware` appears with three spans that are descriptive only (D2 amendment), so the planner sees one new behaviour, not three new cells.

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
- **Phase 4 — Versioned History for Non-Dated Boundary Sources** (3 sub-sprints): wage-type-mapping, entitlement-policy, employee-profile. Hard dep on S20's `SnapshotContract` + manifest. Each source moves from snapshot-at-calculation to effective-dated lookup; existing manifests stay replayable. **Exception recorded S145 (refinement A7; D10 amendment 2026-10-15):** a manifest written before S145 carries no frozen inputs and is refused at replay (`replay-manifest-without-frozen-inputs`), not replayed; none exists (reseed, don't migrate). "Existing manifests stay replayable" holds for manifests written since S145.
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
