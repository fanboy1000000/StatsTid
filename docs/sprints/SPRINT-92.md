# Sprint 92 — Org-model flatten (Organisation + Enhed foundation)

| Field | Value |
|-------|-------|
| **Sprint** | 92 |
| **Status** | complete |
| **Start Date** | 2026-06-22 |
| **End Date** | 2026-06-23 |
| **Orchestrator Approved** | yes — 2026-06-23 |
| **Build Verified** | yes — `dotnet build` 0 warnings 0 errors (combined tree) |
| **Test Verified** | yes — CI GREEN `27988737234` (all 7 jobs, 2026-06-23): 856 unit + 1060 regression + 6 smoke + 29 demoseed + 495 fe = 2446 |

## Sprint Goal
Flatten the org **data model** from the 4-tier MINISTRY/STYRELSE/AFDELING/TEAM tree to the target **MAO → Organisation** structure (the former AFDELING/TEAM units demoted to **Enhed** display-metadata on the user), as the foundation the flat role-scope (S93), flat approval (S94), and tree-machinery retirement (S95) build on. **Authority is RE-BASED to the Organisation, not held identical** (see below). Greenfield reseed; no production-data migration. Phase 1 of the flat-authority reform (refinement: `.claude/refinements/REFINEMENT-flat-authority-model.md`).

### The authority-change framing (Step-0b BLOCKER fix — read this)
The flatten has TWO effects with DIFFERENT authority semantics:
- **(1) Rename** MINISTRY→MAO, STYRELSE→ORGANISATION — **identity-preserving** (the tree-root CTE + `CoversOrg` prefix-matching resolve the same set; a former-styrelse root becomes the same org as an Organisation).
- **(2) Level-collapse** AFDELING/TEAM → users move up to their parent Organisation; their former unit name → `enhed_label` — **NOT identity-preserving, BY DESIGN.** Per the owner model, **Enhed holds no authority** → the smallest authority unit is the Organisation, so an afdeling/team-level scope **coarsens** to its Organisation. This is the intended re-basing, not a bug:
  - `ORG_AND_DESCENDANTS` admin scopes: reach is **PRESERVED** (the parent Organisation's path still contains every moved-up user).
  - `ORG_ONLY` scopes keyed on the *exact* afdeling: **coarsen** to the Organisation (an employee's self-scope; a former afdeling-admin's reach widens to the whole Organisation — intended; afdeling-level authority cannot exist in the target model).
  - The role-scope **MECHANISM** (`ORG_AND_DESCENDANTS`/`CoversOrg`/path-prefix) is **UNCHANGED this sprint** — the full flat-role-scope reform (explicit Organisation-sets, drop `ORG_AND_DESCENDANTS`) is **S93**. S92 only changes the *granularity* (afdeling→Organisation) the existing mechanism operates at.

The proof obligation is therefore **"rename-identical + collapse-with-stated-coverage-deltas + NO narrowing,"** not a single "behavior-identity" assertion.

## Scope (in / out)

**IN:**
- `organizations.org_type` CHECK → `{MAO, ORGANISATION}`; collapse the tree to 2 levels (MAO root → Organisation).
- Reseed users directly onto **Organisations** (every `users.primary_org_id` → an `ORGANISATION` row); former AFDELING/TEAM unit name → `employee_profiles.enhed_label`.
- **Re-point `role_assignments.org_id`** off deleted AFDELING/TEAM orgs onto the parent Organisation (FK-break fix + the deliberate coarsening above).
- Re-point the **transitional** tree machinery to the new types: `ResolveTreeRootOrgIdAsync` CTE + `ReportingLineEndpoints` tree-root validation `('MINISTRY','STYRELSE')` → `('MAO','ORGANISATION')`.
- Org create/edit endpoints (`AdminEndpoints`): `ValidOrgTypes` → new types; type-scoped parent rules (MAO = root; Organisation under a MAO).
- Reseed: `init.sql` org+user+role_assignments seed, `DemoGenerator.cs` + regenerated `99-demo-seed.sql` + `DemoVerifier.cs`, frontend org-type labels/options + the `MedarbejderAdministration.tsx` tree-root-type filter.
- Test-seed conversion across the live grep-gated set + FE tests.
- ADR-035 created (umbrella "Flat authority model"); db-schema doc regenerated.

**OUT (later sprints):**
- The **structured Enhed** table + multi-tag user↔enhed membership + the rich Enhed search UX → the Organisation admin **page** sprint.
- Flat **role-scope** (drop `ORG_AND_DESCENDANTS`, explicit Organisation-sets, `CoversOrg`/`OrgScopeValidator`/JWT/grant-revoke/FE picker) → **S93**.
- Flat **approval** (`CanApprove`, same-Organisation vikar, HR/Admin fallback) → **S94**.
- **Retire** the tree machinery (`tree_root_org_id`, `ResolveTreeRootOrgIdAsync`, `ValidateSameTreeAsync`, `reporting_line_tree_settings` + the 428 gate, lock re-key) → **S95**.
- The Organisation admin **page** → after S95.

## Entropy Scan Findings (Step 0a)

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | INDEX 49 entries consistent (ADR-034 latest); ADR-008/027 paths resolve. |
| Pattern compliance spot-check | CLEAN | n/a at plan time; agents carry FAIL-001/PAT checklists. |
| Orphan detection | CLEAN | No new orphans since S91 close. |
| Documentation drift | DEBT | `MEMORY.md` over its 24.4KB budget — tracked, non-blocking. Pre-existing `init.sql:857-876` comment drift (`hr01→MIN01` vs seeded `STY02`) fixed in TASK-9201. |
| Quality grade review | CLEAN | No domain grade change pending pre-sprint. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 architectural integrity; P7 access control; schema migration = high-risk) |
| **External Codex** | invoked 2026-06-22 — cycle 1: 2 BLOCKER / 3 WARNING / 2 NOTE; **cycle 2: "Sound to plan" — no remaining BLOCKER** |
| **Internal Reviewer** | invoked 2026-06-22 — cycle 1: 2 BLOCKER / 2 WARNING / 3 NOTE; **cycle 2: "Sound to plan" — no remaining BLOCKER** |
| **BLOCKERs resolved before Step 1** | yes — (1) `role_assignments.org_id` re-point + FK validation added to TASK-9201; (2) goal reframed to "rename-identical + collapse-coarsening-by-design + NO narrowing" with collapse-direction RED-on-old in TASK-9206 |

