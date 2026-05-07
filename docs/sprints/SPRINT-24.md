# Sprint 24 — Phase 4c Part 1: Atomic Outbox Site Propagation (TASK-2206 redo)

| Field | Value |
|-------|-------|
| **Sprint** | 24 |
| **Status** | planned |
| **Start Date** | 2026-05-07 |
| **End Date** | TBD |
| **Orchestrator Approved** | no |
| **Build Verified** | no |
| **Test Verified** | no |

## Sprint Goal

Propagate S22's atomic exemplar (`ConfigEndpoints` PUT — single `(NpgsqlConnection, NpgsqlTransaction)` carrying state mutation + audit row + outbox enqueue, all committing together) across 7 in-scope state-change repositories and ~21 endpoint sites, so every event-emitting endpoint converts post-commit `eventStore.AppendAsync` to in-tx `IOutboxEnqueue.EnqueueAsync`. Closes the silent partial-failure window that ADR-018 was designed to remove. Refinement at `.claude/refinements/REFINEMENT-s24-task-2206-redo.md` (Cycle 1+2 reviewed, READY).

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | Sampled ADR-018 (`docs/knowledge-base/decisions/ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md`); reference commits on `worktree-agent-a9b76f8d1f88717ff` (`c615dba` / `1466af9` / `c0093e6` / `2aa3044`) verified to exist. |
| Pattern compliance spot-check | DRIFT (resolved upstream) | Refinement-stage grep enumerated 32 post-commit `eventStore.AppendAsync` sites in `Backend.Api`; 21 in S24 scope, 11 carried to Phase 4c.5 (ROADMAP updated). Stream-naming drift vs ADR-018 (`timer-*`/`employee-*` in code vs `timer-session-*`/`time-entry-*`/`skema-*` in ADR-018) flagged for Phase 4c.5 doc-or-code adjudication. |
| Orphan detection | CLEAN | No unused S22/S23 files. |
| Documentation drift | DRIFT (fixed) | ROADMAP S23 row showed 60 Docker-gated tests; S23 sprint log line 11 + MEMORY both show 61. Fixed in this scan (ROADMAP now 61). MEMORY S24 carry-forwards entry up-to-date. |
| Agent scope gap | RESOLVED | `src/Infrastructure/**/*Repository.cs` and `src/Backend/**/Endpoints/*.cs` are not in any single domain agent's scope per `docs/AGENTS.md`. Resolved by adding the **Cross-Domain Authorization** sub-section to `docs/AGENTS.md` (formalizing the convention S22 already established for TASK-2205/2206). S24 tasks now declare `Data Model (extended into Infrastructure, cross-domain authorized)` for TASK-2401 and `Backend API (cross-domain authorized)` for TASK-2402-2407 per the documented convention. |
| Quality grade review | deferred | Will update at sprint-end. |

**Test baseline (post-S23):** 525 unit + 35 plain regression + 61 Docker-gated (55 expected-pass + 6 pre-S21 PlannerInvariantViolation pre-existing failures on master HEAD) + 76 frontend vitest = 697 total. S24 expected addition: ~21 new Docker-gated forced-rollback regression tests + 1 shared `ForcedRollbackHarness.cs`, target 718 total.

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — touches event-store / outbox infrastructure across 6 endpoint files; schema-adjacent (audit row atomic-rollback semantics change); reaches into ADR-018-governed boundaries. |
| **External Codex** | invoked 2026-05-07 — 2 cycles, Cycle 1: 2B/3W/1N → Cycle 2: B2 resolved, B1 re-flagged (governance gap); cycle-cap exit with AGENTS.md governance edit per user approval |
| **Internal Reviewer** | invoked 2026-05-07 — 1 cycle, 0B/6W/6N — all WARNINGs absorbed in Cycle 2 plan rewrite |
| **BLOCKERs resolved before Step 1** | yes — B1 (agent-scope gap) resolved via AGENTS.md Cross-Domain Authorization formalization; B2 (tx contract validation) resolved via TASK-2401 validation criteria additions |

### Findings (cycle 1)

