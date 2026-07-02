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
1. **spec≡runtime** (`OpenApiSpecRuntimeTests` + `SpecRuntimeMatcher`, xUnit, build-and-test) — the committed schema matches the REAL serialized response PER ROUTE: root/array-ness, property presence, camelCase, declared-success-STATUS fidelity (S112), explicit 204, **required-fidelity** (a schema-`required` member absent at runtime = RED; present-but-null SATISFIES required — S113) and **enum-fidelity** (a non-null value outside a declared `enum` set = RED; **`required` and `nullable` are ORTHOGONAL claims — the enum set never arbitrates null**; null is admissible iff `nullable` — S113). `.Produces<T>` is a *convention* that can lie (esp. array-ness — author a bare array as `.Produces<IEnumerable<T>>`); THIS is the closer.
2. **spec drift** (`tools/check_openapi_sync.py`, build-and-test — needs .NET) — the committed `openapi.json` == what `--openapi` regenerates today.
3. **types freshness** (`npm run gen:api && git diff --exit-code`, frontend-build — needs Node) — the committed `api-types.ts` == what the spec regenerates. (Closes the spec→TS hand-off; without it the bug class reappears one level up.)
4. **convention** (`tools/check_openapi_convention.py`, docs job — pure Python) — every NEW/non-grandfathered operation in `openapi.json` carries a non-empty response (+ requestBody for body verbs) schema, **or declares `204` as its ONLY success response with no content (`.Produces(204)` — the typed no-body statement; S112 owner-ratified amendment)**; an untyped `Results.Ok(new {…})` lands with empty content → caught; a mixed inferred-empty-200 + 204 op or a no-success-code op still fails (the "only" formulation is load-bearing — `--selftest` proves both directions). The grandfather manifest (`tools/openapi-convention-exempt.txt`) only SHRINKS (a stale entry FAILS). **This is the durability keystone** — it forces every new endpoint typed while the retrofit drains the existing.

Plus the no-`as` lint (`npm run lint`, frontend-build) — the typed-read hooks carry no `as` / no hand-written `get<T>` type arg.

