# Sprint 93 — Flat role-scope (drop ORG_AND_DESCENDANTS → explicit Organisation-sets)

| Field | Value |
|-------|-------|
| **Sprint** | 93 |
| **Status** | complete |
| **Start Date** | 2026-06-23 |
| **End Date** | 2026-06-23 |
| **Orchestrator Approved** | yes — 2026-06-23 |
| **Build Verified** | yes — `dotnet build` 0 errors (combined tree) |
| **Test Verified** | yes — CI GREEN `28011098005` (all 7 jobs, 2026-06-23): 854 unit + 1068 regression + 6 smoke + 29 demoseed + 495 fe = 2452 |

> **v2 (post Step-0b dual-lens).** Corrected the seed count (7 not 9), the picker-method dormancy claim, the demo expansion (no MAO-rooted demo scope exists), the RED-on-old framing, broader `GetAccessibleOrgsAsync` consumers, and **resolved OQ1 to REJECT MAO-typed ORG_ONLY grants** (Codex BLOCKER — a MAO scope confers org-structure admin, not inert). See Plan Review (Step 0b).

## Sprint Goal
ADR-035 slice 2 (Phase 2 of the flat-authority reform). Drop `ORG_AND_DESCENDANTS` subtree inheritance from role-scope: coverage becomes **exact Organisation-set membership** (the union of a user's `ORG_ONLY` rows; no materialized-path prefix). `scope_type` collapses to `{GLOBAL, ORG_ONLY}`, and a non-GLOBAL (ORG_ONLY) grant's `org_id` must be an **ORGANISATION** (not a MAO). This **preserves post-S92 coverage exactly** (a pure mechanism change, subtree→explicit-set) — provided MAO-rooted scopes are expanded to explicit per-Organisation rows. Refinement: `.claude/refinements/REFINEMENT-flat-authority-model.md` (OQ2 union-of-rows, OQ3 keep `{GLOBAL,ORG_ONLY}`, OQ8 explicit-grant — locked).

## Scope (in / out)

**IN:**
- `RoleScope.CoversOrg`: delete the `ORG_AND_DESCENDANTS` prefix branch; GLOBAL + exact-equality only.
- `OrgScopeValidator.GetAccessibleOrgsAsync`: remove the `GetDescendantsAsync` subtree expansion → return exactly the assigned (role-floored) org set. (This is the single `GetDescendantsAsync` production caller; its downstream surfaces — admin pickers, **AuditEndpoints**, **VacationSettlementEndpoints**, the **people picker** — all then operate on exact membership.)
- `ApprovalEndpoints` pending/monthly pickers: delete ONLY the `ORG_AND_DESCENDANTS` path-prefix arm; the **GLOBAL arm KEEPS** `GetPendingByOrgPathAsync("/")` / `GetByMonthAndOrgPathAsync("/", …)` (it is NOT dormant).
- `ReportingLineEndpoints` inline vikar-eligibility: delete the prefix branch (exact-match only).
- `AdminEndpoints` grant/revoke: enum `{GLOBAL, ORG_ONLY, ORG_AND_DESCENDANTS}` → `{GLOBAL, ORG_ONLY}`; **+ reject an ORG_ONLY grant whose `org_id` resolves to a non-ORGANISATION org-type (a MAO)** — OQ1.
- `init.sql`: `scope_type` CHECK → `('GLOBAL','ORG_ONLY')`; **convert the 7 seed ORG_AND_DESCENDANTS rows**: the 2 MAO-rooted HR rows EXPAND (`hr01→MIN01` → `{STY01,STY02,STY03}`; `hr02→MIN02` → `{STY04,STY05}`), the 5 Organisation-rooted rows (ladm01, ladm02, mgr01, mgr02, mgr03) convert 1:1 to ORG_ONLY. db-schema regen.
- Demo seed (`DemoGenerator.cs` + regenerated `99-demo-seed.sql` + `DemoVerifier.cs`): convert all 457 ORG_AND_DESCENDANTS rows **1:1 to ORG_ONLY** (the post-S92 generator already roots every HR/leader scope at the ORGANISATION — there is NO MAO-rooted demo scope to expand).
- Frontend: `jwt.ts` union → `'GLOBAL' | 'ORG_ONLY'`; `RoleManagement.tsx` remove the option + dead badge; `frontend/src/lib/__tests__/jwt.test.ts:38` (constructs an ORG_AND_DESCENDANTS scope).
- Tests: convert the ~33 ORG_AND_DESCENDANTS files (incl. `RoleAssignment.cs:9` model comment); add RED-on-old.
- ADR-035 amendment (S93 slice landed) + docs.

**OUT (later sprints):**
- Flat **approval** (`CanApprove` edge OR HR/Admin-over-Organisation; same-Organisation vikar; retire REQUIRED-mode) → **S94**.
- **Retire** the tree machinery (`tree_root_org_id`, `ResolveTreeRootOrgIdAsync`, `ValidateSameTreeAsync`, `GetDescendantsAsync`, the advisory-lock re-key) → **S95**.
- **Org-structure-admin tier** (whether org create/move/delete is GlobalAdmin-only vs MAO-scopable) → the **Organisation admin page** sprint. S93 only rejects MAO *role-grants*; it does not change the org-create/edit endpoint policies.

## Entropy Scan Findings (Step 0a)

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | INDEX 51 entries (ADR-035 latest); paths resolve. |
| Pattern compliance spot-check | CLEAN | n/a at plan time. |
| Orphan detection | DEBT | After S93, `GetDescendantsAsync` loses its only production caller (formally retired with the tree machinery in S95). NOTE: the `…ByOrgPathAsync` repo methods do NOT go dormant — the GLOBAL picker arm keeps them. |
| Documentation drift | DEBT | `MEMORY.md` over budget (pre-existing). |
| Quality grade review | CLEAN | No domain grade change pending. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 architectural integrity; P7 access control; schema migration) |
| **External Codex** | invoked 2026-06-23 — cycle 1: 1 BLOCKER / 3 WARNING / 1 NOTE; **cycle 2: "Sound to plan" — no remaining BLOCKER** |
| **Internal Reviewer** | invoked 2026-06-23 — cycle 1: 0 BLOCKER / 4 WARNING / 4 NOTE; **cycle 2: "Sound to plan" — no remaining BLOCKER** |
| **BLOCKERs resolved before Step 1** | yes — OQ1 = REJECT a non-ORGANISATION org_id for ORG_ONLY grants (closes the MAO-structure-admin vector Codex caught; no seed actor affected; org-create policy untouched — org-structure-admin tier deferred to the Organisation-page sprint) |