**Codex findings:**
- **BLOCKER (B1)** — Agent assignments don't match `docs/AGENTS.md` scopes. `Data Model Agent` covers SharedKernel only, not Infrastructure repositories. `API Integration Agent` covers `src/Integrations/**/External/**` + `src/Infrastructure/**/Resilience/**`, not Backend.Api endpoints. `Test & QA` is `tests/**` only and runs AFTER impl per AGENTS.md L37.
- **BLOCKER (B2)** — `TASK-2401` validation criteria don't enforce the tx-ownership contract: a repo `(conn, tx)` overload could still open/commit/rollback its own internal transaction and pass the gate. Especially load-bearing for `AgreementConfigRepository.PublishAsync` (lines 182-244 currently own internal tx).
- WARNING (W1) — `TASK-2401` broader than downstream scope: `OvertimePreApprovalRepository.UpdateStatusAsync` would get a `(conn, tx)` overload even though its only S24-relevant consumer (L164) is the only converted site, and L244/L271 are deferred to Phase 4c.5.
- WARNING (W2) — `TASK-2406`/`TASK-2407` lack the zero-`AppendAsync` grep validation criterion that `TASK-2402`-`TASK-2405` have.
- WARNING (W3) — Self-managed overload retention rule conflicts: plan says "present and unchanged"; refinement says "grep and remove if dead."
- NOTE — ADR-018 references current and aligned.

**Internal Reviewer findings:**
- WARNING (R1) — `TASK-2401` validation criterion needs explicit grep pattern (`AppendAuditAsync\(NpgsqlConnection`) so Constraint Validator can mechanically verify the 4 audit overloads exist.
- WARNING (R2) — `TASK-2403` publish-path validation under-specified vs current code shape. `AgreementConfigEndpoints.cs:287-311` does FOUR things: `PublishAsync` → `GetByIdAsync` re-read (L291) → `AppendAuditAsync` → post-commit `AppendAsync`. The re-read placement (in-tx vs post-commit) needs an explicit decision.
- WARNING (R3) — Self-managed overload silent removal is a behavior decision, not just an entropy step. (Same concern as Codex W3.)
- WARNING (R4) — Forced-rollback test mechanism unspecified. 6 sub-agents will pick 6 different patterns. Recommend shared `ForcedRollbackHarness.cs`.
- WARNING (R5) — `TASK-2407` stream-naming drift: new tests will assert against current `timer-*` name; Phase 4c.5 will need to update them in lock-step.
- WARNING (R6) — Phase 1/Phase 2 parallelization shape unspecified. 6 endpoint tasks could run in parallel via `isolation: "worktree"` per WORKFLOW.md Step 3.
- NOTE — Site counts, refinement Q5 alignment, Pattern B count of 4, Architectural Constraints mapping, TASK-2406 silent-state-change exclusion, test-baseline arithmetic, no-new-ADR stance, no-wall-clock-timebox alignment — all verified clean.

### Resolution

Cycle 2 fixes applied below (re-review pending):

- **B1 fix (cycle 2 + post-cycle-cap governance edit)**: Cycle 2 split TASK-2408 out for forced-rollback tests (Test & QA, Phase 3, runs AFTER all impl per `AGENTS.md` L37). Cycle 2 Codex re-flagged B1 because "Orchestrator-coordinated" wasn't a documented agent type. **Resolution (post-cycle-cap, user-approved)**: added a new **Cross-Domain Authorization** sub-section to `docs/AGENTS.md` formalizing the convention S22 already used (TASK-2205, TASK-2206). S24 tasks relabeled to use the documented form: TASK-2401 = `Data Model (extended into Infrastructure, cross-domain authorized)`; TASK-2402-2407 = `Backend API (cross-domain authorized)`; TASK-2408 = `Test & QA`. The B1 finding is fully resolved: every task now maps to a documented agent (or cross-domain-authorized declaration) per governance. Cycle 3 re-review skipped — the relabel is a label correction against documented governance, not a substantive plan change. The user approved this exit at cycle cap.
- **B2 fix**: TASK-2401 validation criteria now include "(conn, tx) overloads MUST reuse caller-owned transaction; MUST NOT call `BeginTransactionAsync`, `CommitAsync`, or `RollbackAsync` on the passed-in `tx` parameter or open a new connection." Verified by source-level inspection per repo + a unit test that calls each overload with a sentinel transaction and asserts the overload neither commits nor rolls back.
- **W1 fix (Codex)**: TASK-2401 narrowed — `(conn, tx)` overloads added only for write methods that have a converted consumer in TASK-2402-2407.
- **W2 fix (Codex)**: TASK-2406 and TASK-2407 now include zero-`AppendAsync` grep criterion.
- **W3 fix (Codex+Reviewer)**: Self-managed overloads explicitly retained in S24. Removal of dead self-managed overloads deferred to Phase 4c.5 cleanup task (consistent rule).
- **R1 fix**: TASK-2401 validation explicitly cites `Grep "AppendAuditAsync\(NpgsqlConnection"` as the verification mechanism.
- **R2 fix**: TASK-2403 publish-path GetByIdAsync re-read moved post-commit. Decision rationale: re-read returns the published config to the caller; semantically a separate read after the atomic write completes. Avoids ambiguity about RepeatableRead snapshot consistency mid-tx.
- **R4 fix**: Shared `ForcedRollbackHarness.cs` written as part of TASK-2408 (Test & QA, Phase 3) and used by all 21 forced-rollback tests.
- **R5 fix**: TASK-2407 validation criteria include a `// Phase 4c.5 carry-forward: rename to timer-session-* in this test when ADR-018 alignment lands` breadcrumb in each new test.
- **R6 fix**: Phase 2 declared parallelizable via `isolation: "worktree"`. TASK-2402-2407 each touch a single endpoint file + nothing else; integration-conflict surface is zero.
- **NOTE absorbed**: P3 (event sourcing append-only) is preserved by `OutboxPublisher`'s existing `AppendAsync` shape under its own ReadCommitted tx, NOT by the endpoint's tx. Endpoint moves the call site from "endpoint post-commit" to "publisher polled". Append-only semantics unchanged.