### Findings (cycle 1)

_Codex (external):_
- **BLOCKER** — "authority UNCHANGED" false for the level-collapse; `role_assignments.org_id` (9 AFDELING rows, `init.sql:905-924`) must be re-pointed or FK-break/silent-widen. → **FIXED:** goal reframed (rename-identical + collapse-coarsening-by-design); `role_assignments` re-point added to TASK-9201 + validation; collapse-direction RED-on-old in TASK-9206.
- **BLOCKER** — the single "tree-root resolves identically" proof is too narrow (doesn't cover `CoversOrg`/`OrgScopeValidator`/approval/vikar). → **FIXED:** TASK-9206 now requires collapse-direction assertions (ORG_ONLY coarsens; ORG_AND_DESCENDANTS preserved; assert NO narrowing) + per-Organisation user-count check.
- **WARNING** — `MedarbejderAdministration.tsx:341-345` tree-root filter (MINISTRY|STYRELSE) goes empty post-rename. → **FIXED:** added to TASK-9204.
- **WARNING** — `DemoVerifier.cs:100-105` asserts demo roots are STYRELSE. → **FIXED:** added to TASK-9205.
- **WARNING** — materialized-path consumers participate in the coarsening (coherent only at Organisation-level authority). → **ADDRESSED** by the reframe (Organisation is the authority unit).
- **NOTE** — baseline users span MINISTRY (`admin01/02`) and AFDELING; demo spans TEAM. Re-point map must cover MINISTRY + TEAM, not just AFDELING. → **FIXED:** TASK-9201/9205 mapping covers all source levels.
- **NOTE** — ADR-035 must mark ADR-027 machinery "transitional," not "retired," in S92. → **Already stated; reinforced in TASK-9207.**

_Internal Reviewer:_
- **BLOCKER** — `role_assignments.org_id` re-point omitted; FK break + silent scope shift (same as Codex BLOCKER 1). → **FIXED** (as above).
- **BLOCKER** — collapse is not semantics-preserving for `ORG_ONLY`/exact-org; reframe goal + collapse-direction RED-on-old (same as Codex BLOCKER 2). → **FIXED** (as above).
- **WARNING** — TASK-9205 understates the demo rewrite (4–6-level tree, 263 literal occurrences, `TreeRootOrgId` per row, CROSS_STYRELSE edge cases). → **FIXED:** TASK-9205 expanded.
- **WARNING** — "~50 files" is soft; the grep gate is the source of truth; include shared `*TestSchema`/WAF fixtures + the Postgres-coupled classes (`ReportingLineRepositoryTests`, `ManagerVikarEngineTests`). → **FIXED:** TASK-9206 reframed.
- **NOTE** — `init.sql:857-876` comment drift; `DesignatedApproverAuthorizer.cs`/`Organization.cs` carry type literals in comments (grep criterion must scope to executable code or update comments); `reporting_line_tree_settings(STY02)` stays valid (becomes ORGANISATION). → **FIXED:** comment fixes in TASK-9201/9202; grep criterion reworded; tree-settings note added.

### Resolution
All four cycle-1 BLOCKERs (two per lens, converging on the same two issues) are addressed by the plan edits above. **Cycle 2 (both lenses): "Sound to plan" — no remaining BLOCKER.** Two non-blocking cycle-2 NOTEs folded in: (i) TASK-9201 now states `reporting_lines`/`manager_vikar` seeds need no change (tree_root is STYRELSE→ORGANISATION, no AFDELING FK); (ii) the `ResolveTreeRootOrgIdAsync:414/:454` error-string literals fold into TASK-9202's comment-fix bucket (cosmetic). Reviewer worked example confirms the coarsening only ever moves a scope UP the path (widen-or-equal, never narrow), so TASK-9206's "assert NO narrowing" is the correct discriminator. **Plan is sound to decompose (Step 1).**

## Architectural Constraints Verified
- [x] P1 — Architectural integrity (org-model flatten preserves bounded contexts; tree machinery RE-POINTED not broken — Step-7a both lenses confirmed)
- [x] P3 — Event sourcing append-only (org create/update events unchanged; the init.sql profile pre-seed REVERTED — no projection-row without its EmployeeProfileCreated event; P3 ≫ P9)
- [x] P7 — Security & access control (rename identity-preserving; collapse coarsening intended + tested for NO narrowing via `S92OrgFlattenTests`; `role_assignments`/`local_configurations`/`projects` re-point verified; both lenses confirmed widen-or-equal)
- [x] P8 — CI/CD (full greenfield reseed; 1060 regression green locally; CI confirmation pending)

## Task Log (planned decomposition)

### TASK-9201 — Org schema flatten + greenfield reseed (init.sql)
| Field | Value |
|-------|-------|
| **Agent** | Data Model (extended into `docker/postgres/init.sql` schema + seed, cross-domain authorized — Orchestrator-approved, protected file) |
| **Components** | `organizations` table, `users` seed, `role_assignments` seed, `employee_profiles.enhed_label` |
| **KB Refs** | ADR-008, ADR-027 (tree-root, transitional), ADR-022 (enhed_label) |

**Description**: `org_type` CHECK → `('MAO','ORGANISATION')`. Rewrite the `organizations` seed (`init.sql:834-847`) to 2-level MAO→Organisation (MINISTRY→MAO, STYRELSE→Organisation; the 5 AFDELING rows removed). Re-point **every source level** to an Organisation: users on MINISTRY (`admin01/02`) and AFDELING (`mgr01/02`, `emp00x`) → their Organisation; populate `enhed_label` from the collapsed unit name. **Re-point `role_assignments.org_id`** (9 AFDELING rows, `:905-924`) onto the parent Organisation. Fix the `:857-876` comment drift (`hr01→MIN01` → actual). `materialized_path` stays (MAO→Organisation paths). Note: the seeded `reporting_line_tree_settings(STY02, REQUIRED)` row stays valid (STY02 becomes an ORGANISATION; `:1657` re-pointed to accept it). The `reporting_lines` (`:2425-2444`) and `manager_vikar` seed blocks need **no change** — their `tree_root_org_id` already references STYRELSE rows (STY01/02/04/05 → ORGANISATION), no AFDELING FK. Regenerate `docs/generated/db-schema.md`.

**Validation Criteria**:
- [ ] CHECK accepts only MAO/ORGANISATION; no AFDELING/TEAM rows remain.
- [ ] Every `users.primary_org_id` AND every `role_assignments.org_id` references a MAO/ORGANISATION row (no FK to a deleted org).
- [ ] Per-Organisation user counts match the intended re-point map (no user lands in the wrong Organisation).
- [ ] `tools/check_docs.py` passes; db-schema regenerated.

---

### TASK-9202 — Re-point the transitional tree machinery to the new types
| Field | Value |
|-------|-------|
| **Agent** | Infrastructure + Backend API (cross-domain authorized) |
| **Components** | `ReportingLineRepository.ResolveTreeRootOrgIdAsync` (both overloads, `:404,:444`), `ReportingLineEndpoints` tree-root validation (`:1657`) |
| **KB Refs** | ADR-027 D2/D9 (tree-root invariant — transitionally preserved, retired S95) |

**Description**: CTE `WHERE org_type IN ('MINISTRY','STYRELSE')` → `IN ('MAO','ORGANISATION')`; endpoint validation likewise. The rename is identity-preserving for the tree-root resolution. `ValidateSameTreeAsync`, `CoversOrg`, `materialized_path`, `ORG_AND_DESCENDANTS` LEFT INTACT (retired S95). Update the type-literal doc-comments in `DesignatedApproverAuthorizer.cs:36-39,106,117` + `Organization.cs:7`. Sweep for any other `org_type` literal branch.

**Validation Criteria**:
- [ ] No remaining `'MINISTRY'`/`'STYRELSE'`/`'AFDELING'`/`'TEAM'` literals in **executable** non-test backend code (comments updated separately).
- [ ] RED-on-old: a user under an Organisation resolves that Organisation as tree root; `ResolveTreeRootOrgIdAsync(period.OrgId)` at `ApprovalEndpoints.cs:280,495` resolves the SAME root as before (AFD01→STY02 before == ORGANISATION after).
- [ ] `dotnet build` clean.

---

### TASK-9203 — Org create/edit endpoints on the new types
| Field | Value |
|-------|-------|
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | `AdminEndpoints` org create (POST, `:20-23,:88-143`) + edit (PUT, `:195-285`) |
| **KB Refs** | ADR-008, ADR-018 (org events) |

**Description**: `ValidOrgTypes` → `{MAO, ORGANISATION}`. Type-scoped parent rules: MAO = root (no parent); ORGANISATION must have a MAO parent. `materialized_path` build unchanged. `OrganizationCreated/Updated` emission + audit unchanged. (Enhed creation NOT added — deferred to the page.)

**Validation Criteria**:
- [ ] Create rejects AFDELING/TEAM (400); accepts MAO (root) + ORGANISATION (under MAO); rejects ORGANISATION without a MAO parent.
- [ ] Existing org event + audit emission unchanged.

---

### TASK-9204 — Frontend org-type labels + tree-root-type filter
| Field | Value |
|-------|-------|
| **Agent** | UX |
| **Components** | `OrgManagement.tsx:9-21` (options/labels); `MedarbejderAdministration.tsx:341-345` (tree-root-type filter); any TS org-type list |
| **KB Refs** | ADR-011 |

**Description**: `ORG_TYPE_OPTIONS`/`ORG_TYPE_LABELS` → `{MAO: 'Ministeransvarsområde', ORGANISATION: 'Organisation'}`; default create type → ORGANISATION. **Update the `MedarbejderAdministration.tsx` tree-root filter `MINISTRY|STYRELSE` → `MAO|ORGANISATION`** (else the reporting-tree-root selector goes empty). No new page work.

**Validation Criteria**:
- [ ] No AFDELING/TEAM in the FE; the tree-root selector populates on the new types; FE build + vitest green.

---

### TASK-9205 — Demo seed regeneration (generator + SQL + verifier)
| Field | Value |
|-------|-------|
| **Agent** | Tooling/DemoSeed (cross-domain authorized) |
| **Components** | `DemoGenerator.cs` (`:68-316`), regenerated `99-demo-seed.sql` (263 literal occurrences), `DemoVerifier.cs:100-105` |
| **KB Refs** | S84 demo-seed refinement |

**Description**: Real generator rewrite, not a literal swap: retire the AFDELING(d2/d3)/TEAM(d4) tree-construction loops; emit MAO (root) → Organisation only; lift **all** demo users (today on leaf AFDELING/TEAM) to their Organisation; source `enhed_label` from the collapsed leaf-org name; re-key every `TreeRootOrgId` to the Organisation; preserve the CROSS_STYRELSE_TRANSFER / enhed_label edge cases (`:520,:561`) re-expressed on the 2-level model. Update `DemoVerifier.cs` (STYRELSE assertion → ORGANISATION). Regenerate `99-demo-seed.sql`.

**Validation Criteria**:
- [ ] Regenerated `99-demo-seed.sql` contains zero AFDELING/TEAM; users on Organisations; `enhed_label` populated; every demo reporting-line/vikar `tree_root_org_id` resolves to an ORGANISATION row; `DemoVerifier` passes; demo stack loads.

---

### TASK-9206 — Test-seed conversion + collapse-direction RED-on-old
| Field | Value |
|-------|-------|
| **Agent** | Test & QA |
| **Components** | the grep-gated live set of test files seeding the 4 types (incl. shared `*TestSchema`/WAF fixtures + the Postgres-coupled `ReportingLineRepositoryTests`/`ManagerVikarEngineTests`); smoke; FE |
| **KB Refs** | FAIL-002 (testcontainer churn close-protocol), PAT-008 |

**Description**: Convert every test seed using MINISTRY/STYRELSE/AFDELING/TEAM to MAO/ORGANISATION (users + role_assignments on Organisations). The grep gate enumerates the live set (do NOT rely on a fixed count); include shared fixtures + the localhost:5432-coupled classes (require compose Postgres up — else connection-refused reads as failure). Add the **collapse-direction** RED-on-old assertions: (a) a moved-up `ORG_ONLY` employee's coverage coarsens to the Organisation (stated delta); (b) an `ORG_AND_DESCENDANTS` admin still reaches a moved-up report (PRESERVED); (c) **assert NO narrowing** for any seed actor; (d) a former-AFDELING-typed org row is rejected by the CHECK; (e) `ResolveTreeRootOrgIdAsync` resolves the Organisation as root. Run the full pyramid per the FAIL-002 close protocol.

**Validation Criteria**:
- [ ] All regression/smoke/FE suites green on the new taxonomy; the five RED-on-old assertions present and passing; no `role_assignments` FK violation at seed time.

---

### TASK-9207 — ADR-035 (umbrella) + docs
| Field | Value |
|-------|-------|
| **Agent** | Orchestrator |
| **Components** | `docs/knowledge-base/decisions/ADR-035-flat-authority-model.md`, INDEX, db-schema, SYSTEM_TARGET (if org taxonomy documented there) |
| **KB Refs** | forward-refs ADR-008, ADR-027 |

**Description**: Create **ADR-035 "Flat authority model"** as the reform umbrella; S92's decision = the org-model flatten (MAO/Organisation + Enhed-metadata) + the transitional tree-root re-point + **the intended afdeling→Organisation coarsening** (Enhed holds no authority). Forward-pointer to S93–S95 (role-scope flatten, flat approval, tree retirement) + the ADR-027 **D2/D9/D11/D13/D15/D18/D19 per-item disposition table marked "transitional" for S92** (NOT retired — `reporting_line_tree_settings` + the `:1657` validation + the lock matrix all still live). Update INDEX; note ADR-008 materialized-path role narrows.

**Validation Criteria**:
- [ ] ADR-035 created + INDEXed; `check_docs.py` passes; ADR-027/008 cross-referenced; D-disposition table shows "transitional" for S92.

## Risks & Conflicts
- **Collapse coarsening (P7, conscious/by-design)** — afdeling-level scopes re-base to their Organisation (Enhed holds no authority). Intended per the owner model, but a real authority *widening* for any former afdeling-admin. Mitigation: TASK-9206 collapse-direction tests assert the deltas + NO narrowing; greenfield (seeds illustrative); S93 re-expresses all scopes as explicit Organisation-sets anyway.
- **`role_assignments` FK + re-point correctness** — a mis-mapped row lands an actor in the wrong Organisation. Mitigation: TASK-9201 validation (every org_id resolves; per-Organisation counts).
- **Demo generator rewrite (P8)** — 4–6-level tree → 2-level is a real rewrite with edge cases (CROSS_STYRELSE, TreeRootOrgId per row). Mitigation: TASK-9205 expanded scope + verifier + resolve-to-ORGANISATION criterion.
- **Transitional tree-root identity** — the re-pointed CTE MUST resolve identically. Mitigation: TASK-9202 RED-on-old (AFD01→STY02 == ORGANISATION).
- **Test blast radius** — grep gate (not a fixed count) is the source of truth; include shared fixtures + Postgres-coupled classes. Mitigation: TASK-9206.
- **ADR-027 partial supersession** — D2/D9/D11 transitionally preserved (re-pointed), fully retired S94/S95; ADR-035 disposition table must show "transitional," not "retired."

## Execution Outcome
All 7 tasks complete. Implemented by 4 slice agents in the main working tree (disjoint dirs): Backend (9201/9202/9203), Frontend (9204), DemoSeed (9205), Test & QA (9206); ADR-035 (9207) by the Orchestrator. The Backend agent additionally caught + re-pointed 2 FK breaks the plan did not enumerate (`local_configurations`, `projects` → AFD01/AFD02). Combined tree builds 0/0.

**Two in-flight regression fixes** (Orchestrator, post-central-run; both expected consequences of the flatten, both seed/test-side):
1. `MixedRoleScopeLeakTests` top-level-org-create tests: top-level org is now a **MAO** (an ORGANISATION requires a MAO parent) — body type `ORGANISATION`→`MAO`.
2. `EmployeeProfileEndpointTests.Bootstrap_BackfillsAllSeedUsers`: **reverted** the Backend agent's init.sql `employee_profiles.enhed_label` pre-seed (it created 9 projection rows without `EmployeeProfileCreated` events → P3 violation + broke the seeder's all-19 bootstrap). Reverted = the seeder backfills all 19 via events; the 9 moved users get NULL `enhed_label` (cosmetic, ADR-022 FE falls back to org name). P3 ≫ P9.
Both fixes re-verified green in isolation (3/3 affected tests pass).

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex + internal Reviewer) |
| **Sprint-start commit** | `cc7aa09489819dab026691897b09b7d2a585e131` |
| **Command** | `codex exec "<focused-prompt>"` (uncommitted diff) + Reviewer Agent on `git diff` |
| **Review Cycles** | 1 (clean — no fix cycle needed) |
| **Findings** | 0 BLOCKER, 0 WARNING, 1 NOTE (cosmetic seed comment — addressed) |
| **Resolution** | clean; the cosmetic NOTE (`init.sql:2435` reporting-lines seed comment) reworded |

