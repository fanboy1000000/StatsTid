# Sprint 119 — Typed API Contract retrofit Pass 6: bucket B complete (config + projects)

| Field | Value |
|-------|-------|
| **Sprint** | 119 |
| **Status** | complete |
| **Start Date** | 2026-07-21 |
| **End Date** | 2026-07-21 |
| **Orchestrator Approved** | yes (2026-07-21) |
| **Build Verified** | yes — build 0 err ×2; convention gate 117/20/9/0; sha-idempotent ×2 (TASK-11900) |
| **Test Verified** | yes — 2886 total (861u + 1327r + 6s + 55ds + 637fe), all suites locally green 2026-07-21 (7 FAIL-002-class sheds isolation-cleared 39/39; see Test Summary) |

## Sprint Goal
Retrofit Pass 6 (PAT-012, [[typed-api-contract-program]]): drain **bucket B COMPLETE — the 14-op org-config + projects surface** (config 7: constraints, effective-config, absence-types GET + visibility POST, profile GET/history/PUT; projects 7: list, create, update, delete, available, select, deselect). **Manifest 34→20; typed 103→117; declared body-less 8→9** (the select POST — binds no DTO). **ZERO owner-ruled wire changes — the first zero-delta pass since S116**: every response an exact shape-copy; the only request-side delta is the S112-accepted-class drop of the never-bound `projectCode` key on the project PUT (wire-pinned, lie-audited). Refinement: `.claude/refinements/REFINEMENT-retrofit-pass6.md` — READY; Step-4 closed (Codex 0B/2W/2N; Reviewer 0B/1W/5N; the convergent `Project`-blast-radius WARNING absorbed with the named skema-decoupling resolution; all 9 assumptions Reviewer-verified).