## The paved road (authoring a new admin endpoint — S112-amended)
1. Define a named RESPONSE record in `Contracts/`. **Sibling handlers emitting the identical shape share ONE record** (org create/update/move → one `OrganizationResponse`) — it makes "same wire contract" structural instead of coincidental and halves the strict-types retrofit surface.
2. Return it (`Results.Ok/Created(theRecord)`) + declare `.Produces<T>(code)` with the CORRECT collection-ness (`IEnumerable<T>` for a bare array) AND the code the handler REALLY returns. A body-less mutation: `Results.NoContent()` + `.Produces(204)` — no record. **An explicit `.Produces` REPLACES Swashbuckle's inferred empty 200 on an `IResult` handler** (S112-verified), so declared status codes become honest as a side effect of typing. **Keep ONE declared 2xx per endpoint** — the spec≡runtime gate rejects multiple-2xx ops as ambiguous.
3. **Request side: let Swashbuckle INFERENCE speak** — the inferred requestBody schema is generated FROM the bound parameter type, truthful by construction. `.Accepts<T>` OVERRIDES inference and can lie exactly like `.Produces`; if it is ever used, it MUST match the bound parameter type. **(S113 correction of the S112 framing: request-body `required` IS emitted where the C# class declares `required` members — and that subset is binder-ENFORCED [absence → 400]; the S112 sentence "generated request-body types are all-optional" was FALSE — S112's own createUser finding rode exactly this mechanism. The honest boundary: only C#-`required` members are strict; non-`required` NRT/value members stay optional in the generated type because the binder tolerates their absence — the `ResponseStrictTypesFilter` deliberately does NOT touch request schemas, which would over-claim.)**
4. Regenerate: `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi` → commit `docs/api/openapi.json`.
5. FE-consumed? `npm run gen:api` → commit `api-types.ts`; call via the typed forms — `apiClient.get/post/put/delete(pathKey, {params, query, body})` or `apiFetchWithEtag(pathKey, {method, params, ifMatch, body})` for If-Match routes (no `as`, no type args). **Verify by CALL FORM, not compilation: a raw-body/explicit-`T` string-path call SILENTLY falls through to the untyped fallback — tsc will NOT flag a missed switch** (S112-verified empirically).
6. Add a per-route spec≡runtime assertion (the S112 per-family fixture pattern; a mutation assertion gets its OWN dedicated seeded row — xUnit intra-class order is not guaranteed).
7. The 4 gates + the lint enforce all of the above in CI. (Do NOT add the endpoint to the grandfather manifest — it only shrinks.)

## Retrofit recipe (Pass ≥1, per drained op)
Response record (+`.Produces`, or `.Produces(204)` for body-less) as an EXACT shape-copy — deliver a field-mapping table (anonymous↔record member) for byte-identity review; NO request-class changes; regen spec+types; switch the FE call-site to the typed form; DELETE the manifest line; add the per-route assertion. The FE switch is audited by call form per route (see step 5). Typed-client overload extensions follow the S112 pattern: typed overloads FIRST constrained to literal path-key unions; explicit-`T`/template-URL callers fall through by constraint/arity; runtime structured-vs-legacy discrimination is key-subset-based and must behavior-coincide on every ambiguous overlap; success-status-aware derivation (200/201 JSON, declared-204 → `undefined`, undeclared-content 200s EXCLUDED); proven by synthetic `paths`-shaped fixtures + `@ts-expect-error` tripwires.

## Vindication (S112 + S113, recorded evidence for the program's premise)
The Pass-1 FE switch surfaced the predicted class in NON-test code: **a hand-written FE interface is itself a mock.** Two live prod bugs fell out at compile time — `RoleAssignment` claimed `grantedBy`/`grantedAt` (backend: `assignedBy`/`assignedAt`) → two admin columns rendered blank in prod; `updateUser` claimed a phantom `username` response field → every save silently dropped it from drawer state. Plus three contract lies (phantom fields both directions, an out-of-contract request field, spec-required fields marked optional). **S113 extended the lesson to the NULLABILITY axis: a field-NAME-only drift guard cannot see nullability — `RoleAssignment.assignedBy` claimed non-null `string` while the grant path serves `string | null`; the lie survived S111 AND S112 and surfaced only under strict direct assignment.** Convention from the fix: undeclared error payloads (e.g. 412 bodies) narrow via a runtime type GUARD on the no-`as` surface, never a cast.

## Known residuals
**CLOSED by the S113 strict-types phase:** ~~required-strictness~~ and ~~same-name-TYPE-change~~ — the `ResponseStrictTypesFilter` emits `required` = all members for every schema in the **response-reachable closure** (transitive from 2xx `.Produces` refs; the truthfulness basis: the null-emitting serializer always writes every record member — empirically confirmed against all 26 real per-route responses), so the generated response types are directly strict and the coercion scaffolding (`apiNarrow.ts` / `coerceApiResponse` / `AssertFieldsInSpec`) is DELETED — a type flip now fails `tsc` directly. Enum-widening resolved via **`[property: AllowedValues(...)]` on closed-set response discriminators** (unit type / orgType / periodStatus / vikar reason / scopeType — each set citing its authority: a DB CHECK or a total projection; OPEN config-keyed sets like `employmentCategory` must NOT be declared) → the filter emits `enum: [...]` → generated literal unions + the enum-fidelity gate. Exclusions that keep `required` truthful: `[JsonIgnore(WhenWritingNull/Default)]` members (the `VacationSettlementSnapshot` future, test-pinned) and the nullable-`$ref` case below. Overlap policy: a schema reachable from both request and response gets response-truth `required` (over-strict toward the request side, fails SAFE at compile time).
- **NEW (S113, bounded): the nullable-`$ref` exception** — OpenAPI 3.0 cannot express `nullable` on a bare `$ref`, so a CLR-nullable COMPLEX response member marked `required` would claim never-null (the dangerous-direction lie). The filter excludes such members from `required`; the generated type stays optional; the FE view overrides that single property (`Omit<Spec,'field'> & { field?: T | null }` — the `RosterRow`/`outgoingVikar` precedent, everything else spec-derived). Today exactly ONE member. Truthful fix (an `allOf`-wrapper emission + a coordinated matcher change) is a candidate follow-up.
- **The spec≡runtime gate covers only the typed routes** — a future typed endpoint that lies passes convention+drift until a per-route assertion is added. Fold into the retrofit. (S112: status fidelity + declared-204; S113: required + enum fidelity; all 26 typed ops carry per-route assertions.)
- **Request-side declaration-vs-binder boundary** (documented, accepted): spec `required` on request bodies = the C#-`required` subset only (binder-enforced); other members stay optional by design.

## Scope status
Phase 0 (S111) typed the proof surface (the 5 registry reads + 1 mutation) end-to-end + installed the pipeline + the 4 gates. **Pass 1 (S112) drained the 20-op merged-admin mutation surface — 26 typed / 110 grandfathered** — and added the typed body-verb + If-Match client overloads, the declared-204 gate policy, and status-fidelity per-route assertions. **The strict-types phase (S113) made the generated types DIRECTLY strict** (the `ResponseStrictTypesFilter` + `[AllowedValues]` discriminator enums + required/enum-fidelity gates) **and DELETED the FE coercion scaffolding** — the paved road's step 5 typed calls now consume strict types with no trust-boundary cast. The remaining **110 grandfathered operations** ride subsequent passes (Pass 2 candidate: reporting-lines 13 + employee field-endpoints 8 + remaining admin reads 3; then approval → payroll/settlement → config → employee-facing) — each newly-typed op now lands strict automatically. See [[typed-api-contract-program]].
