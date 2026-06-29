# Sprint 105 — Enhedsspor Phase 2: The Unit-Leader Approval Paths (ADR-038 D4/D5)

| Field | Value |
|-------|-------|
| **Sprint** | 105 |
| **Status** | complete (pending push + CI-verify) |
| **Start Date** | 2026-06-29 |
| **End Date** | 2026-06-29 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes — 0/0 |
| **Test Verified** | yes — 852u + 1129r green locally (Docker up); 6s/e2e CI-verify on push |

## Sprint Goal
Wire the **D4 unit-leader approval authority** into the approval path — the FIRST time `unit_leaders` legitimately enters authority — while holding the **LOCKED D5 Organisation boundary** (units grant the direct-member approval EDGE, never SCOPE, never deep-tree inheritance). `CanApprove(actor, E)` gains a **secondary-unit-leader exception path** (+ the same-Org vikar of a unit leader), STRICTLY bounded to `E.unit_id`'s own direct members; the **see==act dashboard reads** gain the same branch in lockstep; the absence guard is **split** (scope files stay unit-free; the approval files legitimately gain `unit_leaders`) and a new **boundedness RED test** pins that a parent-unit leader grants NOTHING over a child-unit member. Backend-only (the merged Enhedsspor FE is Phase 3). Implements ADR-038 D4; amends ADR-038's "S104"/"S105" wiring notes.

**The load-bearing invariant (P7):** path-2 keys on `unit_leaders(E.unit_id)` — E's OWN unit, NOT an ancestor walk. A leader of a PARENT unit has no `unit_leaders` row for the child unit → no approval. This is what keeps the S76/S85/S91 subtree-inheritance bug class closed even as units gain approval authority.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py` green at S104 close. |
| Pattern compliance spot-check | CLEAN | `grep FindFirst("scopes")` → 0 (FAIL-001 clean). |
| Orphan detection | CLEAN | S104's `Unit*` writers + endpoints all wired + CI-green. |
| Documentation drift | DEBT | DemoSeed inert-enhed dead-code (twice-deferred S103/S104) — cleaned in TASK-10505. |
| Quality grade review | pending | Update at close (the approval domain gains the unit-leader path). |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 + **P7 security — the approval-authority surface** + P3 event-sourcing [audit on approve/reject] + P4). The single highest-risk authority change of the program. |
| **External Codex** | invoked 2026-06-29 — cycle 1: 2B/1W/2N |
| **Internal Reviewer** | invoked 2026-06-29 — cycle 1: 2B/2W/3N |
| **BLOCKERs resolved before Step 1** | yes — the 2 convergent BLOCKERs (the in-lock `unit-org-` advisory + the two-stage see==act pipeline/floors) absorbed into TASK-10501/10502. Cycle-2 verification run. |

### Findings (cycle 1)
_Both lenses CONVERGED on the keystone being sound (D5 boundedness = a single-table `unit_leaders(E.unit_id)` check, RED-test-pinned) + the same 2 BLOCKERs:_
- **BLOCKER (TASK-10501, P4/P7 — convergent):** the approve/reject/reopen tx holds the employee's `reporting-org-` advisory, but the NEW path-2 revokers (`UnitLeaderRemoved`/member-move) hold `unit-org-` — a different key → an unserialized de-designated-leader-approve race. → the action tx now ALSO acquires the employee's `unit-org-` advisory (D8 order) before the in-lock re-eval; race test added.
- **BLOCKER (TASK-10502 — convergent):** the dashboard reads are a TWO-STAGE pipeline (`DesignatedCandidateEmployeesCte` superset → `FilterByEffectiveApproverAsync` edge-only filter); the plan said only "a JOIN" → BOTH stages must extend in lockstep (else act-without-see), AND the inclusion must apply the SAME floors (active/LeaderOrAbove/same-Org/vikar) as the predicate (else see-without-act for a bare/expired row). → rewritten; dashboard negatives added.
- WARNING (Reviewer, TASK-10502) — see-side census incomplete: `GetTeamOverviewRosterAsync` + the allocation-breakdown (1129, edge-only) + the tile-count projection. → extend the team-overview + 1129 in lockstep; the tile-count scoped OUT with rationale.
- WARNING (Codex, TASK-10501, P3) — a unit-leader approval would mislabel as `ORG_SCOPE_FALLBACK`. → add `UNIT_LEADER`/`UNIT_LEADER_VIKAR` `approval_method`.
- WARNING (Reviewer, TASK-10503) — `ApprovalPeriodRepository` was never in the absence set (reword); the boundedness test needs a ≥2-level (grandparent) fixture + dashboard-visibility coverage. → both fixed.
- NOTEs — path-3 vikar is a NEW resolution (budgeted in TASK-10501); NULL `E.unit_id` handled (no match → edge/HR-Admin); TASK-10505 DemoSeed kept last/isolated/droppable.

