# Sprint 101 — Endpoint contract tests (close the recurring "fetchEnheder" bug class; Pass 1 + the convention)

| Field | Value |
|-------|-------|
| **Sprint** | 101 |
| **Status** | complete |
| **Start Date** | 2026-06-24 |
| **End Date** | 2026-06-24 |
| **Orchestrator Approved** | yes — 2026-06-24 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 errors |
| **Test Verified** | yes (local): 850 unit + 1133 regression (+`Pass1EndpointContractTests` 3) + 6 smoke + 29 demoseed + 566 fe; the lint + self-test green; CI-pending (backfilled at close-polish) |

## Sprint Goal
Close the recurring "fetchEnheder" bug class (S97 → S99 → S100 — a FE list-hook test mocks the right response envelope [vitest green] while the real endpoint serves a different shape → prod breaks) with a **durable paved-road bundle**, scoped to **Pass 1** (the exact 3-bug surface). Owner-resolved: the FULL bundle (records + tests + lint + convention); Pass 1 only. Refinement: `.claude/refinements/REFINEMENT-endpoint-contract-tests.md` (Step-4 dual-lens — reshaped from a centralized suite to records + co-located tests + a CI lint). Primarily P8 (CI/CD) + quality.

## Scope (in / out)

**Pass-1 endpoints (the 3-bug surface, cheap seed):**
- `GET /api/admin/enheder` → `{ enheder: EnhedListItem[] }` (`useEnheder.fetchEnheder`) — the S97/S99/S100 bug site.
- `GET /api/admin/organizations/tree` → `{ tree: OrgTreeMaoNode[] }` nested (`useOrganizationTree.fetchTree`).
- `GET /api/admin/organizations` → `OrgListItem[]` (a BARE ARRAY; `useAdmin` `apiClient.get<Organization[]>`).

**IN — the full bundle:**
- **(a) Named response records** — convert these 3 endpoints' anonymous `Results.Ok(new { … })` to named `record`s in a new `src/Backend/StatsTid.Backend.Api/Contracts/` namespace: `EnhedListItem`, `EnhedListResponse{ IReadOnlyList<EnhedListItem> Enheder }`; `OrgTreeEnhedNode`/`OrgTreeOrganisationNode`/`OrgTreeMaoNode` + `OrgTreeResponse{ … Tree }`; `OrgListItem`. **Behaviour-identical** (the JSON wire shape is unchanged — same field names/casing via the existing serializer options) — a diff-reviewability + B-prerequisite change, NOT a contract change. The existing tests pin behaviour.
- **(b) `ContractAssert` helper + co-located contract tests** — `tests/StatsTid.Tests.Regression/Contracts/ContractAssert.cs`: `IsEnvelope(json, "enheder")` (envelope, NOT a bare array), `IsArray(json)`, `HasFields(obj, …)` (required-presence), `FieldKind(obj, name, JsonValueKind…)` (nullability/kind). Co-located contract tests (cheap seed → a `Contracts/Pass1EndpointContractTests.cs`, or beside the enhed/org suites) asserting envelope + required fields + nullability for each of the 3. **RED-on-old**: deleting a field fails; an envelope→bare-array (or array→envelope) change fails (the exact S97/S100 bugs). Each contract seeds ≥1 representative item per asserted nested path (incl. a null + a non-null `parentEnhedId` row). The S100 `GET /enheder` contract test folds in here.
- **(c) CI coverage-lint** — `tools/check_endpoint_contracts.py`: a REGISTRY (the in-scope endpoints + their contract-test method names) + an EXEMPT-list (the other FE-consumed admin GETs, each with a one-word reason: `pass-2` / `scalar` / `single-object`). The lint (i) enumerates FE `apiClient.get`/`apiFetchWithEtag` calls to `/api/admin/*` across `frontend/src/hooks/`, normalizes the path (strip query/template params), and FAILS if a path is in NEITHER the registry NOR the exempt-list (a NEW uncovered admin GET forces a conscious decision); (ii) FAILS if a registered endpoint's contract-test method is absent from the test files. Wired into the CI `docs` job (or a new step). This is the GATE the doc convention lacked (the bug recurred 3× with the lesson already written down).
- **(d) KB `PAT-010` entry** — "endpoint response contract: named record + a co-located contract test + the lint registration; FE hook tests mock against the documented contract." + INDEX. Pass 2 (the approval/roster family) recorded as a follow-up; the OpenAPI→TS typed client (fork B) recorded as the structural future (now de-risked because Pass 1 names the records).