### Findings (cycle 1)
_Codex:_
- **BLOCKER** — OQ1 "allow MAO ORG_ONLY (inert)" is false: a MAO-typed scope passes org-structure gates (`AdminEndpoints.cs:107` create-under-MAO, `:230` MAO-update) → confers MAO-structure admin. → **RESOLVED: OQ1 = REJECT a non-ORGANISATION org_id for ORG_ONLY grants** (TASK-9302; flat-model-correct; no seed actor affected; the org-create endpoint policy is untouched — the org-structure-admin tier is the Organisation-page sprint's decision).
- **WARNING** — `…ByOrgPathAsync` not dormant; the GLOBAL arm calls them with `"/"` (`ApprovalEndpoints.cs:680,779`). → **FIXED** (scope + entropy note corrected).
- **WARNING** — seed count: **7** ORG_AND_DESCENDANTS rows (2 MAO + 5 Org-rooted), not 9; expansion targets verified correct (MIN01⊃STY01/02/03, MIN02⊃STY04/05, `init.sql:843-849`). → **FIXED** (count corrected throughout).
- **WARNING** — RED-on-old overclaim: "Org-X doesn't cover sibling Y" is already green on old code; the true RED check is the MAO-expansion. → **FIXED** (TASK-9306 reframed).
- **NOTE** — `GetAccessibleOrgsAsync` consumers broader: `AuditEndpoints.cs:31`, `VacationSettlementEndpoints.cs:1067`, `AdminEndpoints.cs:2090`. → **FIXED** (named).

_Internal Reviewer:_ (no BLOCKER)
- **W1** — demo: no MAO-rooted scope exists (generator roots HR/leader at the ORGANISATION); all 457 convert 1:1, nothing to expand. → **FIXED** (TASK-9305 corrected).
- **W2** — `…ByOrgPathAsync` not dormant (= Codex W). → **FIXED**.
- **W3** — add `RoleAssignment.cs:9` (comment) + `jwt.test.ts:38` (executable FE test) to the named conversion list. → **FIXED**.
- **W4** — seed accounting (7 rows: 2 expand + 5 1:1), expansion targets correct (= Codex W). → **FIXED**.
- **NOTE 5** — name `S92OrgFlattenTests.cs:160-163` (the current "MAO ORG_AND_DESCENDANTS reaches descendants" assertion) as the canonical convert-to-coverage-identity site. → **FIXED** (TASK-9306).
- **NOTE 6** — add explicit no-widening assertion (hr01 ⇏ cross-MAO STY04/05; a MAO scope yields empty roster). → **FIXED**.
- **NOTE 7** — OQ1: a MAO scope IS inert for roster/employee reads but NOT for org-structure ops (aligns with Codex). → resolved via REJECT.
- **NOTE 8** — S85 CHECKs / audit-mapper / JWT enum-agnosticism all verified as claimed. → confirmed.

### Resolution
All cycle-1 findings incorporated (the single BLOCKER resolved by OQ1=REJECT). Cycle 2 verifies.

## Architectural Constraints Verified
- [x] P1 — Architectural integrity (role-scope mechanism simplified to exact-set; tree machinery untouched; `GetDescendantsAsync` dormant not deleted — both lenses confirmed)
- [x] P3 — Event sourcing (no event vocabulary change; grant/revoke events unchanged; the S85 escalation CHECKs RETAINED)
- [x] P7 — Security & access control (coverage-IDENTICAL to post-S92 via the MAO expansion; ORG_AND_DESCENDANTS rejected by CHECK + grant; MAO-typed ORG_ONLY grant rejected — OQ1; the stale-token CoversOrg fall-through closed via default-deny hardening — Step-7a; RED-on-old proves no narrowing/widening)
- [x] P8 — CI/CD (full greenfield reseed; 1068 regression green locally — 2 FAIL-002 testcontainer sheds cleared in isolation; CI confirmation pending)

## Task Log (planned decomposition)

### TASK-9301 — Core role-scope: drop the subtree branch
| Field | Value |
|-------|-------|
| **Agent** | Security & Compliance (extended into SharedKernel + Infrastructure, cross-domain authorized) |
| **Components** | `RoleScope.cs` (`CoversOrg` :11-19), `OrgScopeValidator.cs` (`GetAccessibleOrgsAsync` :388-422, the `GetDescendantsAsync` call :416) |
| **KB Refs** | ADR-035 (D4 S93), ADR-008, ADR-009 |

**Description**: `CoversOrg`: delete the `ORG_AND_DESCENDANTS` `StartsWith` branch (:15-16); leave GLOBAL + exact-equality. `GetAccessibleOrgsAsync`: replace the per-scope `GetDescendantsAsync` expansion (:404-419) with `accessibleOrgIds.Add(scope.OrgId)` (the role-floored exact set); GLOBAL short-circuit unchanged. The other 3 validators need NO change (they call `CoversOrg`). `GetDescendantsAsync` left in `OrganizationRepository` (now zero production callers; retired S95). Update the doc-comments in `RoleScope.cs:5,9` + `RoleAssignment.cs:9`.

**Validation Criteria**:
- [ ] `CoversOrg` has no ORG_AND_DESCENDANTS branch; Organisation X ⇏ a different Organisation Y.
- [ ] `GetAccessibleOrgsAsync` returns exactly the assigned set (no `GetDescendantsAsync`); role floor preserved; the downstream surfaces (`AuditEndpoints.cs:31`, `VacationSettlementEndpoints.cs:1067`, `AdminEndpoints.cs:2090` people picker) operate on exact membership.
- [ ] `dotnet build` clean.

---

### TASK-9302 — Endpoint consumers (pickers, vikar-eligibility, grant/revoke + OQ1 reject)
| Field | Value |
|-------|-------|
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | `ApprovalEndpoints.cs` (:677-697, :776-804), `ReportingLineEndpoints.cs` (:2820-2831, :2727 comment), `AdminEndpoints.cs` (grant :1649/:1659-1674, revoke :1847-1858) |
| **KB Refs** | ADR-035, S85 |

**Description**: ApprovalEndpoints pickers — delete ONLY the `else if (ScopeType == "ORG_AND_DESCENDANTS")` arm in each (lines ~682-688 + ~781-787); KEEP the GLOBAL arm (`GetPendingByOrgPathAsync("/")` / `GetByMonthAndOrgPathAsync("/")`) and the exact-org ORG_ONLY arm. ReportingLineEndpoints `EvaluateVikarCoverageInTxAsync` — delete the prefix `StartsWith` branch (:2820-2824), keep exact-match; update :2727 comment. AdminEndpoints grant — enum → `("GLOBAL" or "ORG_ONLY")`; **+ OQ1: for an ORG_ONLY grant, look up `org_id`'s org-type and REJECT (400) if it is not `ORGANISATION`** (a MAO is not an authority unit). Revoke — non-GLOBAL arm only handles ORG_ONLY. **The S85 shape + GLOBAL_ADMIN-requires-GLOBAL CHECKs/gates STAY.**

**Validation Criteria**:
- [ ] No `ORG_AND_DESCENDANTS` literal in executable backend code (grep gate).
- [ ] grant rejects ORG_AND_DESCENDANTS (400) AND rejects an ORG_ONLY grant on a MAO org_id (400); accepts GLOBAL + ORG_ONLY-on-an-ORGANISATION; S85 shape gates intact.
- [ ] pickers (GLOBAL + exact-org) + vikar-eligibility (exact) correct.

---

### TASK-9303 — Schema CHECK + seed conversion/expansion (init.sql)
| Field | Value |
|-------|-------|
| **Agent** | Data Model (extended into init.sql, cross-domain authorized — Orchestrator-approved) |
| **Components** | `role_assignments` CHECK (:655), seed (:917-937 + comment :905-916), db-schema doc |
| **KB Refs** | ADR-035, S85 |

**Description**: CHECK → `scope_type IN ('GLOBAL','ORG_ONLY')`. Convert the **7** seed ORG_AND_DESCENDANTS rows: **EXPAND** the 2 MAO-rooted HR rows — `hr01→MIN01` → 3 rows `hr01→{STY01,STY02,STY03}` ORG_ONLY; `hr02→MIN02` → 2 rows `hr02→{STY04,STY05}` ORG_ONLY (preserves exact post-S92 coverage); convert the **5** Organisation-rooted rows (ladm01→STY02, ladm02→STY05, mgr01→STY02, mgr02→STY05, mgr03→STY01) 1:1 to ORG_ONLY. Keep the S85 CHECKs. Update the seed comment. Regenerate `docs/generated/db-schema.md`.

**Validation Criteria**:
- [ ] CHECK forbids ORG_AND_DESCENDANTS; no seed row carries it; every non-GLOBAL row's org_id is an ORGANISATION.
- [ ] `hr01` covers exactly {STY01,STY02,STY03}; `hr02` exactly {STY04,STY05} (MAO expansion = no coverage change).
- [ ] `check_docs.py` passes.

---

### TASK-9304 — Frontend: scope-type union + picker
| Field | Value |
|-------|-------|
| **Agent** | UX |
| **Components** | `frontend/src/lib/jwt.ts` (:14), `frontend/src/pages/admin/RoleManagement.tsx` (:30, :44), `frontend/src/lib/__tests__/jwt.test.ts:38` |
| **KB Refs** | ADR-011 |

**Description**: `jwt.ts` union → `'GLOBAL' | 'ORG_ONLY'`. `RoleManagement.tsx` remove the ORG_AND_DESCENDANTS option + dead badge case. Update `jwt.test.ts:38` (it constructs an ORG_AND_DESCENDANTS scope). Grep `frontend/src` for any other reference.

**Validation Criteria**:
- [ ] No ORG_AND_DESCENDANTS in the FE; picker offers GLOBAL + Organisation-only; FE build + vitest green.

---

### TASK-9305 — Demo seed regeneration
| Field | Value |
|-------|-------|
| **Agent** | Tooling/DemoSeed (cross-domain authorized) |
| **Components** | `DemoGenerator.cs` (:203 HR scope, :209 leader scope), regenerated `99-demo-seed.sql` (457 rows), `DemoVerifier.cs` |
| **KB Refs** | S84 |

**Description**: `DemoGenerator.cs` emits ORG_ONLY role assignments. **All 457 demo ORG_AND_DESCENDANTS rows convert 1:1 to ORG_ONLY** — the post-S92 generator already roots every HR/leader scope at the ORGANISATION (`OrgId = root`/`m.PrimaryOrgId`, both Organisations); there is NO MAO-rooted demo scope to expand. Regenerate `99-demo-seed.sql`. `DemoVerifier` — assert zero ORG_AND_DESCENDANTS; demo HR/leader reaches its Organisation.

**Validation Criteria**:
- [ ] Regenerated demo SQL has zero ORG_AND_DESCENDANTS; every demo role row is GLOBAL or ORG_ONLY-on-an-ORGANISATION; build + verifier green.

---

### TASK-9306 — Test conversion + RED-on-old
| Field | Value |
|-------|-------|
| **Agent** | Test & QA |
| **Components** | the ~33 ORG_AND_DESCENDANTS test files (heavy: `Sprint7ScopeTests` CoversOrg unit cases, grant/revoke endpoint tests, `S92OrgFlattenTests.cs:160-163`, the approval/vikar suites) |
| **KB Refs** | FAIL-002, PAT-008 |

**Description**: Convert every ORG_AND_DESCENDANTS test seed/assertion (grep gate, not a fixed count). Rewrite the `CoversOrg` unit cases (drop subtree). **`S92OrgFlattenTests.cs:160-163` currently asserts hr01's MAO ORG_AND_DESCENDANTS reaches the Organisations under MIN01 — this is the canonical site: convert it to assert the EXPANDED explicit set `{STY01,STY02,STY03}` reaches the SAME orgs (the coverage-identity RED-on-old). NOTE: the suite currently only defines STY01/STY02/STY05 constants (`:48-52`) — ADD an STY03 constant + present-assertion so "exactly {STY01,STY02,STY03}" is proven, not the weaker {STY01,STY02}.** Add: (a) the CHECK + the grant endpoint REJECT ORG_AND_DESCENDANTS (400); (b) **the grant endpoint REJECTS an ORG_ONLY grant on a MAO org_id (400)** — OQ1; (c) a multi-row {X,Y} ORG_ONLY assignment covers both; (d) **coverage-identity (the true RED-on-old)**: `hr01`-expanded reaches exactly {STY01,STY02,STY03}, `hr02`-expanded exactly {STY04,STY05}; (e) **no-widening**: hr01 does NOT reach cross-MAO STY04/STY05, and a (rejected-at-grant, but if force-seeded) MAO-typed scope yields an EMPTY roster from the people picker; (f) `GetAccessibleOrgsAsync` returns the exact assigned set (no descendants). Run the full pyramid per FAIL-002.

**Validation Criteria**:
- [ ] All suites green; the RED-on-old assertions (a)-(f) present + passing; no ORG_AND_DESCENDANTS literal remains in executable tests.

---

### TASK-9307 — ADR-035 amendment + docs
| Field | Value |
|-------|-------|
| **Agent** | Orchestrator |
| **Components** | `ADR-035` (S93 slice status + OQ1), INDEX, db-schema, SECURITY.md |
| **KB Refs** | amends ADR-008/009 |

**Description**: Amend ADR-035 D4/D5 — flat-role-scope slice LANDED (S93): `ORG_AND_DESCENDANTS` removed, coverage = exact Organisation-set, `GetDescendantsAsync`/`materialized_path` dormant-pending-S95, the MAO-expansion migration, **OQ1 = ORG_ONLY grants restricted to ORGANISATION org_ids (MAO rejected); the org-structure-admin tier is the Organisation-page sprint's decision**. Update SECURITY.md (scope model = explicit org-sets). ADR-027 disposition table unaffected.

**Validation Criteria**:
- [ ] ADR-035 amended; SECURITY.md updated; check_docs passes.

## Risks & Conflicts
- **MAO-rooted coverage loss (HIGH, P7).** The 2 init.sql HR rows MUST expand (not 1:1) or HR silently loses coverage of every Organisation beneath. Mitigation: TASK-9303 EXPAND; TASK-9306(d) coverage-identity. (Demo has none to expand — W1.)
- **MAO-structure-admin vector (P7, resolved).** A MAO-typed ORG_ONLY scope confers org-structure admin (Codex BLOCKER). Resolved: grant rejects non-ORGANISATION org_ids (OQ1). The org-create endpoint policy is unchanged (the broader org-structure-admin tier = Organisation-page sprint).
- **Coverage-identity is the proof (P7).** S93 must be coverage-IDENTICAL to post-S92 (mechanism-only) modulo the new MAO-grant rejection; RED-on-old pins no narrowing/widening.
- **`GetAccessibleOrgsAsync` semantic change (MEDIUM).** Audit/settlement/people-picker now get the exact set; verify they expect exact membership (all keyed on `accessibleOrgIds` ∩ `primary_org_id`, no user is MAO-homed → inert). Test.
- **S85 escalation CHECKs must stay.**
- **review-lens-complementarity**: confirmed — Codex caught the MAO-structure vector the internal lens cleared. Dual-lens Step-7a with an adversarial "find the silent narrowing / the MAO-scope authority" prompt.

## Execution Outcome
All 7 tasks complete (4 slice agents — Backend 9301/9302/9303, Frontend 9304, DemoSeed 9305, Test 9306 — + ADR-035 amendment 9307 by the Orchestrator). Combined tree builds 0/0.

**Step-7a hardening (Orchestrator, post-review):** Codex caught a defense-in-depth gap the internal lens cleared — `RoleScope.CoversOrg` fell through to exact-match for ANY non-GLOBAL scope_type, so a stale pre-S93 JWT carrying `ORG_AND_DESCENDANTS@MIN01` would exact-match `/MIN01/` and pass the org-structure gates, bypassing the OQ1 grant-time guard for the token lifetime. Fixed: `CoversOrg` + `GetAccessibleOrgsAsync` now DEFAULT-DENY any non-`ORG_ONLY` non-GLOBAL type; RED-on-old unit test `StaleRemovedScopeType_IsDefaultDenied_NotExactMatched`. Codex cycle-2 "Resolved — clean."

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex + internal Reviewer) |
| **Sprint-start commit** | `73171cbed5e545922799fb47110ee0767772d9af` |
| **Review Cycles** | Codex 2 (1 BLOCKER → fix → clean); Reviewer 1 (clean) |
| **Findings** | 1 BLOCKER (resolved), 0 WARNING, 2 NOTE (minor: dead DI param; the documented MAO-create-is-GlobalAdmin consequence) |
| **Resolution** | BLOCKER fixed (the stale-token CoversOrg default-deny hardening); Codex cycle-2 clean |

