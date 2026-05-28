# Sprint 56 — Arbejdstid Work-Time Persistence + Three-Row Redesign + Timer Retirement + Allocation Gate

| Field | Value |
|-------|-------|
| **Sprint** | 56 |
| **Status** | complete |
| **Start Date** | 2026-05-28 |
| **End Date** | 2026-05-28 |
| **Orchestrator Approved** | yes — 2026-05-28 |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 errors (38 pre-existing warnings) |
| **Test Verified** | yes — 552 unit + 128 frontend vitest + 15 new S56 Docker-gated D-tests, all passing |
| **Sprint-start commit** | `df036c3` (S55) |

## Sprint Goal
Fix the reported data-loss bug — work time added via "Tilføj periode" under Arbejdstid in Min Tid
disappears on month approval because it is never persisted — by giving self-recorded work time a real
event-sourced persistence layer; restructure the work-time section into three rows (Tilføj periode /
Tilføj timer / Diff. fra normtid with real per-employee norm); retire the check-in/out timer; and add a
hard allocation-reconciliation gate so a month cannot be approved with unallocated worked hours.

Full design + dual-lens refinement: `.claude/refinements/REFINEMENT-arbejdstid-persistence-redesign.md`.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | Spot-check of touched ADRs/DEP-004 resolve |
| Pattern compliance spot-check | CLEAN | 0 `FindFirst("scopes")` (FAIL-001 safe); `http://localhost` only in launchSettings.json |
| Orphan detection | CLEAN | n/a — no new files from last 2 sprints flagged |
| Documentation drift | DRIFT | ROADMAP.md rolling detail lags execution (frontier ~S30 vs git S55). Pre-existing; flagged for a future ROADMAP reconciliation, not fixed in S56 (out of this sprint's scope). MEMORY.md "Sprint 52 in progress" also stale. |
| Quality grade review | CLEAN | Skema/work-time domain gains real persistence + test coverage (15 new D-tests); no grade regression. Detailed QUALITY.md matrix refresh deferred to next maintenance pass (low value this sprint). |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (P1 architectural integrity; P3 event sourcing; schema migration add/drop table) |
| **External Codex** | invoked 2026-05-28 — cycle 1: 3B/2W/0N (CLI hung twice earlier this session; this pass completed and IS the external Codex pass per AGENTS.md L503 — not skipped) |
| **Internal Reviewer** | invoked 2026-05-28 — cycle 1: 0B/3W/4N |
| **BLOCKERs resolved before Step 1** | yes — all 3 Codex BLOCKERs resolved (cycle 1); cycle 2 clean on both lenses |

### Findings (cycle 1)

_Codex findings:_
- BLOCKER — TASK-5603/5601 (P3) — plan pins projection but not event emission; ADR-018 requires event+outbox+projection in one tx. Fix: enqueue `WorkTimeRegistered` via IOutboxEnqueue before projection upsert; projection version = event ordering; validate replay/backfill + publisher-stalled RYW.
- BLOCKER — Phase Dependencies / TASK-5604 — "parallel" contradicts 5604's dependency on 5603's projection. Fix: sequence 5604 after 5603.
- BLOCKER — TASK-5602/5605 — dropping timer_sessions (Phase 1) before code reading it removed (Phase 2) breaks runtime. Fix: co-land DROP with 5605 after grep-zero.
- WARNING — TASK-5607 — frontend vitest outside Test & QA `tests/**` scope. Fix: cross-domain-authorized label.
- WARNING — Step 0b process — "document skip" language contradicts AGENTS.md L503 (halt-and-prompt). Fix: this completed Codex pass IS the external review.

_Internal Reviewer findings:_
- WARNING — TASK-5602/5605 build-order co-landing (convergent with Codex BLOCKER 3).
- WARNING — TASK-5605 GET removal surface understated (also remove timerRepo param SkemaEndpoints.cs:111 + timerSession return field :264).
- WARNING — TASK-5606 — submit-path okVersion `year>=2026?OK26:OK24` (useSkema.ts:144) imprecise vs 2026-04-01 switch (P4). Fix or record scope decision.
- NOTE — agent assignments correct; add TASK-5603 P2 pure-read criterion; add TASK-5604 null-TaskId parity criterion; timer-event-retention call confirmed sound.

### Resolution (cycle 1)

All 3 Codex BLOCKERs resolved by plan edit:
- TASK-5603 description + criterion now require `WorkTimeRegistered` enqueued in-tx before projection upsert (ADR-018 D3/D13) with replay/backfill + RYW validation.
- Phase Dependencies restructured: backend tasks SEQUENCED (Phase 2 = 5603 → Phase 3 = 5604 → Phase 4 = 5605+5602(B)); 5602 split into (A) CREATE with 5603 and (B) DROP co-landing with 5605 after grep-zero.
- TASK-5602(B)/5605 co-landing + grep-zero validation pinned.

WARNINGs/NOTEs absorbed: TASK-5607 relabeled cross-domain authorized; TASK-5605 GET surface broadened (param :111 + field :264); TASK-5606 gains submit-path okVersion fix criterion; TASK-5603 gains P2 pure-read criterion; TASK-5604 gains null-TaskId parity criterion; the "document skip" language corrected (this Codex pass is the external review).

### Findings (cycle 2 — verification of cycle-1 edits)

- _Codex:_ "Clean — cycle-1 findings resolved, no new findings." All 3 BLOCKERs + 2 WARNINGs verified resolved.
- _Internal Reviewer:_ "Cycle-1 findings resolved; no new findings." 2 harmless NOTEs (TASK-5603 cites :524 for the enqueue block whose enqueue/projection pair is actually :537-539 — same code unit, no action; the Phase-4 init.sql-DROP + Backend-code co-landing crosses ownership but is intentional + grep-zero gated + Orchestrator-consistent).

**Step 0b OUTCOME: COMPLETE.** 2 cycles (cap respected); cycle 2 clean on both lenses. BLOCKERs resolved before Step 1. Plan APPROVED for decompose/delegate.

## Architectural Constraints Verified
- [x] P1 — Architectural integrity preserved (bounded contexts; Skema→Config read-only resolution, verified by Step 7a Reviewer)
- [x] P2 — Reconciliation gate + norm resolution are approval-precondition/read logic, NOT rule-engine (no rule-engine call; verified both lenses)
- [x] P3 — Event sourcing append-only (WorkTimeRegistered enqueued in-tx before projection upsert; latest-wins outbox_id guard; backfill parity; timer event types RETAINED for replay)
- [x] P4 — OK version via OkVersionResolver per day (2026-04-01 switch); submit-path okVersion fixed
- [x] P6 — Historical timer-origin TimeEntry rows retained; verified no wage-type/rule logic keys off ActivityType="timer"
- [x] P7 — Work-time save employee-scoped (EmployeeId from route); norm resolved server-side; Step 7a BLOCKER (out-of-month date lock-bypass) fixed + tested
- [x] P9 — Three-row redesign + Ikke fordelt row (per-day + all-days-balanced month total) + summary card; directional Danish 422 errors

## Task Log

### TASK-5601 — WorkTimeRegistered event + EventSerializer (timer types retained)
| Field | Value |
|-------|-------|
| **ID** | TASK-5601 |
| **Status** | complete |
| **Agent** | Data Model |
| **Components** | SharedKernel (Events/Models), Infrastructure (EventSerializer) |
| **KB Refs** | PAT-004 (events extend DomainEventBase), PAT-001 (immutable), DEP-003 (serializer registration), ADR-018 D3 |
**Description**: Add `WorkTimeRegistered` domain event carrying { employeeId, date, intervals[{start,end}], manualHours } for one (employee, date); register in EventSerializer type map. RETAIN existing `TimerCheckedIn`/`TimerCheckedOut` classes + registrations (deserialize-only — append-only store still replays historical timer events). Per ADR-020/021/023 versioned precedent, re-saving a day emits a NEW superseding event (latest-wins projection).
**Validation Criteria**:
- [ ] `WorkTimeRegistered : DomainEventBase`, init-only props; registered in EventSerializer
- [ ] EventSerializer round-trip test for WorkTimeRegistered passes
- [ ] TimerCheckedIn/Out remain registered; their round-trip tests still pass
**Files Changed**: `src/SharedKernel/**/Events/WorkTimeRegistered.cs`, `src/Infrastructure/**/EventSerializer.cs`

---

### TASK-5602 — Schema: work_time_projection table + drop timer_sessions (ORCHESTRATOR-OWNED)
| Field | Value |
|-------|-------|
| **ID** | TASK-5602 |
| **Status** | complete |
| **Agent** | Orchestrator (init.sql schema is Orchestrator-gated per CLAUDE.md) |
| **Components** | docker/postgres/init.sql, docs/generated/db-schema.md |
| **KB Refs** | ADR-018 D13 (sync-in-tx projection), ADR-016 D5b |
**Description**: Two init.sql edits, landed in DIFFERENT build units to respect ordering: (A) **CREATE `work_time_projection`** (employee_id TEXT, date DATE, intervals JSONB, manual_hours NUMERIC(8,4), version BIGINT, updated_at; unique (employee_id, date)) — lands WITH/BEFORE TASK-5603 (the repo needs it). (B) **DROP `timer_sessions`** + its indexes — co-lands in the SAME build unit as TASK-5605's code removal, AFTER a grep-zero validation that no code references `timer_sessions`/`TimerSessionRepository`. Regenerate db-schema.md (net +1 −1 = unchanged). High-risk (schema migration) → Step 5a per-task Codex override applies.
**Validation Criteria**:
- [ ] (A) work_time_projection created with unique (employee_id, date); guarded ALTER for legacy-DB upgrade; merged before/with TASK-5603
- [ ] (B) timer_sessions dropped ONLY after grep-zero on timer_sessions/TimerSessionRepository; co-landed with TASK-5605; build green after the combined unit
- [ ] db-schema.md regenerated to match
**Files Changed**: `docker/postgres/init.sql`, `docs/generated/db-schema.md`

---

### TASK-5603 — Work-time persistence: repository + save path + GET (with per-employee norm)
| Field | Value |
|-------|-------|
| **ID** | TASK-5603 |
| **Status** | complete |
| **Agent** | Backend API (extended into Infrastructure, cross-domain authorized) |
| **Components** | Infrastructure (WorkTimeProjectionRepository), Backend.Api (SkemaEndpoints) |
| **KB Refs** | ADR-018 D13, ADR-016 D5b, ADR-022/023 (EmploymentProfileResolver), PAT-005 |
**Description**: `WorkTimeProjectionRepository` with `(conn,tx)` overloads + latest-wins upsert keyed (employee_id, date). Extend `SaveSkemaRequest` with a `WorkTime` block (per-day intervals + manualHours), handled on a dedicated path (NOT the project-entry classifier). **Event sourcing (ADR-018 D3/D13)**: within the save transaction, ENQUEUE a `WorkTimeRegistered` event via `IOutboxEnqueue` (mirroring the existing Skema pattern, SkemaEndpoints.cs:524 enqueue-then-projection) BEFORE upserting `work_time_projection`; the projection `version` carries the event ordering (outbox_id/version) so read-your-write and replay/backfill are consistent. The projection write is the synchronous in-tx read-your-write surface (ADR-018 D13); the canonical event is the source of truth for replay. GET `/api/skema/{employeeId}/month` returns NEW `arrivalDepartures` (interval list) + `manualHours` + a per-day `norm` array. Norm = `WeeklyNormHours × part_time_fraction / 5` (weekends 0) via `IEmploymentProfileResolver.GetByEmployeeIdAtAsync(employeeId, day)` + `ConfigResolutionService`; okVersion via `OkVersionResolver.ResolveVersion(day)`; ANNUAL_ACTIVITY → blank norm. Employee-scoped auth identical to existing save.
**Validation Criteria**:
- [ ] `WorkTimeRegistered` enqueued via IOutboxEnqueue in the SAME tx as the projection upsert (event-then-projection); replay/backfill rebuilds work_time_projection; publisher-stalled read-your-write holds
- [ ] Intervals + manualHours persist and rehydrate after reload/month-switch/approval
- [ ] Save path is dedicated work-time, not routed through the non-absence→project classifier (useSkema.ts:70 contract)
- [ ] GET returns arrivalDepartures + manualHours + per-day norm; norm correct for full-time AND part-time (0.8) employee; blank for ANNUAL_ACTIVITY
- [ ] okVersion resolved per day via OkVersionResolver (2026-04-01 switch honored)
- [ ] Norm resolution is PURE READ (ConfigResolutionService + IEmploymentProfileResolver); no rule-engine HTTP call or rule logic added to the GET path (P2 boundary)
- [ ] RequireAuthorization present; employee-scoped
**Files Changed**: `src/Infrastructure/**/WorkTimeProjectionRepository.cs`, `src/Backend/**/Endpoints/SkemaEndpoints.cs`, `src/Backend/**/Contracts/SaveSkemaRequest.cs`

---

### TASK-5604 — Allocation reconciliation HARD gate at employee-approve
| Field | Value |
|-------|-------|
| **ID** | TASK-5604 |
| **Status** | complete |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | Backend.Api (ApprovalEndpoints), reads work_time_projection + time_entries_projection |
| **KB Refs** | ADR-012 (approval flow), ADR-018 D13 |
**Description**: Add a second deterministic validation to `employee-approve` (~ApprovalEndpoints.cs:647-795), reusing the existing per-period time-entry load (~:715). Per day: worked = period+manual hours (work_time_projection); allocated = Σ time entries WHERE ActivityType='NORMAL' AND TaskId IS NOT NULL (matches grid predicate; excludes historical timer-origin). Round both to 2 decimals; balanced iff equal (tolerance < 0.005, ONE shared constant). BLOCK approval (422) if ANY day unbalanced in either direction. The 422 carries a discriminator (`kind:"allocation"`, `unbalancedDays:[{date,worked,allocated,direction}]`) so it is distinguishable from the existing coverage 422. NOT rule-engine logic (P2). Coexists with the existing workday-coverage check (both must pass).
**Validation Criteria**:
- [ ] Approval blocked (422, discriminated) when any day worked≠allocated beyond tolerance, both directions
- [ ] Day with 3t worked + 4,4t sick approves once 3t allocated (absence excluded)
- [ ] Tolerance prevents false blocks (7,40 vs 7,4 passes); historical ActivityType="timer" excluded from allocated
- [ ] A NORMAL entry with NULL TaskId is excluded from the gate's allocated total — identical to the grid predicate (allowlist parity)
- [ ] Existing coverage check still enforced; reconciliation reads projections only (no events, no rule engine)
**Files Changed**: `src/Backend/**/Endpoints/ApprovalEndpoints.cs`

---

### TASK-5605 — Timer retirement (backend write path)
| Field | Value |
|-------|-------|
| **ID** | TASK-5605 |
| **Status** | complete |
| **Agent** | Backend API + Infrastructure (cross-domain authorized) |
| **Components** | Backend.Api (TimerEndpoints, SkemaEndpoints GET, Program.cs DI/routes), Infrastructure (TimerSessionRepository) |
| **KB Refs** | ADR-026 (timer events), DEP-004 (endpoint registry), ADR-018 D3 |
**Description**: Remove the three `/api/timer/*` endpoints, `TimerSessionRepository`, and DI/route registration (Program.cs:53,304). In SkemaEndpoints GET remove the FULL timer surface: the `TimerSessionRepository timerRepo` parameter (SkemaEndpoints.cs:111), the active-timer read (~:217-229), AND the `timerSession` field in the returned object (~:264) — leave no dangling unused param/field (Constraint Validator dead-code check). RETAIN `TimerCheckedIn`/`TimerCheckedOut` event classes + EventSerializer registrations (deserialize-only; see TASK-5601). Historical timer-origin `TimeEntryRegistered` rows left untouched (P6). Confirm no payroll/wage-type logic keys off ActivityType="timer" before removal (PeriodCalculationService / PayrollMappingService check). **Build-order**: this task's repo/caller removal MUST co-land in the same build/merge unit as TASK-5602's `DROP TABLE timer_sessions` (build verified only after both) — dropping the table while the repo still reads it, or deleting the repo while SkemaEndpoints still injects it, breaks runtime/build respectively.
**Validation Criteria**:
- [ ] /api/timer/* endpoints + TimerSessionRepository + GET timerSession block removed; DI/routes cleaned
- [ ] TimerCheckedIn/Out deserialization retained (replay/backfill green)
- [ ] No payroll logic depends on ActivityType="timer" (verified)
- [ ] `dotnet build` clean; no dangling references
**Files Changed**: `src/Backend/**/Endpoints/TimerEndpoints.cs` (delete), `src/Infrastructure/**/TimerSessionRepository.cs` (delete), `src/Backend/**/Endpoints/SkemaEndpoints.cs`, `src/Backend/**/Program.cs`

---

### TASK-5606 — Frontend: three-row redesign + persistence wiring + reconciliation UI + timer removal
| Field | Value |
|-------|-------|
| **ID** | TASK-5606 |
| **Status** | complete |
| **Agent** | UX |
| **Components** | frontend (SkemaPage, SkemaGrid, useSkema, types, new summary card; remove TimerControl/useTimer) |
| **KB Refs** | ADR-011 (design system), ADR-012, DEP-004 |
**Description**: Rename rows to "Tilføj periode" (interval dialog, = old Arbejdstid) + new "Tilføj timer" numeric row + "Diff. fra normtid" (= old Difference). Danish comma parse (`7,4`; current parseFloat at SkemaGrid.tsx:203 fails). Diff = (period+manual) − per-day norm from GET. Debounced work-time save via the dedicated path + flush before approve. Add "Ikke fordelt" computed row (per-day + month; green/amber/red via the ONE shared tolerance = gate-accept set) and "Fordeling af arbejdstid" summary card (allocated-of-worked % + per-project breakdown). Handle the allocation 422 discriminator: extend `ApprovalValidationError` to a discriminated union; both `useSkema` handlers (employeeApprove + submitAndApprove) branch on it; `formatApprovalValidationError` renders directional Danish messages. Remove `TimerControl`, `useTimer`, timer-sync effects (SkemaPage:151-192), `timerHoursToday`/`showTimerWarning`/`checkInClientTime`, `TimerSessionEntry`/`timerSession` types, unused `toTimeString`/`toWorkInterval`.
**Validation Criteria**:
- [ ] Three rows render; persisted values survive reload/month-switch/approval
- [ ] `7,4` parses; diff uses GET per-day norm; part-time shows lower norm
- [ ] Ikke fordelt row + summary card correct; green state == gate-accept set (shared tolerance)
- [ ] Allocation 422 renders styled day-by-day Danish error via BOTH approve paths (never raw JSON)
- [ ] All timer UI/types/helpers removed; frontend builds
- [ ] Fix the submit-path okVersion imprecision (useSkema.ts:144 `year>=2026?OK26:OK24`) to use the period-start month (switch is 2026-04-01), so a Jan–Mar 2026 month submits OK24 not OK26 (P4 > P9; we touch this file anyway)
**Files Changed**: `frontend/src/pages/SkemaPage.tsx`, `frontend/src/components/SkemaGrid.tsx`, `frontend/src/hooks/useSkema.ts`, `frontend/src/types.ts`, new summary-card component; delete `TimerControl.tsx`, `useTimer.ts`

---

### TASK-5607 — Tests
| Field | Value |
|-------|-------|
| **ID** | TASK-5607 |
| **Status** | complete |
| **Agent** | Test & QA (extended into frontend test files `frontend/src/**/__tests__/**`, cross-domain authorized) |
| **Components** | tests/**, frontend vitest |
| **KB Refs** | — |
**Description**: Backend D-tests: work-time persistence round-trip + approval-survival; reconciliation gate (under, over, tolerance, absence-excluded, weekend-worked, build-order with no work_time row); norm resolution full + part-time + ANNUAL_ACTIVITY-blank; OkVersionResolver per-day; TimerCheckedIn/Out deserialization retained. Frontend vitest: Danish comma parse, 422 discriminated-union rendering, Ikke fordelt states. Remove timer endpoint/repo behavioral tests (TxContractTests TimerSessionRepository cases, TimerAtomicTests); KEEP serializer round-trip tests for timer events.
**Validation Criteria**:
- [ ] All new D-tests pass; removed timer tests cleaned (not left failing); serializer round-trip retained
- [ ] Frontend vitest green
**Files Changed**: `tests/**`, `frontend/src/**/__tests__/**`

---

### TASK-5608 — Docs: ADR-028 + DEP-004 + ADR-026 note + db-schema regen + QUALITY (ORCHESTRATOR-OWNED)
| Field | Value |
|-------|-------|
| **ID** | TASK-5608 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Components** | docs/knowledge-base, docs/generated, docs/QUALITY.md |
| **KB Refs** | new ADR-028, DEP-004, ADR-026 |
**Description**: Author ADR-028 (self-recorded work-time persistence + allocation reconciliation gate + timer retirement w/ event-type retention). Update DEP-004 (remove timer endpoints; add work-time save + GET fields). Add ADR-026 note: timer events retired-but-retained. Regenerate db-schema.md (with TASK-5602). Update QUALITY.md.
**Validation Criteria**:
- [ ] ADR-028 authored + INDEX.md updated; DEP-004 + ADR-026 updated; db-schema.md current; QUALITY.md updated
**Files Changed**: `docs/knowledge-base/**`, `docs/generated/db-schema.md`, `docs/QUALITY.md`

---

## Phase Dependencies
Backend tasks 5603 / 5604 / 5605 all touch `SkemaEndpoints.cs` and have real data dependencies, so
they are SEQUENCED (not parallel) to avoid merge conflicts and respect ordering. Build verified after
each phase.
- **Phase 1**: TASK-5601 (Data Model — `WorkTimeRegistered` + serializer) + TASK-5602(A) (CREATE `work_time_projection`). Independent; can run together.
- **Phase 2**: TASK-5603 (work-time persistence: event-in-tx + projection + GET incl. per-day norm). Depends on Phase 1. Merged + build-green BEFORE Phase 3.
- **Phase 3**: TASK-5604 (allocation hard gate). Depends on TASK-5603's `work_time_projection` existing and being populated. Reads projections only.
- **Phase 4**: TASK-5605 + TASK-5602(B) as ONE build unit (timer code removal + `DROP timer_sessions` after grep-zero validation). Independent of 5603/5604 logic but shares `SkemaEndpoints.cs` — sequenced last among backend to avoid conflicts.
- **Phase 5**: TASK-5606 (UX) — after all backend contracts (GET shape, 422 discriminator) are final.
- **Phase 6**: TASK-5607 (Test & QA) — after implementation.
- **Phase 7**: TASK-5608 (docs) + Orchestrator `dotnet build && dotnet test` + frontend build + Step 7a.

## Legal & Payroll Verification
| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | norm resolution = WeeklyNorm×fraction/5 |
| Wage type mappings produce correct SLS codes | N/A | no wage-type change |
| Overtime/supplement calculations deterministic | N/A | — |
| Absence effects correct | pending | absences excluded from allocation gate |
| Retroactive recalculation stable | pending | timer events retained for replay; WorkTime not rule-engine input |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes (both lenses) |
| **Sprint-start commit** | `df036c3` |
| **Command** | `codex review "..."` (prompt-alone, uncommitted/staged diff) + internal Reviewer Agent on `git diff --cached` |
| **Review Cycles** | 2 per lens |
| **Findings** | cycle 1: Codex 1B/1W; Reviewer 0B/0W/2N. cycle 2: both clean |
| **Resolution** | all resolved + regression-tested |

### Findings

- **BLOCKER (P1/P7)** — Codex — `SkemaEndpoints.cs` POST `/save` — work-time save did not validate `workTime[].date` against the requested month; a save for an unlocked month could include a date from an already-approved/locked month and overwrite its `work_time_projection`, bypassing the period lock. **Fixed** (cycle 2 verified) — up-front guard rejects out-of-month work-time dates (400 `work_time_date_out_of_range`) before the tx; regression test `SkemaWorkTimeDateRangeGuardTests` (2 Docker D-tests).
- **WARNING (P9)** — Codex — `SkemaGrid.tsx` — "Ikke fordelt" month-total netted days, showing green when offsetting days canceled (contradicting the per-day gate). **Fixed** (cycle 2 verified) — total is green only when `allDaysBalanced` (every gated day individually balanced); else `allocUnbalanced`; 2 frontend vitest added.
- **NOTE (P9)** — Reviewer — dead `arrivalDepartures` type field in `types.ts`. **Removed.**
- **NOTE (P9)** — Reviewer — AllocationSummary card uses minute precision vs seconds in gate/grid (cosmetic for HH:mm input). Left as optional consolidation follow-up.

Both lenses cycle 2: **clean**. Cycle cap (2/lens) respected.

## Legal & Payroll Verification (update)
- Historical timer-origin `TimeEntryRegistered` rows retained; verified no payroll/wage-type/rule logic branches on `ActivityType="timer"` (Step 7a P6).
- Absences excluded from the allocation gate (verified by `AllocationGateTests` sick-day case).
- WorkTime is NOT a rule-engine input; timer events retained deserializable → retroactive replay stable.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 552 | all passing |
| Frontend vitest | 128 (20 files) | all passing |
| Docker-gated D-tests (new S56) | 15 | all passing (live Postgres testcontainer) |
| Solution build | — | `dotnet build StatsTid.sln` 0 errors (38 pre-existing warnings) |

New S56 D-tests: WorkTimeProjectionAtomic (4), WorkTimeBackfill (2), AllocationGate (7), SkemaWorkTimeDateRangeGuard (2). New unit: Sprint56DailyNorm (10 cases) + WorkTimeRegistered serializer round-trip. New vitest: locale comma parse, allocation classify, 422 union rendering, SkemaGrid month-total balance.

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 8 (TASK-5601…5608) |
| Constraint Violations | 0 (self-checked per task) |
| Plan Review (Step 0b) | Codex 3B/2W + Reviewer 0B/3W/4N (cycle 1) → cycle 2 clean |
| Step 7a Findings | Codex 1B/1W + Reviewer 0B/0W/2N (cycle 1) → cycle 2 clean |
| Re-dispatches | 0 (cross-domain dependencies — ProjectionBackfillService, TimerSession model — resolved by Orchestrator directly, not re-dispatch) |
| First-Pass Rate | 100% (8/8 accepted without agent re-dispatch) |

## Sprint Retrospective

**What went well**: Refinement's dual-lens review caught the architecture issues early (event-in-tx, timer non-isolation, append-only event retention) so agents implemented cleanly first-pass. Codex (intermittently hung mid-session) recovered and earned its keep at Step 0b (3 plan BLOCKERs) and Step 7a (the lock-bypass BLOCKER the internal Reviewer missed) — clean lens complementarity. Docker was available so all D-tests actually executed.

**What to improve**: Codex CLI hung twice mid-session (0 CPU / 0 output) before recovering — wasted ~18 min; worth a watchdog. The ROADMAP rolling-detail lag (frontier ~S30 vs git S55) is real documentation debt flagged in the entropy scan — a future reconciliation sprint should resync it.

**Knowledge produced**: ADR-028 (work-time persistence + allocation gate + timer retirement); DEP-004 + ADR-026 + db-schema.md updated.