### Resolution
The 2 convergent BLOCKERs + all WARNINGs + the substantive NOTEs absorbed into TASK-10501/10502/10503/10504/10505.

**Cycle 2 (verification):** BOTH lenses confirm **all findings RESOLVED, 0 residual BLOCKER**. Codex: both BLOCKERs + the audit WARNING resolved; the keystone intact (single-table direct-member check, no ancestor walk; ≥2-level boundedness covering action + dashboard). Reviewer: all 4 resolved + verified the lock-order addition is deadlock-safe (both advisories key on the same Organisation; D8 prefix order `reporting-org-`→`unit-org-`→row holds, advisory-before-row preserved vs the payroll-export FOR UPDATE in reopen). Two minor implementer NOTEs absorbed: (a) centralize the extended predicate into ONE shared helper so the team-overview filter can't drop what the candidate CTE included (the two-stage trap, relocated); (b) the my-reports reads add edge-OR-unit-leader visibility ONLY, not HR/Admin scope (wording clarified).

**0 residual BLOCKER; cycle-cap (2/lens) respected. Cleared for Step 1.** This was the deepest Step-0b of the program — both lenses caught the unit-leader revoke-vs-approve advisory race + the two-stage see==act half-wire, pre-code.

## Architectural Constraints Verified
- [x] P1 — the Organisation stays the authority anchor; units grant the direct-member approval EDGE only (D4), NEVER scope (D5). (absence-guard SCOPE-split; SCOPE files unit-free.)
- [x] P3 — approve/reject/reopen audit correct for the new approver class (`UNIT_LEADER`/`UNIT_LEADER_VIKAR`; replay-safe additive CHECK + the legacy guard).
- [x] P4 — the S78/S83 in-lock re-eval preserved + EXTENDED: the action tx holds `reporting-org-`→`unit-org-` (D8 order) before re-eval (the revoke-vs-approve race test green).
- [x] P7 — **the keystone:** the unit-leader approval is STRICTLY `E.unit_id`-bounded (single-table, no ancestor walk); a grandparent/parent/sibling-unit leader grants NOTHING (the ≥2-level boundedness RED test); + the Step-7a self-approval self-exclusion (no SoD hole); same-Org + LeaderOrAbove floors hold.
- [x] P8 — build 0/0 + full pyramid (852u + 1129r incl. boundedness/see==act/vikar/race/self-approval) green locally.

## Task Log

### TASK-10500 — Sprint open (entropy + plan + Step-0b)
| Field | Value |
|-------|-------|
| **ID** | TASK-10500 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **KB Refs** | ADR-038 D4/D5, ADR-027, S94 (the flat CanApprove), docs/SECURITY.md |

**Description**: Entropy scan recorded; plan authored; Step-0b dual-lens run (2 cycles; 2 convergent BLOCKERs absorbed — the in-lock `unit-org-` advisory race + the two-stage see==act half-wire; 0 residual). The deepest Step-0b of the program.