### Findings
- **Codex** cycle 1: BLOCKER — stale-token `CoversOrg` fall-through bypasses OQ1 → fixed (default-deny) → cycle 2 "Resolved — clean."
- **Internal Reviewer**: No BLOCKER/WARNING. Confirmed coverage-IDENTITY (the hr01/hr02 MAO-expansion = exactly the Organisations under each MAO; no narrowing/widening), OQ1 non-bypassable, the actor-org-vs-scope test decoupling FAITHFUL, tree machinery untouched. 2 minor NOTEs.
- Artifacts: `.claude/reviews/SPRINT-93-step7a-{codex,reviewer}.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 854 | all passing (incl. the new CoversOrg default-deny hardening test) |
| Regression | 1068 | all passing (local, fresh-greenfield; 1066 first run + 2 FAIL-002 testcontainer sheds cleared 15/15 in isolation) |
| Smoke | 6 | all passing |
| DemoSeed | 29 | all passing |
| Frontend (vitest) | 495 | all passing |
| **Total** | **2452** | CI GREEN `28011098005` (all 7 jobs) |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 7 |
| Constraint Violations | 0 |
| Reviewer Findings | Step-0b: 1 BLOCKER (Codex) + ~8 W/N (all resolved pre-code); Step-7a: 1 BLOCKER (Codex, fixed) / 2 NOTE |
| External Review Cycles | Step-0b: 2 ("sound to plan"); Step-7a: Codex 2 (clean), Reviewer 1 (clean) |
| Re-dispatches | 0 (1 Orchestrator-side Step-7a hardening) |
| First-Pass Rate | 7/7 = 100% (no agent re-dispatch) |

## Sprint Retrospective
**What went well**: [[review-lens-complementarity]] decisive TWICE — Step-0b Codex caught the MAO-typed-scope org-structure vector the internal lens cleared (→ OQ1 reject); Step-7a Codex caught the stale-token CoversOrg fall-through the internal lens cleared (→ default-deny hardening). The coverage-identity framing (mechanism-only, MAO-expansion-preserves-coverage) held cleanly. 100% first-pass.
**What to improve**: the plan's first cut mis-stated the seed count (9 vs 7) and the demo expansion (there was none) — both caught at Step-0b; a `grep ORG_AND_DESCENDANTS` census at plan time (rather than carrying the S92-era count) would have been exact. The OQ1 "MAO scope is inert" lean was wrong (Codex); inert-for-rosters ≠ inert-for-org-structure.
**Knowledge produced**: ADR-035 S93 amendment (flat role-scope landed + OQ1).
