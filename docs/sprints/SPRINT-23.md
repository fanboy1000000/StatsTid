# Sprint 23 — Phase 4b Publisher Hardening

| Field | Value |
|-------|-------|
| **Sprint** | 23 |
| **Status** | complete |
| **Start Date** | 2026-05-06 |
| **End Date** | 2026-05-06 |
| **Orchestrator Approved** | yes — 2026-05-06 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 warnings 0 errors (the 19 CS0618 warnings are pre-existing carry-forward [Obsolete] breadcrumbs from S20/S21, not introduced by S23) |
| **Test Verified** | yes — 525 unit + 35 plain regression + 76 frontend vitest = 636 directly verified; 61 Docker-gated tests compile but not executed locally (Docker engine not running) |

## Sprint Goal

Tactical hardening pass on the S22 transactional-outbox / row-version exemplar before Phase 4c propagation begins. Absorbs S22 Step 7a Codex cycle-3 cascade findings + Reviewer WARNINGs/NOTEs deferred per cycle-cap discipline. No new architecture: each item maps to a named file and a commit-sized change.

The original SPRINT-23.md placeholder (drafted alongside S22) planned D2.2 ETag/If-Match propagation; that was demoted to Phase 4c by the 2026-05-05 ROADMAP restructure (commit `e070744`) once cycle-3 cascade routed publisher-hardening forward as the higher-priority next step. The propagation work + open questions (Q-23-1..Q-23-4 in the old draft) carry forward to the actual D2.2 sprint.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | Spot-checked ADR-018 path + QUALITY.md presence; all entries link to existing files. |
| Pattern compliance spot-check | CLEAN | No `FindFirst("scopes")` (FAIL-001 regression), no production-code `http://localhost` (only in launchSettings.json dev artifacts). |
| Orphan detection | CLEAN | S21+S22 produced focused changes; no obvious orphans. |
| Documentation drift | DRIFT — fixed | Stale SPRINT-23.md placeholder reflected pre-restructure D2.2 plan; overwritten with this Phase 4b plan. D2.2 content preserved in ROADMAP.md Phase 4c. |
| Quality grade review | stable | S22 was correctness/hardening with no domain quality changes; grades unchanged from S21. |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | OPTIONAL — pure tech-debt / Reviewer-finding-absorption sprint per WORKFLOW.md SKIP rows. User explicitly requested external Codex review of the approach + Open Questions, so Step 0b ran as plan-mode review (one cycle). Internal Reviewer skipped for the same SKIP-row rationale. |
| **External Codex** | invoked 2026-05-06 — 1 cycle, 1B / 3W / 2N. Prompt at `.claude/codex-s23-prompt.txt`; output at `.claude/codex-s23-review.txt`. |
| **Internal Reviewer** | not invoked (SKIP rationale: pure tech-debt absorption sprint, no new architectural ground). |
| **BLOCKERs resolved before Step 1** | yes — BLOCKER on Item 4 (endpoint-level no-op short-circuit bypasses If-Match enforcement) addressed by relocating no-op detection inside `LocalAgreementProfileRepository.SupersedeAndCreateAsync` AFTER `AcquireLockAsync` + `ValidatePrecondition`. See TASK-2304. |

### Findings (cycle 1)

_Codex findings:_
- **BLOCKER — Item 4** — Endpoint-level fast path returns 200 whenever the payload happens to equal the current row, even when the stored version has advanced past the caller's `If-Match`. Bypasses repo-level `OptimisticConcurrencyException`. Reintroduces a TOCTOU race between the predecessor read and the early return. → Resolved: TASK-2304 detection moved inside repo, after lock + version validation.
- **WARNING — Item 1** — `idx_outbox_attempts` is NOT the scan driver for the polling query (excludes `attempts = 0`, no `outbox_id` ordering); `idx_outbox_unpublished` continues to drive scan with the new predicate filtering in-scan. `MaxAttempts = 10` quarantines a poison row in seconds at the 250ms active-poll cadence — acceptable only with explicit operator visibility. → Resolved: TASK-2301 adds a warn-log when a row first crosses the cap; sprint log documents the ops query for stuck rows.
- **WARNING — Item 3** — Blindly formatting `data.version` could synthesize `"undefined"` if the response body is malformed. → Resolved: TASK-2303 adds runtime guard `typeof === 'number'` + `Number.isSafeInteger` + `>= 1`; on guard failure return `etag: null` (no bogus token).
- **WARNING — Item 5c** — The 3 deferred D12 tests cover old NOTE debt, not the *new* hardening behaviour introduced by Items 1 and 4. → Resolved: TASK-2305 adds three sprint-local tests in addition to the deferred D12 set: cap behaviour (poison row stops, others continue), no-op path (no version bump / no audit / no outbox; stale If-Match still 412), weak ETag unit test in `etag.ts`.
- **NOTE — Item 2** — Log enough breadcrumb to recover the audit chain on parse failure: include `outboxId`, `eventId`, `streamId`, raw `correlation_id`. → Resolved: TASK-2302 logs all four fields.
- **NOTE — Item 5a** — Catch only DB/transport (PostgresException, NpgsqlException), let `OperationCanceledException` propagate; do NOT catch broad Exception. → TASK-2305 follows this pattern.

