# Sprint 95 — Retire the tree-walk machinery (same-tree → same-Organisation; the lock domain is the Organisation)

| Field | Value |
|-------|-------|
| **Sprint** | 95 |
| **Status** | complete |
| **Start Date** | 2026-06-23 |
| **End Date** | 2026-06-23 |
| **Orchestrator Approved** | yes — 2026-06-23 |
| **Build Verified** | yes — `dotnet build` 0 errors (combined tree) |
| **Test Verified** | yes (local, fresh-greenfield Postgres): 850 unit + 1070 regression + 6 smoke + 29 demoseed + 492 fe; CI-pending (push-triggered full pyramid confirmation; backfilled at close-polish) |

> **v2 (post Step-0b dual-lens).** Reframed the equivalence to the always-true `tree_root == primary_org` (not "primary_org IS an Organisation"); **KEEP the `tree_root_org_id` columns** (they have many load-bearing readers — drop was unsafe), populate from `primary_org` directly; added the Organisation-home guard + the missed callers. The lock-equivalence core was VALIDATED by both lenses. See Plan Review.

## Sprint Goal
ADR-035 slice 4 (the FINAL flat-authority phase). Retire the tree-**walk** machinery: delete `ResolveTreeRootOrgIdAsync` (the recursive CTE) + the dead `ResolveEmployeeTreeRootInTxAsync` + the long-dormant `GetDescendantsAsync`, and re-derive every "tree root" from the user's `primary_org_id` directly. **The advisory lock DOMAIN is UNCHANGED — it stays the Organisation.**

**The equivalence (both-lens-validated):** `ResolveTreeRootOrgIdAsync` walks up `parent_org_id` and stops at the first `org_type IN ('MAO','ORGANISATION')`; since BOTH permitted types are terminal, the walk **always returns the input org itself (depth 1)** — so for any user, `tree_root == primary_org_id`, ALWAYS (regardless of whether the home is an Organisation or, edge-case, a MAO). Therefore re-deriving the advisory key from `primary_org_id` directly is provably identical to the CTE result, and the existing `reporting-tree-{tree_root}` advisory IS already keyed on the user's home org (an Organisation under the new guard; a MAO for any legacy raw-seeded edge case — still lock-equivalent). We keep that lock, change only its DERIVATION (read `primary_org`, no walk) + the same-tree abstraction (`ValidateSameTreeAsync` → a direct `primary_org` equality = same-Organisation). This **preserves every S78/S83 guarantee** (cycle-guard 2-/≥3-node, approve-vs-revoke, transfer two-key, vikar-affects-all-reports, the revoke anchor) — there is NO per-subject lock-matrix and NO new ≥3-node proof needed (the single Organisation lock serializes the whole component, exactly as the tree-root lock did; cross-Organisation edges are rejected pre-cycle-check, so no cycle spans Organisations). The drift-guard STAYS (the Organisation membership is mutable via transfer). Supersedes the refinement's per-subject lock-matrix premise (it was an artifact of the rejected "remove the org-level domain" framing).

## Scope (in / out)