## Architectural Constraints Verified

- [ ] P1 — Architectural integrity preserved (atomic exemplar propagation only; no new patterns)
- [ ] P3 — Event sourcing append-only semantics respected. **Preserved by `OutboxPublisher`'s existing `AppendAsync` shape under its own ReadCommitted tx; endpoint changes shift the call site, not the canonical append path.**
- [ ] P5 — Integration isolation and delivery guarantees (closes silent-loss window per ADR-018)
- [ ] P7 — Security and access control (existing `RequireAuthorization` calls unchanged)
- [ ] P8 — CI/CD enforcement (build clean, all tests pass, +21 new D-tests + harness)

Not directly affected: P2, P4, P6, P9.

## Task Log

### TASK-2401 — Repository `(conn, tx)` overloads

| Field | Value |
|-------|-------|
| **ID** | TASK-2401 |
| **Status** | planned |
| **Agent** | Data Model (extended into Infrastructure, cross-domain authorized; general-purpose Agent invocation; scope: `src/Infrastructure/StatsTid.Infrastructure/*Repository.cs`) |
| **Components** | Infrastructure / Repositories |
| **KB Refs** | ADR-018 (D2/D3/D5) |
| **Phase** | Phase 1 (independent foundation; everything else depends on it) |

**Description**: Add `(NpgsqlConnection, NpgsqlTransaction)` overloads to write methods on the 7 in-scope repositories that have a converted consumer in TASK-2402-2407, mirroring `LocalAgreementProfileRepository.SupersedeAndCreateAsync(conn, tx, …)` at lines 247–252. Self-managed overloads stay (read paths and tests). For the 4 audit-bearing (Pattern B) repos — `Approval`, `AgreementConfig`, `PositionOverride`, `WageTypeMapping` — also add `(conn, tx)` overload on `AppendAuditAsync` so audit inserts share the endpoint tx. The remaining 3 in-scope repos (`OvertimePreApproval`, `OvertimeBalance`, `Timer`) have no audit method.

`EntitlementBalanceRepository` is **excluded** from S24 (its only event-emitting consumer is `SkemaEndpoints.cs:439`, deferred to Phase 4c.5; the overload travels with Skema there).

For `AgreementConfigRepository.PublishAsync` (lines 182–244, owns internal multi-table tx today): keep `PublishAsync(configId, actorId, ct)` as the public entry; add `PublishAsync(conn, tx, configId, actorId, ct)` overload; route the public version through `BeginTransactionAsync` then through the overload. Same shape as `LocalAgreementProfileRepository.SupersedeAndCreateAsync(ct)`.

**Tx contract (verified by validation criteria below)**: `(conn, tx)` overloads MUST reuse the caller-owned transaction. They MUST NOT call `BeginTransactionAsync`, `CommitAsync`, or `RollbackAsync` on the passed-in `tx` parameter, AND MUST NOT open a new `NpgsqlConnection`.