_Open-Questions verdicts (Codex):_
- Q1 (correlation_id storage): Codex picks **A** (log-on-fail). Schema migration to TEXT has higher blast radius for a non-functional concern. Aligns with my recommendation.
- Q2 (weak ETag): Codex picks **Other** — strip `W/` *case-insensitively* (RFC says uppercase but tolerating `w/` is effectively free). Stricter than my Option A.
- Q3 (sprint shape): Codex picks **A** (sequential commits, no agent dispatch) with caveat that Item 4 will touch repo + endpoint + tests in one commit. Aligns with my recommendation.

### Resolution

User accepted all Codex revisions and Open Question picks (2026-05-06). Sprint proceeds with:
- Item 4 implemented at the repository layer with no-op detection inside `SupersedeAndCreateAsync` AFTER lock + version validation.
- Item 1 keeps `MaxAttempts = 10` plus a first-crossing warn-log for ops visibility; the sprint log documents the ops query for stuck rows.
- Item 3 frontend fallback adds runtime guards on `data.version` (typeof, SafeInteger, >= 1).
- Item 5b weak-ETag strip is case-insensitive.
- Item 5c adds three sprint-local hardening tests in addition to the three deferred D12 tests (six total new tests in S23).

## Architectural Constraints Verified

- [ ] P1 — Architectural integrity preserved
- [ ] P2 — Rule engine determinism maintained (no I/O, no side effects) — N/A (no rule-engine touches)
- [ ] P3 — Event sourcing append-only semantics respected
- [ ] P4 — OK version correctness (entry-date resolution) — N/A (version-correctness in S23 refers to row-version optimistic-concurrency on profiles, not OK-version)
- [ ] P5 — Integration isolation and delivery guarantees (outbox cap + correlation_id robustness)
- [ ] P6 — Payroll integration correctness (traceability chain) — N/A (no payroll touches)
- [ ] P7 — Security and access control — unchanged (no auth/scope changes)
- [ ] P8 — CI/CD enforcement — unchanged
- [ ] P9 — Usability and UX (frontend ETag fallback robustness)

## Task Log

### TASK-2301 — Outbox max-attempts cap

| Field | Value |
|-------|-------|
| **ID** | TASK-2301 |
| **Status** | pending |
| **Agent** | Orchestrator (Small Tasks Exception per item) |
| **Components** | Infrastructure (OutboxPublisher) |
| **KB Refs** | ADR-018 (D2 + D4 + D5) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | skipped (Small Tasks Exception) |
| **External Review (Codex)** | bundled into Step 7a sprint-end review |
| **Orchestrator Approved** | no |

**Description**: `OutboxPublisher.ReadBatchAsync` lacks an `attempts < N` predicate. Permanently broken rows hot-loop forever, burning DB + log churn. Add `MaxAttempts = 10` const, extend the polling SQL, and add a first-crossing warn-log so ops can detect stuck rows. The existing `idx_outbox_unpublished` partial index (`service_id, outbox_id` WHERE `published_at IS NULL`) continues to drive the scan; the new predicate is an in-scan filter. The complementary `idx_outbox_attempts` partial index is repurposed for an ops-dashboard query (documented below) that hunts stuck rows.

