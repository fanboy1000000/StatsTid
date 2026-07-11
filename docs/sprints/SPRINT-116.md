# Sprint 116 — Typed API Contract retrofit Pass 3: the approval bucket + the overtime list repair

| Field | Value |
|-------|-------|
| **Sprint** | 116 |
| **Status** | complete |
| **Start Date** | 2026-07-11 |
| **End Date** | 2026-07-14 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes — backend 0 err (0 new warnings in touched files); FE tsc 0 + lint 0 (8 tiers RED-probed live) + build green |
| **Test Verified** | yes (local, full): 861 unit + **1261 regression** (1260 first-run + 1 pre-existing FAIL-002 shed isolation-cleared 1/1 — `Adr032ConsumptionPinTests.HalfTimeWindow`, Skema surface, untouched by S116; the 42 fixed-port tests ran locally vs the compose Postgres) + 6 smoke (live 8-container stack) + 55 demoseed + **589 FE** (1 load-flake on the first run — a timing-sensitive skema debounce test under the concurrent regression build — clean re-run 589/589); **pyramid 861u+1261r+6s+55demoseed+589fe = 2772 (+49)**; gates: convention **67 typed / 70 grandfathered / 2 declared body-less** + selftest 9 directions, drift in-sync (102 paths/124 schemas), regen sha-idempotent ×2 checkpoints, endpoint-contract lint + check_docs hard-green |

## Sprint Goal
Retrofit Pass 3 (PAT-012, [[typed-api-contract-program]]): drain the **17-op approval bucket** — the approval family (10) + the reporting-lines delegate trio (3) + the overtime pre-approval quartet (4, owner-ruled OQ-1) — via the thrice-proven recipe (manifest 87→70; typed 49→66), **plus ONE bounded product task (owner-ruled OQ-2): a NEW scope-bounded `GET /api/overtime/pre-approvals` admin list endpoint, typed from birth (67th typed op), + the full FE repair of `OvertimePreApprovalManagement`** — whose list read today calls that nonexistent route and 404s (the L3 pre-existing defect, discovered by the S116 grounding sweep). Enums owner-confirmed FINAL (OQ-3): `[AllowedValues]` on period `status` / `periodType` / overtime `status`.

The pass is recipe-clean (grounding-swept, dual-lens-verified over 3 review cycles): zero `.Produces` on all 17 ops, NO ETag/If-Match work, NO multi-2xx (no matcher changes), NO success-shape polymorphism (no flag-and-defer), NO nullable-complex member (**the nullable-$ref residual stays at 2**).

Refinement: `.claude/refinements/REFINEMENT-retrofit-pass3.md` — **READY; Step-4 review closed over 3 cycles** (cycle 1: Codex 1B/1W — the OQ-defaults joint-executability BLOCKER; Reviewer 0B/2W/6N — the error-body `direction` strike + the non-identical overtime siblings; cycle 2: both CLEAN; scoped cycle 3 on the owner-ruling edits: Codex 2W + Reviewer 2W/4N — the missing repo-method capture, the no-org-column join design, the wrong-op FE-caller fix — all absorbed). All three OQs OWNER-RULED 2026-07-11.