**OUT:** Pass 2 (the approval/roster/team-overview/allocation family — the richest drift surface but heavy seed; co-locate beside their existing period/tree-seeding suites — a recorded follow-up); fork B (the typed client — needs OpenAPI on the now-named records); converting ALL ~50 endpoints' responses to records (only the 3 Pass-1 ones); asserting EXACT shape (additive backend fields must NOT break the contract — envelope + required-presence + nullability only).

## Honest framing (Step-4 WARNING 1 — do NOT oversell)
This bundle pins backend-shape STABILITY (a regression guard against re-dropping a field) + diff-reviewability (the records) + coverage-gating (the lint). It does NOT structurally close the FE↔backend AGREEMENT gap (a hand-declared field-set is a reviewed copy of the FE's expectation, not a shared type) — only fork B (a shared typed client) does that. The bundle is the paved road TO B, not a substitute. Documented in PAT-010.

## Plan Review (Step 0b)
| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (a new CI gate [the lint] + cross-cutting response-record refactor + the convention) |
| **External Codex** | done — "Sound to plan"; 2 WARNING / 3 NOTE |
| **Internal Reviewer** | done — "Sound to plan"; the byte-identity guarantee EMPIRICALLY VERIFIED |

### Step-0b Findings + Resolutions (both lenses confirmed byte-identity is provably satisfiable + the lint enumeration is tractable; no hard BLOCKER)
- **The #1 risk — byte-identical wire JSON — is GUARANTEED by the .NET 8 minimal-API `JsonSerializerDefaults.Web` default** (camelCase policy; the Reviewer EMPIRICALLY verified it via the existing `ComplianceCheckResult` round-trip — a PascalCase record → `Results.Ok` → camelCase wire that `useCompliance.ts:18` consumes — and confirmed no `ConfigureHttpJsonOptions`/`AddJsonOptions` override in `Program.cs`, and no `DefaultIgnoreCondition=WhenWritingNull` → `parentEnhedId: null` is still emitted). **RESOLUTION (TASK-10101/10102):** the records are plain PascalCase members, **NO `[JsonPropertyName]`** (rely on the verified default; per-member attrs would MASK a future policy regression); the contract tests assert the **camelCase keys LITERALLY** (`GetProperty("enhedId")`, `parentEnhedId` as `JsonValueKind.Null` at root / `String` at child) — **this is the load-bearing guard** that catches any future global `AddJsonOptions` regression RED. Byte-identity proof = the existing enhed/org tests + S100 + the new contract tests stay green.
- **NOTE (both) — the org-tree contract test must assert the NESTING + `level`, not just top-level fields** (the S100 bug was a NESTED drop). **RESOLUTION (TASK-10102):** seed a ≥2-deep enhed chain under a visible Organisation; assert a representative DEEP node carries `level` (root=1, child=2) + `children` + `parentEnhedId` kind (null root / string child). Reuse/lift the S100 helpers (`FindEnhedNode`, the MAO→organisations→enheder walk — `S100EnhedHierarchyTests.cs:373-388,576-587`) into `ContractAssert` rather than re-author.
- **NOTE (Reviewer) — the record refactor must NOT touch the assembly logic.** `BuildEnhedForest` (`AdminEndpoints.cs:3257-3302`) returns `object`/anonymous nodes recursively; `:671` reads `c.employeeCount` dynamically. **RESOLUTION (TASK-10101):** convert the LEAF/element records first (`EnhedListItem`, `OrgListItem`, the enhed-tree node) + keep the forest recursion + the `:671` rollup untouched (the named node type still exposes `EmployeeCount`); the bare-array `GET /organizations` → the record is the ELEMENT (via `MapOrgResponse` `:3233`), the response stays a bare array → the contract asserts `IsArray`, NOT `IsEnvelope`.
- **WARNING (both) — the CI lint scope.** **RESOLUTION (TASK-10103):** scan `frontend/src/**/*.ts(x)` EXCLUDING `__tests__/` + `*.test.ts` (there's an admin GET outside hooks — `/api/admin/audit` in `AuditLogView.tsx:77` — so scan pages too, not just hooks); GET-only (POST/PUT/DELETE are write-paths, out of scope); normalize the path (strip `?<query>`; replace `${…}` template segments with `{}`). The admin-GET surface is **~9** (3 Pass-1 + ~6 exempt: `entitlement-configs`, `position-overrides`, `wage-type-mappings`, `users`, `users/{}/roles`, `organizations/{}/users`, `audit`) — NOT ~19 (that counted non-GET verbs). The registry/exempt shape mirrors `check_docs.py`'s `SPRINT_EXCEPTIONS`; wire into the `docs-consistency` CI job (`ci.yml:316-332`, Python 3.12); a self-test fixture (an unregistered admin GET → non-zero exit).
- **NOTE — fix the refinement's S100 citation** (it's in `tests/.../Security/S100EnhedHierarchyTests.cs`, the list contract at `:406-419` + the tree-nesting at `:373-388` — not the "Skema suite :398-420").

## Architectural Constraints
- [ ] P1 — Architectural integrity (response records in a Contracts namespace; the lint mirrors `check_docs.py`)
- [ ] P8 — CI/CD (the lint is a real gate; the contract tests in Regression; behaviour-identical records [the wire JSON unchanged])
- [ ] Quality — RED-on-old proofs (delete-field + envelope-drift both fail); the honest-framing documented

## Task Log (planned)
- **TASK-10101 — Named records** (the 3 endpoints → named records in `Contracts/`; verify the wire JSON is byte-identical — same field names/casing; the existing enhed/org tests stay green).
- **TASK-10102 — `ContractAssert` + the contract tests** (the shared helper + the 3 co-located contract tests; RED-on-old delete-field + envelope-drift; the S100 test folded in).
- **TASK-10103 — the CI lint** (`tools/check_endpoint_contracts.py` + the registry/exempt-list + the FE-hook enumeration; wire into CI; a self-test that an unregistered admin GET fails).
- **TASK-10104 — PAT-010 + docs** (the KB entry + INDEX; Pass 2 + fork B recorded; INDEX/QUALITY/ROADMAP).

## Risks
- **Over-selling (Step-4 WARNING 1)** — the honest-framing section + PAT-010 state it pins backend↔itself, not FE↔backend; B is the structural fix.
- **The record conversion must be byte-identical on the wire** — same JSON field names/casing/null handling; pin with the existing enhed/org tests + the new contract tests (a mismatch fails them).
- **The lint's path-normalization** — templated paths (`?organisationId=`, `/${id}/`) must normalize correctly; the self-test + the exempt-list bootstrap cover it.
- **The exempt-list bootstrap** (~19 entries) — a one-time classification of the existing admin GETs (valuable as an audit); each gets a reason.
- **review-lens-complementarity**: Step-0b + Step-7a dual-lens (the lint design + the record byte-identity + the RED-on-old proofs are the targets).

## Execution Outcome
The recurring "fetchEnheder" bug class (S97 → S99 → S100) is now gated. Pass 1 (the 3-bug surface — `GET /api/admin/enheder`, `/organizations/tree`, `/organizations`) ships the full bundle: **named response records** (byte-identical wire JSON — proven by the existing enhed/org suites staying green), a **`ContractAssert` helper + co-located contract tests** (envelope + required-field presence + literal camelCase keys + deep nesting), and a **CI coverage-lint** (`tools/check_endpoint_contracts.py`, wired into the `docs-consistency` job) that hard-fails if a FE-consumed admin GET has no contract test. PAT-010 documents the convention + the honest framing. Build 0/0; the lint passes (19 admin GETs: 3 registered + 16 exempt) + the self-test exits 1 (gate live). NO schema/authority/event/rule path touched — a pure response-shape + test + tooling change.

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex + internal Reviewer), adversarial on byte-identity + guard-strength |
| **Sprint-start commit** | `25da26c` |
| **Review Cycles** | 1 review + 1 fix pass |
| **Findings** | 0 BLOCKER; 1 WARNING + 2 NOTE (all fixed) |

