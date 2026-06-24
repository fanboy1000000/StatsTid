# Sprint 98 — Organisation page, backend gaps (org soft-delete + move + the aggregated tree-with-counts endpoint)

| Field | Value |
|-------|-------|
| **Sprint** | 98 |
| **Status** | complete |
| **Start Date** | 2026-06-24 |
| **End Date** | 2026-06-24 |
| **Orchestrator Approved** | yes — 2026-06-24 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 errors |
| **Test Verified** | yes (local): 850 unit + 1111 regression (+`S98OrgStructureTests` 17, verified 17/17 in isolation post-fix) + 6 smoke + 29 demoseed + 518 fe; CI GREEN `28084380708` (all 7 jobs) |

## Sprint Goal
The BACKEND half of the redesigned Organisation (Global administration) page (`design_handoff_organisation`) — Phase 1 of 2 (S99 = the hi-fi FE tree-table). Fill the org-structure CRUD gaps the page needs, on the FLAT S97 Enhed model: **org soft-delete** (blocked-if-employees), **org move/re-parent** (path-rewrite in-tx), and a **single aggregated tree-with-employee-counts endpoint**. Reuse the existing create/rename + the S97 enhed CRUD. Refinement: `.claude/refinements/REFINEMENT-organisation-page.md` (owner-resolved 4 forks + Step-4 dual-lens, 3 BLOCKERs resolved).

## Scope (in / out)

