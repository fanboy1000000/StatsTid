# ADR-023 — Employee-Profile Versioning Emission + Rule-Engine Cutover Architecture (Phase 4d-3 Part 2 Design)

| Field | Value |
|-------|-------|
| **Status** | DRAFT (cycle 1 review pending at TASK-3202) |
| **Sprint** | S32 (design-only sprint; produces this ADR for S33 implementation) |
| **Domains** | Backend, Infrastructure, Frontend, Data Model, Payroll Integration, SharedKernel |
| **Tags** | versioned-config, employee-profile, planner-snapshot, rule-engine-determinism, consumption-time-lookup, phase-4d, design-binding |
| **Supersedes** | none |
| **Amends** | none — ADR-022 §Implications "S32 commitment list" stands; this ADR fills in the architectural details S32 design must settle so S33 implementation refinement has zero ambiguity. ADR-016 D5b stays at 5 patterns (D1 below uses the WTM-precedent natural-key snapshot + consumption-time-lookup at PCS per-segment loop — same as pattern #4, not a new pattern). |

## Context

ADR-022 (S31) committed to landing employee-profile versioning emission + rule-engine cutover + planner-snapshot **as one logical sprint commit-group** because partial delivery re-opens the P4 retroactive replay window the S31 reframe eliminated. The S32-implementation refinement at `.claude/refinements/REFINEMENT-s32-phase-4d3-part2.md` attempted to deliver against that commitment list directly. Step 4 cycle 1 dual-lens review surfaced 2 convergent BLOCKERs (planner-enrollment seam shape mismatch + Q3 LEAVE not replay-safe for `agreement_code`). User picked option (a) "absorb in-refinement"; cycle 1 absorption proposed WTM-precedent verbatim seam + MIGRATE `agreement_code`+`employment_category` to `employee_profiles`. Step 4 cycle 2 surfaced **4 NEW BLOCKERs in the SAME architectural area as cycle 1**, including a fatal one — TASK-3206 proposed re-resolving the snapshot in `MapSegmentToCalculationAsync`, but that method **does not exist**; the WTM precedent (`MapSegmentToExportLinesAsync`) is payroll-only and runs AFTER rule evaluation, not before it. Per `feedback_thrash_defer_real_world.md`, this is the canonical "cycle-2-finds-NEW-blockers-in-SAME-area" smoke alarm. User chose defer-to-design-only (S28 → S29 split precedent for ADR-020 → WTM implementation).

This ADR settles the 7 architectural questions the deferral verdict enumerated. **Every decision below is binding for S33 implementation refinement** — precise enough that S33 refinement Step 4 should converge on a single mechanical path, not a per-decision architectural fork.

## Decisions

### D1 — PCS consumption-site location: per-segment `segmentProfile` construction at `PeriodCalculationService.cs:326-339`

The EmployeeProfile snapshot is re-resolved **inside the existing per-segment loop at `PeriodCalculationService.CalculateAsync`**, specifically replacing the current `segmentProfile` construction at lines 326-339. Today that block copies the caller-supplied profile and overrides only `OkVersion` per segment. S33 changes it to construct `segmentProfile` via `EmployeeProfileResolver.GetByEmployeeIdAtAsync(profile.EmployeeId, segment.StartDate)`, then applies the `OkVersion` server-resolution overlay (which stays separate per ADR-003) and a `Position` fallback to caller-supplied if resolver returns no Position (preserves TASK-1802 position-specific-override semantics for callers that supply position explicitly).

**Why this site, not `MapSegmentToExportLinesAsync`** (the WTM precedent location): MapSegmentToExportLinesAsync runs **after** rule evaluation on each segment — its scope is mapping rule outputs to payroll wage types. The EmployeeProfile fields (`WeeklyNormHours`, `PartTimeFraction`, `Position`) are consumed **inside** rule evaluation by `NormCheckRule.cs:174` (`profile.WeeklyNormHours * profile.PartTimeFraction`) and `RuleRegistry.cs:211/L258` (position-aware agreement-config lookup). Re-resolution must happen BEFORE `EvaluateSegmentAsync` builds the per-segment rule payload, not after. PCS.cs:326-339 is the natural site because `segmentProfile` is the variable that flows into `EvaluateSegmentAsync(client, segmentProfile, ...)` at line 344.

**No planner-enrollment seam change**. The `IPlannerEnrollment.RegisterSnapshotContract(string, Func<EmploymentProfile, object?>)` sync-uniform-per-plan signature stays as-is. Instead of registering a hydrator that returns a per-segment snapshot (which the seam doesn't support), S33 registers a natural-key marker hydrator `enrollment.RegisterSnapshotContract("EmployeeProfileKey", p => new EmployeeProfileKey(p.EmployeeId))` — same shape as S29's `WtmNaturalKey`. The marker just carries `employee_id`; segment-aware re-resolution happens at PCS.cs:326-339 with `segment.StartDate`. **ADR-020 D1 inherited literally; no seam amendment.**

**Binding implementation note for S33**: the `EmployeeProfileResolver` injects via DI into `PeriodCalculationService`; consumption-site call is `var segmentProfile = await _profileResolver.GetByEmployeeIdAtAsync(profile.EmployeeId, segment.StartDate, ct);` immediately followed by the existing OkVersion overlay at lines 326-339 (collapsed into a single segment-effective construction).

### D2 — `agreement_code` + `employment_category` STAY ON `users` (LIVE READ in resolver); documented determinism gap

**Reverses cycle 1's MIGRATE absorption.** Cycle 2 revealed downstream MIGRATE consequences that cascade beyond their value:

- Fatal: `users.agreement_code` drop forces a 25+-call-site cascade through `User.AgreementCode` (cycle 2 Reviewer WARNING). S33 scope explodes.
- Fatal: post-MIGRATE soft-delete has no fallback path for the rule-engine-route consumption (cycle 2 Codex BLOCKER #2).
- Fatal: AuthEndpoints + JwtTokenService cutover is P7 security-critical and the JWT-time read NEEDS the live value anyway. MIGRATE creates JWT drift surface that needs separate UI affordance (Reviewer WARNING).

**The cycle 1 BLOCKER #2 critique was correct, but MIGRATE is not the right response.** The right response is to **accept the determinism gap for `agreement_code` + `employment_category` under pre-launch posture and document it as a Phase 4e candidate** — exactly Reviewer cycle 1 option (a) which I should have absorbed at cycle 1.

**Implementation**:
- `EmployeeProfileResolver.GetByEmployeeIdAtAsync(employeeId, asOfDate, ct)` JOINs `employee_profiles` (dated, predicate `ep.effective_from <= asOfDate AND (ep.effective_to IS NULL OR ep.effective_to > asOfDate)`) with `users` (live, no temporal filter) and returns a fully-hydrated `EmploymentProfile`:
  - DATED from `employee_profiles`: `weekly_norm_hours`, `part_time_fraction`, `position`
  - LIVE from `users`: `agreement_code`, `employment_category`, `primary_org_id` (the last is RBAC-scope, not rule-engine input — live is correct anyway)
- `ok_version` stays server-resolved per ADR-003 at the segment boundary (already correct; not the resolver's job).

**Documented determinism gap**:
- Admin mutation of `users.agreement_code` flips replays of past PCS-routed calculations for that employee.
- Under pre-launch posture (ROADMAP L369): no production data, no live admin workflow. Hypothetical exposure only.
- Phase 4e production-readiness sprint addresses via one of: (a) per-time-entry agreement_code snapshot using `time_entries.agreement_code` already-stored values; (b) versioning `users.agreement_code` via row-level history; (c) some pattern not yet enumerated. ADR-016 D5b NOT extended in S33 — the determinism gap stays a documented limitation, not a new pattern.

**S33 scope shrinks dramatically** with D2 reversal: no schema migration, no backfill, no AdminEndpoints PUT cutover, no AuthEndpoints/JwtTokenService cutover, no users column drop, no User-model surface cascade. Estimated S33 task count: **~10** (was ~14 under MIGRATE).

### D3 — Soft-delete consumption semantic: PCS-routed = fail-closed; HTTP consumers = fallback-to-live-shape

Cycle 1 surfaced Q7 (silent-skip vs fallback vs fail-closed); cycle 1 absorption picked fallback. Cycle 2 revealed fallback-to-live-`users` was unviable post-MIGRATE. Under D2 reversal (no MIGRATE), fallback IS viable — but the right answer per-consumer differs:

- **PCS-routed callers** (TimeEndpoints `/calculate*` — dead code per D6; Payroll Integration `/calculate-and-export`): `EmployeeProfileResolver` returns null when no live employee_profiles row exists for the segment date. `segmentProfile` construction at PCS.cs:326-339 detects null and **throws `EmployeeProfileNotFoundException`** which propagates to the caller as a 500. **Fail-closed.** Rationale: rule engine cannot evaluate without a profile (NormCheckRule reads `profile.WeeklyNormHours`); silent-skip would NPE inside the rule engine; fallback-to-live-users gets only 3 of 6 EmploymentProfile fields. Pre-launch posture: an admin who soft-deletes an active employee profile while payroll is running is making an obviously-wrong move; fail-loud is correct.
- **Non-PCS rule-engine HTTP callers** (`ComplianceEndpoints` POSTs to `/api/rules/check-compliance`): same construction shape as PCS-routed; throws on null, surfaces to client as 500. Compliance is request-time-only (no stored result), so this just fails the current request.
- **Pure-HTTP non-rule-engine consumers** (`BalanceEndpoints.cs:66-68` fallback chain): if resolver returns null, fall through to existing chain (AgreementConfig → Central → hardcoded 37.0m). Graceful degradation; admin still sees a summary, just with default values for the soft-deleted employee. Balance summary is informational, not load-bearing for replay determinism.

**Soft-delete is intentionally a "this employee is being retired" state**, not a "rule engine should silently skip" state. Admins who want to remove a profile temporarily should NOT use soft-delete; they should edit the live values. Documented in ADR-023 itself + in the admin frontend tooltip on the DELETE button.

### D4 — Backfill ledger pattern: NOT NEEDED under D2 reversal

Cycle 2 BLOCKER #2 identified that an UPDATE-with-backfill needs a ledger-entry guard distinct from the S22 D8 INSERT-with-ON-CONFLICT pattern. Under **D2 reversal**, there is no backfill (no MIGRATE → no UPDATE-with-backfill). The ledger pattern question is moot for S33.

S31's `EmployeeProfileSeeder` (which is INSERT-with-NOT-EXISTS-guard, not UPDATE-with-backfill) is unaffected. S31 Phase 4e candidate #2 (`23505` race on concurrent startup) remains in Phase 4e scope; no change.

### D5 — Multi-commit ordering: NOT NEEDED under D2 reversal

Cycle 2 Reviewer BLOCKER #1 identified the unenforceability of "no admin uses /api/admin/users PUT during sprint days" as the cycle-1 commit-gate. Under **D2 reversal**, there is no users-column DROP, no AdminEndpoints PUT cutover, no AuthEndpoints/JwtTokenService cutover. The commit-gate concern dissolves.

S33's actual cutovers are file-disjoint:
- PCS.cs:326-339 — segmentProfile construction (Payroll Integration domain)
- ComplianceEndpoints.cs:72-79 — profile construction (Backend.Api domain)
- BalanceEndpoints.cs:66-68 — fallback chain top (Backend.Api domain)
- EmployeeProfileEndpoints.cs — new DELETE endpoint + extended PUT for cross-day supersession (Backend.Api domain)
- EmployeeProfileRepository — SupersedeAndCreateAsync + SoftDeleteAsync (Infrastructure domain)
- Frontend EmployeeProfileEditor — as-of-date toggle (Frontend domain)

These can land in any order; no intermediate state breaks anything because `agreement_code` + `employment_category` are unchanged on `users`. Phase 2 parallel-dispatch discipline (S29/S30/S31 precedent — file-disjoint, non-worktree) applies cleanly.

### D6 — TimeEndpoints `/calculate*` endpoint disposition: DELETE the dead endpoints + their unit tests

Codex cycle 1 WARNING + cycle 2 NOTE confirmed: `/api/time-entries/calculate` + `/api/time-entries/calculate-week` have **no live caller**. Verified via Grep against `frontend/` (0 hits) + `src/` (0 production hits — only `Orchestrator/OrchestratorScopeEnforcementTests.cs` references for scope-enforcement test coverage + `OkVersionRuntimeRegressionTests.cs`).

S33 **DELETES the two endpoints** + their Contracts (`CalculateRequest.cs`, `WeeklyCalculateRequest.cs`) + the two tests that exercise them. **Single-commit task** in Phase 1 (TASK-3303-or-equivalent at S33 plan-mode time).

**Rationale**: dead code is the cleanest absorption of Codex WARNING #1. Updating dead endpoints to use the new snapshot pattern is theater. The scope-enforcement test pattern can be re-encoded against a live endpoint (e.g., `/api/admin/employee-profiles/{employeeId}` PUT which has the same RBAC + scope-validator shape).

**Frontend hard-cut becomes a no-op** because no frontend caller exists. The `frontend/src/hooks/useTime*.ts` files (if any) that referenced `weeklyNormHours` were S31 false positives — none POST to the calculate endpoints.

### D7 — User-model surface cascade: NOT NEEDED under D2 reversal

Cycle 2 Reviewer WARNING #1 identified 25+ call sites consuming `User.AgreementCode`. Under **D2 reversal**, the `users` columns stay; no cascade. `User.AgreementCode` continues to be the live-read source for non-rule-engine consumers (Compliance, Balance, AdminEndpoints PUT, JwtTokenService).

The JWT-drift question (cycle 2 Reviewer WARNING #2) also dissolves: JWT reads live `users.agreement_code` (no change from today); rule-engine PCS-routed reads dated `employee_profiles` for the 3 new fields + live `users.agreement_code` (documented determinism gap per D2).

### D8 — Versioning emission + admin DELETE endpoint (the actual P4-load-bearing work)

With D2-D7 dissolving the MIGRATE-cascade scope, S33's actual implementation work is materially smaller and focused on what ADR-022 §S32-commitment-list called out as **critical for P4 window-safety**:

- `EmployeeProfileRepository.SupersedeAndCreateAsync(conn, tx, request, expectedVersion, ct)` per ADR-020 D2 3-case routing under `SELECT ... FOR UPDATE` (Cases A/B/C); existing `UpsertAsync` becomes thin shim
- `EmployeeProfileRepository.SoftDeleteAsync(conn, tx, employeeId, expectedVersion, closeDate, ct)` sets `effective_to = closeDate` per end-exclusive `[from, to)` semantic (Codex cycle 1 WARNING absorbed — row absent on dates `≥ effective_to`)
- New `EmployeeProfileEndpoints.DELETE` endpoint with HROrAbove + OrgScopeValidator + ADR-019 admin-strict If-Match contract (412/428); emits `EmployeeProfileSoftDeleted` via atomic outbox + audit-row with `version_before = version_after = expected` per ADR-019 D8 DELETE convention (Reviewer cycle 1 WARNING absorbed)
- EmployeeProfileEndpoints.PUT extends to use `SupersedeAndCreateAsync`; cycle-3 same-day-only-edit validator (S29/S30 precedent) rejects `effective_from != today` with 422; cross-day case emits `EmployeeProfileSuperseded`
- PCS.cs:326-339 segmentProfile cutover (D1)
- ComplianceEndpoints.cs:72-79 cutover to `EmployeeProfileResolver.GetByEmployeeIdAtAsync` (replaces hardcoded `37.0m`)
- BalanceEndpoints.cs:66-68 fallback chain inserts resolver at top
- Dead-code DELETE of TimeEndpoints `/calculate*` per D6
- Frontend EmployeeProfileEditor.tsx as-of-date toggle (read-only when `asOfDate != today`)
- Marquee D-test `ReplayAsync_StableUnderEmployeeProfileMutation_ResultByteIdentical` — admin updates `weekly_norm_hours` 37 → 32 today; replay of last month's PCS-routed calc uses 37 (dated snapshot), not 32 (live)

**S33 task count target: ~10**. Phase decomposition follows S29/S30/S31 precedent (Phase 0 sprint open → Phase 1 sequential plumbing → Phase 2 parallel cutovers → Phase 3 D-tests → Phase 4 validation → Phase 5 docs + close).

## Alternatives Considered

### A1 — MIGRATE `agreement_code` + `employment_category` to `employee_profiles`

Rejected. Cycle 2 revealed the cascade cost (25+ User.AgreementCode call sites, AuthEndpoints/JwtTokenService cutover, soft-delete fallback unviable, multi-commit ordering enforceability). The replay-stability benefit applies only to a hypothetical (admin edit of agreement_code) that pre-launch posture eliminates. Phase 4e production-readiness sprint addresses determinism rigorously when production data exists.

### A2 — Per-time-entry agreement_code snapshot via `time_entries.agreement_code` already-stored

Considered. `time_entries` already stores agreement_code at write time (verified — column exists). PCS could read each segment's first time_entry's agreement_code as the historical source. **Rejected for S33** because: (a) introduces a new pattern not yet in ADR-016 D5b (per-time-entry historical source); (b) requires PCS-side per-segment first-entry lookup logic; (c) doesn't help Compliance/Balance (which don't have time_entries in their request scope). Phase 4e candidate for production-readiness.

### A3 — Amend ADR-020 D1 with per-segment hydrator signature

Considered. Cycle 1 Codex BLOCKER #1 framing offered "extend `IPlannerEnrollment` with `Func<EmploymentProfile, DateOnly, object?>` overload." **Rejected** because D1's PCS.cs:326-339 approach achieves the same replay-stability without amending the seam. WTM-precedent natural-key marker hydrator + per-segment consumption-time re-resolution is the established pattern (S29). No seam-extension carrying-cost.

### A4 — Defer Phase 4d-3 Part 2 entirely; close Phase 4d at Part 1 (S31)

Considered. ROADMAP could mark Phase 4d as "complete except for Part 2 rule-engine cutover" and move to Phase 4e. **Rejected** because P4 (version correctness) requires the rule-engine cutover at some point; deferring forever leaves the S31 authoritative store partially-orphaned (admin can write but rule engine doesn't read).

## Implications

### S33 implementation refinement scope (binding contract)

S33 implementation refinement opens against this ADR as scope-source-of-truth. Step 4 dual-lens review against S33's refinement should converge on ~10 tasks following the D8 enumeration. If S33 refinement Step 4 cycle 1 surfaces NEW architectural BLOCKERs (i.e., NOT covered by ADR-023's D1-D8), that's signal to revisit this ADR — not absorb at refinement level.

### Pattern landscape stable at 5

ADR-016 D5b stays at 5 patterns. S33 inherits ADR-020 D1 verbatim (WTM-precedent natural-key marker + per-segment consumption-time re-resolution). No sixth pattern needed.

### Documented determinism gap is a Phase 4e candidate

ADR-016 D5b extension paragraph NOT filed in S33. The gap is documented in ADR-023 D2 + ROADMAP Phase 4e candidates. Production-readiness sprint addresses with the right architectural choice (A2 per-time-entry snapshot OR users.agreement_code versioning OR something else) when production data exists and the pattern can be validated against real workflows.

## Refinement Trail

`.claude/refinements/REFINEMENT-s32-phase-4d3-part2.md` (DEFERRED 2026-05-16):

- **Cycle 1 (Codex gpt-5.5 + Reviewer Agent)**: 2 convergent BLOCKERs absorbed — planner-enrollment seam shape mismatch + Q3 LEAVE not replay-safe for agreement_code. Cycle 1 absorption proposed WTM-precedent verbatim seam + MIGRATE agreement_code + employment_category.
- **Cycle 2 (Codex gpt-5.5 + Reviewer Agent)**: 4 NEW BLOCKERs in SAME architectural area as cycle 1 + 2 Reviewer WARNINGs. Specifically: (1) TASK-3206 wrong PCS consumption-site — MapSegmentToCalculationAsync doesn't exist; (2) Q7 fallback-to-live-users unviable post-MIGRATE; (3) Phase 2 commit-gate unenforceable; (4) backfill SQL shape unspecified. Per `feedback_thrash_defer_real_world.md`, this is the canonical "cycle-2-finds-NEW-blockers-in-SAME-area" smoke alarm.
- **User adjudication 2026-05-16**: defer to design-only sprint producing ADR-023 (S28 → S29 split precedent).

This ADR resolves the cycle-2 BLOCKERs by **reversing cycle-1 MIGRATE absorption** (D2). Both cycle-2 BLOCKERs related to MIGRATE downstream consequences (#2, #3, #4 fully; WARNINGs #1 and #2 entirely). The remaining cycle-2 BLOCKER #1 (wrong consumption-site location) is resolved by D1 picking PCS.cs:326-339 explicitly.

**The reversal exposes a process learning**: cycle 1 should have surfaced Reviewer's option (a) "document the gap as Phase 4e candidate" as a primary alternative, not just absorbed via MIGRATE reflexively. The MIGRATE absorption traded "fix the agreement_code determinism gap NOW" for "expand sprint scope by 4 tasks + cascade through auth + risk N+1 cycle-2 layers". Pre-launch posture argues for the inverse trade.

## Status History

- **2026-05-16**: ADR-023 DRAFT filed (TASK-3201 sprint S32). Settles 7 questions from deferred S32-implementation refinement.

## Related ADRs

- **ADR-016** — Temporal Period Handling (D5b 5-pattern landscape stable; S33 inherits pattern #4 WTM-style; no extension)
- **ADR-018** — Transactional Outbox + Row-Version Optimistic Concurrency (D3/D5/D6 inherited for atomic admin emission)
- **ADR-019** — Optimistic Concurrency via Row-Version (D2/D5/D6/D8 admin-strict If-Match inherited for PUT + DELETE endpoints)
- **ADR-020** — Versioned-Config Design Foundations for Phase 4d-1 (D1 planner-enrollment + D2 3-case routing inherited verbatim)
- **ADR-021** — Entitlement-Policy Versioned History (D4 consumption-time-lookup pattern S33 inherits for Compliance/Balance cutover; D7 soft-delete consumption semantic recontextualized in D3 above)
- **ADR-022** — Employee-Profile Consolidation + Pre-Baked Versioning (Phase 4d-3 Part 1) — parent commitment-list this ADR fills in details for
