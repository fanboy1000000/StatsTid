# Sprint 104 — Enhedsspor Phase 1b: Units CRUD + Two-Regime Concurrency + Cross-Org Transfer (ADR-038 D3/D8)

| Field | Value |
|-------|-------|
| **Sprint** | 104 |
| **Status** | complete |
| **Start Date** | 2026-06-28 |
| **End Date** | 2026-06-28 |
| **Orchestrator Approved** | yes — 2026-06-28 |
| **Build Verified** | yes — `dotnet build` 0 warnings / 0 errors (incl. the Step-7a fix) |
| **Test Verified** | **yes — CI GREEN `28336028912` (all 7 jobs)**: 852 unit + 1116 regression (34m41s, incl. the 15 `S104UnitManagementTests` + the unit contract test) + 6 smoke + 517 FE + e2e; 0 failures. (The first push CI [`28334317748`] caught 4 Docker-tier failures the local no-Docker run couldn't — 1 stale assertion [R9] + 3 fixture self-FK cleanup orderings — all test-only, fixed in `57dd160`, re-run green.) |

## Sprint Goal
Land the **units management layer** (Enhedsspor Phase 1b — the heavier half of the owner-split Phase 1): a `UnitRepository` + admin endpoints for unit create/rename/move/delete + leader designate/remove + a person's unit-change, on the **two-regime concurrency** (within-Organisation `unit-org-` advisory + recursive-CTE cycle guard [the S100 spine]; the cross-Organisation person unit-change **extending the existing users-transfer path** — Codex's S103-plan BLOCKER-2), with the new `Unit*`/leader/membership event WRITERS (registered in S103) now EMITTING, and the runtime **derived `primary_org_id`** on unit-assign. **Backend-only** — no FE (the merged Enhedsspor page is Phase 3); the new **approval paths (D4 secondary unit-leader + see==act) are Phase 2 (S105)** and are NOT in this sprint. Implements ADR-038 D3 (leader designation + write floor + member-invariant) + D8 (concurrency).