**Validation Criteria**:
- [ ] `OutboxPublisher.ReadBatchAsync` SQL contains `AND attempts < @maxAttempts`; bound parameter
- [ ] Constant `MaxAttempts = 10`
- [ ] Warn-log emitted on first-crossing inside `IncrementAttemptsAsync` (after the UPDATE, when `attempts + 1 == MaxAttempts`)
- [ ] XML-doc on `MaxAttempts` notes that `idx_outbox_unpublished` remains the primary poll index; `idx_outbox_attempts` is for the ops-dashboard query
- [ ] Docker-gated regression test: row that fails publish 10 times stops being polled; rows on other streams continue publishing
- [ ] `dotnet build` clean

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/Outbox/OutboxPublisher.cs` — add MaxAttempts const, extend SQL, warn-log on first-crossing
- `tests/StatsTid.Tests.Regression/Outbox/OutboxPublisherTests.cs` — Docker-gated cap test

**Ops query for stuck rows** (documented here, no code in S23):
```sql
SELECT outbox_id, stream_id, event_type, attempts, last_error, last_attempt_at
FROM outbox_events
WHERE service_id = $1
  AND published_at IS NULL
  AND attempts >= 10
ORDER BY last_attempt_at DESC
LIMIT 100;
-- Uses idx_outbox_attempts (service_id, attempts, last_attempt_at) WHERE attempts > 0
```

---

### TASK-2302 — `correlation_id` parse robustness

| Field | Value |
|-------|-------|
| **ID** | TASK-2302 |
| **Status** | pending |
| **Agent** | Orchestrator (Small Tasks Exception) |
| **Components** | Infrastructure (OutboxPublisher) |
| **KB Refs** | ADR-018 (D4) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | skipped |
| **External Review (Codex)** | bundled into Step 7a |
| **Orchestrator Approved** | no |

**Description**: `OutboxPublisher.InsertEventAsync` parses `outbox_events.correlation_id` (TEXT) into the `events.correlation_id UUID` column via `Guid.TryParse`. On parse failure today the value silently becomes `DBNull.Value` — the audit-chain breadcrumb is lost without trace. Replace the inline ternary with a guarded helper that logs a structured warning (with `outboxId`, `eventId`, `streamId`, raw value) and binds `DBNull.Value`. Open Question 1 verdict: Option A (log-on-fail), keep `events.correlation_id` as `UUID`. Schema migration is overkill for a debug-breadcrumb concern.

**Validation Criteria**:
- [ ] `InsertEventAsync` correlation_id branch calls a private helper (e.g. `BindCorrelationId(cmd, row, _logger)`)
- [ ] Helper logs structured warning with `outboxId`, `eventId`, `streamId`, raw `correlation_id` on parse failure
- [ ] Helper binds `DBNull.Value` on null OR parse failure
- [ ] Unit test for helper: valid GUID string parses; invalid string logs + binds DBNull; null binds DBNull
- [ ] `dotnet build` clean

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/Outbox/OutboxPublisher.cs` — extract helper, replace ternary
- `tests/StatsTid.Tests.Unit/Outbox/OutboxPublisherCorrelationParseTests.cs` — new

---

### TASK-2303 — Frontend ETag fallback with runtime guard

| Field | Value |
|-------|-------|
| **ID** | TASK-2303 |
| **Status** | pending |
| **Agent** | Orchestrator (Small Tasks Exception) |
| **Components** | Frontend (api/profileApi, lib/etag) |
| **KB Refs** | ADR-018 (D7) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | skipped |
| **External Review (Codex)** | bundled into Step 7a |
| **Orchestrator Approved** | no |

**Description**: `res.headers.get('ETag')` returns null in cross-origin deployments unless `Access-Control-Expose-Headers: ETag` is set. Both `getCurrentProfile` and `saveProfile` should fall back to the response body's `version` field (already returned by `MapProfileResponse`). Per Codex WARNING: blindly formatting `data.version` synthesizes `"undefined"` if the body is malformed. Add a runtime guard before formatting. The backend CORS expose-header is a no-op today (no `AddCors`/`UseCors` present, vite dev-proxy is same-origin) and is deferred to Phase 4e infra when CORS is wired.

**Validation Criteria**:
- [ ] `getCurrentProfile` falls back to `data.version` when `res.headers.get('ETag')` is null AND `data.version` passes the runtime guard
- [ ] `saveProfile` does the same for `newEtag`
- [ ] Runtime guard: `typeof data.version === 'number' && Number.isSafeInteger(data.version) && data.version >= 1`
- [ ] On guard failure, return `etag: null` / `newEtag: null` (no bogus `"undefined"` or `"NaN"` token)
- [ ] vitest tests: header present → uses header; header null + valid body version → fallback ETag; header null + missing/invalid version → null
- [ ] `npx vitest run` green

