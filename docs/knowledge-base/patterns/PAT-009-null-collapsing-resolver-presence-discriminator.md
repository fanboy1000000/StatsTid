# PAT-009 — Null-collapsing shared resolver + presence discriminator

| Field | Value |
|-------|-------|
| **Status** | approved |
| **Sprint** | S66 (proposed by the TASK-6603 implementation agent; Reviewer-adjudicated accept) |
| **Domains** | Backend, Infrastructure |
| **Tags** | shared-seam, resolver, null-semantics, drift-proof, consumption |

## Pattern

A shared calculator/resolver may deliberately collapse two distinct domain cases into a single `null` for its own contract's simplicity — e.g. `DailyNormCalculator` returns `null` for BOTH "employee is ANNUAL_ACTIVITY (no daily norm by design)" and "no dated profile covers this date (data-integrity signal)". When a downstream consumer must treat those cases differently (ADR-032: academics get a `7.4 × fraction` fallback basis; no-profile must fail closed), the consumer **re-resolves only the cheap discriminator input** (here: dated-profile *presence* via `GetByEmployeeIdAtAsync`) — **never the collapsed value itself**.

## Guardrail (load-bearing)

**Re-resolve the *discriminator* only, never the *value*.** Re-deriving the collapsed computation (the norm formula) downstream would defeat the drift-proof single-source seam the shared calculator exists to provide (the S65 `DailyNormCalculator` extraction). If the discriminator itself becomes expensive or the split recurs in a third consumer, promote the distinction into the shared contract (e.g. a discriminated result type) instead of stacking re-resolves.

## Origin

S66 `ConsumptionCalculator.FullDayHoursAsync` (`src/Backend/StatsTid.Backend.Api/Services/ConsumptionCalculator.cs`): norm-null + profile-present ⇒ ANNUAL_ACTIVITY fallback; norm-null + profile-null ⇒ propagate null (anchor-422/fail-closed family). Reviewer assessment: recurring tension whenever a shared seam is intentionally lossy; accepted with the guardrail above.
