# Sprint 94 — Flat approval authority (CanApprove = edge OR HR/Admin-over-Org; retire REQUIRED-mode)

| Field | Value |
|-------|-------|
| **Sprint** | 94 |
| **Status** | complete |
| **Start Date** | 2026-06-23 |
| **End Date** | 2026-06-23 |
| **Orchestrator Approved** | yes — 2026-06-23 |
| **Build Verified** | yes — `dotnet build` 0 errors (combined tree) |
| **Test Verified** | yes — CI GREEN `28019562226` (all 7 jobs, 2026-06-23): 850 unit + 1062 regression + 6 smoke + 29 demoseed + 492 fe = 2439 |

> **v2 (post Step-0b dual-lens).** Added the live enforcement-mode ADMIN surface (BLOCKER 1), the `ExplicitFallbackConfirmation` event/model/overload drop (BLOCKER 2), the `period.OrgId`→`ValidateEmployeeAccessAsync(empId)` fallback binding (WARNING 3), the SET-clause lockstep removal, the orphan/read-act residuals, and the FE-test + `ForcedRollbackHarness` scope. See Plan Review (Step 0b).

## Sprint Goal
ADR-035 slice 3 (Phase 3 of the flat-authority reform). Reshape approval authority: **`CanApprove(actor, emp) = IsEffectiveDesignatedApprover (edge) OR HasHrAdminScopeOverEmpOrg(actor, emp)`** — retire the unfloored leader-by-org-scope branch (a non-designated leader must hold the reporting edge; only **LocalHR/LocalAdmin+** form the org-scope fallback, bound to the employee's CURRENT Organisation — OQ4/OQ5) — and **RETIRE the REQUIRED-mode enforcement machinery end-to-end** (`reporting_line_tree_settings` + the GET/PUT `/settings` admin endpoints + the FE PREFERRED↔REQUIRED toggle + the 428 `ORG_SCOPE_FALLBACK` confirm gate + `confirmFallback` + `TreeSettingsRepository` + `explicit_fallback_confirmation` on the column, model, events, and overloads — OQ6). The **tree machinery stays** (`ValidateSameTreeAsync`/`ResolveTreeRootOrgIdAsync`/`tree_root` — retired in S95); post-S92 `tree_root == Organisation`, so the predicate's same-tree re-check is already same-Organisation and the vikar bound is already same-Organisation (no S94 change). Refinement: `.claude/refinements/REFINEMENT-flat-authority-model.md` (OQ4/5/6/7 locked).

## Scope (in / out)

**IN:**
- **OQ4/OQ5 — the approval org-scope branch becomes the HR/Admin fallback, bound to the employee's CURRENT Organisation.** In approve + reject (and the reopen LEADER arm), replace the org-scope gate `ValidateOrgAccessAsync(actor, period.OrgId, ct)` (roleFloor `null`) with `ValidateEmployeeAccessAsync(actor, period.EmployeeId, StatsTidRoles.LocalHR, ct)` — this is exactly `HasHrAdminScopeOverEmpOrg` (HR/Admin scope over the employee's current `primary_org_id`). Net: `CanApprove = edge OR HR/Admin-over-emp-Org`. A non-designated in-scope LEADER can no longer approve (must hold the edge). The in-lock edge re-eval (`if (!orgScopeAllowed) re-check edge`) carries over (the floor only narrows what counts as `orgScopeAllowed`).
- **OQ6 — retire REQUIRED-mode end-to-end** (the full surface the dual-lens enumerated):
  - Backend approval path: the pre-tx + in-tx 428 `ORG_SCOPE_FALLBACK` gates in approve/reject; the `confirmFallback` query param + the `explicitFallback` flow; the `GetEnforcementModeAsync` calls. (KEEP `ResolveTreeRootOrgIdAsync(period.OrgId)` IFF the `FallbackTraversalWarning.TreeRootOrgId` depth>3 payload still needs `treeRoot` — do not blind-delete the local.)
  - The enforcement-mode **admin endpoints**: GET + PUT `/api/admin/reporting-lines/tree/{treeRootOrgId}/settings` (`ReportingLineEndpoints.cs:1605/:1635`) + the `UpdateTreeSettingsRequest` record (`:3007`) + `ValidateTreePopulatedAsync` (dies with the repo).
  - `TreeSettingsRepository` (class + DI in `Program.cs`) + `GetEnforcementModeAsync` + `UpsertAsync`.
  - `explicit_fallback_confirmation`: the `approval_periods` column; the `ApprovalPeriod` model field; the `PeriodApproved`/`PeriodRejected` **event** fields; the `TryUpdateStatusConditionalAsync` + both `UpdateStatusAsync` overload params; the SET-clause fragments + the `@explicitFallback` binding in `BuildUpdateStatusCommand` (ALL 3 branches APPROVED/REJECTED/DRAFT — lockstep with the column drop).
  - FE: the `TeamOversigt.tsx` 428 confirm dialog/state/handler + `confirmFallback` URLs; the `useReportingLines.ts` `fetchTreeSettings`/`updateTreeSettings`; the `MedarbejderAdministration.tsx` enforcement toggle (state/load/`handleToggleEnforcement`/UI) + its css + tests.
  - **KEEP** `approval_periods.approval_method` + `designated_approver_id` (audit; `ORG_SCOPE_FALLBACK` stays a valid classification — only the *gate* + the *explicit-confirm* go).
