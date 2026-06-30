# Sprint 110 — Enhedsspor Phase 4: final cleanup + program close-out

| Field | Value |
|-------|-------|
| **Sprint** | 110 |
| **Status** | complete (pending push + CI-verify) |
| **Start Date** | 2026-06-30 |
| **End Date** | 2026-06-30 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes — `dotnet build` + `npm run build` 0/0 |
| **Test Verified** | yes — full pyramid green locally (852u + 1155r [1 FAIL-002 shed isolation-cleared] + 29demoseed + 531fe); Step-7a dual-lens BOTH CLEAN; CI-verify on push |

## Sprint Goal
The FINAL Enhedsspor sprint — cleanup + program close-out (owner chose all 3 candidates + the core cleanup + a best-practice close-out). Four work-streams: (1) **remove the vestigial `enhedLabel` RESPONSE field** (the `employee_profiles.enhed_label` COLUMN was ALREADY dropped in S103 — Step-0b BLOCKER; this is purely the dead response field [= `orgName`, redundant with `primaryOrgName`] + its FE consumers + the contract tests; **NO schema/event change**); (2) the **search "N flere" truncation signal** (surface the per-section total the backend already computes-then-discards); (3) the **MAO-delete-vs-child-orgs guard** (the pre-existing S98 lifecycle gap — block a MAO soft-delete with active child Organisations, AND close the create-side TOCTOU; **the real P7 keystone of this sprint**); (4) the **close-out** — prune the actionable stale comments, reconcile the docs (FRONTEND.md + SYSTEM_DOCUMENTATION.md + SECURITY.md → the single merged-admin surface), mark **ADR-038 as-built COMPLETE**, update the QUALITY grades. On close, the Enhedsspor program (S102→S110) is DONE.

