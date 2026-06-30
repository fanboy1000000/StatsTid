# Sprint 108 — Enhedsspor Phase 3b-2a: the merged-admin STRUCTURE editing

| Field | Value |
|-------|-------|
| **Sprint** | 108 |
| **Status** | complete — CI GREEN `28408033942` |
| **Start Date** | 2026-06-29 |
| **End Date** | 2026-06-30 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes — `dotnet build` + `npm run build` 0/0 |
| **Test Verified** | yes — **CI GREEN `28408033942` (all 7 jobs; frontend-build + e2e + smoke + the unchanged regression 1149)**; FE 601 vitest local |

## Sprint Goal
Make the S107 merged page's STRUCTURE editable (owner-chosen structure-then-people split): the **Unit drawer** (create child unit / rename / move / delete + leader designate-remove) + the **org/MAO mutations** (Organisation create/rename, MAO-create, Organisation move/delete) — wired to the EXISTING endpoints (S104 `UnitEndpoints` + S98/S99 org mutations in `AdminEndpoints`), **capability-gated to the LIVE floors** (unit CRUD/leaders = LocalHR; Organisation create/rename = LocalAdmin; MAO-create + Organisation move/delete = GlobalAdmin), the backend re-checking every mutation. This is the **S91 dead-button discipline IN REVERSE** (wire-before-render — like S100 inverted S99): the structure-mutation affordances S107 asserted ABSENT now render (gated), and the S107 no-mutation vitests for the STRUCTURE surface INVERT (RED-on-old). **People mutations (the Person drawer, person-unit-assign, cross-unit "Ret", vikar-edit) + the CUTOVER stay S109.** FE-only (no new backend — the mutations exist).

