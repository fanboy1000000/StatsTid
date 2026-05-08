# Sprint 26 — Phase 4c.5: Atomic Outbox Final Sweep

| Field | Value |
|-------|-------|
| **Sprint** | 26 |
| **Status** | planned |
| **Start Date** | 2026-05-08 |
| **End Date** | TBD |
| **Orchestrator Approved** | no |
| **Build Verified** | no |
| **Test Verified** | no |
| **Sprint-start commit base** | `b256a51` (S25 sprint close) |

## Sprint Goal

Close the atomic-outbox propagation workstream entirely. Sweep the remaining 11 post-commit `eventStore.AppendAsync` sites (Skema 3 + Admin 6 + Time 2) into the ADR-018 D3 atomic in-tx pattern + close the 2 silent-state-change Overtime endpoints by adding new event types AND repository overload. Also adjudicate the ADR-018 D6 stream-naming drift (doc-only retabulate matching code reality, since renaming streams would break replay determinism per ADR-016 D10). Refinement at `.claude/refinements/REFINEMENT-s26-phase-4c5.md` (cycles 1+2 reviewed; 1 cycle-2-round-2 BLOCKER absorbed inline; READY).

**Cycle-cap discipline (per AGENTS.md L371/L455 + `feedback_step7a_cycle_cap_discipline.md`):** Step 0b: 2 BLOCKER-fix cycles per lens. Step 7a: same. After cycle 2 on either lens halt and prompt user.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | ADR-018 D6 stream-ownership table at `docs/knowledge-base/decisions/ADR-018-...md:265` confirmed; ADR-019 (S25) at `:267` (post-INDEX append) confirmed; no orphan KB references in scope. TASK-2601 will retabulate ADR-018 D6 to match code reality. |
| Pattern compliance spot-check | CLEAN | 11 remaining post-commit `eventStore.AppendAsync` sites verified at SkemaEndpoints L369/L393/L439, AdminEndpoints L143/L211/L322/L401/L559/L669, TimeEndpoints L71/L163. 2 silent-state-change sites at OvertimeEndpoints L266/L293 verified. AdminEndpoints L505/L628 already wrap role_assignments + role_assignment_audit in `BeginTransactionAsync` (cycle-2 round 2 verification — different shape from inline-SQL endpoints L143/etc). |
| Orphan detection | CLEAN | No unused S25 files post-cleanup (`ef9ec91` already absorbed `ConcurrencyTestSchema.cs`). |
| Documentation drift | CLEAN | S25 sprint close (`b256a51`) recorded ROADMAP + MEMORY. Phase 4c.5 is now starting. ADR-018 D6 stream-name drift is the documented entry point for TASK-2601. |
| S25 lessons absorbed | n/a | S24's "commit Phase 1 BEFORE dispatching Phase 2 worktrees" applied (R7); S25's "convergent BLOCKERs from multi-lens review have higher signal" applied (refinement cycles 1+2 weighted convergent findings). |
| Quality grade review | deferred | Will update at sprint-end. |

**Test baseline (post-S25):** 525 unit + 35 plain regression + 129 Docker-gated + 88 frontend vitest = 777 total. S26 expected addition: ~6-8 Docker-gated D-tests (Skema multi-event rollback + order-preservation + Admin atomic per sub-shape + Time atomic + Overtime atomic with new-event emission + 2 TxContractTests for new overloads). No frontend changes. Target ~785 total.

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY — P3 (event sourcing/auditability) atomic-tx invariants on remaining 11+2 endpoints; P5 (integration isolation) on Skema multi-event single-tx semantics; ADR-018 D6 amendment. |
| **External Codex** | invoked 2026-05-08 (refinement cycle 1: 3B/2W/1N → cycle 2: 0B/2W/1N → cycle 2 round 2 absorbed inline) |
| **Internal Reviewer** | invoked 2026-05-08 (refinement cycle 1: 1B/4W/5N → cycle 2: 1B/2W/2N → cycle 2 round 2 absorbed inline) |
| **Cycle cap** | 2 BLOCKER-fix cycles per lens (per WORKFLOW.md / `feedback_step7a_cycle_cap_discipline.md`). All cycle-1 + cycle-2 BLOCKERs absorbed in refinement. **READY for Step 1 decompose.** |

### Findings (cycle 1)