- **OQ7 — read/act parity (document).** The HR/Admin fallback's read path is the existing org-scoped pending/monthly pickers; the leader's edge-accurate surface is the S87 team-overview. Document; no picker-floor this slice (see OQ1 + Risks).
- init.sql schema + db-schema regen; demo seed; tests; ADR-035 amendment + SECURITY.md + ADR-027 D11/D13 disposition.

**OUT (S95):** delete the tree machinery (`tree_root_org_id`/`ResolveTreeRootOrgIdAsync`/`ValidateSameTreeAsync`/`CrossTreeAssignmentException`), migrate the predicate + write-path/vikar same-tree checks to a direct `primary_org` comparison, re-key the advisory locks (the lock-matrix). The Organisation admin **page** → after S95.

## Entropy Scan Findings (Step 0a)

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | INDEX 51 entries (ADR-035 latest). |
| Pattern compliance spot-check | CLEAN | n/a at plan time. |
| Orphan detection | CLEAN | — |
| Documentation drift | DEBT | `MEMORY.md` over budget (pre-existing). |
| Quality grade review | CLEAN | No domain grade change pending. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 architectural integrity; P7 access control; schema migration; approval/auth = high-risk) |
| **External Codex** | invoked 2026-06-23 — cycle 1: 0 BLOCKER / 3 WARNING / 6 NOTE; **cycle 2: "Sound to plan"** |
| **Internal Reviewer** | invoked 2026-06-23 — cycle 1: 2 BLOCKER / 0 WARNING / 7 NOTE; **cycle 2: "Sound to plan"** (one NOTE: the dead `useApprovals.ts` hook — folded into TASK-9404) |
| **BLOCKERs resolved before Step 1** | yes — (1) the full enforcement-mode admin surface (GET+PUT `/settings` + FE toggle/hook/tests) added to TASK-9402/9404; (2) `ExplicitFallbackConfirmation` dropped from events+model+overloads+SET-clauses in lockstep; (W3) the HR/Admin fallback bound to `ValidateEmployeeAccessAsync(empId, LocalHR)` (current-org) |

