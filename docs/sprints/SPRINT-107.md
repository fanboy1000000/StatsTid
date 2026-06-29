# Sprint 107 — Enhedsspor Phase 3b-1: the merged-admin page (VIEW half)

| Field | Value |
|-------|-------|
| **Sprint** | 107 |
| **Status** | complete — CI GREEN `28397136776` |
| **Start Date** | 2026-06-29 |
| **End Date** | 2026-06-29 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes — `dotnet build` + `npm run build` 0/0 |
| **Test Verified** | yes — **CI GREEN `28397136776` (all 7 jobs; regression 1149/1149, 57m; frontend-build + e2e + smoke green)**; matched the local run exactly |

## Sprint Goal
Build the merged "Organisation & medarbejdere" admin page (`design_handoff_org_medarbejdere` "Model A") — the **VIEW/navigate half** (owner-chosen view-first split): the 3-region layout (Afgrænsning scope header + left org-structure tree + the recursive right "Struktur") that RENDERS the unit structure + people from the S106 reads, plus search. **NO mutations / NO dead buttons** (the S91 discipline — drawers, create/edit/delete, cross-unit "Ret", vikar-edit are S108). The new page ships on a **new route, ALONGSIDE the two old pages** (the redirect + retire is the S108 cutover). Capability context is read at LocalHR floor; the page is scope-bounded via Afgrænsning. Consumes the S106 forest/roster/search reads; registers them in the contract-lint + adds FE hook tests on the real shape (the 3a carry-over). **FE + ONE small backend read-field** (`organisationId` on the search person result, for the Afgrænsning filter) + the lint registry — no schema/authority/mutation change.

## Entropy Scan Findings
| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `check_docs.py` all hard checks passed |
| Pattern compliance | CLEAN | FE: no dead buttons (S91); tokens-not-hardcoded (FRONTEND.md); the S106 reads have NO FE consumer yet (expected — S107 wires them) |
| Orphan detection | CLEAN (carried) | S106 reads CI-green `28372165094` |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P9 usability + P7-adjacent — the page must SHOW only what the actor may see, mirroring the read scope; the contract-lint registration closes the S106→FE shape-drift gap [PAT-010]). FE-only lowers the risk vs the authority sprints. |
| **External Codex** | invoked 2026-06-29 — 2B/2W/1N → cycle-2 all RESOLVED |
| **Internal Reviewer** | invoked 2026-06-29 — 1B/1W/3N → cycle-2 all RESOLVED (0 residual BLOCKER) |
| **BLOCKERs resolved before Step 1** | yes — absorbed (the search `organisationId`, the lint Contracts/+REGISTRY mechanics, the Afgrænsning option-source); cycle-2 verification run |

### Findings (cycle 1)
Both lenses confirm the view/edit split sound + the read-only scope cleanly drawn. BLOCKERs/WARNINGs absorbed (plan-text fixes, no architecture change):
- **BLOCKER (Codex, TASK-10704) — the search shape can't Afgrænsning-filter people:** `PersonSearchResult` has no `organisationId` (only path TEXT). → add `organisationId` to the search person result (+ Contracts test); the FE filters by id, not fragile path-text. (S107 = FE + this ONE small read-field.)
- **BLOCKER (BOTH, TASK-10705) — the contract-lint liveness scans `Contracts/` only**, but the roster pin is in `Approval/S106RosterUnitTagTests` + the roster path is EXEMPT `pass-2`. → add a dedicated `Contracts/RosterEndpointContractTests.cs` + MOVE the roster EXEMPT→REGISTRY (forest+search already have Contracts tests). + the hooks must use INLINE URLs (the lint blind spot).
- WARNING (Codex, TASK-10703) — the no-dead-button vitest asserts the ALLOWED surface (allowlist), not just a denylist. → absorbed (+ exhaustive: `+ Medarbejder`/`Tildel leder`/person-name links/`Skift`/`Afslut`/drawer mounts; search-result navigates ONLY).
- WARNING (Codex, TASK-10704) — Afgrænsning filtered counts RECOMPUTE from the selected set. → absorbed.
- WARNING (Reviewer, TASK-10704) — the Afgrænsning OPTION SOURCE = the scoped forest (not an unbounded org list — a scoped HR must not see out-of-scope orgs in the popover). → absorbed.
- NOTEs — the temporary LocalHR sidebar entry (reachability); the inline-URL requirement; the exhaustive denylist. → absorbed.