**Codex (3 BLOCKERs, 2 WARNINGs, 1 NOTE):**
- B1: Site count "13 sites" wrong; actual is 11 AppendAsync + 2 silent-state-change.
- B2: Refinement assumed `OvertimePreApprovalRepository.UpdateStatusAsync(conn, tx)` exists from S24 — verified false; only `CreateAsync(conn, tx)` exists.
- B3: Overtime task expanded to "status + audit + outbox" but no `overtime_pre_approval_audit` table exists; would require new schema migration.
- W1: TASK-2601 grep scope too narrow (Backend.Api/Endpoints/ only vs cross-service ADR-018 D6).
- W2: Admin sites not uniform; org/user create-update vs role grant/revoke have different shapes; 1 representative D-test too weak.
- NOTE: TimerAtomicTests.cs:45/89 breadcrumbs become stale once TASK-2601 lands.

**Reviewer (1 BLOCKER, 4 WARNINGs, 5 NOTEs):**
- B1 (convergent with Codex B2): UpdateStatusAsync(conn, tx) overload doesn't exist.
- W1: AdminEndpoints inline-SQL+tx is novel pattern; needs prototype-first.
- W2: Admin endpoints excluded from ADR-019 row-version+ETag; deferral rationale missing.
- W3: CheckAndAdjustAsync failure-path read on separate connection diverges from outer tx snapshot.
- W4: Missing per-stream FIFO + grep-zero-hits ACs.
- N1: Quota-breach behavior change (single-tx wrap rolls back ALL events on breach).
- N2: DEP-003 entry count not pre-verified.
- N3: TimeEndpoints/SkemaEndpoints are Pattern C (no audit), not Pattern B as refinement implied.
- N4: ADR-018 D6 amendment scope: 4 additional surfaces (Approval/Compliance/Balance/Overtime) also drift.
- N5: TASK-2601 doesn't depend on Phase 2 worktrees.

### Resolution (cycle 2 plan rewrite)

Refinement updated at `.claude/refinements/REFINEMENT-s26-phase-4c5.md`:
- Site count corrected to "11 AppendAsync + 2 silent-state-change" with explicit Site Inventory table.
- TASK-2603 expanded to TWO overloads (CheckAndAdjustAsync + UpdateStatusAsync), with failure-path read pinned to (conn, tx) routing per W3.
- TASK-2607 explicitly Pattern C only — NO new audit row, NO new schema migration.
- TASK-2601 grep scope expanded to all of `src/`; covers 4 additional drift surfaces; TimerAtomicTests cleanup folded in.
- TASK-2605 split into 2605a (sequential prototype on L143) + 2605b (remaining 5).
- Assumption #6 added: Admin endpoints excluded from ADR-019 contract; deferral rationale recorded.
- Assumption #11 added: Skema TASK-2604 single-tx rolls back ALL events on quota breach; correct atomicity behavior; clients retry on 422.
- Assumption #12 added: DEP-003 pre-verified at 45 typeof() registrations; TASK-2602 commits with pre/post grep verification.
- Pattern B vs C categorization table added to refinement.
- ACs added: per-stream FIFO D-test, grep-zero-hits report, two Admin sub-shape D-tests, TxContractTests for new overloads.

### Findings (cycle 2)

**Codex (0 BLOCKERs, 2 WARNINGs, 1 NOTE):** confirmed cycle-1 BLOCKERs absorbed; flagged TASK-2603 lingering a-1/a-2 alternatives + TASK-2601 misclassified as "doc-only".

**Reviewer (1 BLOCKER, 2 WARNINGs, 2 NOTEs):**
- **B-NEW**: Assumption #10 + R10 misstate existing AdminEndpoints L505/L628 tx state. Verified at code: both endpoints already have `BeginTransactionAsync` wrapping role_assignments + role_assignment_audit. Refinement implied "no explicit tx" — wrong. Without correction, TASK-2605b agent prompt would have implementers add a duplicate `BeginTransactionAsync` (tx-leak likely). Fix: Assumption #10 + R10 + R7 reworded to reflect existing-tx reality; TASK-2605b scope narrowed (move post-catch eventStore.AppendAsync INSIDE existing try block, no new tx).
- W: TASK-2605a/b serialization breaks Phase 2 4-way parallelism guarantee (R7 wording stale).
- W: TASK-2603 dual-overload size — bundle-vs-split decision needed.
- N: Skema multi-event order-preservation D-test asserts a tautology (bigserial monotonicity is guaranteed); should test publisher delivery order or delete.
- N: Pattern C table missing "no audit-table writes pre-S26 verified by grep" closing sentence.