**Validation Criteria**:
- [ ] Every write method on the 7 in-scope repos that has a converted consumer in TASK-2402-2407 exposes a `(NpgsqlConnection, NpgsqlTransaction, …, CancellationToken)` overload. Verified by `Grep "(NpgsqlConnection conn, NpgsqlTransaction tx"` returning a hit per converted-consumer method.
- [ ] All 4 Pattern B repos expose `(conn, tx)` overloads on `AppendAuditAsync`. Verified by `Grep "AppendAuditAsync\(NpgsqlConnection"` returning 4 hits.
- [ ] `AgreementConfigRepository.PublishAsync(conn, tx, …)` overload added; public `PublishAsync(ct)` entry routes through it via `BeginTransactionAsync`.
- [ ] **Tx contract enforcement**: source-level inspection per repo confirms `(conn, tx)` overloads do NOT call `BeginTransactionAsync` / `CommitAsync` / `RollbackAsync` on the passed-in `tx`, and do NOT open a new connection. Verified by per-overload review during Constraint Validator step 5α.
- [ ] **Tx contract test**: a unit test calls each `(conn, tx)` overload with a real `NpgsqlTransaction` and asserts the overload neither commits nor rolls back the tx (the test owns commit).
- [ ] Self-managed overloads still present and unchanged in signature. Removal of dead self-managed overloads is **explicitly out of scope for S24** — deferred to a Phase 4c.5 cleanup task once Step 0a-style grep enumerates which are dead post-S24.
- [ ] `dotnet build` clean (0/0).
- [ ] All existing 525 unit + 35 plain + 55 expected-pass Docker-gated + 76 frontend tests still pass.