## Entropy Scan Findings
| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py` all hard checks passed |
| Pattern compliance | CLEAN | the role gate primitive `hasMinRole(role,minRole)` via `useAuth` exists; the org mutation hooks `useOrganizations`/`useOrganizationStructure` exist to reuse |
| Orphan detection | CLEAN (carried) | S107 page CI-green `28397136776` |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P7-adjacent — the FE capability-gating must mirror the live role floors [the backend is the enforcer, but a wrong gate is a UX/expectation bug]; P9; the optimistic/refetch correctness on mutation). |
| **External Codex** | invoked 2026-06-29 — 1B/2W/1N → cycle-2 all RESOLVED |
| **Internal Reviewer** | invoked 2026-06-29 — 1B/1W/1N → cycle-2 all RESOLVED (0 residual BLOCKER) |
| **BLOCKERs resolved before Step 1** | yes — the convergent unit-delete-cascade BLOCKER + the gating-over-grant + the `useAuth` harness gap absorbed; cycle-2 verification run |

### Findings (cycle 1)
Both lenses confirm the capability FLOORS are stated correctly + the FE-gate-is-UX/backend-enforces framing sound + keeping `OrganisationPage` live to S109 safe. Absorbed:
- **BLOCKER (CONVERGENT — Codex WARNING + Reviewer BLOCKER, TASK-10801) — the unit "Slet" is a CASCADE, not a guard:** S104's `DELETE /units/{id}` has NO children/members 422 — it soft-deletes + re-parents children UP / re-homes members UP / clears leaders. The plan built a never-firing blocked-422 branch + failed to warn of the destructive cascade. → rewritten as confirm-and-cascade with the "flyttes ét niveau op" warning; the 422-guard dropped.
- **BLOCKER (Codex, TASK-10803) — the criterion over-granted** ("LocalHR sees Organisation create/rename"). → fixed: LocalHR = UNIT only; LocalAdmin = + Org create/rename; GlobalAdmin = all.
- **WARNING (Reviewer, TASK-10803/10804) — the `useAuth` test-harness gap (load-bearing):** the page+StrukturPanel will import `useAuth` (throws without a provider); the S107 suites have no mock → they'd throw; the inversion is only meaningful under a permitting role (no-role = false-green); the StrukturPanel allowlist needs RE-ARCHITECTING (not just inverting) so the people-guard survives. → absorbed into TASK-10803/10804 (parametrized `useAuth` mock per role; allowlist re-architecture).
- WARNING (Codex, TASK-10801) — the move picker must also exclude same-or-deeper TYPE-RANK targets (partial-rank ordering). → absorbed.
- NOTE (both, TASK-10802) — the org delete is 2-branch (blocked/empty), not 3; MAO-create + Org-create are the SAME endpoint keyed off the body; leader-remove is a path-param. → absorbed.

### Resolution
The 2 BLOCKERs + the WARNINGs + the NOTE absorbed into TASK-10801/10802/10803/10804.

**Cycle 2 (verification):** BOTH lenses confirm all cycle-1 findings RESOLVED + the convergent over-grant BLOCKER resolved + no new BLOCKER. Both caught ONE shared residual — the stale P9 constraint bullet still said "move/delete guards … 422" (the task body was authoritative + right; only the summary line was stale) → FIXED (the unit delete is confirm-and-cascade; create/move + org-delete surface their real 422s). **0 residual BLOCKER; cycle-cap (2/lens) respected. Cleared for Step 1.**

## Architectural Constraints Verified
- [x] P7 — every structure mutation calls the EXISTING server endpoint (which re-checks the floor + concurrency/guards); the FE gating mirrors the live ROLE floors but is NOT the enforcer (verified: the backend 403s a forged/out-of-scope call — the deferred MAO role-vs-scope gate is a UX over-show, not a bypass). NO new authority/scope path.
- [x] P8 — the FE wires the SAME shapes the S104/S98/S99 tests pin (the non-GET mutations are correctly outside the GET-only lint); FE 601 vitest green; backend regression unchanged (FE-only) → CI-carried.
- [x] P9 — wire-before-render (no un-backed button — disabled placeholders only); affordances render per the permitted role (`CapabilityMatrix` keystone); the UNIT delete is confirm-and-CASCADE (re-parent/re-home up, no false 422), create/move surface the parent-validation 422, the ORG delete the blocked-if-employees 422; refetch keeps the tree/Struktur consistent (the partial-success trap fixed).

## Task Log

### TASK-10800 — Sprint open (entropy + plan + Step-0b)
| Field | Value |
|-------|-------|
| **ID** | TASK-10800 |
| **Status** | complete — entropy CLEAN; plan authored; Step-0b dual-lens (2 cycles; 2 BLOCKERs absorbed — the unit-delete cascade-vs-guard [convergent] + the gating over-grant; + the `useAuth` test-harness gap + the move-rank/org-delete-branch corrections; 0 residual). |
| **Agent** | Orchestrator |
| **KB Refs** | REFINEMENT-phase3-merged-fe.md (3b), design_handoff_org_medarbejdere §2, S104 UnitEndpoints, S98/S99 org mutations, docs/FRONTEND.md |

**Validation Criteria**:
- [x] Entropy recorded; plan authored; Step-0b dual-lens run; BLOCKERs absorbed before Step 1.

---

### TASK-10801 — The Unit drawer + unit mutations (create/rename/move/delete + leaders)
| Field | Value |
|-------|-------|
| **ID** | TASK-10801 |
| **Status** | complete (+ the TASK-10803 gating FOUNDATION) — `useUnitMutations` (S104-wired, inline URLs) + `UnitDrawer`/`UnitMoveDialog`/`UnitDeleteConfirm`; the action row gated `canEditUnits = hasMinRole(role,'LocalHR')` in the StrukturPanel title block (`+<ChildType>`/Rediger/Flyt/Slet). **Delete = confirm-and-CASCADE** (no 422-branch); **move excludes `ORD[target]<ORD[moving]`** + self/descendants + "→ Rod". **The `useAuth` harness fix** (parametrized role mock + no-op toast in both S107 suites — the Reviewer BLOCKER) + the **allowlist RE-ARCHITECTED** (unit-action-* allowed; people-surface guard survives). `npm run build` 0 err; **583 vitest** (47 files; +12 UnitDrawer). |
| **Agent** | UX (frontend) |
| **Components** | `frontend/src/pages/admin/enhedsspor/UnitDrawer.tsx` (new) + the page action row + a `useUnitMutations` hook |
| **KB Refs** | S104 `UnitEndpoints` (POST/PUT/PUT-move/DELETE `/api/admin/units` + POST/DELETE `/units/{id}/leaders`), the design's Unit drawer (§2: "Ny [type]", Navn, Ledere checkboxes) |

**Description**: The **Unit drawer** (StatsTid Drawer): create child (`+ <ChildType>` — the type DERIVED from the parent's `CHILD[parentType]` via `typeMaps`; disabled on `enhed`/`ministeromrade`), edit (Rediger — rename + the **Ledere** checkboxes designating peer leaders from the unit's OWN members [the member-invariant — "En leder skal være placeret i denne enhed"]; leader-remove = `DELETE /units/{id}/leaders/{userId}`, a path-param), **delete (Slet) — CONFIRM-AND-CASCADE, NOT a guard [BOTH Step-0b lenses, BLOCKER]:** S104's `DELETE /units/{id}` has NO children/members 422 — it SOFT-deletes + **cascades** (re-parents surviving children UP / re-homes direct members UP / clears leaders). → the Slet flow is a confirm dialog WARNING the operator of the destructive cascade ("Underenheder og medarbejdere flyttes ét niveau op") — do NOT build the (never-firing) blocked-422 branch. **Flyt (move) — the picker excludes self+descendants AND same-or-deeper TYPE-RANK targets [Codex WARNING]** (S104 rejects an invalid type-rank parent — the partial-rank CHILD ordering) + a "→ Rod" option; same-Organisation only. Wired to S104's `UnitEndpoints` with If-Match/version (rename/move/delete). On success: refetch the forest (+ the affected roster); surface the REAL errors (412 stale, 409 dup-name, 422 on create/move parent-validation — NOT on delete).

**Validation Criteria**:
- [ ] Create-child (type-derived, leaf/MAO-disabled) / rename / move (picker excludes self+descendants + same-or-deeper-rank, "→ Rod") / **delete (confirm-and-cascade with the re-parent-up WARNING, no blocked-guard branch)** / leader designate-remove (path-param) all wired to S104 + refetch; If-Match concurrency surfaced; vitest on the drawer + the mutations (mocking the REAL S104 responses).

---

### TASK-10802 — The org / MAO mutations (Organisation create/rename, MAO-create, Org move/delete)
| Field | Value |
|-------|-------|
| **ID** | TASK-10802 |
| **Status** | complete — `useOrgMutations` (S98/S99-wired, the 422 blocked-count parsed) + `OrgStructureDialogs` (create name-only / rename MAO+Org / move / **2-branch** delete / `MaoCreateAction`), reusing the `OrganisationPage` logic. Gated: Org create/rename = LocalAdmin; MAO-create + Org move/delete = GlobalAdmin (floors confirmed vs `AdminEndpoints.cs` `LocalAdminOrAbove`/`HasGlobalScope`); refetch the forest. `OrganisationPage` left LIVE (S109 cutover). `npm run build` 0 err; **597 vitest** (48 files; +13 OrgStructureMutations + per-role gating). |
| **Agent** | UX (frontend) |
| **Components** | the page's org/MAO action affordances + dialogs (reuse the `OrganisationPage` create/rename/move/delete dialog LOGIC) + a `useOrgMutations` hook |
| **KB Refs** | S98/S99 org mutations (`AdminEndpoints` POST/PUT/DELETE `/organizations` + PUT `/move`), the existing `OrganisationPage.tsx` dialogs (the patterns to port) |

**Description**: The Organisation/MAO structure mutations on the merged page, reusing the S98/S99 endpoint contracts + the `OrganisationPage` dialog logic: **Organisation create** (name-only, under a MAO — the S99 name-only-create; LocalAdmin floor) + **rename** (the rename-warning dialog; LocalAdmin); **MAO-create** (GlobalAdmin). **[Reviewer NOTE — same endpoint]:** MAO-create + Organisation-create are the SAME `POST /api/admin/organizations` keyed off the body (parent-less + `orgType=MAO` → GlobalAdmin; under a MAO + `ORGANISATION` → LocalAdmin) — the FE gates the two affordances by role accordingly. **Organisation move** (the Flyt dialog → a new MAO; GlobalAdmin) + **delete (the 2-branch dialog — NOT 3-branch [both lenses NOTE]):** the live `OrganisationPage` `DeleteDialog` is `blocked` (employees → 422-with-count) / `empty` (confirm); PORT THAT, do NOT invent a third branch or the dead flat-Enhed untag branch (GlobalAdmin). On success: refetch the forest; surface the real errors (422 blocked, 400 no-op). (The `OrganisationPage` stays live until the S109 cutover — both surfaces hit the same backend-serialized, guard-checked endpoints, so concurrent use is safe; do NOT delete `OrganisationPage` yet.)

**Validation Criteria**:
- [ ] Org create (name-only) / rename / move / 2-branch delete (blocked-422 / empty-confirm) + MAO-create wired to the existing endpoints (the create/MAO-create same-endpoint-keyed-off-body) + refetch; vitest on the org dialogs + the mutations.

---

### TASK-10803 — Capability-gating + wire-before-render + the S107 vitest INVERSION
| Field | Value |
|-------|-------|
| **ID** | TASK-10803 |
| **Status** | complete (folded into TASK-10801/10802 + verified by the TASK-10804 matrix) — `useAuth`+`hasMinRole` gating on every affordance (unit=LocalHR, Org create/rename=LocalAdmin, MAO-create+Org move/delete=GlobalAdmin); the FE gate is UX, the backend re-checks. Wire-before-render (no un-backed button). The `useAuth` harness fix + the StrukturPanel allowlist re-architecture (TASK-10801). The S107 inversion holds under a permitting role; the people-surface guard survives. |
| **Agent** | UX (frontend) |
| **Components** | the page action-row gating + the role hook (the existing `useAuth`/role context) |
| **KB Refs** | the LIVE floors (S104 LocalHR units; S98/S99 LocalAdmin org create/rename, GlobalAdmin MAO-create/org-move/delete), docs/SECURITY.md, the S107 no-mutation vitests |

**Description**: Render each structure affordance ONLY for the actor's permitted role (the LIVE floors via `hasMinRole`): unit create/rename/move/delete + leaders = **LocalHR**; Organisation create/rename = **LocalAdmin**; MAO-create + Organisation move/delete = **GlobalAdmin**. The backend re-checks every mutation (the FE gate is UX, NOT the enforcer — a 403 from a forged call is still safe). **Wire-before-render** (the S91 discipline IN REVERSE): an affordance renders only when BOTH the role permits AND the mutation is wired. **INVERT the S107 no-mutation vitests** for the STRUCTURE surface (under a PERMITTING role): the S107 tests that asserted "+ ChildType / Rediger / Slet absent on units" now assert they're PRESENT for the permitted role (RED-on-old) — but the PEOPLE-mutation affordances (+ Medarbejder / Ret / Tildel leder / vikar-edit) stay ABSENT (still S109).
- **[Reviewer WARNING — the `useAuth` test-harness gap, load-bearing]:** the merged page (and `StrukturPanel`, which hosts the unit title-block actions) will now import `useAuth`, which THROWS outside an `AuthProvider`; the S107 suites render with NO provider/mock → they would throw on import. → (a) add a **parametrized `useAuth`/role mock** to the page + StrukturPanel suites, one render per role; (b) the inversion must be tested **under a permitting role** (with no role, the structure buttons are absent for the WRONG reason → a false-green on BOTH the inversion AND people-absence); (c) **re-architect the StrukturPanel allowlist keystone** (it currently asserts EVERY button is caret/toggle/breadcrumb/open-unit) so the now-present structure buttons (for the permitting role) are ADDED to the allowlist while the PEOPLE-surface guard (no +Medarbejder/Ret/Tildel-leder/vikar-edit) SURVIVES — not merely "inverted away".

**Validation Criteria**:
- [ ] **A LocalHR sees the UNIT affordances ONLY** (NOT Organisation create/rename, NOT MAO-create/Org-move/Org-delete); a **LocalAdmin** adds Organisation create/rename; a **GlobalAdmin** sees ALL — the gating mirrors the live floors (a vitest per role, each with a parametrized `useAuth` mock). The S107 structure no-button assertions INVERTED (under a permitting role); the StrukturPanel allowlist re-architected so the PEOPLE-mutation guard STILL HOLDS (no +Medarbejder/Ret/Tildel-leder/vikar-edit in S108).

---

### TASK-10804 — Tests
| Field | Value |
|-------|-------|
| **ID** | TASK-10804 |
| **Status** | complete — the drawer/dialog flows + per-affordance gating tests (TASK-10801 +12, TASK-10802 +13) PLUS the consolidated **`CapabilityMatrix.test.tsx`** keystone (one test per role: Employee→none / LocalHR→unit only / LocalAdmin→+org create-rename / GlobalAdmin→all; the people surface absent for ALL roles; node-type exclusivity). The FE gating matched the live floors EXACTLY (no gap found). `npm run build` 0 err; **601 vitest** (49 files). |
| **Agent** | Test & QA (frontend) |
| **Components** | vitest (+ an e2e happy-path if warranted) |
| **KB Refs** | the S107 vitests, the S104/S98 endpoint contracts |

**Description**: The S108 structure-editing test surface (all with a **parametrized `useAuth`/role mock** — the page+StrukturPanel now consume `useAuth`, which throws without a provider; mock it per role-test): the Unit drawer flows (create-child type-derivation, leaf/MAO disabled, rename, move picker excludes self+descendants **+ same-or-deeper-rank**, **delete confirm-and-cascade [no 422-guard branch]**, leader designate/remove); the org/MAO dialogs (create/rename/move/**2-branch** delete + MAO-create); the **capability-gating per role (the load-bearing one): LocalHR = UNIT affordances ONLY; LocalAdmin = + Org create/rename; GlobalAdmin = all**; the S107 inversion (structure buttons present under a permitting role) with the **StrukturPanel allowlist re-architected** so the PEOPLE-surface guard survives (no +Medarbejder/Ret/Tildel-leder/vikar-edit); the refetch-on-mutation consistency. Optionally an e2e create-unit happy path.

**Validation Criteria**:
- [ ] The drawer/dialog/gating(per-role)/inversion vitests green (parametrized `useAuth` mock — no provider-throw); the PEOPLE-mutation surface still absent (the re-architected allowlist holds it); full FE vitest + (if added) e2e green.

---

### TASK-10805 — Validation + Step-7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-10805 |
| **Status** | complete — `dotnet build` + `npm run build` 0/0; FE 601 vitest green; backend UNCHANGED (FE-only → the S107 CI-green 1149 regression carries); Step-7a dual-lens (Codex 0B/1W-deferred + Reviewer 0B/1W-fixed/4N); the `submitEdit` partial-success bug + the stale header FIXED; INDEX/ROADMAP/QUALITY/memory updated. Commit + push + CI-verify next. |
| **Agent** | Orchestrator |

**Validation Criteria**:
- [x] builds 0/0; FE 601 vitest green (FE-only — backend regression unchanged, CI-verified at S107); Step-7a dual-lens → the 2 actionable items fixed; INDEX/ROADMAP/QUALITY updated; commit + push + CI-verify; the S109 items carried forward.

---

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules / payroll | N/A | FE wiring of existing structure mutations; no backend/schema change. |

## External Review (Step 7a)
Dual-lens on the full uncommitted S108 diff (FE-only; the capability-gating + wire-before-render + the S107 inversion). Artifacts: `.claude/reviews/SPRINT-108-step7a-{codex,reviewer}.md`.

**Reviewer (internal) — 0 BLOCKER, 1 WARNING (FIXED), 4 NOTE:** verified clean (wire-before-render exact vs S104/S98; the cascade-delete + move-rank + 2-branch-delete correct; the inversion + people-guard real; the `OrganisationPage` port faithful; the gates mirror the live ROLE floors — `CapabilityMatrix` keystone). WARNING: `submitEdit` partial-success retry-poison (a rename-commit-then-leader-fail hid the commit + a retry 412'd) → **FIXED** (leaders first [no version bump], rename last, refetch on partial-commit). NOTEs: the stale "read-only" header → FIXED; the role-vs-scope MAO gate → deferred (the Codex WARNING); `orgMoveTargets`-from-filtered-forest + the pre-existing S98 MAO-delete-vs-child-orgs → accepted.

**Codex (external) — 0 BLOCKER, 1 WARNING (DEFERRED → S109):** the MAO-level org-create + MAO-rename are role-gated (LocalAdmin) but the backend scope-checks the parent MAO → a scoped LocalAdmin 403s (an over-shown button on the read-only-context MAO node). **Deferred to S109 with rationale:** low exposure (forest scope-filtered) + the backend-aware Reviewer rated it a NOTE + gating to GlobalAdmin embeds a scope-model product decision the S109 cutover (which consolidates org-admin gating + retires the GlobalAdmin-only `OrganisationPage`) should make. Not a security issue (the backend enforces).

[[review-lens-complementarity]]: Codex caught the role-vs-scope gate; the Reviewer caught the partial-success trap — disjoint, both real.

## Test Summary
**Pyramid: 852u + 1149r + 6s + 29demoseed + 601fe = 2637 — VERIFIED locally (FE) + backend carried.** FE 601 vitest (49 files; +32 vs S107's 569: UnitDrawer 12 + OrgStructureMutations 13 + CapabilityMatrix 4 + page/inversion net 3). Backend UNCHANGED (S108 is FE-only — `dotnet build` 0/0; no `src/`/`init.sql`/`tools` change → the S107 CI-green regression 1149 + smoke + e2e carry; CI re-verifies). Unit 852 (unchanged).

## Sprint Retrospective
- **The structure is now editable — the S91 dead-button discipline IN REVERSE.** S107 withheld the structure-mutation affordances (asserted absent); S108 wired them (the Unit drawer → S104; the org/MAO mutations → S98/S99) and the S107 vitests INVERTED, while the StrukturPanel allowlist was RE-ARCHITECTED so the people-mutation guard survives (people editing is S109). The `CapabilityMatrix` keystone pins the per-role affordance set.
- **The corrected unit DELETE is the headline Step-0b catch:** both lenses caught that S104's `DELETE /units/{id}` CASCADES (re-parent/re-home up), not 422-guards — the plan would have built a dead blocked-branch + failed to warn of the destructive cascade. The FE is now confirm-and-cascade.
- **The `useAuth` test-harness gap (Step-0b Reviewer):** adding the role-gate made the page import `useAuth` (throws without a provider) → the S107 suites would have thrown + the inversion would false-green under no-role. The parametrized role mock + the allowlist re-architecture closed it.
- **Step-7a:** the partial-success retry-412 trap (fixed) + the role-vs-scope MAO gate (deferred to S109 with rationale — the two lenses genuinely diverged on severity; deferring the scope-model product decision to the cutover is the honest call).
- FE-only → fast local verify (no 1h regression; the backend is unchanged). Durable: SPRINT-108.md + the Step-7a artifacts + FRONTEND.md. **NEXT = S109 (Phase 3b-2b: the PEOPLE-editing half + CUTOVER — the Person drawer [unit-assign with the two-endpoint Organisation-change routing, reporting/approver, apex/promote, vikar-edit] + cross-unit "Ret" [single→one-click via etag / multiple→picker] + leaderless "Tildel leder" + the CUTOVER [redirect+retire `/admin/ledelseslinjer`+`/global/organisation`, one sidebar entry]; AND resolve the deferred MAO role-vs-scope gate). Then Phase 4 final cleanup.**