### Resolution (cycle 2 round 2 — final, applied inline)

- BLOCKER fix: Assumption #10 + R10 corrected to reflect existing tx; R7 expanded to clarify Phase 2 has 3 parallelizable worktrees + TASK-2605a sequential prototype + TASK-2605b dependent on 2605a Reviewer signoff; TASK-2605b scope narrowed.
- Codex W1 (TASK-2603 a-1/a-2 inconsistency): pinned to a-1 only (route failure-path through (conn, tx)); a-2 explicitly rejected (frontend reads currentUsed for 422 display).
- Codex W2 (TASK-2601 misclassification): reclassified to "doc + test-comment cleanup".
- Reviewer W's (TASK-2605a/b serialization clarity, TASK-2603 dual-overload bundle decision) deferred to this SPRINT-26.md task log.
- Reviewer N's (D-test redundancy, Pattern C closing sentence) deferred to this SPRINT-26.md task log.

## Architectural Constraints Verified

- [ ] P1 — Architectural integrity preserved (ADR-018 atomic-outbox D3 propagated to remaining 13 endpoints; no new ADR introduced; ADR-018 D6 amendment is doc-correction matching code, not architectural decision change)
- [ ] P3 — Event sourcing append-only semantics respected; new event types (`OvertimePreApprovalApproved/Rejected`) follow PAT-004 (DomainEventBase + actor tracking); EventSerializer registration verified pre/post (45 → 47)
- [ ] P5 — Integration isolation and delivery guarantees (atomic-tx contract closes residual partial-failure window across the entire write surface)
- [ ] P7 — Security and access control (no scope changes; AdminEndpoints retain existing role-based authorization)
- [ ] P8 — CI/CD enforcement (build clean, all tests pass, +6-8 D-tests, +2 TxContractTests)
- [ ] P9 — Usability and UX (Skema TASK-2604 single-tx rollback on quota breach is correct atomicity; clients re-submit on 422; no new UI required since 422 retry path exists)

Not directly affected: P2, P4, P6.

## Task Log

### TASK-2601 — ADR-018 stream-naming amendment + test/source-comment cleanup

| Field | Value |
|-------|-------|
| **ID** | TASK-2601 |
| **Status** | planned |
| **Agent** | Orchestrator-direct (small task exception; doc + 4-line code-comment cleanup) |
| **Components** | Knowledge Base, Tests (comments), Source (comments) |
| **KB Refs** | ADR-018 D6 (stream-ownership table) |
| **Phase** | Phase 1 (sequential — independent of Phase 2 worktrees architecturally; sequencing is conservative for clean commit ordering only) |

**Description**: Adjudicate the documented stream-name drift (per ROADMAP L320). Code rename forbidden — replay-determinism preserved per ADR-016 D10 (changing stream_id literals would invalidate every existing event in the stream). Doc-correction matches code as the canonical source.

**Sub-deliverables**:
- (a) Grep `stream_id` literals across **all of `src/`** (Backend.Api/Endpoints + Payroll + External + Orchestrator + RuleEngine — even if some emit no streams today, ADR-018 D6 is a cross-service ownership table).
- (b) Audit covers 4 additional drift surfaces beyond the originally-noted Skema/Time/Timer: ApprovalEndpoints, ComplianceEndpoints, BalanceEndpoints, OvertimeEndpoints (`overtime-preapproval-*` actual vs `overtime-pre-approval-*` documented at ADR-018:265).
- (c) Update ADR-018 D6 stream-ownership table to match code reality. Add a footnote to the Review Cycles section documenting the cycle 6 amendment ("S26 / TASK-2601: D6 stream-ownership table amended to match code reality; original `timer-session-*` / `time-entry-*` / `skema-*` / `overtime-pre-approval-*` documented stream names did not match emitted code; renaming would break replay determinism per ADR-016 D10").
- (d) Clean up `tests/StatsTid.Tests.Regression/Outbox/TimerAtomicTests.cs:45-46` and `:89-90` Phase 4c.5 carry-forward breadcrumbs (no-op once TASK-2601 lands; comments become stale).
- (e) Clean up `src/Backend/StatsTid.Backend.Api/Endpoints/TimerEndpoints.cs:60-61` and `:135-136` matching source comments.

