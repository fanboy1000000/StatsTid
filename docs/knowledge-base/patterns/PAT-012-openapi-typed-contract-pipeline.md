# PAT-012 — OpenAPI typed-contract pipeline (fork B)

| Field | Value |
|-------|-------|
| **Type** | PAT (pattern) |
| **Status** | active |
| **Introduced** | S111 (Phase 0 of the long-term typed-contract commitment) |
| **Relates to** | PAT-010 (endpoint-response-contract — the wire-byte anchor), PAT-011 (design-sync-drift-gate — the generate-and-gate sibling) |

## Problem
The FE↔backend response **shape** was hand-copied on both sides (`apiClient.get<T>` casts `res.json() as T` with a hand-written `T`; the backend hand-shapes the JSON). A FE list-hook reading a shape the backend doesn't serve passed its own mock test (green) but broke in prod — the "fetchEnheder" class, shipped 3× (S97/S99/S100). PAT-010 added a regression guard (the backend shape stays stable); it did NOT guarantee FE↔backend AGREEMENT.

## Pattern — the pipeline + 4 gates
**One generated source of truth, gated at every hand-off so drift is a CI failure, not a prod incident.**

```
backend Contracts/ records + .Produces<T>/.Accepts<T>
        │  (--openapi no-DB entrypoint; Swashbuckle, camelCase)
        ▼
   docs/api/openapi.json   ── committed
        │  (npm run gen:api; openapi-typescript)
        ▼
 frontend/src/lib/api-types.ts  ── committed
        │  (the typed apiClient: get(pathKey, {params,query}))
        ▼
        FE hooks (tsc)
```

**The 4 gates (each closes one hand-off; complementary, not redundant):**
1. **spec≡runtime** (`OpenApiSpecRuntimeTests` + `SpecRuntimeMatcher`, xUnit, build-and-test) — the committed schema matches the REAL serialized response PER ROUTE (root/array-ness, required, nullable, camelCase). `.Produces<T>` is a *convention* that can lie (esp. array-ness — author a bare array as `.Produces<IEnumerable<T>>`); THIS is the closer.
2. **spec drift** (`tools/check_openapi_sync.py`, build-and-test — needs .NET) — the committed `openapi.json` == what `--openapi` regenerates today.
3. **types freshness** (`npm run gen:api && git diff --exit-code`, frontend-build — needs Node) — the committed `api-types.ts` == what the spec regenerates. (Closes the spec→TS hand-off; without it the bug class reappears one level up.)
4. **convention** (`tools/check_openapi_convention.py`, docs job — pure Python) — every NEW/non-grandfathered operation in `openapi.json` carries a non-empty response (+ requestBody for body verbs) schema; an untyped `Results.Ok(new {…})` lands with empty content → caught. The grandfather manifest (`tools/openapi-convention-exempt.txt`) only SHRINKS (a stale entry FAILS). **This is the durability keystone** — it forces every new endpoint typed while the retrofit drains the existing.

Plus the no-`as` lint (`npm run lint`, frontend-build) — the typed-read hooks carry no `as` / no hand-written `get<T>` type arg.

## The paved road (authoring a new admin endpoint)
1. Define a named record in `Contracts/` (a response record; a request DTO for a body verb).
2. Return it (`Results.Ok(theRecord)`) + declare `.Produces<T>(code)` with the CORRECT collection-ness (`IEnumerable<T>` for a bare array) + `.Accepts<TRequest>(ct)` for a body.
3. Regenerate: `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi` → commit `docs/api/openapi.json`.
4. FE-consumed? `npm run gen:api` → commit `api-types.ts`; call via the typed `apiClient.get(pathKey, {params,query})` (no `as`, no `get<T>`).
5. The 4 gates + the lint enforce all of the above in CI. (Do NOT add the endpoint to the grandfather manifest — it only shrinks.)

## Known residuals (deferred — a later "strict-types" phase)
- **Same-name TYPE change** isn't caught by `AssertFieldsInSpec` (it compares field NAMES; `coerceApiResponse`'s `as` bridges the spec's intentional optionality + enum-as-`string` widening). The historical bug class is name/shape (caught); a same-name type flip is a residual. Closed by emitting OpenAPI `required` (Swashbuckle doesn't populate it for record positional members) + enums so the generated types are directly-strict and the coercion scaffolding disappears.
- **The spec≡runtime gate covers only the typed routes** — a future typed endpoint that lies about array-ness passes convention+drift until a per-route assertion is added. Fold into the retrofit.

## Scope status
Phase 0 (S111) typed the proof surface (the 5 registry reads + 1 mutation) end-to-end + installed the pipeline + the 4 gates. The bulk **retrofit** of the 130 grandfathered operations rides subsequent phases (lazy / risk-prioritized). See [[typed-api-contract-program]].