**IN:**
- **Delete the walk**: `ResolveTreeRootOrgIdAsync` (both overloads), the dead `ResolveEmployeeTreeRootInTxAsync` (`ReportingLineEndpoints.cs:2809`, replaced/unused), `GetDescendantsAsync` (dormant since S93). Re-derive every former caller from `primary_org_id` directly.
- **Same-Organisation validation**: `ValidateSameTreeAsync` → `ValidateSameOrganisationAsync` — read both users' `primary_org_id`, compare equality (KEEP the dual-row `FOR UPDATE` id-sorted pin), throw `CrossOrganisationAssignmentException` on mismatch, return the common `primary_org` (the Organisation). All 8 callers migrate.
- **Re-key the advisory derivation**: the helpers (`AcquireTreeLockForEmployeeAsync`/`…ForTransferAsync`/`AcquireRevokeTreeLocksAsync`/`DeriveEmployeeTreeRootInTxAsync`/`AcquireTreeLockAsync`) derive the key from `primary_org_id` directly (no CTE); rename the lock prefix `reporting-tree-`→`reporting-org-` + the helper names `…TreeLock…`→`…OrgLock…`. The **drift-guard** (derive→acquire→re-derive→retry) STAYS verbatim (primary_org is mutable). The **transfer two-key** (OLD ∪ NEW Organisation) + the **revoke anchor** (persisted ∪ current) STAY. `GuardNoCycleAsync` UNCHANGED (runs under the held Organisation advisory).
- **KEEP the `tree_root_org_id` columns** (`reporting_lines` + `manager_vikar`) — they have MANY load-bearing readers (`GetTreeAsync` → the live `GET /api/admin/reporting-lines/tree/{root}` endpoint; the R10 delete-with-reassignment root-invariant; the `ReportingLine` model `required` field; the `ReportingLineAssigned/Superseded/BulkImported` events + 6 audit mappers; `EmploymentEndDateLifecycleWriter`/`SettlementCloseService`/`DelegationExpiryService`). Populate `reporting_lines.tree_root_org_id` from the employee's `primary_org` directly (= `ValidateSameOrganisationAsync`'s return — the same create-time Organisation it held before). KEEP `manager_vikar`'s persisted Organisation anchor (the S83 D19 revoke-safety). **Column rename `tree_root_org_id`→`organisation_id` is DEFERRED** to a cosmetic follow-up (it hits events/audit/model/FE — not worth the churn in the concurrency-critical sprint); document the semantic (the column holds the Organisation).
- **The predicate** (`DesignatedApproverAuthorizer.cs:112`): the same-tree re-check → `ValidateSameOrganisationAsync` (direct same-Organisation; defense-in-depth preserved — a planted cross-Organisation edge still denied).
- **`FallbackTraversalWarning`**: populate `TreeRootOrgId` from `period.OrgId` directly (delete the `ResolveTreeRootOrgIdAsync(period.OrgId)` call); KEEP the field name (no event-shape rename — minimal churn).
- **NEW — Organisation-home guard (Codex):** user create + transfer reject a `primary_org_id` whose `org_type` is not `ORGANISATION` (a MAO holds no employees per the flat model). Makes "users sit on Organisations" by-construction (mirrors the S93 OQ1 MAO-grant-reject). The seed already complies (users on STY0x).
- The bulk-import `:891` caller migrates (the batch `request.TreeRootOrgId` request-contract field now means "the Organisation" — by construction every row is single-Organisation; document).
- init.sql (the Organisation-home guard; no column drop) + db-schema regen; demo seed; tests; ADR-035 amendment (reform COMPLETE) + ADR-027 D2/D9 superseded + D15/D18/D19 re-keyed + SECURITY.md.

**OUT:** the column rename (cosmetic follow-up); the redesigned Organisation admin **page** (the next major non-reform piece).

## Entropy Scan Findings (Step 0a)

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | INDEX 51 entries (ADR-035 latest). |
| Pattern compliance spot-check | CLEAN | n/a at plan time. |
| Orphan detection | CLEAN | `GetDescendantsAsync` (dormant since S93) deleted this sprint; the dead `ResolveEmployeeTreeRootInTxAsync` deleted. |
| Documentation drift | DEBT | `MEMORY.md` over budget (pre-existing). |
| Quality grade review | CLEAN | No domain grade change pending. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1; P4 concurrency; P7; schema; **concurrency-critical — extra adversarial review**) |
| **External Codex** | invoked 2026-06-23 — cycle 1: 4 BLOCKER / 1 WARNING / 2 NOTE; **cycle 2: "Sound to plan"** |
| **Internal Reviewer** | invoked 2026-06-23 — cycle 1: 1 BLOCKER / 1 WARNING / 4 NOTE; **cycle 2: "Sound to plan"** (lock-equivalence re-confirmed byte-identical; all readers preserved) |
| **BLOCKERs resolved before Step 1** | yes — (1) the equivalence downgraded to the always-true `tree_root == primary_org` + the Organisation-home guard added; (2) the `tree_root_org_id` columns KEPT (no drop — many load-bearing readers), populated from `primary_org` directly; (3) the missed bulk-import + dead-helper callers added |

