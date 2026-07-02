# Sprint 113 — Typed API Contract: the strict-types phase

| Field | Value |
|-------|-------|
| **Sprint** | 113 |
| **Status** | complete — CI GREEN `28611480203` (all 7 jobs, 2026-07-02) |
| **Start Date** | 2026-07-02 |
| **End Date** | 2026-07-02 |
| **Orchestrator Approved** | yes — 2026-07-02 (Step 7a BOTH lenses cycle-1 converged: Codex CLEAN; Reviewer APPROVED 0B/0W/4N) |
| **Build Verified** | yes — `dotnet build` 0 errors; `npm run build` 0 errors; `tsc --noEmit` clean (FIRST pass post-deletion); `npm run lint` clean |
| **Test Verified** | yes (local): **861 unit** (+9 `ResponseStrictTypesFilterTests`) + **1203 regression** (+7 matcher; central 1199 + 4 FAIL-002 sheds isolation-cleared 41/41 — payroll/skema/settlement classes untouched by S113; the 42 fixed-port tests passed CENTRALLY this run, compose Postgres up) + 6 smoke (rides CI) + 29 demoseed + **553 fe**; all 4 OpenAPI gates green on the strict spec; **pyramid 861u+1203r+6s+29demoseed+553fe = 2652 (+16 vs S112)**; **CI GREEN `28611480203` (all 7 jobs — the full regression + smoke + e2e + all 4 gates with required/enum-fidelity live in CI)** |

## Sprint Goal
The strict-types phase of the Typed API Contract program (PAT-012, [[typed-api-contract-program]]): make the generated TS types **directly strict** so the FE coercion scaffolding (`apiNarrow.ts` — `coerceApiResponse` + `AssertFieldsInSpec`, 57 occurrences across 7 files) is DELETED and the PAT-012 residuals (required-strictness; same-name-TYPE-change) CLOSE. Two spec-metadata emissions, zero wire-byte change (PAT-010): (1) `required` = all members for every schema in the **response-reachable closure** (the truthfulness rule: the null-emitting serializer always writes every record member — empirically affirmed by the Reviewer against all 26 real S112 per-route responses); (2) **string-literal enums** for the closed-set discriminators (owner-ratified Q1a: `[property: AllowedValues(...)]` on ~4-6 record members → generated TS literal unions → FE exhaustiveness preserved). The spec≡runtime gate gains **required-fidelity** + **enum-fidelity**.