## Entropy Scan Findings
| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py` all hard checks passed (pre-sprint) |
| `enhed_label` column | ALREADY DROPPED (S103) | `init.sql:4075` "enhed_label column REMOVED"; `db-schema.md` shows none; zero SQL readers → TASK-11001 is the response-FIELD removal, not a column drop |
| Transitional markers | NOTED | ~149 TODO/FIXME/transitional markers — most are legitimate inline notes; the close-out targets only the ACTIONABLE retired-thing debt (the `OrganisationPage`/`useMedarbejderRoster` stale comments), NOT a blanket sweep |
| Orphan detection | CLEAN (carried) | S109 CI-green `28447245606` |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (the NEW authority/lifecycle guard [the MAO-delete guard + its create-side TOCTOU — P7] is the keystone; TASK-11001 is NOT a schema change [the column was dropped S103] — it is an additive-field-removal contract change). |
| **External Codex** | invoked 2026-06-30 — 1B/3W/1N → cycle-2 all RESOLVED (the SECURITY.md-in-criteria residual fixed) |
| **Internal Reviewer** | invoked 2026-06-30 — 1B/2W/4N → cycle-2 all RESOLVED (the stale "column drop" phrasing + the 11002 agent-field residuals fixed) |
| **BLOCKERs resolved before Step 1** | yes — the 2 convergent BLOCKERs (the column-already-gone reframe + the create-side TOCTOU) absorbed; cycle-2 both lenses RESOLVED, 0 residual |

### Findings (cycle 1)
Both lenses converge on the 2 BLOCKERs + the WARNINGs/NOTEs (all absorbed):
- **BLOCKER (CONVERGENT, TASK-11001) — the `enhed_label` COLUMN was already dropped in S103** (`init.sql:4075`; `db-schema.md` shows none; zero SQL readers). NOT a schema change → re-scoped to the vestigial **`enhedLabel` response-field** removal (= `orgName`, redundant); the full consumer list named (`PersonSearchHit`/`useReportingLines`, `PersonPickerDialog` [the live S109 "Ret" picker], `useRoster`, 5 fixtures) + the 4 registry-gated contract tests; prune stale prose comments (NOT the immutable `schema_migrations` ledger). The "schema change" framing deleted; the MANDATORY trigger re-grounded on TASK-11003.
- **BLOCKER (Codex) / WARNING (Reviewer) — CONVERGENT (TASK-11003): the delete-side lock alone does NOT close the TOCTOU.** The 2-level FK takes `FOR KEY SHARE` on the MAO (serializing the lock) but does NOT check `is_active` → "create reads MAO active → delete commits → create's INSERT lands" orphans an active org under a dead MAO. → a symmetric in-tx **active-parent re-check** on org-create (+ confirm org-move); the concurrency test must exercise the **create-after-count** ordering.
- WARNING (both, TASK-11001) — the FE consumer list was understated (`PersonPickerDialog` renders it live) → enumerated.
- NOTE (both, TASK-11002) — the per-section total is already computed-then-discarded → surface it additively (not the `==200` heuristic); update the `GetSearch` contract test. → absorbed.
- NOTE (Reviewer, TASK-11004) — add `docs/SECURITY.md` (the D5 invariant + enhedLabel prose) + the runbook/KB to the reconcile. → absorbed.
- NOTE (both) — sequence the 3 `AdminEndpoints.cs` tasks (11001/11002/11003) on ONE backend agent (different regions, same file). → absorbed (TASK-11001 agent note + below).

### Resolution
The 2 convergent BLOCKERs + the WARNINGs + the NOTEs absorbed into TASK-11001/11002/11003/11004. The backend edits to `AdminEndpoints.cs` (11001 response fields / 11002 the total / 11003 the guard+TOCTOU) sequence on ONE backend agent.

**Cycle 2 (verification):** BOTH lenses confirm all findings RESOLVED + no new BLOCKER. Three residual NOTE-level cleanups absorbed: the SECURITY.md/runbook/KB additions promoted into TASK-11004's validation criterion; the stale "column drop" phrasing (P8/TASK-11000/TASK-11005) reworded to "field removal"; the TASK-11002 agent-field aligned to the one-backend-agent sequence. **0 residual BLOCKER; cycle-cap (2/lens) respected. Cleared for Step 1.** The two convergent BLOCKERs were the value: TASK-11001 was mis-scoped as a schema change (the column was already dropped S103 — it is a vestigial response-field removal), and the MAO-delete guard's real risk is the create-side TOCTOU (the delete-side lock alone doesn't serialize against an unlocked create).

## Architectural Constraints Verified
- [x] P3/P4 — the `enhedLabel` removal touched NO event, NO schema (the column was dropped S103), NO projection write → an additive-field REMOVAL from 3 read responses; replay-safe BY CONSTRUCTION; the 4 contract tests updated (assert absence) so the lint gate stays honest. (Both Step-7a lenses confirmed completeness — the `JOIN organizations` correctly retained for the scope WHERE.)
- [x] P7 — the MAO-delete guard is an ADDITIVE block + the symmetric create-side active-parent re-check (TOCTOU CLOSED — the delete locks `FOR UPDATE` at its first step; the create `FOR SHARE`+is_active recheck → serialize; deadlock-free; `pg_blocking_pids` test on the dangerous ordering); the active-user block unchanged; no authority granted. Both lenses verified correct.
- [x] P8 — the field removal broke no real-contract test (the 4 registry-gated PAT-010 tests updated to assert absence); full pyramid green locally (CI re-verifies).
- [x] P9 — the search "N flere" signal is honest (Afgrænsning-aware — server total vs server-returned count; a capped set is no longer mistaken for complete); no dead button.

## Task Log

### TASK-11000 — Sprint open (entropy + plan + Step-0b)
| Field | Value |
|-------|-------|
| **ID** | TASK-11000 |
| **Status** | complete — entropy CLEAN; plan authored; Step-0b dual-lens (2 cycles; 2 convergent BLOCKERs absorbed — the `enhed_label` column-already-gone reframe [TASK-11001 is a response-field removal, not a schema change] + the MAO-guard create-side TOCTOU; + the consumer-list/contract-test/docs/sequencing NOTEs; 0 residual). |
| **Agent** | Orchestrator |
| **KB Refs** | the S96/S97 `enhed_label` transitional notes, ADR-038 D9 (greenfield), the S106 search NOTE, the S98 MAO-delete path |

**Validation Criteria**:
- [x] Entropy recorded; plan authored; Step-0b dual-lens run (the `enhedLabel` field-removal scope + the MAO-guard authority/TOCTOU + the close-out scope); BLOCKERs absorbed before Step 1.

---

### TASK-11001 — Remove the vestigial `enhedLabel` RESPONSE field
| Field | Value |
|-------|-------|
| **ID** | TASK-11001 |
| **Status** | complete — backend: `enhedLabel` removed from the roster + person-search responses/records; the 4 registry-gated contract tests updated (assert absence); contract lint passes. FE: removed from `useRoster`/`useReportingLines` types + `PersonPickerDialog` (now `primaryOrgName` only — no duplicate) + 6 fixtures; grep-zero (only an accurate historical comment remains). NO column/db-schema/event change (the column was dropped S103). |
| **Agent** | one backend agent (the `AdminEndpoints`/`ApprovalPeriodRepository` edits) THEN a UX pass (FE) — sequenced |
| **Components** | `ApprovalPeriodRepository.cs` (the roster/search record `EnhedLabel` + the build at :874/:914), `AdminEndpoints.cs` (the response fields ~:2608/:2696), `frontend/src/hooks/useRoster.ts:46` + `useReportingLines.ts:35` (`PersonSearchHit`), `frontend/src/pages/admin/editPerson/PersonPickerDialog.tsx:173` (renders it live — the S109 "Ret" picker), the 5 test fixtures (`useRoster.test`, `CapabilityMatrix.test`, `StrukturPanel.test`, `UnitDrawer.test`, `LifecycleSections.test`), the contract tests below |
| **KB Refs** | the S96/S97 notes; ADR-038 D9; PAT-010 |

**Description**: **[Step-0b BLOCKER, BOTH lenses — re-scoped]: the `employee_profiles.enhed_label` COLUMN was ALREADY dropped in S103** (`init.sql:4075` "enhed_label column REMOVED"; `db-schema.md` shows none; ZERO SQL readers). So this is **NOT a schema change** — no column drop, no db-schema regen, no event (the `EmployeeProfile*` events never carried it). The remaining debt is the vestigial **`enhedLabel` RESPONSE field** — built as `EnhedLabel: orgName` (`ApprovalPeriodRepository.cs:874`), redundant with the `primaryOrgName` every one of these responses ALSO carries (`PersonPickerDialog:173` even renders "enhedLabel · primaryOrgName" — a visible duplicate). **Remove the field** from: the roster + search + person-search responses (the repository record + the `AdminEndpoints` projections) + the FE types (`useRoster`/`useReportingLines` `PersonSearchHit`) + the live consumer (`PersonPickerDialog` → use `primaryOrgName` only) + the 5 test fixtures. **[Step-0b NOTE — the registry-gated contract tests that pin it]:** update `GetRoster_IsEnvelope_RowsCarryUnitTagFieldSet` (`S106RosterUnitTagTests.cs:464`), `MedarbejderRosterReadTests.cs:447`, `UnitFoundationTests.cs:338`, `PeriodStatusAndPersonSearchReadsTests.cs:444` — else the `check_endpoint_contracts.py` gate trips at CI. Also prune the stale `enhed_label` prose comments (init.sql display-comments, NOT the `schema_migrations` ledger text — that is an immutable historical record).

**Validation Criteria**:
- [ ] The `enhedLabel` response field removed from all 3 responses + the FE types + `PersonPickerDialog` (renders `primaryOrgName` only — no duplicate) + the 5 fixtures; grep-zero of live `enhedLabel`; the 4 contract tests updated (the lint gate green); NO column/db-schema/event change; `dotnet build` + `npm run build` + the affected tests green.

---

### TASK-11002 — The search "N flere" truncation signal
| Field | Value |
|-------|-------|
| **ID** | TASK-11002 |
| **Status** | complete — backend: `SearchResponse` += `unitsTotal`/`peopleTotal` (the discarded `matched→total→page` CTE totals; the `GetSearch` contract test updated). FE: `SearchOverlay` shows "{N} flere — forfin søgningen" per truncated section, computed from the **server total vs the server-RETURNED count** (NOT the Afgrænsning-narrowed displayed count → honest about the server cap, never falsely claims completeness for the filtered view). +3 vitest. |
| **Agent** | the ONE backend agent (the `SearchResponse` total — same `AdminEndpoints.cs` as 11001/11003) THEN a UX pass (the overlay signal) — sequenced |
| **Components** | `AdminEndpoints.cs` (surface the discarded per-section total into `SearchResponse`), the FE search overlay, the `GetSearch` contract test |
| **KB Refs** | the deferred S106 Reviewer NOTE (the 200/section silent cap) |

**Description**: The search overlay silently truncates at 200 results per section → a capped set reads as complete. Surface an honest "viser X af Y" / "N flere — forfin søgningen" indicator per section when truncated. **[Step-0b NOTE, BOTH lenses — the total is ALREADY computed and discarded]:** `SearchUnitsAsync`/`SearchPeopleForOverlayAsync` return exact totals (the `matched→total→page` CTE, `ApprovalPeriodRepository.cs:1207-1208`) but `AdminEndpoints.cs:2744-2747` discards them (`var (unitHits, _)`) and `SearchResponse(units, people)` carries no total. **Surface the discarded per-section total into `SearchResponse` (additive)** — NOT the `items.length==200` cap heuristic (which false-positives at exactly 200 real hits). `/api/admin/search` is registry-gated (`check_endpoint_contracts.py:98` → `GetSearch_IsTwoSectionEnvelope_…`) → the additive field updates that contract test. Define the FE behavior after the Afgrænsning client-filter (the signal reflects the displayed section).

**Validation Criteria**:
- [ ] `SearchResponse` carries the per-section total (additive); a truncated section shows "N flere"/"X af Y", a non-truncated section none; the `GetSearch` contract test updated; vitest pins both FE states.

---

### TASK-11003 — The MAO-delete-vs-child-orgs guard
| Field | Value |
|-------|-------|
| **ID** | TASK-11003 |
| **Status** | complete (backend) — **delete-side block** (`CountActiveChildOrganisationsAsync`, `materialized_path` subtree, under the existing `FOR UPDATE`; >0 → 422 "Ministerområdet har aktive organisationer…"; 2-level assumption pinned) + the **create-side TOCTOU close** (`LockActiveByIdForShareAsync` — `FOR SHARE` + `is_active` recheck before INSERT; conflicts with the delete's `FOR UPDATE` → serialize; 422 "Ministerområdet er slettet") + org-MOVE confirmed already safe (`LockActiveByIdAsync`). `S110MaoDeleteGuardTests` 6/6 incl. the **concurrency keystone** (`pg_blocking_pids` barrier: the create parks blocked then 422s on the inactive recheck → zero active orgs under the dead MAO). |
| **Agent** | Security/Data Model (backend) |
| **Components** | `AdminEndpoints.cs` (the `DELETE /api/admin/organizations/{id}` MAO branch) |
| **KB Refs** | the S98 org soft-delete (blocked-if-employees); the S108 Step-7a flag; ADR-037 (MAO=`materialized_path` ancestor) |

**Description**: The pre-existing S98 gap (flagged at S108 Step-7a): a MAO soft-delete is blocked only on active users in its subtree, NOT on active child Organisations → an "empty" MAO (no direct users) with active child Organisations can be soft-deleted, leaving them path-rooted under an inactive MAO. **The DELETE-side guard:** when deleting a MAO, also block (422) if it has active child Organisations — `COUNT` of active `organizations` under this MAO, mirroring the active-user block's `materialized_path LIKE … AND is_active=TRUE` form (the model is 2-level [`AdminEndpoints.cs:114-139`] so "direct child" == "subtree" today — **pin the 2-level assumption** so a future deeper model doesn't silently under-block). In-tx FOR UPDATE on the MAO row (consistent with the S98 path); GlobalAdmin; the existing active-user block unchanged. A Danish 422 ("Ministerområdet har aktive organisationer — flyt eller slet dem først"). **Scope-tight: the MAO branch only.**

**[Step-0b BLOCKER (Codex) / WARNING (Reviewer) — CONVERGENT: the delete-side lock ALONE does NOT close the TOCTOU]:** the org-CREATE-under-MAO path does a plain `GetByIdAsync(parent)` then INSERTs with **NO in-tx re-check of the parent's `is_active`** (`AdminEndpoints.cs:132-202`). The child INSERT's FK takes `FOR KEY SHARE` on the MAO row (serializing the LOCK against the delete's `FOR UPDATE`), but the FK only requires the row to EXIST, not be active → the ordering "create reads MAO active → delete locks, counts 0 children, soft-deletes, commits → create's INSERT lands" still orphans an active org under a dead MAO. **FIX (symmetric):** add an in-tx **active-parent re-check** to the org-create path (`FOR SHARE` lock + `is_active` recheck on the parent MAO before the INSERT; 422 if inactive) — AND confirm the S98 org-MOVE locks+rechecks the target MAO's `is_active`. The concurrency test must exercise the **create-after-count** ordering (not just the benign create-before-count, which passes vacuously).

**Validation Criteria**:
- [ ] Deleting a MAO with active child Organisations → 422 (RED-on-old); deleting an empty MAO → succeeds; the existing active-user block + Organisation-delete unchanged.
- [ ] The org-CREATE path re-checks the parent MAO's `is_active` in-tx under a lock (422 if inactive); org-MOVE rechecks the target; a concurrency test proves the **create-after-count** ordering CANNOT commit an active org under an inactive MAO (the create blocks/aborts).

---

### TASK-11004 — Close-out: dead-code/comment prune + docs/ADR/QUALITY reconcile
| Field | Value |
|-------|-------|
| **ID** | TASK-11004 |
| **Status** | complete — ADR-038 marked **as-built COMPLETE** (the program shipped S102→S110); `SECURITY.md` += the S110 MAO-guard + the now-CLOSED create-vs-delete residual; `QUALITY.md` S110 close-out note (anchor→110); the `legacy-db-upgrade-runbook.md` enhed_label section + the KB INDEX ADR-035/036 rows marked **SUPERSEDED by ADR-038**; the stale FE comments pruned (Agent B — `useRoster`/`useAdmin`). FRONTEND.md was reconciled at S109. `check_docs.py` green. |
| **Agent** | Orchestrator (docs are Orchestrator-only) + UX (the FE comment prune) |
| **Components** | the stale FE comments (`useAdmin.ts`/`useRoster.ts`), `docs/FRONTEND.md`, `docs/SYSTEM_DOCUMENTATION.md`, **`docs/SECURITY.md`** (the ADR-038 D5 invariant + enhed/enhedLabel prose — Step-0b NOTE), the ADR-038 KB entry, `docs/QUALITY.md`, `docs/operations/legacy-db-upgrade-runbook.md` + the KB INDEX ADR summaries (mark legacy-only/retired so ADR-038 COMPLETE isn't contradicted — Codex NOTE) |
| **KB Refs** | ADR-038 (the program contract), the retired-page comments |

**Description**: The actionable close-out (NOT a blanket 149-marker sweep): (a) prune the stale retired-thing comments (the `OrganisationPage`/`useMedarbejderRoster` references in `useAdmin.ts`/`useRoster.ts` — update to the merged-page reality); (b) **reconcile the docs** — `FRONTEND.md` (confirm the merged-admin route table + no stale two-page prose) + `SYSTEM_DOCUMENTATION.md` (the onboarding guide → the single merged-admin surface + the Enhedsspor unit model); (c) **mark ADR-038 as-built COMPLETE** (fold the per-sprint amendments [D4/D5 wired S105; the program realized S106–S109] into a coherent end-state note; status → the program is shipped); (d) update the **QUALITY grades** for the program's domains (Frontend/org-model); (e) note the retired `Enhed*` events' disposition (kept name-keyed for replay-safety — confirm intentional vs greenfield-removable; document the decision).

**Validation Criteria**:
- [ ] The stale retired-thing comments pruned/updated; **FRONTEND.md + SYSTEM_DOCUMENTATION.md + `docs/SECURITY.md`** reconciled to the merged surface (no stale two-page prose; the D5 invariant + enhedLabel prose updated); the `legacy-db-upgrade-runbook.md` + the KB INDEX ADR summaries marked legacy-only/retired (so ADR-038 COMPLETE isn't contradicted); ADR-038 marked as-built COMPLETE; QUALITY grades updated; the `Enhed*` event disposition documented; `check_docs.py` green.

---

### TASK-11005 — Validation + Step-7a + close (THE PROGRAM CLOSE)
| Field | Value |
|-------|-------|
| **ID** | TASK-11005 |
| **Status** | complete — builds 0/0; full pyramid green locally (852u + 1155r [1 FAIL-002 shed isolation-cleared] + 29demoseed + 531fe; Smoke env-bound); Step-7a dual-lens BOTH CLEAN (0B/0W); INDEX/ROADMAP/QUALITY/ADR-038/SECURITY.md/runbook/KB updated. Commit + push + CI-verify next. |
| **Agent** | Orchestrator |

**Validation Criteria**:
- [x] builds 0/0; full pyramid green locally (the enhedLabel removal + the MAO guard + the search signal exercised; 1 FAIL-002 shed isolation-cleared); Step-7a dual-lens BOTH CLEAN; docs updated; commit + push + CI-verify. **THE ENHEDSSPOR PROGRAM (S102→S110) IS COMPLETE.**

---

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules / payroll | N/A | A vestigial-column drop + a lifecycle guard + an FE signal + docs; no agreement/payroll logic touched. |

## External Review (Step 7a)
Dual-lens on the full uncommitted S110 diff. Artifacts: `.claude/reviews/SPRINT-110-step7a-{codex,reviewer}.md`. **BOTH lenses CLEAN — 0 BLOCKER, 0 WARNING.**

**Codex (external):** "No sprint-end blocking issues found in the MAO delete/create serialization, enhedLabel removal, search totals surfacing, or close-out docs." 0 BLOCKER.

**Reviewer (internal):** 0 BLOCKER, 0 WARNING, 5 NOTE (all confirmations / by-design / pre-existing). The keystone (TASK-11003) verified **correct + race-closed + deadlock-free**: the delete acquires the `FOR UPDATE` at its FIRST step (before counting), so no ordering commits an active org under a soft-deleted MAO (create wins → the delete's count sees the child → 422; delete wins → the create's `is_active` re-read returns null → 422); one row-lock each → no deadlock; the test exercises the DANGEROUS ordering (`pg_blocking_pids` barrier). The `enhedLabel` removal complete (the `JOIN organizations` correctly retained — load-bearing for the `materialized_path` scope; no payroll/audit reader depended on the field); the search totals exact; the docs match the code. NOTEs: a pre-existing out-of-tx `materializedPath` derivation (safe — MAO paths immutable); the "N flere" by-design caveat (capped+scoped counts out-of-scope hidden rows — a "forfin søgningen" hint, acknowledged); historical enhedLabel mentions in past-sprint docs (don't contradict COMPLETE; ADR-035/036 now INDEX-marked SUPERSEDED).

[[review-lens-complementarity]] — a fitting final sprint: the one risk-bearing change (the MAO-guard TOCTOU) verified correct by both lenses.

## Test Summary
**Pyramid: 852u + 1155r + 6s + 29demoseed + 531fe = 2573 — VERIFIED LOCALLY (full backend regression + FE vitest).** Backend regression **1155** (S109's 1149 + the `S110MaoDeleteGuardTests` 6; the enhedLabel contract-test updates + the search-total test net-neutral) — re-run was 1154/1155 with 1 FAIL-002 testcontainer shed (`AllocationGateTests.Balanced_Approves` — `Npgsql: Exception while writing to stream`, unrelated to S110), **isolation-cleared** (re-ran `AllocationGateTests` 7/7). Unit 852 ✓; DemoSeed 29 ✓; FE vitest **531** (S109's 528 + 3 search-signal − fixture deltas) ✓; `dotnet build` + `npm run build` 0/0. (Smoke 6 fail LOCALLY = the no-full-stack environment — CI runs them with the compose stack up.) CI re-verifies the full pyramid + the e2e on push.

## Sprint Retrospective
- **THE ENHEDSSPOR PROGRAM (S102→S110) IS COMPLETE.** The merged "Organisation & medarbejdere" page is the single admin surface; the D5 Organisation authority boundary held by construction across all 9 sprints; ADR-038 is marked **as-built COMPLETE**.
- **The Step-0b was the value, again — both convergent BLOCKERs reshaped the work pre-code:** (1) TASK-11001 was mis-scoped as a schema change, but the `enhed_label` COLUMN was already dropped in S103 (`db-schema.md`-confirmed) → re-scoped to the vestigial `enhedLabel` RESPONSE-field removal (= `orgName`, redundant), with the full consumer list + the 4 registry-gated contract tests named; (2) the MAO-delete guard's delete-side lock ALONE doesn't close the TOCTOU — the org-CREATE path reads the parent MAO unlocked, so the create-side needs the symmetric `FOR SHARE` + `is_active` re-check. Both absorbed before code.
- **Step-7a — BOTH lenses CLEAN (0B/0W):** the keystone TOCTOU fix verified correct + race-closed (the delete locks at its FIRST step before counting → no ordering commits the bad state) + deadlock-free, backed by a `pg_blocking_pids` test exercising the DANGEROUS ordering. The cleanup streams complete + consistent; the contract/FE tests are real regression guards.
- **The "drop enhed_label" item is a lesson in grounding:** the headline turned out to be 90%-done already (S103 dropped the column) — only a vestigial display field + docs remained. The Step-0b reader-audit + the `db-schema.md` check prevented a no-op "column drop" + a false "schema change" framing.
- Durable: SPRINT-110.md + the Step-7a artifacts + ADR-038 (as-built COMPLETE) + SECURITY.md (the MAO guard) + the runbook/KB legacy markers. **NEXT: the Enhedsspor program is DONE — open to a wholly new feature, or any deferred backlog the owner chooses.**