### Findings (cycle 1)
- **The lock-equivalence core VALIDATED by both lenses** — `tree_root == primary_org` always; all 5 serialization guarantees preserved; the per-subject matrix correctly superseded; the drift-guard correctly kept. (Both lenses' NOTEs.)
- **BLOCKER (Codex ×3) — the "primary_org IS an Organisation" framing.** No `org_type='ORGANISATION'` CHECK on `users.primary_org_id`; create/transfer don't guard org-type → a user CAN be planted on a MAO. → **RESOLVED:** (1) the equivalence DOWNGRADED to the always-true `tree_root == primary_org` (the lock is correct even for a MAO-homed user — the Reviewer confirmed); (2) ADD the Organisation-home guard (create + transfer reject non-ORGANISATION primary_org) to make the model invariant by-construction.
- **BLOCKER (Codex + Reviewer) — `reporting_lines.tree_root_org_id` has many load-bearing readers** (`GetTreeAsync` live endpoint, R10 root-invariant, the model `required` field + events + 6 audit mappers + 3 lifecycle services). OQ1's "drop it" was unsafe. → **RESOLVED: KEEP the column**, populate from `primary_org` directly; defer the rename. (Materially lower-risk.)
- **WARNING (Reviewer) — TASK-9502 missed two callers**: bulk-import `:891` (`request.TreeRootOrgId` = the Organisation by construction) + the dead `ResolveEmployeeTreeRootInTxAsync:2809`. → **FIXED** (added to TASK-9501/9502).
- **NOTE (both) — the FallbackTraversalWarning rename** is replay-safe but unnecessary → **KEEP the field name**; just change the source to `period.OrgId`.

### Resolution
All cycle-1 BLOCKERs resolved by: downgrading the equivalence claim + the Organisation-home guard; keeping the columns (no drop). Cycle 2 verifies.

## Architectural Constraints Verified
- [x] P1 — Architectural integrity (the tree-WALK + abstraction retired; the lock DOMAIN unchanged = the Organisation; the columns kept as denormalized Organisation storage)
- [x] P4 — Concurrency/version (THE CRITICAL ONE: the Organisation advisory byte-identical — `hashtext('reporting-tree-' || primary_org)`; drift-guard + cycle-guard + transfer two-key + revoke anchor all preserved; the held-lock interleave test PROVES it; both lenses confirmed)
- [x] P3 — Event sourcing (no event-shape change — `TreeRootOrgId` field kept; populated from `primary_org`/`period.OrgId`)
- [x] P7 — Security & access control (same-Organisation containment preserved exactly; cross-Organisation edge/vikar still rejected; the Organisation-home guard added; the inactive-org-home NOTE accepted+documented as unreachable)
- [x] P8 — CI/CD (greenfield reseed; 1070 regression green locally — 2 new-test precondition-header bugs fixed; CI confirmation pending)

## Task Log (planned decomposition)

### TASK-9501 — Delete the walk + re-key the advisory derivation to primary_org (Infrastructure)
| Field | Value |
|-------|-------|
| **Agent** | Security & Compliance + Infrastructure (cross-domain authorized) |
| **Components** | `ReportingLineRepository.cs` (DELETE `ResolveTreeRootOrgIdAsync` + `GetDescendantsAsync`; `ValidateSameTreeAsync`→`ValidateSameOrganisationAsync` [direct primary_org]; `CrossTreeAssignmentException`→`CrossOrganisationAssignmentException`; the lock helpers → derive from primary_org + rename `…Org…`/`reporting-org-`; `GuardNoCycleAsync` UNCHANGED; populate `reporting_lines.tree_root_org_id` from primary_org in `InsertLineAsync`/`AssignAsync`); `ReportingLineEndpoints.cs:2809` (DELETE dead `ResolveEmployeeTreeRootInTxAsync`) |
| **KB Refs** | ADR-035, ADR-027 D2/D9 (superseded), D15/D18/D19 (re-keyed) |

**Description**: Delete `ResolveTreeRootOrgIdAsync` (walk) + `GetDescendantsAsync` (dead) + the dead `ResolveEmployeeTreeRootInTxAsync`. `ValidateSameOrganisationAsync`: read both users' `primary_org_id` (pin both `FOR UPDATE` id-sorted — KEEP), compare, throw `CrossOrganisationAssignmentException` on mismatch, return the common `primary_org`. The advisory helpers derive the key from `primary_org` directly; prefix `reporting-tree-`→`reporting-org-`; drift-guard + transfer two-key + revoke anchor preserved verbatim. KEEP populating `reporting_lines.tree_root_org_id` (= the employee's primary_org at write — same value as before, derived directly).