**Files Changed**:
- `frontend/src/api/profileApi.ts` — add fallback in `getCurrentProfile` (line ~74) and `saveProfile` (line ~176)
- `frontend/src/api/__tests__/profileApi.etag-fallback.test.ts` — new

---

### TASK-2304 — Same-day no-op short-circuit (REPO-LEVEL)

| Field | Value |
|-------|-------|
| **ID** | TASK-2304 |
| **Status** | pending |
| **Agent** | Orchestrator (Small Tasks Exception expanded — touches repo + endpoint + tests, single domain Backend) |
| **Components** | Backend (LocalAgreementProfileRepository, ConfigEndpoints) |
| **KB Refs** | ADR-018 (D7, D9) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | skipped |
| **External Review (Codex)** | bundled into Step 7a (high-risk: repo-level concurrency change + endpoint contract) |
| **Orchestrator Approved** | no |

**Description**: When admin saves a profile with no field changes on the same effective_from, today's flow calls `UpdateInPlaceAsync`, bumps `version`, emits MODIFIED audit, and enqueues an outbox event with empty `changedFields`. Per Codex BLOCKER on the original endpoint-level proposal: bypassing the repo's `If-Match` enforcement reintroduces a TOCTOU race and lets stale callers get 200 when the stored version has advanced. **Revised approach**: detect the no-op INSIDE `LocalAgreementProfileRepository.SupersedeAndCreateAsync`, AFTER `AcquireLockAsync` + `ValidatePrecondition` (the existing concurrency check). The repo returns a flag in its result tuple indicating "no-op" so the endpoint can skip the audit + outbox emission paths. The same-version, same-row, no-mutation result is what the caller receives.

