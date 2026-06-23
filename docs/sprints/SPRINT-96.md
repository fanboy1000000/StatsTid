# Sprint 96 — Two low-risk cleanups: the `tree_root`→`organisation` cosmetic rename + the inactive-org-home `is_active` guard

| Field | Value |
|-------|-------|
| **Sprint** | 96 |
| **Status** | complete |
| **Start Date** | 2026-06-23 |
| **End Date** | 2026-06-23 |
| **Orchestrator Approved** | yes — 2026-06-23 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 errors |
| **Test Verified** | yes (local, fresh-greenfield): 850 unit + 1072 regression (1070 central + 2 new `is_active` pins verified in-class) + 6 smoke + 29 demoseed + 492 fe; CI-pending (backfilled at close-polish) |

## Sprint Goal
Two recorded low-risk follow-ups from the flat-authority reform (owner-requested 2026-06-23):
1. **The cosmetic rename** — the org-root-misnomer identifiers (`tree_root_org_id` columns, `TreeRootOrgId` model/event fields, the `reporting-tree-` advisory-prefix string) → `organisation`-named. Post-S95 they hold the **Organisation** (the walk is gone), so "tree_root" is a misnomer. **No behaviour change** (greenfield reseed; the EventSerializer is name-keyed by TYPE, so renaming event FIELDS is replay-safe). The **people-reporting-tree** concept stays (`GetTreeAsync`, the `/api/admin/reporting-lines/tree/{…}` view route PATH, "reporting tree" terminology) — a reporting line is still a people-hierarchy tree; only the org-root IDENTIFIER renames.
2. **The inactive-org-home `is_active` guard** — the S95 Step-7a NOTE: the post-walk `primary_org` reads dropped the old `organizations.is_active=TRUE` check, so an assign against a *deactivated* home Organisation would proceed where the walk 400'd. Restore the fail-closed (unreachable today — no org-deactivation path — but byte-identical + defense-in-depth).

## Scope (in / out)