**Validation Criteria**:
- [ ] No `ResolveTreeRootOrgIdAsync`/`GetDescendantsAsync`/`ValidateSameTreeAsync`/`CrossTreeAssignment` symbol remains (grep gate); the advisory derives from `primary_org` (no CTE); drift-guard/transfer/revoke preserved.
- [ ] `dotnet build` clean.

---

### TASK-9502 — Migrate the callers (predicate + write paths + bulk-import + warning) + the Organisation-home guard
| Field | Value |
|-------|-------|
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | `DesignatedApproverAuthorizer.cs:112`; `ReportingLineEndpoints.cs` (assign :97/:578, bulk-import :891+:1015, R10 :1307, self-delegate :1803, admin-vikar :2301); `AdminEndpoints.cs:756` (create-with-approver) + `:1063` (transfer) + the user create/transfer Organisation-home guard; `ApprovalEndpoints.cs:287/:461` (`FallbackTraversalWarning` → `period.OrgId`) |
| **KB Refs** | ADR-035, ADR-027 D13 |

**Description**: Replace every `ValidateSameTreeAsync` → `ValidateSameOrganisationAsync`. The bulk-import re-derives from `primary_org` (the `request.TreeRootOrgId` batch field now = the Organisation — by construction single-Organisation). `FallbackTraversalWarning.TreeRootOrgId` ← `period.OrgId` (field name kept). **ADD the Organisation-home guard**: user create (`AdminEndpoints` POST) + transfer (`PUT /users/{id}`) reject a `primary_org_id` whose `org_type != 'ORGANISATION'` (400) — load the org, check the type (mirrors the S93 grant guard).

**Validation Criteria**:
- [ ] No executable `ValidateSameTreeAsync`/`ResolveTreeRootOrgIdAsync` caller remains.
- [ ] The predicate still DENIES a cross-Organisation edge (RED-on-old).
- [ ] User create/transfer reject a MAO `primary_org` (400); accept an ORGANISATION.

---

### TASK-9503 — Schema: the Organisation-home guard (NO column drop)
| Field | Value |
|-------|-------|
| **Agent** | Data Model (extended into init.sql, cross-domain authorized) |
| **Components** | `init.sql` (the `users.primary_org_id` Organisation-home enforcement note; the seed already complies — verify), db-schema doc |
| **KB Refs** | ADR-035 |

**Description**: NO table/column DROP this sprint (the `tree_root_org_id` columns stay as denormalized Organisation storage; the readers are preserved). Verify the seed users all sit on ORGANISATIONs (they do post-S92). The Organisation-home guard is application-level (TASK-9502) — a DB CHECK can't reference `organizations.org_type` from `users` without a trigger; document the invariant. Regenerate db-schema only if any column changed (it didn't — no-op unless a comment-level change).

