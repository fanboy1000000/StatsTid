# Sprint 91 — Teamoversigt REJECTED dead-button fix + remove "Medarbejdere" (open the tree page to LocalHR)

| Field | Value |
|-------|-------|
| **Sprint** | 91 |
| **Status** | closed |
| **Start Date** | 2026-06-21 |
| **End Date** | 2026-06-21 |
| **Orchestrator Approved** | yes (owner-ruled both cleanups; the HR-tree-access expansion is a deliberate P7 decision) |
| **Build Verified** | yes (`dotnet build` 0 err; `tsc` + `npm run build` clean) |
| **Test Verified** | yes — local: FE 495 + the S91 security suite 15/15 + scope-leak suites 100/100; **CI GREEN run `27915641624`, all 7 jobs** (build-and-test = regression **1055** + smoke + e2e). One e2e flake (the unrelated skema-registration journey timed out on a backend response under a service-health hiccup; passed on re-run — S91 touches no skema/absence/rule code) |

## Sprint Goal
Two owner-requested cleanups: (1) the small Teamoversigt **REJECTED-row dead-button** fix (a rejected month showed a Genåbn that 409s server-side); (2) **remove the old "Medarbejdere" page** (`UserManagement`, LocalHR) and **open the "Medarbejder administration" tree page** (`MedarbejderAdministration`, currently LocalAdmin) to LocalHR — a deliberate **P7 privilege expansion** so HR keeps employee management on the surviving page.

## Owner rulings (2026-06-21)
- The two pages are NOT actually redundant — `UserManagement` is the LocalHR user-LIST (list + person create/edit via the shared `EditPersonDrawer`); `MedarbejderAdministration` is the LocalAdmin reporting-line TREE (reassign approver / vikar / delete employee / enforcement). Surfaced to the owner.
- Owner chose **"Remove + full HR admin access"**: delete `UserManagement` and grant LocalHR the FULL tree page — lower the ~13 backend endpoints (GET + the structural mutations + the in-handler org-scope role floors) from LocalAdmin → LocalHR. HR can then reassign approvers, manage vikars, delete employees, and set enforcement (within org scope). Implement with a security-focused Step-7a.

## Task Log

### TASK-9101 — REJECTED dead-button fix (FE) — DONE
`TeamOversigt.tsx`: added `isReopenable` (APPROVED-only) to `StatusMeta`; the Genåbn (row + footer) + the "Sendt til lønkørsel" badge now gate on `isReopenable` instead of `isDecided`. A REJECTED row shows a muted "Afventer ny indsendelse" (the employee re-submits), no dead button. +1 RED-on-old vitest. tsc + the approval suite green (47).

### TASK-9102 — lower the tree-page backend auth to LocalHR (P7, security-sensitive)
For each endpoint `MedarbejderAdministration` + its drawer/inline-controls call that currently EXCLUDES LocalHR (a `LocalAdminOrAbove` policy and/or a `StatsTidRoles.LocalAdmin` in-handler `OrgScopeValidator` floor), lower BOTH the `RequireAuthorization` policy → `HROrAbove` AND the in-handler scope-validator floor → `StatsTidRoles.LocalHR`. Candidate set (verify each precisely against the code — only lower those that genuinely exclude LocalHR; the person-edit endpoints are already HR; `/reports` is `LeaderOrAbove` which already INCLUDES LocalHR — confirm):
- GET `reporting-lines/tree/{root}/medarbejdere` (roster), GET/PUT `.../tree/{root}/settings`, GET `.../{managerId}/vikar`, GET `admin/users/search`;
- POST/PUT `admin/users` (create/edit), POST/DELETE `admin/reporting-lines` (assign/remove approver), POST/DELETE `.../{managerId}/vikar`, POST `.../{employeeId}/remove` (delete-with-reassignment).
**Preserve the org-scope containment** (the actor must still be scoped to the org — only the ROLE floor drops from LocalAdmin to LocalHR; HR stays bounded to their org subtree). Do NOT remove any scope check. Tests: a LocalHR actor IN-scope succeeds on each; an out-of-scope or below-HR actor still 403s; no privilege-escalation (the S85 class). **KB:** ADR-027 / docs/SECURITY.md note the HR-tree-access decision.

### TASK-9103 — remove UserManagement + open the tree page (FE)
Delete `UserManagement.tsx`, `UserManagement.module.css`, `__tests__/UserManagement.test.tsx` (KEEP all shared helpers: `useAdmin`, `useEditPerson`, `useReportingLines`, `EditPersonDrawer`, `editPerson/*`, `medarbejderTree.ts`). Remove the `UserManagement` import + the `/admin/medarbejdere` route from `App.tsx`; move `MedarbejderAdministration`'s route to the LocalHR `RequireRole` block (from LocalAdmin); remove the "Medarbejdere" `Sidebar` item + change "Medarbejder administration"'s `Sidebar` minRole LocalAdmin → LocalHR; repoint `TopNav` `firstRoute` `/admin/medarbejdere` → `/admin/ledelseslinjer`; update `docs/FRONTEND.md` route table. tsc + FE build + vitest green.