**Rename — the EXACT 4 tokens (identical mapping across every layer):**
- `tree_root_org_id` → `organisation_id` (DB columns on `reporting_lines` + `manager_vikar`; all SQL)
- `TreeRootOrgId` → `OrganisationId` (C# model + event fields; FE TS interface fields)
- `treeRootOrgId` → `organisationId` (C# locals, route-template params, FE TS vars, JSON manifest keys, SQL aliases)
- `reporting-tree-` → `reporting-org-` (the advisory lock-prefix STRING — in the helper + the test that holds the lock)

**KEEP (NOT renamed — the people-reporting-tree concept):** `GetTreeAsync`; the `/api/admin/reporting-lines/tree/{organisationId}/…` route PATH segment `tree`; the "reporting tree" / "reporting-line tree" prose. (None of these contain the 4 tokens above except the route param `{treeRootOrgId}`, which DOES rename to `{organisationId}` — the `/tree/` segment stays.)

**IN:**
- Backend: `ReportingLine`/`ManagerVikar` models; the events (`ReportingLineAssigned/Superseded/BulkImported/ManagerDeactivated`, `ManagerVikarCreated/Ended`, `FallbackTraversalWarning`); the 5 audit mappers; the 3 lifecycle services (`EmploymentEndDateLifecycleWriter`, `SettlementCloseService`, `DelegationExpiryService`); `ReportingLineRepository` (queries + the lock helpers' prefix string); `DesignatedApproverAuthorizer`; `ReportingLineEndpoints`/`AdminEndpoints`/`ApprovalEndpoints` (route params + locals); `ManagerVikarRepository`.
- **The `is_active` guard** (TASK-9601): `ValidateSameOrganisationAsync` (join `organizations` with `is_active=TRUE`, `FOR UPDATE OF u` to keep user-only locking) + `DeriveEmployeeTreeRootInTxAsync` (add the org-active join condition).
- init.sql (column rename, both tables; the FK + index names if they embed the column name) + db-schema regen.
- FE (`useMedarbejderRoster`, `useReportingLines`, the `LifecycleSections.test`).
- Demo (`DemoGenerator`/`DemoLoader`/`DemoVerifier`/models/`Program` + the 2 manifests, regen).
- Tests (~48 files — the column/field references in seed-DDL + assertions; the `reporting-tree-` lock-string in `S95FlatOrgLockTests`).

**OUT:** the people-tree concept rename (kept); the structured Enhed feature (→ S97, refined separately); the Organisation admin page.

## Plan Review (Step 0b)
**SKIPPED — pure tech-debt** (a targeted 4-token mechanical rename + a few-line defensive guard; no new behaviour, no design fork). Verification is the **compiler + the full pyramid** (a missed/partial rename → a build break or a failing assertion) + the **Step-7a dual-lens** (no behaviour change, no missed reference, the event-field rename greenfield-safe). Per the WORKFLOW pure-tech-debt skip rule.

## Architectural Constraints
- [x] P1 — Architectural integrity (a pure rename; the people-tree vs org-root distinction respected — `GetTreeAsync`/`/tree/` kept, only the org-root identifier renamed)
- [x] P3 — Event sourcing (event FIELD rename is name-keyed-by-TYPE serializer + greenfield = replay-safe; no event TYPE change; both lenses confirmed)
- [x] P4 — Concurrency (the lock-prefix string renamed consistently across ALL acquire sites + the test's reconstruction — prod↔test both `reporting-org-`, verified; the `is_active` guard uses `FOR UPDATE OF u` to preserve the S74 user-only row-pin)
- [x] P7 — Security (the `is_active` guard restores the repository-level fail-closed; no widening; the endpoint org-scope gate already filtered `is_active` — defense-in-depth)
- [x] P8 — CI/CD (greenfield reseed; 1072 regression green locally; CI confirmation pending)

## Task Log (planned)

### TASK-9601 — The inactive-org-home `is_active` guard (Backend/Infrastructure)
`ValidateSameOrganisationAsync`: `SELECT u.user_id, u.primary_org_id FROM users u JOIN organizations o ON o.org_id = u.primary_org_id AND o.is_active = TRUE WHERE u.user_id = ANY(@ids) AND u.is_active = TRUE ORDER BY u.user_id FOR UPDATE OF u` (an inactive home Organisation → no row → the existing "not found/inactive" throw; `FOR UPDATE OF u` keeps locking ONLY the user rows). `DeriveEmployeeTreeRootInTxAsync`: add the same `JOIN organizations o ON o.org_id = u.primary_org_id AND o.is_active = TRUE`. Refresh the "not found or inactive" messages to "…or home Organisation inactive".

### TASK-9602 — Backend rename (src + init.sql) — the 4 tokens
All `src/` + `docker/postgres/init.sql`. Regen `docs/generated/db-schema.md`.

### TASK-9603 — Frontend rename — the 4 tokens
`frontend/src` (`useMedarbejderRoster`, `useReportingLines`, `LifecycleSections.test`).

### TASK-9604 — Demo rename — the 4 tokens + regen manifests
`tools/StatsTid.DemoSeed/**` + the 2 manifests.

### TASK-9605 — Tests rename — the 4 tokens
`tests/**` (~48 files). The `reporting-tree-`→`reporting-org-` lock-string in `S95FlatOrgLockTests` MUST match the production helper (else the held-lock interleave breaks).

### TASK-9606 — Docs + close (Orchestrator)
ADR-035 (note the cosmetic rename DONE + the inactive-org `is_active` restored — the S95 follow-up condition closed); SECURITY.md (the lock prefix is now `reporting-org-`); INDEX/QUALITY/ROADMAP.

## Risks
- **Partial/inconsistent rename** → a build break (caught by central build) or a lock-key mismatch (the test holds `reporting-tree-` but prod uses `reporting-org-` → the interleave fails — caught by the pyramid). Mitigation: the SAME 4-token mapping to every layer; central build + full pyramid.
- **The `reporting-tree-` lock string** must change in the production helper AND the test together (the test reconstructs the exact `hashtext('reporting-tree-' || org)` expression). Both in scope.
- **Event-field rename**: greenfield (no persisted events) + name-keyed-by-TYPE serializer → replay-safe (confirmed S95 for `FallbackTraversalWarning`).
- **`is_active` guard `FOR UPDATE OF u`**: preserves the existing user-only row-pin (does NOT add org-row locking) — no concurrency change.

## Execution Outcome
Both cleanups landed. The rename touched **~50 files** across 4 disjoint slices (Backend 23 src + init.sql, FE 3, Demo 7 + 2 regenerated manifests, Tests 20) via the identical 4-token mapping; the `is_active` guard is 2 `ReportingLineRepository` methods. Full `dotnet build` 0/0; fresh-greenfield Postgres reseed verified the column rename (2 `organisation_id`, 0 `tree_root_org_id`); db-schema regen (65 tables). FE 492 vitest + Unit 850 green.

**Finding (the inactive-org guard's real reachability):** the new `is_active` test surfaced that the PRIMARY-assign **endpoint** already rejects an inactive-home subject at the org-scope gate (`OrgScopeValidator.ValidateEmployeeAccessAsync` → `OrganizationRepository.GetByIdAsync`, which carries a **pre-existing** `is_active=TRUE` filter) → 403, BEFORE reaching the S96 in-tx guard. So the S96 guard is **repository-level defense-in-depth** (covering direct repository callers / non-gated paths) — the endpoint was already safe. The S96-specific RED-on-old proof is therefore the **repository unit test** `ValidateSameOrganisation_InactiveHomeOrg_ThrowsInvalidOperation` (the endpoint test is a faithful "rejected/no-edge" but not S96-isolating).

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex + internal Reviewer) |
| **Sprint-start commit** | `76de6bb7cfe3332db0ef001d60c003dba1601758` |
| **Review Cycles** | 1 (both clean — no fix cycle) |
| **Findings** | 0 BLOCKER, 0 WARNING, 2 NOTE (both accepted) |

### Findings
- **Codex**: A/B/D clean (rename consistent + complete; the people-tree concept preserved; prod↔test lock-string both `reporting-org-`; the `is_active` guard correct). NOTE (P3): the standard greenfield-replay caveat (old persisted `treeRootOrgId` payloads would need a compat shim) — accepted (pre-launch greenfield, no persisted events).
- **Internal Reviewer**: "No findings." Verified every column reader moved together, the lock-string match, P3 replay-safety, the guard's `FOR UPDATE OF u` preserving the S74 concurrency contract + the S83 revoke-safe swallow. NOTE: the index NAME `idx_reporting_lines_tree_root` kept (no contract depends on it; index-name renames deferred) — accepted cosmetic residue.
- Artifacts: `.claude/reviews/SPRINT-96-step7a-{codex,reviewer}.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 850 | all passing |
| Regression | 1072 | 1070 central green + 2 new `is_active` RED-on-old pins (verified 12/12 in `S95FlatOrgLockTests` in isolation) |
| Smoke | 6 | all passing |
| DemoSeed | 29 | all passing |
| Frontend (vitest) | 492 | all passing |
| **Total** | **2449** | CI confirmation pending |

## Sprint Retrospective
**What went well**: a clean 4-token mechanical rename across 4 disjoint slices in parallel — the identical token mapping kept every layer consistent; the compiler + full pyramid (not a review) were the real verification, and Step-0b was correctly skipped (pure tech-debt). The critical risk (the `reporting-org-` lock-prefix string drifting between prod + the held-lock test) was called out in the plan + verified by both Step-7a lenses. The `is_active` test surfaced the endpoint's pre-existing org-scope-gate protection — clarifying the guard's true layer.
**What to improve**: the index NAME `idx_reporting_lines_tree_root` still carries the old token (no-contract cosmetic; a trivial future tidy if a schema-touching sprint passes through).
**Knowledge produced**: ADR-035 (the cosmetic rename DONE + the inactive-org `is_active` restored — the S95 follow-up condition CLOSED); SECURITY.md (the advisory prefix is now `reporting-org-`). Remaining recorded item: **S97 = the structured Enhed table + multi-tag UX** (a feature — to be refined).
