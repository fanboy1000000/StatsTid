# Sprint 99 — Organisation page, the hi-fi FE tree-table (the visible half)

| Field | Value |
|-------|-------|
| **Sprint** | 99 |
| **Status** | complete |
| **Start Date** | 2026-06-24 |
| **End Date** | 2026-06-24 |
| **Orchestrator Approved** | yes — 2026-06-24 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 errors; `frontend` tsc 0 errors |
| **Test Verified** | yes (local): 850 unit + 1117 regression (+`S99NameOnlyCreateTests` 6) + 6 smoke + 29 demoseed + 547 fe (+29) + 1 e2e (CI-gated); CI-pending (backfilled at close-polish) |

## Sprint Goal
The FE half of the redesigned **Global administration → Organisation** page (`design_handoff_organisation`) — Phase 2 of 2 (S98 = the backend gaps). A hi-fi React tree-table of the whole org hierarchy (MAO → Organisation → Enhed) with a level control, search, an aggregated employee count, and guarded create/rename/move/delete flows, built on the FLAT S97 Enhed model + consuming the S98/S97 endpoints. Refinement: `.claude/refinements/REFINEMENT-organisation-page.md` (owner-resolved + Step-4 dual-lens). FE-only (no backend).

## Scope (in / out)
**IN (FE + a THIN backend create-adaptation — Step-0b BLOCKER 1):**
- **Backend (thin):** make `POST /api/admin/organizations` accept a NAME-ONLY body — `orgId` optional (backend-generated stable code), `agreementCode`/`okVersion` optional (defaulted server-side; vestigial), `orgType` + `parentOrgId` from the request (the existing MAO-root / Organisation-needs-MAO-parent validation + the GlobalAdmin-MAO / LocalAdmin-Organisation floors UNCHANGED). RED-on-old: a name-only POST → 201 (was 400). KEEP backward-compat (an explicit orgId still works).
- A new hi-fi **`OrganisationPage`** (replacing the basic `OrgManagement.tsx`) under Global administration, **GlobalAdmin-gated** (route + nav; the structural ops are GlobalAdmin). Built with the real ui-kit + design tokens (oes green, 0px corners, hairline borders) per the handoff — NOT the `.dc.html` runtime.
- **The tree-table** consuming `GET /api/admin/organizations/tree` (S98): indented (depth×22px) expandable rows; a chevron for nodes with children; columns **Enhed** (name, weight 600 at depth 0), **Type** (badge: Ministeransvarsområde=info / Organisation=success / Enhed=neutral), **Medarb.** (right, tabular-nums — `employeeCount` for MAO/Organisation; `taggedUserCount` for Enhed), **Handling** (inline ghost actions).
- **"Vis til niveau"** segmented control (Ministeransvarsområde / Organisation / Enhed) → expands the whole tree to that depth; manual chevron toggle clears the active level.
- **Search** ("Søg enhed…") → flattens the tree to matching nodes (case-insensitive substring), no chevrons; empty → "Ingen enheder matcher søgningen."
- **Inline row actions** (the "Handling" cell): **Omdøb** (Enhed → inline edit via the S97 rename `PUT /enheder/{id}`; MAO/Organisation → the rename-warning dialog via the existing `PUT /organizations/{id}`), **Tilføj** (create a child of the correct type — MAO→Organisation via the existing create, Organisation→Enhed via the S97 `POST /enheder`; **Enhed→Enhed DROPPED** — flat, no nesting), **Flyt** (Organisation → the move dialog via S98 `PUT …/move`; **hidden for MAO** [roots] **and Enhed** [flat — no cross-org move]), **Slet** (the 3-branch delete dialog).
- **The 4 dialogs** (one shared shell): **Create** (`Nyt ministeransvarsområde`/`Ny organisation`/`Ny enhed` — name-first); **Rename-warning** (MAO/Organisation — the SLS/reports/history copy; Enhed renames inline, no dialog); **Move** (`Flyt enhed`→ for an Organisation: select a target MAO; the targets = the other MAOs); **Delete** — 3 branches: **Blocked** (Organisation/MAO with employees → the S98 422 + `employeeCount`; a single "Luk"), **Enhed delete** (the S97 soft-delete = untag; "members lift up"), **Empty Organisation/MAO delete** (the S98 soft-delete).
- **Status handling**: 422 (blocked-if-employees → the blocked dialog with the count), 409 (active-name dup → an inline error), 403 (non-GlobalAdmin — shouldn't reach the page). Re-fetch the tree after each mutation.
- New hooks (`useOrganizationTree`, `useOrganizationStructure` for delete/move) + reuse `useEnheder` (S97) + the existing `useOrganizations` (create/rename). vitest + a Playwright e2e journey.

**OUT:** any OTHER backend change (only the thin name-only-create adaptation above; S98 did delete/move/tree); the hierarchical-Enhed UX (nesting / cross-org Enhed move / re-parent-on-delete — DROPPED under the flat model: Enheder are leaves, Enhed delete = untag); the "no leader/overenskomst column" is enforced (the handoff's explicit rule — do NOT add them).

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (a substantial new FE surface + the flat-Enhed UX adaptation + the GlobalAdmin gating + consuming the new S98 endpoints' status codes) |
| **External Codex** | done — 1 BLOCKER / 2 WARNING / 3 NOTE |
| **Internal Reviewer** | done — 3 BLOCKER / 2 WARNING / 3 NOTE (contract mismatches the name-only dialogs hid) |

### Step-0b Findings + Resolutions (the flat-Enhed adaptation + the GlobalAdmin gate confirmed sound; the gaps are contract mismatches)
- **BLOCKER 1/2 (Reviewer) — the Create dialog is name-only, but `POST /organizations` requires `OrgId`/`OrgType`/`AgreementCode`/`OkVersion` (all `required`); "Tilføj" inherits it.** No S98 name-only create exists. **RESOLUTION: a THIN backend adaptation (S99 is no longer pure-FE) — make the create accept a name-only body**: `orgId` OPTIONAL → the backend GENERATES it (a stable code; the format is a backend detail — owner can revisit); `agreementCode`/`okVersion` OPTIONAL → default server-side (VESTIGIAL — "overenskomst is NOT a property of the org tree" per the handoff, so the org-row values don't drive employee agreements); `orgType` + `parentOrgId` from the dialog context (MAO = root; Organisation needs a MAO parent — the existing validation stays). The FE Create dialog stays name-only per the handoff. Keep the existing GlobalAdmin floor on MAO-create + LocalAdmin floor on Organisation-create.
- **BLOCKER 3 (Reviewer) — RESOLVED (already safe).** The rename `PUT /organizations/{id}` already COALESCEs null `agreementCode`/`okVersion` to the existing values (`request.X ?? existingOrg.X`, verified `AdminEndpoints.cs:254-256`). So a **name-only rename is safe** (it keeps the existing agreement/ok). Keep the rename dialog name-only; do NOT re-surface overenskomst/OK-version (the handoff forbids it on the org tree). A vitest asserts the rename PUT body doesn't clobber agreement/ok.
- **WARNING 1 (both) — the Move dialog.** The move endpoint returns **400** for shape errors (missing/self/no-op parent) + **422** for semantic (subject is a MAO / target not an active MAO), **200** + the moved row on success. **RESOLUTION:** the target `<select>` = **all active MAOs from the tree MINUS the org's current parent** (pre-exclude → no guaranteed 400); map 400 (no-op/self) + 422 (type) to an inline dialog error; re-fetch the tree on 200.
- **WARNING 2 (Codex BLOCKER) — the flat-Enhed delete copy.** The handoff's Enhed-delete dialog promises hierarchical re-parenting ("Medarbejdere, der flyttes til {parent}", "Underenheder, der slettes") — **the flat model has neither**. **RESOLUTION:** the Enhed-delete dialog = the S97 soft-delete/UNTAG copy ONLY (the users keep their `primary_org` home; NO sub-unit count, NO re-parent promise). Plus: define the **post-delete selection fallback** (`selectedB`, README:104); the **empty-MAO** delete title branches ("Slet ministeransvarsområde?"). The S91 dead-copy guard: a vitest asserts NO "underenheder slettes" line on an Enhed delete.
- **NOTE 1 (Reviewer) — the tree's Enhed nodes lack `version`/`etag`, but inline Enhed rename/delete need `If-Match`.** **RESOLUTION:** before an inline Enhed rename/delete, RESOLVE the enhed's version via `fetchEnheder(orgId)` (find the row's enhed, use its etag) — the S86 `useResolveReportingLine` ETag-resolve pattern. Otherwise every inline Enhed write 428/412s.
- **NOTE 2/3 (both) — the test matrix.** Assert: default level = Organisation on mount; search flattens + hides chevrons + the empty message; and the **no-dead-button matrix** (Tilføj present on MAO+Organisation, ABSENT on Enhed; Flyt on Organisation only, ABSENT on MAO+Enhed; Omdøb inline on Enhed, dialog on MAO/Organisation).

## Architectural Constraints
- [x] P1 — Architectural integrity (the page consumes the S98 aggregated tree; the flat-Enhed adaptation [leaves, no nesting/move]; only the thin name-only-create backend adaptation — the validation/floors/event UNCHANGED, both lenses confirmed)
- [x] P7 — Security (GlobalAdmin-gated page; the FE doesn't bypass the backend floors — the name-only create's floors + parent-type checks are not bypassable [both lenses verified]; no leader/overenskomst surfaced)
- [x] P9 — UX (hi-fi per the handoff: the tree-table, the level control [default Organisation], search-flatten, the 4 dialogs, the Danish copy; the flat-Enhed adaptation is DEAD-BUTTON-FREE [built OUT, vitest-pinned])
- [x] P8 — CI/CD (FE tsc 0 + vitest 547 + the e2e [CI-gated]; the regression 1117 — incl. the latent S97 `fetchEnheder` data-shape bug repaired)

## Task Log (planned)
- **TASK-9900 — Backend: the name-only create adaptation** (`POST /organizations` accepts name-only — generate `orgId`, default `agreementCode`/`okVersion`, keep the type/parent validation + floors; backward-compat; RED-on-old name-only→201). + the audit/event unchanged. Tests in the regression suite.
- **TASK-9901 — Hooks** (`useOrganizationTree` [GET /tree], `useOrganizationStructure` [DELETE /{id}, PUT /{id}/move]; reuse `useEnheder` [+ the **ETag-resolve-before-inline-Enhed-write** via `fetchEnheder`, NOTE 1] + `useOrganizations` [name-only create + the COALESCE-safe rename]).
- **TASK-9902 — The tree-table + level control + search** (the indented expandable table mapping MAO→`organisations`→`enheder` [depth 0/1/2]; chevron/depth; type badges; the Medarb. column [`employeeCount`/`taggedUserCount`]; "Vis til niveau" [default = Organisation]; search-flatten [no chevrons] + the empty message).
- **TASK-9903 — Inline actions + the 4 dialogs** (Omdøb [inline Enhed via the resolved-version PUT / rename-warning name-only dialog for MAO+Organisation]; Tilføj [MAO→Organisation + Organisation→Enhed; **ABSENT on Enhed**]; Flyt [Organisation only; target = other active MAOs minus current parent; **hidden on MAO+Enhed**; 400/422 inline]; Slet [blocked-422+employeeCount / **Enhed=UNTAG copy, no sub-unit/re-parent** / empty-Org/MAO]); the shared dialog shell; the post-delete selection fallback.
- **TASK-9904 — Wire-up + nav** (replace `OrgManagement.tsx`; the GlobalAdmin route [retained as-is, `App.tsx:96` — no regression] + nav; re-fetch the tree after every mutation; NO leader/overenskomst column).
- **TASK-9905 — Tests** (vitest: tree render + level control [default Organisation] + search-flatten + each dialog + the status branches [422 blocked+count / 409 dup / move 400-vs-422]; the **no-dead-button matrix** [Tilføj absent on Enhed; Flyt absent on MAO+Enhed; Omdøb inline on Enhed]; the Enhed-delete-no-"underenheder" guard; the rename-doesn't-clobber-agreement guard; a Playwright e2e journey: create → rename → move → delete).
- **TASK-9906 — Docs + close** (FRONTEND.md note; ADR-037 amendment [the FE + the name-only create]; INDEX/QUALITY/ROADMAP).

## Risks
- **The flat-Enhed UX adaptation** — the handoff assumes hierarchical Enhed; the FE must render Enheder as flat leaves (no chevron-children, no Tilføj-on-Enhed, no Flyt-on-Enhed) — the dropped behaviours must not leave dead buttons (the S91 lesson). Build them OUT, don't disable.
- **Status-code handling** — the dialogs must map the S98 422 (blocked + count) / 409 (dup) correctly; the move's target validation; re-fetch the tree after mutations (the counts roll up).
- **GlobalAdmin gating** — the page is GlobalAdmin; the backend re-checks every mutation (the FE gate is convenience, not the security boundary).
- **review-lens-complementarity**: Step-0b + Step-7a dual-lens (the flat-Enhed adaptation completeness + the dead-button/status-handling are the targets).

## Execution Outcome
The Organisation page is COMPLETE — the redesigned `design_handoff_organisation` page built on the real ui-kit/tokens, consuming the S98/S97 endpoints, on the flat model. A thin backend adaptation (the name-only create) + the FE (a new `OrganisationPage` replacing `OrgManagement.tsx`; the `useOrganizationTree`/`useOrganizationStructure` hooks; the 3-tier tree-table; "Vis til niveau" + search; the 4 dialogs; inline actions; the version-resolve). Build 0/0; FE tsc 0; vitest 547 (+29).

**Step-0b corrected 3 BLOCKERs — contract mismatches the handoff's clean name-only dialogs hid**: the create POST needed `OrgId`/`OrgType`/`AgreementCode`/`OkVersion` (→ the thin name-only-create adaptation); the rename was already COALESCE-safe (BLOCKER cleared — name-only rename keeps agreement/ok); the tree's enhed nodes lack `version` (→ the `fetchEnheder` ETag-resolve before inline Enhed writes). Plus the flat-Enhed delete copy (drop the hierarchical "re-parent"/"sub-units").

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex + internal Reviewer), adversarial P7 + flat-Enhed |
| **Sprint-start commit** | `33041edc79846fb56865a40172039af270c47355` |
| **Review Cycles** | 1 review + 1 fix pass |
| **Findings** | 1 BLOCKER (Codex; fixed) + 2 WARNING (1 fixed, 1 accepted) + NOTEs |

### Findings
- **The internal Reviewer adversarially CONFIRMED sound**: the name-only create's validation (MAO=root / ORGANISATION-needs-MAO-parent) + the floors (GlobalAdmin-MAO / LocalAdmin-Organisation) + the `OrganizationCreated` event + materialized_path are UNCHANGED + not bypassable (the generated-id retry bounded + correct); the flat-Enhed adaptation is DEAD-BUTTON-FREE (built OUT, vitest-pinned); no leader/overenskomst surfaced; the version-resolve + status handling correct.
- **[[review-lens-complementarity]] — Codex caught a real BLOCKER (a latent S97 bug) the internal lens cleared**: `fetchEnheder` typed `GET /api/admin/enheder` as a bare array but the backend returns `{ enheder: [...] }` → `.map` throws (the S97 vitest mocked the WRONG shape, hiding it — so S97's EnhederPanel/EnhedTagPicker listing was latently broken too). Fixed (the unwrap + the corrected mocks — now faithful to the backend); **a real prod bug repaired beyond S99's scope.**
- **WARNING (fixed)** — `createOrganization` dropped the HTTP status (the 409 branch couldn't fire) → a status-tagged error (mostly for the shared ENHED-create path; the org-create 409 is effectively unreachable). **WARNING (accepted)** — orgs have no name-uniqueness by design (dup names allowed); the FE's org-dup copy is harmless dead code.
- **NOTEs (accepted)** — a forward-stale high count strands the empty-delete behind "Luk" until reload (small window — the tree re-fetches after every mutation); the Move dialog doesn't translate a 404 (cosmetic).
- Artifacts: `.claude/reviews/SPRINT-99-step7a-{codex,reviewer}.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 850 | all passing |
| Regression | 1117 | +`S99NameOnlyCreateTests` 6 (the name-only create + backward-compat + the validation) |
| Smoke | 6 | all passing |
| DemoSeed | 29 | all passing |
| Frontend (vitest) | 547 | +29 (the tree, the no-dead-button matrix, the Enhed-delete-no-underenheder guard, the rename-name-only guard, the status branches, the version-resolve); the latent `fetchEnheder` bug fixed |
| E2E (Playwright) | +1 | the create→rename→move→delete journey (CI-gated) |
| **Total** | **2549** | CI confirmation pending |

## Sprint Retrospective
**What went well**: Step-0b caught that the handoff's clean name-only dialogs hide real contract mismatches (the create needs 4 fields; the tree's enhed nodes lack version) — resolved before code (the thin name-only-create adaptation + the ETag-resolve). Step-7a's Codex lens then caught the `fetchEnheder` data-shape BLOCKER — **a latent S97 bug the S97 vitest masked by mocking the wrong shape**; fixing it repaired the S97 EnhederPanel/EnhedTagPicker listing too. The flat-Enhed adaptation is faithfully dead-button-free (built OUT, not disabled — the S91 lesson, vitest-pinned). **The redesigned Organisation page (S98 backend + S99 FE) is COMPLETE.**
**What to improve**: the `fetchEnheder` mock-the-wrong-shape miss is a reminder that FE hook tests must mock the REAL response envelope (an integration/e2e or a shared typed client would have caught it at S97). The handoff's name-only dialogs needed contract reconciliation the design didn't surface — verify the FE design against the live API shape at refinement, not at code.
**Knowledge produced**: ADR-037 amendment (the FE + the name-only create). Recorded follow-ups: the org-create 409/name-uniqueness decision (currently dup names allowed); the forward-stale empty-delete polish; drop `enhed_label` once consumers cut over.
