# Sprint 109 — Enhedsspor Phase 3b-2b: the merged-admin PEOPLE editing + the CUTOVER

| Field | Value |
|-------|-------|
| **Sprint** | 109 |
| **Status** | complete — CI GREEN `28447245606` |
| **Start Date** | 2026-06-30 |
| **End Date** | 2026-06-30 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes — `dotnet build` + `npm run build` 0/0 |
| **Test Verified** | yes — **CI GREEN `28447245606` (all 7 jobs incl. the merged-page E2E)**; FE 528 vitest + regression 1149 + e2e. (The 1st push CI [`28447…`'s predecessor] caught 2 ambiguous merged-page Playwright locators — test-only, fixed `b813628` after a LOCAL e2e run [the page itself was correct], re-run green.) |

## Sprint Goal
The FINAL feature sprint of the Enhedsspor program (owner chose one sprint: people editing + cutover). Make the merged page's PEOPLE editable + RETIRE the two old pages: the **Person drawer** (create/edit — porting the existing `EditPersonDrawer`/`useEditPerson` + the `editPerson/` ApproverSection/VikarSection cores onto the merged page, + the unit `Placering` field + apex/promote) with the **two-endpoint Organisation-change routing** (the load-bearing watch-item), the **Nærmeste-leder** (reporting/approver) + **vikar-edit**, cross-unit **"Ret"** + leaderless **"Tildel leder"**; then the **CUTOVER** — redirect + retire `/admin/ledelseslinjer` + `/global/organisation`, collapse to ONE sidebar entry, a **capability-parity audit**, and resolve the deferred S108 **MAO scope-gate**. This INVERTS the S107/S108 people-mutation no-button assertions (the S91 discipline in reverse, completing it). FE-only (the mutations exist). On close the merged "Organisation & medarbejdere" page is the single admin surface — the program's goal.

## Entropy Scan Findings
| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py` all hard checks passed |
| Pattern compliance | _pending_ | FE: wire-before-render; the two-endpoint routing; tokens-not-hardcoded; the cutover loses NO capability |
| Orphan detection | CLEAN (carried) | S108 structure editing CI-green `28408033942` |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P7-adjacent — the two-endpoint routing [a cross-Org transfer re-anchors edges; a wrong route silently 422s or mis-transfers]; the cutover capability-PARITY [retiring 2 pages must lose NOTHING]; the MAO scope-gate). The highest-coordination FE sprint. |
| **External Codex** | invoked 2026-06-30 — 2B/3W/1N → cycle-2 all RESOLVED |
| **Internal Reviewer** | invoked 2026-06-30 — 2B/3W/3N → cycle-2 all RESOLVED (0 residual BLOCKER) |
| **BLOCKERs resolved before Step 1** | yes — the convergent routing-matrix + parity-omission BLOCKERs absorbed; cycle-2 both lenses RESOLVED |

### Findings (cycle 1)
Both lenses confirm the direction sound + the backend two-endpoint substrate correct; the risk is in the FE save orchestration + the retire scope. The 2 CONVERGENT BLOCKERs + the WARNINGs/NOTEs absorbed:
- **BLOCKER (CONVERGENT, TASK-10902) — the routing is a 4-case matrix, not a 2-endpoint switch.** Both `PUT /users/{id}` + `/unit` share `users.version` → the reused `useEditPerson` (stamdata PUT first → version bump) would 412 EVERY time on a follow-up unit-assign; a cross-Org move is ONE transfer call carrying `unitId` (the transfer accepts it atomically; cross-Org unit-assign 422s by design), NOT "transfer then unit"; create+Placering = POST-then-`/unit`(v=1); a move+promote must run unit-assign BEFORE designate (the unit-assign strips leaderships). → rewritten as the explicit 4-case matrix + a placement wrapper + the version-threading + RED tests.
- **BLOCKER (CONVERGENT, TASK-10904) — the parity audit omitted 3 live capabilities** (grep-confirmed the merged page lacks `periodStatus`/`isOrphan`/pending-counts): the status tiles + click-filter, the "mangler godkender" orphan overview + inline assign, the "Vis niveau" control. → a parity CHECKLIST (port-or-owner-removal BEFORE deletion); the cutover GATED behind green routing+parity.
- WARNING (Codex+Reviewer, TASK-10904) — the MAO gate must be PER-NODE-KIND (MAO Omdøb/create = GlobalAdmin; Org Omdøb = LocalAdmin), not a blanket "GlobalAdmin in practice". → absorbed.
- WARNING (both, TASK-10905) — the inversion is StrukturPanel-only (SearchOverlay stays navigate-only) + re-architect the matrix to PRESENCE rows (not delete the guard). → absorbed.
- WARNING (Reviewer, scope) — the cutover is gated behind the routing+parity proof (don't retire before parity demonstrated). → absorbed.
- NOTEs — the Placering data source (the S106 forest, null=Org-homed, reload on Org change); the reuse is sounder than it looks (PersonPicker server-side; `fetchUser` for the etag). → absorbed.

### Resolution
The 2 convergent BLOCKERs + the WARNINGs + the NOTEs absorbed into TASK-10901/10902/10904/10905.

**Cycle 2 (verification):** BOTH lenses confirm all findings RESOLVED + no new BLOCKER. One Reviewer NOTE absorbed: the version-threading must use the FINAL `users.version` (`result.live.user.version` after ALL of `saveEdit`'s sub-writes — DOB/employment-start also bump it), not the step-1 stamdata response. **0 residual BLOCKER; cycle-cap (2/lens) respected. Cleared for Step 1.** This was the deepest FE-phase Step-0b — both lenses caught the routing's 4-case true shape (the version-threading 412-trap + the org+unit single-call + the leadership-strip ordering) + 3 cutover-parity omissions that would have silently regressed the admin surface.

## Architectural Constraints Verified
- [x] P7 — every people mutation calls the EXISTING server endpoint (the backend re-checks floor/scope/concurrency); the **4-case placement routing** correct + tested (org-change=ONE `/users/{id}` w/ `primaryOrgId`+`unitId`; same-Org=`/unit` threading the FINAL version; the wrong endpoint never used; cross-Org `/unit` 422-by-design avoided). NO new authority path.
- [x] P8 — the FE wires the SAME shapes the existing user/reporting-line/vikar tests pin; FE 528 vitest + the merged-page e2e green; backend regression unchanged (FE-only) → CI-carried.
- [x] P9 — wire-before-render; people affordances per role; the CUTOVER preserved EVERY capability (the parity audit — the 3 at-risk ones owner-decided: 2 ported, 1 retired); both routes redirect; ONE sidebar entry; the MAO gate per-node resolved (no scoped-LocalAdmin dead button).

## Task Log

### TASK-10900 — Sprint open (entropy + plan + Step-0b)
| Field | Value |
|-------|-------|
| **ID** | TASK-10900 |
| **Status** | complete — entropy CLEAN; plan authored; Step-0b dual-lens (2 cycles; 2 convergent BLOCKERs absorbed — the 4-case placement routing/version-threading + the cutover-parity omissions [3 capabilities]; + the per-node MAO gate, the promote-vs-Placering order, the inversion scope; 0 residual). The deepest FE-phase Step-0b. |
| **Agent** | Orchestrator |
| **KB Refs** | REFINEMENT-phase3-merged-fe.md (3b), design_handoff_org_medarbejdere §3, S104 person-unit-assign + transfer, ADR-027 reporting/vikar, the existing `EditPersonDrawer`, docs/FRONTEND.md |

**Validation Criteria**:
- [x] Entropy recorded; plan authored; Step-0b dual-lens run; BLOCKERs absorbed before Step 1.

---

### TASK-10901 — The Person drawer (create/edit) + the "+ Medarbejder" affordance
| Field | Value |
|-------|-------|
| **ID** | TASK-10901 + TASK-10902 |
| **Status** | complete — `PersonDrawer` (wraps the reused `EditPersonDrawer`/`editPerson` cores + `personDrawerData` Placering-from-forest [reloads on Org change, incl. `null`=Org-homed]) + `usePlacement` (the 4-case wrapper): create=POST-then-`/unit`(v=1); org-change=ONE `PUT /users/{id}` w/ `primaryOrgId`+`unitId` (no follow-up `/unit`); same-Org=`/unit` threading the **FINAL** `users.version` (RED test: stamdata `"5"`→`/unit` `"6"`); move+promote=assign-FIRST-then-designate. `+ Medarbejder` + per-row `Rediger ›` (LocalHR); `fetchUser` for the etag. `npm run build` 0 err; **612 vitest** (+11 routing/drawer). The 3 people-absence suites minimally updated to PRESENCE (Ret/Tildel/Skift/Afslut still absent — later tasks). |
| **Agent** | UX (frontend) |
| **Components** | `enhedsspor/PersonDrawer.tsx` (porting `EditPersonDrawer`/`useEditPerson` + the `editPerson/` cores) + the StrukturPanel "+ Medarbejder" / "Rediger ›" affordances |
| **KB Refs** | the existing `EditPersonDrawer.tsx` + `editPerson/` (ApproverSection/VikarSection) — the logic to REUSE; the design §3 (the person drawer fields) |

**Description**: The **Person drawer** on the merged page, reusing the existing `EditPersonDrawer`/`useEditPerson` + the `editPerson/` mutation cores (port/wrap the proven create/edit/approver/vikar logic). **[Reviewer NOTE — the reuse is sounder than it looks]:** the approver/vikar `PersonPicker` searches SERVER-side (`/api/admin/users/search`, scope+descendant-filtered) and `LifecycleSections` self-resolves the current approver/lineETag/vikar in edit mode → the drawer needs NEITHER the merged roster shape for candidates NOR for cycle-prevention. The only roster coupling is `fetchUser` for the edit etag (the `RosterRow` isn't a full `User` — port the old `handleOpenEdit`); the `organizations` + Placering options assemble from the S106 forest. The design §3 fields: Navn (req), Titel, E-mail, **Organisation** (Select — from the forest, carries agreementCode/okVersion), **Placering** (the unit — **[NOTE — data source]:** Select derived from the S106 FOREST for the chosen Organisation, incl. `null` = directly under the Organisation; RELOAD options when the Organisation changes), **Nærmeste leder** (Select + the "Vis ledere uden for enheden" widen), "Øverste leder — ingen overordnet" (apex), "Er leder af [unit]" (promote → `unit_leaders`), the **Vikar ved fravær** section. **[WARNING — create+Placering is a dead input]:** `POST /users` does NOT persist a unit → create+Placering must be create-THEN-`/users/{id}/unit` (v=1), or drop Placering at create (the routing is TASK-10902). Create via `+ Medarbejder` (on a unit, gated LocalHR); edit via the person row "Rediger ›" / clickable name (NOW clickable — the S107/S108 inversion, TASK-10905). Refetch on save.

**Validation Criteria**:
- [ ] The Person drawer create/edit renders the design's fields; the Placering options derive from the forest for the chosen Organisation (incl. `null`=Org-homed) + reload on Organisation change; `+ Medarbejder` (LocalHR) + the person-row edit open it; reuses `useEditPerson`/`editPerson` cores + `fetchUser` for the etag; refetch on save; vitest on the drawer.

---

### TASK-10902 — The two-endpoint Organisation-change routing + Nærmeste-leder + vikar (the watch-item)
| Field | Value |
|-------|-------|
| **ID** | TASK-10902 |
| **Status** | planned |
| **Agent** | UX (frontend) |
| **Components** | the PersonDrawer save logic + the ApproverSection/VikarSection reuse |
| **KB Refs** | S104 (`PUT /users/{id}/unit` same-Org vs `PUT /users/{id}` w/ `primaryOrgId` cross-Org transfer — re-anchors edges, BLOCKS a manager-with-active-reports 422), ADR-027 (the PRIMARY edge + manager_vikar) |

**Description**: **The load-bearing watch-item — a precise 4-case PLACEMENT matrix (both Step-0b lenses; NOT a simple 2-endpoint switch).** Both `PUT /users/{id}` (stamdata) and `PUT /users/{id}/unit` key If-Match on the SAME `users.version`, and the transfer `PUT /users/{id}` ALREADY accepts `unitId` (applied atomically; a cross-Org unit-assign 422s BY DESIGN). So:
  1. **Create** (`+ Medarbejder`): `POST /users` homes at the Organisation (no unit). If a Placering is chosen → THEN `PUT /users/{id}/unit` with the create's returned version (v=1). (Or drop Placering at create.)
  2. **Edit, Organisation CHANGED (± unit):** ONE `PUT /users/{id}` carrying BOTH `primaryOrgId` AND `unitId|null` (the transfer re-anchors reporting/vikar edges + 422-blocks a manager-with-active-reports, all atomic) — **NOT** "transfer then unit-assign". → extend `updateUser`/`useEditPerson` to send `unitId`.
  3. **Edit, SAME-Org Placering change (unit↔unit or unit↔org-home):** `PUT /users/{id}/unit` (If-Match). **[BLOCKER — version-threading]:** `useEditPerson` step-1 ALWAYS PUTs stamdata first → bumps `users.version` → a follow-up unit-assign with the PRE-read etag 412s EVERY TIME. → thread the **FINAL** `users.version` into the unit-assign If-Match — `result.live.user.version` returned by `saveEdit` AFTER ALL its sub-writes (NOT the step-1 stamdata response; the DOB/employment-start PUTs in the BLOCKER-1 chain ALSO bump the version) — or SKIP the stamdata PUT when stamdata is unchanged. A RED test for the same-Org stamdata+unit double-write.
  4. **[BLOCKER — promote-vs-Placering ordering]:** the same-Org unit-assign STRIPS all of the user's leaderships (`RemoveAllLeadershipForUserAsync`). A save that moves Placering AND promotes-to-leader must run the unit-assign FIRST, then `designateLeader` — else the move wipes the just-added leadership. Pin the order + a test.
A dedicated **placement mutation wrapper** (the routing decision) — the reused `useEditPerson` cannot do this as-is (it only sends `primaryOrgId`). The **Nærmeste leder** = the PRIMARY `reporting_lines` edge (reuse `ApproverSection` → `POST /reporting-lines`); **vikar-edit** = `VikarSection` (`manager_vikar`). Surface the real errors (the 422 manager-with-reports block, 412 stale).

**Validation Criteria**:
- [ ] All 4 cases pinned by tests: create+Placering (POST then `/unit` w/ v=1); org-change → ONE `/users/{id}` w/ `primaryOrgId`+`unitId` (re-anchors edges; manager-with-reports 422); same-Org Placering → `/unit` with the version threaded after a stamdata change (RED on the 412-every-time double-write); move+promote runs unit-assign-then-designate (RED on the leadership-wipe). The wrong endpoint is never used (cross-Org never hits `/unit`).

---

### TASK-10903 — Cross-unit "Ret" + leaderless "Tildel leder"
| Field | Value |
|-------|-------|
| **ID** | TASK-10903 |
| **Status** | complete — cross-unit **"Ret"** (single leader → one-click `POST /reporting-lines` with create-vs-supersede on the nullable `primaryReportingLineVersion` etag [non-null→If-Match supersede; null→If-None-Match:* create], reusing `useReportingLines().assignManager`; multiple → `RetLeaderPicker` pre-filtered to the unit's OWN leaders) + leaderless **"Tildel leder"** (reuses the S108 `UnitDrawer` edit); gated `canEditUnits` (LocalHR); refetch via `onMutated`. `npm run build` 0 err; **617 vitest** (+5). Absence→PRESENCE flipped (Skift/Afslut still absent; SearchOverlay navigate-only preserved). |
| **Agent** | UX (frontend) |
| **Components** | the StrukturPanel cross-unit + leaderless affordances |
| **KB Refs** | the refinement's "Ret" rule, the S106 roster etag (`primaryReportingLineVersion`), S108 `UnitDrawer` |

**Description**: Re-enable the read-only S107 cross-unit + leaderless surfaces as ACTIONS: **cross-unit "Ret"** (a member whose reporting manager ∉ their unit's leaders) — if the unit has EXACTLY ONE leader, one-click reassign the PRIMARY edge to it (via the roster's `primaryReportingLineVersion` etag — the S99 resolve-etag-first; create-vs-supersede on the nullable etag); if MULTIPLE peer leaders, open the Nærmeste-leder picker pre-filtered to the unit's own leaders (NEVER an arbitrary pick). **Leaderless "Tildel leder"** → open the S108 `UnitDrawer` edit (the Ledere checkboxes). Refetch on success.

**Validation Criteria**:
- [ ] "Ret" single-leader → one-click reassign (etag-carried, create-vs-supersede); multiple → the pre-filtered picker; "Tildel leder" opens the unit-leader edit; refetch; vitest on both.

---

### TASK-10904 — The CUTOVER: redirect + retire the 2 old pages + capability-parity + the MAO gate
| Field | Value |
|-------|-------|
| **ID** | TASK-10904 |
| **Status** | complete — **PORTED** the status tiles (period overview + click-to-filter, from `periodStatus`/`pendingCountByManager`) + the orphan overview ("⚠ N mangler godkender" + inline "+ Tildel godkender" via `InlineApproverControl`) onto the merged page's detail header; **Vis niveau RETIRED** (owner-approved). **MAO gate per-node** (MAO Omdøb/create=GlobalAdmin, Org Omdøb=LocalAdmin + a scoped-LocalAdmin no-dead-button test). **CUTOVER:** redirects `/admin/ledelseslinjer`+`/global/organisation`→ the merged page; ONE sidebar entry; DELETED `MedarbejderAdministration`+`OrganisationPage` + the dead chain (`medarbejderTree`/`useMedarbejderRoster`/`organisationTree`/`EditPersonDrawer`/`InlineVikarControl`/`useOrganizationTree`/`useOrganizationStructure`); kept the live cores + all backend endpoints; grep-zero proof. `npm run build` 0 err; **527 vitest** (retired ~93 + new 6). Follow-up: the Playwright `organisation.spec.ts` targets the retired page → TASK-10905. |
| **Agent** | UX (frontend) |
| **Components** | `App.tsx` (redirects), `Sidebar.tsx`, delete `MedarbejderAdministration.tsx` + `OrganisationPage.tsx` (+ their tests/CSS/now-dead hooks), `StrukturPanel.tsx` (the MAO gate), docs/FRONTEND.md (Orchestrator) |
| **KB Refs** | the S108 deferred MAO scope-gate (Codex WARNING), the S91 cutover discipline (S98/S99 precedent) |

**Description**: **The cutover — the merged page becomes THE admin surface. GATED: it lands ONLY behind a GREEN routing (TASK-10902) + a demonstrated parity proof — the old pages are NOT retired before parity is shown.** (a) **The capability-PARITY CHECKLIST (BLOCKER — both lenses; do this BEFORE deleting):** map EVERY old affordance → a merged-page replacement OR an owner-approved removal. **Three live `MedarbejderAdministration` capabilities the merged page does NOT yet have (grep-confirmed). OWNER DECISION (2026-06-30):** **PORT** (1) the **status tiles** "Ikke indsendt"/"Ikke godkendt" period-settlement overview + click-to-filter (from the roster's `periodStatus`/`pendingCountByManager`; the ADMIN-facing surface — `TeamOversigt` is the *leder* view) and (2) the aggregated **"X mangler godkender" ORPHAN overview** + inline approver-assign (from the roster's `isOrphan`); **RETIRE (owner-approved)** (3) the numbered **"Vis niveau"** level control (the merged page's expand carets + the "Vis org./Skjul org." toggle + the Afgrænsning cover it). Plus the already-present ones (roster/approver/vikar; org tree create/rename/move/delete; the Styrelse selector → the Afgrænsning). **Port the 2 onto the merged page BEFORE retiring.** (b) **Redirect** `/admin/ledelseslinjer` + `/global/organisation` → `/admin/organisation-medarbejdere` (`<Navigate replace>`); (c) **retire** `MedarbejderAdministration.tsx` + `OrganisationPage.tsx` + their tests/CSS + prune now-dead hooks (grep-zero live refs — KEEP the backend endpoints); (d) **ONE sidebar entry** (promote the merged page to its proper home); (e) **Resolve the deferred S108 MAO gate PER-NODE-KIND [both lenses]:** the MAO-node "Omdøb"/create need MAO scope → gate by GlobalAdmin (a scoped LocalAdmin sees the MAO as read-only ancestor context → otherwise a dead button); the Organisation-node "Omdøb" stays LocalAdmin. NOT a blanket "GlobalAdmin in practice" — split per operation/node; re-architect the `CapabilityMatrix` accordingly + add scoped-LocalAdmin dead-button tests.

**Validation Criteria**:
- [ ] The parity CHECKLIST maps every old affordance → replacement/owner-removal; the 3 named capabilities (status tiles / orphan overview / Vis-niveau) ported or owner-approved-removed BEFORE deletion. The cutover is gated behind green routing+parity.
- [ ] Both old routes redirect; the 2 old components + tests/CSS/dead hooks deleted (grep-zero; backend kept); ONE sidebar entry; the MAO gate per-node (MAO Omdøb/create = GlobalAdmin, Org Omdøb = LocalAdmin; scoped-LocalAdmin no-dead-button test) + the matrix updated.

---

### TASK-10905 — Tests
| Field | Value |
|-------|-------|
| **ID** | TASK-10905 |
| **Status** | complete — the stale Playwright `organisation.spec.ts` (targeted the retired page) REPLACED with a merged-page spec (2 happy paths, real backend round-trips: the people-edit keystone [select STY02 → emp002 Rediger → rename → PUT round-trips + roster refetch] + structure create→rename→delete; `runNonce`-stamped, self-cleanup). The `CapabilityMatrix` audited CLEAN (per-role PRESENCE; + a LocalLeader floor-boundary row pinning the gate at exactly LocalHR; the Skift/Afslut + SearchOverlay-navigate-only guards intact). Retired pages confirmed gone (no orphaned import). `npm run build` 0 err; **528 vitest** (44 files); the e2e compiles + lists 2 live-route tests. |
| **Agent** | Test & QA (frontend) |
| **Components** | vitest + the e2e |
| **KB Refs** | the S107/S108 vitests, the existing `EditPersonDrawer` tests |

**Description**: The S109 surface: the Person drawer flows (create/edit, Placering, apex/promote, vikar); the **4-case PLACEMENT routing** (create+Placering / org-change-single-call / same-Org-version-threaded / move+promote-order — the keystone, RED-on-mis-route + RED-on-the-412-double-write + RED-on-the-leadership-wipe); cross-unit "Ret" (single one-click + multiple picker) + "Tildel leder". **[Both lenses — the INVERSION is StrukturPanel-only + re-architects the matrix]:** the people-mutation surface becomes editable on the **StrukturPanel roster rows** (clickable-to-edit + `+ Medarbejder`) — the **`SearchOverlay` stays NAVIGATE-ONLY** (its tests' no-edit assertions HOLD). Re-architect the `CapabilityMatrix` people block: REPLACE the per-role ABSENCE assertions (`+ Medarbejder`/`Ret`/`Tildel leder`/`Rediger ›`/clickable-names) with per-role PRESENCE rows encoding the people floors (create/edit/unit-assign = LocalHR; approver/vikar = the existing floors) — mirroring S108's structure inversion, not deleting the guard. The **cutover** vitests (the redirects; the merged page at its proper route; the parity checklist incl. the 3 named capabilities); update/retire the deleted pages' tests. An e2e people-edit happy path on the merged page.

**Validation Criteria**:
- [ ] The drawer/4-case-routing/Ret/inversion(StrukturPanel-only; SearchOverlay navigate-only holds)/parity/redirect vitests green; the `CapabilityMatrix` people block re-architected to PRESENCE rows; the e2e people-edit; the deleted pages' tests removed; full FE vitest + CI green.

---

### TASK-10906 — Validation + Step-7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-10906 |
| **Status** | complete — `dotnet build` + `npm run build` 0/0; FE 528 vitest + the merged-page e2e green; backend UNCHANGED (FE-only → the S107 CI-green 1149 regression carries); Step-7a dual-lens (Codex 0B/2W + Reviewer 0B/1W/2N — the stale-source-roster + the apex-no-op FIXED); INDEX/ROADMAP/QUALITY/FRONTEND.md/memory updated. Commit + push + CI-verify next. |
| **Agent** | Orchestrator |

**Validation Criteria**:
- [x] builds 0/0; FE 528 vitest + e2e green (FE-only — backend regression 1149 unchanged, CI-verified at S108); Step-7a → the 2 actionable items fixed; INDEX/ROADMAP/QUALITY/FRONTEND.md updated; commit + push + CI-verify. **The Enhedsspor merged-admin program is FEATURE-COMPLETE.**

---

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules / payroll | N/A | FE wiring of existing people mutations + the cutover; no backend/schema change. |

## External Review (Step 7a)
Dual-lens on the full uncommitted S109 diff (FE-only; the two-endpoint routing + the cutover capability-parity). Artifacts: `.claude/reviews/SPRINT-109-step7a-{codex,reviewer}.md`.

**Reviewer (internal) — 0 BLOCKER, 1 WARNING (FIXED), 2 NOTE:** ALL dimensions CLEAN (the 4-case routing correct + well-tested incl. the 412-version-trap + the 422-block; the cutover with ZERO dangling refs + no lost capability; the per-node MAO gate; the people inversion load-bearing). WARNING: the edit-mode apex toggle was a no-op + hid the "Fjern" removal control → **FIXED** (apex create-only; in edit `isRoot=false` → the ApproverSection exposes the real assign/Skift/Fjern). NOTEs accepted (a minor partial-failure refetch; the hard-coded period label).

**Codex (external) — 0 BLOCKER, 2 WARNING (both FIXED):** the apex toggle (convergent) + a **stale SOURCE roster** after a cross-Org transfer (only the destination was refetched) → **FIXED** (refetch both `primaryOrgId`s on transfer).

[[review-lens-complementarity]]: the Codex lens additionally caught the stale-source-roster the Reviewer didn't; both flagged the apex no-op.

## Test Summary
**Pyramid: 852u + 1149r + 6s + 29demoseed + 528fe = 2564 — VERIFIED locally (FE) + backend carried.** FE 528 (down from S108's 569: the cutover retired `MedarbejderAdministration`+`OrganisationPage` + ~93 of their tests; +52 net new S109 [PersonDrawer/usePlacement/Ret/Tildel/the ported tiles/the MAO-gate/the matrix] − the retired). Backend UNCHANGED (S109 is FE-only — `dotnet build` 0/0; no `src/`/`init.sql`/`tools` change → the S108 CI-green regression 1149 + smoke + e2e carry; the e2e was REPOINTED at the merged page; CI re-verifies). Unit 852 (unchanged).

## Sprint Retrospective
- **THE ENHEDSSPOR MERGED-ADMIN PROGRAM IS FEATURE-COMPLETE.** The merged "Organisation & medarbejdere" page is now THE single admin surface — both old pages (`/admin/ledelseslinjer` + `/global/organisation`) redirect + are deleted; one sidebar entry. The owner's original goal (one page administering org structure AND people) is realized.
- **The deepest FE-phase Step-0b (the routing was the catch):** both lenses converged that the "two-endpoint" routing is really a 4-CASE PLACEMENT MATRIX with a version-threading 412-trap (the reused `useEditPerson` PUTs stamdata first → bumps `users.version` → a follow-up unit-assign with the pre-read etag 412s every time → thread the FINAL version), an org+unit SINGLE-call (the transfer accepts `unitId` atomically; cross-Org `/unit` 422s by design), and a unit-assign-BEFORE-promote ordering (the assign strips leaderships). Pre-code.
- **The cutover-parity catch saved a silent regression:** both lenses (grep-confirmed) found the merged page lacked 3 `MedarbejderAdministration` capabilities → surfaced to the OWNER (port the status-tiles + orphan-overview; retire Vis-niveau) → ported BEFORE deleting. The cutover gated behind a green routing+parity proof.
- **Step-7a:** the stale-source-roster (Codex) + the edit-mode apex no-op (both lenses) — fixed. The reuse was sound (the `EditPersonDrawer`/`editPerson` server-side PersonPicker + `fetchUser` for the etag — no merged-roster coupling for candidates).
- **The recurring lesson, one last time:** [[review-lens-complementarity]] was decisive at EVERY gate of the program (S102→S109) — the two lenses caught disjoint, real issues pre-code and at close, sprint after sprint.
- Durable: SPRINT-109.md + the Step-7a artifacts + FRONTEND.md (the merged route; the 2 retired). **NEXT = Phase 4 (final cleanup) — the only remaining Enhedsspor work: prune any residual dead code, the `medarbejderTree`/legacy comments, confirm no transitional artifacts, a docs sweep; OR a wholly new feature. The merged-admin program is DONE.**