**Validation Criteria**:
- [x] Entropy recorded; plan authored; Step-0b dual-lens run; BLOCKERs absorbed before Step 1.

---

### TASK-10501 — The approval predicate: the secondary-unit-leader path (ADR-038 D4)
| Field | Value |
|-------|-------|
| **ID** | TASK-10501 |
| **Status** | complete (`IsUnitLeaderApproverAsync` [single-table `unit_leaders(E.unit_id)` + path-3 vikar; NO ancestor walk] + the centralized shared helper `IsEffectiveApproverOrUnitLeaderAsync` routed through ALL sites; the in-lock `reporting-org-`→`unit-org-` advisory before re-eval; `UNIT_LEADER`/`UNIT_LEADER_VIKAR` `approval_method` CHECK+model+classifier. Build 0 err; SCOPE files unit-free.) |
| **Agent** | Security & Compliance + Backend (cross-domain authorized) |
| **Components** | src/Infrastructure/.../DesignatedApproverAuthorizer.cs (+ the `unit-org-` lock acquisition; `ReportingLineRepository`/the advisory helper), src/Backend/.../Endpoints/ApprovalEndpoints.cs (the approve/reject/reopen sites + the classification) + ComplianceEndpoints, the `approval_method` audit classification (`docker/postgres/init.sql` CHECK [Orchestrator-approved] + the `ApprovalPeriod` model + the classifier) |
| **KB Refs** | ADR-038 D4/D5, ADR-027 (the edge predicate), S94 (CanApprove = edge OR HR/Admin), S78/S83 (in-lock re-eval), docs/SECURITY.md (S76 floor) |

