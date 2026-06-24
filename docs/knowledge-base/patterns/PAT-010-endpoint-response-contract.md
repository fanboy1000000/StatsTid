# PAT-010 — Endpoint response contract: named record + co-located contract test + a CI coverage-lint

| Field | Value |
|-------|-------|
| **Status** | approved |
| **Sprint** | S101 (closing the recurring "fetchEnheder" bug class: S97 → S99 → S100) |
| **Domains** | Backend, Test, CI/CD, Frontend |
| **Tags** | response-shape, contract-test, fetchenheder, wire-json, named-records, ci-lint, false-green, drift-proof |

## The recurring bug this prevents
A FE list-hook test mocks the response envelope it EXPECTS (so vitest is green) while the real backend endpoint serves a DIFFERENT shape — a dropped/renamed field, or an envelope (`{enheder:[…]}`) vs a bare array (`[…]`). The mock matches the FE's expectation, not the backend's reality, so nothing fails until production. It recurred THREE times: S97 (the bare-array-vs-`{enheder:[…]}` `fetchEnheder`), S99 (resurfaced), S100 (`GET /enheder` dropped `parentEnhedId`/`level`). A FE-side mock can never catch it — the only authoritative guard exercises the REAL endpoint's serialized JSON.

## The convention (3 complementary guards)
For every FE-consumed `/api/admin/*` list/structured GET endpoint:
1. **A named response record** (in `StatsTid.Backend.Api.Contracts`) instead of an anonymous `Results.Ok(new { … })`. A dropped field becomes a one-line deletion a REVIEWER SEES IN THE DIFF (the shape no longer hides deep in a 1300-line endpoint file), AND it is the prerequisite that makes a future OpenAPI→TS typed client cheap. **Plain PascalCase members, NO `[JsonPropertyName]`** — the .NET 8 minimal-API `JsonSerializerDefaults.Web` default applies camelCase (verified: no `ConfigureHttpJsonOptions`/`AddJsonOptions` override exists; null is still emitted — no `WhenWritingNull`). The record is byte-identical on the wire; per-member attributes would MASK a future policy regression.
2. **A co-located contract test** (beside the feature's existing seed-owning suite; a shared `tests/.../Contracts/ContractAssert.cs`: `IsEnvelope`/`IsArray`/`HasFields`/`FieldKind`). Assert the envelope shape (envelope-vs-bare-array — the S97/S99 bug), required-field PRESENCE (NOT exact shape — additive backend fields must not break it), nullability/kind, and the **camelCase keys LITERALLY** (`GetProperty("enhedId")` — the load-bearing guard that catches any future global `AddJsonOptions` PascalCase regression RED). Seed ≥1 representative item per asserted nested path (a null + a non-null row); assert DEEP nested nodes for tree responses (the S100 bug was a nested drop).
3. **A CI coverage-lint** (`tools/check_endpoint_contracts.py`, in the `docs-consistency` job): a REGISTRY (endpoint → contract-test method) + an EXEMPT-list (other admin GETs → a reason). It enumerates FE `apiClient.get`/`apiFetchWithEtag` GET calls to `/api/admin/*` and HARD-FAILS if a path is in NEITHER (a new uncovered admin GET forces a conscious decision) or a registered method is absent. **This is the teeth the doc convention lacked** — the bug recurred 3× WITH the lesson already written down in QUALITY.md; a memory aid is not a gate.

## Honest framing (load-bearing — do NOT over-sell)
This pins backend-shape STABILITY (a regression guard against re-dropping a field) + diff-reviewability (the records) + coverage-gating (the lint). It does NOT structurally close the FE↔backend AGREEMENT gap — the contract test's field-set is a REVIEWED hand-copy of the FE's expectation, not a shared type. If the FE later reads a field the backend never serves, the contract test (still asserting the old set) stays green — the same false-green, relocated. **The only structural fix is a shared type both sides consume** (an OpenAPI→TS typed client, now de-risked because the records are named). This convention is the paved road TO that, not a substitute.

## Known blind spot (the lint)
The lint enumerates only STATICALLY-INLINE URL arguments. An admin GET whose URL is built via a `const url = …`/path-helper (e.g. `apiFetchWithEtag(ELIGIBILITY_PATH(id))`) is NOT enumerated → it relies on this convention, not the gate. A report-only soft scan surfaces un-enumerable `/api/admin/` references as `[warn]` for a human; prefer inline URLs in FE hooks so the gate covers them.

## Scope state
- **Pass 1 (S101):** `GET /api/admin/enheder`, `/organizations/tree`, `/organizations` — the exact 3-bug surface.
- **Pass 2 (follow-up):** the approval/roster family (`team-overview`, the `medarbejder` roster, `allocation-breakdown`, balances) — the richest field-mapping + highest drift surface, heaviest to seed → co-locate beside their existing period/tree-seeding suites.