**Validation Criteria**:
- [ ] ADR-018 D6 stream-ownership table updated to match code reality
- [ ] Grep report committed in commit message: every drift identified, every line reference cited
- [ ] **Zero hits** in test files for documented-but-not-code stream names (`timer-session-*` / `time-entry-*` / `skema-*` / `entitlement-*` literals)
- [ ] TimerAtomicTests + TimerEndpoints comments cleaned up
- [ ] Footnote added to ADR-018 Review Cycles section
- [ ] No code stream_id literals changed (replay-determinism preserved)

**Files Changed** (anticipated):
- `docs/knowledge-base/decisions/ADR-018-transactional-outbox-and-row-version-optimistic-concurrency.md`
- `tests/StatsTid.Tests.Regression/Outbox/TimerAtomicTests.cs`
- `src/Backend/StatsTid.Backend.Api/Endpoints/TimerEndpoints.cs`

---

### TASK-2602 — Net-new event types: OvertimePreApprovalApproved + OvertimePreApprovalRejected

| Field | Value |
|-------|-------|
| **ID** | TASK-2602 |
| **Status** | planned |
| **Agent** | Data Model (scope: `src/SharedKernel/Events/` + `src/Infrastructure/EventSerializer.cs`) |
| **Components** | SharedKernel, Infrastructure |
| **KB Refs** | PAT-004 (DomainEventBase + actor tracking), DEP-003 (EventSerializer must register all event types) |
| **Phase** | Phase 1 (sequential) |

**Description**: Add 2 net-new event types to the canonical event registry for OvertimePreApproval lifecycle. Required by TASK-2607 (Overtime atomic + new-event emission).

**Event shapes**:
```csharp
public sealed class OvertimePreApprovalApproved : DomainEventBase
{
    public required Guid PreApprovalId { get; init; }
    public required string EmployeeId { get; init; }
    public required string ApprovedBy { get; init; }
    public string? Reason { get; init; }
}

public sealed class OvertimePreApprovalRejected : DomainEventBase
{
    public required Guid PreApprovalId { get; init; }
    public required string EmployeeId { get; init; }
    public required string RejectedBy { get; init; }
    public string? Reason { get; init; }
}
```

DomainEventBase contributes `EventId, OccurredAt, ActorId, ActorRole, CorrelationId`. Total registered event types in EventSerializer: 45 → 47.