### Findings
- **Codex**: "Clean — no findings."
- **Internal Reviewer**: No BLOCKER/WARNING. Adjudicated the 3 implementer-flagged test changes as FAITHFUL (the 403→428 is the intended ADR-035 D5 coarsening surfaced honestly; the disjoint-JWT-scope + ACTING-swap preserve their security properties). P7 widen-or-equal/never-narrow confirmed; P3 profile-pre-seed reversion confirmed complete; tree machinery re-pointed not broken. NOTE (P9, addressed): stale seed comment.
- Artifacts: `.claude/reviews/SPRINT-92-step7a-{codex,reviewer}.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 856 | all passing |
| Regression | 1060 | all passing (local, fresh-greenfield Postgres; 1058 first run + 2 seed/test fixes re-verified) |
| Smoke | 6 | all passing |
| DemoSeed | 29 | all passing |
| Frontend (vitest) | 495 | all passing |
| **Total** | **2446** | CI GREEN `27988737234` (all 7 jobs) |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 7 |
| Constraint Violations | 0 (no new events/endpoints; no scope-boundary crossings beyond the authorized cross-domain backend slice) |
| Reviewer Findings | Step-0b: 4 BLOCKER (2 distinct, both lenses) + 5 W + 5 N (all resolved pre-code); Step-7a: 0 B / 0 W / 1 N |
| External Review Cycles | Step-0b: 2 (cycle-2 "sound to plan"); Step-7a: 1 (clean) |
| Re-dispatches | 0 (2 Orchestrator-side seed/test fixes post-central-run) |
| First-Pass Rate | 7/7 = 100% (no agent re-dispatch) |

## Sprint Retrospective
**What went well**: The Step-0b dual-lens caught the load-bearing collapse-vs-rename authority distinction BEFORE any code (both lenses, same 2 BLOCKERs) — the plan-encoded "authority unchanged" claim would otherwise have shipped a silent FK break + an unstated coarsening. The 4-slice parallel/sequential split (disjoint dirs) ran clean with 100% first-pass. Step-7a both lenses clean.
**What to improve**: The plan under-enumerated the FK blast radius (`local_configurations`/`projects`) — the Backend agent caught it, but a grep for `REFERENCES organizations` at plan time would have surfaced it. The Backend agent's well-intentioned enhed_label pre-seed violated P3 (caught only at the central regression run, not its own build gate) — a reminder that event-sourced projection rows must come from the seeder/events, never raw init.sql INSERTs.
**Knowledge produced**: ADR-035 (umbrella — Flat authority model).