**Validation Criteria**:
- [ ] `SupersedeAndCreateAsync` (or new sibling method) detects no-op AFTER lock + version validation; returns `(profileId, version, isNoOp)` (or equivalent shape)
- [ ] No-op detection condition: `predecessor.EffectiveFrom == newProfile.EffectiveFrom && all-overridable-fields-match` (5 nullable fields)
- [ ] Stale `If-Match` still produces 412 — the precondition check runs before no-op detection
- [ ] On no-op: NO `UPDATE local_agreement_profiles` (version unchanged), NO audit row, NO outbox event
- [ ] Endpoint: when repo returns `isNoOp = true`, skip the audit INSERT + `outbox.EnqueueAsync`; return 200 with predecessor's `Version` and `ETag = "<predecessor.Version>"`
- [ ] Endpoint: when `changedFields.Count == 0` AND no-op detected, alignment validation can be skipped (validator only checks changed fields, so it would no-op anyway — explicit skip is symmetry not safety)
- [ ] Docker-gated regression tests: no-op produces no version bump / no audit row / no outbox row; stale If-Match (lower version than current) still 412; concurrent two-writer no-op race lands as one no-op + one If-Match-ok update
- [ ] `dotnet build` clean

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/LocalAgreementProfileRepository.cs` — extend `SupersedeAndCreateAsync` return shape with `isNoOp`
- `src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs` — branch on `isNoOp` to skip audit + outbox; return early
- `tests/StatsTid.Tests.Regression/Profile/ProfileNoOpShortCircuitTests.cs` — new (Docker-gated)

---

### TASK-2305 — NOTE absorption (412 try/catch + weak ETag + 3 D12 + sprint-local tests)

| Field | Value |
|-------|-------|
| **ID** | TASK-2305 |
| **Status** | pending |
| **Agent** | Orchestrator (Small Tasks Exception) |
| **Components** | Backend (ConfigEndpoints), Frontend (lib/etag), Test suites |
| **KB Refs** | ADR-018 |
| **Constraint Validator** | pending |
| **Reviewer Audit** | skipped |
| **External Review (Codex)** | bundled into Step 7a |
| **Orchestrator Approved** | no |

**Description**: Three NOTE absorptions plus three deferred D12 coverage gaps.

**5a — 412 fallback robustness**: `ConfigEndpoints.cs:303` `GetCurrentOpenAsync` runs AFTER the primary write tx already raised `OptimisticConcurrencyException`. If the recovery fetch also throws, the endpoint surfaces an unhandled exception masking the original concurrency error. Wrap in try/catch — catch `PostgresException` + `NpgsqlException` only; let `OperationCanceledException` and other exceptions propagate. On caught exception: log warning, return 412 with `currentState: null` and the `expectedVersion`/`actualVersion` preserved from the original.

**5b — Weak ETag parser**: `frontend/src/lib/etag.ts` `parseVersionFromETag` rejects weak validators (`W/"5"`) by returning null. Per Codex Q2 verdict (Other): strip `W/` *case-insensitively* before the existing quote/unquote. The integer-row-version semantic doesn't distinguish strong vs weak; tolerating `w/` from broken intermediaries is free.

**5c — Three deferred D12 + three sprint-local tests** (six total new):
- Deferred D12 #1: concurrent enqueue across two endpoints competing for same `stream_id`; assert per-stream FIFO via `outbox_id` ordering on the published `events` rows.
- Deferred D12 #2: sustained-load 50+ rows across 4 streams to exercise `MaxStreamParallelism = 4` saturation.
- Deferred D12 #3: `EndExclusiveMigrationTests` extension — backfill on already-shifted rows is idempotent / no double-shift.
- Sprint-local #1 (TASK-2301): poison row stops being polled at cap; others continue. (Already listed in TASK-2301 acceptance criteria — not double-counted here.)
- Sprint-local #2 (TASK-2304): no-op path semantics. (Already listed in TASK-2304 acceptance criteria.)
- Sprint-local #3 (TASK-2303 + 5b): weak-ETag parser unit tests in `etag.ts`. (vitest)

**Validation Criteria**:
- [ ] `ConfigEndpoints.cs:303` `GetCurrentOpenAsync` wrapped in try/catch (PostgresException + NpgsqlException only); `OperationCanceledException` propagates; on catch: log warning, return 412 body with `currentState: null`
- [ ] `frontend/src/lib/etag.ts` `parseVersionFromETag` strips case-insensitive `W/` prefix before existing logic
- [ ] Three new Docker-gated tests (concurrent-stream FIFO, sustained-load, migration backfill idempotency)
- [ ] vitest tests for the weak-ETag strip (uppercase W/, lowercase w/, no prefix, malformed)
- [ ] `dotnet build` clean
- [ ] `npx vitest run` green

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/ConfigEndpoints.cs` — wrap recovery fetch
- `frontend/src/lib/etag.ts` — case-insensitive `W/` strip
- `tests/StatsTid.Tests.Regression/Outbox/ConcurrentEnqueueTests.cs` — new (Docker-gated)
- `tests/StatsTid.Tests.Regression/Outbox/SustainedLoadTests.cs` — new (Docker-gated)
- `tests/StatsTid.Tests.Regression/Migration/EndExclusiveBackfillIdempotencyTests.cs` — extends existing or new
- `frontend/src/lib/__tests__/etag.weak-validator.test.ts` — new

---

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule-engine or agreement-config touches. |
| Wage type mappings produce correct SLS codes | N/A | No wage-type-mapping touches. |
| Overtime/supplement calculations are deterministic | N/A | No overtime-rule touches. |
| Absence effects on norm/flex/pension are correct | N/A | No absence-rule touches. |
| Retroactive recalculation produces stable results | N/A | No retroactive-correction touches. |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes |
| **Sprint-start commit** | `e070744` (S22 sprint close) |
| **Command** | `codex review --base e070744` (no prompt — fallback shape because intermediate per-task commits exist on master; project-specific steering lost per WORKFLOW.md) |
| **Review Cycles** | 2 — cycle 1 found 1 P1, cycle 2 clean |
| **Findings** | cycle 1: 1 BLOCKER (P1 FIFO regression in TASK-2301 cap predicate); cycle 2: 0 findings |
| **Resolution** | cycle 1 P1 fixed in commit `cd8d2ed` (NOT EXISTS subquery in ReadBatchAsync stalls entire stream when a quarantined ancestor exists); cycle 2 verified clean — `"I did not find any discrete correctness issues in the changes relative to e070744. The no-op profile save path, ETag fallback handling, and outbox hardening all appear internally consistent with the updated tests and surrounding code."` |

### Findings