**Validation Criteria**:
- [ ] 2 event types added under `src/SharedKernel/Events/`
- [ ] EventSerializer registers both types (verified in commit message via pre-grep + post-grep typeof() count: 45 → 47)
- [ ] DEP-003 entry count line in MEMORY.md updated at sprint close
- [ ] No read-path code currently consumes these event types (verified by grep — pre-S26 state read from projection table; new events are forward-only audit metadata for Phase 4d's versioned-history work)

**Files Changed**:
- `src/SharedKernel/Events/OvertimePreApprovalApproved.cs` (new)
- `src/SharedKernel/Events/OvertimePreApprovalRejected.cs` (new)
- `src/Infrastructure/EventSerializer.cs`

---

### TASK-2603 — Repository plumbing: 2 new (conn, tx) overloads

| Field | Value |
|-------|-------|
| **ID** | TASK-2603 |
| **Status** | planned |
| **Agent** | Data Model (scope: 2 repository files only) |
| **Components** | Infrastructure |
| **KB Refs** | ADR-018 D3 (atomic state-change), S24 TASK-2206 (overload pattern) |
| **Phase** | Phase 1 (sequential — required by Phase 2 TASK-2604 + TASK-2607) |

**Description**: Two new (conn, tx) overloads required by Phase 2 worktrees. Single task envelope per refinement; ship in TWO separate commits for clean Reviewer attribution per Reviewer cycle-2 W (split-decision deferred here).

**Sub-deliverable (a) — `EntitlementBalanceRepository.CheckAndAdjustAsync(conn, tx, ...)` overload + `GetByEmployeeAndTypeAsync(conn, tx, ...)` overload**:
- Preserve atomic-quota-check single-UPDATE semantics (TOCTOU-safe via `UPDATE ... WHERE used_days + @delta <= @quota RETURNING used_days`) inside supplied tx.
- Add `GetByEmployeeAndTypeAsync(conn, tx, ...)` overload — required because the failure-path fallback at `EntitlementBalanceRepository.cs:117` reads current Used for the 422-response payload that the frontend consumes; routing through the same tx ensures snapshot consistency under RepeatableRead.
- Self-managed v1 overloads stay unchanged (legacy callers).

**Sub-deliverable (b) — `OvertimePreApprovalRepository.UpdateStatusAsync(conn, tx, ...)` overload**:
- Mirror `CreateAsync(conn, tx, ...)` shape at `OvertimePreApprovalRepository.cs:70-93`. ~15 LOC.
- Self-managed v1 at L95-110 stays unchanged.

**Commit shape**: 2 commits (one per repo) for clean Reviewer per-overload attribution. Both commits ship in Phase 1 before Phase 2 dispatch.

**Validation Criteria**:
- [ ] `EntitlementBalanceRepository.CheckAndAdjustAsync(NpgsqlConnection, NpgsqlTransaction, ...)` overload added
- [ ] `EntitlementBalanceRepository.GetByEmployeeAndTypeAsync(NpgsqlConnection, NpgsqlTransaction, ...)` overload added (failure-path routing per cycle-2 round 2 W3)
- [ ] `OvertimePreApprovalRepository.UpdateStatusAsync(NpgsqlConnection, NpgsqlTransaction, ...)` overload added (mirrors CreateAsync L70-93)
- [ ] Self-managed v1 overloads unchanged
- [ ] Atomic-quota-check single-UPDATE semantics preserved (verified by reading the SQL shape)
- [ ] dotnet build clean

**Files Changed**:
- `src/Infrastructure/StatsTid.Infrastructure/EntitlementBalanceRepository.cs`
- `src/Infrastructure/StatsTid.Infrastructure/OvertimePreApprovalRepository.cs`

---

### TASK-2604 — Skema atomic (3 sites + multi-event single-tx)

| Field | Value |
|-------|-------|
| **ID** | TASK-2604 |
| **Status** | planned |
| **Agent** | Backend API + Data Model (cross-domain authorized) |
| **Components** | Backend.Api |
| **KB Refs** | ADR-018 D3 (atomic single-tx), ADR-018 D5 (per-stream FIFO) |
| **Phase** | Phase 2 (parallel via worktrees — depends on Phase 1 commits) |

**Description**: Refactor SkemaEndpoints save handler so the entire `request.Entries[]` + `request.Absences[]` + `EntitlementBalanceAdjusted[]` loop wraps in a single tx. Each event becomes one `IOutboxEnqueue.EnqueueAsync(conn, tx, ...)` call within that tx; `EntitlementBalanceRepository.CheckAndAdjustAsync(conn, tx, ...)` (TASK-2603(a)) consumed within the same tx. Single commit at end → all-or-nothing.

**Sites migrated**: SkemaEndpoints.cs L369 (TimeEntryRegistered loop) + L393 (AbsenceRegistered loop) + L439 (EntitlementBalanceAdjusted).

**Behavior change called out** (refinement Assumption #11 / R9): pre-S26, a quota breach at absence #N silently skipped that absence's balance adjustment but kept time entries 1..M committed; post-S26, the entire Skema save rolls back atomically (no partial commit). This is correct atomicity per ADR-018 D3. Frontend already handles 422 retry; no new UX. Document in commit message body explicitly.

**Validation Criteria**:
- [ ] All 3 Skema sites migrated to atomic in-tx outbox enqueue (`IOutboxEnqueue.EnqueueAsync(conn, tx, ...)` before `tx.CommitAsync`)
- [ ] CheckAndAdjustAsync consumed within the same tx (no separate connection)
- [ ] Behavior change documented in commit message body
- [ ] dotnet build clean
- [ ] 35 plain regression tests still pass

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs`

---

### TASK-2605a — Admin atomic prototype on OrganizationCreated (L143)

| Field | Value |
|-------|-------|
| **ID** | TASK-2605a |
| **Status** | planned |
| **Agent** | Backend API |
| **Components** | Backend.Api |
| **KB Refs** | ADR-018 D3 |
| **Phase** | Phase 2 (sequential prototype — runs in parallel with TASK-2604/2606/2607 in its own worktree, but TASK-2605b WAITS for 2605a Reviewer signoff) |

**Description**: Prototype the inline-SQL + `BeginTransactionAsync` + `IOutboxEnqueue.EnqueueAsync(conn, tx, ...)` pattern on OrganizationCreated (`AdminEndpoints.cs:143`). This is the FIRST inline-SQL+tx+outbox case across the codebase; existing Phase-2 atomic patterns (S22+S24+S25) all wrap Repository calls. Verify the pattern works end-to-end before TASK-2605b parallelizes the remaining 3 sites with the same shape.

**Sites migrated**: AdminEndpoints.cs L143 (OrganizationCreated, single endpoint).

**Validation Criteria**:
- [ ] L143 OrganizationCreated wrapped in new explicit `BeginTransactionAsync` + try/commit/rollback
- [ ] `IOutboxEnqueue.EnqueueAsync(conn, tx, ...)` placed BEFORE `tx.CommitAsync(ct)`
- [ ] Inline INSERT into organizations table participates in the new tx
- [ ] dotnet build clean
- [ ] 35 plain regression tests still pass
- [ ] Reviewer signoff REQUIRED before TASK-2605b dispatch (mandatory per cycle-2 round 2 fix)

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` (L143 only)

---

### TASK-2605b — Admin atomic remaining 5 sites

| Field | Value |
|-------|-------|
| **ID** | TASK-2605b |
| **Status** | planned (depends on TASK-2605a + Reviewer signoff) |
| **Agent** | Backend API |
| **Components** | Backend.Api |
| **KB Refs** | ADR-018 D3 |
| **Phase** | Phase 2-b (sequential after 2605a) |

**Description**: Apply the TASK-2605a prototype pattern to the remaining 5 Admin sites. **Two distinct sub-shapes** (cycle-2 fix of W2):

**Sub-shape (i) — org/user create-update (3 sites)**: L211 (OrganizationUpdated) + L322 (UserCreated) + L401 (UserUpdated). Mirror TASK-2605a exactly: add `BeginTransactionAsync` + try/commit/rollback + `IOutboxEnqueue.EnqueueAsync(conn, tx, ...)` before `tx.CommitAsync`.

**Sub-shape (ii) — role grant/revoke (2 sites)**: L559 (RoleAssigned grant) + L669 (RoleRemoved revoke). These endpoints **already** have explicit `BeginTransactionAsync` at L505 / L628 wrapping role_assignments + role_assignment_audit (verified in cycle-2 round 2 BLOCKER fix). The S26 work is narrower: **move the post-catch `eventStore.AppendAsync` (L559 / L669) INSIDE the existing try block** and convert to `IOutboxEnqueue.EnqueueAsync(conn, tx, ...)` placed BEFORE `tx.CommitAsync`. **Do NOT add a duplicate `BeginTransactionAsync`** — the existing one already encompasses audit + state writes correctly.

**Validation Criteria**:
- [ ] Sub-shape (i): 3 sites wrapped in new explicit tx + outbox enqueue (mirrors 2605a exactly)
- [ ] Sub-shape (ii): 2 sites have outbox enqueue inserted INSIDE existing try block; no duplicate `BeginTransactionAsync` added
- [ ] dotnet build clean
- [ ] 35 plain regression tests still pass
- [ ] Reviewer audit MANDATORY at task close (P3 + P7 trigger)

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/AdminEndpoints.cs` (L211/L322/L401/L559/L669)

---

### TASK-2606 — Time atomic (2 sites)

| Field | Value |
|-------|-------|
| **ID** | TASK-2606 |
| **Status** | planned |
| **Agent** | Backend API |
| **Components** | Backend.Api |
| **KB Refs** | ADR-018 D3 |
| **Phase** | Phase 2 (parallel via worktrees) |

**Description**: Migrate 2 simple single-event sites in TimeEndpoints to atomic in-tx outbox enqueue. Pattern C (state + outbox; no audit table; events carry actor metadata per PAT-004).

**Sites migrated**: TimeEndpoints.cs L71 (TimeEntryRegistered POST `/api/time-entries`) + L163 (AbsenceRegistered POST `/api/absences`).

**Validation Criteria**:
- [ ] L71 + L163 wrapped in new explicit `BeginTransactionAsync` + outbox enqueue + `tx.CommitAsync`
- [ ] dotnet build clean
- [ ] 35 plain regression tests still pass

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs`

---

### TASK-2607 — Overtime atomic + new-event emission (2 sites)

| Field | Value |
|-------|-------|
| **ID** | TASK-2607 |
| **Status** | planned (depends on TASK-2602 + TASK-2603(b)) |
| **Agent** | Backend API |
| **Components** | Backend.Api |
| **KB Refs** | ADR-018 D3, PAT-004, DEP-003 |
| **Phase** | Phase 2 (parallel via worktrees — runs after TASK-2602 + TASK-2603(b) commit) |

**Description**: Refactor OvertimePreApproval approve/reject endpoints to atomic Pattern C (state + outbox; NO new audit row, NO new schema migration — explicit cycle-2 fix of Codex B3 + Reviewer N3 since `overtime_pre_approval_audit` table doesn't exist).

**Sites migrated**: OvertimeEndpoints.cs L266 (PUT `/api/overtime/pre-approval/{id}/approve` → emit `OvertimePreApprovalApproved`) + L293 (PUT `/api/overtime/pre-approval/{id}/reject` → emit `OvertimePreApprovalRejected`).

**Pattern**: parse If-Match (currently absent — keep absent for S26; ADR-019 row-version+ETag explicitly out of scope per refinement Assumption #6) → existing existence + status + scope checks → atomic tx with `OvertimePreApprovalRepository.UpdateStatusAsync(conn, tx, ...)` (TASK-2603(b)) + `IOutboxEnqueue.EnqueueAsync(conn, tx, ...)` of new event before `tx.CommitAsync`.

**Validation Criteria**:
- [ ] L266 (approve) wraps `UpdateStatusAsync(conn, tx, ...)` + `OvertimePreApprovalApproved` outbox enqueue in single tx
- [ ] L293 (reject) wraps `UpdateStatusAsync(conn, tx, ...)` + `OvertimePreApprovalRejected` outbox enqueue in single tx
- [ ] No new audit row added (Pattern C; events carry actor metadata)
- [ ] No new schema migration (NO `overtime_pre_approval_audit` table)
- [ ] dotnet build clean
- [ ] 35 plain regression tests still pass

**Files Changed**:
- `src/Backend/StatsTid.Backend.Api/Endpoints/OvertimeEndpoints.cs`

---

### TASK-2608 — D-test suite expansion

| Field | Value |
|-------|-------|
| **ID** | TASK-2608 |
| **Status** | planned |
| **Agent** | Test & QA (scope: `tests/StatsTid.Tests.Regression/`) |
| **Components** | Tests / Regression |
| **KB Refs** | ADR-018 D3, D5 |
| **Phase** | Phase 3 (sequential — runs after Phase 2) |

**Description**: D-test coverage for the 6 area migrations + 2 new (conn, tx) overloads. All tests `[Trait("Category", "Docker")]` + use `DockerHarness` fixture per S24 convention.

**Test slots (~7-8 D-tests)**:
- Skema multi-event rollback (forced outbox failure → all events + balance + audit roll back together)
- Admin atomic per sub-shape: 1 representative for org/user create-update (2605b sub-shape i) + 1 representative for role grant/revoke (2605b sub-shape ii). Two tests, not one (cycle-2 fix of Codex W2).
- Time atomic (1 representative)
- Overtime approve/reject atomic with new-event emission: 1 test verifies `OvertimePreApprovalApproved` appears in `outbox_events` after approve; 1 test verifies same for reject + verify rollback under forced outbox failure for one of them.
- TxContractTests: 1 for `EntitlementBalanceRepository.CheckAndAdjustAsync(conn, tx, ...)` participates in caller tx; 1 for `OvertimePreApprovalRepository.UpdateStatusAsync(conn, tx, ...)` participates in caller tx.

**NOT added (cycle-2 round 2 fix of Reviewer N1)**: the originally-proposed Skema multi-event order-preservation D-test. Reviewer flagged it asserts a tautology (bigserial monotonicity is guaranteed by `BIGSERIAL PRIMARY KEY`). The actual replay-determinism guarantee is publisher-side (per-stream FIFO at drain) — covered by ADR-018 D5 contract + S22's existing publisher tests. Adding a producer-side test would be redundant signal.

**Validation Criteria**:
- [ ] ~7-8 new D-tests added under `tests/StatsTid.Tests.Regression/Outbox/` and `tests/StatsTid.Tests.Regression/Infrastructure/` (mirroring S24 TASK-2408 + TASK-2401 layout)
- [ ] Each marked `[Trait("Category", "Docker")]`
- [ ] All tests compile clean (Docker runtime gating same as S24)
- [ ] Existing 21 ForcedRollbackHarness tests + 23 TxContractTests + 23 S25 Concurrency tests still pass (regression)

**Files Changed** (anticipated):
- `tests/StatsTid.Tests.Regression/Outbox/SkemaAtomicTests.cs` (new)
- `tests/StatsTid.Tests.Regression/Outbox/AdminAtomicTests.cs` (new — covers both Admin sub-shapes)
- `tests/StatsTid.Tests.Regression/Outbox/TimeAtomicTests.cs` (new)
- `tests/StatsTid.Tests.Regression/Outbox/OvertimeApproveRejectAtomicTests.cs` (new — extends existing OvertimeAtomicTests OR new file)
- `tests/StatsTid.Tests.Regression/Infrastructure/TxContractTests.cs` (extend with 2 new test methods)

---

## Phase Ordering

**Phase 1 (sequential — single-agent steps, commit between each)**:
- TASK-2601 ADR-018 amendment + comment cleanup → commit
- TASK-2602 net-new event types → commit
- TASK-2603 (a) EntitlementBalance overloads → commit
- TASK-2603 (b) OvertimePreApproval overload → commit

**Critical**: commit Phase 1 (TASK-2601 + TASK-2602 + TASK-2603 a/b) BEFORE dispatching Phase 2 worktrees. S24 lesson absorbed (R7).

**Phase 2 (parallel via worktrees, mostly)**:
- TASK-2604 Skema (1 worktree)
- TASK-2605a Admin prototype on L143 (1 worktree, sequential — Reviewer signoff required before 2605b)
- TASK-2606 Time (1 worktree)
- TASK-2607 Overtime (1 worktree)

→ TASK-2605a Reviewer signoff →

- TASK-2605b Admin remaining 5 sites (1 worktree, sequential after 2605a)

Phase 2 yields **4 simultaneously-active worktrees** initially (2604/2605a/2606/2607); 2605b dispatches as a 5th worktree once 2605a clears Reviewer.

**Phase 3 (sequential, runs AFTER all Phase 2 per AGENTS.md L37)**:
- TASK-2608 Test & QA (D-test suite expansion)

**Phase 4 (Orchestrator)**: build/test validation, Constraint Validator on each agent output, Reviewer audits per task (P3 + P5 trigger MANDATORY for atomicity work; TASK-2605b explicit Reviewer requirement), Step 7a Codex review on full sprint diff vs `b256a51`.

**Cycle cap discipline**: 2 BLOCKER-fix cycles per lens at Step 0b and Step 7a. After cycle 2 on either lens, halt and prompt user (per WORKFLOW.md / `feedback_step7a_cycle_cap_discipline.md`).

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | N/A | No rule engine changes |
| Wage type mappings produce correct SLS codes | N/A | No payroll calculation changes |
| Overtime/supplement determinism | N/A | No rule engine changes |
| Absence effects correct | DOC-CHANGE | Skema TASK-2604 single-tx wrap rolls back all events on quota breach (Assumption #11). Pre-S26 silently skipped breaching absence; post-S26 atomic rollback. Net behavior change for one edge case; clients re-submit on 422. No legal exposure (the breaching absence wasn't recorded either way). |
| Retroactive recalculation stable | N/A | No retroactive logic changes |

## External Review (Step 7a)

_Pending sprint-end._

| Field | Value |
|-------|-------|
| **Invoked** | not yet |
| **Sprint-start commit** | `b256a51` (S25 sprint close) |
| **Command** | TBD at sprint end |
| **Review Cycles** | 0 (cycle cap: 2 per WORKFLOW.md) |
| **Findings** | 0 |
| **Resolution** | n/a |

## Test Summary

_Pending sprint-end. Target: 525 unit + 35 plain + ~136 Docker-gated (129 pre-S26 + ~7 new D-tests + 2 TxContractTests) + 88 frontend vitest = ~786 total._

## Agent Effectiveness

_Pending sprint-end._

## Sprint Retrospective

_Pending sprint-end._