**THE NEW LIVE PROD BUG (found at refinement, fixed this pass — FE-only):** `Project.isActive` is a PHANTOM field (`types.ts:74`) — no endpoint emits it; every read path filters `is_active = TRUE` (`ProjectRepository.cs:20/:104`). `ProjectManagement.tsx:198` renders `isActive ? 'Aktiv' : 'Inaktiv'` → **the admin project table shows "Inaktiv" for EVERY project today** (`undefined` → falsy; the S112 blank-columns class — prod bug #7; masked by ABSENCE of coverage — the admin projects page has zero tests; the skema mocks carried the phantom at the type level only). Fix: drop the field, REMOVE the Status column (a constant cell is informationless), one new component test (the page's first).

**Explicit exclusions:** NO enum declarations anywhere (absence-type strings have NO DB authority — free TEXT, no CHECK, C#-dict-only `ConfigEndpoints.cs:587–599`; `agreementCode`/`okVersion` stay refused); error bodies stay untyped (the profile-PUT 5-shape non-2xx surface incl. the nullable-`currentState` 412 — the S118 exclusion verbatim); NO request-class changes; the dead-but-binder-REQUIRED absence-types query params stay (spec documents them required; Docker tests must send them); select-trio retirement is NOT this pass (deprecated-but-live; `SkemaLegacySelectionAlignmentTests` exercises it — doubles as the unmodified-suites tripwire); the skema shapes are NOT re-pointed at bucket-B spec types (the sibling-`extends` smell one level up — both Step-4 lenses); `GET /health` + the 2 flag-and-defer ops untouched; bucket C (17) = Pass 7.

## Entropy Scan Findings (Step 0a) — the fact-sheet digest
| Check | Result | Detail |
|-------|--------|--------|
| Gate baseline | GREEN | 103 typed / 34 grandfathered / 8 declared at `a78ebae` (S118 close `e4992b0` CI GREEN `29813644589`) |
| The bucket-B cut | exact | manifest config 7 + projects 7 = 14; remainder 20 = bucket C 17 + `GET /health` + the 2 flag-and-defer |
| Config return sites | mapped | `ConfigEndpoints.cs` single file: constraints list rows 13 members (:33–48) and effective-config 14 members (:81–97) are TWO hand-maintained inline copies sharing 13 fields (drift risk → two sibling records, shapes NOT merged); absence-types 2-member rows from the hard-coded `AbsenceTypeLabels` dict; visibility POST 3-member; `MapProfileResponse` (:561–584, 14 members) ×3 SUCCESS sites (PUT-200 :415, GET :441, history-Select :463) + ×1 ERROR site (412 `currentState` :370 — stays anonymous) |
| Projects return sites | mapped | `ProjectEndpoints.cs` single file: list rows + 201-create = the IDENTICAL 4-member shape (ONE `ProjectResponse` — the S112 sibling rule applied); available 5-member (+`selected`); select 2-member; update 2-member; the 2 DELETEs 204 body-less. NO If-Match/ETag anywhere in the family. List GET returns an unmaterialized `Select` → declare `.Produces<IEnumerable<ProjectResponse>>` (array-ness) |
| The profile precondition | verified | Flexible: `If-Match:"<ver>"` OR `If-None-Match:*` (`EtagHeaderHelper.TryParseIfMatchOrIfNoneMatchStar` :152–154); ETag stamped on GET (:440) + PUT (:414), history none (immutable). Maps 1:1 onto the S115 typed `ifMatch`/`ifNoneMatch` options — **the program's FIRST live `ifNoneMatch` use**; the mutual-exclusion throw cannot fire (branches exclusive by construction, Reviewer-verified vs `api.ts:522`) |
| Enum authorities | ZERO declared | absence types: free TEXT, no CHECK (init.sql:1088), C#-dict-only → REFUSED; the profile-audit `action` CHECK two-step (base :848–850 omits MODIFIED; the s22 ledger-guarded block :1970–1972 widens it) is confirmed harmless on fresh DB — consolidation candidate for a future schema sprint, NOT S119 |
| FE consumers | mapped | `useConfig.ts` (constraints, explicit-T), `api/profileApi.ts` (profile GET/history/PUT via explicit-T `apiFetchWithEtag`; null-etag → If-None-Match:* create; failure path reads `result.body` for 412/400 runtime narrowing — preserved), `useProjects.ts` (CRUD, explicit-T). Consuming pages: `ConfigManagement.tsx`, `ProfileEditor.tsx`, `ProjectManagement.tsx`. **6 ZERO-caller ops** (absence-types GET, visibility POST, effective-config GET; available, select, deselect — `ProjectPicker` retired S72, stale endpoint comments to correct) — typed anyway (S117 greenfield precedent) |
| Hand-written interfaces (the lie audit pre-scan) | 3 + locals | `LocalAgreementProfile` (14 — faithful, member-verified), `ConfigConstraint` (13 — faithful), `Project` (5 incl. the PHANTOM `isActive`); profileApi request/response locals. `Project` is SHARED with skema types (`SkemaCatalogs.projects` :130, `SkemaMonthData.projects` :167) + 3 skema test files' mocks |
| Request DTOs | no lie-detector exposure | every binder-`required` member (projectName/projectCode on create; effectiveFrom on profile PUT; absenceType on visibility) is ALREADY sent by the FE — no compile-refusal possible this pass. The project-PUT `projectCode` key is never-bound (DTO has no member) → the S112 accepted-delta drop |
| Test homes | mapped | NO endpoint-level contract coverage exists (only `MixedRoleScopeLeakTests` auth 403s + `SkemaLegacySelectionAlignmentTests` select/deselect semantics ~7 tests). Profile REPO suites ×9 (Config/) cover version/supersession/audit invariants — UNMODIFIED criterion. Seeds: `projects` STY02 ×5 (init.sql:1133–1139, read-assert only); `local_agreement_profiles` has NO seed → profile tests create via the If-None-Match:* PUT (doubles as the create-path proof) |
| P7 per-op map | pinned | config reads + projects reads + select/deselect = `EmployeeOrAbove` (select POST + deselect DELETE are WRITES at EmployeeOrAbove BY DESIGN — self-service); visibility POST + profile PUT + project create/update/delete = `LocalAdminOrAbove`. No generalization — per-op pins |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — P4-adjacent surface (profile overrides feed rule resolution) + the FE prod-bug fix + the first live ifNoneMatch use |
| **External Codex** | cycle 1 (2026-07-21): **CLEAN — no findings.** All plan anchors independently verified: member counts, per-op policies (incl. the two EmployeeOrAbove writes), ETag sites, gate math (34 exempt / 8 declared at baseline), the isActive bug chain, ifNoneMatch helper support both ends (`api.ts:522` / `EtagHeaderHelper.cs:62`), tripwire-suite decoupling (profile suites repository-level — the retrofit cannot force modifying them) |
| **Internal Reviewer** | cycle 1 (2026-07-21): **0 BLOCKER / 2 WARNING / 3 NOTE** — W1 the Status-column/component-test AC was description-only → now a 11901 checkbox; W2 the profile-chain ordering-independence mitigation lost in translation → now a 11902 criterion (single-method chain or per-fact self-seeding); N1 projects-unconditioned demoted to observation → now a 11902 pin; N2 a NEW pre-existing UX defect: the edit dialog's `projectCode` is a DEAD EDITABLE control (backend never persisted it on update — S91 dead-button class) → 11901 renders it read-only in edit mode + lie-audit entry; N3 the regen cross-domain annotation made explicit (S118 precedent back-annotated → PAT-012 at 11903). Full fact base verified incl. no-seed premise, STY02 FAIL-002 safety, the 6 zero-caller ops, P4 fencing adequate. Cycle 2: **all 5 absorptions verified faithful; 1 trivial residual (the PAT-012 back-annotation now in 11903's own text); 0 new findings — CONVERGED (2 cycles, cap honored)** |
| **BLOCKERs resolved before Step 1** | **yes — ZERO BLOCKERs both lenses; all W/N absorbed as task criteria** |

---

### TASK-11900 — Backend: the 14-op drain
| Field | Value |
|-------|-------|
| **ID** | TASK-11900 |
| **Status** | complete (2026-07-21) — **117 typed / 20 grandfathered / 9 declared / 0 stale, exactly the target; build 0 err ×2; drift in-sync (102 paths/150 schemas); sha-idempotent ×2 (`163f7a7d…`/`33402bb8…`); tripwires 130/130 UNMODIFIED (MixedRoleScopeLeak + SkemaLegacySelectionAlignment + the Config profile suites); ZERO checksum discrepancies (the fact sheet transcribed clean — a first).** 9 records (5 config + 4 projects); the exclusion boundary held via the record-erased-to-`object?` form: `MapProfileResponse` retained as a 412-ONLY private mapper delegating to the typed `MapProfile` — the profile PUT's spec responses set is exactly `['200']`, the 400/403/412/428 surface provably undeclared. Policy strings byte-identical ×14; zero handler-logic hunks; `[AllowedValues]` zero (spec-verified no enum in any new schema). Profile record nullability 14/14/6 mirrors the model. Stale ProjectPicker comments corrected. The 2 new Contracts files are untracked → the close-commit explicit file set (FAIL-003). |
| **Agent** | Backend/API (extended into docs/api/openapi.json + generated FE types via regen — cross-domain AUTHORIZED, the S118/PAT-012 pipeline precedent; Step-0b Reviewer N3, back-annotation of the standing pattern → PAT-012 at 11903) |
| **Components** | ConfigEndpoints.cs, ProjectEndpoints.cs, Contracts/ConfigResponses.cs (new), Contracts/ProjectResponses.cs (new), tools/openapi-convention-exempt.txt, tools/openapi-bodyless-declared.txt, regen |
| **KB Refs** | PAT-012 (paved road; sibling-record rules; declared-bodyless; the error-body exclusion), PAT-010 |

**Description**: Records as EXACT shape-copies at every enumerated success site (the fact-sheet member lists are checksums — the shape-copy rule wins on any discrepancy, declared not trimmed): config = `ConfigConstraintResponse` (13) + `EffectiveConfigResponse` (14) as SEPARATE siblings (shapes differ: bare-array rows vs object root + `orgId`; merging = a barred wire change) + `AbsenceTypeResponse` (2) + `AbsenceTypeVisibilityResponse` (3) + `LocalAgreementProfileResponse` (14) replacing `MapProfileResponse` at its 3 SUCCESS sites ONLY — **the 412 `currentState` site keeps the current anonymous/nullable projection (record-erased-to-`object?` acceptable; wire-identical either way); the exclusion boundary is a task criterion**; projects = `ProjectResponse` (4, list rows + 201 create — one record, the sibling rule) + `AvailableProjectResponse` (5) + `ProjectSelectionResponse` (2) + `ProjectUpdateResponse` (2). `.Produces<T>` per op (list GETs as `IEnumerable<T>` — array-ness; create 201; the 2 DELETEs `.Produces(204)`); POST select → the declared-bodyless list (8→9). ZERO `[AllowedValues]`. Correct the stale `ProjectPicker` comments (:110/:143 — retired S72; comment-only). Manifest 34→20; regen; field-mapping tables per op/site.

**Validation Criteria**:
- [ ] Build 0 err; convention **117 typed / 20 grandfathered / 9 declared / 0 stale**; drift + freshness green; sha-idempotent ×2
- [ ] Wire-byte identity on ALL 14 responses (field-mapping tables); the 412 error site provably untyped (exclusion held)
- [ ] Zero handler-logic hunks; policy strings byte-identical per the per-op map; the profile repo suites UNMODIFIED

---

### TASK-11901 — FE: the typed switch + the isActive fix (depends: 11900)
| Field | Value |
|-------|-------|
| **ID** | TASK-11901 |
| **Status** | complete (2026-07-21) — **tsc 0 (baseline was RED 3: the stale S118 phase-pin vs the S119 regen — updated per its own "(retrofit updates this)" contract, now 36/21/13); lint 0 (24→28 files, +4 FULL tier, all 4 RED-probed w/ scratchpad-copy reverts); vitest 617→637 (+20, 51→54 files).** All 4 switched files FULL tier — no partial tier needed (no deferred legacy call remains; the request-side lie detector did NOT fire — every binder-required member already sent). The first live `ifNoneMatch:'*'` use wire-pinned both branches (+ the unparseable-legacy-etag raw passthrough + the 412 structured-body round-trip). Lie audit: **prod bug #7 FIXED** (`Project.isActive` phantom + the always-"Inaktiv" column removed; 14 mock occurrences cleaned; skema NOT aliased to spec types); `LocalAgreementProfile` 14/14 + `ConfigConstraint` 13/13 FAITHFUL; the dropped never-bound `projectCode` key wire-pinned (S112 accepted-delta); the dead `projectCode` edit control now disabled in edit mode (S91 class, test-pinned); `ProjectManagement`'s FIRST component tests (4). One honest-boundary delta declared (hand-written `ProfileSaveRequest` claimed all-6-required vs the spec's `effectiveFrom`-only — the FE still sends all 6, pinned). `ConfigManagement`/`ProfileEditor` untouched (spec-type aliases preserved their imports). 3 NEW untracked test files → the close file set (FAIL-003). **Constraint Validator: PASS all 6 checks (scope, request-payload invariance incl. the 6-key profile body + the 2-key update body + zero preconditions in the projects family, skema decoupling [mock-only diffs verified hunk-by-hunk], tier integrity incl. the S118 carve-outs intact, gates independently re-run, honesty greps clean).** |
| **Agent** | UX/Frontend |
| **Components** | useConfig.ts, api/profileApi.ts, useProjects.ts, ProjectManagement.tsx, types.ts, ConfigManagement.tsx + ProfileEditor.tsx (as needed), SkemaPage.test.tsx + skemaWorkTime.test.ts + SkemaProjectManager.test.tsx (mocks only), eslint.config.mjs, package.json |
| **KB Refs** | PAT-012 (call-form audit; typed-etag overloads; the request-side lie detector — expected NOT to fire) |

**Description**: Switch `useConfig.ts` (constraints → typed `get`), `profileApi.ts` (GET/history → typed `get` with ETag resolve unchanged; PUT → typed `apiFetchWithEtag` threading the EXACT flexible precondition — null-etag branch `ifNoneMatch:'*'`, else `ifMatch` with the ready RFC-7232 string incl. the raw-passthrough legacy fallback; the 412/400 `result.body` runtime-guard narrowing PRESERVED), `useProjects.ts` (CRUD → typed verbs; the PUT body drops ONLY the never-bound `projectCode` key — wire-pinned, the S112 accepted-delta class). DELETE `LocalAgreementProfile`/`ConfigConstraint` + profileApi locals (spec types replace). **The isActive fix**: `types.ts Project` loses the phantom field (skema-owned by usage thereafter; NOT aliased to bucket-B spec types); the 3 skema test files' mocks lose `isActive`; `ProjectManagement.tsx` Status column REMOVED + the page's FIRST component test added. Lint tiers: the 3 switched files + consuming pages join FULL tier; RED-probe each. Vitest wire pins: both precondition branches; the create/update/delete key sets; the constraints read.

**Validation Criteria**:
- [ ] tsc 0; lint 0 (tiers RED-probed); vitest green (delta counted); zero unsanctioned explicit-T/`as` on the switched surface
- [ ] Both profile precondition branches wire-pinned byte-identical; the 412-body round-trip preserved (test-pinned)
- [ ] The PUT-project key set pinned post-drop; every other mutation's key set byte-unchanged
- [ ] **Status column removed; the `ProjectManagement` component test (the page's FIRST) added and green** (Step-0b Reviewer W1)
- [ ] The edit dialog's `projectCode` input rendered READ-ONLY/disabled in edit mode (the backend never persisted it on update — a dead editable control, the S91 dead-button class; FE-only, no wire change) and the defect named in the lie audit (Step-0b Reviewer N2)
- [ ] The lie audit reported: `Project.isActive` = prod bug #7 (fixed); `LocalAgreementProfile`/`ConfigConstraint` faithfulness confirmed or discrepancies declared

---

### TASK-11902 — Test & QA: per-route assertions for the 14 ops (depends: 11900)
| Field | Value |
|-------|-------|
| **ID** | TASK-11902 |
| **Status** | complete (2026-07-21) — **22 new per-route assertions green (Config 6, Profile 7, Project 9 — the families' FIRST endpoint contract coverage); composed Contracts 178/178 (156+22, zero failures); build 0 err.** Ordering independence via PER-FACT SELF-CREATION (each profile fact creates its own profile through the real `If-None-Match:*` PUT under its own org key + a fresh testcontainer per fact — doubly structural). Precondition-free projects DOUBLY pinned (mutations succeed header-free AND every response `AssertNoEtag` — a future concurrency surface goes RED). Policy pins per the P7 map incl. the two Employee-floor writes proven positively + 403s at the floor. The profile chain: create 200+ETag`"1"` → GET fidelity → If-Match bump → REAL-supersession history (effectiveTo populated, no ETag) → 428/412×2 (error bodies narrowed structurally only — undeclared). RED-on-lie proof PERMANENT in the committed test (both halves: GREEN on truth, `Assert.Throws` RED on the injected phantom-required lie). Seed census disjoint (10 S119 orgs, 6 actors, S119AGR/OKS119 profile keys; STY02's 5 rows read-asserted exact). Tripwires: only the 4 new untracked S119 files in tests/ — everything else byte-unchanged. **Zero spec≡runtime mismatches; the 11900 exclusion boundary independently confirmed (the PUT's declared responses = exactly `['200']`).** Session-limit interruption ×2 mid-task — resumed from transcript both times, files intact. **Constraint Validator: PASS 7/7 (scope exactly the 4 untracked files; all 14 ops covered [per-op line-cited]; ordering independence structural [per-fact org keys + fresh container, zero shared mutable state]; the RED tripwire's corruption proven in-memory-only with the phantom member name-asserted; STY02 strictly read-only [hit-by-hit audit]; the 22/22 gate re-run fresh; all 7 policy pins located].** |
| **Agent** | Test & QA |
| **Components** | tests/StatsTid.Tests.Regression/Contracts/ (new per-family S119 classes) |
| **KB Refs** | PAT-012 (gate #1), FAIL-002 |

**Description**: Per-route spec≡runtime Docker assertions, per-family classes (the families' FIRST endpoint contract coverage): config — constraints + effective-config byte assertions (the two-sibling shapes each pinned); absence-types GET (SEND the binder-required query params) + visibility POST; the profile chain: create via `If-None-Match:*` PUT (no seed exists — the create path IS the proof) → GET (ETag fidelity `"<version>"`) → If-Match PUT → history (no ETag); 428/412 composed as the FE does; projects — list + 201-create (the shared `ProjectResponse` at both sites), available `selected` flip, select 200 + deselect 204 + delete 204 (status + empty body), update. Per-op POLICY pins per the map (incl. the two EmployeeOrAbove writes). Seeds: STY02 project rows READ-ASSERTED never mutated; S119-prefixed orgs/codes for all mutations; `SkemaLegacySelectionAlignmentTests` + the 9 profile repo suites UNMODIFIED (tripwires). One RED-on-lie proof (in-memory spec corruption, the S117 technique). Scoped runs only; composed Contracts green alongside.

**Validation Criteria**:
- [ ] All new Docker assertions green (exact counts); the RED proof demonstrated; seed census disjoint
- [ ] **Ordering independence: the profile chain runs within a SINGLE test method, or each fact self-creates its profile under its own S119 org key** — xUnit guarantees no `[Fact]` order; the chain must not assume a pre-existing profile (Step-0b Reviewer W2, restoring the refinement's explicit constraint)
- [ ] **Project mutations asserted PRECONDITION-FREE** (no If-Match anywhere in the family — pinned, not just observed; Step-0b Reviewer N1)
- [ ] Composed Contracts green; the named unmodified suites byte-unchanged

---

### TASK-11903 — Orchestrator: PAT-012 update + validation + Step 7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-11903 |
| **Status** | complete (2026-07-21) — **PAT-012 updated** (Pass-6 record: 117/20/9, the zero-wire-delta note, prod bug #7, the tally → **7 prod bugs + 11 lies** [the `ProfileSaveRequest` all-6-required claim = a declared delta, NOT tallied — stricter than the contract, wire-identical], the regen cross-domain back-annotation, the Pass-7 map). **Validation** (`sprint-test-validation`): delta table below, +42; the 7 regression sheds isolation-cleared 39/39 (FAIL-002 class — Approval reads ×2, EmployeeProfile endpoint ×2, Outbox ×2, PhaseE backfill ×1; all green in the S118 run, all green isolated). **Step 7a converged** (section below): Codex 2 cycles (0B/1W — the `sortOrder` alias weakening → the `{ sortOrder: number }` intersections, tsc/lint/vitest re-verified → c2 clean); Reviewer CLEAN at cycle 1 (0B/0W/4N — all record-only: the 3-copy employee-client helper → promote at Pass 7; the ASCII-orthography small-task candidate; the boot-seed value-coupling awareness; the stricter error guard documented). Close via the explicit file set; `design_handoff_*` untracked with cause (owner assets). |
| **Agent** | Orchestrator |
| **Components** | PAT-012, sprint log, INDEX, close gates |
| **KB Refs** | PAT-012, FAIL-002/003 |

**Description**: PAT-012: scope → **117 typed / 20 grandfathered / 9 declared** + the Pass-7 map (bucket C 17 incl. the skema drain graduating `useSkema.ts`; then only `GET /health` + the 2 flag-and-defer remain); lie tally +1 prod bug (`Project.isActive` #7) + any switch finds; **+ the regen cross-domain-authorization pattern back-annotated (the S118/S119 standing practice: the backend drain agent writes docs/api/openapi.json + generated FE types as part of the pipeline — Step-0b Reviewer N3/cycle-2 residual)**. Validation (`sprint-test-validation`). Step 7a dual-lens (named targets: wire-byte identity on ALL 14; the exclusion boundary at the 412 site; the first-live-ifNoneMatch precondition fidelity; the isActive fix's skema decoupling; the dropped-key pin). Close per the 5 gates; explicit file set; `design_handoff_*` stays untracked with cause.

**Validation Criteria**:
- [ ] PAT-012 updated; delta table; Step 7a converged; close + push + CI green all 7 jobs

---

## External Review (Step 7a)

Dual-lens (2026-07-21). Artifacts: `.claude/reviews/SPRINT-119-step7a-{codex,reviewer}.md`.

| Lens | Cycle 1 | Cycle 2 (fix verification) |
|------|---------|---------------------------|
| External Codex | **0 BLOCKER / 1 WARNING** — the hook-level `ProjectCreateRequest`/`ProjectUpdateRequest` generated-schema aliases let future callers omit `sortOrder` (spec-optional per the honest boundary; the old hand-written FE contract required it) | **"Verified — no findings."** (the `{ sortOrder: number }` intersections restore the FE boundary; wire unchanged) |
| Internal Reviewer | **CLEAN: 0 BLOCKER / 0 WARNING / 4 NOTE** — wire-byte identity verified on ALL 14 ops member-by-member; the 412 boundary, P7 ×14, precondition fidelity, skema decoupling, and all five Step-0b carry-overs held; the DELETE spec hunks established as spec-truth 200→204 corrections (runtime always 204 — the declared-204 policy, not a wire change). NOTEs (record-only): the 3-copy employee-client JWT helper (promote into `SpecRuntimeTestSupport` at Pass 7 + the FE fetch-capture scaffolding ×2); the pre-existing ASCII misspellings now test-pinned ("Tilfoej"/"paakraevet" — orthography = a small-task candidate, page+test together); the constraints/effective value assertions couple to central-config boot-seed values (deliberate resolution probe — a future seed change REDs as value-coupling, not a contract lie); the `isProfileSaveError` guard marginally stricter than the replaced cast (test-pinned as intent) | not needed — converged at cycle 1 |

**The fix set (absorbed before close):** the two request-alias intersections in `useProjects.ts` (tsc 0 / lint 0 / vitest 54/637 re-verified). [[review-lens-complementarity]]: the external lens caught a TYPE-boundary weakening invisible to wire-byte review; the internal lens did the exhaustive wire/carry-over verification — disjoint classes again.

## Test Summary (close, 2026-07-21)

| Suite | Previous (S118) | Current | Delta |
|-------|-----------------|---------|-------|
| Unit | 861 | 861 | 0 |
| Regression | 1305 | 1327 | +22 |
| Smoke | 6 | 6 | 0 |
| DemoSeed | 55 | 55 | 0 |
| Frontend | 617 | 637 | +20 |
| **Total** | **2844** | **2886** | **+42** |

Delta composition: +22 regression = the S119 per-route Contracts assertions (Config 6, Profile 7, Project 9 — composed Contracts 178/178); +20 FE = the wire-pin/component tests across 3 new files (51→54). Full-run integrity: the central run (fresh compose Postgres per FAIL-002) 1320/1327 first-pass with 7 sheds across 4 pre-existing families (Approval reads, EmployeeProfile endpoint, Outbox, PhaseE backfill — all green at S118), **isolation-cleared 39/39**; unit + DemoSeed green same run; smoke 6/6 against the healthy 8-service stack post-run; FE vitest 637/637 (three independent green runs incl. the Constraint Validator's and the post-absorption rerun).

## Phase Plan
- **Phase 1:** TASK-11900 (one Backend agent — Contracts/ + manifest + regen are coupled; single-agent per the S118 guidance; if sizing forces a split, BY FAMILY with contracts/manifest/regen ownership staying with ONE agent).
- **Phase 2 (parallel):** TASK-11901 (FE — needs the regen'd types) ∥ TASK-11902 (assertions — needs the spec; file-disjoint: frontend/ vs tests/)
- **Phase 3:** TASK-11903 (docs + validation + close)

**Atomicity pin:** ONE close commit; explicit file set; gates at close.

## Legal & Payroll Verification
| Check | Status |
|-------|--------|
| Agreement rule compliance | ACTIVE-WATCH (P4) — the profile surface feeds rule resolution but this pass changes ONLY response construction (records = byte-copies); no rule value, merge semantics (`ConfigResolutionService` untouched), validation path, or supersession logic changes; enum refusal keeps all agreement-defining sets open |
| Wage type mapping correctness | N/A — surface untouched |
| Event sourcing / audit | N/A — no event/audit-path change (the profile-audit CHECK two-step recorded as a future consolidation candidate, not touched) |
| Security (P7) | PASSIVE — no new endpoint; per-op policy pins incl. the two by-design EmployeeOrAbove writes |