**Validation Criteria**:
- [ ] No `tree_root_org_id` column dropped; seed users all on ORGANISATIONs; check_docs passes.

---

### TASK-9504 — Frontend (verification / minimal)
| Field | Value |
|-------|-------|
| **Agent** | UX |
| **Components** | `frontend/src` — any `treeRoot`/`tree_root`/`TreeRootOrgId` reference |
| **KB Refs** | ADR-011 |

**Description**: Grep `frontend/src` for `treeRoot`/`tree_root`. The `tree/{treeRootOrgId}` tree-view endpoint + the `TreeRootOrgId` payload field are KEPT (the column stays), so likely a no-op. Confirm no FE breakage from the backend changes (the tree-view endpoint still returns the same shape).

**Validation Criteria**:
- [ ] FE build + vitest green; the tree-view page (if any) still works.

---

### TASK-9505 — Demo seed (verification)
| Field | Value |
|-------|-------|
| **Agent** | Tooling/DemoSeed (cross-domain authorized) |
| **Components** | `DemoGenerator.cs`/`SqlEmitter.cs`, `99-demo-seed.sql`, `DemoVerifier.cs` |
| **KB Refs** | S84 |

**Description**: The `tree_root_org_id` columns stay, so the demo emission is largely unchanged. Verify the demo users sit on ORGANISATIONs (they do — post-S92). Confirm `DemoVerifier`'s tree-root assertions still hold (the column is populated from primary_org = the Organisation). Regenerate only if needed. Largely a no-op verification.

**Validation Criteria**:
- [ ] Demo users on ORGANISATIONs; `tree_root_org_id` = the Organisation; build + verifier green.

---

### TASK-9506 — Tests: the concurrency suites + RED-on-old
| Field | Value |
|-------|-------|
| **Agent** | Test & QA |
| **Components** | `ApprovalConcurrencyHardeningTests`, `ReportingLineWriteLifecycleTests`, `ReportingLineRepositoryTests` (incl. the `ResolveTreeRootOrgIdAsync — MIN01→MIN01`-style cases — DELETE/migrate), `ManagerVikarEngineTests`, `AdminVikarOnBehalfTests` + any `ValidateSameTree`/`CrossTreeAssignment`/`ResolveTreeRoot`/`tree_root` test |
| **KB Refs** | FAIL-002, PAT-008 |

**Description**: Migrate every `ValidateSameTree`/`CrossTreeAssignment`/`ResolveTreeRoot` test reference to the `…Organisation…` forms; DELETE the `ResolveTreeRootOrgIdAsync` unit tests (the walk is gone). Add/keep the concurrency interleave + RED-on-old: (a) a cross-Organisation assign/admin-vikar is REJECTED (`CrossOrganisationAssignmentException`/422); (b) the Organisation advisory serializes a concurrent first-assign 2-cycle (cycle guard fires — KEEP the S78 tests, re-pointed); (c) approve vs concurrent revoke serializes (S78 R1, under the Organisation advisory); (d) transfer serializes against assigns in both Organisations; (e) the vikar revoke keys on the persisted Organisation anchor after a manager transfer (S83); (f) user create/transfer reject a MAO `primary_org` (the new guard). Run the full pyramid per FAIL-002 (the Postgres-coupled classes need compose Postgres up).

**Validation Criteria**:
- [ ] All suites green; interleave (a)-(f) present + passing; no `ValidateSameTree`/`ResolveTreeRoot` literal remains in executable tests.

---

### TASK-9507 — ADR-035 amendment + docs (the reform is COMPLETE)
| Field | Value |
|-------|-------|
| **Agent** | Orchestrator |
| **Components** | `ADR-035` (S95 slice + the FULL D-disposition), INDEX, db-schema, SECURITY.md, ADR-027 |
| **KB Refs** | supersedes ADR-027 D2/D9; re-keys D15/D18/D19 |