### Findings (cycle 1)
- **BLOCKER 1 (Reviewer; Codex WARNING)** — OQ6 omits the live enforcement-mode ADMIN surface: GET+PUT `/settings` (`ReportingLineEndpoints.cs:1605/:1635`) + `useReportingLines.ts:163` + the `MedarbejderAdministration.tsx` toggle + css + test. → **FIXED:** added to TASK-9402 (endpoints) + TASK-9404 (FE).
- **BLOCKER 2 (Reviewer; Codex WARNING)** — `ExplicitFallbackConfirmation` is in the shared events (`PeriodApproved.cs:13`/`PeriodRejected.cs:14`) + model (`ApprovalPeriod.cs:37`) + both `UpdateStatusAsync` overloads + the `BuildUpdateStatusCommand` SET-clause (3 branches) + unit tests. → **RESOLVED: DROP it everywhere** (clean, greenfield; no audit mapper reads it — Codex/Reviewer NOTE 6); lockstep with the column. Added to TASK-9402/9403; disposition noted in TASK-9407.
- **WARNING 3 (both)** — the floored gate keys on `period.OrgId` (creation snapshot), not the employee's CURRENT org. → **RESOLVED: bind the fallback to `ValidateEmployeeAccessAsync(actor, period.EmployeeId, LocalHR)`** (current-org = the refinement's `HasHrAdminScopeOverEmpOrg`). TASK-9401.
- **NOTE 4 (both)** — OQ4 strands no legitimate edge-approver; orphans route to HR/Admin (intended). Caveat: a new Organisation with no HR scope yet (OQ8) + orphans = unapprovable → greenfield-acceptable, OQ8-linked residual. → AC added (TASK-9406).
- **NOTE 5/8 (both)** — the in-lock re-eval + the "ResolveTreeRootOrgIdAsync stays" boundary are sound; keep `treeRoot` for the FallbackTraversalWarning. → folded into TASK-9402.
- **NOTE 6 (both)** — dropping `explicit_fallback_confirmation` is audit-safe; co-remove the SET-clause fragments + param in lockstep (else runtime "column does not exist"). → TASK-9402/9403.
- **NOTE 7 (both)** — the OQ1 read/act residual is real; "document" is acceptable; confirm team-overview is the only leader entry point. → OQ1 + AC.
- **NOTE 9 (Reviewer)** — test blast radius: `ApprovalConcurrencyHardeningTests` (29 enforcement/428 refs) + the 3 FE test files + `ForcedRollbackHarness.cs` (references `reporting_line_tree_settings`). → TASK-9406.

### Resolution
All cycle-1 BLOCKERs/WARNINGs incorporated. Cycle 2 verifies.