**Explicit exclusions:** NO `.Accepts`/request-class changes; NO error-shape typing (the employee-approve 422 pair + reopen's discriminated 409 stay untyped; the L4 `kind` discriminator gets an AUDIT + verdict only — Reviewer pre-verdict: likely not broken, `useSkema.ts:93-111` synthesizes `kind` FE-side); NO wire-byte change on any EXISTING op; NO auth-path change on any existing op (every `RequireAuthorization` string byte-identical — P7); NO handler-logic change outside TASK-11601's new endpoint; `GET /api/compliance/{employeeId}/period` OUT (compliance family); Pass 4+ (payroll/settlement → config → employee-facing) NOT in this sprint.

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| Gate baseline | GREEN | convention 49 typed / 87 grandfathered; drift+freshness in sync at `eeb5e66` (S115 close `ddede73` CI GREEN `29144794028`) |
| Grounded per-op truth | fact sheet (sweep, dual-lens re-verified) | All 17: single success status each (overtime create = true 201); bare arrays: pending/by-month (byte-identical 9-field element — ONE shared record), GET `/{employeeId}` (WIDER 14-field — own record), overtime per-employee list (10-field); envelopes: team-overview (`{employees}`, 18-field handler-assembled row — the fragile one), allocation-breakdown; `{periodId,status}` shared by submit/approve/employee-approve/reopen; reject adds `reason` (sibling); delegate GET = STABLE key set across active/inactive branches (null-vs-populated, NOT polymorphic); DELETE delegate = genuine 200 `{revokedCount}` (the S115 DELETE-vikar precedent) |
| Sibling shapes that must stay SEPARATE records | 3 pairs pinned | overtime approve `{id,status,approvedBy,reason}` vs reject `{id,status,reason}`; create-201 (7f) vs per-employee element (10f); per-employee element (10f) vs the NEW admin-list element (11f, +non-null `employeeName`) — an optional shared member would ADD `employeeName: null` to the existing wire |
| Retry-wrapped ops | 5 | approve/reject/reopen + delegate POST/DELETE run inside `TreeRootDriftRetry.RunAsync` — record swap INSIDE each retry lambda (S115 `MapLineResponse` precedent) |
| Multi-return-site ops | enumerated | team-overview 2 (empty-roster `:818` + assembled `:1130`); pending/by-month 2 each (my-reports vs scope); delegate GET 2 — mapping tables must cover EVERY site |
| Enum authorities (OQ-3 FINAL) | cited | period `status` `init.sql:867`/`1103` (5-state incl. EMPLOYEE_APPROVED; + the synthetic team-overview `"DRAFT"`); `periodType` `init.sql:866`; overtime `status` `init.sql:1858`. **NOT `direction`** — 422-error-body-only (excluded shape) |
| The NEW endpoint's plumbing (P7) | captured pre-plan | `overtime_pre_approvals` has NO org column (`init.sql:1850-1861`) → scope admission rides `⋈ users ON user_id=employee_id` keyed on `users.primary_org_id`; the SAME join carries `users.display_name` → non-null `employeeName`; a NEW all-status scope-bounded repo method is required (existing repo: GetById/GetByEmployeeAndPeriod/GetPendingByEmployees[PENDING-only]); the `pending` ADMISSION loop (scope iteration, GLOBAL vs ORG_ONLY, dedupe — `ApprovalEndpoints.cs:616-687`) mirrors verbatim, the SQL does not |
| FE lie inventory | 4 hits | L1 `MyPeriods.tsx:104,110` `post<ApprovalPeriod>` overclaims (`{periodId,status}` reality); L2 two competing `ApprovalPeriod` interfaces (`types.ts:71` 18f with 4 phantom members vs `MyPeriods.tsx:6` 14f — the LOCAL one exactly matches the backend and sets the consolidation direction); L3 the dead route (this sprint's OQ-2 fix); L4 the coverage-422 `kind` (audit-only) |
| NO-FE-CALLER ops | 2 | overtime per-employee list GET + create POST — backend-typed + asserted only; they STAY uncalled (the page's repaired read points at the NEW endpoint, NOT the per-employee route — Reviewer W4) |
| Test-home census | mapped | Approval suites hold the seed scaffolding (`ApprovalConcurrencyHardeningTests`/`S94FlatApprovalTests`/`ApprovalAtomicTests` drive periods through the full state machine); NO existing per-route contract test for pending/by-month/GET-{employeeId}/overtime-list — gaps this sprint fills; new seeds MUST be disjoint from those suites' fixtures (S115 lesson, AC-pinned) |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — P7 (the NEW authority-bearing list endpoint) |
| **External Codex** | 2 cycles (2026-07-11): cycle 1 = **1 BLOCKER / 1 WARNING / 3 verifying NOTEs** — the BLOCKER: the users-join admission had NO `is_active` predicate → a terminated employee's rows would derive an in-scope org and render with `employeeName` (grounded: the approval roster/list surfaces DO filter active); the WARNING: 11600's gate criteria unevaluable under the one-regen rule. Gate math independently verified against `check_openapi_convention.py` (66/70 → 67/70; born-typed never enters the manifest). Cycle 2: **"Clean — plan ready for Step 1."** |
| **Internal Reviewer** | 2 cycles (2026-07-11): cycle 1 = **0 BLOCKER / 2 WARNING / 4 NOTE** — W1 = the SAME `is_active` hole, CONVERGENT with Codex's BLOCKER, plus the decisive see==act tiebreaker (the act path is fail-closed for inactive targets → phantom Godkend/Afvis buttons) and the pending-outlier analysis (those queries never join `users` — the outlier does not transfer); W2 = the regen-timing conflict (→ regen-twice); N1 TeamOversigt 3-call-sites-by-call-form; N2 current-org attribution recorded for Step-7a (accepted consequence of the no-org-column design); N3 empty-scopes-403 + dedupe promoted to criteria; N4 RED-test authorship pinned (11603 authors, 11601 consumes). Cycle 2: **all 6 CLOSED, 0 new findings — CLEAN.** |
| **BLOCKERs resolved before Step 1** | **yes — the convergent `is_active` finding pinned in 11601 (join predicate + inactive-exclusion RED-first test) before any code; all WARNINGs/NOTEs absorbed** |

---

### TASK-11600 — Backend: the 17-op response typing + manifest drain + enums
| Field | Value |
|-------|-------|
| **ID** | TASK-11600 |
| **Status** | complete (2026-07-14) — all 17 ops typed as exact shape-copies (field-mapping tables delivered for EVERY return site incl. team-overview's 2, pending/by-month's 2 each, delegate GET's 2; BI=yes on all); the 3 sibling-pair pins honored (separate records); the 5 retry-lambda swaps clean; 3 enums cited + emitted (`direction` correctly NOT enumerated); build 0 err; existing Contracts tests 88/88 UNMODIFIED at both checkpoints; sha-idempotent regen ×2. **⚠ DECLARED DEPENDENCY (the agent stopped rather than improvised — correct behavior):** POST approve + employee-approve bind NO request DTO, and the gate's body-verb rule deliberately fails body-less POSTs (the "forgot the DTO" tripwire) with no request-side analog of the declared-204 amendment → their RESPONSES were typed but their manifest lines restored with a comment (checkpoint read 64+1 typed / 72). **RESOLUTION (owner-ratified 2026-07-14): the gate grew the "declared body-less POST" rule** — `tools/openapi-bodyless-declared.txt` (explicit list; the S112 declared-204 request-side analog; Orchestrator-implemented as gate infrastructure): a listed op passes IFF its response is typed; unlisted body-less POSTs still trip; declared+untyped stays RED (liveness); stale declarations FAIL. Selftest extended with all four directions (8/8 ok). The 2 manifest lines deleted → **the gate reads 67 typed / 70 grandfathered / 2 declared body-less — the planned landing point.** The amendment is flagged for Step-7a review (a mid-sprint gate-semantics change). |
| **Agent** | Backend/API |
| **Components** | ApprovalEndpoints, ReportingLineEndpoints (delegate trio), OvertimeEndpoints, Contracts/, tools/openapi-convention-exempt.txt, docs/api/openapi.json, frontend/src/lib/api-types.ts (regen) |
| **KB Refs** | PAT-012 (the paved road + the S115 recipe notes), PAT-010 |

**Description**: Named response records in `Contracts/` (new `ApprovalResponses.cs`, `DelegationResponses.cs`, `OvertimePreApprovalResponses.cs`) — EXACT shape-copies of the HANDLER's anonymous objects (never the repo model), one record per NON-identical shape (the 3 pinned sibling pairs stay separate; pending/by-month share ONE record; submit/approve/employee-approve/reopen share ONE `{periodId,status}` record; reject gets the `+reason` sibling). `.Produces<T>(code)` per op — bare arrays as `IEnumerable<T>`; overtime create declares 201; DELETE delegate declares 200 (genuine body). The 5 retry-wrapped ops swap the record INSIDE the `TreeRootDriftRetry` lambda. `[AllowedValues]` on the three cited closed sets ONLY (period status [5-state + synthetic DRAFT on the team-overview row record], periodType, overtime status). Manifest drain 87→70. Regen spec + `api-types.ts` (sha-idempotence). Field-by-field mapping tables for ALL 17 ops covering EVERY return site (team-overview 2, pending/by-month 2 each, delegate GET 2). NO `.Accepts`; NO error-shape `.Produces`; NO handler-logic change; every `RequireAuthorization` string byte-identical.

**Validation Criteria**:
- [ ] Build 0 err/0 warn; convention gate 66 typed / 70 grandfathered / 0 stale; drift + freshness green
- [ ] Mapping tables delivered (17 ops, every return site); existing contract tests green UNMODIFIED
- [ ] P7 diff audit: zero auth/logic hunks — response construction + `.Produces` metadata only

---

### TASK-11601 — Backend (product, P7): the NEW scope-bounded overtime admin list endpoint
| Field | Value |
|-------|-------|
| **ID** | TASK-11601 |
| **Status** | complete (2026-07-14) — `GET /api/overtime/pre-approvals` live: `LeaderOrAbove`; the `pending` admission loop mirrored verbatim in structure (empty scopes → 403; per-scope GLOBAL vs ORG_ONLY; HashSet dedupe by id); NEW repo method `GetAllScopedWithEmployeeNamesAsync(string? orgId)` on the concrete repository (no interface exists in this codebase — the "(+ interface)" instruction had nothing to attach to; existing style followed) with **`is_active = TRUE` in the JOIN predicate (the Step-0b convergent pin)**, all-status, `ORDER BY created_at DESC`; `users.display_name` is `NOT NULL` (init.sql:498) → `EmployeeName` non-null BY CONSTRUCTION; the NEW 11-field `OvertimePreApprovalAdminListItem` record SEPARATE from the per-employee element; typed from birth (never entered the manifest; counts in the 67). Drift green (102 paths / 124 schemas); sha-idempotent. Its RED-test criteria are consumed from TASK-11603 (authorship split per Step-0b N4). |
| **Agent** | Backend/API |
| **Components** | OvertimeEndpoints, OvertimePreApprovalRepository, Contracts/, spec+types regen |
| **KB Refs** | PAT-012 (typed-from-birth), docs/SECURITY.md (scope admission), the S106 bounded-enumeration lesson |

**Description**: NEW `GET /api/overtime/pre-approvals` — `LeaderOrAbove`, admission mirroring the `GET /api/approval/pending` scope-aggregate loop VERBATIM (empty-scopes 403; per-scope GLOBAL vs ORG_ONLY; dedupe). NEW repo method: all-status, scope-bounded enumeration via `overtime_pre_approvals ⋈ users ON users.user_id = employee_id **AND users.is_active = TRUE**` (Step-0b CONVERGENT finding — Codex BLOCKER + Reviewer W1: the users join is the ADMISSION SOURCE — the table has no org column — and without the active predicate a terminated employee's rows still derive an in-scope org. Precedents: the S106 scoped reads + user-search filter active [`ApprovalPeriodRepository.cs:583`, `:821`, `:1086`, `:1184`]; the `pending` repo queries do NOT — but they never join `users` at all [`approval_periods` carries its own `org_id`], so that outlier does not transfer. **The decisive tiebreaker (Reviewer): see==act — the approve/reject act path is fail-closed for inactive targets** [`ValidateEmployeeAccessAsync` → `UserRepository.GetByIdAsync` `WHERE is_active = TRUE`], so an unfiltered list would render Godkend/Afvis buttons that ALWAYS 403 — the S105 see≠act inconsistency class. A deliberate terminated-aware/historical extension would be S70-allowlist territory, NOT this endpoint's default. **N2 consequence, recorded for Step-7a: org attribution is CURRENT-org** — after a cross-org transfer, an employee's historical pre-approvals list under the new org [`users.primary_org_id` is the only org source]; inherent to the no-org-column design, accepted.) Org derivation on `users.primary_org_id`; `users.display_name` rides the same join. Response: bare array of a NEW 11-field record = the per-employee 10-field core + **non-null `employeeName`** (a SEPARATE record — never shared with the per-employee element). Typed from birth: record + `.Produces<IEnumerable<T>>` + spec + types + NEVER enters the manifest. This is the sprint's ONLY new handler logic.

**Validation Criteria**:
- [ ] The scope-boundedness RED test passes RED-first (an out-of-scope employee's pre-approval must NOT appear; a GLOBAL actor sees all; an ORG_ONLY actor sees exactly their org's) — **AUTHORED by TASK-11603** (tests/ is its scope); this criterion reads "passes against 11601's implementation"
- [ ] **The inactive-exclusion RED test passes RED-first** (a deactivated employee's pre-approval must NOT appear in any actor's list — the Step-0b convergent pin; authored by 11603)
- [ ] Empty-scopes → 403; multi-scope actor → deduped rows (both asserted by 11603)
- [ ] All three statuses enumerable (not PENDING-only); `employeeName` non-null proven against the seeded rows
- [ ] Convention gate: the new op counts TYPED (67) from birth

---

### TASK-11602 — FE: the typed switch + the page repair + lint tiers (depends: 11600+11601)
| Field | Value |
|-------|-------|
| **ID** | TASK-11602 |
| **Status** | complete (2026-07-14, resumed after a session-limit interruption) — all 21 consumed call sites switched (full table delivered; call-form audit: 22 typed literal-key calls + the 2 sanctioned helper-pinned legacy skema calls; zero unsanctioned explicit-T/template-literal remain). **Named request deltas (S112 precedent): employee-approve's `{}` body → NO body (×2 call sites; the handler binds no DTO — never read; wire-pinned `body === undefined`).** L1 closed (both MyPeriods posts derived `{periodId,status}`; consumers already discarded — no phantom-field reader existed); L2 closed (BOTH hand-written `ApprovalPeriod` variants DELETED; `useApprovals` migrated to the spec `ApprovalPeriodListItem`; honest delta surfaced: `delegatedEmployees[].displayName` is `string\|null` — the hand-written claimed non-null); the `PreApproval` interface deleted (it invented non-null `reason`, omitted `approvedBy/approvedAt`). **The page repair: the list read now hits the NEW endpoint (same URL string — the route now exists; W4 honored) + the page's FIRST working-read wire test (4 tests).** L4 verdict: **NOT broken, confirmed** (the coverage-422 has no `kind`; the FE synthesizes it from `Array.isArray(missingDays)`; only the allocation branch reads the backend `kind`) — recorded, nothing touched. Lint: 7 files joined the FULL tier; **`useSkema.ts` = a PARTIAL tier with cause** (its skema month GET + save POST ride grandfathered UNTYPED ops — no typed form exists until a later pass; the two calls pinned by `SKEMA_MONTH_PATH`/`SKEMA_SAVE_PATH` route helpers — the S115 `ELIGIBILITY_PATH` precedent, second firing); `package.json` list +8. RED-probes 8/8 files (exit 1 → revert → 0) + the useSkema non-helper and as-cast probes. **Validated: tsc 0; lint 0; vitest 589/589 (+22: the typed-wire suite 13 incl. the no-body pins, the page's 4, TeamOversigt 3, MyPeriods 2); the phase pin updated 18 POSTs/13 PUTs/8 DELETEs.** Noted for the owner: the page's empty-state copy ("Ingen ventende godkendelser") is now slightly imprecise — the new list is all-status (UI-copy nit). 1 PROPOSED PAT-012 note (the partial-tier pattern's second firing) → 11604. |
| **Agent** | UX/Frontend |
| **Components** | useApprovals, useTeamOverview, useAllocationBreakdown, useDelegation, useSkema (approval slice), MyPeriods.tsx, TeamOversigt.tsx, OvertimePreApprovalManagement.tsx, types.ts, eslint.config.mjs, package.json (lint list) |
| **KB Refs** | PAT-012 (call-form audit; the S115 route-helper lint pin precedent) |

**Description**: Switch ALL consumed call sites to typed forms — verified by CALL FORM, not compilation (the silent-fallback lesson). L1: `MyPeriods.tsx` post overclaims → typed posts derive `{periodId,status}`; any consumer of phantom fields gets honest consumption (never a wire widening). L2: consolidate onto spec-derived types; DELETE the `types.ts:71` `ApprovalPeriod` phantom variant + the superseded hand-written interfaces (direction: the truthful LOCAL 14-field shape ≡ the spec type). **The page repair (OQ-2): `OvertimePreApprovalManagement.tsx` list read re-points at the NEW `/api/overtime/pre-approvals` via the typed form** (NOT the per-employee route — W4); `PreApproval` interface deleted; the two PUT call-sites switch (bodies stay discarded); the page joins the lint tiers. L4 AUDIT: verify the `useSkema.ts:93-111` coverage-narrowing verdict (expected: not broken; record it; NO wire change either way). Lint tiers extended: the 5 hooks + `MyPeriods.tsx` + `TeamOversigt.tsx` (hosting direct calls) + `OvertimePreApprovalManagement.tsx`; `package.json` lint list updated. Vitest wire pins for the switched suites incl. the repaired page (the list renders — it 404s today).

**Validation Criteria**:
- [ ] tsc 0; lint 0 with the new tiers RED-probed (a violating explicit-T call in each new tier file fails)
- [ ] No remaining explicit-T/hand-written response interface on the switched surface; L1/L2 closed; the L4 verdict recorded; **TeamOversigt's 3 mutation call-sites (approve/reject/reopen) verified switched BY CALL FORM** (stripping the type arg to a bare legacy call passes lint — only the call-form audit catches it; Reviewer N1)
- [ ] Vitest green (delta counted); the repaired page's wire test proves the NEW route + working render path

---

### TASK-11603 — Test & QA: per-route assertions for 17 drained + 1 new (depends: 11600+11601)
| Field | Value |
|-------|-------|
| **ID** | TASK-11603 |
| **Status** | complete (2026-07-14, resumed after a session-limit interruption) — 3 per-family Docker classes, **27 tests / 17 drained ops + the NEW endpoint**, all green (Approval 13 — all statuses driven through the REAL state machine incl. the null-submittedAt path via reopen; team-overview BOTH return sites; the 14-field element's 3 nullables in both states; Delegation 4 — the stable-key-set inactive branch asserts KEYS-present-with-nulls, DELETE asserted 200-with-`{revokedCount}`-not-204; OvertimePreApproval 10 — create asserted 201 exactly; the reject sibling asserts `approvedBy` ABSENT from the wire). **The P7 RED-first proofs delivered verbatim (inverted-assertion evidence): scope-boundedness (the ORG_ONLY leader's list contains exactly its org's 3 rows; the out-of-scope row genuinely absent) + inactive-exclusion (the deactivated employee's row absent from the GLOBAL list; deactivation via the REAL admin path — `PUT /api/admin/users/{id}` `{isActive:false}` + If-Match, no SQL flip).** Empty-scopes→403 + multi-scope dedupe named tests; the RED-on-lie proof (bare-array 200 vs an injected object schema → the matcher throws array-ness). Seed disjointness proven by census: `S116*` MAOs/orgs/users vs the three named suites' `STY02/STY05` + `s78_*/s94_*/EMP_FR_AP_*` — zero overlap, plus per-class testcontainer isolation. **Validated: scoped 27/27 (1m52s); composed Contracts 115/115 (88 pre-existing stay green).** Matcher/Support consumed AS-IS. |
| **Agent** | Test & QA |
| **Components** | tests/StatsTid.Tests.Regression/Contracts/ (new per-family classes) |
| **KB Refs** | PAT-012 (gate #1 per-route), FAIL-002 |

**Description**: Per-route spec≡runtime Docker assertions: 3 new classes (Approval / Delegation / OvertimePreApproval). Approval periods seeded through the REAL state machine (submit → employee-approve → approve/reject → reopen; reuse the existing suites' scaffolding patterns but with **provably DISJOINT seeds — separate Organisations** from the `ApprovalConcurrencyHardeningTests`/`S94FlatApprovalTests`/`ApprovalAtomicTests` fixtures). Cover: both delegate-GET branches (active + inactive — same key set, null vs populated); the DELETE-delegate 200 body; team-overview BOTH return sites (empty roster + assembled, incl. the nullable scalars: null `periodId` on zero-period DRAFT rows, `decisionAt`/`rejectionReason` state-dependence); enum fidelity exercised on status/periodType; the NEW endpoint's scope-boundedness RED test + the inactive-exclusion RED test (a deactivated employee's row absent) + the empty-scopes 403 + the multi-scope dedupe + all-status + non-null `employeeName` (11603 AUTHORS all of these — 11601's criteria consume them); one RED-on-lie proof (injected wrong-shape spec → the matcher throws). NO matcher/Support changes (consumed AS-IS). No full-regression runs (scoped filters only).

**Validation Criteria**:
- [ ] All new Docker assertions green first-run (exact counts); the scope RED test demonstrated RED-first; the RED-on-lie proof demonstrated
- [ ] Seed disjointness verified (no shared org/employee ids with the three named suites)

---

### TASK-11604 — Orchestrator: PAT-012 update + validation + Step 7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-11604 |
| **Status** | complete (2026-07-14) — PAT-012 amended (the declared-bodyless rule on the paved road step 3; scope status → 67/70/2 + the Pass-4 buckets + the born-typed first + the partial-tier second firing; the FE-lie tally 3 prod bugs + 7 lies); the gate amendment implemented + owner-ratified + dual-lens-reviewed (the Step-7a convergent double-membership catch fixed + selftest direction 5); validation delta table below; Step 7a converged 2 cycles both lenses 0 BLOCKER; the `design_handoff_*` dirs left untracked with cause (owner assets — the W2 disposition); close via the explicit file set. Open owner nits recorded: the admin-list empty-state copy; the optional `design_handoff_*/` gitignore line. |
| **Agent** | Orchestrator |
| **Components** | PAT-012, sprint log, INDEX, close gates |
| **KB Refs** | PAT-012, FAIL-003 |

**Description**: PAT-012: scope status → **67 typed / 70 grandfathered** + the Pass-4 buckets (payroll/settlement [mind the nullable-$ref multiplication] → config ~35 → employee-facing ~30); record the OQ-2 born-typed product op (the first NEW endpoint authored on the paved road inside the program); the nullable-$ref residual re-asserted at 2. Validation (`sprint-test-validation` — full suites + delta table). Step 7a dual-lens (adversarial focus: wire-byte identity on all 17 + the NEW endpoint's scope boundedness). Close per the 5 gates; ONE close commit with the EXPLICIT sprint file set (never `git add -A` — the S115 lesson); push; CI-verify all 7 jobs; the docs-only CI backfill.

**Validation Criteria**:
- [ ] PAT-012 updated; delta table arithmetic verified; Step 7a converged; close + push + CI green all 7 jobs

---

## External Review (Step 7a)

Dual-lens, both cycle-1 → cycle-2 converged (2026-07-14). Artifacts: `.claude/reviews/SPRINT-116-step7a-{codex,reviewer}.md`.

| Lens | Cycle 1 | Cycle 2 (fix verification) |
|------|---------|---------------------------|
| External Codex (`codex review`, prompt-steered) | **0 BLOCKER / 1 WARNING / 0 NOTE** | **"Clean — the gate hole is closed"** (paths traced + gate/selftest run live) |
| Internal Reviewer | **0 BLOCKER / 2 WARNING / 6 NOTE** | **W1 CLOSED on all three axes** (incl. an independent `evaluate()` probe of the TYPED double-membership direction — two independent FAIL paths); **W2 disposition ADEQUATE** (corroborated); no new findings; 1 optional NOTE |

**The CONVERGENT WARNING (both lenses, FIXED):** the declared-bodyless list had no mechanical disjointness with the grandfather manifest — an op on BOTH lists with an untyped response was accepted as grandfathered (the waiver + the manifest together masked exactly the declared+untyped case the liveness rule keeps RED). Fixed in `check_openapi_convention.py`: a declared key is INELIGIBLE for grandfathering; double membership itself is a first-clause `bodyless_stale` FAIL regardless of typed-ness; selftest direction 5 proves not-grandfathered + failed + flagged-stale. [[review-lens-complementarity]]: the same hole from two independent priors — the amendment's fourth review catch this sprint (Step-0b caught the `is_active` admission hole; Step-7a caught the double-membership bypass).

**Reviewer W2 (close-process, disposition recorded):** the four untracked `design_handoff_*` dirs are PRE-EXISTING owner design assets (zero tracked history; mtimes weeks pre-S116; the S115 close deliberately unstaged the same set) — **left untracked with cause; the S116 close stages an EXPLICIT file list (never `git add -A`)**. Optional follow-up (owner's call): a `design_handoff_*/` gitignore line to make the disposition mechanical.

**Accepted NOTEs:** the RED-first proofs verified-by-claim at review time (the regression held the Docker lock; the inverted-assertion evidence is verbatim in TASK-11603's report); the stale-message wording covers method-changes implicitly (cosmetic); `DelegationPage`'s null-displayName empty prefix (the old type lied — runtime unchanged; copy nit); the admin-list empty-state copy imprecise for all-status (owner nit); the multi-scope redundant fetch mirrors `pending` verbatim (correct per the pin); the `AGREEMENT_CODES` widening equivalent.

**Confirmed SOUND by both lenses:** wire-byte identity on all 17 ops at every return site; the 5 retry-lambda swaps (attempt-2 ≡ attempt-1); the sibling-record separations incl. the wire-absence assert on reject's `approvedBy`; the enums (3 sets only, authorities verified; the nullable-`$ref` residual counted in BOTH specs = exactly 2, identical set); the new endpoint's P7 (admission line-equivalent to `pending`; parameterized SQL; `is_active` in the JOIN; fail-closed joins; no route ambiguity); the auth census (+1 `LeaderOrAbove` = the new endpoint only); the FE call-form audit (zero unsanctioned calls; the helper pins exact; no dangling imports; the no-body deltas wire-pinned and handler-verified).

## Phase Plan
- **Phase 1 (sequential, one Backend agent):** TASK-11600 → TASK-11601 (same files: OvertimeEndpoints + Contracts). **Regen TWICE (Step-0b Codex WARNING fix): 11600 regens + gate-validates independently (66 typed / 70 grandfathered), then 11601 regens again (67 / 70)** — regen is deterministic and sha-idempotent, and independent 11600 validation is worth the second run. Phase 2 consumes the POST-11601 spec/types.
- **Phase 2 (parallel):** TASK-11602 (FE — needs the regen'd types) ∥ TASK-11603 (assertions — needs the spec; file-disjoint: frontend/ vs tests/)
- **Phase 3:** TASK-11604 (docs + validation + close)

**Atomicity pin:** ONE close commit; no push mid-sprint; gates evaluated at close; stage the explicit sprint file set.

## Test Summary (close, 2026-07-14)

| Suite | Previous (S115) | Current | Delta |
|-------|-----------------|---------|-------|
| Unit | 861 | 861 | 0 |
| Regression | 1234 | 1261 | +27 |
| Smoke | 6 | 6 | 0 |
| DemoSeed | 55 | 55 | 0 |
| Frontend | 567 | 589 | +22 |
| **Total** | **2723** | **2772** | **+49** |

Delta composition: regression +27 = the 3 S116 per-route Docker contract classes (Approval 13 + Delegation 4 + OvertimePreApproval 10, incl. the new endpoint's P7 RED-first pair); FE +22 = the typed-wire suite (13, incl. the no-body-delta pins), the repaired page's first working-read suite (4), TeamOversigt call-form pins (3), MyPeriods pins (2). Full-run integrity: 1260 first-pass + 1 pre-existing FAIL-002 shed isolation-cleared (`Adr032ConsumptionPinTests`, Skema surface — not S116); the composed Contracts suite 115/115 (the pre-existing 88 stay green).

## Legal & Payroll Verification
| Check | Status |
|-------|--------|
| Agreement rule compliance | N/A — types + tests + one read-only list endpoint; zero rule-path change |
| Wage type mapping correctness | N/A |
| Event sourcing / audit | N/A — no event/audit-path change (the new endpoint is a READ; no state transition) |
| Security (P7) | ACTIVE — the new endpoint is authority-bearing enumeration: Step-0b MANDATORY + the scope RED test + Step-7a adversarial probe; all EXISTING policy strings byte-identical |
