# Sprint 115 — Typed API Contract retrofit, Pass 2: the admin-core remainder

| Field | Value |
|-------|-------|
| **Sprint** | 115 |
| **Status** | complete — close `ddede73`; ✅ CI GREEN `29144794028` (all 7 jobs, 2026-07-11) |
| **Start Date** | 2026-07-03 |
| **End Date** | 2026-07-11 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes — backend 0 err / 0 warn; FE tsc 0 + lint 0 (new tiers RED-probed live) + build 0 |
| **Test Verified** | yes (local, full): 861 unit + **1234 regression** (1230 first-run + 4 pre-existing FAIL-002 sheds isolation-cleared 4/4 — Tx-contract/R9-lifecycle/allocation-drift/terminated-access, none S115 surface; **the 42 fixed-port tests ran locally** against a fresh compose Postgres on :5432 — the owner's demo stack was down, no S114-style deferral) + 6 smoke (live stack) + 55 demoseed + **567 FE**; **pyramid 861u+1234r+6s+55demoseed+567fe = 2723 (+45)**; gates: convention 49/87/0-stale, drift in-sync (101 paths/107 schemas), freshness sha-idempotent, endpoint-contract lint + check_docs hard-green |

## Sprint Goal
Retrofit Pass 2 (PAT-012, [[typed-api-contract-program]]): drain the **24-op admin-core remainder** — the reporting-lines admin family (13), the employee field-endpoints (8), the remaining admin reads (3) — via the twice-proven recipe. **23 ops drain (manifest 110→87; typed 26→49); 1 op takes the program's FIRST flag-and-defer** (`GET …/entitlement-eligibility/{type}` is genuinely polymorphic — its no-row branch OMITS keys; typing = wire change = PAT-010-forbidden). Two slice novelties, both resolved inside the program's rules and dual-lens-verified in ONE cycle: (1) the **homogeneous-multi-2xx matcher extension** (2 reporting-line POSTs return 201-or-200 from ONE shared shape); (2) the **`ifNoneMatch` typed-etag option** (first-assign sends `If-None-Match: *` — confirmed missing from the S112 overload surface, confirmed additive). Post-S113: every drained op lands STRICT automatically (required + enum fidelity per route).

Refinement: `.claude/refinements/REFINEMENT-retrofit-pass2.md` — **READY, review CLOSED in ONE cycle** (Codex clean with all grounding re-verified; Reviewer APPROVED 0B/0W — the extension hole-free on all four guarantee paths [convention gate untouched; `SuccessDataOf` trivially correct on a same-T union; required/enum fidelity downstream of status resolution; undeclared-status stays RED]). Preceded by a full read-only grounding sweep (per-op status/shape/caller/If-Match fact sheet — the reason for the one-cycle review).

**Explicit exclusions:** NO `.Accepts`/request-class changes; NO error-shape typing (incl. the employment-end-date 409 settlement-conflict body); NO wire-byte changes anywhere; the deferred GET-eligibility op stays grandfathered with an in-manifest comment; Pass 3+ (approval 10 → payroll/settlement 7 [+ the 2 employee settlement POSTs] → config ~35 → employee-facing ~31) NOT in this sprint.

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| Gate baseline | GREEN | convention 26 typed / 110 grandfathered; drift+freshness in sync at `f5cf53b` |
| Grounded per-op truth | fact sheet | 2 × conditional 201/200 (POST lines, POST acting — ONE `MapLineResponse` shape both branches); 2 × true 204 DELETEs ({employeeId}, acting); DELETE vikar = genuine 200-with-body; 2 bare arrays (tree, reports); 3 envelopes (lines {active,history}, period-status, audit); `activeVikar` = a STABLE envelope with one nullable-COMPLEX member (key always emitted, null-or-object) — **the S113 nullable-$ref residual goes 1→2 (watched; escalation trigger at 3+ lands in PAT-012)** |
| The polymorphic op | verified genuine | GET eligibility no-row branch omits `effectiveFrom`/`version` KEYS (not null); every in-rules alternative fails (null-emitting/WhenWritingNull = wire changes; oneOf unsupported) → the S112 flag-and-defer rule fires as designed |
| NO-FE-CALLER ops | 8 | tree, period-status, acting ×2, import, employment-end-date GET+PUT, units list — backend-typed + asserted only; honest audit rows |
| `GET /api/admin/units` | typed-but-for-`.Produces` | `UnitListResponse`/`UnitListItem` with the DORMANT S114 `[AllowedValues]` — auto-emits on regen (the S114 dormancy note closes exactly as predicted) |
| FE inventory | exact | `useReportingLines.ts` 9 calls (8 slice, all explicit-T; 1 already-typed search); `useEntitlementEligibility.ts` 6; `useAdmin.ts:243` = the S112 audit's ONE deliberate survivor (closes); `AuditLogView.tsx:77` query-string URL → the structured `{query}` typed form |
| If-Match demand map | verified per mutation | assign: If-Match/If-None-Match:*; removes + field-PUTs: strict If-Match; vikar create/revoke + remove-with-reassignment + import: NONE — the FE switch preserves each op's exact precondition |
| Lint end-state | verified | after the pass: `useReportingLines` + `useAdmin` → FULL ban (the legacy-GET exception closes); `useEntitlementEligibility` → PARTIAL (the 1 deferred GET; reuses the WITH_LEGACY_GET tier pattern) |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY-equivalent (matcher/gate semantics change — the multi-2xx extension) |
| **External Codex** | cycle 1 (2026-07-03): **0B/0W/1N** — a path nit (`frontend/eslint.config.mjs`), fixed. Transcription, phase ordering, gate logic, baselines all verified (incl. `operation_reasons` needing NO change for multi-2xx — any 2xx schema passes) |
| **Internal Reviewer** | cycle 1 (2026-07-03): **0B/0W/4N, APPROVED** — all 5 refinement NOTEs verified landed verbatim; the tests/Contracts double-occupancy safe by phase sequencing WITH the ripple-containment condition (absorbed into 11500); the 11502 fixture-case named in criteria (absorbed); 11500's Phase-1 Docker validation correctly meaningful against the pre-11501 spec, no :5432 conflict (testcontainers) |
| **BLOCKERs resolved before Step 1** | **yes — ZERO BLOCKERs both lenses; all NOTEs absorbed** |

---

### TASK-11500 — Test & QA: the homogeneous-multi-2xx matcher extension
| Field | Value |
|-------|-------|
| **ID** | TASK-11500 |
| **Status** | complete (2026-07-03) — `SuccessContract` → `(IReadOnlyList<int> StatusCodes, JsonElement? Schema)` with resolution: multi-2xx acceptable IFF all share ONE `$ref` (204 members rejected FIRST — heterogeneous by content; missing-json-schema rejected; INLINE schemas rejected conservatively; differing `$ref`s rejected naming both); status fidelity = membership (undeclared runtime status RED). **Ripple contained ONE STEP DEEPER than planned** (honestly named): the S112 injected-lie tests consume the constructor + `StatusCode` directly → back-compat members added (single-status ctor overload; `StatusCode` accessor throwing-with-guidance on multi; `DescribeStatuses()` diagnostics); the Support helper's public shape UNCHANGED. 1 existing test reworked-in-place (the old blanket rejection's fixture became the GREEN case — renamed `Red_WhenMulti2xxDeclaresDifferentSchemaRefs`), 3 assertion-shape updates (named), +4 new cases. **Validated (agent + Orchestrator): matcher 24/24; Docker 28/28 UNMODIFIED against the PRE-Pass-2 spec (backward-compat proven); post-11501 composed run 61/61.** PROPOSED KB (the SuccessContract single-status surface is now a compatibility contract) accepted → 11504 |
| **Agent** | Test & QA (`tests/StatsTid.Tests.Regression/Contracts/**`) |
| **Components** | SpecRuntimeMatcher (gate #1) |
| **KB Refs** | PAT-012 (gate 1 + the single-2xx convention being amended) |

**Description**: Extend `ResolveSuccessContract`: MULTIPLE declared 2xx is acceptable **IFF all declared 2xx share ONE schema `$ref`** — resolve the declared-status SET + the shared schema; `AssertSuccessMatches` asserts runtime status ∈ the set (undeclared status stays RED) then matches the shared schema (required/enum fidelity downstream unchanged). Heterogeneous sets stay REJECTED — unit tests MUST include (Reviewer NOTEs): a 204-no-content + 200-with-schema pair (heterogeneous BY CONTENT), a two-INLINE-schema pair (non-`$ref` — rejected conservatively, the tripwire against inline-schema drift), plus the shared-`$ref` GREEN case and the undeclared-runtime-status RED case. Handle the small signature ripple in `SpecRuntimeTestSupport.AssertOperationMatchesRuntimeAsync` (it reads `contract.StatusCode` for diagnostics — becomes the resolved set/actual). **Ripple-containment condition (Step-0b, load-bearing): the Support helper KEEPS its public call shape `(spec, client, request, specPath, method)` — the `SuccessContract` shape change stays INTERNAL to Matcher+Support; widening the helper's signature instead would break the "Docker suites green UNMODIFIED" criterion.** `Resolve200Schema` back-compat wrapper untouched (single-200 callers only).

**Validation Criteria**:
- [ ] Matcher unit tests green incl. the 4 named cases; existing 20 matcher tests green (updated only where the signature ripple demands — named)
- [ ] Existing Docker per-route suites (S111 6 + S112 22) green unmodified

---

### TASK-11501 — Backend: the 23-op response typing + manifest drain
| Field | Value |
|-------|-------|
| **ID** | TASK-11501 |
| **Status** | complete (2026-07-03) — 19 new records across 3 new + 1 extended Contracts files; the 2 conditional POSTs share ONE `ReportingLineResponse` declared 201+200 (`MapLineResponse` return type `object`→record; factories untouched); 2 declared-204 DELETEs; DELETE vikar = genuine 200 record; bare arrays stay bare (tree/reports/org-users — the tree row is a DELIBERATE SUBSET of the line shape [no `effectiveTo`], the reports row lacks `managerDisplayName` — **the recipe note: shape-copy the HANDLER's anonymous object, never the repo model**); `activeVikar` ALWAYS-emitted nullable-complex (the S113 exclusion auto-applied → TS optional; **the residual 1→2, watched**); audit `details` = raw string passthrough; units gained only its missing `.Produces` (**the dormant S114 enum auto-emitted — closing that note exactly as predicted**); the polymorphic GET DEFERRED with the in-manifest comment (the flag-and-defer rule's FIRST firing). 23 field-mapping tables delivered. **Validated (agent + Orchestrator re-ran): build 0/0; convention 49/87/0-stale; drift green (107 schemas); existing contract tests 37/37 UNMODIFIED; composed Contracts 61/61.** 4 PROPOSED KB entries accepted → 11504 |
| **Agent** | Backend API (cross-domain authorized: `src/Backend/StatsTid.Backend.Api/Endpoints/{ReportingLineEndpoints,AdminEndpoints,EmploymentDateEndpoints,EntitlementEligibilityEndpoints,AuditEndpoints,UnitEndpoints}.cs`, `src/Backend/StatsTid.Backend.Api/Contracts/**`, `tools/openapi-convention-exempt.txt`, `docs/api/openapi.json` + `frontend/src/lib/api-types.ts` [REGEN ONLY, one task — no freshness window]) |
| **Components** | Backend.Api endpoints + Contracts |
| **KB Refs** | PAT-012 (recipe), PAT-010 (byte-identity), ADR-019 (ETag/If-Match) |

**Description**: Exact shape-copy records + `.Produces` per the grounded truth: the 2 conditional POSTs declare **BOTH** `.Produces<LineResponse>(201)` and `.Produces<LineResponse>(200)` (one shared record — both branches serialize `MapLineResponse`; statuses UNCHANGED); the 2 true-204 DELETEs get `.Produces(204)` only; DELETE vikar gets its 200 record; bare arrays as `IEnumerable<T>` (tree, reports); envelopes as records (lines {active,history}, period-status, audit — `details` stays a raw `string` passthrough, NO reshaping); the field-endpoint quartet records; `ActiveVikarResponse(ActiveVikarInfo? ActiveVikar)` — the nullable-complex member rides the S113 filter exclusion automatically; `GET /units` gains its missing `.Produces<UnitListResponse>(200)` (the enum auto-emits). **The deferred op (`GET …/entitlement-eligibility/{type}`) keeps its manifest line + gains an in-manifest comment naming the polymorphic cause.** Delete the other 23 lines. Regen BOTH artifacts. Field-mapping tables per op.

**Validation Criteria**:
- [ ] Build 0 errors; regression + existing contract tests green UNMODIFIED (byte-identity)
- [ ] Convention gate: 49 typed / 87 grandfathered, ZERO stale; drift green; the 2 conditional ops carry both statuses with one shared `$ref`; the units enum present in the spec; the 23 field-mapping tables delivered

---

### TASK-11502 — FE: the `ifNoneMatch` option + the call-site switch (depends: 11501)
| Field | Value |
|-------|-------|
| **ID** | TASK-11502 |
| **Status** | complete (2026-07-03) — the `ifNoneMatch?: '*'` option (LITERAL-ONLY — an entity-tag is a compile error, negative-probed; grep-verified additive; 3 fixture cases); ALL consumed slice call-sites switched per the demand map with **the delicate row vitest-PINNED (first-assign `If-None-Match: *` / reassign `If-Match`)** in a NEW `useReportingLines.test.ts` wire suite (9 tests); the S112 org-users survivor CLOSED; `AuditLogView` → structured `{query}` byte-equivalent; `activeVikar` consumed optional-as-nullable normalized in-hook (reported choice — a one-member envelope doesn't warrant the Omit-override); the same-T 201/200 union compile-pinned. **⚠ FINDING — THE PROGRAM'S 4th REAL CONTRACT LIE: `DirectReport extends ReportingLineEntry` inherited 3 PHANTOM fields (`effectiveTo`/`createdBy`/`createdAt`) the reports endpoint never serves + a nullability lie (`employeeDisplayName` is `string \| null`) — latent (no consumer read them); an `extends`-of-a-sibling-response is a LIE-AMPLIFIER.** 1 mock correction reported (LifecycleSections' phantom fields). Lint: `useReportingLines`+`useAdmin` → FULL ban (the WITH_LEGACY_GET tier retired); `useEntitlementEligibility` → a TIGHTER partial tier (`[arguments.length=2]` esquery narrowing sanctions exactly the ONE deferred call FORM); 7 `as`-casts → runtime guards. **Cross-domain RATIFIED: `frontend/package.json` lint script +1 line** (the hard-coded file list would have silently skipped the new tiers — a false-green; deriving the list from the config = a noted follow-up). **Validated (agent + Orchestrator re-ran): tsc 0; lint 0; vitest 566/566 (554+12); build 0; gen:api idempotent (sha-proven).** 3 PROPOSED KB entries accepted → 11504 |
| **Agent** | UX (`frontend/src/lib/api.ts` [the additive option ONLY], `useReportingLines.ts`, `useEntitlementEligibility.ts`, `useAdmin.ts`, `AuditLogView.tsx`, `frontend/eslint.config.mjs`, the typed-overload fixture test, affected hook tests, + forced consumers reported) |
| **Components** | frontend api client option + hooks |
| **KB Refs** | PAT-012, ADR-019, PAT-010 |

**Description**: (A) **The `ifNoneMatch` option (pinned shape, Reviewer NOTE):** an optional `ifNoneMatch: '*'` key on the typed `apiFetchWithEtag` options + the runtime allowed-key set + the normalization branch emitting `If-None-Match` — verified additive (no legacy RequestInit carries the key; discrimination cannot flip; existing callers untouched); a fixture-test case for the new key. (B) Switch ALL consumed slice call-sites to typed forms per the demand map — **the delicate row vitest-PINNED: first-assign sends `If-None-Match: *`, reassign sends `If-Match`** (assignManager); removes/field-PUTs strict If-Match; vikar/remove/import no precondition; `AuditLogView` → structured `{query}` (byte-equivalent query construction — the S112 users-search precedent); the org-users survivor closes. (C) Interface inventory (DELETED/KEPT/CHANGED=FINDING — historically lying-prone surfaces); the `activeVikar` FE view uses the RosterRow Omit-override pattern. (D) Lint: `useReportingLines` + `useAdmin` → FULL ban; `useEntitlementEligibility` → PARTIAL (documented). (E) Call-form audit for all 24 routes (8 NO-FE-CALLER + 1 DEFERRED rows included).

**Validation Criteria**:
- [ ] tsc clean; build 0; vitest green (report count) with the If-None-Match/If-Match pin AND the new-option fixture-test case (guards the option's type-level shape); lint green on the extended surface; freshness gate green
- [ ] Call-form audit: zero untyped-fallback survivors on drained routes; the inventory delivered; mock corrections reported not silent

---

### TASK-11503 — Test & QA: per-route assertions for the 23 drained ops (depends: 11500 + 11501)
| Field | Value |
|-------|-------|
| **ID** | TASK-11503 |
| **Status** | complete (2026-07-03) — 3 per-family Docker classes, 27 tests / 23 ops: **the 2-branch conditional proofs from TWO dedicated seed states EACH, in SEPARATE Organisations** (virgin→201 asserted-equal THEN matched; predecessor-carrying→If-Match reassign→200; acting via a pre-seeded acting line; the per-org isolation discovered load-bearing: the DELETE multi-root census would 409 sibling pairs in one org); the 204 DELETEs status+empty-body; **GET vikar BOTH branches** (object + emitted-null — the nullable-$ref exclusion exercised live); the field PUTs read-then-If-Match as the FE composes it; the units read exercises ENUM fidelity; **the audit row produced by a REAL in-test admin mutation** (ADR-026 sync-in-tx projection verified, not SQL-faked); **the multi-2xx RED proof**: a real 201 vs a synthetic {200,202} contract → throws with "UNDECLARED". Matcher/Support consumed AS-IS (public shapes untouched). **Validated (agent + Orchestrator re-ran): 17/17 + 7/7 + 3/3 first-run; composed Contracts 88/88 (61+27).** 3 PROPOSED KB entries accepted → 11504 |
| **Agent** | Test & QA (`tests/StatsTid.Tests.Regression/Contracts/**`) |
| **Components** | gate #1 per-route coverage |
| **KB Refs** | PAT-012, FAIL-002 |

**Description**: Per-route spec≡runtime assertions for all 23 drained ops in per-family Docker fixture classes (reporting-lines family / field-endpoints family / admin-reads family), dedicated seeded rows per mutation. **The 2-branch conditional proofs (Reviewer NOTE): TWO dedicated seed states per conditional POST — a VIRGIN employee proving 201 AND a predecessor-carrying employee proving 200 (for acting: a pre-seeded acting line); never prove the reassign branch by mutating the first-assign row.** RED-on-lie extended to a multi-2xx route (an undeclared runtime status). Required/enum fidelity exercise automatically (strict spec).

**Validation Criteria**:
- [ ] All new Docker assertions green (exact counts); both conditional branches proven; the RED proof demonstrated
- [ ] No full-regression runs (scoped filters only)

---

### TASK-11504 — Orchestrator: PAT-012 amendment + validation + Step 7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-11504 |
| **Status** | complete (2026-07-11) — PAT-012 amended (the multi-2xx homogeneous-set rule + the flag-and-defer FIRST-FIRING record + the nullable-$ref residual 1→2 with the numbered escalation trigger + scope 49/87 + the S115 recipe notes: shape-copy the HANDLER's object / never `extends` a sibling response / one seed state per declared status / the typed-etag option surface as a compatibility contract). Validation via `sprint-test-validation` (delta table below; the aborted Jul-3 MSB4166 run re-done clean). Step 7a dual-lens 2 cycles converged (0 BLOCKER; 1 convergent WARNING + 1 Codex WARNING, all fixed + verified; section above). Close through the 5 gates. |
| **Agent** | Orchestrator |
| **Components** | PAT-012, sprint log, close gates |
| **KB Refs** | PAT-012, FAIL-003 |

**Description**: PAT-012: the single-2xx rule amended (+ "or a homogeneous conditional-status set — one shared `$ref`, runtime status ∈ the declared set"); the flag-and-defer rule's first firing recorded (the polymorphic GET, with cause); **the nullable-$ref residual count 1→2 WITH the numbered escalation trigger ("at 3+, do the `allOf`-wrapper + matcher change")**; scope status → 49/87 + the Pass-3 buckets. Validation (`sprint-test-validation`); Step 7a dual-lens; close per the 5 gates (the demo stack's :5432 sequencing per the S114 precedent).

**Validation Criteria**:
- [ ] PAT-012 amended; delta table; Step 7a converged; close + push + CI green all 7 jobs

---

## External Review (Step 7a)

Dual-lens, both cycle-1 → cycle-2 converged (2026-07-11). Artifacts: `.claude/reviews/SPRINT-115-step7a-{codex,reviewer}.md`.

| Lens | Cycle 1 | Cycle 2 (fix verification) |
|------|---------|---------------------------|
| External Codex (`codex review`, prompt-steered on the uncommitted diff) | **0 BLOCKER / 2 WARNING / 0 NOTE** | CLEAN — both fixes verified; the new esquery selector empirically AST-probed (sanctioned call 0 matches; two-arg + other-URL one-arg forms banned) |
| Internal Reviewer | **0 BLOCKER / 1 WARNING / 4 NOTE** | All fixes CLOSED, no new findings |

**Findings absorbed (the Step-7a fix commit rides the close commit):**
1. **Codex WARNING (FIXED) — simultaneous ETag preconditions**: a structured `apiFetchWithEtag` caller could pass BOTH `ifMatch` and `ifNoneMatch` → both RFC 7232 headers on the wire, no single create-vs-update semantics. Fixed at the TYPE level (`EtagOptionsIn` now intersects `{ifMatch?: string; ifNoneMatch?: never} | {ifMatch?: never; ifNoneMatch?: '*'}`) + a runtime throw in the structured-normalization branch before any fetch (unreachable for legacy `RequestInit` callers). Pinned by a `@ts-expect-error` negative probe (negativeProbes → 7) + a runtime `rejects.toThrow(/mutually exclusive/)`/no-fetch case. FE 567/567 (+1).
2. **CONVERGENT WARNING, both lenses (FIXED) — the eligibility lint carve-out was call-FORM scoped**: `[arguments.length=2]` sanctioned ANY future one-argument explicit-T call in `useEntitlementEligibility.ts`, not just the deferred polymorphic GET. Fixed with a ROUTE-HELPER pin: `NO_ETAG_TYPEARG_RULE_EXCEPT_ELIGIBILITY_GET` bans every explicit-T etag call `:not([arguments.length=1][arguments.0.callee.name='ELIGIBILITY_PATH'])`. Live RED probe: other-URL one-arg explicit-T fails lint; the sanctioned call passes. Accepted residual (documented): the pin is on the helper NAME — an in-file redefinition would ride the exemption (inherent to AST linting, diff-visible).
3. **Reviewer NOTE (FIXED)** — unused `using Xunit.Sdk;` removed from `S115EmployeeFieldSpecRuntimeTests.cs`.

**Accepted-and-documented NOTEs (no code change):** (a) multi-2xx over-declaration is gate-invisible without per-branch tests — the one-seed-state-per-declared-status rule is PAT-012 convention, not a gate (the lie is inert client-side: homogeneous set ⇒ identical FE type); (b) `default(SuccessContract)` NREs instead of a clean diagnostic — test-infra only, no live path constructs one; (c) the eligibility PUT's If-Match/update branch has no per-route Docker assertion — shape/status coverage complete via the create branch (same single-200 record), the FE If-Match mechanism covered generically.

**Confirmed SOUND by both lenses:** wire-byte identity on all 23 ops incl. every conditional branch (the internal lens diffed every removed anonymous object field-by-field; spec `nullable` ≡ CLR NRT on all 15 new schemas); P7 untouched (every `RequireAuthorization` policy string byte-unchanged); matcher single-status path exactly as strict as S112 (1-set membership ≡ equality); the FE holds exactly ONE explicit-T call (the sanctioned deferred GET); `AuditLogView` query strings byte-identical; manifest math 110→87 = exactly 23.

---

## Phase Plan
- **Phase 1 (parallel):** TASK-11500 (matcher extension — spec-independent, test-only) ∥ TASK-11501 (backend typing + regen pair)
- **Phase 2 (parallel):** TASK-11502 (FE — needs 11501's types) ∥ TASK-11503 (assertions — needs 11500's matcher + 11501's spec)
- **Phase 3:** TASK-11504 (docs + validation + close)

**Atomicity pin:** ONE close commit; no push mid-sprint; gates evaluated at close. TASK-11500 and 11501 are file-disjoint (tests/ vs src/+tools/+regen); 11502 and 11503 are file-disjoint (frontend/ vs tests/).

## Test Summary (close, 2026-07-11)

| Suite | Previous (S114) | Current | Delta |
|-------|-----------------|---------|-------|
| Unit | 861 | 861 | 0 |
| Regression | 1203 | 1234 | +31 |
| Smoke | 6 | 6 | 0 |
| DemoSeed | 55 | 55 | 0 |
| Frontend | 553 | 567 | +14 |
| **Total** | **2678** | **2723** | **+45** |

Delta composition: regression +31 = the 27 per-route Docker contract tests (17 reporting-lines + 7 field-endpoints + 3 admin-reads) + 4 new matcher unit cases (multi-2xx). FE +14 = the `useReportingLines.test.ts` wire suite (9) + the `ifNoneMatch` fixture/wire cases + 1 Step-7a mutual-exclusion runtime pin (the 566→567 step). Full-run integrity: the first S115-close regression attempt (Jul 3) aborted on MSB4166 child-node crashes and was re-run clean at close — 1230 first-pass + the 4 pre-existing FAIL-002 sheds isolation-cleared 4/4 (`TxContractTests`, `R9_CreateWithCrossTreeApprover`, `AllocationBreakdown` drift, `TerminatedEmployeeAccess` — none touch S115 surface). The 42 fixed-port tests ran locally against a fresh compose Postgres (:5432 free — no deferral, no waiver).

## Legal & Payroll Verification
| Check | Status |
|-------|--------|
| Agreement rule compliance | N/A — metadata + types + tests only; zero wire/behavior change (the 2 employee settlement POSTs are explicitly OUT — Pass-3 payroll bucket) |
| Wage type mapping correctness | N/A |
| Event sourcing / audit | N/A — the audit READ gets typed; no event/audit-path change |
