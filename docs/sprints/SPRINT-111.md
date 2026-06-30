# Sprint 111 — Typed API Contract, Phase 0: the foundation (pipeline + gates + proof)

| Field | Value |
|-------|-------|
| **Sprint** | 111 |
| **Status** | complete — CI GREEN `28473854257` (all 7 jobs) |
| **Start Date** | 2026-06-30 |
| **End Date** | 2026-06-30 |
| **Orchestrator Approved** | yes |
| **Build Verified** | yes — `dotnet build` + `npm run build` 0/0 |
| **Test Verified** | yes — **CI GREEN `28473854257` (all 7 jobs; the 4 NEW gates [drift→build-and-test, freshness+lint→frontend-build, convention→docs] all ran in CI + passed; + 21 Contracts + 6 matcher + 531 vitest + regression + smoke + e2e)**; PAT-010 byte-identity held; Step-7a dual-lens BOTH 0 BLOCKER (closure-critical W's FIXED in Phase 0) |

## Sprint Goal
Phase 0 of the long-term commitment to OpenAPI as the durable FE↔backend contract — **the foundation: the generate→spec→types→FE pipeline + the gates that make the commitment self-sustaining + an end-to-end proof on a tiny surface.** This structurally closes the recurring "fetchEnheder" shape-mismatch bug class (S97→S99→S100) for the proof surface AND installs the **convention gate** that forces every *future* endpoint to ship typed (the durability keystone — the lesson recurred 3× *with* the lesson written down because nothing CI-enforced it). The bulk **retrofit of existing endpoints is explicitly DEFERRED** to subsequent phases (lazy / risk-prioritized). Decisions locked (owner + Step-4 dual-lens-clean refinement `REFINEMENT-fork-b-typed-client.md`): **single typed client** (evolve the existing `apiClient`, NOT a second client — via the STRUCTURED `get(pathKey, {params,query})` call shape so templated/query paths bind, Step-0b); **proof surface (Step-0b-corrected) = 4 CONSUMABLE registry reads typed end-to-end** (`organizations`, `units/forest` [literal], `search` [query], `roster` [templated]) + **`organizations/tree` typed backend-only** (FE-orphaned post-S109) + **1 admin mutation** request DTO; **OpenAPI/Swashbuckle** as the long-term spec source. **The closure is the PER-ROUTE spec≡runtime gate** (`.Produces<T>` is a convention that can lie about array-ness); the no-DB spec gen is a guarded **`--openapi` entrypoint**; the convention gate rides **`openapi.json`** (empty-schema detection), not the FE-call lint.

## Entropy Scan Findings
| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | _pending TASK-11100_ | `check_docs.py` pre-sprint |
| Existing OpenAPI | NONE | no Swashbuckle/`Microsoft.AspNetCore.OpenApi`; `.Produces<>`/`TypedResults`/`Results.Ok<T>` all ZERO; 94 anonymous `Results.Ok(new {...})` |
| Existing pipeline precedent | PRESENT | `generate_db_schema.py` + `check_design_sync.py` + `check_endpoint_contracts.py` (a REGISTRY/coverage lint) + the PAT-010 `Contracts/` records + the contract tests (the spec≡runtime anchors) |
| Proof surface | the 5 REGISTRY reads | `/api/admin/organizations`, `/organizations/tree`, `/units/forest`, `/search`, `/reporting-lines/tree/{}/medarbejdere` (all `apiClient.get`, none `apiFetchWithEtag`) |

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (new build tooling + CI gates [P8]; the spec≡runtime gate is the load-bearing closure; the single-client binding touches the shared `apiClient`; the no-DB spec-generation mechanism). |
| **External Codex** | invoked 2026-06-30 — 4B/2W → cycle-2 all RESOLVED |
| **Internal Reviewer** | invoked 2026-06-30 — 2B/2W/2N → cycle-2 all RESOLVED (3 cosmetic NOTES fixed; 0 residual BLOCKER) |
| **BLOCKERs resolved before Step 1** | yes — the 4 convergent BLOCKERs (per-route gate / `--openapi` no-DB gen / structured binding / spec-based convention gate) absorbed; cycle-2 both lenses RESOLVED |

### Findings (cycle 1)
Both lenses converge on 4 structural corrections (the response-side typing is well-grounded; the issues are in the *mechanisms*). All absorbed:
- **BLOCKER (both, TASK-11101) — the spec≡runtime gate must be PER-ROUTE, not per-shape exemplar.** `.Produces<T>` is author-chosen metadata that can lie (esp. array-ness — `/organizations` returns `IEnumerable<OrgListItem>`, the record is the array ELEMENT → must be `.Produces<IEnumerable<OrgListItem>>`). → per-operation schema validation against each captured real response (root kind/array-ness, required props, nullable-required, item schema, camelCase); `.Produces` is a convention, the gate is the closer.
- **BLOCKER (both, TASK-11101) — `swagger tofile` is NOT no-DB** (`Program.cs` runs 6 seeders/backfills + 3 hosted services before `app.Run()`; the WebApplicationFactory needs Docker). → a guarded `--openapi` entrypoint that maps endpoints + writes the doc BEFORE the seeders (mapping needs services registered, not connected → Docker-free).
- **BLOCKER (both, TASK-11102) — the literal-key binding can't type templated/query paths**, AND `organizations/tree` is FE-ORPHANED (no consumer post-S109). → the structured `get(pathKey, {params,query})` call shape; the proof = 4 consumable reads (org-list/forest/search/roster) + org-tree backend-only.
- **BLOCKER (both, TASK-11103) — the FE-call lint can't see a new backend endpoint.** → a separate backend gate riding `openapi.json` (empty-schema detection) + a backend-path grandfather manifest; `check_endpoint_contracts.py` stays FE-coverage-only.
- WARNING (both) — bare-array `.Produces` must be the collection type; the request DTO convention (named `Contracts/` record + `.Accepts<T>`, the existing ones are private nested classes). → absorbed.
- WARNING (Codex) / sizing — Phase 0 is multi-slice → 2 internal checkpoints (backend gates / FE binding+convention gate); checkpoint 2 may split to a follow-up. → absorbed.
- NOTE (Reviewer) — the request side is spec≡DTO (weaker than spec≡runtime; web JSON is case-insensitive on input); sequence 11101 → (11102,11103) → 11104. → absorbed.

### Resolution
The 4 convergent BLOCKERs + the WARNINGs + the NOTEs absorbed into the Goal + TASK-11101/11102/11103/11104.

**Cycle 2 (verification):** BOTH lenses confirm all findings RESOLVED + no new BLOCKER. 3 cosmetic Reviewer NOTES fixed: `UnitEndpoints.cs` (the `units/forest` `.Produces`) added to the TASK-11101 scope; the TASK-11102 Components reconciled to "4 consumable hooks"; the "~130 existing string-path callers still compile" criterion made explicit (the typed-overload ordering). **0 residual BLOCKER; cycle-cap (2/lens) respected. Cleared for Step 1.** The value: both lenses converged that the first draft's mechanisms (per-shape gate / `swagger tofile` / literal-key binding / FE-lint convention gate) each collided with the verified codebase — corrected pre-code to the per-route gate, the `--openapi` entrypoint, the structured-call binding, and the spec-based backend gate.

## Architectural Constraints Verified
- [x] P8 — the pipeline + 4 gates (spec≡runtime / drift / convention / **freshness** + the no-`as` lint) are CI-wired (drift→build-and-test; convention→docs; freshness+lint→frontend-build) and self-enforcing; the convention gate forces new endpoints typed (130 grandfathered, manifest only shrinks, stale entry FAILS).
- [x] Closure (not relocation) — the PER-ROUTE spec≡runtime gate asserts each committed schema ≡ its REAL serialized bytes (array-ness/camelCase/nullable; RED on an injected array-ness lie), and the freshness gate closes the spec→TS hand-off; `.Produces<T>` + the FE type cannot agree with each other while disagreeing with reality. (Residual: same-name TYPE change — documented, deferred.)
- [x] Single client — the path→response-type binding lives IN `apiClient` (typed overload + string fallback; auth/`handle401`/`ApiResult` intact; the ~130 string callers still compile); NO second HTTP client.
- [x] No behavior change — response-typing metadata + tooling only; the runtime JSON byte-identical (PAT-010 held); no route/auth/rule/event path touched; the priority order untouched.

## Task Log

### TASK-11100 — Sprint open (entropy + plan + Step-0b)
| Field | Value |
|-------|-------|
| **ID** | TASK-11100 |
| **Status** | complete — entropy CLEAN; plan authored; Step-0b dual-lens (2 cycles; 4 convergent BLOCKERs absorbed — the per-route spec≡runtime gate, the `--openapi` no-DB entrypoint, the structured-call binding [+ the FE-orphaned org-tree], the spec-based backend convention gate; 0 residual). |
| **Agent** | Orchestrator |
| **KB Refs** | `REFINEMENT-fork-b-typed-client.md` (Step-4 dual-lens-clean), PAT-010, the existing drift-gate tools |

**Validation Criteria**:
- [x] Entropy recorded; plan authored; Step-0b dual-lens run (the spec≡runtime gate; the no-DB spec gen; the single-client binding; the convention gate); BLOCKERs absorbed before Step 1.

---

### TASK-11101 — The backend spec pipeline + the spec≡runtime gate + the drift gate
| Field | Value |
|-------|-------|
| **ID** | TASK-11101 |
| **Status** | complete (checkpoint 1) — Swashbuckle 6.6.2 (doc-only; camelCase via MVC `JsonOptions` [Swashbuckle ignores `Http.Json`], `SupportNonNullableReferenceTypes`, `CustomSchemaIds`); the **`--openapi` no-DB entrypoint** (shared `ApiEndpoints.MapAll` → spec can't drift from runtime; `UseRouting`/`UseEndpoints` link the DI `EndpointDataSource` no-Kestrel/no-DB — **proven: Postgres stopped+removed → 101 paths/74 schemas, byte-identical**); committed `docs/api/openapi.json`; the proof reads typed (`/organizations` → `.Produces<IEnumerable<OrgListItem>>` [array-ness confirmed], roster → new `RosterContracts`, POST `/units` → `CreateUnitRequest` + `.Accepts`+`.Produces<UnitResponse>(201)`); **byte-identity HELD** (PAT-010 green). The **per-route spec≡runtime gate** (`SpecRuntimeMatcher` — root/array-ness, presence, camelCase, nullable, item/dict schemas; `OpenApiSpecRuntimeTests` 5 routes + `Gate_IsRed_OnInjectedArraynessLie` + 6 matcher units, all green). The **drift gate** `check_openapi_sync.py` (regenerates no-DB + parsed-JSON compare; `--selftest`; verified RED-on-perturbation). Build 0 err; 21 Contracts + 6 matcher green. **Flag: CI wiring → the drift gate needs .NET (regenerates) → `build-and-test`; the convention gate [TASK-11103] reads the committed json → `docs` job.** |
| **Agent** | Backend + Infrastructure |
| **Components** | `Program.cs` (the `--openapi` entrypoint), `Endpoints/AdminEndpoints.cs` (organizations/org-tree/search/roster + 1 mutation) + **`Endpoints/UnitEndpoints.cs`** (`units/forest`'s `.Produces`, `~:91`), `Contracts/`, a new `openapi.json` (committed), `tools/check_openapi_sync.py`, the spec≡runtime test |
| **KB Refs** | the PAT-010 records + contract tests; the `db-schema` generate-and-gate pattern |

**Description**:
1. Add **`Swashbuckle.AspNetCore`** (document-only — no Swagger UI in prod) **configured to the web/camelCase property policy** (Swashbuckle does NOT read minimal-API `Http.Json.JsonOptions` → emits PascalCase unless told; a missed config = a loud `tsc` mismatch).
2. **The no-DB spec generation = a guarded `--openapi` doc-only entrypoint [Step-0b BLOCKER, both lenses].** `swagger tofile` against the normal host FAILS — `Program.cs` runs 6 inline DB-touching seeders/backfills (`:341-428`) + 3 `AddHostedService` (`:34-36`) before `app.Run()`; the WebApplicationFactory alternative needs Docker Postgres (drags Docker into the lightweight `docs` job). → in `Program.cs`, BEFORE the seeders/hosted-services: `if (args contains --openapi) { register services [NO connect] → Map the endpoints → write the `ISwaggerProvider` doc to file → return; }`. Feasible because minimal-API handlers resolve services PER-REQUEST → mapping needs them REGISTERED, not CONNECTABLE → no DB. Commit `openapi.json`.
3. **Type the proof surface (records + `.Produces` — a CONVENTION, not the closure [Step-0b]):** each in-scope read returns its named record (reuse/extend the PAT-010 `Contracts/` records) AND declares `.Produces<T>(200)` with the **CORRECT collection-ness** — a bare-array endpoint is **`.Produces<IEnumerable<OrgListItem>>`** (the record is the array ELEMENT, `OrgListItem.cs:14`), NOT `.Produces<OrgListItem>`. (`.Produces<T>` is author-chosen + independent of the returned instance → "reuse the same instance" does NOT mechanically prevent an array-ness lie; the per-route gate in step 4 is the closer.) Plus **1 admin mutation** (a POST/PUT body) gets a named **request DTO record** in `Contracts/` + `.Accepts<T>()` (the request-side proof; note: spec≡DTO, weaker than spec≡runtime — `JsonSerializerDefaults.Web` is case-insensitive on input).
4. **The spec≡runtime gate (THE closure — a hard PER-ROUTE CI gate [Step-0b BLOCKER, both lenses]):** for EVERY in-scope operation (not one exemplar per shape), assert its committed `200` schema matches its OWN real serialized response (reuse the PAT-010 contract tests' captured responses) — **root kind/array-ness**, **required properties**, **nullable-required fields**, **array item schema**, **camelCase**. A `.Produces` that mis-states array-ness or a field FAILS this gate.
5. **The drift gate** `tools/check_openapi_sync.py` — regenerate the spec (via `--openapi`, Docker-free), fail on diff vs the committed `openapi.json` (mirrors `check_design_sync.py`); wire into the `docs`/CI job.

**Validation Criteria**:
- [ ] `Swashbuckle` camelCase-configured; the `--openapi` entrypoint generates `openapi.json` Docker-free (short-circuits before the seeders) + committed; the in-scope reads typed with correct collection-ness + the 1 mutation request DTO (`.Accepts<T>`); the **per-route** spec≡runtime gate RED-on-array-ness/field/casing/nullable divergence for each operation; the drift gate fails on an un-regenerated change; `dotnet build` + the gates green.

---

### TASK-11102 — The single typed client (the FE binding) + codegen
| Field | Value |
|-------|-------|
| **ID** | TASK-11102 |
| **Status** | complete (checkpoint 2) — `openapi-typescript@7` → committed `api-types.ts` (idempotent) + `npm run gen:api`; `apiClient.get` overloaded — a typed `get<P extends GetPath>(pathKey, {params,query})` (helper types over the generated `paths` — literal/query/templated all bind from the key; URL-encoded interpolation) FIRST + the string `get<T>(path)` fallback SECOND (**the ~130 existing `get<ExplicitT>` callers fall through → `tsc` 0 err, no mass-retrofit**); Bearer/`handle401`/`ApiResult` untouched. The 4 hooks wired (org-list/forest/search/roster). The no-`as` lint (eslint flat config) on the 3 single-read hooks (`useAdmin` excluded — co-hosts deferred reads; declared). PAT-010 lint re-pointed (`{name}`→`{}` normalize). `npm run build` 0 err; **531 vitest**. **FINDING (→ Step-7a): the spec has NO `required` arrays → generated types all-OPTIONAL + enums-as-`string` → the FE bridges via `coerceApiResponse` (`apiNarrow.ts`, a single `as`) + per-type `AssertFieldsInSpec<Local,Spec>` compile-time guards (FE field-set ⊆ spec; a renamed/removed field → tsc error, demonstrated). Closes the bug class, but a `required`-emitting Swashbuckle config would make types directly-strict (a cleaner paved road).** |
| **Agent** | UX (frontend) |
| **Components** | `frontend/src/lib/api.ts` (`apiClient`), a new committed `frontend/src/lib/api-types.ts`, the **4 consumable hooks** (`useAdmin` org-list / `useForest` / `useSearch` / `useRoster`; org-tree is backend-only), an eslint rule, `package.json` (the typegen script) |
| **KB Refs** | the refinement OQ-2 (single-client end-state) |

**Description**: **Evolve the EXISTING `apiClient` into the typed client — NO second HTTP client** (keep its Bearer-token + `handle401` + `ApiResult<T>` envelope). **Depends on TASK-11101's committed `openapi.json`.**
1. **Codegen:** `openapi-typescript` → committed `frontend/src/lib/api-types.ts` (the `paths` type); an `npm run gen:api` script.
2. **Bind path→response-type via the STRUCTURED call shape [Step-0b BLOCKER, both lenses — a raw literal `keyof Paths` CANNOT type a templated/query path].** A naive `get<P extends keyof Paths>(path: P)` only matches LITERAL keys → `search` (`/api/admin/search?q=…`) + `roster` (`/api/admin/reporting-lines/tree/${org}/medarbejdere`) widen to `string` and fall to the UNTYPED overload. → adopt the openapi-typescript-helpers call shape **inside `apiClient`**: `get(pathKey, { params?: {path}, query? })` — `pathKey` is the templated literal (`'/api/admin/reporting-lines/tree/{organisationId}/medarbejdere'`), `apiClient` interpolates `params`/`query`, and the response type derives from `pathKey`. Un-bypassable for in-scope calls; the plain string overload stays for non-retrofitted callers.
3. **Wire the in-scope hooks — corrected surface [Step-0b: `organizations/tree` is FE-ORPHANED, no consumer post-S109].** The **4 CONSUMABLE reads**: `organizations` (`useAdmin.ts:82`, literal), `units/forest` (`useForest.ts:97`, literal), `search` (`useSearch.ts:105`, query — uses the new shape), `roster` (`useRoster.ts:101`, templated param — uses the new shape) → switch to the typed `apiClient` call, remove the hand-written `T`. **`organizations/tree` is typed BACKEND-only** (in the spec + the gate; no FE hook to wire). Re-point the PAT-010 inline-URL lint to the path-key form where a call site changes.
4. **The no-`as` lint:** an eslint rule forbidding `as`/hand-written response types on the in-scope hooks.

**Validation Criteria**:
- [ ] `api-types.ts` generated + committed; `apiClient` exposes the structured typed call (`get(pathKey, {params,query})`) that types BOTH literal AND templated/query paths; the 4 consumable hooks (org-list/forest/search/roster) use it with their hand-types removed; `organizations/tree` typed backend-only; the no-`as` lint active in-scope; the existing auth/401/error behavior unchanged AND **the ~130 existing `get<ExplicitT>(stringPath)` callers still compile** (the typed overload ordered so a non-`keyof Paths` arg falls to the string fallback — validate, no mass-`tsc` regression); `npm run build` + vitest green.

---

### TASK-11103 — The convention gate (the durability keystone) + the paved-road doc
| Field | Value |
|-------|-------|
| **ID** | TASK-11103 |
| **Status** | complete (gate) — `tools/check_openapi_convention.py` (pure Python, rides the committed `openapi.json`): every operation must carry a non-empty success-response schema + (body verbs) a `requestBody` schema; an untyped `Results.Ok(new{})` lands with empty content → CAUGHT. **130 grandfathered** via `openapi-convention-exempt.txt` (136 total − 6 typed; the 6 proof ops enforced; the manifest only SHRINKS as the retrofit types ops). `--selftest` RED on a new untyped GET + a new POST lacking a DTO; `--check` green on committed (typed:6/grandfathered:130/failing:0). `check_endpoint_contracts.py` byte-untouched (FE-coverage). Complementary to the drift gate + the spec≡runtime xUnit. **CI: Python-only → the `docs` job.** (Doc = Orchestrator, pending TASK-11104.) |
| **Agent** | Backend (the gate) + Orchestrator (the doc) |
| **Components** | `tools/check_endpoint_contracts.py` (extend), `docs/FRONTEND.md` / a convention doc (Orchestrator-only) |
| **KB Refs** | `check_endpoint_contracts.py` (the existing REGISTRY/coverage mechanism) |

**Description**: **The keystone that makes the long-term commitment self-sustaining. [Step-0b BLOCKER/WARNING, both lenses — the FE-call lint is structurally BLIND to a new backend endpoint with no FE caller, which is exactly what this must gate.]** A **NEW backend gate riding the committed `openapi.json`** (NOT `check_endpoint_contracts.py`, which stays the FE-coverage lint): assert **every operation in the spec carries a NON-EMPTY response schema** (an untyped `Results.Ok(new {…})` lands in the spec with empty `200` content → CAUGHT) **and** a `requestBody` schema for body verbs (POST/PUT). The **94 existing untyped endpoints are GRANDFATHERED via a backend-path EXEMPT manifest** (the retrofit drains it in later phases); a NEW/changed endpoint not on the manifest with an empty schema → **CI FAILS**. The **request DTO convention** (Step-0b WARNING — existing request bodies are private nested classes): body verbs declare a named DTO record (`Contracts/`) + `.Accepts<T>()`; the gate's `--selftest` includes a NEW POST lacking a DTO/metadata (→ RED). Document the **endpoint-authoring paved road** (Orchestrator-only doc).

**Validation Criteria**:
- [ ] A NEW backend gate (riding `openapi.json`) fails CI on a new/non-grandfathered endpoint with an empty response or (body-verb) requestBody schema — proven by a `--selftest`; the 94 existing endpoints grandfathered via a backend-path manifest; `check_endpoint_contracts.py` unchanged (FE coverage only); the paved-road doc lands; wired into CI. **The retrofit is subsequent phases.**

---

### TASK-11104 — The two-sided proof + validation + Step-7a + close
| Field | Value |
|-------|-------|
| **ID** | TASK-11104 |
| **Status** | complete — Proof A (a `forrest` field typo → `tsc` TS2551) + Proof B (the drift gate RED-on-perturbation) demonstrated by the build agents; **CI wired** (the drift gate → `build-and-test` [needs .NET]; the convention gate → the `docs` job [Python]; + the Step-7a fixes: the `api-types.ts` freshness gate + the no-`as` lint → `frontend-build`); the paved-road doc = **PAT-012** (+ KB INDEX); Step-7a dual-lens BOTH 0 BLOCKER (2W each, FIXED). Validated: backend build 0 err; drift/convention/freshness gates green (+ `--selftest` RED); FE build 0 err + lint clean; 21 Contracts + 6 matcher + 531 vitest; PAT-010 byte-identity HELD. |
| **Agent** | Test & QA + Orchestrator |

**Description**: The full-pipeline proof + close.
- **Proof A (response agreement — the strong closure):** change a backend record field → regenerate spec+types → the stale FE field-access FAILS `tsc` (via the REAL pipeline, not a hand-edit of `api-types.ts`).
- **Proof B (drift):** change a backend record field WITHOUT committing the regen → `check_openapi_sync.py` FAILS.
- **Honest framing [Step-0b NOTE]:** the RESPONSE side is spec≡runtime (anchored to real bytes — the strong closure); the REQUEST side is spec≡DTO (no "real response" to anchor to; `JsonSerializerDefaults.Web` is case-insensitive on input → a request-casing mismatch breaks only the generated TS, not deserialization). Don't claim symmetric strength.
- Full pyramid + CI green; the PAT-010 contract-lint + tests still pass (complementary).

**Validation Criteria**:
- [ ] Both proofs demonstrated (a CI/test step or a documented reproducible check); `dotnet build` + `npm run build` + full pyramid + CI green; **Step-7a dual-lens (the per-route spec≡runtime gate genuinely closes [not relocates] the gap; the structured-call binding types the templated/query reads; the single-client preserves auth/401/error; the backend convention gate fires on new endpoints; the `--openapi` no-DB gen)** → BLOCKERs absorbed; INDEX/ROADMAP/QUALITY/FRONTEND updated; commit + push + CI-verify. **Phase 0 done → the retrofit rides subsequent phases.**

**Sequencing [Step-0b NOTE — NOT a clean 4-way fan-out]:** TASK-11101 (backend spec, the riskiest) FIRST → then TASK-11102 (codegen, hard-depends on the committed `openapi.json`) + TASK-11103 (the gate, depends on the spec) → TASK-11104. Two internal acceptance CHECKPOINTS so a long sprint stays shippable [Codex sizing WARNING]: **(1)** backend spec gen + the per-route spec≡runtime gate + the drift gate (independently valuable); **(2)** the typed `apiClient` proof + the convention gate. If checkpoint 1 runs long, checkpoint 2 splits to a follow-up sprint (the retrofit stays deferred either way).

---

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules / payroll | N/A | Response-typing + build tooling + CI gates; no agreement/payroll/rule/event logic touched. |

## External Review (Step 7a)
Dual-lens on the full uncommitted S111 diff (the central question: is the loose-types + coercion closure SOUND + the right foundation?). Artifacts: `.claude/reviews/SPRINT-111-step7a-{codex,reviewer}.md`. **BOTH lenses 0 BLOCKER; the foundation is sound + non-bypassable** (the no-DB gen, the byte-identical `MapAll` surface, the array-ness self-test RED, the grandfather-only-shrinks manifest, the overload ordering — all verified). The closure catches the historical bug class (renamed/removed field, envelope shape) for the wired reads. **2 WARNING each → the closure-critical ones FIXED in Phase 0:**
- **Reviewer:** the `openapi.json`→`api-types.ts` step was UNGATED (the bug class one level up) → **FIXED** (a freshness gate in `frontend-build`); the no-`as` lint wasn't in CI → **FIXED** (lint step added).
- **Codex:** a stale convention-manifest entry only WARNED (a false-green for retrofitted ops) → **FIXED** (stale → exit 1); `AssertFieldsInSpec` is name-only (a same-name TYPE change is a residual) → DEFERRED + documented.
- **Both — `required`-strictness is a defensible DEFERRAL, not a blocker** (Swashbuckle doesn't populate `required` for record positional members; the enum-as-`string` widening means coercion survives even with it). → the later strict-types phase.

[[review-lens-complementarity]]: the external lens caught the stale-manifest false-green; the internal lens caught the ungated spec→TS hand-off — disjoint, both closure-critical.

## Test Summary
**Backend build 0 err; FE build 0 err + lint clean.** The typed-contract surface: 21 Contracts tests (incl. the per-route spec≡runtime `OpenApiSpecRuntimeTests` 5 routes + `Gate_IsRed_OnInjectedArraynessLie`) + 6 `SpecRuntimeMatcher` units; **531 FE vitest**; PAT-010 byte-identity HELD (the runtime JSON unchanged — only `.Produces`/`.Accepts` metadata + named records added; the roster anonymous→`RosterResponse` preserved member order + nullability). The 4 gates green locally: spec≡runtime (xUnit), drift (`check_openapi_sync.py` — 101 paths/74 schemas in sync), freshness (`gen:api` idempotent), convention (`check_openapi_convention.py` — 6 typed/130 grandfathered; `--selftest` RED) + the no-`as` lint. **Backend regression unchanged from S110 CI-green (byte-identity) → CI re-verifies the full pyramid + the new gates on push.**

## Sprint Retrospective
- **Phase 0 of the typed-contract program is built: the pipeline + 4 gates + the proof, on the 5 reads + 1 mutation.** The "fetchEnheder" bug class (S97→S100) is structurally closed for the wired surface, AND the **convention gate forces every NEW endpoint typed** (the durability keystone) — so the untyped surface can't grow while the retrofit drains it.
- **The DEEPEST Step-0b of any sprint — both lenses converged on 4 structural mechanism BLOCKERs pre-code**, each of which would have built the foundation on a wrong assumption: the spec≡runtime gate had to be PER-ROUTE (not per-shape — `.Produces` can lie about array-ness); `swagger tofile` is NOT no-DB (the seeders/hosted-services need EventStore → the guarded `--openapi` entrypoint); a literal-key binding can't type templated/query paths (→ the structured call shape; + `organizations/tree` was FE-orphaned post-S109); the FE-call lint is blind to a new backend endpoint (→ the spec-based convention gate). For the FIRST sprint of a long program, getting the mechanisms right pre-code was the whole game.
- **Step-7a — both lenses 0 BLOCKER, and the complementarity was decisive again:** the external lens caught a convention-gate false-green (a stale manifest entry); the internal lens caught the ungated spec→TS hand-off (the bug class one level up). Both FIXED in Phase 0 rather than deferred — because for a sprint whose purpose is closing that class, shipping the second drift surface ungated would have been self-defeating.
- **Honest framing held throughout:** the response side is the strong closure (spec≡runtime, anchored to real bytes); the request side is spec≡DTO (weaker); the same-name-type-change residual + `required`-strictness are documented deferrals (PAT-012), not hidden.
- Durable: SPRINT-111.md + PAT-012 + the Step-7a artifacts + the typed-api-contract program memory. **NEXT — the RETROFIT phases** (admin mutations → approval/reporting → payroll/settlement → config → …, lazy or dedicated) draining the 130 grandfathered ops; a **strict-types phase** (emit `required`+enums → drop the coercion scaffolding + close the same-name-type residual); the per-route spec≡runtime assertion per newly-typed endpoint. The convention gate keeps the surface from growing untyped meanwhile.