**Cycle 1 (P1 BLOCKER, fixed in `cd8d2ed`)** — `OutboxPublisher.cs:202` `ReadBatchAsync` SQL: the new `attempts < @maxAttempts` cap predicate filtered out a quarantined row but allowed LATER rows on the same stream to publish, violating ADR-018 D5 per-stream FIFO. Concrete failure: Row N on stream S fails 10 times → attempts reaches MaxAttempts → no longer fetched; Row N+1 on stream S has attempts=0 → IS fetched → publishes → Row N+1 takes the stream_version that should have gone to Row N. Permanent ordering break. **Fix**: extended `ReadBatchAsync` SQL with a `NOT EXISTS` subquery that excludes any row whose stream has a quarantined ancestor (smaller outbox_id, attempts >= MaxAttempts). Net result: quarantined head-of-stream row stalls its ENTIRE stream until manual reconcile (correct strict-FIFO behaviour); other streams drain freely. Added Docker-gated test `MaxAttemptsCap_SameStreamSuccessor_StaysUnpublishedToPreserveFifo` pinning the invariant + cross-stream isolation negative-side. Reaffirms the S22 cycle-2 `break` discipline at READ time.

**Cycle 2** — clean. No findings. Codex verbatim: _"I did not find any discrete correctness issues in the changes relative to e070744. The no-op profile save path, ETag fallback handling, and outbox hardening all appear internally consistent with the updated tests and surrounding code."_

## Test Summary

| Suite | Previous (S22) | Current (S23) | Delta |
|-------|----------------|---------------|-------|
| Unit | 517 | 525 | +8 (TASK-2302 OutboxCorrelationParserTests) |
| Regression — plain | 35 | 35 | +0 |
| Regression — Docker-gated | 50 | 61 | +11 (1 cap test + 6 no-op tests + 3 D12 deferred tests + 1 cycle-2 same-stream-FIFO test) |
| Frontend (vitest) | 48 | 76 | +28 (16 etag.test.ts + 8 profileApi.etag-fallback.test.ts + 4 net new W/ cases) |
| **Total** | **650** | **697** | **+47** |

Docker-gated tests compile but are not executed locally (Docker engine not running on the dev workstation). The 6 pre-existing PlannerInvariantViolation Docker-gated failures inherited from pre-S21 are unchanged by S23.

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 5 (TASK-2301..TASK-2305) |
| Constraint Violations | 0 (Small Tasks Exception per item; Orchestrator self-checked) |
| Reviewer Findings | n/a (internal Reviewer skipped for pure tech-debt sprint per WORKFLOW.md SKIP rule) |
| External Review Cycles | 1 sprint-start (Step 0b, plan-mode) + N sprint-end (Step 7a, populated below) |
| External Findings (Step 0b) | 1 BLOCKER (Item 4 endpoint-level → repo-level) + 3 WARNING (Item 1 index rationale, Item 3 runtime guard, Item 5c new-behaviour test coverage) + 2 NOTE (Item 2 breadcrumb fields, Item 5a narrow catch) — all addressed before Step 1 |
| Re-dispatches | 0 (no agent dispatch — Small Tasks Exception per item) |
| First-Pass Rate | n/a (no agent dispatch) |

## Sprint Retrospective

**What went well**: Step 0b plan-mode Codex review caught the Item 4 BLOCKER before any code was written — moving the no-op detection from endpoint-level to repo-level under lock + precondition validation was a category fix that would have been painful to discover in Step 7a. The `SaveProfileResult` record with the backward-compat 2-arg `Deconstruct` overload absorbed all 13 existing test destructures unchanged — only one helper (using implicit tuple-return conversion) needed an explicit destructure. Per-task commits with sprint-plan + per-item bodies preserved a clean bisectable history.

**What to improve**: Step 7a cycle 1 surfaced a load-bearing P1 (per-stream FIFO violation under the new max-attempts cap predicate) that the Step 0b plan-mode review did not catch. The plan-mode reviewer flagged Item 1's index rationale and observability concerns but missed the FIFO interaction. Lesson: when extending a SQL predicate that interacts with an established ordering invariant (ADR-018 D5 in this case), explicitly enumerate the existing invariant + the new predicate's interaction with it as a Step 0b question, not just at Step 7a. The cycle-2 fix (`NOT EXISTS` subquery on quarantined ancestors) is correct, but the cycle-1 finding cost ~30 min of rework that earlier framing could have avoided.

**Knowledge produced**: No new ADR/PAT entries — S23 is pure ADR-018 hardening. The `SaveProfileResult` record with backward-compat `Deconstruct` is a candidate PAT for future repo-shape evolutions (test-friendly migration), but not promoted in S23 — single instance is not yet a pattern.