**IN (backend only):**
- **Org soft-delete** — `DELETE /api/admin/organizations/{orgId}` → `is_active=false` (recoverable; NOT hard-delete; NO If-Match — no version column, GlobalAdmin low-contention). **GlobalAdmin-floored.** In ONE tx: `SELECT … FOR UPDATE` the org row, count employees beneath, then flip `is_active=false` (serializes concurrent structural ops). **BLOCKED (422 + reason)** if it has employees beneath: an Organisation blocks if `COUNT(users WHERE primary_org_id=org AND is_active)>0`; a MAO blocks if ANY active user lives under it (`users JOIN organizations ON primary_org_id … WHERE materialized_path LIKE '/{MAO}/%' AND users.is_active`). Empty → allowed. Emits `OrganizationDeleted` (+ the 5-point P3 surface + the catalog doc).
- **Home guards — NO CHANGE (Step-0b BLOCKER B).** `GetByIdAsync` already filters `is_active=TRUE`, so the create/transfer home guards ALREADY reject a soft-deleted org (null → 400). Just DOCUMENT this as the enforcement point + a regression test (not RED-on-old). The residual sub-second create-vs-delete TOCTOU is accepted+documented (recoverable).
- **Org move / re-parent** — `PUT /api/admin/organizations/{orgId}/move` `{newParentOrgId}` (NO If-Match). Organisation only (MAOs are roots — 422); the target MUST be a MAO. Changes `parent_org_id` + **RECOMPUTES the moved row's `materialized_path` in the SAME tx** (`/{oldMAO}/{org}/`→`/{newMAO}/{org}/`) — **load-bearing (BLOCKER 1): the tree-roster reads `GetMedarbejderRosterForTreeAsync:699`/`GetPeriodStatusProjectionForTreeAsync:532` scope by `materialized_path LIKE prefix`** (the full consumer inventory is in Plan Review). No descendant cascade (Organisations are leaves; enheder/users key on org_id; the vikar reader is exact-match → move-safe). GlobalAdmin-floored. Emits `OrganizationMoved` (OLD+NEW `parent_org_id` + `materialized_path`; + the 5-point P3 surface + catalog).
- **Aggregated tree endpoint** — `GET /api/admin/organizations/tree` → the MAO→Organisation forest with per-node `employeeCount` (Organisation = its active users; MAO = Σ children) + each Organisation's active `enheder` (with tagged-user counts). **SET-BASED** (one `GROUP BY primary_org_id` over users + one `GROUP BY organisation_id`/`enhed_id` over enheder/user_enheder) + in-memory forest assembly — NO recursion/N+1. Visibility-bounded via the existing `ValidateOrgAccessAsync`/`GetAccessibleOrgsAsync` gate (GlobalAdmin = all; scoped roles = their orgs).
- The reference-class disposition (Step-4 WARNING 1): the block predicate guards `users` (the orphan-employee invariant). `enheder` are read-filtered (S97 soft-delete) → accept. `role_assignments`/`projects`/`local_configurations` referencing a soft-deleted org → **accept `is_active=false` + filtered reads, documented** (a soft-deleted org is recoverable; these aren't orphaned, just dormant) — NOT a widened block. Enumerated in the plan.
- Events + audit (P3): `OrganizationDeleted`, `OrganizationMoved` + their `IAuditProjectionMapper<T>` (ADR-026) + `EventSerializer` registrations + `Program.cs` DI. Tests. ADR + INDEX/QUALITY/ROADMAP/SECURITY.

**OUT (→ S99):** the hi-fi FE tree-table page + all 4 dialog types (Create/Rename-warning/Move/Delete) + the level control + search + the "no leader/overenskomst column" rule. The existing basic `OrgManagement.tsx` stays until S99. **OUT entirely:** hierarchical Enhed (nesting/cross-org-move/re-parent — dropped under the flat model); tightening the existing Organisation create/rename (stays LocalAdmin+ — Step-4 WARNING 2; only the NEW move/delete are GlobalAdmin).

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (new endpoints + new events [P3] + P7 access-control + the move-path-rewrite correctness) |
| **External Codex** | done — 2 BLOCKER / 1 WARNING / 1 NOTE |
| **Internal Reviewer** | done — 2 BLOCKER / 2 WARNING / 1 NOTE |

### Step-0b Findings + Resolutions (the core design — path-rewrite-as-correctness, GlobalAdmin floor, set-based aggregate, blocked-if-employees on `users.primary_org_id` — confirmed sound)
- **BLOCKER A (both) — `organizations` has NO `version` column.** The plan's "If-Match optimistic concurrency, mirror the S97 enhed fix" has no token to predicate on (the existing `OrganizationUpdated` PUT is last-writer-wins; `ok_version` is the OK-version, not a row version). **RESOLUTION: DROP the If-Match/version requirement** for move/delete (GlobalAdmin-only, low-contention structural ops; consistent with the existing rename PUT). NO new `version` column. Concurrency safety for delete comes from an in-tx serialization instead (BLOCKER/WARNING below), not optimistic concurrency.
- **BLOCKER B (Reviewer) — the is_active home-guard premise was FALSE; the protection ALREADY EXISTS.** `OrganizationRepository.GetByIdAsync` (`:20`) filters `WHERE org_id=@id AND is_active=TRUE`; both home guards (`AdminEndpoints.cs:429` create, `:1001` transfer) already `return BadRequest("...not found")` on the null → **a soft-deleted home org is ALREADY rejected today.** **RESOLUTION: DROP TASK-9802** (adding the guard is redundant). Instead **document** that GetByIdAsync's `is_active` filter is the home-guard enforcement point; keep a **regression test** (soft-delete an empty org → create/transfer onto it → 400) — NOT labelled RED-on-old (it passes on unchanged code; it pins the invariant). The soft-delete + move endpoints themselves read via GetByIdAsync (so a soft-deleted org can't be re-deleted/moved — fine).
- **WARNING (Reviewer) — TOCTOU: soft-delete vs a concurrent create/transfer.** Between the delete's employee-count and the `is_active=false` flip, a concurrent create could read the org as active (pre-commit) and insert a user → a stray user on a just-deleted org. **RESOLUTION:** the soft-delete does the **count + the `is_active=false` flip in ONE tx with `SELECT … FOR UPDATE` on the `organizations` row** (serializes concurrent structural ops + the count is consistent). The remaining sub-second create-reads-active-then-inserts-after-commit window is **accepted + documented** as a known low-severity, GlobalAdmin-initiated, fully-recoverable case (re-activate the org or re-transfer the stray user) — not worth restructuring the create/transfer handlers into the delete tx.
- **WARNING (both) — the FULL `materialized_path` consumer inventory** (so the move's blast radius is complete): (1) `ApprovalPeriodRepository` `GetMedarbejderRosterForTreeAsync:699` + `GetPeriodStatusProjectionForTreeAsync:532` — LIKE-prefix, **LOAD-BEARING** (the move-preserves-roster RED test); (2) `ApprovalPeriodRepository` pending-by-path `:96` + month-by-path `:116` — LIKE but only ever called with `"/"` (GLOBAL, move-irrelevant); (3) `ReportingLineEndpoints.cs:2670/:2688` vikar-eligibility — read + **EXACT-equality** compare (`:2709`), **move-safe** (a consistent self-path rewrite keeps empOrgPath==scopePath); (4) `OrganizationRepository.cs:31` ORDER BY (display). Only (1) needs the move to rewrite the path; all enumerated so Step-7a doesn't re-discover them.
- **NOTE (both) — the FULL P3 registration surface per event (5 touch-points, the 5th is the classic miss):** (1) the event class in `SharedKernel/Events/`; (2) the `EventSerializer.cs` dictionary entry; (3) `AuditMappers/Organization{Deleted,Moved}AuditMapper.cs`; (4) `Program.cs` `AddSingleton<IAuditProjectionMapper<T>,…>`; (5) **`Program.cs` `AddSingleton(new RegisteredAuditEventType(typeof(T), nameof(T)))`** (separate, runtime-resolved); PLUS (6) add both mappers to `docs/operations/audit-projection-catalog.md` (check_docs gates it → CI `docs` job fails otherwise). `OrganizationMoved` carries OLD+NEW `parent_org_id` + `materialized_path`.

## Architectural Constraints
- [x] P1 — Architectural integrity (soft-delete + move on the 2-tier flat model; the move rewrites the moved row's `materialized_path` in-tx [LOAD-BEARING — the tree-roster reads]; no descendant cascade [leaves]; the leaves-invariant dependency documented)
- [x] P3 — Event sourcing (`OrganizationDeleted`/`OrganizationMoved` + the full 5-point registration + catalog; `OrganizationMoved` carries old+new parent+path for replay; both lenses confirmed complete)
- [x] P4 — Concurrency (NO version column → no If-Match; the soft-delete does count + flip in ONE tx with `SELECT … FOR UPDATE`; the move locks BOTH the moved org + the new parent in-tx [the move-vs-delete-of-new-parent race, Step-7a — fixed]; the create-vs-delete sub-second residual accepted+documented)
- [x] P7 — Security (GlobalAdmin floor on move/delete [both lenses confirmed]; the home guards already reject inactive orgs via `GetByIdAsync`'s `is_active` filter [BLOCKER B — no new guard]; blocked-if-employees; the tree visibility-bounded, no cross-org leak)
- [x] P8 — CI/CD (no schema change [67 tables]; 1111 regression — `S98OrgStructureTests` 17/17 isolation-verified post-fix; CI GREEN `28084380708`)

## Task Log (planned)
- **TASK-9801 — Org soft-delete** (endpoint + repo: in ONE tx `SELECT … FOR UPDATE` the org → blocked-if-employees predicate [Organisation + MAO-subtree LIKE] → flip `is_active=false`; NO If-Match/version; `OrganizationDeleted` event + the 5-point P3 surface + catalog; GlobalAdmin floor).
- ~~TASK-9802 — the is_active home guards~~ **DROPPED (Step-0b BLOCKER B — already protected by `GetByIdAsync`'s `is_active` filter)** → folded into TASK-9806 (document the enforcement point) + TASK-9805 (a regression test, not RED-on-old).
- **TASK-9803 — Org move/re-parent** (endpoint + the in-tx `parent_org_id` + `materialized_path` rewrite [the moved row's own path only]; MAO-target validation; NO If-Match; `OrganizationMoved` event [old+new parent+path] + the 5-point P3 surface + catalog; GlobalAdmin floor).
- **TASK-9804 — Aggregated tree endpoint** (`GET /organizations/tree`; SET-BASED counts [`GROUP BY primary_org_id` over users; `GROUP BY organisation_id`/`enhed_id` over active enheder/user_enheder] + in-memory forest assembly; visibility-bounded via `ValidateOrgAccessAsync`/`GetAccessibleOrgsAsync`).
- **TASK-9805 — Tests** (RED-on-old: **move-preserves-roster** [`/period-status`+`/medarbejdere` still return the org's employees under the NEW path — the BLOCKER-1 pin]; soft-delete blocked-if-employees [Org + MAO]; GlobalAdmin-floor on move/delete + out-of-scope/under-tier denied; the aggregate counts incl. enheder tag counts. Regression [NOT RED-on-old]: soft-delete-then-create/transfer→400 [the existing GetByIdAsync protection]; the soft-delete in-tx FOR UPDATE).
- **TASK-9806 — Docs + close** (an ADR-035 amendment [or ADR-037] for the org-structure ops [soft-delete + move + the GlobalAdmin tier]; the audit-projection-catalog entries [9801/9803]; SECURITY [the GlobalAdmin structural ops + the GetByIdAsync home-guard enforcement point + the documented create-vs-delete TOCTOU residual]; INDEX/QUALITY/ROADMAP).

## Risks
- **The move path-rewrite (Step-4 BLOCKER 1, P7/P1)** — the moved org's `materialized_path` MUST be rewritten in-tx or the tree-roster reads silently drop its employees. RED test pins it. (No descendant cascade — leaves.)
- **The is_active home guards (BLOCKER 2, P7)** — without them, soft-delete leaves a hole (new users could land on a dead org). Added + RED-tested.
- **Optimistic concurrency** — delete/move carry If-Match with the version predicate IN the UPDATE + affected-row check (the S97 lesson — don't check outside the tx).
- **Authority reconciliation (WARNING 2)** — existing create/rename UNCHANGED (LocalAdmin+); only the NEW move/delete are GlobalAdmin. Flagged to the owner.
- **review-lens-complementarity**: Step-0b + Step-7a dual-lens (the move-path correctness + the soft-delete authority holes are the adversarial targets).

## Execution Outcome
All tasks complete (TASK-9802 DROPPED — already protected; folded into docs + a regression test). The backend half of the Organisation page: 3 endpoints (`DELETE /organizations/{id}`, `PUT …/{id}/move`, `GET …/tree`) + 2 events (full 5-point P3 surface each) + the OrganizationRepository methods + the audit-projection-catalog entries. **NO schema change** (67 tables — soft-delete/move use existing columns; no `version` column added). Full build 0/0. The FE is deferred to S99.

**Step-0b corrected 2 of the refinement's assumptions** (false-green traps avoided): (a) `organizations` has no `version` column → the If-Match plan was unimplementable → DROPPED (last-writer-wins + in-tx FOR UPDATE instead); (b) `GetByIdAsync` ALREADY filters `is_active` → the home guards already reject a soft-deleted org → TASK-9802 (adding the guard) was redundant → DROPPED.

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex + internal Reviewer), adversarial P7/P4/P3 |
| **Sprint-start commit** | `2cd1319` (the S98 diff) |
| **Review Cycles** | 1 review + 1 fix pass |
| **Findings** | 1 BLOCKER (Codex; fixed) + 1 WARNING (both; fixed) + 2 NOTE (1 fixed via test, 1 documented) |

### Findings
- **The core CONFIRMED sound** (both lenses): the GlobalAdmin floor (`HasGlobalScope`, same as MAO-create); the tree visibility-bounding (global aggregates filtered in assembly — no cross-org leak); the path-boundary escaping (`EscapeLike` + trailing-slash); the in-tx FOR UPDATE soft-delete; the full P3 surface (incl. the `RegisteredAuditEventType` 2nd registration).
- **[[review-lens-complementarity]] — Codex caught a BLOCKER the internal lens rated NOTE**: the **move-vs-delete-of-new-parent race** — the new parent MAO was read off-tx (unlocked), so a concurrent soft-delete of the target MAO could leave an active Organisation under an inactive MAO → fixed by locking the new parent in-tx (FOR UPDATE), which also re-validates it.
- **WARNING (both, fixed)** — the enhed `taggedUserCount` counted INACTIVE users (no `users.is_active` filter; tags clear on transfer, not deactivation) → inflated vs the active-only `employeeCount` → fixed (active-user join) + a RED-on-old test.
- **NOTE (test strengthened)** — the move-preserves-roster pin now asserts BOTH directions (the employee drops from the old MAO's roster + gains the new MAO's), not just the moved org's own read.
- **NOTE (documented)** — "Organisations are leaves" is endpoint-enforced only (no DB constraint); `ReparentAsync`'s no-cascade correctness depends on it — recorded in ADR-037 + SECURITY (a future org-nesting MUST grow a cascade).
- Artifacts: `.claude/reviews/SPRINT-98-step7a-{codex,reviewer}.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 850 | all passing |
| Regression | 1111 | +`S98OrgStructureTests` 17 (incl. the BLOCKER-1 move-preserves-roster pin [both directions] + the inactive-enhed-count RED-on-old); verified 17/17 in isolation post-fix; clean full run confirms |
| Smoke | 6 | all passing |
| DemoSeed | 29 | all passing |
| Frontend (vitest) | 518 | unchanged (FE is S99) |
| **Total** | **2514** | CI GREEN `28084380708` (all 7 jobs) |

## Sprint Retrospective
**What went well**: the review machinery corrected real misconceptions before code — Step-0b caught that `organizations` has no `version` column (the If-Match plan was unbuildable) AND that `GetByIdAsync` already filters `is_active` (the home-guard work was redundant) — both *false-green traps* the refinement would have shipped. Step-7a's Codex lens then caught the move-vs-delete-of-new-parent race (a genuine concurrency hole) the internal lens cleared. The move-path-rewrite-as-correctness (not display) framing held — the strengthened both-direction roster test pins it.
**What to improve**: the OrganizationRepository new-parent read started off-tx (the move-vs-delete race) — a reminder that any cross-row invariant in a structural mutation needs ALL the rows locked in the SAME tx (the S78/S95 lesson, re-learned for org-structure). The S98 regression run was disrupted by a concurrent fix-agent testcontainer run holding the bin (file locks) — a process-hygiene note: serialize local full-regression runs.
**Knowledge produced**: ADR-037 (the org-structure lifecycle). Recorded follow-ups: the hi-fi FE tree-table page + dialogs (S99 — the visible half of the Organisation page); a DB-level leaves guard if org-nesting is ever introduced.