**Description**: Amend ADR-035 — the tree-walk machinery RETIRED; the lock domain is the Organisation (re-derived from `primary_org`; the existing serialization preserved — record the design rationale that superseded the per-subject premise + the `tree_root == primary_org` proof). The `tree_root_org_id` columns KEPT as denormalized Organisation storage (rename deferred). The Organisation-home guard added. Mark ADR-027 D2/D9 **SUPERSEDED** + D15/D18/D19 **RE-KEYED to the Organisation** in the disposition table. **The flat-authority reform (S92–S95) is COMPLETE.** Update SECURITY.md (the `reporting-tree-`→`reporting-org-` lock, same domain). Note: column rename + the Organisation admin page are the recorded follow-ups.

**Validation Criteria**:
- [ ] ADR-035 amended (reform complete); ADR-027 D2/D9 superseded + D15/D18/D19 re-keyed; SECURITY.md updated; check_docs passes.

## Risks & Conflicts
- **Concurrency equivalence (HIGHEST, P4 — both-lens-VALIDATED).** `tree_root == primary_org` always (proven: both org-types terminal in the walk). The Organisation advisory (re-derived from primary_org) preserves the exact serialization; the cycle-guard/vikar-fan-out/transfer/revoke are all covered by the single Organisation lock (all affected users same-Org). The per-subject matrix is correctly superseded. Step-7a re-confirms with interleave tests.
- **Column KEPT (de-risked).** No drop → no reader blast radius (`GetTreeAsync`/R10/events/audit/lifecycle all keep working). The column now holds the Organisation (populated from primary_org directly).
- **Organisation-home guard (P7, new).** A small behavior addition (the user create POST + transfer PUT reject a MAO `primary_org`) — **app-level only** (a DB CHECK can't cross-reference `organizations.org_type` from `users`), so RAW-SQL seeds bypass it by design. The init.sql seed complies (users on STY0x). NOTE: `AdminVikarOnBehalfTests` deliberately raw-seeds an admin on MIN01 (a MAO) for a cross-org audit-discriminator fixture — that is EXPECTED + does not break (raw INSERT, never the guarded endpoint); `Sprint6SecurityTests` MIN01 fixtures are event-serialization-only (never persisted). TASK-9506(f) must exercise the guard via the **endpoint**, not raw SQL.
- **Naming residue.** `tree_root_org_id` columns + `FallbackTraversalWarning.TreeRootOrgId` keep their names (cosmetic-misnomer; they hold the Organisation) — the rename is a recorded low-risk follow-up to avoid event/audit/model churn in the concurrency-critical sprint.
- **review-lens-complementarity (concurrency-critical)**: Step-7a dual-lens with the SAME adversarial prompt ("prove the Organisation lock is not weaker; find a breaking interleaving; find a cross-Organisation edge that slips through").

## Execution Outcome
All 7 tasks complete (Backend 9501/9502/9503, Tests 9506, ADR 9507; **TASK-9504 (FE) + TASK-9505 (Demo) were verified no-ops** — the `treeRootOrgId` FE references are to the KEPT tree-view endpoint/column, and the demo's `tree_root_org_id` is repo-populated from `primary_org` transparently). Combined tree builds 0/0; schema verified (65 tables unchanged, `tree_root_org_id` columns KEPT, users on ORGANISATIONs).

**Two in-flight test fixes** (Orchestrator-dispatched, post-central-run; both TEST bugs in the new `S95FlatOrgLockTests`, NOT a lock break — the existing S78 concurrency suites all passed): the new cross-Org-reject + 2-cycle-interleave tests fired assign POSTs WITHOUT the optimistic-concurrency precondition header → the endpoint 428'd before reaching the lock/cross-Org check. Fixed by adding `If-None-Match: *` (first-assign) / `If-Match: <version>` (reassign) — all 10 `S95FlatOrgLockTests` pass in isolation, incl. the held-lock interleave that POSITIVELY proves the assign blocks on the held Organisation advisory.

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex + internal Reviewer), adversarial-concurrency |
| **Sprint-start commit** | `e5e78f05161b3112752128cd2e12a988770aaea6` |
| **Review Cycles** | 1 (both lenses clean — no fix cycle) |
| **Findings** | 0 BLOCKER, 0 WARNING, 1 NOTE (the inactive-org-home gap — accepted+documented as unreachable) |
| **Resolution** | clean; the NOTE recorded as a follow-up condition |

### Findings
- **Codex**: "Clean — no findings." The advisory key byte-identical (`hashtext('reporting-tree-' || primary_org)`); drift-guard/transfer/revoke preserved; no broken interleaving; the home guard present; columns + event shape kept.
- **Internal Reviewer**: No BLOCKER/WARNING. The lock-equivalence verified rigorously (the held-lock interleave proves it). NOTE (P7): the new direct `primary_org` read drops the old `is_active=TRUE` check, so an assign against a *deactivated*-Organisation home would proceed where the walk 400'd — **effectively unreachable** (no org-deactivation path; the home guard keeps users on ORGANISATIONs; the lock key stays consistent) → accept+document; if org-deactivation is ever added, restore the `is_active` fail-closed.
- Artifacts: `.claude/reviews/SPRINT-95-step7a-{codex,reviewer}.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 850 | all passing |
| Regression | 1070 | all passing (local, fresh-greenfield; +`S95FlatOrgLockTests` 10 incl. the held-lock interleave proving the Organisation advisory serializes; 2 new-test precondition-header bugs fixed) |
| Smoke | 6 | all passing |
| DemoSeed | 29 | all passing |
| Frontend (vitest) | 492 | all passing |
| **Total** | **2447** | CI confirmation pending |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 7 (2 no-op: TASK-9504/9505) |
| Constraint Violations | 0 |
| Reviewer Findings | Step-0b: 4 BLOCKER (Codex) + 1 (Reviewer) — all resolved pre-code; Step-7a: 0 B / 0 W / 1 N (accepted) |
| External Review Cycles | Step-0b: 2 ("sound to plan"); Step-7a: 1 (both clean) |
| Re-dispatches | 1 (the 2 new-test precondition-header fixes) |
| First-Pass Rate | 7/7 = 100% (no domain-code re-dispatch; the test fix was a new-test bug, not a production defect) |

## Sprint Retrospective
**What went well**: the **pivotal insight** — post-S92 `tree_root == primary_org` (both org-types terminal in the walk), so the existing Organisation-scoped advisory IS the flat lock domain — superseded the refinement's per-subject lock-matrix + the ≥3-node proof, turning the "hardest" sprint into a derivation+rename refactor with byte-identical locking. [[review-lens-complementarity]] was decisive at Step-0b (Codex caught the unenforced "primary_org IS an Organisation" framing → the Organisation-home guard; both lenses caught that the column drop was unsafe → keep the columns). The held-lock interleave test POSITIVELY proves the equivalence. **The flat-authority reform (S92–S95) is COMPLETE.**
**What to improve**: the new concurrency test's assign POSTs omitted the precondition header (a 428-before-the-lock trap) — caught only at the central regression run; a new endpoint-hitting concurrency test should mirror the established `PostAssignAsync` helper from day one.
**Knowledge produced**: ADR-035 S95 amendment (the reform COMPLETE; ADR-027 D2/D9 superseded + D15/D18/D19 re-keyed). Recorded follow-ups: the cosmetic `tree_root`/`reporting-tree-`/`TreeRootOrgId`→Organisation rename; the inactive-org-home `is_active` restoration (if org-deactivation is ever added); the redesigned **Organisation admin page** (the next major non-reform piece).
