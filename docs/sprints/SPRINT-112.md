# Sprint 112 — Typed API Contract retrofit, Pass 1: the merged-admin mutation surface

| Field | Value |
|-------|-------|
| **Sprint** | 112 |
| **Status** | complete — CI GREEN `28589103053` (all 7 jobs, 2026-07-02) |
| **Start Date** | 2026-07-02 |
| **End Date** | 2026-07-02 |
| **Orchestrator Approved** | yes — 2026-07-02 (Step 7a BOTH lenses cycle-1 converged: Codex CLEAN, Reviewer APPROVED 0B/0W/7N) |
| **Build Verified** | yes — `dotnet build` 0 errors; `npm run build` 0 errors; `tsc --noEmit` clean; `npm run lint` clean (extended no-`as` surface) |
| **Test Verified** | yes (local): 852 unit + **1196 regression** (central 1148 + the 42 fixed-port tests 42/42 vs a FRESH compose Postgres [the compose DB was DOWN during the central run — environmental, per the S105 fixed-port protocol] + 6 FAIL-002 sheds isolation-cleared 43/43 with classmates) + 29 demoseed + **553 fe** (531+22); 6 smoke + e2e ride CI per the S107+ protocol; all 4 OpenAPI gates + `check_endpoint_contracts.py` green locally; **pyramid 852u+1196r+6s+29demoseed+553fe = 2636 (+51 vs S111: +29 regression [7 matcher + 22 per-route] +22 fe)**; **CI GREEN `28589103053` (all 7 jobs — the full regression, smoke, e2e, and all 4 OpenAPI gates on the drained manifest verified in CI)** |

## Sprint Goal
Pass 1 of the retrofit (PAT-012, [[typed-api-contract-program]]): drain the **20-op merged-admin mutation surface** from the convention-gate grandfather manifest (130→110) end-to-end — named response records + `.Produces`, spec + generated-types regen, FE call-sites switched to typed forms, per-route spec≡runtime assertions — plus the two enablers the slice needs (typed `post`/`put`/`delete` overloads on `apiClient`; a typed If-Match overload on `apiFetchWithEtag`) and the owner-ratified **declared-204 gate amendment**. PAT-010 byte-identity holds throughout: metadata + types only, zero wire-JSON change.

Refinement: `.claude/refinements/REFINEMENT-retrofit-pass1.md` — **READY, owner-ratified 2026-07-02** (Q1 = the 20-op slice [option a]; Q2 = amend the gate so a *declared* 204-no-content is convention-compliant [option a]). Dual-lens refinement review CLOSED at cycle 2: Codex 3B/2W → RESOLVED/0 new; Reviewer 1B/3W → APPROVED-WITH-WARNINGS, the 1 WARNING (the `useUnitMutations.ts:89` collision — at Step 0b empirically re-verified as a SILENT FALL-THROUGH, not a compile break) absorbed into scope.

**The slice (all 20 verified present on the manifest):** units — PUT `/api/admin/units/{id}`, PUT `…/move`, DELETE `…/{id}`, POST `…/{id}/leaders`, DELETE `…/leaders/{userId}` (5); organizations — POST, PUT `/{orgId}`, PUT `…/move`, DELETE (4); users — POST `/users`, PUT `/{userId}`, PUT `…/unit`, GET `/users/search`, GET `/{userId}`, GET `…/roles` (6); roles — POST `/grant`, POST `/revoke` (2); employee-profiles — GET, PUT, DELETE (3). 16 carry bodies; 4 are body-less 204 DELETEs (drain via the gate amendment).