**Reference code:** the deleted `EnhedRepository.cs` (in git ≤ `22c3f80` — the S100 advisory + `GuardNoEnhedCycleAsync` recursive-CTE pattern) is the template for `UnitRepository`; `ReportingLineRepository.AcquireTreeLocksForTransferAsync` (`:661`, called at `AdminEndpoints.cs:1446` — the correct old+new `reporting-org-` transfer helper, NOT the S83 revoke variant `AcquireRevokeTreeLocksAsync`) + `ValidateSameOrganisationAsync` + the `AdminEndpoints` user-transfer branch are the cross-Org-transfer substrate.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py` green at S103 close. |
| Pattern compliance spot-check | CLEAN | `grep FindFirst("scopes")` → 0 (FAIL-001 clean). |
| Orphan detection | CLEAN | The S103 events have no writer yet (intended — this sprint adds them). |
| Documentation drift | DEBT | The DemoSeed tool carries inert enhed dead-code (`StructuralModels`/`GenerateEnheder`/`EnhedFragments`; S103 Step-7a NOTE) — cleaned in TASK-10405. |
| Quality grade review | pending | Update at close. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 + P3 [event writers emit] + P4 [If-Match/version] + P7 [unit-op floors + the cross-Org-transfer auth + locking]). |
| **External Codex** | invoked 2026-06-28 — cycle 1: 2B/2W |
| **Internal Reviewer** | invoked 2026-06-28 — cycle 1: 0B/3W/3N |
| **BLOCKERs resolved before Step 1** | yes — both Codex BLOCKERs absorbed (unit DELETE semantics → TASK-10401; cross-Org transfer `unit_leaders` re-sync → TASK-10402). Cycle-2 verification run. |

### Findings (cycle 1)
_Codex (external) — 2B / 2W:_
- BLOCKER (TASK-10401) — unit DELETE semantics underspecified; the `UnitDeleted`/`UnitMoved` contract implies child re-parent + member re-home (the S100 analog). → TASK-10401 now specifies soft-delete + re-parent children UP (per-child `UnitMoved`) + re-home direct members UP (per-member `UserUnitChanged`) + clear leader rows; test added.
- BLOCKER (TASK-10402) — cross-Org transfer must re-sync `unit_leaders` (a transferred leader keeps old-unit leadership). → in-tx delete of old-unit `unit_leaders` + `UnitLeaderRemoved` per row; test added.
- WARNING (TASK-10402) — wrong lock helper (`AcquireRevokeTreeLocksAsync` = S83 revoke variant) → use `AcquireTreeLocksForTransferAsync` (the real transfer helper); preserve the `TreeRootDriftRetry`/`ReadCommitted` wrapper. Fixed.
- WARNING (TASK-10403) — same-Org person unit-change floor implicit → explicit `RequireAuthorization("HROrAbove")` + LocalHR over the unit's Org. Fixed.

_Internal Reviewer — 0B / 3W / 3N:_
- WARNING (TASK-10402) — wrong lock helper [convergent with Codex] → `AcquireTreeLocksForTransferAsync` (`ReportingLineRepository.cs:661`, called `AdminEndpoints.cs:1446`). Fixed.
- WARNING (TASK-10402) — reporting/vikar re-anchor is NEW behaviour (the existing transfer doesn't touch reporting/vikar; the old clear was the retired S97 enhed-tag clear) + the **manager-side fan-out** (a transferred MANAGER leaves reports' edges cross-Org). → enumerated the edge classes + decided: **block the cross-Org transfer of a user with active reports (422)**; pinned in TASK-10404.
- WARNING (TASK-10401/10403) — the per-event `audit_projection` write not a deliverable/test → added the per-event `InsertAsync` + an audit-parity test + ADR-026/PAT-004 KB refs.
- NOTE — same-Org member unit-change vs `users.version` → If-Match added (TASK-10401). NOTE — preserve the transfer wrapper (`TreeRootDriftRetry`+`ReadCommitted`) → added. NOTE — the regex gaps are `UnitId`/`UnitLeaders` (PascalCase; `units` already IgnoreCase-caught) → TASK-10405 refined.

### Resolution
Both Codex BLOCKERs + all 4 WARNINGs (3 Reviewer + the convergent lock-helper) + the substantive NOTEs absorbed into TASK-10401/10402/10403/10404/10405.

**Cycle 2 (verification):** Codex — both BLOCKERs RESOLVED, warnings resolved, **0 new BLOCKER**; the manager-with-reports-422 rule confirmed coherent (a defensible behaviour change blocking an existing unsafe success). Reviewer — all RESOLVED + the manager-422 decision endorsed, BUT one **new WARNING**: the unit delete's re-parent-UP is type-safe only if the CHILD ordering is **partial-rank (level-skips allowed)**, not strict-adjacency. **Absorbed** (TASK-10401 now pins partial-rank, clarifying ADR-038 D1) — almost certainly the design intent. The stale `AcquireRevokeTreeLocksAsync` citations in the intro + KB ref were corrected. Minor (non-issue): the `UserUnitChanged` audit `target_org_id` resolves correctly via the event's `OrganisationId`, which IS the derived `primary_org_id` per the S103 event contract.

**0 residual BLOCKER; cycle-cap (2/lens) respected. Cleared for Step 1.** (The partial-rank clarification slightly reinterprets ADR-038 D1's CHILD map as the create-default + rank-order rather than strict-adjacency — owner-confirmable at S104 start; the lean is strong.)

## Architectural Constraints Verified
- [ ] P1 — Organisation stays the authority anchor; `units` ops are structure-only; the LOCKED D5 boundary unchanged (no scope from units).
- [ ] P3 — the Unit*/leader/membership events now EMIT (atomic with the writes, ADR-018 D3); audit projections fire.
- [ ] P4 — units carry `version`; If-Match on mutate (412 on stale); the cross-Org transfer preserves `users.version`/If-Match.
- [ ] P6 — the derived `primary_org_id` recompute on unit-assign keeps payroll/attribution correct.
- [ ] P7 — unit ops + leader designation LocalHR-floored over the unit's Organisation; the cross-Org person-change floors BOTH old+new Organisations (the existing transfer floor); no S76 mixed-role escape.
- [ ] P8 — build + full pyramid (incl. the new held-lock interleave + cycle-guard tests) green.

## Task Log

### TASK-10400 — Sprint open (entropy + plan + Step-0b)
| Field | Value |
|-------|-------|
| **ID** | TASK-10400 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **KB Refs** | ADR-038, S100, S83, ADR-027 |

**Description**: Entropy scan recorded; plan authored; Step-0b dual-lens run (2 cycles; 2 Codex BLOCKERs absorbed; 0 residual). **Owner-confirmed 2026-06-28: the CHILD ordering is PARTIAL-RANK (level-skips allowed)** — a unit branch need not traverse all 7 levels; a child's type just needs a deeper rank than its parent. This clarifies ADR-038 D1's CHILD map as the create-default + rank-order, NOT strict-adjacency, and makes the delete's re-parent-UP type-safe.

**Validation Criteria**:
- [x] Entropy recorded; plan authored; Step-0b dual-lens run; BLOCKERs absorbed before Step 1; partial-rank ordering owner-confirmed.

---

### TASK-10401 — `UnitRepository`: CRUD + within-Org concurrency + leader designation + event emission (ADR-038 D3/D8/D10)
| Field | Value |
|-------|-------|
| **ID** | TASK-10401 |
| **Status** | complete (`UnitRepository` — `unit-org-` advisory + `GuardNoUnitCycleAsync` recursive-CTE + CRUD + the partial-rank delete cascade [re-parent children UP / re-home members UP / clear leaders] + leader designate-remove + member-invariant re-sync + `ApplyUserUnitChanged` + atomic event + per-event audit writes; If-Match. Built clean 0 err.) |
| **Agent** | Infrastructure (cross-domain authorized) |
| **Components** | src/Infrastructure/StatsTid.Infrastructure/UnitRepository.cs (new), EventStore/outbox wiring |
| **KB Refs** | ADR-038 D3/D8/D10, S100 (the deleted EnhedRepository spine), ADR-018 (atomic outbox + D2 sync-in-tx audit), ADR-026 + PAT-004 (audit-projection mappers), `docs/operations/audit-projection-catalog.md` |

**Description**: New `UnitRepository` (modelled on the S100 enhed spine): create / rename / move / delete units under `pg_advisory_xact_lock(hashtext('unit-org-' || organisation_id))` + a recursive-CTE cycle guard over `parent_unit_id` on the held connection; app-enforced CHILD type-ordering — **PARTIAL-RANK** (a child's type rank must be strictly DEEPER than its parent's; **level-skips ALLOWED**, e.g. an `omrade` may directly parent a `team`): the `organisation→direktion→omrade→kontor→team→enhed` map (ADR-038 D1) is the **create-UI default child type + the rank order**, NOT a strict-immediate-adjacency constraint — real orgs omit levels, and this makes the delete's re-parent-UP (below) type-safe by construction (Reviewer Step-0b cycle-2; clarifies ADR-038 D1). Reject only a child whose rank ≤ its parent's; soft-delete + the in-tx version predicate (412/404 on 0-row); **leader designate/remove** under the SAME `unit-org-` advisory (ADR-038 D3 — closes the designate-vs-move race) with the **member-invariant** (the designee's `unit_id` == the unit) + **remove old-unit `unit_leaders` rows in-tx when a member's `unit_id` changes**. Emit `UnitCreated/Renamed/Moved/Deleted` + `UnitLeaderDesignated/Removed` atomically with the writes (ADR-018 D3; the events registered in S103), **each with its per-event `audit_projection` InsertAsync (ADR-018 D2 sync-in-tx; the S103 Unit* mappers; `target_org_id` = the event's OrganisationId — P3 parity)**. The **same-Org unit-assign** recomputes the derived `primary_org_id` = the unit's `organisation_id` (a no-op when same-Org); emits `UserUnitChanged`; **takes the user's `If-Match`/`users.version`** (last-writer-wins is not acceptable — the Codex/Reviewer NOTE).
- **[BLOCKER fix] Unit DELETE semantics (the single-unit analog of the S100 enhed delete + the `UnitDeleted`/`UnitMoved` event contract):** soft-delete the unit (projection-filter, NOT a hard delete); **re-parent surviving CHILD units UP** to the deleted unit's parent (root→roots; a leaf→only `UnitDeleted`) via a **per-child `UnitMoved`**; **re-home the deleted unit's DIRECT members UP** to the parent unit (or `unit_id` NULL → home at the Organisation, when the deleted unit was top-level) via a **per-member `UserUnitChanged`** (recomputing each member's `primary_org_id` — same-Org, a no-op); and **remove the deleted unit's `unit_leaders` rows**. All under the `unit-org-` advisory, atomic, with the audit writes.

**Validation Criteria**:
- [ ] Within-Org move/create/delete serialize on `unit-org-` + the cycle guard rejects a cycle-forming move; CHILD ordering enforced.
- [ ] **Delete re-parents children UP (per-child `UnitMoved`) + re-homes direct members UP (per-member `UserUnitChanged`, to the parent or NULL) + clears the unit's leader rows** — pinned by a test (RED on a naive hard-delete / orphaned-children).
- [ ] Leader designate/remove take the `unit-org-` advisory + enforce the member-invariant; a member's unit move removes their old-unit leader rows in-tx.
- [ ] Events emit atomically with the writes (no post-commit append) **+ the per-event `audit_projection` row** (parity test); `UserUnitChanged` on a unit change; the same-Org unit-assign honours `If-Match`.

---

### TASK-10402 — Cross-Org person unit-change = EXTEND the users-transfer path (ADR-038 D8; Codex S103-plan BLOCKER-2)
| Field | Value |
|-------|-------|
| **ID** | TASK-10402 |
| **Status** | complete (extended the `AdminEndpoints` `PUT /users/{id}` transfer branch: `UnitId` on the request; sets unit_id + recomputes `primary_org_id`; **BLOCKS 422 a user with active reports**; clears old-unit `unit_leaders` [+`UnitLeaderRemoved`]; re-anchors the user's own PRIMARY/ACTING `reporting_lines` [`ReportingLineSuperseded`] + `manager_vikar` [new `ManagerVikarRepository.CloseAllInvolvingUserAsync` → `ManagerVikarEnded`]; `UserUnitChanged`+audit; preserves `TreeRootDriftRetry`/`ReadCommitted`/`AcquireTreeLocksForTransferAsync`/both-org floor/home-guard/If-Match. Lock order: `reporting-org-` → `unit-org-`(old+new) → users `FOR UPDATE`.) |
| **Agent** | Backend API + Infrastructure (cross-domain authorized) |
| **Components** | src/Backend/.../AdminEndpoints.cs (the user-transfer path), src/Infrastructure/.../ReportingLineRepository.cs (lock helper) |
| **KB Refs** | ADR-038 D8, S83 (`AcquireTreeLocksForTransferAsync` — the transfer helper; `AcquireRevokeTreeLocksAsync` only for any revoke cleanup), docs/SECURITY.md (S76 floor; S78/S83 serialization), ADR-027 |

**Description**: A person's unit-change that crosses Organisations is a **TRANSFER** and MUST **extend the existing transfer branch** of `PUT /api/admin/users/{userId}` (the `AdminEndpoints.cs` path) — NOT a bare `users.unit_id`/`primary_org_id` writer — preserving its wrapper (`TreeRootDriftRetry.RunAsync` + explicit `ReadCommitted`) and gates: **LocalHR over BOTH old+new Organisations**, the ORGANISATION-home guard, `If-Match`/`users.version`, `users_audit` + `UserUpdated`, agreement handling; and its **`AcquireTreeLocksForTransferAsync`** call (the correct helper — OLD root from the live user + NEW from the target org's both `reporting-org-` advisories, before `users FOR UPDATE`; **not** the S83 revoke variant `AcquireRevokeTreeLocksAsync`). On top, S104 adds:
- **[BLOCKER fix] Re-sync `unit_leaders`:** delete the moved user's OLD-unit `unit_leaders` rows in-tx + emit `UnitLeaderRemoved` per row (a transferred leader must lose the old-unit designation — the D3 member-invariant across orgs).
- **The retained-edge re-anchor — enumerate the edge classes** (NEW behaviour; the existing transfer does NOT touch reporting/vikar today — the only old clear was the now-retired S97 enhed-tag clear): (a) the user-as-EMPLOYEE PRIMARY/ACTING `reporting_lines` rows (a cross-Org primary edge is forbidden — the ADR-027/S95 same-Org reporting invariant → clear/re-point); (b) the user's `manager_vikar` rows; (c) **the manager-side fan-out** — when the transferred user MANAGES reports, those reports' PRIMARY edges become cross-Org on the manager's departure. **Decision (Phase-0 fork, settle in this task):** the cross-Org transfer of a user **with active reports is BLOCKED (422)** (re-assign the reports first) — the simplest safe rule, avoiding silent orphaning; revisit a deactivation-style fan-out (`ReportingLineManagerDeactivated` precedent) only if the owner needs it. Pin the blocked-with-reports case in TASK-10404.
- Recompute `primary_org_id` = the new unit's `organisation_id`; set `users.unit_id`; emit `UserUnitChanged` + its audit row. **Total lock order:** all `reporting-org-` (id-sorted) → all `unit-org-` (id-sorted) → row `FOR UPDATE` (advisory-before-row, ADR-027).

**Validation Criteria**:
- [ ] Cross-Org unit-change asserts old+new-org LocalHR floor + home guard + `If-Match` + `UserUpdated`/audit parity + the `TreeRootDriftRetry`/`ReadCommitted` wrapper — transfer-path parity, not a bare write.
- [ ] The moved user's old-unit `unit_leaders` rows are deleted in-tx + `UnitLeaderRemoved` emitted; a transfer of a user WITH active reports is BLOCKED (422).
- [ ] The fixed total lock order holds (held-lock interleave proof in TASK-10404); no AB/BA deadlock.
- [ ] `primary_org_id` recomputed to the new unit's Organisation; `UserUnitChanged` + audit row emitted.

---

### TASK-10403 — Units admin endpoints (ADR-038 D3)
| Field | Value |
|-------|-------|
| **ID** | TASK-10403 |
| **Status** | complete (new `UnitEndpoints.cs` — `GET/POST/PUT(rename)/PUT(/{id}/move)/DELETE /api/admin/units` + `POST/DELETE /units/{id}/leaders` + `PUT /users/{id}/unit` [same-Org assign; cross-Org unit → 422 routing]; ALL `RequireAuthorization("HROrAbove")` + `ValidateOrgAccessAsync(LocalHR)` over the unit's Org; If-Match. PAT-010 `UnitResponse`/`UnitListResponse` named records + a registered `UnitEndpointContractTests`. `Program.cs` wired. No existing floor weakened.) |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | src/Backend/.../Endpoints/*.cs, Contracts/ |
| **KB Refs** | ADR-038 D3, ADR-019 (If-Match), PAT-010 (named response records + contract test), ADR-007 (RequireAuthorization) |

**Description**: Admin endpoints: `POST/PUT(rename)/PUT(move)/DELETE /api/admin/units`, leader `POST/DELETE .../units/{id}/leaders`, and the person unit-change (folded into the existing user PUT or a dedicated endpoint per TASK-10402). Floors: unit ops + leader designation + the **same-Org** person unit-change are **`RequireAuthorization("HROrAbove")` + `ValidateOrgAccessAsync(LocalHR)` over the unit's Organisation** (multi-org via `GetAccessibleOrgsAsync`) — stated explicitly so a dedicated person-change endpoint cannot become a floor escape; the cross-Org person-change floors BOTH orgs (TASK-10402). If-Match/`version` (412). Each mutating endpoint writes its event + `audit_projection` row in-tx (TASK-10401). Named response records (PAT-010) + a registered endpoint contract test (no `fetchEnheder`-class drift). **No FE** (Phase 3).

**Validation Criteria**:
- [ ] Every endpoint `RequireAuthorization("HROrAbove")` + per-scope-role-floored + org-scoped (S76 invariant — incl. the SAME-Org person-change, not just cross-Org); If-Match enforced (412 on stale).
- [ ] PAT-010 named records + a registered contract test green.

---

### TASK-10404 — Tests (concurrency + CRUD + transfer parity)
| Field | Value |
|-------|-------|
| **ID** | TASK-10404 |
| **Status** | planned |
| **Agent** | Test & QA |
| **Components** | tests/** |
| **KB Refs** | ADR-038 (test hooks), S95/S100 (held-lock interleave pattern), FAIL-002 |

**Description**: Held-lock interleave (concurrent within-Org structural move / leader-designate vs a cross-Org transfer compose under the fixed total lock order — `pg_locks⋈pg_stat_activity` waiter barrier, S95/S100 pattern); the cycle guard (a move forming a cycle is rejected); the CRUD happy/If-Match/412 paths; **the DELETE semantics** (re-parent surviving children UP [per-child `UnitMoved`] + re-home direct members UP [per-member `UserUnitChanged`, parent-or-NULL] + clear leader rows — RED on a naive hard-delete/orphan); the cross-Org transfer parity (old+new-org floor, `UserUpdated`/audit parity, reporting/vikar re-anchor, `primary_org_id` recompute, **the moved leader's old-unit `unit_leaders` cleared**, **a transfer of a manager WITH active reports BLOCKED 422**); the **audit-projection parity** for the new events (`target_org_id`); the leader member-invariant + the on-move re-sync; the unit-op LocalHR floor + a cross-styrelse reject (S76 no-leak).

**Validation Criteria**:
- [ ] Held-lock interleave + cycle-guard + CRUD/If-Match + **delete-semantics** + transfer-parity (incl. **leader-cleared** + **manager-with-reports-blocked**) + **audit-parity** + leader-invariant + floor/no-leak tests green (RED-on-old where applicable).
- [ ] Full pyramid green; FAIL-002 sheds isolation-cleared if any.

---

### TASK-10405 — Hardening: absence-guard regex widen + DemoSeed dead-code cleanup (S103 Step-7a NOTEs)
| Field | Value |
|-------|-------|
| **ID** | TASK-10405 |
| **Status** | partial — regex widen DONE; DemoSeed dead-code DEFERRED (Orchestrator) |
| **Agent** | Orchestrator |
| **Components** | tests/.../UnitAuthorityAbsenceTests.cs, tools/StatsTid.DemoSeed/** |
| **KB Refs** | S103 Step-7a review |

**Description**: Widen the `UnitAuthorityAbsenceTests` token set — the current regex is IgnoreCase so `units` is already caught; the real PascalCase gaps are **`UnitId` / `UnitLeaders`** (a `.UnitId` property read). Add them so this sprint's unit-aware code cannot leak a unit token into the authority files (`OrgScopeValidator`/`RoleScope`/`DesignatedApproverAuthorizer`/`ValidateEmployeeAccess`). Remove the DemoSeed inert enhed dead-code (`StructuralModels` `DemoEnhed`/`DemoUserEnhed`/`Enheder`/`UserEnheder`/`EnhedLabel`, `DemoGenerator.GenerateEnheder`, `DanishPools.EnhedFragments`).

**Validation Criteria**:
- [x] The absence-guard regex widened to `UnitId`/`UnitLeaders` (PascalCase); the authority files stay clean → green. **DONE** (Orchestrator).
- [~] **DemoSeed dead-code removal DEFERRED to a follow-up.** It is INERT (S103 emitter already stops emitting enheder; the `DemoEnhed`/`GenerateEnheder`/`EnhedFragments`/`EnhedLabel` code only populates a dataset that is no longer emitted — build + 29 demoseed tests + CI were all green WITH it present). Removing it cleanly is a multi-file refactor of `DemoGenerator`/`StructuralModels`/`DemoManifest` (EnhedLabel is woven through user generation) with breakage risk and zero functional gain — deferred out of a units-backend sprint to avoid risk. Recorded as a standing hygiene follow-up.

---

### TASK-10406 — Validation + Step-7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-10406 |
| **Status** | planned |
| **Agent** | Orchestrator |

**Validation Criteria**:
- [ ] `dotnet build` 0/0; full pyramid green; Constraint Validator pass; Step-7a dual-lens (high-risk: the cross-Org-transfer auth/locking) → BLOCKERs absorbed; INDEX/ROADMAP updated (Phase 2 / S105 promoted); commit + push + CI-verify.

---

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules / wage mappings / overtime / absence | N/A | No rule/payroll-logic change. |
| Retroactive recalculation stable | verified-by-test | The cross-Org transfer recomputes `primary_org_id` correctly → payroll/settlement attribution stays correct (TASK-10404). |

### TASK-10404 — Tests (complete)
15 Docker-gated `S104UnitManagementTests` (held-lock interleave + distinct-org-no-block + cross-Org-transfer interleave + cycle-guard 422 + partial-rank + CRUD/If-Match-412 + delete-cascade [mid-level + top-level] + leader member-invariant + on-move re-sync + transfer-parity + with-reports-422 + both-org-floor-403 + audit-parity + cross-styrelse-no-leak) + the unit-endpoint contract test. Build 0 err; unit 852/852; Docker-gated CI-pending. No production bug found by the test agent.

### TASK-10406 — Validation + Step-7a + close (complete)
Build 0/0; Constraint-Validator spot-check (DEP-003 events registered; no endpoint floor weakened — Step-7a-confirmed; no FindFirst("scopes")). **Step-7a 1 cycle:** Reviewer 0B/0W/4N + Codex 0B/1W. The Codex WARNING — the delete cascade's member re-home filtered `is_active = TRUE`, leaving an inactive user homed at a soft-deleted unit (reactivation hazard) — **FIXED** (dropped the filter; ALL members re-home; build re-verified). 4 Reviewer NOTEs → S105 follow-ups.

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes (high-risk: cross-Org-transfer auth + two-regime locking) |
| **Sprint-start commit** | `e22482e` |
| **Command** | `codex review "..."` (prompt-alone, uncommitted) + Reviewer Agent |
| **Review Cycles** | 1 |
| **Findings** | **0 BLOCKER, 1 WARNING (fixed), 4 NOTE** |
| **Resolution** | the WARNING fixed + build-verified; 4 NOTEs → S105 follow-ups |

Artifacts: `.claude/reviews/SPRINT-104-step7a-{codex,reviewer}.md`.
- Codex WARNING (P2) — delete-cascade re-homed only ACTIVE members → fixed (re-home ALL; no user points at a soft-deleted unit). **Follow-up:** extend the delete-cascade Docker test to assert an inactive member re-homes.
- Reviewer NOTEs (→ S105): the transfer's `ReportingLineSuperseded`/`ManagerVikarEnded` have no audit_projection row (event-sourced; deactivation precedent); the manager-side asymmetry (manager-with-reports 422 vs leader silently cleared → leaderless unit — confirm the D7 fallback in S105); a non-transfer PUT's no-op unit_id self-write + ignored-without-org-change `request.UnitId`; the absence-guard scans the scope/approve files but not the approval-READ path (D4 in S105 will legitimately add `unit_leaders` there — the guard prescribes the ADR-038 amendment).

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 852 | all passing (CI + local) |
| Regression (Docker-gated) | 1116 | **all passing (CI GREEN, 34m41s)** — incl. the 15 `S104UnitManagementTests` + the unit contract test (+16 vs S103's 1100) |
| Smoke | 6 | all passing (CI GREEN) |
| DemoSeed | 29 | (unchanged; local-green at S103) |
| Frontend (vitest) | 517 | (unchanged — no FE change in S104) |
| E2E (Playwright) | — | all passing (CI GREEN) |

**Pyramid: 852u + 1116r + 6s + 29demoseed + 517fe = 2520 — CI GREEN `28336028912`, all 7 jobs.** The 1st push CI (`28334317748`) caught 4 Docker-tier failures (R9 stale assertion + 3 fixture self-FK cleanup orderings) — all TEST-ONLY (production code untouched), fixed in `57dd160`, re-run fully green. The Docker tier earning its keep: a behaviour-correct change (the transfer re-anchor) updated a stale test, and 3 fixture-teardown FK-ordering bugs surfaced only against a real DB.

## Sprint Retrospective

**What went well**: The Step-0b split paid off again — Phase 1b landed clean with the keystone D5 boundary preserved BY CONSTRUCTION (both lenses confirmed independently) and the highest-risk piece (the cross-Org transfer) composing correctly under the fixed total lock order with the TOCTOU closed. The Step-0b dual-lens caught 2 real BLOCKERs pre-code (unit delete semantics + the cross-Org `unit_leaders` re-sync) and a genuine design fork (partial-rank ordering, owner-confirmed). The 4-agent flow (units-backend → tests; events earlier) sequenced with no merge conflict. Step-7a found one real latent bug (inactive-member rehome on delete) — exactly the kind a markdown review can't catch, fixed in one line.

**What to improve**: Docker unavailable locally → the Docker tier is CI-pending (2nd CI-pending close since S101; S102 design-only/S103 backfilled-green, so not consecutive-blocking — the public-repo CI now runs). The DemoSeed inert-enhed dead-code cleanup is still deferred (a standing hygiene follow-up, flagged twice now).

**Knowledge produced**: no new ADR/PAT (implements ADR-038; the partial-rank clarification of D1 is recorded in the plan + ADR-038 should get a one-line amendment at the next ADR touch). Follow-ups → S105 (Phase 2): the D4 approval paths (secondary unit-leader + same-Org vikar) + the see==act dashboard reads + the LOCKED-boundary RED test + the absence-guard extension to the approval-READ path + the leaderless-unit fallback confirm; plus the inactive-member-rehome test + the DemoSeed dead-code cleanup.