**Description**: Add a NEW `DesignatedApproverAuthorizer.IsUnitLeaderApproverAsync(actorId, employeeId, asOf, ct)` → `true` iff the actor is **active LeaderOrAbove** AND holds a `unit_leaders` row for the employee's **own `users.unit_id`** (NOT an ancestor — a single-table membership check `ul.unit_id = E.unit_id AND ul.user_id = @actor`, never a recursive walk; a NULL `E.unit_id` → no match) AND shares the employee's Organisation (the existing same-Organisation re-check). **Path-3 vikar is a NEW resolution** (NOT a reuse of `ResolveDesignatedApproverAsync`, which only resolves the EDGE manager's vikar): the actor is an active `manager_vikar` stand-in for some `U ∈ unit_leaders(E.unit_id)`, same-Organisation (same-Org binds transitively — D12). Then extend `CanApprove` at EVERY composition site (approve / reject / the reopen-LEADER arm in `ApprovalEndpoints` ~:270/:448/:1565 + `ComplianceEndpoints`) so the predicate is **`IsEffectiveDesignatedApprover (edge) OR IsUnitLeaderApprover (NEW) OR HasHrAdminScopeOverEmpOrg`**. **NO scope grant** — this path NEVER touches `OrgScopeValidator`/`RoleScope`/`ValidateEmployeeAccess` (D5).
- **[BLOCKER fix — the in-lock re-eval advisory domain (P4/P7)]:** the existing approve/reject/reopen tx serializes the revoke-vs-act race by holding the employee's `reporting-org-` advisory (`AcquireTreeLockForEmployeeAsync`). But the NEW path-2 revokers — `UnitLeaderRemoved` + the same-Org member-move — serialize on the **`unit-org-`** advisory (ADR-038 D8), a DIFFERENT key → a just-de-designated leader's approve would NOT serialize against the concurrent removal (a stale-authority window the edge path closes). So the action tx MUST **additionally acquire the employee's current `unit-org-` advisory**, in the D8 total order (`reporting-org-` → `unit-org-`), BEFORE the in-lock re-eval of the extended `CanApprove`. Pinned by a held-lock interleave test (TASK-10504).
- **[WARNING fix — the P3 audit classification]:** a unit-leader approval would otherwise be recorded as `approval_method = ORG_SCOPE_FALLBACK` (misleading — it is NOT HR/Admin scope fallback). Add `UNIT_LEADER` + `UNIT_LEADER_VIKAR` to the `approval_method` CHECK + the model + the classification logic; the existing edge/HR-Admin classifications unchanged.
- **[Drift-proofing — Reviewer NOTE]:** the extended predicate is applied at MULTIPLE filter sites — `FilterByEffectiveApproverAsync` (both my-reports reads), the inline loop in `GetTeamOverviewRosterAsync` (`ApprovalPeriodRepository.cs:439`), `ApprovalEndpoints.cs:1129`, `ComplianceEndpoints.cs:50`, and the action endpoints. Centralize `IsEffectiveDesignatedApprover OR IsUnitLeaderApprover` into ONE shared predicate helper that ALL of these call (the candidate-CTE side already shares a constant), so no site can filter-OUT what the candidate set included — the exact two-stage half-wire the BLOCKER warned of, otherwise just relocated to the team-overview filter.

**Validation Criteria**:
- [ ] A secondary leader of E's own unit (active LeaderOrAbove, same-Org) can approve/reject E; a NON-leader / an Employee-role `unit_leaders` row / a leader of a SIBLING-or-PARENT-or-GRANDPARENT unit CANNOT (the direct-member bound).
- [ ] The vikar of a unit leader can approve (path-3, dedicated resolution); the same-Organisation re-check holds for every path; an orphan (no edge, no unit leader) routes to in-scope HR/Admin.
- [ ] **The action tx acquires `reporting-org-` THEN the employee's `unit-org-` advisory** (D8 order) before the in-lock re-eval; a concurrent `UnitLeaderRemoved`/member-move serializes against it (race test). The approval is recorded as `UNIT_LEADER`/`UNIT_LEADER_VIKAR` in `approval_method`.

---

### TASK-10502 — see == act: the dashboard reads (ADR-038 D4)
| Field | Value |
|-------|-------|
| **ID** | TASK-10502 |
| **Status** | complete (both stages extended — `DesignatedCandidateEmployeesCte` gains a `unit_led_members` UNION + `FilterByEffectiveApproverAsync` via the shared helper with the FULL floors; `GetTeamOverviewRosterAsync` + the allocation-breakdown gate in lockstep; my-reports = edge-OR-unit-leader only; the tile-count projection scoped out [documented]; inside the accessible-org bound.) |
| **Agent** | Infrastructure (cross-domain authorized) |
| **Components** | src/Infrastructure/.../ApprovalPeriodRepository.cs (`GetPendingForDesignatedReportsAsync` + `GetByMonthForDesignatedReportsAsync` — incl. BOTH internal stages `DesignatedCandidateEmployeesCte` + `FilterByEffectiveApproverAsync`; the approval-surface census below) |
| **KB Refs** | ADR-038 D4 (see==act), the DesignatedApproverAuthorizer contract |

**Description**: Extend the my-reports dashboard reads so the actor SEES exactly the employees they may ACT on (the single-encoding contract). **[BLOCKER fix — the reads are a TWO-STAGE pipeline, not a single query]:** each builds a candidate SUPERSET via `DesignatedCandidateEmployeesCte` (a reporting-EDGE descent) then filters via `FilterByEffectiveApproverAsync` → the edge-only predicate. A unit member whose primary edge does not descend from the actor never enters the candidate set, so BOTH stages must move in lockstep: (a) extend `DesignatedCandidateEmployeesCte` with a UNION seeding the direct members of units the actor leads (`unit_leaders` join on `E.unit_id`) + members of units led by managers the actor is an active vikar for; (b) extend `FilterByEffectiveApproverAsync` to the SAME extended predicate (`IsEffectiveDesignatedApprover OR IsUnitLeaderApprover`). **[BLOCKER fix — the floors]:** the inclusion must apply the SAME gates as TASK-10501's predicate (active actor + active LeaderOrAbove + same-Organisation + active-vikar semantics) — NOT a bare `unit_leaders` join — else a bare/expired/Employee-role row creates see-without-act. Kept INSIDE the actor's accessible-org bound (no cross-org leak).
- **[WARNING fix — the approval-surface census] in-lockstep extension of the OTHER edge-keyed ACT surfaces** so see==act holds on every approval surface, not just the two named reads: `GetTeamOverviewRosterAsync` (`GET /api/approval/team-overview` — the LEADER's primary surface, S87) + the per-employee allocation-breakdown authorization (`ApprovalEndpoints.cs:1129`, currently edge-ONLY, no HR/Admin fallback → a unit leader who can ACT would 403 on expand). **Explicitly SCOPED OUT (with rationale):** `GetPeriodStatusProjectionForTreeAsync` (the medarbejder-tree TILE counts) — an HR roster aggregate, not an approval-action surface; fail-closed (under-reports, no leak); deferred to the Phase-3 FE rework. Recorded, not silently dropped.

**Validation Criteria**:
- [ ] BOTH dashboard stages (candidate CTE + filter) extended in lockstep with the FULL floors (active/LeaderOrAbove/same-Org/vikar); a secondary unit-leader's dashboard + team-overview show exactly their unit's direct members + their existing edge reports — the my-reports reads add **edge OR unit-leader visibility ONLY, NOT HR/Admin scope** (that stays the separate non-my-reports org-scope branch, unexpanded); NOTHING from sibling/parent/grandparent units.
- [ ] Negative (see==act): an Employee-role `unit_leaders` row sees nothing; an inactive/expired-role unit leader sees nothing; a vikar without the active/role/same-Org checks sees nothing.
- [ ] The allocation-breakdown 1129 gate extended (a unit leader can expand a direct member); the tile-count scope-out documented; no cross-org leak.

---

### TASK-10503 — ADR-038 D4 amendment + the absence-guard SPLIT + the boundedness RED test (P7 keystone)
| Field | Value |
|-------|-------|
| **ID** | TASK-10503 |
| **Status** | complete — ADR-038 D4 amendment authored (the in-lock advisory + the two-stage/shared-helper + the audit classification + the ≥2-level boundedness) + the `UnitAuthorityAbsenceTests` SPLIT (Agent A removed `DesignatedApproverAuthorizer` from the scanned set; `OrgScopeValidator`/`RoleScope` KEPT — the surviving SCOPE keystone; 2/2 green). The behavioural boundedness RED test lands in TASK-10504. |
| **Agent** | Orchestrator (ADR + KB) + Test & QA (the guard + RED test) |
| **Components** | docs/knowledge-base/decisions/ADR-038 (D4 amendment), tests/.../UnitAuthorityAbsenceTests.cs (split), a new boundedness test |
| **KB Refs** | ADR-038 D4/D5, docs/SECURITY.md |

**Description**: Amend ADR-038 D4 to ACCEPTED-AS-IMPLEMENTED (the unit-leader approval is live; the guard's own failure message prescribed this explicit amendment). **Reframe the `UnitAuthorityAbsenceTests`:** the guard today scans exactly 3 files — `OrgScopeValidator`, `RoleScope`, `DesignatedApproverAuthorizer` (`ApprovalPeriodRepository` was NEVER in it). (a) the **SCOPE guard** KEEPS `OrgScopeValidator`/`RoleScope.CoversOrg`/the `ValidateEmployeeAccess` path unit-free (D5 — units grant NO scope); this is the surviving keystone assertion; (b) `DesignatedApproverAuthorizer` is REMOVED from the scanned set (units LEGITIMATELY enter the approval EDGE there, D4) — replaced by the behavioural boundedness test below. Add the **boundedness RED test** (a **≥2-level** fixture: a GRANDPARENT-unit leader, not just the immediate parent, to robustly catch any ancestor-walk reintroduction): a leader designated on any non-`E.unit_id` unit (grandparent / parent / sibling) grants NO `CanApprove` AND appears in NO dashboard/team-overview read for an `E.unit_id` member — RED on a naive `is-ancestor`/subtree implementation. The proof that D4 is direct-member-bounded and does NOT reintroduce subtree inheritance (the S76/S85/S91 guard).

**Validation Criteria**:
- [ ] ADR-038 D4 amended (live); KB INDEX entry updated.
- [ ] The SCOPE guard still RED-on-a-unit-token in `OrgScopeValidator`/`RoleScope`/`ValidateEmployeeAccess`; `DesignatedApproverAuthorizer` removed from the scanned set with the documented D4 rationale + the boundedness test as the replacement.
- [ ] The boundedness RED test uses a ≥2-level fixture (grandparent leader) and covers BOTH action authorization AND dashboard/team-overview visibility; RED on a naive ancestor implementation.

---

### TASK-10504 — Tests (the approval-boundary suite + see==act parity + vikar + S104 follow-ups)
| Field | Value |
|-------|-------|
| **ID** | TASK-10504 |
| **Status** | complete — `S105UnitLeaderApprovalTests` (12 Docker-gated): the keystone ≥2-level boundedness (grandparent/parent/sibling-leader denied action AND dashboard visibility; direct leader allowed; RED-on-naive-subtree) + secondary-leader-approve (`UNIT_LEADER`) + vikar (`UNIT_LEADER_VIKAR`, bad-vikar denied) + see==act parity (my-reports edge-OR-unit-leader only) + dashboard negatives (Employee/inactive/expired) + the in-lock race (revoke wins → 403, no stale approval) + cross-Org + orphan-to-HR + HR/edge regression + leaderless-no-dead-end + the S104 inactive-member-rehome. **The agent ran Docker: 12/12 passed (41s); build 0/0; scope-guard 2/2. NO authority bug found.** |
| **Agent** | Test & QA |
| **Components** | tests/** |
| **KB Refs** | ADR-038 D4/D5, S104 Step-7a follow-ups |

**Description**: The S105 authority suite (Docker-gated): a secondary unit-leader approves/rejects a direct member (200, recorded `approval_method = UNIT_LEADER`); a **grandparent/parent/sibling-unit leader is denied** (403 — the ≥2-level boundedness pin, RED-on-naive-subtree); the **vikar-of-a-unit-leader** approves (`UNIT_LEADER_VIKAR`); the HR/Admin fallback unchanged; the **see==act parity** (dashboard + team-overview set == the act-able set, for a secondary leader); the **dashboard negatives** (an Employee-role `unit_leaders` row / an inactive/expired-role unit leader / a vikar-without-the-checks → sees NOTHING + cannot act); the same-Org re-check (a cross-Org unit-leader designation cannot approve); the orphan (no edge, no unit leader) → in-scope HR/Admin; the **in-lock race** (a concurrent `UnitLeaderRemoved`/member-move vs an in-flight approve serializes on the employee's `unit-org-` advisory — approval denied if the revocation wins; a `pg_locks⋈pg_stat_activity` waiter barrier). Plus the **S104 Step-7a follow-ups**: the delete-cascade **inactive-member rehome** (an inactive member of a deleted unit re-homes; reactivation into a deleted unit prevented); the **leaderless-unit fallback** (members but no `unit_leaders` → approval routes to the primary edge / HR-Admin, no dead end).

**Validation Criteria**:
- [ ] The full S105 authority suite green: secondary-leader-approves (`UNIT_LEADER`) + ≥2-level boundedness (grandparent denied) + vikar (`UNIT_LEADER_VIKAR`) + see==act parity (dashboard + team-overview) + the dashboard negatives + same-Org + orphan-to-HR + the in-lock race.
- [ ] The S104 inactive-member-rehome + leaderless-unit-fallback tests green.
- [ ] Full pyramid green; FAIL-002 sheds isolation-cleared if any.

---

### TASK-10505 — DemoSeed inert-enhed dead-code cleanup (the twice-deferred hygiene)
| Field | Value |
|-------|-------|
| **ID** | TASK-10505 |
| **Status** | complete — the inert enhed dead-code removed from DemoSeed (5 files: `StructuralModels` `DemoEnhed`/`DemoUserEnhed`/`Enheder`/`UserEnheder`/`EnhedLabel`, `DemoManifest.EnhedLabel`, `DemoGenerator.GenerateEnheder`/`DeterministicEnhedId`/the EnhedLabel writes, `DanishPools.EnhedFragments`, README). Build 0/0; generated `99-demo-seed.sql` **byte-identical** (the enhed round-robin consumed no RNG); 29 demoseed tests green; 0 residual symbols. The twice-deferred (S103/S104) hygiene CLOSED. |
| **Agent** | Test & QA + tooling (cross-domain authorized) |
| **Components** | tools/StatsTid.DemoSeed/** (StructuralModels `DemoEnhed`/`DemoUserEnhed`/`Enheder`/`UserEnheder`/`EnhedLabel`, `DemoGenerator.GenerateEnheder`, `DanishPools.EnhedFragments`, the manifest `EnhedLabel`) |
| **KB Refs** | S103/S104 Step-7a NOTE |

**Description**: Remove the inert enhed dead-code in the DemoSeed generator (flagged S103 + S104; harmless — the emitter no longer references it, so the generated SQL is already enhed-free). A careful, isolated multi-file refactor (EnhedLabel is woven through user generation): drop the models + the generation + the manifest field; keep the DemoSeed compiling + the 29 demoseed tests green + the generated overlay enhed-free. **Isolation rule (both Step-0b lenses):** this is unrelated `tools/`-only churn inside the highest-risk authority sprint — do it LAST + isolated, and DROP it to a follow-up if any S105 security work slips (so the high-risk Step-7a diff stays tight).

**Validation Criteria**:
- [ ] `grep enheder|enhed_label|GenerateEnheder|EnhedFragments tools/StatsTid.DemoSeed/` → 0 live; DemoSeed compiles; 29 demoseed tests green; regenerated overlay 0 enhed refs.

---

### TASK-10506 — Validation + Step-7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-10506 |
| **Status** | complete — build 0/0; pyramid 852u+1129r green locally; Step-7a dual-lens (2 cycles, 2 external BLOCKERs absorbed, 0 residual); ADR-038 D4 amendment + QUALITY.md S105 + INDEX/ROADMAP updated. Commit + push + CI-verify next. |
| **Agent** | Orchestrator |

**Validation Criteria**:
- [x] `dotnet build` 0/0; full pyramid green; **Step-7a dual-lens (high-risk override)** → BLOCKERs absorbed; INDEX/ROADMAP updated; commit + push + CI-verify.

---

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules / wage mappings / overtime / absence | N/A | No rule/payroll-logic change. |
| Retroactive recalculation stable | N/A | No calc change; approval-authority surface only. |

## External Review (Step 7a)
Dual-lens on the full uncommitted S105 diff (high-risk override — the approval-authority surface). Artifacts: `.claude/reviews/SPRINT-105-step7a-{codex,reviewer}.md`.

**Codex (external) — 2 P1 BLOCKER, cycle-2 RESOLVED:**
- **BLOCKER — self-approval (segregation of duties):** the `unit_leaders(E.unit_id)` join matched `actorId == employeeId` (a leader IS a member of the unit they lead) → a unit leader could approve/see their OWN period. **FIXED** — a fail-closed `e.user_id <> @actorId` self-exclusion in BOTH the predicate (`DesignatedApproverAuthorizer`) AND the candidate CTE (both UNION branches); pinned by `SelfApproval_UnitLeader_CannotApproveOrSeeOwnPeriod`.
- **BLOCKER — the legacy `approval_method` CHECK:** the inline `ADD COLUMN IF NOT EXISTS` is skipped on a non-fresh DB → the old CHECK rejects `UNIT_LEADER`. **FIXED** — an idempotent guarded `DROP/ADD CONSTRAINT` in init.sql.

**Reviewer (internal) — 0 BLOCKER, 1 WARNING (addressed), 2 NOTE (accepted):** verified the keystone clean (single-table, no ancestor walk), the in-lock D8 lock order deadlock-safe, see==act centralized, floors complete + replay-safe. WARNING: the medarbejder-tree pending-tile under-counts for a secondary unit-leader (edge-only) — a non-authority display drift; **addressed** by the conscious TASK-10502 scope-out + the amended docstring. NOTEs accepted.

Cycle 2 (both lenses): 0 residual BLOCKER. The dual-lens was decisive — the external lens caught a real self-approval security hole that the plan reviews, the implementation agent, AND the 12-test suite all missed ([[review-lens-complementarity]]).

## Test Summary
**Pyramid: 852u + 1129r + 6s + 29demoseed + 517fe = 2533 — VERIFIED GREEN LOCALLY (Docker up this sprint).**

| Tier | Count | Status |
|------|-------|--------|
| Unit | 852 | all passing (1s, no Docker) |
| Regression (Docker-gated) | 1129 | all passing — 1116 S104 baseline + 12 `S105UnitLeaderApprovalTests` + 1 `SelfApproval_UnitLeader…` (the BLOCKER fix) |
| Smoke / E2E | 6 / — | CI (docker-compose) |
| DemoSeed | 29 | all passing (the TASK-10505 cleanup) |
| Frontend (vitest) | 517 | unchanged (no FE in S105) |

The full regression re-ran LOCALLY after the Step-7a BLOCKER fixes: 1087 testcontainer-based tests passed (incl. ALL 13 S105 tests); the only 42 "failures" were the two FIXED-PORT legacy classes (`ReportingLineRepositoryTests` + `ManagerVikarEngineTests`, hardcoded `localhost:5432`) that need a STANDING Postgres my env lacked — re-run against the compose Postgres they passed **42/42 in 1s** (an environment gap, NOT a code/test failure; 0 assertion failures anywhere). CI re-verifies on push (it provides both testcontainers + the 5432 service).

## Sprint Retrospective
- **The deepest review chain of the program.** Step-0b (2 cycles, 2 convergent BLOCKERs — the in-lock `unit-org-` advisory race + the two-stage see==act half-wire) AND Step-7a (2 cycles, 2 external BLOCKERs — self-approval + the legacy CHECK) both materially changed the result. The keystone (units grant the DIRECT-MEMBER approval edge only, single-table, no ancestor walk) held throughout.
- **The self-approval catch is the headline.** A unit leader is a member of the unit they lead, so the obvious `unit_leaders(E.unit_id)` predicate silently granted self-approval — a segregation-of-duties hole the plan, the impl agent, and 12 Docker-verified tests all missed. Only the adversarial external lens, reading the predicate against the member-invariant, caught it. The general lesson: a membership-derived authority predicate must explicitly exclude the actor's own subject. [[review-lens-complementarity]].
- **Docker-local verification restored.** Unlike S103/S104 (no local Docker → CI-pending→backfill), the test agent started Docker and the full regression ran locally — so the BLOCKER fixes were verified against a real DB BEFORE the push, not after.
- **The two-regime lock order composed cleanly.** The S100 `unit-org-` advisory + the S104 transfer `reporting-org-` pair extended to the approval path with no AB/BA cycle (all key on the same Organisation; D8 prefix order). The held-lock race test proves the revoke-vs-approve serialization.
- Durable: SPRINT-105.md + ADR-038 D4 amendment (wired-in-S105) + the Step-7a artifacts. **NEXT (owner's call): Phase 3 — the merged Enhedsspor admin FE (the unit-tree + people on one page; the deferred `design_handoff_org_medarbejdere` Model A), which also closes the tile-count scope-out; then Phase 4 cutover.**