### TASK-9104 — validate + security Step-7a + docs + close (Orchestrator)
Build + FE (tsc/vitest/build) + the affected backend regression (the reporting-line/admin auth tests). **SECURITY-focused Step-7a dual-lens** (privilege-escalation / scope-leak hunt — the S85 class; verify the org-scope floor consistently dropped to LocalHR with no new escalation, and HR stays org-bounded). Docs (QUALITY/ROADMAP/INDEX/SPRINT-91; docs/SECURITY.md the HR-tree-access decision). Commit + push + CI-verify; MEMORY.

## External Review (Step 7a) — SECURITY-focused
| Lens | Verdict | Artifact |
|------|---------|----------|
| Internal Reviewer | APPROVED — containment preserved on every lowered endpoint; no escalation | `.claude/reviews/SPRINT-91-step7a-reviewer.md` |
| External Codex | **4 P1** (secondary-principal same-tree binding) → **owner-ACCEPTED** (pre-existing LocalAdmin model extended to HR; documented + follow-up) | `.claude/reviews/SPRINT-91-step7a-codex.md` |

**[[review-lens-complementarity]] decisive again:** the internal lens rated the secondary-principal binding "acceptable (same as LocalAdmin)"; Codex flagged 4 P1s — the lowered tree-WRITE endpoints floor-check the PRIMARY target (the employee's org) but bind the SECONDARY principal (assigned manager / approver / replacement / vikar) only by `ValidateSameTreeAsync` (structural same-styrelse), so a sub-org-scoped LocalHR could laterally assign within their styrelse (cross-styrelse still blocked). **Owner disposition: ACCEPT + document + follow-up** — it's the pre-existing LocalAdmin model (unchanged code; S91 only admits HR), containment stays intra-styrelse, and a naive fix would break legitimate up-tree manager assignment. Documented in `docs/SECURITY.md`; a dedicated tightening sprint (both tiers, "ancestors + own-subtree, not lateral siblings") is the recorded follow-up.

## Test Summary
| Tier | S90 | S91 | Δ | Note |
|------|-----|-----|---|------|
| Unit | 856 | 856 | 0 | no unit change (the auth tests are Regression) |
| Regression | 1040 | 1055 | +15 | `S91TreePageHrAccessTests` (15: HR-in-scope 200 RED-on-old / out-of-scope 403 / below-HR 403); scope-leak suites 100/100 re-verified. **CI build-and-test = 1055** |
| Smoke | 6 | 6 | 0 | no schema change |
| Frontend (vitest) | 499 | 495 | −4 | +1 REJECTED dead-button test; −5 from the `UserManagement.test` deletion |
| e2e | 3 | 3 | 0 | via CI |

Build: `dotnet build` 0 err; `tsc` + `npm run build` clean; `check_docs` green. No schema/event change (auth + FE only).

## Sprint Retrospective
**Two owner cleanups — one trivial, one that turned into a deliberate P7 privilege decision.** (1) The Teamoversigt **REJECTED dead-button** is fixed (`isReopenable`, APPROVED-only; a rejected month shows "Afventer ny indsendelse"). (2) The "remove the old Medarbejdere page" request **was NOT a redundancy** — investigation showed the two pages serve different roles (`UserManagement` = the LocalHR user-list; `MedarbejderAdministration` = the LocalAdmin reporting-line tree), so removing the HR one meant either cutting HR off from employee management or granting HR the admin tree. **The owner chose to grant HR full tree access** → ~12 endpoints lowered LocalAdmin → LocalHR (policy + the in-handler org-scope floor), the old page deleted, the tree page opened to LocalHR.

**The security review was the value.** Both lenses confirmed the per-scope floor invariant (S76) holds and cross-styrelse is blocked; the external lens then caught a real intra-styrelse lateral-assignment characteristic (the secondary-principal same-tree binding) that the internal lens accepted — surfaced to the owner, who accepted it as the pre-existing model with a tracked tightening follow-up. The "small page cleanup" correctly escalated into an informed P7 decision with two owner sign-offs.

**Follow-up:** tighten the secondary-principal scoping (assigned manager/approver/replacement/vikar) for BOTH LocalAdmin and LocalHR with an "ancestors + own-subtree, not lateral siblings" rule (docs/SECURITY.md S91 note). Durable: SPRINT-91.md + docs/SECURITY.md S91 section.