Refinement: `.claude/refinements/REFINEMENT-strict-types.md` — **READY, owner-ratified 2026-07-02** (Q1 = (a) declare the discriminators in the spec). Dual-lens review CLOSED cycle 2: Codex 1B/1W → RESOLVED/0 new (the cycle-1 BLOCKER: `NonNullableReferenceTypesAsRequired()` doesn't exist in Swashbuckle 6.6.2 → custom filter, no upgrade); Reviewer BLOCKED 1B/2W → APPROVED (the BLOCKER: the string→literal-union SECOND variance `apiNarrow` bridges — now Q1a; the WARNINGs: null-emission is NOT global [`VacationSettlementSnapshot` carries `WhenWritingNull` ×3] → the closure scope + conditional-ignore skip; request-side declaration-vs-binder gap → requests UNTOUCHED by the filter).

**Explicit exclusions:** NO Swashbuckle upgrade; NO request-schema changes (their 33 `required` arrays are C#-`required`-keyword binder-enforced truth — filter-added request required would be unverifiable over-claiming); NO wire-byte/serializer/endpoint changes; NO retrofit-drain work (the 110 grandfathered ops untouched); NO new endpoints.

## Entropy Scan Findings (Step 0a)
| Check | Result | Detail |
|-------|--------|--------|
| `check_docs.py` | GREEN (run at S112 close) | 58 KB entries; sprint inventory through S112 pending the close-commit's own row (verified at this sprint's close) |
| Gate baseline | GREEN | convention 26 typed / 110 grandfathered; drift + freshness in sync at `c7404d4` |
| Grounding corrections vs the program's framing | 2 | (1) 33 request schemas ALREADY carry `required` (C#-`required`-driven, binder-enforced — the S112 "all-optional request bodies" note was overly broad → PAT-012 correction is an AC); (2) ZERO C# enums on the HTTP surface (0 in spec, 0 in Contracts) — enum emission per Q1a targets stringly-typed closed-set discriminators via `[AllowedValues]`, not C# enums |
| The future-lie landmine | DEFENDED | `SharedKernel/Models/VacationSettlementSnapshot.cs:157-191` carries `[JsonIgnore(WhenWritingNull/Default)]` ×4 — enters the closure at the payroll pass; the filter's conditional-ignore skip + a RED unit test pin it NOW |
| S112 CI | run `28589103053` in flight at plan time | MUST be green before this sprint's close (gate 2 enforces); backfill precedes S113 close |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (spec-pipeline semantics + the spec≡runtime gate extension + the shared FE type surface) |
| **External Codex** | cycle 1 (2026-07-02): **0B/1W/1N** — (W) enum-attribute scope ambiguous vs the request-untouched exclusion (`CreateUnitRequest.Type` is a Contracts member) → clarified RESPONSE-records-only; (N) stale occurrence count → zero-grep is the truth. Cleared the phase-seam concern (the phase-pin test pins path unions, not response optionality) |
| **Internal Reviewer** | cycle 1 (2026-07-02): **0B/1W/5N, APPROVED-WITH-WARNINGS** — (W) enum-fidelity null-on-nullable semantics undefined → defined (null admissible iff nullable); (N) atomicity pin restored; 11301 interface-inventory deliverable added; the out-of-set-data risk restored; 11302 marked not-descopable; **empirically verified the Phase-1→2 window is green by construction** (fixture aliases generated-to-generated; hooks compile through coercion until deletion) |
| **BLOCKERs resolved before Step 1** | **yes — ZERO BLOCKERs from either lens; both WARNINGs + all actionable NOTEs absorbed by plan edit** (no cycle-2 required per the workflow: only BLOCKERs mandate re-review) |

---

### TASK-11300 — Backend: the strictness filter + `[AllowedValues]` + the regen pair
| Field | Value |
|-------|-------|
| **ID** | TASK-11300 |
| **Status** | complete (2026-07-02; usage-limit interruption → resumed, regens re-run hash-identical) — `ResponseStrictTypesFilter` (ONE instance = ISchemaFilter [schemaId→CLR map] + IDocumentFilter [2xx-closure walk incl. 2XX ranges; visited-set terminates self-refs; fail-CONSERVATIVE on unresolvable mappings]; both generation paths covered by construction — one DI container). `required` = all closure members EXCEPT conditional-ignore + **the nullable-$ref exception (FINDING):** OpenAPI 3.0 forbids `nullable` on a bare `$ref` → marking a CLR-nullable complex member required would claim never-null (the DANGEROUS-direction lie) → excluded from `required`, TS stays optional (today: exactly `RosterEmployeeRow.outgoingVikar`; documented as the new bounded residual — the truthful `allOf`-wrapper fix needs a coordinated matcher change). **Overlap policy: response-truth wins** (binder-enforced request subset ⊆ all-members; over-strict fails SAFE). **14 discriminator members / 5 sets, each with a cited authority** (unit type + orgType + vikar reason + scopeType = DB CHECKs; periodStatus = the TOTAL `ProjectStatus` projection — never null, correcting the plan's example); 2 sibling sets FOUND (vikar reason, scopeType); `employmentCategory` correctly REFUSED (open config-keyed set). Seed cross-checks ALL CLEAN. **Validated (agent + Orchestrator re-ran): build 0/0; spec delta = 28 closure schemas, ONLY required/enum additions (288 pure insertions), all 58 request schemas + paths byte-identical; api-types strict (`parentOrgId: string \| null`, `orgType: "MAO" \| "ORGANISATION"` literal unions); convention 26/110 + drift GREEN; Docker Contracts 50/50 UNMODIFIED.** 2 PROPOSED KB entries accepted → 11303 |
| **Agent** | Backend API (cross-domain authorized: `src/Backend/StatsTid.Backend.Api/**` [the filter + `Program.cs`/`OpenApiSpecGenerator.cs` registration + `[property: AllowedValues]` on Contracts record members], `docs/api/openapi.json` + `frontend/src/lib/api-types.ts` [REGEN ONLY — both in THIS task, killing the S112 freshness window]) |
| **Components** | Swashbuckle document/schema filter; Contracts records (attributes only) |
| **KB Refs** | PAT-012, PAT-010 |

**Description**: (A) A custom Swashbuckle filter (document-filter shape — it needs the whole doc): compute the **response-reachable schema closure** (every 2xx response `$ref`, transitively over `components`; today 28/28 all-`Contracts/`) and for each closure schema set `required` = ALL members **except** members carrying `[JsonIgnore(Condition=WhenWritingNull/WhenWritingDefault)]`. Request-only schemas UNTOUCHED; define + document the **overlap policy** for a hypothetical schema reachable from both sides (disjoint today). Nullability continues via the existing `SupportNonNullableReferenceTypes()`. (B) `[property: AllowedValues(...)]` on the closed-set discriminator members of **RESPONSE records ONLY** (Step-0b Codex WARNING: request DTOs are Contracts members too — `CreateUnitRequest.Type` is explicitly EXEMPT; the request-schema byte-UNCHANGED spot-proof enforces it) — inventory them (known: forest/search/units response `type` [the `UnitType` set], `orgType` [`MAO`/`ORGANISATION`], roster `periodStatus`; find any siblings); the filter reads the attribute's `Values` and emits `enum: [...]` (Swashbuckle 6.6.2 doesn't map it natively — read explicitly). **Verify each inventoried member's declared set against the values the fixtures/seeders actually serialize** (Reviewer: the S112 fixture unit types are all in the `TypeRank` set; a demo-seeded value outside a declared set would turn the 11302 fidelity assertions RED as a REAL finding). (C) Regen BOTH artifacts: `--openapi` → `docs/api/openapi.json`, then `npm run gen:api` → `api-types.ts`; commit-ready, hand-edit neither.

**Validation Criteria**:
- [ ] Build 0 errors; the spec: `UnitResponse`/`OrganizationResponse` (etc.) carry full `required` arrays; `CreateUnitRequest`/`CreateUserRequest` byte-UNCHANGED; the discriminator members carry `enum` values; nullable members stay `nullable: true`.
- [ ] `api-types.ts`: response types strict (no `?` on closure members; nullable → `T | null` — spot-pin `OrganizationResponse.parentOrgId`); the discriminators are literal unions.
- [ ] Drift + freshness + convention gates green on the regenerated pair (26/110 unchanged — this phase drains nothing).
- [ ] ZERO wire-byte change: existing contract tests green UNMODIFIED (spot-run the Contracts filter if Docker is up).

---

### TASK-11301 — FE: delete the coercion scaffolding (depends: 11300)
| Field | Value |
|-------|-------|
| **ID** | TASK-11301 |
| **Status** | complete (2026-07-02) — `apiNarrow.ts` DELETED; ZERO-grep proven; **tsc clean on the FIRST pass** (no downstream component forced); the full interface inventory delivered (6 files: 11 DELETED→spec-alias, 8 KEPT-with-direct-assignment, 1 CHANGED=FINDING, 8 out-of-scope grandfathered untouched); the `RosterRow` nullable-$ref override pattern (`Omit<Spec,'outgoingVikar'> & {…}` — single-property override, everything else spec-derived); fixture test verified NO-OP (as the Reviewer predicted); ZERO mock corrections needed (all mocks already in-union). **⚠ FINDING 1 — ANOTHER REAL CONTRACT LIE caught by strictness: `RoleAssignment.assignedBy` claimed non-null `string`; `RoleGrantResponse` serves `string \| null` (backend `string? AssignedBy`) — a NULLABILITY lie the S111 field-NAME-only guards structurally could not see; fixed to the honest union, sole renderer null-safe.** Finding 2: `PersonSearchHit.primaryOrgName` kept deliberately wider (benign direction, documented). Finding 3: per-node-kind orgType literals → the shared generated union (generator can't express per-kind constants; no consumer relied on it). **Validated (agent + Orchestrator re-ran): zero-grep 0; tsc 0; build 0; lint clean (the one new narrowing is a runtime type GUARD, not `as`); vitest 553/553 with 412/envelope pins UNMODIFIED; `check_endpoint_contracts.py` green.** 3 PROPOSED KB entries accepted → 11303 |
| **Agent** | UX (`frontend/src/lib/apiNarrow.ts` [DELETE], `useAdmin.ts`, `useForest.ts`, `useSearch.ts`, `useRoster.ts`, `useReportingLines.ts`, `pages/admin/editPerson/employeeProfileApi.ts`, the S112 drift-guard sites, `frontend/src/lib/__tests__/api-typed-overloads.test.ts` [fixture + phase-pin updates], affected hook tests) |
| **Components** | frontend hooks + lib |
| **KB Refs** | PAT-012, PAT-010, ADR-019 |

**Description**: Delete `apiNarrow.ts`; remove every `coerceApiResponse` call + `AssertFieldsInSpec` guard (~63 tokens across 7 files per the Step-0b recount — the ZERO-GREP criterion is the truth, not the count) + the S112 drift guards where the generated type takes over; the hooks' hand-written interfaces either DELETE in favor of the generated types or (where a local view-type genuinely differs) assign directly from the strict generated type — any mismatch surfacing is a FINDING reported prominently (the S112 lesson: two prod bugs lived exactly here), never a silent adjustment. **Mandatory deliverable (Reviewer NOTE, the S112 mapping-table pattern): a per-file interface inventory in the task output — each hand-written interface classified DELETED / KEPT-with-direct-assignment (e.g. `User`, pre-verified assignable from both strict `UserCreatedResponse` and `UserDetailResponse`) / CHANGED (= a FINDING with field evidence)** — making no-silent-adjustment auditable rather than trusted. Update the typed-overload fixture test (synthetic fixtures + phase-pin) for the stricter real-spec truth. Both lenses verified `coerceApiResponse` is `return value as T` — deletion is runtime-neutral.

**Validation Criteria**:
- [ ] `apiNarrow.ts` gone; `grep coerceApiResponse|AssertFieldsInSpec frontend/src` → ZERO; drift guards superseded-and-removed.
- [ ] Repo-wide `tsc` clean; `npm run build` 0 errors; no-`as` lint green (deletions only remove `as` sites); vitest green (mock corrections reported).
- [ ] 412/If-Match + envelope pins green UNMODIFIED.

---

### TASK-11302 — Test & QA: required-fidelity + enum-fidelity + the filter unit tests (depends: 11300)
| Field | Value |
|-------|-------|
| **ID** | TASK-11302 |
| **Status** | complete (2026-07-02) — matcher: REQUIRED-fidelity (required-but-absent = RED with the member name; enforced independently of `properties`; present-but-null SATISFIES required — the JSON-Schema-correct boundary, test-pinned) + ENUM-fidelity (non-null out-of-set = RED, inline AND behind-`$ref`; **the null rule: `nullable` alone governs null — the enum set NEVER arbitrates it**) + the stale "Swashbuckle does not populate required" doc rewritten. `SpecRuntimeMatcherTests` 13→20 (7 new incl. both null-rule directions). **Filter unit tests 9/9 in `tests/StatsTid.Tests.Unit/OpenApi/` driving the REAL Swashbuckle SchemaGenerator (Program.cs config mirrored — declared coupling), not mocks**: transitive closure, request-only untouched + no enum leak, non-2xx excluded, fail-conservative skip, the conditional-ignore `VacationSettlementSnapshot` future pinned RED, the nullable-$ref exception pinned (with a non-nullable complex control), `[AllowedValues]`→enum, the response-truth overlap policy. **Docker re-verification: 28/28 (22 S112 + 6 S111) against the strict spec with the extended matcher — no relaxation needed. Orchestrator re-ran: matcher 20/20, filter 9/9.** Tests-only scope held. PROPOSED KB (required/nullable orthogonality one-liner) accepted → 11303 |
| **Agent** | Test & QA (`tests/**`) |
| **Components** | spec≡runtime gate; filter unit tests |
| **KB Refs** | PAT-012, FAIL-002 |

**Description**: (A) `SpecRuntimeMatcher`: **required-fidelity** — every schema-`required` member must be PRESENT in the real serialized response (required-but-absent = RED); **enum-fidelity** — a serialized discriminator value outside the declared `enum` set = RED, with the **null-on-nullable semantics DEFINED** (Step-0b Reviewer WARNING): `null` is admissible iff the member is `nullable: true` (e.g. roster `periodStatus` may legitimately serialize null when no approval period exists); only a NON-NULL out-of-set value is RED. Matcher unit tests + RED proofs for both (extend the S112 RED-on-lie pattern). The 26 per-route Docker assertions re-run green with the stricter schemas. (B) **Filter unit tests** (non-Docker): pin the closure derivation (a synthesized doc), the conditional-ignore skip (RED case: a `WhenWritingNull` member marked required — the `VacationSettlementSnapshot` future), the request-side non-emission, and the declared overlap policy.

**Validation Criteria**:
- [ ] Matcher tests green incl. the 2 new fidelity dimensions + RED proofs; `Contracts.S112` 22/22 + `OpenApiSpecRuntimeTests` 6/6 green against the strict spec (Docker).
- [ ] Filter unit tests green incl. the `WhenWritingNull` RED case.
- [ ] No full-regression runs (scoped filters only).

---

### TASK-11303 — Orchestrator: PAT-012 rewrite + validation + Step 7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-11303 |
| **Status** | complete (2026-07-02) — PAT-012 rewritten (gate-1 gains required/enum-fidelity with the null-orthogonality principle; residuals: required-strictness + same-name-TYPE-change CLOSED, the NEW bounded nullable-$ref residual documented [3 coherent places; kept on the program backlog — the payroll pass has more nullable complex members], the request-side declaration-vs-binder boundary documented; **the FALSE S112 "all-optional request bodies" sentence CORRECTED in PAT-012 + a dated gloss on SPRINT-112.md** [history annotated, not rewritten]; Vindication extended to the NULLABILITY axis + the runtime-type-guard convention; Scope status → strict-types DONE, future passes land strict automatically). Validation delta table produced (2652, +16). Step 7a BOTH lenses cycle-1 converged (see below) |
| **Agent** | Orchestrator |
| **Components** | PAT-012, sprint log, close gates |
| **KB Refs** | PAT-012, FAIL-003 |

**Description**: PAT-012: Known residuals REWRITTEN (required-strictness CLOSED; same-name-TYPE-change CLOSED; enum residual resolved via `[AllowedValues]`); the FALSE "generated request-body types are all-optional" sentence CORRECTED in PAT-012 §paved-road-step-3 + the S112 log echo (the truth: C#-`required` members emit as required and are binder-enforced — S112's own createUser finding proves it; the binder-enforced subset = exactly the C#-`required` members, documented); the paved road gains the strictness rule + the `[property: AllowedValues]` step. Validation via `sprint-test-validation`; Step 7a dual-lens; close through the 5 mechanical gates AFTER the S112 CI backfill is green.

**Validation Criteria**:
- [ ] PAT-012 rewritten; S112-log echo corrected; sprint log complete with the delta table.
- [ ] Step 7a both lenses converged; close commit through the gates; pushed; CI GREEN all 7 jobs.

---

## Phase Plan
- **Phase 1:** TASK-11300 (filter + attributes + BOTH regens — no freshness window by construction)
- **Phase 2 (parallel):** TASK-11301 (FE deletion — needs the strict `api-types.ts`) ∥ TASK-11302 (gates — needs the strict spec)
- **Phase 3:** TASK-11303 (docs + validation + close; gated on the S112 CI green backfill)

**Atomicity pin (Step-0b, both lenses):** ONE close commit. The Reviewer verified the Phase-1→2 window is green by construction (the fixture test's real-spec aliases are generated-to-generated; the phase-pin is strictness-invariant; the hooks still compile through `coerceApiResponse` until 11301 deletes it) — but any intermediate FE type-surface issue that DOES surface belongs to TASK-11301, not to 11300 (no "helpful" fixture fixes from inside 11300's scope); NO push before Phase 2 completes. **TASK-11302 is not descopable/deferrable** — the conditional-ignore skip and overlap policy are unexercised by the real spec (zero instances in today's closure), so 11302's synthetic tests are their ONLY proof; dropping it re-opens 11300's adequacy.

## Risks (plan-level)
- **Out-of-set seeded/demo DATA** (restored from the refinement per Step-0b): a demo-seeded unit type or status outside a declared `[AllowedValues]` set turns the 11302 fidelity assertions RED — greenfield data makes it unlikely; any hit is a REAL finding (fix the set or the seed, reviewed — never silently widen).

## External Review (Step 7a)
| Lens | Result |
|------|--------|
| **External Codex** (`codex review`, full uncommitted diff) | **cycle 1: "Clean — no findings"** (ran its own scoped checks: backend build, filtered filter/matcher tests, `tsc --noEmit`). Artifact: `.claude/reviews/SPRINT-113-step7a-codex.md` |
| **Internal Reviewer** (same instance across the whole program — refinement + Step-0b + S112 context) | **cycle 1: APPROVED — 0B/0W/4N.** Verified programmatically: the spec delta = EXACTLY the 28 closure schemas with only required/enum additions; all 58 request schemas + `paths` byte-identical; the 8 Contracts diffs attributes+comments only (every hunk); every enum set verified against its cited authority; the `assignedBy` nullability-lie fix verified against the backend; the matcher's null-orthogonality semantics exact; scoped tests re-run green. Artifact: `.claude/reviews/SPRINT-113-step7a-reviewer.md` |

NOTEs absorbed at close: 2 safe FE micro-refinements recorded (the `useForest` `?? []` fallback dropped — the key is now spec-required + gate-enforced; the 412 guard passes `undefined` for a non-object body — both unreachable on real responses); the orgType per-kind-literal→shared-union widening stands as honestly documented (generator limitation); the nullable-$ref residual stays on the program backlog; `.codex_diff.txt` re-deleted.

## Test Summary
| Suite | S112 | S113 | Delta |
|-------|------|------|-------|
| Unit | 852 | **861** | **+9** (`ResponseStrictTypesFilterTests` — real-Swashbuckle-generator driven) |
| Regression (Docker) | 1196 | **1203** | **+7** (`SpecRuntimeMatcherTests` 13→20: required/enum-fidelity + null-rule cases) |
| Smoke | 6 | 6 (rides CI) | 0 |
| DemoSeed | 29 | 29 | 0 |
| Frontend (vitest) | 553 | 553 | 0 (scaffolding deletion is type-level; all mocks were already in-union) |
| **Total** | **2636** | **2652** | **+16** |

Regression detail: central run 1199/1203 in 1h06m — the 42 fixed-port tests passed centrally (compose Postgres up, unlike S112's run); 4 FAIL-002 churn sheds (`RetroactiveCorrectionManifest` ×2, `SkemaFullDayOnlyGuard`, `EmploymentEndDateLifecycle` — classes S113 does not touch) isolation-cleared 41/41 with classmates. Zero S113-attributable failures.

## Legal & Payroll Verification
| Check | Status |
|-------|--------|
| Agreement rule compliance | N/A — spec metadata + FE types + tests only; zero wire/behavior change |
| Wage type mapping correctness | N/A — untouched |
| Event sourcing / audit | N/A — untouched (the `VacationSettlementSnapshot` conditional-ignore members are DEFENDED, not touched) |