### Resolution
The 3 BLOCKERs + the WARNINGs + the substantive NOTEs absorbed into TASK-10701/10703/10704/10705.

**Cycle 2 (verification):** BOTH lenses confirm all cycle-1 findings RESOLVED, 0 residual BLOCKER. Codex: 4/4 resolved, no new blocker. Reviewer: 5/5 resolved + the chosen roster-lint fix (a `Contracts/` test + EXEMPT→REGISTRY, no lint-logic change) is sound + does NOT weaken the gate — plus ONE process WARNING (the `organisationId` add is a BACKEND read-shape change but was under a "UX (frontend)" task / "FE-only" goal → agent-scope mismatch). **Absorbed:** the goal softened to "FE + one small backend read-field"; the `organisationId` slice (Contracts + the Infrastructure SELECT + the C# contract test) reassigned to Backend/Infrastructure within TASK-10704 (the popover/overlay stays UX). Micro-check for Step 1 (Reviewer): the new REGISTRY row points at the method in the NEW `Contracts/` roster file, not the `Approval/` one.

**0 residual BLOCKER; cycle-cap (2/lens) respected. Cleared for Step 1.**

## Architectural Constraints Verified
- [x] P7/scope — the page renders ONLY what the scope-bounded S106 reads return; the Afgrænsning options come from the scoped forest (not `/organizations`) + counts recompute + the search filters by `organisationId` (both lenses CLEAN — no client-side widening).
- [x] P8/PAT-010 — forest/roster/search registered in `check_endpoint_contracts.py` REGISTRY (the roster via a dedicated `Contracts/` test + EXEMPT→REGISTRY); INLINE-URL hooks; FE hook tests on the REAL shape (`satisfies …Response`, null handling). `check_endpoint_contracts.py` exit 0.
- [x] P9 — no dead buttons (S91): NO mutation affordance renders (allowlist vitest in StrukturPanel + an exhaustive page-level denylist + no-`dialog`); tokens-not-hardcoded (`--unit-accent-*` == `typeMaps.ts` byte-for-byte); Danish copy verbatim.

## Task Log

### TASK-10700 — Sprint open (entropy + plan + Step-0b)
| Field | Value |
|-------|-------|
| **ID** | TASK-10700 |
| **Status** | complete — entropy CLEAN; plan authored; Step-0b dual-lens (2 cycles; 3 BLOCKERs absorbed — the search `organisationId`, the contract-lint Contracts/+REGISTRY mechanics; + the Afgrænsning option-source + the dead-button allowlist + the org-id ownership; 0 residual). |
| **Agent** | Orchestrator |
| **KB Refs** | REFINEMENT-phase3-merged-fe.md (3b), design_handoff_org_medarbejdere, docs/FRONTEND.md, PAT-010 |

**Validation Criteria**:
- [x] Entropy recorded; plan authored; Step-0b dual-lens run; BLOCKERs absorbed before Step 1.

---

### TASK-10701 — The page shell + 3-region layout + routing + design tokens
| Field | Value |
|-------|-------|
| **ID** | TASK-10701 |
| **Status** | complete — `OrganisationOgMedarbejdere.tsx` + `.module.css` (3-region layout, square corners, tokens-not-hardcoded; per-type `--unit-accent-*`/`--unit-tint-*` CSS vars for the subcomponents) + `enhedsspor/typeMaps.ts` (CHILD/ACCENT/TINT/ORD/LABEL ported verbatim) + a new LocalHR route `admin/organisation-medarbejdere` + a temp Sidebar entry (old routes/entries UNCHANGED). Afgrænsning/Søg = DISABLED placeholders (no dead buttons). `npm run build` 0 err; 4/4 vitest (incl. the no-mutation-affordance pin). (docs/FRONTEND.md route-table update = Orchestrator, at close.) |
| **Agent** | UX (frontend) |
| **Components** | `frontend/src/pages/admin/OrganisationOgMedarbejdere.tsx` (new) + `.module.css`, `frontend/src/App.tsx` (a NEW route, LocalHR floor — old routes UNCHANGED in S107), the design's `CHILD`/`ACCENT`/`TINT`/`ORD` maps as a TS module |
| **KB Refs** | docs/FRONTEND.md (UI Kit, CSS modules, tokens), the design handoff |

**Description**: The page scaffold on the StatsTid UI Kit: the 3-region layout (header with the title "Organisation & medarbejdere" + "Enhedsspor — organisationen er rygraden" + the Afgrænsning placeholder + the Søg button; the 330px left sidebar; the flex-1 right detail panel) — square corners, IBM Plex Sans, tokens-not-hardcoded (port the design's per-type ACCENT/TINT colors as token-referencing CSS). A NEW route under Administration (LocalHR floor) — the two old routes stay live (the cutover is S108). **[Both lenses — sidebar reachability]:** add a temporary LocalHR **sidebar entry** for "Organisation & medarbejdere" (so the view-half page is reachable for validation during the S107→S108 interim), leaving `/admin/ledelseslinjer` + `/global/organisation`'s entries unchanged until the S108 cutover collapses to one. Port the `CHILD`/`ACCENT`/`TINT`/`ORD`/`LABEL` domain maps verbatim (TS module). The page is a shell; the tree/Struktur/scope/search land in 10702-10704.

**Validation Criteria**:
- [ ] The page renders at its new route (LocalHR gate); the 3-region layout matches the design (tokens, square corners, copy); the old routes untouched; vitest renders the shell.

---

### TASK-10702 — The left org-structure tree (the forest read)
| Field | Value |
|-------|-------|
| **ID** | TASK-10702 |
| **Status** | complete — `useForest` hook (INLINE URL `/api/admin/units/forest`; named TS interfaces cross-checked vs `ForestContracts.cs`) + `OrgStructureTree` (recursive MAO→Org→units, per-type `--unit-accent-*` dots, `memberCount` pills, expand/collapse, selection lifts `SelectedNode`); read+navigate ONLY, no client-side scope logic. `npm run build` 0 err; **15/15 vitest** (9 tree + 2 hook + 4 shell). |
| **Agent** | UX (frontend) |
| **Components** | the page + a `useForest` hook (`GET /api/admin/units/forest`), the left-tree subcomponent |
| **KB Refs** | the S106 `ForestResponse` shape, the design's sidebar spec |

**Description**: The left "ORGANISATIONSSTRUKTUR" tree from the forest read: the indented expandable tree (MAO → Organisation → units), per-type colored dots, the deep member-count pill, selection (the green left-border + tint), 8+depth×15 indent. Units-only (no people in the sidebar). Scope-respecting (the forest is already scope-bounded server-side — the tree renders exactly what it returns; MAO ancestors are read-only context). Selecting a node drives the right panel (10703).

**Validation Criteria**:
- [ ] The tree renders the forest (nesting, dots, count pills, levels); selection updates the detail panel; it shows only what the scope-bounded forest returns; `useForest` has a hook test on the real shape (TASK-10705).

---

### TASK-10703 — The right recursive "Struktur" (forest + roster, read-only)
| Field | Value |
|-------|-------|
| **ID** | TASK-10703 |
| **Status** | complete — `useRoster` (INLINE URL, lazy+cached per Organisation) + `forestIndex` + `StrukturPanel` (recursive leaders→employees→child-units grouping; title-chip/breadcrumb/back-forward/"Refererer opad til" read-only chips; cross-unit/leaderless/vikar all READ-ONLY [no Ret/Tildel-leder/vikar-edit]; the 2 view toggles). **Keystone no-mutation ALLOWLIST vitest** (every button in the allowed set; exhaustive denylist absent; person names not click-to-edit). `npm run build` 0 err; **545 frontend vitest** (+13 struktur; +28 total S107). |
| **Agent** | UX (frontend) |
| **Components** | the detail panel + a `useRoster` hook (the unit-tagged per-Organisation roster, lazy per Organisation), the recursive Struktur subcomponent + the breadcrumb/back-forward nav |
| **KB Refs** | the S106 roster shape (`unitId`/`leaderIds`/`NameResolution`/`OutgoingVikar`), the design's Struktur spec |

**Description**: The right panel for the selected unit: the title block (type chip + name), the breadcrumb (path) + back/forward, the "Refererer opad til" strip (read-only chips via `NameResolution`), and the recursive **Struktur** — group the (lazy-loaded-per-Organisation) roster client-side: per unit, the MEDARBEJDERE header → each leader (LEDER badge, count) → their direct reports; **cross-unit exception** shown read-only (the amber "Leder uden for enheden" flag — NO "Ret" button yet, S108); **leaderless-unit** note read-only (NO "Tildel leder" button yet); **vikar** display (FRAVÆRENDE badge + "Vikar: …" line from `OutgoingVikar`; the inverse "Vikar for X" tag); child units expandable inline. The "Vis org./Skjul org." + "Vis/Skjul medarbejdere" toggles. **NO `+ Medarbejder` / `+ <ChildType>` / Rediger / Slet / Ret / drawer affordances** (S108 — dead-button discipline).

**Validation Criteria**:
- [ ] The Struktur renders leaders→employees→child units from forest+roster (grouped client-side, lazy per Organisation); cross-unit/leaderless/vikar shown READ-ONLY (no action buttons); the toggles work.
- [ ] **[Both lenses — the no-mutation vitest asserts the ALLOWED surface (allowlist), not just a denylist]:** the ONLY interactive affordances are expansion carets, the "Vis org./Skjul org." + "Vis/Skjul medarbejdere" view toggles, breadcrumb/back-forward, Afgrænsning, search + result-navigation. EXPLICITLY ABSENT: `+ Medarbejder`, `+ <ChildType>` (e.g. "+ Kontor"), title-block `Rediger`/`Slet`, per-row `Rediger ›` / person-name edit links, cross-unit `Ret`, leaderless `Tildel leder`, `Skift`/`Afslut`/`Omdøb`/`Flyt`/`Gem`, vikar-edit links, and ANY drawer mount (all S108).

---

### TASK-10704 — The Afgrænsning scope popover + the search overlay
| Field | Value |
|-------|-------|
| **ID** | TASK-10704 |
| **Status** | complete — **org-id slice (Backend):** `PersonSearchResult.organisationId` added (repo already SELECTed `primary_org_id`; net `SearchContracts`+`AdminEndpoints`+`SearchEndpointContractTests`; 4/4 Docker-green). **FE:** `useSearch` (INLINE URL, debounced) + `AfgraensningControl` (tri-state popover; OPTIONS from the scoped forest — out-of-scope orgs absent; count RECOMPUTE on filter) + `SearchOverlay` (two-section; **navigates-ONLY, no drawer**; `organisationId` Afgrænsning filter; `/`+Esc). `npm run build` 0 err; **567 frontend vitest** (+22). NB: `/forest` + `/search` now enumerated-but-uncovered → the lint is RED until TASK-10705. |
| **Agent** | UX (frontend) + Backend (org-id slice) |
| **Components** | the Afgrænsning popover + a `useSearch` hook (`GET /api/admin/search`), the search overlay (UX); **the `organisationId` slice** = `Contracts/SearchContracts.cs` `PersonSearchResult` + `ApprovalPeriodRepository.SearchPeopleForOverlayAsync` SELECT + `Contracts/SearchEndpointContractTests.cs` (Backend/Infrastructure) |
| **KB Refs** | the S106 search shape, the design's Afgrænsning + search specs |

**Description**: The **Afgrænsning** scope popover (ministerområde tri-state + organisations; "Vælg alle"/"Ryd"/"Anvend") — narrows the rendered forest/roster to the chosen Organisation set. **[Reviewer WARNING — the OPTION SOURCE]:** the popover's ministerområde/organisation options derive from the **already-scope-bounded forest read**, NOT `GET /api/admin/organizations`/`/tree` — else a scoped LocalHR would see org names OUTSIDE their accessible set in the popover (a P7-adjacent over-show). It is a pure narrowing filter over the server-admitted set (server scope = the MAX). **[Codex WARNING — the COUNTS]:** the filtered display counts (MAO/Organisation roll-ups) RECOMPUTE from the selected Organisation set — never show the unfiltered forest's totals for hidden orgs. The **search overlay** (Søg / `/` shortcut, Esc to close) consuming the search read: the two sections (ENHEDER + MEDARBEJDERE) with paths + count pills; selecting a result **navigates the tree/panel ONLY** (the design opens the edit drawer on click — S107 must NOT; dead-button discipline). **[Codex BLOCKER — the search shape]:** a faithful "Søgningen er begrænset til den valgte afgrænsning" needs the org id per person result — the S106 `PersonSearchResult` carries only path TEXT (no `organisationId`). → a **tiny S106-search-shape addition** in S107: add `organisationId` to `PersonSearchResult` (the `UnitSearchResult` already has it) + extend its Contracts test, so the FE filters by id (NOT fragile path-text). (S107 is thus FE + this ONE small read-field addition.)

**Validation Criteria**:
- [ ] Afgrænsning OPTIONS derive from the scoped forest (a scoped HR sees NO out-of-scope org in the popover); the filter narrows the view; the filtered counts recompute from the selected set; never beyond the actor's admitted set.
- [ ] `PersonSearchResult.organisationId` added + Contracts-tested; the search overlay renders the two-section results + navigates ONLY (no drawer-open); `useSearch` hook test on the real shape (inline URL — see TASK-10705).

---

### TASK-10705 — Contract-lint registration + FE hook tests (the 3a carry-over)
| Field | Value |
|-------|-------|
| **ID** | TASK-10705 |
| **Status** | complete — 3 REGISTRY entries (`/forest`→`ForestEndpointContractTests`, `/search`→`SearchEndpointContractTests`, the roster→the NEW `Contracts/RosterEndpointContractTests` [3/3 Docker-green]); the roster MOVED EXEMPT(pass-2)→REGISTRY (not double-listed); `useRoster` real-shape hook test added (`useForest`/`useSearch` already had them). `check_endpoint_contracts.py` **exit 0** (5 registered found in `Contracts/`; `--selftest` exits 1 = live); `dotnet build` 0 err; `npm run build` 0 err; **569 frontend vitest**. |
| **Agent** | Test & QA (frontend + backend contract test) |
| **Components** | `tools/check_endpoint_contracts.py` (registry), `frontend/src/**/__tests__` (hook tests), the page vitest |
| **KB Refs** | PAT-010, the S106 contract tests, the recurring S97→S99→S100 drift class |

**Description**: Close the S106 3a carry-over now that the forest/roster/search reads HAVE FE consumers. **[Both Step-0b lenses — BLOCKER: the lint liveness scans ONLY `tests/.../Contracts/`]:** the forest + search Contracts tests already live there (fine), but the **roster** contract pin is in `Approval/S106RosterUnitTagTests.cs` (PAT-010 Pass-2 co-located) — so naively registering the roster pointing there → the lint's `_contract_test_blob` (scans `CONTRACTS_DIR` only) MISSES it → exit 1 → the `docs-consistency` CI job RED. Fix: (a) add a dedicated **`Contracts/RosterEndpointContractTests.cs`** for `/api/admin/reporting-lines/tree/{}/medarbejdere` (the envelope + the unit-tag/`nameResolution` field-set); (b) **MOVE the roster path from EXEMPT (`pass-2`) → REGISTRY** (not just add); the forest + search get REGISTRY rows pointing at their existing Contracts tests. **[Reviewer NOTE — inline URLs]:** `useForest`/`useSearch` MUST pass the URL inline to `apiClient.get`/`apiFetchWithEtag` (NOT a path-helper const — the documented lint blind spot) else the lint can't enumerate them + the registration is inert (the roster hook is already inline). **FE hook tests** for `useForest`/`useRoster`/`useSearch` consuming the REAL serialized shape (a mock mirroring the backend's actual JSON — the S97/S99 fix: the FE mock must NOT diverge from the backend).

**Validation Criteria**:
- [ ] A `Contracts/RosterEndpointContractTests.cs` exists; the roster path MOVED EXEMPT→REGISTRY; forest + search registered; the lint's liveness finds ALL three (no uncovered admin GET); `check_endpoint_contracts.py --selftest` + the `docs-consistency` job green.
- [ ] `useForest`/`useSearch` use INLINE literal URLs (lint-enumerable); FE hook tests exercise the real shape.

---

### TASK-10706 — Validation + Step-7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-10706 |
| **Status** | complete — `dotnet build`+`npm run build` 0/0; pyramid 852u+1149r+569fe green LOCALLY (Docker; 3 settlement sheds isolation-cleared 31/31); Step-7a dual-lens (Codex 0B/1W + Reviewer 0B/1W/2N — both WARNINGs fixed); FRONTEND.md route table + INDEX/ROADMAP/QUALITY/memory updated. Commit (staged precisely — scaffolding excluded) + push + CI-verify next. |
| **Agent** | Orchestrator |

**Validation Criteria**:
- [x] builds 0/0; full pyramid green locally; Step-7a dual-lens → WARNINGs fixed; FRONTEND.md + INDEX/ROADMAP/QUALITY updated; commit + push + CI-verify; the S108 items carried forward.

---

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules / payroll | N/A | FE view-only + 1 additive read-field; no rule/payroll/authority change. |

## External Review (Step 7a)
Dual-lens on the full uncommitted S107 diff (FE-focused; the no-dead-button + scope-faithful render + the contract-lint closure). Artifacts: `.claude/reviews/SPRINT-107-step7a-{codex,reviewer}.md`.

**Reviewer (internal) — 0 BLOCKER, 1 WARNING (fixed), 2 NOTE:** all five dimensions CLEAN by construction (the S91 keystone — person/leader rows are non-clickable `<div>`s, no non-GET calls in the new FE surface; P7 scope-faithful — the Afgrænsning options from the scoped forest + count recompute + the `organisationId` search filter; the lint closure — all 3 methods found in `Contracts/`; the additive org-id field; byte-level FE↔wire fidelity). WARNING: the PAGE-level no-mutation vitest was a weak 5-label denylist (the StrukturPanel keystone test is already an allowlist) → **FIXED** (exhaustive denylist + no-`dialog`-role). NOTEs accepted: the MAO-overview-shows-no-people (read-only, → an S108 polish); the untracked-scaffolding housekeeping (excluded from the commit).

**Codex (external) — 0 BLOCKER, 1 WARNING (fixed):** the `useRoster` shared loading flag raced on fast cross-Organisation navigation (A completes → flashes B's empty-state) → **FIXED** (`setLoading(loadingRef.size > 0)` — an in-flight count). [[review-lens-complementarity]]: the internal lens caught the weak test guard; the external lens caught the loading race.

## Test Summary
**Pyramid: 852u + 1149r + 6s + 29demoseed + 569fe = 2605 — VERIFIED GREEN LOCALLY (Docker).** Regression 1146 passed + 3 Settlement FAIL-002 testcontainer sheds ("Exception while writing to stream") → isolation-cleared 31/31. +1 vs S106 (the new `Contracts/RosterEndpointContractTests`). FE 569 (+52 vs S106's 517: shell 4 + tree 9 + hooks 7 + struktur 13 + afgræns 6 + search 9 + page net + contract closure). Unit 852 (unchanged). Smoke/e2e CI-verify on push.

## Sprint Retrospective
- **The visible payoff, view-first — the merged page renders structure + people, read-only, dead-button-free.** The S91 discipline was the keystone: built OUT of the render (person rows non-clickable, search navigates-only, no drawer mounts), pinned by an allowlist vitest in StrukturPanel + an exhaustive page-level denylist.
- **The contract-lint closure is the durable win.** The S106 reads finally have FE consumers → registered (forest/search/roster) with a dedicated `Contracts/RosterEndpointContractTests` + the EXEMPT→REGISTRY move + INLINE-URL hooks + real-shape hook tests. The S97→S99→S100 FE↔backend drift class is now gated for the merged page's reads.
- **Step-0b earned its keep (3 BLOCKERs pre-code):** the search lacked `organisationId` for the Afgrænsning filter; the lint liveness scans `Contracts/` only (the roster pin was in `Approval/`); the dead-button guard needed an allowlist. **Step-7a** then caught the page-test weakness + the cross-Org loading race — both fixed. [[review-lens-complementarity]] decisive at both gates.
- **Docker-local verification:** the full regression + the isolation-clear + the FE-fix re-verify ran locally before the push.
- Durable: SPRINT-107.md + the Step-7a artifacts + FRONTEND.md (the new route). **NEXT = S108 (Phase 3b-2: the EDIT half + cutover) — the Unit + Person drawers + all mutations (unit CRUD/leaders, person-unit-assign with the two-endpoint Organisation-change routing, reporting/approver, cross-unit "Ret" [single→one-click via the etag, multiple→picker], vikar-edit) + the CUTOVER (redirect+retire `/admin/ledelseslinjer`+`/global/organisation`, collapse to one sidebar entry). Carry: the MAO-overview-people polish.**