**Files Changed** (anticipated):
- `src/Infrastructure/StatsTid.Infrastructure/ApprovalPeriodRepository.cs`
- `src/Infrastructure/StatsTid.Infrastructure/AgreementConfigRepository.cs`
- `src/Infrastructure/StatsTid.Infrastructure/PositionOverrideRepository.cs`
- `src/Infrastructure/StatsTid.Infrastructure/WageTypeMappingRepository.cs`
- `src/Infrastructure/StatsTid.Infrastructure/OvertimePreApprovalRepository.cs`
- `src/Infrastructure/StatsTid.Infrastructure/OvertimeBalanceRepository.cs`
- `src/Infrastructure/StatsTid.Infrastructure/TimerSessionRepository.cs`
- New unit test file (in-scope per TASK-2401 since it's a tx-contract test, not a forced-rollback regression test): `tests/StatsTid.Tests.Unit/Infrastructure/TxContractTests.cs`

---

### TASK-2402 — ApprovalEndpoints atomic conversion

| Field | Value |
|-------|-------|
| **ID** | TASK-2402 |
| **Status** | planned |
| **Agent** | Backend API (cross-domain authorized; general-purpose Agent invocation in worktree; scope: `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs` only) |
| **Components** | Backend.Api / Endpoints |
| **KB Refs** | ADR-018 (D2/D3/D5) |
| **Phase** | Phase 2 (parallelizable with TASK-2403-2407 via `isolation: "worktree"`; depends on TASK-2401) |

**Description**: Convert 5 post-commit `eventStore.AppendAsync` sites in `ApprovalEndpoints.cs` (L91, L143, L197, L377, L430) to atomic shape: open `conn`+`tx`, call `ApprovalPeriodRepository (conn, tx)` write, call `(conn, tx)` audit, call `outbox.EnqueueAsync(conn, tx, …)`, then `tx.CommitAsync()`. Remove post-commit `AppendAsync`.

Forced-rollback regression tests for these 5 sites are in TASK-2408 (Test & QA, Phase 3).

**Validation Criteria**:
- [ ] Zero post-commit `eventStore.AppendAsync` calls remain in `ApprovalEndpoints.cs` (verified by `Grep "eventStore.AppendAsync" ApprovalEndpoints.cs` returning zero hits).
- [ ] All 5 sites use atomic in-tx pattern: `await using var tx = await conn.BeginTransactionAsync(ct);` followed by repo `(conn, tx)` write + `(conn, tx)` audit + `outbox.EnqueueAsync(conn, tx, …)` + `await tx.CommitAsync(ct);`.
- [ ] No leaked control flow (every path either commits or rolls back via `await using var tx`).

**Files Changed** (anticipated):
- `src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs`

---

### TASK-2403 — AgreementConfigEndpoints atomic conversion

| Field | Value |
|-------|-------|
| **ID** | TASK-2403 |
| **Status** | planned |
| **Agent** | Backend API (cross-domain authorized; general-purpose Agent invocation in worktree; scope: `src/Backend/StatsTid.Backend.Api/Endpoints/AgreementConfigEndpoints.cs` only) |
| **Components** | Backend.Api / Endpoints |
| **KB Refs** | ADR-018 (D2/D3/D5) |
| **Phase** | Phase 2 (parallelizable with TASK-2402, 2404-2407; depends on TASK-2401) |

**Description**: Convert 5 post-commit `eventStore.AppendAsync` sites in `AgreementConfigEndpoints.cs` (L106, L203, L260, L311, L360) to atomic shape. Most invasive of the 6 endpoint files because L260 is the publish handler — it now drives a single tx through `AgreementConfigRepository.PublishAsync(conn, tx, …)` overload, emits ONE `PUBLISHED` audit row + ONE `AgreementConfigPublished` event in-tx (Q5 resolution).

**Publish-path GetByIdAsync re-read placement** (Reviewer R2): the re-read at `AgreementConfigEndpoints.cs:291` (today reads the just-published config to return to the caller) is moved **POST-COMMIT** — runs after `tx.CommitAsync(ct)`. Rationale: it's a read for the response payload, semantically separate from the atomic write; avoids RepeatableRead snapshot ambiguity.

Forced-rollback regression tests are in TASK-2408. The publish-path test asserts archive+activate+audit+event ALL roll back if any step fails.

**Validation Criteria**:
- [ ] Zero post-commit `eventStore.AppendAsync` calls remain in `AgreementConfigEndpoints.cs`.
- [ ] All 5 sites use atomic in-tx pattern.
- [ ] Publish path emits one audit row, one event, both in same tx as archive+activate.
- [ ] Publish-path `GetByIdAsync` re-read (line ~291 today) runs AFTER `tx.CommitAsync`.
- [ ] No leaked control flow.

**Files Changed** (anticipated):
- `src/Backend/StatsTid.Backend.Api/Endpoints/AgreementConfigEndpoints.cs`

---

### TASK-2404 — PositionOverrideEndpoints atomic conversion

| Field | Value |
|-------|-------|
| **ID** | TASK-2404 |
| **Status** | planned |
| **Agent** | Backend API (cross-domain authorized; general-purpose Agent invocation in worktree; scope: `src/Backend/StatsTid.Backend.Api/Endpoints/PositionOverrideEndpoints.cs` only) |
| **Components** | Backend.Api / Endpoints |
| **KB Refs** | ADR-018 (D2/D3/D5) |
| **Phase** | Phase 2 (parallelizable; depends on TASK-2401) |

**Description**: Convert 4 post-commit `eventStore.AppendAsync` sites in `PositionOverrideEndpoints.cs` (L102, L166, L209, L252) to atomic shape.

**Validation Criteria**:
- [ ] Zero post-commit `eventStore.AppendAsync` calls remain in `PositionOverrideEndpoints.cs`.
- [ ] All 4 sites use atomic in-tx pattern.
- [ ] No leaked control flow.

**Files Changed** (anticipated):
- `src/Backend/StatsTid.Backend.Api/Endpoints/PositionOverrideEndpoints.cs`

---

### TASK-2405 — WageTypeMappingEndpoints atomic conversion

| Field | Value |
|-------|-------|
| **ID** | TASK-2405 |
| **Status** | planned |
| **Agent** | Backend API (cross-domain authorized; general-purpose Agent invocation in worktree; scope: `src/Backend/StatsTid.Backend.Api/Endpoints/WageTypeMappingEndpoints.cs` only) |
| **Components** | Backend.Api / Endpoints |
| **KB Refs** | ADR-018 (D2/D3/D5) |
| **Phase** | Phase 2 (parallelizable; depends on TASK-2401) |

**Description**: Convert 3 post-commit `eventStore.AppendAsync` sites in `WageTypeMappingEndpoints.cs` (L84, L149, L208) to atomic shape.

**Validation Criteria**:
- [ ] Zero post-commit `eventStore.AppendAsync` calls remain in `WageTypeMappingEndpoints.cs`.
- [ ] All 3 sites use atomic in-tx pattern.
- [ ] No leaked control flow.

**Files Changed** (anticipated):
- `src/Backend/StatsTid.Backend.Api/Endpoints/WageTypeMappingEndpoints.cs`

---

### TASK-2406 — OvertimeEndpoints atomic conversion

| Field | Value |
|-------|-------|
| **ID** | TASK-2406 |
| **Status** | planned |
| **Agent** | Backend API (cross-domain authorized; general-purpose Agent invocation in worktree; scope: `src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs` only) |
| **Components** | Backend.Api / Endpoints |
| **KB Refs** | ADR-018 (D2/D3/D5) |
| **Phase** | Phase 2 (parallelizable; depends on TASK-2401) |

**Description**: Convert 2 post-commit `eventStore.AppendAsync` sites in `OvertimeEndpoints.cs` (L164 — pre-approval create; L328 — overtime balance adjust) to atomic shape. The silent-state-change bug at L244/L271 (UpdateStatusAsync APPROVED/REJECTED with NO event emission) is **out of scope** — pre-existing, requires net-new event types, deferred to Phase 4c.5 (tracked in MEMORY deferred items).

**Validation Criteria**:
- [ ] L164 and L328 use atomic in-tx pattern.
- [ ] Zero post-commit `eventStore.AppendAsync` calls remain in `OvertimeEndpoints.cs` *that are inside scope* (verified by `Grep "eventStore.AppendAsync" OvertimeEndpoints.cs` returning zero hits — consistent with TASK-2402-2405).
- [ ] L244/L271 unchanged (silent state-change carry-forward to Phase 4c.5).
- [ ] No leaked control flow.

**Files Changed** (anticipated):
- `src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs`

---

### TASK-2407 — TimerEndpoints atomic conversion

| Field | Value |
|-------|-------|
| **ID** | TASK-2407 |
| **Status** | planned |
| **Agent** | Backend API (cross-domain authorized; general-purpose Agent invocation in worktree; scope: `src/Backend/StatsTid.Backend.Api/Endpoints/TimerEndpoints.cs` only) |
| **Components** | Backend.Api / Endpoints |
| **KB Refs** | ADR-018 (D2/D3/D5) |
| **Phase** | Phase 2 (parallelizable; depends on TASK-2401) |

**Description**: Convert 2 post-commit `eventStore.AppendAsync` sites in `TimerEndpoints.cs` (L66 — check-in; L123 — check-out) to atomic shape. Streams currently named `timer-*` (per code) which conflicts with ADR-018's `timer-session-*` spec — do NOT rename in S24 (would break replay determinism); flag for Phase 4c.5 doc-or-code adjudication.

**Validation Criteria**:
- [ ] L66 and L123 use atomic in-tx pattern.
- [ ] Zero post-commit `eventStore.AppendAsync` calls remain in `TimerEndpoints.cs`.
- [ ] Stream name unchanged (`timer-*`); flag preserved for Phase 4c.5.
- [ ] Forced-rollback regression tests added in TASK-2408 include the breadcrumb comment `// Phase 4c.5 carry-forward: rename to timer-session-* in this test when ADR-018 alignment lands`.
- [ ] No leaked control flow.

**Files Changed** (anticipated):
- `src/Backend/StatsTid.Backend.Api/Endpoints/TimerEndpoints.cs`

---

### TASK-2408 — Forced-rollback regression test suite + shared harness

| Field | Value |
|-------|-------|
| **ID** | TASK-2408 |
| **Status** | planned |
| **Agent** | Test & QA (scope: `tests/**` per AGENTS.md) |
| **Components** | Tests / Regression / Outbox |
| **KB Refs** | ADR-018 (D2/D3/D5) |
| **Phase** | Phase 3 (sequential, runs AFTER all impl agents complete per AGENTS.md L37) |

**Description**: Build a shared `ForcedRollbackHarness.cs` and write 21 forced-rollback Docker-gated regression tests, one per converted endpoint site. The harness must be reused by all 21 tests so the forced-failure mechanism is consistent.

**Harness mechanism (recommended, finalize during impl)**: a test-only `IOutboxEnqueue` decorator that throws on call. Inject the decorator via DI override in the test's `WebApplicationFactory<Program>`. Each test then calls the converted endpoint, expects 500, and asserts that:
- The state-mutation row was NOT inserted/updated (DB query against the relevant table).
- The audit row was NOT inserted (DB query against the audit table, where applicable).
- No event row appears in `events` table for the relevant stream.
- No outbox row appears in `outbox_events` for the relevant stream.

The 21 test scenarios mirror the converted sites in TASK-2402-2407: 5 (Approval) + 5 (AgreementConfig) + 4 (PositionOverride) + 3 (WageTypeMapping) + 2 (Overtime) + 2 (Timer) = 21.

**Validation Criteria**:
- [ ] Shared `ForcedRollbackHarness.cs` exists and is reused by all 21 tests.
- [ ] 21 forced-rollback regression tests added under `tests/StatsTid.Tests.Regression/Outbox/` (or similar).
- [ ] Each test asserts: 500 response + no state-mutation row + no audit row (where applicable) + no event row + no outbox row.
- [ ] All 21 tests pass under Docker.
- [ ] Each TimerEndpoints test (2) carries the breadcrumb comment for Phase 4c.5 stream rename.
- [ ] Test execution time within S22-baseline expectations (each forced-rollback test should be a single endpoint call + a few DB assertions).

**Files Changed** (anticipated):
- `tests/StatsTid.Tests.Regression/Outbox/ForcedRollbackHarness.cs` (new)
- `tests/StatsTid.Tests.Regression/Outbox/ApprovalAtomicTests.cs` (new, 5 tests)
- `tests/StatsTid.Tests.Regression/Outbox/AgreementConfigAtomicTests.cs` (new, 5 tests)
- `tests/StatsTid.Tests.Regression/Outbox/PositionOverrideAtomicTests.cs` (new, 4 tests)
- `tests/StatsTid.Tests.Regression/Outbox/WageTypeMappingAtomicTests.cs` (new, 3 tests)
- `tests/StatsTid.Tests.Regression/Outbox/OvertimeAtomicTests.cs` (new, 2 tests)
- `tests/StatsTid.Tests.Regression/Outbox/TimerAtomicTests.cs` (new, 2 tests)

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule engine changes |
| Wage type mappings produce correct SLS codes | N/A | No payroll calculation changes |
| Overtime/supplement calculations are deterministic | N/A | No rule engine changes |
| Absence effects on norm/flex/pension are correct | N/A | No absence logic changes |
| Retroactive recalculation produces stable results | N/A | No retroactive logic changes |

S24 is infrastructure-only. Legal/payroll surfaces unaffected.

## External Review (Step 7a)

_Pending sprint-end._

| Field | Value |
|-------|-------|
| **Invoked** | not yet |
| **Sprint-start commit** | `b046399` (S23 sprint close, pushed to origin/master 2026-05-07) |
| **Command** | TBD at sprint end |
| **Review Cycles** | 0 |
| **Findings** | 0B, 0W, 0N |
| **Resolution** | n/a |

### Findings

_Pending._

## Test Summary

_Pending sprint-end. Target: 525 unit + N tx-contract unit tests + 35 plain + 82 Docker-gated (55 pre-existing pass + 6 pre-existing fail + 21 new forced-rollback) + 76 frontend = 718+ total._

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 525 + tx-contract | pending — TASK-2401 adds tx-contract test(s) |
| Plain regression tests | 35 | pending |
| Docker-gated regression tests | 82 (target) | pending — 21 new forced-rollback tests in TASK-2408 |
| Frontend vitest | 76 (target) | pending |
| **Total** | 718+ (target) | pending |

## Agent Effectiveness

_Pending sprint-end._

| Metric | Value |
|--------|-------|
| Tasks | 8 (planned) |
| Constraint Violations | TBD |
| Reviewer Findings | TBD |
| External Review Cycles | TBD (sprint-end) + 1 cycle (Step 0b plan review) — 2B/3W/1N Codex + 0B/6W/6N internal — all resolved before Step 1 |
| External Findings | TBD |
| Re-dispatches | TBD |
| First-Pass Rate | TBD |

## Sprint Retrospective

_Pending sprint-end._

**What went well**: TBD

**What to improve**: TBD

**Knowledge produced**: TBD (no new ADR planned — S24 propagates ADR-018 only)