**Explicit exclusions:** NO request-class/DTO conversion, NO `.Accepts` (Swashbuckle inference is truthful by construction — Reviewer-verified the `$ref` requestBody schemas already exist in the committed spec AND `api-types.ts`); NO error-shape (400/404/412/422) typing; NO strict-types work (`required`/enums/`coerceApiResponse` — separate deferred phase); NO wire-byte or field changes of any kind ("while I'm here" additions are backlog items); Pass 2+ (reporting-lines 13, employee field-endpoints 8, remaining admin reads 3, then approval → payroll/settlement → config → employee-facing) NOT in this sprint.

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| `check_docs.py` | GREEN | db-schema in sync (67 tables); KB INDEX complete (58 entries incl. new FAIL-003); sprint inventory through S111 |
| Convention gate baseline | GREEN | 6 typed / 130 grandfathered (manifest count verified = 130) |
| Post-close debt | ABSORBED pre-sprint | FAIL-003 (the S111 omitted gate-#1 files): fix-forward `2571550` CI-verified `28567810051`; the close-guard untracked-source gate live (harness 14/14); `e667d1d` CI in progress at plan time — must be GREEN before close (gate 2 enforces) |
| FE mutation call-sites | inventoried | get 47 / post 22 / put 8 / delete 4; `useUnitMutations.ts:89` (the S111 proof op, no explicit `T`, raw-body arg) **SILENTLY FALLS THROUGH to the untyped fallback** when the typed overloads land — Reviewer-verified empirically (scratchpad repro, tsc clean): NOT a compile break. The dangerous implication: a call-site the switch misses stays compiling-but-untyped forever → the TASK-11203 sweep checks CALL FORM per slice route, not hand-`T` absence |
| Slice response shapes | CLEAN | Reviewer conditional/polymorphic scan: all slice responses are unconditional anonymous shapes; `GET /users/{userId}/roles` is a bare array (the array-ness sentinel case) |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (per S111 precedent: CI-gate semantics change [the keystone gate's 204 policy] + shared `apiClient` infrastructure + the program's structural-closure machinery) |
| **External Codex** | cycle 1 (2026-07-02): 2B/2W/1N — (B1) freshness gate red at the Phase-2/3 boundary; (B2) 11202's "zero call-site edits" vs the collision; (W) wrong `employeeProfileApi.ts` path; (W) selftest exit-convention ambiguity. **Cycle 2: RESOLVED — 0 new findings** (independently re-repro'd the fall-through: the raw-body literal call is rejected by the structured overload and absorbed by the string fallback) |
| **Internal Reviewer** | cycle 1 (2026-07-02): 0B/3W/6N, APPROVED-WITH-WARNINGS — **empirically OVERTURNED Codex B2** (scratchpad tsc repro: the `useUnitMutations.ts:89` raw-body call FALLS THROUGH silently, no compile break → the real defect is the sweep criterion needing a CALL-FORM check); (W) 11202 criteria unverifiable in Phase 1 (no typed PUT/DELETE pre-regen → synthetic type fixtures); (W) lint extension mis-phased in 11202 → moved to 11203; plus path/KB-ref corrections. **Cycle 2: APPROVED** (3 bookkeeping NOTEs, fixed: 11202 scope leftover; the :16 historical gloss; 6N count) |
| **BLOCKERs resolved before Step 1** | **yes — BOTH lenses converged cycle 2 (Codex RESOLVED/0 new; Reviewer APPROVED)**; plan edits: atomicity pin + no-push window; synthetic fixtures; call-form sweep; lint move; path + KB-ref + selftest-phrasing fixes |

---

### TASK-11200 — The declared-204 gate amendment (owner-ratified Q2a)
| Field | Value |
|-------|-------|
| **ID** | TASK-11200 |
| **Status** | complete (2026-07-02) — `operation_reasons` gained the `declared_204_only` clause (`success_codes == ["204"]` + no content); edge-cases directly executed by the agent (no-success FAILS; mixed empty-200+204 FAILS order-independently; 204-with-schema rides the existing rule; reqbody reason independent); docstrings supersede the S111 "belongs on the manifest" sentence; `--selftest` +2 cases (declared-204-only ACCEPTED / no-success REJECTED), all 6 green, exits 1 by design; **Orchestrator re-ran both commands: `--check` UNCHANGED 6 typed/130 grandfathered; scope verified single-file**. PROPOSED PAT-012 amendment accepted → lands in TASK-11205 |
| **Agent** | Backend (cross-domain authorized: `tools/check_openapi_convention.py`) |
| **Components** | convention gate (CI `docs` job) |
| **KB Refs** | PAT-012, PAT-010 |

**Description**: Amend `operation_reasons` so an operation whose **only** success response is a **declared `204` with no content** is convention-compliant — the `.Produces(204)` declaration is the typed statement "this intentionally has no body". Strictness preserved: an op with NO declared success code still fails; a mixed inferred-empty-`200` + `204` op still fails (the "only" formulation); nothing else relaxes. Supersede the S111 docstring sentence ("a body-less action endpoint belongs on the grandfather manifest") and update the manifest header comment. `--selftest` gains a GREEN case (declared-204-only op) and a RED case (op with no declared success).

**Validation Criteria**:
- [ ] `--selftest` exercises both new cases with the correct OUTCOMES per the tool's documented selftest exit convention (a declared-204-only op is ACCEPTED; an op with no declared success is REJECTED) — outcome-phrased because the existing selftest's exit semantics are its own convention (`check_openapi_convention.py:276-323`), which the agent must follow, not reinvent
- [ ] `--check` on the CURRENT committed spec unchanged (6 typed / 130 grandfathered — the amendment alone drains nothing)
- [ ] Gate + manifest docstrings updated; stale-entry=FAIL untouched

---

### TASK-11201 — Backend: the 20-op response typing + manifest drain
| Field | Value |
|-------|-------|
| **ID** | TASK-11201 |
| **Status** | complete (2026-07-02) — all 20 ops typed, ZERO deferred (no conditional shapes found); 4 new `Contracts/` files (Organization/User/UnitAdmin/EmployeeProfile responses; sibling handlers share ONE record where shapes were identical — org trio, employee-profile GET/PUT); 4 DELETEs = `.Produces(204)` only, spec confirmed 204-ONLY per op (explicit `.Produces` REPLACES the inferred empty 200 — no mixed-op gate trip); creates honest at 201 (org/user/role-grant); `GET /users/{userId}/roles` declared `IEnumerable<>` (bare-array sentinel); `/users/search` stays an ENVELOPE. 20 field-mapping tables delivered (name-identical, zero drops/adds). NOTE: `PUT /users/{userId}/unit` lives in UnitEndpoints.cs (not AdminEndpoints). **Validated (agent + Orchestrator re-ran): build 0 err; convention 26 typed/110 grandfathered/0 stale; drift green (101 paths, 86 schemas = 74 + the 12 new records); Docker Contracts filter 21/21 UNMODIFIED (byte-identity holds on the pinned surface); freshness expectedly RED (the pinned no-push window).** 2 PROPOSED KB entries accepted → PAT-012 amendment in TASK-11205 (`.Produces` replaces inferred 200; shared sibling records) |
| **Agent** | Backend API (cross-domain authorized: `src/Backend/StatsTid.Backend.Api/Endpoints/{UnitEndpoints,AdminEndpoints,EmployeeProfileEndpoints}.cs`, `src/Backend/StatsTid.Backend.Api/Contracts/**`, `tools/openapi-convention-exempt.txt`, `docs/api/openapi.json`) |
| **Components** | Backend.Api endpoints + Contracts |
| **KB Refs** | PAT-012 (paved road), PAT-010 (byte-identity), ADR-037/038 context (the admin surface) |

**Description**: For the 16 body-carrying slice ops: named `Contracts/` response record (an exact shape-copy of today's anonymous object — PascalCase members, Web-defaults camelCase serialization, null-emission unchanged) returned via `Results.Ok/Created(record)` + `.Produces<T>` with **correct collection-ness** (`GET /users/{userId}/roles` → `.Produces<IEnumerable<UserRoleItem>>` — the array-ness sentinel; org/user creates are `201`). For the 4 body-less DELETEs: `.Produces(StatusCodes.Status204NoContent)` declaration ONLY (the declaration replaces Swashbuckle's inferred empty `200` — Reviewer-verified mechanism). **NO `.Accepts`, NO request-class changes.** Regenerate `docs/api/openapi.json` via `--openapi`; delete the 20 manifest lines. **Deliverable per op: a field-by-field mapping table (anonymous member ↔ record member) in the task output** — the Step-7a byte-identity review artifact.

**Validation Criteria**:
- [ ] Build 0 errors; full regression + existing contract tests green UNMODIFIED (a needed test edit = a wire change = BLOCKER)
- [ ] Convention gate: 26 typed / 110 grandfathered, ZERO stale entries
- [ ] `openapi.json` regenerated + committed; drift gate green
- [ ] The 20 field-mapping tables delivered

---

### TASK-11202 — FE enablers: typed body verbs + typed If-Match
| Field | Value |
|-------|-------|
| **ID** | TASK-11202 |
| **Status** | complete (2026-07-02; interrupted mid-run by a session usage limit → resumed from transcript, finished clean) — typed `post/put/delete(pathKey, options)` overloads FIRST + string fallbacks (explicit-`T`/template-URL/raw-body all fall through by constraint/arity — the `:89` fall-through pinned as `acceptedFallthrough`); success-status derivation exported + generic (`200/201`→typed, declared-`204`→`undefined` matching runtime, undeclared-content 200s EXCLUDED by design); `apiFetchWithEtag` typed overload with UPPERCASE method discriminant + `ifMatch`, normalizing into the SAME (url,init) legacy path (412/428/204 protocol byte-identical); runtime structured-vs-legacy discrimination key-subset-based with behavior-coinciding overlaps (`{}` body, `{method}`-only, pre-stringified bodies — all pinned) + 2 documented no-caller residuals. 22 fixture tests (synthetic `FixturePaths` + 6 `@ts-expect-error` tripwires + real-spec PHASE PIN [`put/delete = never`] that 11203 MUST update on regen). **Validated (agent + Orchestrator spot-check): tsc exit 0 / vitest 553 (531+22) / build 0 err — ZERO call-site edits.** PROPOSED KB (typed-overload-family pattern) accepted → PAT-012 in TASK-11205 |
| **Agent** | UX (`frontend/src/lib/api.ts`) |
| **Components** | frontend api client |
| **KB Refs** | PAT-012; ADR-019 (ETag/If-Match contract); the S111 structured-`get` overload pattern |

**Description**: (A) Typed structured `post`/`put`/`delete`(pathKey, `{ params?, query?, body? }`) overloads on `apiClient` — request-body and response types derived from `paths[P][method]` with **success-status-aware derivation** (the S111 helper hard-codes `responses.200`; the slice has `201` creates; `204` → `void`). Fallback-preserving overload ordering (the S111-proven pattern): all ~34 existing untyped mutation call-sites keep compiling with NO edits in this task. The member is `delete` (NOT `del`) — ride it, no rename. (B) A typed structured overload on `apiFetchWithEtag` with an explicit **method discriminant** — `(pathKey, { method, params?, ifMatch?, body? })` (one pathKey hosts multiple verbs, e.g. `/units/{id}` PUT+DELETE) — preserving the ETag envelope + 412 semantics untouched. **Phase-1 verifiability constraint (Reviewer):** the committed spec has ZERO typed PUT/DELETE ops when this task runs (the 6 typed = 5 GETs + POST `/units`), so the typed `put`/`delete` unions are `never` pre-regen — the derivation logic MUST be proven by **synthetic compile-time type fixtures** (a test-local `paths`-shaped type exercising 200/201/204 derivation + the method discriminant), with real-types verification landing in TASK-11203. (The no-`as` lint extension moved to TASK-11203 — extending it here would encode a deliberately-red intermediate lint state.)

**Validation Criteria**:
- [ ] Repo-wide `tsc` clean with ZERO call-site edits (proves fallback preservation — Reviewer empirically confirmed even the `useUnitMutations.ts:89` raw-body call falls through cleanly)
- [ ] Auth/401/`ApiResult` envelope + `apiFetchWithEtag` 412 behavior unchanged (existing vitest pins green)
- [ ] Synthetic type-fixture tests prove the 200/201-typed + 204→`void` derivation and the method discriminant at compile time (real-spec verification = TASK-11203)

---

### TASK-11203 — FE call-site switch (depends: 11201 + 11202)
| Field | Value |
|-------|-------|
| **ID** | TASK-11203 |
| **Status** | complete (2026-07-02) — `api-types.ts` regenerated (+176/−30, idempotence SHA-proven); ALL 20 consumed routes switched (typed-structured / typed-etag / typed-get per the audit table in the task output; 1 NO-FE-CALLER: DELETE employee-profiles); THE SWEEP done (`useUnitMutations` POST `/units` → typed-structured, closing the S111 fall-through); grep-evidenced ZERO residual raw-body/explicit-`T` calls on slice routes (1 deliberate documented survivor on a NON-slice grandfathered route: `GET /organizations/{orgId}/users`); phase-pin block updated; no-`as` lint extended (3 `as` casts eliminated); scope extension REPORTED: users-search lives in `useReportingLines.ts`. **⚠ WRONG-SHAPE FINDINGS (the S97→S100 class, caught by the typed switch — the program's predicted payoff): (1) REAL LIVE PROD BUG — `RoleAssignment` claimed `grantedBy`/`grantedAt`/`userId`, backend serves `assignedBy`/`assignedAt` → RoleManagement's "Tildelt af"/"Tildelt dato" columns rendered BLANK in prod; FIXED. (2) `updateUser` claimed a `username` response field that doesn't exist → every save silently dropped username from drawer state; FIXED (merge-over-snapshot). (3) employee-profiles two-way lie (`weeklyNormHours` phantom both directions); (4) revoke sent an out-of-contract `userId`; (5) `createUser` marked spec-required fields optional. A hand-written FE interface IS a mock — the lie lived in non-test code twice.** **Validated (agent + Orchestrator re-ran): tsc 0; lint 0 (extended surface); vitest 553/553; build 0 err; `check_endpoint_contracts.py` hard checks pass; 412/If-Match pins green UNMODIFIED (`usePlacement` fetch-router pins the real wire).** Orchestrator absorbed the 2 declared stale comments (Small Tasks Exception). PROPOSED KB accepted → PAT-012/FAIL note in TASK-11205 |
| **Agent** | UX (`frontend/src/hooks/useUnitMutations.ts`, `useOrgMutations.ts`, `useAdmin.ts`, `frontend/src/pages/admin/editPerson/employeeProfileApi.ts`, `frontend/src/lib/api-types.ts` [regen], eslint no-`as` config, + slice-consumer components as needed) |
| **Components** | frontend hooks |
| **KB Refs** | PAT-012, PAT-010 (the S97→S100 mock-shape history is codified there), ADR-019 (ETag/If-Match contract) |

**Description**: Regenerate `api-types.ts` (`npm run gen:api`) from the TASK-11201 spec. Switch every consumed slice call-site to the typed forms: the plain mutations onto the typed `post`/`put`/`delete`; the **full If-Match inventory** (units `useUnitMutations.ts:98-128`, users POST/PUT `useAdmin.ts:225,261`, employee-profiles `frontend/src/pages/admin/editPerson/employeeProfileApi.ts:48,77`) onto the typed `apiFetchWithEtag` overload; + `useUnitMutations.ts:89` (POST `/units` — the already-typed S111 proof op whose raw-body call silently rides the fallback), completing the S111 proof op's FE half. Extend the no-`as` lint to all switched hooks (moved here from 11202 — it must land WITH the switch, not before). Mock-shape corrections in tests are REPORTED, not silently fixed (the S99 lesson). **The sweep criterion checks CALL FORM, not hand-`T` absence (Reviewer, empirical):** a missed call-site does NOT fail tsc — it silently falls through to the untyped fallback — so "no hand-`T` left" proves nothing for the raw-body class.

**Validation Criteria**:
- [ ] Freshness gate green (`gen:api` + `git diff --exit-code`); no-`as` lint green on switched hooks
- [ ] Full vitest green; 412/concurrency pins unchanged
- [ ] `npm run build` 0 errors
- [ ] **Call-form sweep:** for EVERY slice route + POST `/units`, the call-site verifiably uses the structured typed shape — asserted by enumerating the slice paths against the 2-arg raw-body/string-path form (grep-able audit in the task output); zero untyped-fallback survivors on slice routes

---

### TASK-11204 — Test & QA: matcher extension + per-route spec≡runtime assertions (depends: 11201)
| Field | Value |
|-------|-------|
| **ID** | TASK-11204 |
| **Status** | complete (2026-07-02; usage-limit interruption → resumed, nothing lost) — `ResolveSuccessContract` (exactly-ONE declared 2xx; 0→untyped throw, ≥2→ambiguity throw; 200/201 need a JSON schema, declared-204 must NOT) + `AssertSuccessMatches` (STATUS FIDELITY: runtime≠declared 2xx is RED; 204→empty-body assert; 200/201→structural match); `Resolve200Schema` kept as a back-compat wrapper (S111 call-sites unchanged). 3 per-FAMILY Docker classes covering ALL 20 ops + 2 RED-on-lie proofs (bare-array-vs-object + 201-vs-spec-lying-200) + dedicated seeded row per mutation (fixed ids; version=1; org-DELETE target employee-free; nullables exercised non-null). **The load-bearing verification: all 20 committed contracts matched runtime EXACTLY — the S112 typing proven faithful, not assumed. Validated (agent + Orchestrator re-ran): matcher 13/13 (6 S111 + 7 new); `Contracts.S112` 22/22 (Docker, first-run, 0 retries); S111 `OpenApiSpecRuntimeTests` 6/6 on the extended matcher; scope tests-only.** PROPOSED KB (single-declared-2xx convention + dedicated-row rule) accepted → PAT-012 in TASK-11205 |
| **Agent** | Test & QA (`tests/StatsTid.Tests.Regression/Contracts/**`) |
| **Components** | spec≡runtime gate (gate #1) |
| **KB Refs** | PAT-012 (gate 1), FAIL-002 (Docker-churn discipline), FAIL-003 |

**Description**: (A) Extend `SpecRuntimeMatcher` from hard-coded `"200"` to the operation's **declared success status** (200/201) + explicit **204 handling** (assert status + empty body); extend the matcher unit tests. (B) Per-route spec≡runtime assertions for all 20 slice ops, consolidated in **per-family fixture classes** (units family / admin-org-users-roles family / employee-profiles family — FAIL-002 discipline, not per-op classes), each mutation asserted against a **dedicated seeded row** (xUnit intra-class ordering is not guaranteed; a DELETE assertion must not invalidate a sibling's row). (C) The RED-on-lie proof extends to a mutation route (an injected wrong array-ness/status `.Produces` fails).

**Validation Criteria**:
- [ ] Matcher unit tests green (incl. 201/204 cases); all new per-route assertions green vs Docker
- [ ] RED-on-lie proof for a mutation route demonstrated
- [ ] No regression-suite interference (dedicated seed rows; per-family classes)

---

### TASK-11205 — Orchestrator: PAT-012 amendment + validation + Step 7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-11205 |
| **Status** | complete (2026-07-02) — PAT-012 amended (declared-204 gate clause; S112-amended paved road [shared sibling records, `.Produces`-replaces-inferred-200, ONE declared 2xx, inference-not-`.Accepts` + all-optional honesty, call-form verification, per-route assertion step]; the retrofit recipe; the Vindication section recording the 2 prod bugs; scope status → 26/110 + Pass-2 candidates); `sprint-test-validation` delta table produced (2636, +51); Step 7a BOTH lenses cycle-1 converged (below); close per the 5 gates |
| **Agent** | Orchestrator (docs are Orchestrator-only) |
| **Components** | PAT-012, sprint log, close gates |
| **KB Refs** | PAT-012, FAIL-003 |

**Description**: Amend PAT-012 with the ratified retrofit recipe: response-side records + `.Produces`; the **inference-not-`.Accepts`** request-side rule (if `.Accepts` is ever used it must match the bound type); the **declared-204 policy**; the per-op field-mapping-table discipline; the honest framing that request-body types are all-optional until strict-types (catches wrong names/shapes, not omissions — do not oversell). Sprint validation via the `sprint-test-validation` skill (delta table); Step 7a dual-lens on the full sprint diff; close through all 5 mechanical gates (Step-7a artifacts, CI-health, CI-pending, **untracked-source** [its first live sprint], harness).

**Validation Criteria**:
- [ ] PAT-012 amended; sprint log complete with the delta table
- [ ] Step 7a both lenses converged (cycle-cap discipline)
- [ ] Close commit through the 5 gates; pushed; CI GREEN all 7 jobs

---

## Phase Plan
- **Phase 1 (parallel):** TASK-11200 (gate amendment) ∥ TASK-11202 (FE enablers — spec-independent, rides the committed `api-types.ts`)
- **Phase 2:** TASK-11201 (backend typing + drain — the manifest deletion of the 4 DELETE lines requires 11200's amended gate to validate locally)
- **Phase 3 (parallel):** TASK-11203 (FE switch — needs 11201's spec + 11202's overloads) ∥ TASK-11204 (assertions — needs 11201)
- **Phase 4:** TASK-11205 (validation + close)

**Atomicity pin (Step-0b, both lenses):** the sprint ships as ONE close commit (the established close-only-push discipline). Between Phase 2 and Phase 3 the LOCAL tree is expectedly freshness-RED (`openapi.json` regenerated, `api-types.ts` not yet) — this is a known intermediate state, NOT a defect to "fix" by early regen without the switch. **NO push between TASK-11201 and TASK-11203**; all four gates are evaluated green at the close commit. Per-task "gate green" criteria refer to the task's OWN gate surface at its completion point (11200: convention selftest; 11201: convention+drift on the regenerated pair; 11203: freshness+lint; 11204: spec≡runtime).

## External Review (Step 7a)
| Lens | Result |
|------|--------|
| **External Codex** (`codex review`, full uncommitted diff) | **cycle 1: "Clean — no findings"** (ran its own scoped checks: convention gate 26/110 green, `tsc --noEmit` clean, `dotnet build` 0 err). Artifact: `.claude/reviews/SPRINT-112-step7a-codex.md` |
| **Internal Reviewer** (same instance as refinement+Step-0b — full program context) | **cycle 1: APPROVED — 0B/0W/7N.** Byte-identity SAMPLED on 12+ ops directly vs the removed anonymous shapes (exact copies; only wire-neutral delta a `.ToList()`); gate rule verified live vs the ratified formulation; the FE interface fixes verified against REAL backend serialization (the RoleManagement blank-columns prod bug confirmed real); S112 Docker classes testcontainers-only (zero fixed-port interference); dedicated-row discipline "exemplary". Artifact: `.claude/reviews/SPRINT-112-step7a-reviewer.md` |

NOTEs absorbed at close: the 2 request-payload deltas (never-bound fields dropped — `weeklyNormHours: 0` placeholder, revoke's ignored `userId` — behavior-identical, forced by the typed body) consciously ACCEPTED; `.codex_diff.txt` deleted; `docs/WORKFLOW.md` (FAIL-003 gate documentation, 14 harness cases) attributed to the pre-sprint absorption; the structured-vs-raw discrimination residuals documented+tested+codified; PAT-012's two S112-introduced conventions (shared sibling records; single-declared-2xx) surfaced to the owner.

## Test Summary
| Suite | S111 (+fix-forward) | S112 | Delta |
|-------|---------------------|------|-------|
| Unit | 852 | 852 | 0 |
| Regression (Docker) | 1167 | **1196** | **+29** (7 `SpecRuntimeMatcherTests` + 22 `Contracts.S112` per-route) |
| Smoke | 6 | 6 (rides CI) | 0 |
| DemoSeed | 29 | 29 | 0 |
| Frontend (vitest) | 531 | **553** | **+22** (typed-overload fixtures) |
| **Total** | **2585** | **2636** | **+51** |

Regression detail: the central 1h06m run finished 1148/1196 — 42 failures were the fixed-port `ReportingLineRepositoryTests`+`ManagerVikarEngineTests` (they target the compose Postgres at `localhost:5432`, which was DOWN; re-run 42/42 in 963 ms against a fresh compose DB — the S105 protocol) and 6 were FAIL-002 churn sheds (`SkemaEntitlementEligibilityGuard`/`TerminatedEmployeeAccess`/`SettlementCloseServiceBoundary`, 2 each; isolation re-run 43/43 with classmates). Zero S112-attributable failures.

## Legal & Payroll Verification
| Check | Status |
|-------|--------|
| Agreement rule compliance | N/A — no rule-engine, agreement, or payroll surface touched (metadata + types + tests only) |
| Wage type mapping correctness | N/A — untouched |
| Event sourcing / audit | N/A — no event or audit-path changes; endpoints' behavior byte-identical |