## Architectural Constraints Verified
- [x] P1 — Architectural integrity (approval predicate reshaped; tree machinery preserved for S95; the enforcement admin surface fully removed — no dead-auth/build-break, both lenses confirmed)
- [x] P3 — Event sourcing (`explicit_fallback_confirmation` dropped from column+model+events in lockstep; `EventSerializer` name-keyed = replay-safe; `approval_method`/`designated_approver_id` audit retained; no projection mapper read it)
- [x] P4 — Version correctness (the S78 in-lock re-eval + R2 conditional status update preserved; the kept concurrency tests pass)
- [x] P7 — Security & access control (leader-org-scope branch RETIRED; HR/Admin fallback floored at LocalHR + bound to the employee's CURRENT Organisation; RED-on-old proves the inversion + the fallback + the orphan case; no stranded approver)
- [x] P8 — CI/CD (greenfield reseed; 1062 regression green locally — 5 FAIL-002 sheds cleared; CI confirmation pending)

## Task Log (planned decomposition)

### TASK-9401 — Approval authorization: HR/Admin fallback bound to the employee's current Org (OQ4/OQ5)
| Field | Value |
|-------|-------|
| **Agent** | Security & Compliance + Backend API (cross-domain authorized) |
| **Components** | `ApprovalEndpoints.cs` approve (~:263, in-lock ~:330), reject (~:479, in-lock ~:523), reopen LEADER arm (~:1629/:1659) |
| **KB Refs** | ADR-035 (OQ4/5), ADR-027 D13 (reshaped), ADR-012 |

**Description**: Replace the org-scope gate `ValidateOrgAccessAsync(actor, period.OrgId, ct)` (roleFloor null) with `ValidateEmployeeAccessAsync(actor, period.EmployeeId, StatsTidRoles.LocalHR, ct)` (pre-tx + in-lock, approve + reject + reopen-leader-arm). This = `HasHrAdminScopeOverEmpOrg` (HR/Admin over the employee's CURRENT Organisation). The edge arm (`IsEffectiveDesignatedApproverAsync`) unchanged. The in-lock edge re-eval keys on the same (now-floored) `orgScopeAllowed`. The reopen EMPLOYEE arm + the S90 payroll-export lock untouched.

**Validation Criteria**:
- [ ] A non-designated LEADER scoped over the employee's Organisation is DENIED approve/reject (was allowed) — RED-on-old.
- [ ] A LocalHR/LocalAdmin scoped over the employee's current Organisation approves WITHOUT an edge; an out-of-scope HR is denied.
- [ ] A designated leader (holds the edge) still approves (team-overview/bulk flow unaffected); the in-lock edge re-eval fires only when the HR/Admin fallback didn't admit.

---

### TASK-9402 — Retire REQUIRED-mode end-to-end (OQ6) — backend
| Field | Value |
|-------|-------|
| **Agent** | Backend API + Infrastructure + Data Model (cross-domain authorized) |
| **Components** | `ApprovalEndpoints.cs` (428 gates pre-tx+in-tx, `confirmFallback`, `GetEnforcementModeAsync`); `ReportingLineEndpoints.cs:1605/:1635` (GET+PUT `/settings`) + `:3007` (`UpdateTreeSettingsRequest`); `TreeSettingsRepository.cs` (DELETE) + `Program.cs` (DI); `ApprovalPeriodRepository.cs` (the 3 `UpdateStatusAsync`/conditional overloads + `BuildUpdateStatusCommand` SET-clauses); `ApprovalPeriod.cs` + `PeriodApproved.cs`/`PeriodRejected.cs` (the `ExplicitFallbackConfirmation` field) |
| **KB Refs** | ADR-027 D11 (retired), ADR-035 (OQ6) |

**Description**: Remove the 428 `ORG_SCOPE_FALLBACK` machinery (pre-tx + in-tx, approve + reject) + `confirmFallback` + the `GetEnforcementModeAsync` calls. **KEEP `ResolveTreeRootOrgIdAsync(period.OrgId)` IFF the `FallbackTraversalWarning.TreeRootOrgId` depth>3 payload (`~:391-404/:576-589`) still needs it** — verify before deleting the `treeRoot` local. Delete the GET+PUT `/settings` admin endpoints + `UpdateTreeSettingsRequest` + `TreeSettingsRepository` (class+DI) + `GetEnforcementModeAsync`/`UpsertAsync`/`ValidateTreePopulatedAsync`. **DROP `ExplicitFallbackConfirmation`** from `PeriodApproved`/`PeriodRejected` events + `ApprovalPeriod` model + all `UpdateStatusAsync`/`TryUpdateStatusConditionalAsync` params + the `BuildUpdateStatusCommand` SET-clause fragments + `@explicitFallback` binding (ALL 3 branches), in lockstep with the column drop (TASK-9403). KEEP `DeriveApprovalMethod` + the `approval_method` write.

**Validation Criteria**:
- [ ] No `428`/`confirmFallback`/`enforcementMode`/`TreeSettingsRepository`/`GetEnforcementModeAsync`/`explicit_fallback`/`ExplicitFallbackConfirmation`/`tree/.*settings` references remain in executable backend code (grep gate).
- [ ] An HR/Admin org-scope approval succeeds with NO 428 (records `approval_method=ORG_SCOPE_FALLBACK`); `dotnet build` clean; the S78 in-lock re-eval + R2 conditional update intact; `EventSerializer` map updated if the event shape changed.

---

### TASK-9403 — Schema: drop reporting_line_tree_settings + explicit_fallback_confirmation
| Field | Value |
|-------|-------|
| **Agent** | Data Model (extended into init.sql, cross-domain authorized) |
| **Components** | `init.sql` (`reporting_line_tree_settings` table+seed ~:2504-2547; `approval_periods.explicit_fallback_confirmation` ~:2513), db-schema doc |
| **KB Refs** | ADR-035 (OQ6) |

**Description**: DROP `reporting_line_tree_settings` (table + the STY02 seed). DROP `approval_periods.explicit_fallback_confirmation`. KEEP `approval_method` + `designated_approver_id`. Regenerate db-schema (66 → 65 tables).

**Validation Criteria**:
- [ ] `reporting_line_tree_settings` + `explicit_fallback_confirmation` gone; `approval_method`/`designated_approver_id` retained; check_docs passes (table count updated to 65).

---

### TASK-9404 — Frontend: delete the REQUIRED-mode dialog + the enforcement toggle
| Field | Value |
|-------|-------|
| **Agent** | UX |
| **Components** | `TeamOversigt.tsx` (428 dialog/state/handler ~:542-564, `confirmFallback` ~:438-439); `useReportingLines.ts:163-178` (`fetchTreeSettings`/`updateTreeSettings`); `useApprovals.ts:32-44` (DEAD hook carrying `confirmFallback` — delete the param or the hook so the grep gate passes); `MedarbejderAdministration.tsx` (enforcement toggle ~:369-373/:438-459/:610-647/:772-797) + `.module.css:69` + `__tests__/MedarbejderAdministration.test.tsx`; `TeamOversigt.test.tsx`/`TeamRowDetail.test.tsx` 428 cases |
| **KB Refs** | ADR-011 |

**Description**: Delete the `TeamOversigt` enforcement/428 confirm dialog + state + handler + `?confirmFallback=true` URLs + the 428-result branches (approve/reject single-shot). Delete the `useReportingLines` `fetchTreeSettings`/`updateTreeSettings` pair. Delete the `MedarbejderAdministration` enforcement toggle (state/load/`handleToggleEnforcement`/UI) + its css + the test cases. Update the FE tests asserting 428/enforcement.

**Validation Criteria**:
- [ ] No 428/`confirmFallback`/enforcement/`tree.*settings` references in the FE; approve/reject single-shot; FE build + vitest green.

---

### TASK-9405 — Demo seed
| Field | Value |
|-------|-------|
| **Agent** | Tooling/DemoSeed (cross-domain authorized) |
| **Components** | `DemoGenerator.cs`/`SqlEmitter.cs` (if it emits `reporting_line_tree_settings`), `99-demo-seed.sql`, `DemoVerifier.cs` |
| **KB Refs** | S84 |

**Description**: Remove any `reporting_line_tree_settings` emission; regenerate `99-demo-seed.sql`; drop any enforcement-mode `DemoVerifier` assertion. No-op verification if the demo doesn't touch it.

**Validation Criteria**:
- [ ] Regenerated demo SQL has no `reporting_line_tree_settings`; build + verifier green.

---

### TASK-9406 — Tests: OQ4 inversion + RED-on-old + delete REQUIRED-mode tests
| Field | Value |
|-------|-------|
| **Agent** | Test & QA |
| **Components** | `DesignatedApproverAuthorityTests` (incl. `OrgScopeManager_StillApproves_ViaOrgScope_NoEdgeNeeded:747` — RETIRE/INVERT), `ApprovalConcurrencyHardeningTests` (29 enforcement/428 refs), `ReportingLineTests.cs` (the `ExplicitFallbackConfirmation`/`TreeSettings` unit cases), `ForcedRollbackHarness.cs` + `ReportingLineRepositoryTests` (the `reporting_line_tree_settings` DDL/fixtures), the 3 FE test files |
| **KB Refs** | FAIL-002, PAT-008 |

**Description**: DELETE the REQUIRED-mode tests (428 gate, `confirmFallback`, enforcement upsert/version, `explicit_fallback_confirmation`, the `TreeSettings`/`ValidateTreePopulated` cases) + fix `ForcedRollbackHarness`/fixtures referencing the dropped table. INVERT `OrgScopeManager_StillApproves_ViaOrgScope_NoEdgeNeeded` (the retired flow). Add RED-on-old: (a) a non-designated in-scope LEADER is DENIED approve/reject (was allowed); (b) a LocalHR/LocalAdmin scoped over the employee's current Organisation approves with NO edge + NO 428; (c) an out-of-scope HR is denied; (d) a designated leader still approves; (e) the reopen leader arm follows the same floor; (f) an **orphan** employee (no manager edge) is approvable by in-scope HR and DENIED to a non-designated in-scope leader. Run the full pyramid per FAIL-002.

**Validation Criteria**:
- [ ] All suites green; RED-on-old (a)-(f) present + passing; no enforcement/428/tree-settings/`explicit_fallback` test remains.

---

### TASK-9407 — ADR-035 amendment + docs
| Field | Value |
|-------|-------|
| **Agent** | Orchestrator |
| **Components** | `ADR-035` (S94 slice + D-disposition: D11 RETIRED, D13 reshaped), INDEX, db-schema, SECURITY.md, ADR-027 |
| **KB Refs** | amends ADR-027 D11/D13 |

**Description**: Amend ADR-035 D4/D5/D6 — flat-approval slice LANDED: `CanApprove = edge OR HR/Admin-over-emp-Org`; leader-org-scope branch retired; REQUIRED-mode enforcement retired (table + admin endpoints + FE toggle + 428 + `explicit_fallback_confirmation` on column/model/events). Mark ADR-027 D11 **RETIRED** + D13 **reshaped** in the disposition table (D2/D9/tree-root still "transitional" pending S95). Note the `ExplicitFallbackConfirmation` event-field drop (record-shape; greenfield, no replay). Update SECURITY.md (approval-authority model). Record the OQ1 read/act residual + the OQ8-linked "new org, no HR scope" operational note.

**Validation Criteria**:
- [ ] ADR-035 amended; SECURITY.md updated; check_docs passes; ADR-027 D11 retired / D13 reshaped.

## Open Questions (Step-0b confirmed; leans resolved)
1. **Read/act residual (OQ7).** Post-OQ4 a LEADER may still SEE org-scoped periods in the legacy pending/monthly pickers (unfloored) yet not be able to approve (no edge). → **Resolved: document** (the team-overview is the leader's edge-accurate surface; the legacy pickers are the HR/admin/global view). AC: confirm team-overview is the only leader entry point so a leader never sees a row of 403s; revisit picker-floor at the page sprint.

## Risks & Conflicts
- **OQ4 authority tightening (P7, intended).** A non-designated in-scope leader LOSES approve/reject. Orphans route to HR/Admin (the model). **OQ8-linked residual**: a freshly-created Organisation with orphan employees and no in-scope HR yet is unapprovable until an HR scope is granted — greenfield-acceptable, documented.
- **Enforcement-admin-surface removal (MEDIUM, BLOCKER-1 fix).** Spans 2 backend endpoints + the repo + the FE toggle + hook + 3 FE tests + the schema. Removing the table without the endpoints = build break / 500. Mitigation: TASK-9402+9404 enumerate; grep gate.
- **Event-shape change (P3, BLOCKER-2).** Dropping `ExplicitFallbackConfirmation` from `PeriodApproved`/`PeriodRejected` changes the event record shape (greenfield, no replay history; audit mapper doesn't read it). Update `EventSerializer` if needed; confirm no replay test asserts it.
- **`period.OrgId` vs current-org (WARNING-3 fix).** Switching to `ValidateEmployeeAccessAsync(empId)` binds the fallback to the employee's current Organisation — more correct + matches the refinement; behavior-identical in tests where the employee sits on period.OrgId.
- **`ResolveTreeRootOrgIdAsync` stays (P1).** Only the enforcement-path `GetEnforcementModeAsync` call goes; keep the `treeRoot` resolve if the FallbackTraversalWarning needs it. Don't over-delete (S95 owns the deletion).
- **review-lens-complementarity**: dual-lens Step-0b (this) + Step-7a; adversarial "find a stranded approver / a leftover enforcement path / a half-dropped event field."

## Execution Outcome
All 7 tasks complete (Backend 9401/9402/9403, Frontend 9404, Tests 9406, ADR 9407; **TASK-9405 was a no-op** — the demo seed never emitted `reporting_line_tree_settings`). Combined tree builds 0/0; schema verified flat (`reporting_line_tree_settings` + `explicit_fallback_confirmation` dropped, `approval_method` kept, 65 tables). The Backend agent caught + handled the `TreeSettings` model deletion; the Test agent caught + handled the `S91TreePageHrAccessTests` `/settings` cases (beyond the named scope).

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex + internal Reviewer) |
| **Sprint-start commit** | `d382ed59ee5de2fbc243ac545789ea6a4281a507` |
| **Review Cycles** | 1 (both lenses clean — no fix cycle) |
| **Findings** | 0 BLOCKER, 0 WARNING, 0 NOTE |
| **Resolution** | clean |

### Findings
- **Codex**: "Clean — no findings" (backend build verified).
- **Internal Reviewer**: No findings. Confirmed the OQ4 core (no stranded approver; orphans → HR/Admin; binds to the employee's current org; in-lock re-eval preserved), OQ6 completeness (zero leftover refs, backend + FE), P3 event-shape (name-keyed serializer = replay-safe), the `ResolveTreeRootOrgIdAsync` retention boundary, and test faithfulness (the kept S78 concurrency tests; a *strengthened* FE assertion).
- Artifacts: `.claude/reviews/SPRINT-94-step7a-{codex,reviewer}.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 850 | all passing (−4 deleted retired `TreeSettings`/`ExplicitFallbackConfirmation` cases) |
| Regression | 1062 | all passing (local, fresh-greenfield; 1057 first run + 5 FAIL-002 testcontainer sheds cleared 21/21 in isolation) |
| Smoke | 6 | all passing |
| DemoSeed | 29 | all passing |
| Frontend (vitest) | 492 | all passing (−3 deleted enforcement-dialog cases) |
| **Total** | **2439** | CI GREEN `28019562226` (all 7 jobs) |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 7 (1 no-op: TASK-9405) |
| Constraint Violations | 0 |
| Reviewer Findings | Step-0b: 2 BLOCKER (Reviewer) + WARNINGs (both, all resolved pre-code); Step-7a: 0 |
| External Review Cycles | Step-0b: 2 ("sound to plan"); Step-7a: 1 (both clean) |
| Re-dispatches | 0 |
| First-Pass Rate | 7/7 = 100% (no agent re-dispatch) |

## Sprint Retrospective
**What went well**: [[review-lens-complementarity]] decisive at Step-0b — the internal Reviewer caught 2 BLOCKERs (the live enforcement-mode ADMIN surface + the `ExplicitFallbackConfirmation` event-contract field) the plan under-specified, and the dual-lens corrected the `period.OrgId`→current-org binding. The OQ4/OQ6 split was clean; the key insight (post-S92 `tree_root == Organisation` ⇒ the predicate's same-tree re-check is already same-Org, so the tree machinery stays for S95) kept the sprint focused. Step-7a both lenses clean. 100% first-pass.
**What to improve**: the plan's first cut under-scoped two whole surfaces (the GET/PUT `/settings` admin endpoints + the FE PREFERRED↔REQUIRED toggle; the event-contract field) — an upfront `grep` for every consumer of the retired feature (not just the obvious endpoints) would have surfaced them at plan time.
**Knowledge produced**: ADR-035 S94 amendment (flat approval landed; ADR-027 D11 RETIRED / D13 reshaped).