### Findings
- **Byte-identity CONFIRMED CLEAN by both lenses** (field-by-field: the records' member names map 1:1 to the old anonymous camelCase keys; `BuildEnhedForest` recursion + the `employeeCount` rollup untouched; the bare array stays bare; null preserved; no JSON-policy override). The contract tests are REAL (literal camelCase keys → a future PascalCase regression goes RED; envelope-vs-bare-array + a deep nested enhed node → the S97/S100 bugs caught; `HasFields` tolerates additive fields).
- **WARNING (fixed) — the lint enumerator is foolable** (inline-URL-only; a future helper/variable-built admin GET evades — no live gap). Fixed: documented the limitation honestly in the script header + PAT-010 + a report-only soft scan surfacing un-enumerable `/api/admin/` references.
- **NOTE (fixed) — the tree test didn't pin the MAO/Organisation node fields** (only the enhed nodes). Fixed: added `HasFields` on a representative MAO + Organisation node.
- **NOTE (fixed) — a dead EXEMPT entry + the liveness check robustness.** Fixed: removed the dead entry (+ a soft-warn for future ones); tightened the liveness check to the method-declaration form.
- Artifacts: `.claude/reviews/SPRINT-101-step7a-{codex,reviewer}.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit | 850 | all passing |
| Regression | 1133 | +`Pass1EndpointContractTests` 3 (the enhed envelope+fields+nullability, the org-tree DEEP nesting, the organizations bare-array — RED-on-old on a field-drop or an envelope↔bare-array flip); clean re-run 0 sheds (the 1st aborted at 162 on a concurrent test-DLL rebuild — re-run clean) |
| Smoke | 6 | all passing |
| DemoSeed | 29 | all passing |
| Frontend (vitest) | 566 | unchanged (no FE change — backend records are byte-identical) |
| CI lint | — | `check_endpoint_contracts.py` (19 admin GETs covered) + `--selftest` (exit 1, gate live) |
| **Total** | **2584** | CI confirmation pending |

## Sprint Retrospective
**What went well**: the Step-4 dual-lens reshaped this from "a centralized contract suite" into a durable bundle (named records + co-located tests + a CI lint) — and the records turned out to be the higher-leverage half (diff-reviewability + the OpenAPI/typed-client prerequisite). The Reviewer EMPIRICALLY verified the byte-identity guarantee (the `JsonSerializerDefaults.Web` camelCase default, proven via the `ComplianceCheckResult` round-trip) BEFORE the code, so the #1 risk was retired up front. The honest framing (pins backend↔itself, not FE↔backend — only a shared type closes that) is preserved end-to-end, so the work doesn't over-claim.
**What to improve**: the lint's inline-URL-only enumeration is a known blind spot (helper-built URLs evade) — surfaced honestly + soft-warned, but the durable fix is fork B (a shared typed client, now de-risked by the records). And — for the SECOND sprint running — the first regression aborted because a sub-agent rebuilt/ran the test DLL concurrently with the central run (S100 at 174, S101 at 162); the standing rule (never run an isolated `dotnet test`/build during the central regression) needs to be enforced, not just remembered.
**Knowledge produced**: PAT-010 (the convention + the honest framing + the blind spot). Recorded follow-ups: Pass 2 (the approval/roster family, co-located with their seed-owning suites); fork B (the OpenAPI→TS typed client, the structural FE↔backend fix, now de-risked).

## Status: COMPLETE (close commit; push + CI-verify)
