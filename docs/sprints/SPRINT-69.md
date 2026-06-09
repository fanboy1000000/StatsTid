# Sprint 69 — ADR-033 slice 1b: the §24 auto-payout durable staging pipeline (placeholder lønart, no external emission)

| Field | Value |
|-------|-------|
| **Sprint** | 69 |
| **Status** | in-progress |
| **Start Date** | 2026-06-09 |
| **End Date** | — |
| **Orchestrator Approved** | no |
| **Build Verified** | yes — `dotnet build StatsTid.sln` 0 warnings 0 errors |
| **Test Verified** | **local full-pyramid GREEN + CI whole-workflow GREEN** — 652 unit + **527 regression** (exclusive run, 0 failures, 25m19s — the FAIL-002 Docker-churn host-crash on the first non-exclusive run cleared on the exclusive re-run, FAIL-002 precedent) + 5 smoke + 176 FE = **1360** (+23). **CI GREEN — all 6 jobs — run [27226277321](https://github.com/fanboy1000000/StatsTid/actions/runs/27226277321)** (6th consecutive CI-verified close). |

## Sprint Goal

Build the §24 year-end vacation auto-payout Payroll pipeline as a **durable STAGING slice**: a new Payroll-context event consumer turns slice-1a's recorded `VacationAutoPaidOut` disposition into a **persisted, exactly-once, replay-deterministic settlement export line** that is **never delivered externally this sprint**. The line carries a **placeholder lønart** (`SLS_TBD_S24`); the real SLS code + final line format are deferred to a future SLS-dialogue task (the lønart is dated `wage_type_mappings` config — a one-row swap later, per ADR-020). **Triple-locked against a wrong real payout:** (1) the slice-1a D13 go-live gate (dormant until `Settlement:GoLiveDate` — and pre-launch NO `VacationAutoPaidOut` events exist to consume), (2) settlement-line external delivery disabled outright (no delivery path wired + a fail-closed guard at the outbound point), (3) a fail-closed sentinel-lønart guard.

Refinement: `.claude/refinements/REFINEMENT-s69-slice1b-payroll-emitter.md` (READY).

**Single-database topology (load-bearing — Step-0b W1).** All services (Backend, Payroll, External) share ONE Postgres database (`statstid`); there is no separate "Payroll database". This is what makes the design sound: the consumer's durable line + checkpoint are co-located with `vacation_settlements` and `wage_type_mappings` in one DB, so a single local transaction spans them, and a `pg_advisory_xact_lock(hashtext('employee-'||id))` taken by the Payroll emitter genuinely serializes against the Backend close service and the reconcile endpoint (same lock space, one DB). "The consumer's transaction" below always means a transaction in this shared DB on the Payroll process's own connection.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | Spot-checked the S68/ADR-033 file set (settlement code, events, init.sql DDL, wage_type_mappings, outbox) via two Explore sweeps — all paths resolve. |
| Pattern compliance spot-check | CLEAN | `FindFirst("scopes")` → 0 hits (FAIL-001 not regressed). `http://localhost` → only in `Properties/launchSettings.json` dev profiles (not runtime code). Payroll `Program.cs` = 5× `RequireAuthorization` on the 5 mutating/sensitive endpoints (health anonymous). |
| Orphan detection | CLEAN | The 5 define-only settlement events (`FeriehindringTransferred` §22, `FeriehindringPaidOut` §25, `SaerligeFeriedagePaidOut`, `SettlementReversed`, `TerminationSettled`) are **forward-declared per ADR-033 D5** (slices 2-4), not orphans. All S68 code wired. |
| Documentation drift | DEBT (carry-forward, non-blocking) | (a) `AuditProjectionParityTests` doc/assert drift ("6 TBD rows" vs `Equal(5)`, S45-era, non-S69) — docs-debt candidate, recorded since S68. (b) Untracked `.claude/s68-*.log` run-logs + `design_handoff_skema/` (future-skema handoff) — housekeeping, not S69 scope. None block sprint start. |
| Quality grade review | CLEAN (stable) | S68 added the Backend settlement machinery; S69 introduces the **first Payroll-side event consumer**. Re-grade Payroll Integration + Integration-Isolation at sprint close (TASK-6908). |

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | **MANDATORY** — three independent triggers: schema migrations (new line/checkpoint tables + legacy ALTER), payroll export (the §24 settlement line + SLS code), P3 (event sourcing / a new event consumer + exactly-once dedup). Also P4 (replay-determinism of the staged line), P5 (integration isolation). |
| **External Codex** | invoked 2026-06-09 — c1: **5B/5W/1N** (gpt-5.5); c2: B1-B4 RESOLVED, **2 NEW B**; c3: C2-B1/B2 RESOLVED, **1 NEW B** + 1W; c4: C3-B1 RESOLVED, **0 new — "sound for decomposition"** |
| **Internal Reviewer** | invoked 2026-06-09 — c1: **2B/5W/3N**; c2: cycle-1 RESOLVED, **0 new B/W, 2N**; c3: C2-B1/B2 RESOLVED, **1 NEW B** + 2N; c4: C3-B1 RESOLVED, **1 NEW B** (C4-B1 — the symmetric mirror) |
| **BLOCKERs resolved before Step 1** | **yes** — c1-c4 edits applied (incl. C4-B1); owner chose **Proceed to implementation** at the cycle-4 halt; plan BLOCKER-free at the invariant level (Codex: "sound for decomposition") |

### Findings (cycle 1)

The two lenses **converged strongly**. Consolidated (union; Codex severities rated higher on several items the Reviewer raised as WARNINGs — taken at the higher severity):

**BLOCKERs:**
- **B1 — consumer source + checkpoint shape (Codex B / Reviewer B).** The "line-row-IS-the-checkpoint" lean cannot represent reconciled-skips, poison events, retries, or dead letters (they produce NO line, so a no-line state is indistinguishable from not-yet-consumed → repeated reprocessing). AND the source premise was wrong: `outbox_events` is a **per-`service_id` private drain partition** (the Backend's own `OutboxPublisher` independently sets `published_at`; ADR-018 D6 single-drainer), and the cited External `EventConsumerService` precedent actually polls **`outbox_messages`** (the external-delivery queue), not the event log. → **Resolution:** source = the canonical **`events`** table (Backend-published `VacationAutoPaidOut`), read with the emitter's OWN high-water cursor (a non-authoritative scan optimization). A **separate `settlement_payroll_inbox` checkpoint table keyed by source `event_id`**, carrying `(identity, sequence, bucket)` + a terminal `processing_status` ∈ {PROCESSED, SKIPPED_RECONCILED, DEAD_LETTER}, written in the SAME tx as the uniquely-keyed line. The **`event_id` UNIQUE (inbox) is the authoritative consumer dedup**; the line's `(identity, sequence, bucket)` UNIQUE is the line dedup. OQ-1 is now **resolved in-plan** (no implementation-time census deferral).
- **B2 — concurrency axis (Codex B / Reviewer W).** "advisory lock OR settlement-row CAS" is NOT an equivalent choice — a CAS on `vacation_settlements.version` neither serializes nor detects an INSERT into a separate line table, so emitter-claim and reconcile can BOTH succeed (double-pay). → **Resolution:** mandate the **employee advisory lock acquired FIRST on both paths**, in one shared-DB tx: the emitter reads `payout_reconciled_at` before inserting checkpoint+line; the reconcile endpoint (which today takes NO advisory lock — only If-Match CAS) is **retrofitted** to take the same lock and checks line/checkpoint absence before setting the marker. Test BOTH winner orderings. TASK-6905 is reframed as a non-independent sequential extension of TASK-6904's single lock/tx design.
- **B3 — B4 capture contract (Codex B).** Nullability was backwards and `Position`'s source was wrong: `AgreementCode` is **required**; `Position` is **nullable, canonicalized to `""`** for the wage-mapping key; `Position`'s dated source is **`employee_profiles`** (the `IEmploymentProfileResolver`), not the entitlement-config/OK chain. → **Resolution:** in-transaction effective-date reads for both, at a **named, persisted lookup date**; store the EXACT canonical key `GetByKeyAtAsync` consumes (`AgreementCode` required, `Position ?? ""`, `OkVersion`); **fail settlement capture** on missing profile/key data (never silently fall back to live values).
- **B4 — the wage-mapping `asOf` date is unnamed (Codex B).** "boundary date" is ambiguous (vacation-year end vs the §24 31-Dec execution boundary vs event-processing date); entry-date correctness (P4) can't be tested without choosing one. → **Resolution:** **capture the exact settlement boundary date in the immutable snapshot** (`SettlementBoundaryDate`) and use it as the `asOf` for `GetByKeyAtAsync` — replay-stable. The replay D-test must be a TRUE delayed-first-consumption (create event → supersede mapping afterward → FIRST consume → prove the historical mapping is chosen); re-consuming an already-checkpointed event only proves dedup, not mapping determinism.
- **B5 — fail-closed on missing snapshot/key/mapping (Codex B / Reviewer W).** No live-data, empty-key, or hard-coded-placeholder fallback. → **Resolution:** missing snapshot/key/mapping ⇒ NO line, retain retry diagnostics, eventually DEAD_LETTER; live-data and hard-coded-wage fallback explicitly forbidden.

**WARNINGs:**
- **W1 — "Payroll-DB" misnomer (Reviewer B, taken as W on absorption).** One shared DB; the single-DB topology is load-bearing. → stated explicitly in the Sprint Goal + Architectural Constraints.
- **W2 — line immutability / future `delivery_status` (Codex W / Reviewer W).** Admitting `DELIVERED/FAILED` now encodes an unreviewed future workflow and weakens the staged invariant. → the `settlement_export_lines` row is **immutable + staged-only** this sprint (no mutable delivery state column; the append-only row + actor/created_at IS the audit record — ADR-026 mapper not required this slice); delivery state belongs to a future slice/table.
- **W3 — TASK-6903 legacy-seed guard (Reviewer W).** The §24 seed rows need a `schema_migrations`-guarded idempotent INSERT (mirror the `s68-vacation-reset-month-check` DO-block), not a bare top-of-file INSERT (no-op on legacy DBs — the S68 B1 lesson).
- **W4 — TASK-6903 collision assertion vague (Codex W).** The sentinel is intentionally reused across many natural keys. → precise assertion: `SLS_TBD_S24` occurs ONLY for the new §24 `time_type`; that `time_type` maps to no non-sentinel wage type this sprint; no existing unrelated `time_type` uses the sentinel.
- **W5 — TASK-6906 discriminator + fail-closed default (Codex W).** Pin how `PayrollExportService` identifies a settlement line (a dedicated method / discriminator, not a caller-supplied flag) and default delivery to **disabled when config is absent**; test mixed batches + direct `ExportAsync`.
- **W6 — TASK-6907 crash matrix mismatched (Codex W).** "after claim / after line-write / around enqueue" doesn't match the atomic-tx design and there is no settlement enqueue this sprint. → rewrite the matrix around: before-commit, after-commit-before-ack/restart, duplicate concurrent claims, reconciled-skip checkpoint, poison/dead-letter, delayed-first-consumption.
- **W7 — KB refs imply an HTTP dependency (Codex N, taken as W).** `PAT-005`/`DEP-002` are not operational deps for this event-driven, non-rule-engine path — removed from the emitter task to avoid implying HTTP. TASK-6904 must NOT modify External merely because its consumer is a precedent.
- **W8 — UNIQUE business key is the authoritative line dedup, the cursor is an optimization (Reviewer W).** A redelivery after crash-before-cursor-advance is absorbed by the inbox `event_id` UNIQUE + the line `(identity,sequence,bucket)` UNIQUE (INSERT … ON CONFLICT DO NOTHING) — the cursor is not the dedup. → stated in TASK-6902/6904.

**NOTEs (absorbed):** OQ-3 snapshot migration is genuinely no-backfill (JSONB additive, settlement dormant) — kept, with the capture-source precision folded into B3; agent assignments + remaining KB refs verified correct; semantic-collision check must run against the actual seeded code set.

### Resolution

All five BLOCKERs and eight WARNINGs absorbed into the cycle-1 plan revision (this document). The central change: the exactly-once design is now an **`events`-sourced consumer + a `settlement_payroll_inbox` checkpoint (event_id-keyed) + an immutable `settlement_export_lines` row, both in one advisory-locked shared-DB tx**, replacing the "one-table line-is-checkpoint" lean. OQ-1 and OQ-3 are resolved in-plan; OQ-2 narrowed to the discriminator/fail-closed-default detail.

### Findings (cycle 2 — verification of the cycle-1 edits)

Codex confirmed cycle-1 BLOCKERs B1-B4 RESOLVED and surfaced **2 NEW BLOCKERs** (both genuine consequences of the cycle-1 two-table design — the inbox lifecycle was under-specified). The internal Reviewer found **all cycle-1 findings RESOLVED, 0 new B/W**, plus 2 NOTEs.

**New BLOCKERs (Codex):**
- **C2-B1 — inbox retry-state coherence (TASK-6902/6904).** The inbox CHECK admitted only the THREE terminal statuses (PROCESSED/SKIPPED_RECONCILED/DEAD_LETTER), but TASK-6904 also required persisted `attempts`/`last_error` *before* eventual dead-lettering — a failed-but-retryable event then has no coherent state (a terminal status is wrong; no row loses the diagnostics). → **Resolution:** add a NON-terminal **`RETRY_PENDING`** status; define the lifecycle (`RETRY_PENDING` → {PROCESSED | SKIPPED_RECONCILED | DEAD_LETTER}); the success/skip path commits its terminal state + line in ONE advisory-locked tx; a deterministic/transient failure rolls that tx back and a SEPARATE committed write atomically increments `attempts`/`last_error` and sets `RETRY_PENDING`, transitioning to `DEAD_LETTER` after the durable retry budget; the poll re-selects events whose inbox row is absent or non-terminal.
- **C2-B2 — claim-and-stage conflict masking (TASK-6904).** A bare `INSERT … ON CONFLICT (business key) DO NOTHING` would mark `PROCESSED` even if the existing line came from a *different* source event/payload — masking a real collision. → **Resolution:** on a line business-key conflict the tx **verifies the existing line is byte-identical / shares the same `source_event_id`** (benign redelivery ⇒ idempotent success); a semantic mismatch ⇒ roll back, set `DEAD_LETTER`, and report the collision (never silently no-op a non-identical conflict).

**Reviewer NOTEs (absorbed):**
- **N1 — table-count baseline.** "+2 → 61" is correct against the **doc-authoritative** count (`docs/generated/db-schema.md`: "Total: 59 tables") — NOT a raw `CREATE TABLE` grep (which counts 71, incl. `schema_migrations`/tooling). TASK-6902/6908 reconcile against the doc's 59→61.
- **N2 — candidate-PAT invariant wording.** State the dedup invariant for slices 2-4 as "**one inbox row per source `event_id`; one line per `(identity, sequence, bucket)`**" so a future slice does not assume one-inbox-row-per-settlement (folded into TASK-6908).

### Resolution (cycle 2)

C2-B1 and C2-B2 absorbed into TASK-6902 (the `RETRY_PENDING` status) + TASK-6904 (the retry lifecycle + the conflict-verification rule) + TASK-6907 (retry-lifecycle and non-identical-collision tests); the two Reviewer NOTEs folded into TASK-6902/6908.

### Findings (cycle 3 — verification of the cycle-2 edits)

**Both lenses converged on a single NEW BLOCKER** (severity trend 7B → 2B → 1B = convergence, not thrash). C2-B1 and C2-B2 both confirmed RESOLVED by both lenses.

- **C3-B1 — the post-rollback diagnostics write is lock-free and can clobber a terminal status (Codex B + Reviewer B, convergent).** Because the cycle-2 retry path **rolls back** the stage tx, the xact-scoped `pg_advisory_xact_lock` is released *before* the SEPARATE diagnostics write runs — so that write is lock-free, and as worded ("atomically increments … and sets `RETRY_PENDING`") it is an unconditional upsert. Race: worker B fails + rolls back (releases lock) → worker A claims + commits `PROCESSED` + line (releases lock) → worker B's lagging write flips the row to `RETRY_PENDING` (or, on the C2-B2 path, `DEAD_LETTER`), masking a real terminal outcome with a committed line (the line UNIQUE self-heals the *line*, but the inbox-status coherence C2-B1 guaranteed is violated, and a stray `DEAD_LETTER` over a genuine `PROCESSED` could halt re-processing). → **Resolution:** the separate post-rollback write is made **terminal-aware** — it re-acquires the employee advisory lock and uses a conditional transition `UPDATE … WHERE event_id = E AND processing_status = 'RETRY_PENDING'` (insert-if-absent only when still unclaimed) that **never overwrites a terminal status**; the SAME guard covers both the `RETRY_PENDING` increment and the C2-B2 collision `DEAD_LETTER` (one mechanism — Reviewer NOTE). Invariant stated: *a non-terminal/diagnostics write never overwrites a terminal status.* A matching failure-diagnostics-vs-concurrent-success test added to TASK-6907.
- **C3-W (Codex) + NOTEs:** a residual "`INSERT … ON CONFLICT DO NOTHING`" phrase in TASK-6902 contradicted the line's verify-on-conflict rule → clarified that the **inbox** insert is `ON CONFLICT (event_id) DO NOTHING` while the **line** insert uses verify-on-conflict (TASK-6904). OQ-1's "terminal `processing_status`" wording tidied to name the non-terminal `RETRY_PENDING`.

### Resolution (cycle 3)

C3-B1 + C3-W + the NOTEs absorbed into TASK-6904 (the terminal-aware post-rollback write + the one-mechanism guard), TASK-6902 (the idempotency-mechanics clarification), TASK-6907 (the diagnostics-vs-concurrent-success test), and OQ-1 (wording). Owner chose **iterate** at the cycle-3 halt-and-prompt.

### Findings (cycle 4 — verification of the cycle-3 edits)

The lenses **DIVERGED** — the canonical convergence-completion / thrash-signal boundary (3rd consecutive cycle touching the inbox-lifecycle area, now with lens divergence). C3-B1 confirmed RESOLVED by both.
- **Codex:** "No new findings; C3-B1 resolved — **the plan is sound for decomposition**."
- **Reviewer — C4-B1 (the symmetric mirror of C3-B1):** the cycle-3 guard correctly stops a *failure* write from overwriting a terminal status, but the *success-on-retry* path's inbox write (worded as `ON CONFLICT (event_id) DO NOTHING`) **cannot promote an existing `RETRY_PENDING` row to `PROCESSED`** — so a transient-fail-then-retry-succeeds leaves a committed line with a stuck non-terminal inbox row, re-selected every poll (benign for the line via the UNIQUE; violates the inbox terminal-coherence C2-B1 provides). → **Resolution:** the success/skip inbox write is a conditional **PROMOTION** `ON CONFLICT (event_id) DO UPDATE … WHERE processing_status='RETRY_PENDING'` (promotes non-terminal→terminal; idempotent no-op against already-terminal). This **closes the bidirectional invariant** (a terminal write promotes toward terminal; a non-terminal/diagnostics write never overwrites terminal; neither rewrites one terminal into another) — there is no remaining direction for the 4-state monotonic lifecycle to be wrong.

### Resolution (cycle 4)

C4-B1 absorbed into TASK-6904 (the success-path conditional promotion + the complete bidirectional invariant), TASK-6902 (the idempotency-mechanics wording), and TASK-6907 (the retry-then-success-promotion test). The exactly-once inbox lifecycle invariant is now **bidirectionally complete**. The residual is the exact SQL upsert form (an implementation mechanism — per the ADR-033 Step-0b owner-ruling precedent, "the design names every invariant; the exact key/CAS/tx mechanisms are slice-implementation detail," caught by the TASK-6904 Step-5a high-risk Codex review + the TASK-6907 D-tests). **2nd halt-and-prompt raised to the owner** (lens divergence at cycle 4); **owner chose Proceed to implementation (2026-06-09)** — Step-0b closes BLOCKER-free at the invariant level; the exact upsert SQL form is pinned for TASK-6904 implementation + the TASK-6907 D-tests + the Step-5a high-risk Codex review.

## Implementation Record (Steps 1-5a)

Decomposed into 7 implementation tasks + close, dispatched in 4 phases (all in the main tree — disjoint files per phase, no worktrees):
- **TASK-6901 (Data Model + Infra x-domain) — DONE.** `VacationSettlementSnapshot` gained `AgreementCode`/`Position` (nullable `string?`, NOT `required` — round-trip safety) + `SettlementBoundaryDate` (`DateOnly`); captured in `VacationSettlementService.CaptureSnapshotAsync` in-tx, fail-closed (throws on missing dated agreement [Step-5a fix] or missing profile). No DDL, no EventSerializer change. `IEmploymentProfileResolver` injected (already DI-registered).
- **TASK-6902 (Data Model schema) — DONE.** `settlement_payroll_inbox` (PK `source_event_id`; `processing_status` {RETRY_PENDING,PROCESSED,SKIPPED_RECONCILED,DEAD_LETTER}; attempts/last_error) + immutable `settlement_export_lines` (UNIQUE `(employee_id,entitlement_type,entitlement_year,sequence,bucket)`; `amount=0` CHECK; `hours>=0`; no delivery-status column). +2 tables (61); documentary `s69-adr033-slice1b-payroll-staging` ledger; `check_docs` green.
- **TASK-6903 (Payroll) — DONE.** `VACATION_SETTLEMENT_PAYOUT → SLS_TBD_S24` ADR-020-versioned rows, 10 pairs ({AC,HK,PROSA,AC_RESEARCH,AC_TEACHING}×{OK24,OK26}, position=''), via a `schema_migrations`-guarded idempotent DO-block (`s69-s24-settlement-wage-type`). 3-part semantic collision check passed.
- **TASK-6904 + TASK-6905 (Payroll + Infra + Backend.Api x-domain) — DONE.** `SettlementExportEmitter` BackgroundService (reads canonical `events`, type `VacationAutoPaidOut`, selects events with no terminal inbox row) + `SettlementInboxLineRepository`; per-event in ONE employee-advisory-locked tx: terminal re-check under lock [Step-5a fix] → reconciled-skip (`SKIPPED_RECONCILED`, sequence-bound [Step-5a fix]) → snapshot-keyed dated `GetByKeyAtAsync(asOf=SettlementBoundaryDate)` → money-free line (`hours=PayoutDays`, `amount=0`, no rate) → verify-on-conflict insert (same `source_event_id`=benign / else DEAD_LETTER) → terminal-aware PROMOTE inbox→PROCESSED; failure path = separate lock-re-acquired tx, atomic server-side RETRY_PENDING/DEAD_LETTER [Step-5a fix], terminal-aware. Reconcile-payout endpoint retrofitted to the SAME advisory lock + 409-if-line-staged (claim/reconcile XOR). DI `AddHostedService`.
- **TASK-6906 (Payroll) — DONE.** Fail-closed outbound guard in `PayrollExportService` (data-derived sentinel/`SLS_TBD_`/time-type discriminator; config-absent⇒disabled; sentinel refused unconditionally; throws before the `outbox_messages` INSERT; non-settlement paths untouched).
- **TASK-6907 (Test & QA) — DONE.** 22 new facts (15 regression + 7 unit), all 12 acceptance scenarios, none skipped; Settlement namespace 59/59.

**Build:** `dotnet build StatsTid.sln` 0W/0E (combined). **Step 5α Constraint Validator** (self-check): PASS — no new endpoints (reconcile keeps auth), no new events, no RuleEngine imports, no `FindFirst("scopes")`, no hardcoded URLs, all SQL parameterized, scope adherence.

**Step 5a high-risk review (schema migration + payroll export + new exactly-once consumer) — dual-lens:**
- **Internal Reviewer:** 0B / 0W / 3N (traced the C3-B1/C4-B1 race orderings against the literal SQL; clean). NOTEs: unused using (fixed), unlocked retry-budget read (fixed), provisional period-date for the unverified SLS format (accepted — documented).
- **External Codex cycle 1:** 1 BLOCKER + 3 WARNING — all real (the Reviewer's happy-path trace missed them): **B1** select→lock TOCTOU → terminal re-check under the lock; **W2** `snapshot.AgreementCode` reused the valuation's `?? user.AgreementCode` live fallback → strict dated read, fail-closed; **W3** reconciled-skip omitted `sequence` → bind the event identity; **W4** retry-exhaustion read outside the lock → atomic server-side `CASE` in the locked upsert. All absorbed (fix-forward, 3 files, build 0E). **Codex cycle 2: "No new findings; cycle-1 findings resolved."**

**Step 6 (merge):** n/a — all agents worked in the main tree (disjoint files per phase); no worktrees to merge. **Step 7a (sprint-end dual-lens):** see the External Review (Step 7a) section — Codex c1 1B/4W → fix-forward → c2 clean; Reviewer APPROVED-WITH-WARNINGS. The Step-7a BLOCKER (poison-event dead-letter) + the test-strengthening + the W1 doc fix landed as a post-Step-5a fix-forward (re-reviewed at Step-7a cycle 2). +1 regression test (the poison test) on top of TASK-6907's 15.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity: the emitter lives in the Payroll bounded context (ADR-033 D4); it does NOT modify External (the `EventConsumerService` is a shape-only precedent — poll + `FOR UPDATE SKIP LOCKED` + circuit-breaker + dead-letter — but its SOURCE is `outbox_messages`, not the path here). Single-DB topology stated.
- [ ] P2 — Rule engine determinism: untouched. Settlement line is balance/period-close-derived, NOT the rule-engine FLEX_PAYOUT seam (ADR-033 D7).
- [ ] P3 — Event sourcing/auditability: consumer reads the append-only canonical `events`; the `settlement_payroll_inbox` checkpoint + the immutable `settlement_export_lines` row are append-only and auditable (the row carries actor/created_at); no event mutated.
- [ ] P4 — Version correctness: the §24 lønart resolves off the immutable snapshot via the full ADR-020 dated natural key at the **captured `SettlementBoundaryDate`** — replay-deterministic, no live lookup.
- [ ] P5 — Integration isolation + delivery guarantees: the emitter's exactly-once is its OWN inbox checkpoint; external failure cannot affect the close; delivery disabled (no path wired + outbound guard).
- [ ] P6 — Payroll traceability: day-count line, `Amount=0` (CHECK-pinned), no rate read; SLS owns kroner. Precise semantic SLS-code collision check.
- [ ] P7 — Security/access: claim/reconcile mutual exclusion under one lock; no new unauthorized endpoint; delivery disabled + sentinel guard.
- [ ] P8 — CI/CD: full pyramid green; db-schema regen + `check_docs` green; build 0E/0W.

## Knowledge Base Context (Step 0)

- **ADR-033** — the binding contract. Slice-1b-relevant: **D1** (money-free; §24 line shape provisional), **D4** (service ownership + the two exactly-once boundaries; new Payroll emitter + consumer checkpoint keyed `(identity, sequence, bucket)`; `sequence` load-bearing), **D5** (state machine + event vocabulary), **D6** (disposition on the row; §24 is payout, never carryover provenance), **D7** (payout lines only; new emitter; ADR-020-dated `wage_type_mappings`; new settlement `time_type`), **D12** (settlement GLOBAL — forbids per-*institution* override; does NOT remove the agreement/position key dimensions), **D13** (slice 1b launch-neutral; first boundary = first 31 Dec after launch).
- **ADR-020** — versioned `wage_type_mappings` natural key `(time_type, ok_version, agreement_code, position)` + effective-dating + partial-unique-open-row + the dated `GetByKeyAtAsync(timeType, okVersion, agreementCode, position, asOfDate)` read (with the `position = @position OR position = ''` fallback).
- **ADR-018** — D3 atomic outbox (Backend's enqueue), **D6 stream/partition ownership** (the reason a second drainer of the Backend `outbox_events` partition is unsound — so the consumer reads the canonical `events`), D13 sync-in-tx.
- **ADR-023** — `IEmploymentProfileResolver` (the dated `employee_profiles` source for `Position`, used by the B3 capture).
- **ADR-032 D4** — the employee-scoped `pg_advisory_xact_lock(hashtext('employee-'||id))` two-phase contract — the lock the claim/reconcile mutual exclusion (B2) rides, shared across Backend close + consumption + (now) the emitter + reconcile.
- **ADR-026** — audit-projection mappers (the source `VacationAutoPaidOut` already has one; the staged line's own audit surface is NOT required this slice — the immutable append-only row + actor/created_at is the record, W2).
- **ADR-013 / ADR-016 D10** — single-period no-cascade (reversal/compensating lines are later slices; the `bucket` axis reserves them) + the byte-identical replay D-test shape.
- **FAIL-002** — Docker testcontainer-churn close protocol for the new D-tests.
- **Lesson (S36/S37)** — the MERARBEJDE/OVERTIME→`SLS_0210` collision: the new settlement code passes a precise semantic collision check (W4), not just natural-key uniqueness.
- _(Deliberately NOT cited on the emitter: PAT-005 / DEP-002 — this is an event-driven, non-rule-engine path; those refs imply an HTTP dependency, W7.)_

## Task Log

### TASK-6901 — Snapshot B4 capture contract: required `AgreementCode`, canonical `Position`, and the `SettlementBoundaryDate`, all dated + fail-closed

| Field | Value |
|-------|-------|
| **ID** | TASK-6901 |
| **Status** | complete |
| **Agent** | Data Model (extended into `src/Infrastructure/StatsTid.Infrastructure/VacationSettlementService.cs` capture site, cross-domain authorized) |
| **Components** | SharedKernel.Models (`VacationSettlementSnapshot`), Infrastructure (`VacationSettlementService.CaptureSnapshotAsync`) |
| **KB Refs** | ADR-033 D3/D7, ADR-020 (natural key), ADR-023 (`IEmploymentProfileResolver` for `Position`), ADR-016 (replay determinism) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | pending (MANDATORY — P3/P4 + cross-domain) |
| **External Review (Codex)** | pending (discretionary; additive — Orchestrator may skip per Step-5a) |
| **Orchestrator Approved** | no |

**Description**: Extend `VacationSettlementSnapshot` so the emitter can resolve the §24 lønart off the snapshot via the **exact** ADR-020 natural key, replay-deterministically (Codex B3/B4). Add: **`string AgreementCode` (REQUIRED, non-null)**, **`string? Position` (nullable; canonicalized to `""` at mapping-lookup time, matching the `wage_type_mappings` `position` default)**, and **`DateOnly SettlementBoundaryDate`** (the exact `asOf` the emitter's dated lookup uses — the §24 execution boundary the close service already computes; ends the "boundary date" ambiguity). All three ride inside the existing JSONB `snapshot` column — **no DDL**. Capture them in `CaptureSnapshotAsync` via **in-transaction effective-date reads**: `AgreementCode` from the dated `user_agreement_codes` chain already resolved at the ferieår boundary (reuse the existing resolution, not a live read); `Position` from the dated `IEmploymentProfileResolver` (`employee_profiles`) as-of the boundary; `SettlementBoundaryDate` from the close boundary. **Fail-closed:** if any required key datum is missing, **fail the settlement capture** (do not silently use live values). Forward-only, no backfill (settlement dormant ⇒ no rows). The fields flow into the `VacationAutoPaidOut` payload automatically (System.Text.Json additive init props — no EventSerializer change).

**Validation Criteria**:
- [ ] `VacationSettlementSnapshot` gains `AgreementCode` (required), `Position` (nullable), `SettlementBoundaryDate` (init-only, PAT-001), serialized into the existing JSONB snapshot; no init.sql change; no EventSerializer change.
- [ ] `CaptureSnapshotAsync` populates all three from in-tx dated reads (agreement = the existing ferieår-boundary resolution; position = `IEmploymentProfileResolver` as-of boundary; boundary date = the close boundary).
- [ ] Missing required key datum fails settlement capture (no silent live fallback) — covered by a test.
- [ ] The captured key, when passed as `(time_type, OkVersion, AgreementCode, Position ?? "", asOf=SettlementBoundaryDate)`, is exactly what TASK-6904's `GetByKeyAtAsync` consumes.

**Files Changed**: `src/SharedKernel/StatsTid.SharedKernel/Models/VacationSettlementSnapshot.cs`, `src/Infrastructure/StatsTid.Infrastructure/VacationSettlementService.cs`.

---

### TASK-6902 — Schema: `settlement_payroll_inbox` checkpoint + immutable `settlement_export_lines` + legacy-upgrade ALTER

| Field | Value |
|-------|-------|
| **ID** | TASK-6902 |
| **Status** | complete |
| **Agent** | Data Model (extended into `docker/postgres/init.sql` schema, cross-domain authorized — Orchestrator-approved schema change) |
| **Components** | init.sql (2 new tables + indexes + legacy ALTER), `docs/generated/db-schema.md` (regen) |
| **KB Refs** | ADR-033 D4/D5/D7, ADR-018 D3/D6 (events-source rationale), ADR-020 (partial-unique-open-row precedent) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | pending (MANDATORY — P1/P3 schema) |
| **External Review (Codex)** | **pending — HIGH-RISK (schema migration)**: per-task Codex at Step 5a |
| **Orchestrator Approved** | no |

**Description**: Add TWO tables in the shared DB (the resolved OQ-1 design — not the rejected one-table lean):
1. **`settlement_payroll_inbox`** — the consumer checkpoint, **PK = source `event_id`** (the `events.event_id` of the consumed `VacationAutoPaidOut`; the authoritative consumer dedup). Columns: `(employee_id, entitlement_type, entitlement_year, sequence, bucket)`, `processing_status TEXT CHECK IN ('RETRY_PENDING','PROCESSED','SKIPPED_RECONCILED','DEAD_LETTER')`, `attempts INT NOT NULL DEFAULT 0`, `last_error TEXT`, `processed_at TIMESTAMPTZ`. **`RETRY_PENDING` is the only NON-terminal status (C2-B1)** — a failed-but-retryable event lives here with its `attempts`/`last_error`; the three others are terminal. A reconciled or poison event commits a terminal checkpoint here with **no line** (B1/B5).
2. **`settlement_export_lines`** — the durable staged line, **UNIQUE `(employee_id, entitlement_type, entitlement_year, sequence, bucket)`** (the line dedup; the `bucket` reserves later slices' reversal/termination/godtgørelse lines). Columns: `wage_type` (the resolved sentinel), `hours NUMERIC NOT NULL`, `amount NUMERIC NOT NULL DEFAULT 0` + **CHECK `amount = 0`** (money-free pinned at the schema level, B5), `ok_version`, `agreement_code`, `position`, `period_start`, `period_end`, `source_event_id`, `created_at`, `created_by`. **Immutable + staged-only this sprint — NO mutable `delivery_status` column** (admitting `DELIVERED/FAILED` now would encode an unreviewed future workflow — W2; delivery state is a future slice's separate table).

Both rows are written in ONE shared-DB tx (TASK-6904). The **inbox `event_id` UNIQUE + the line business-key UNIQUE are the authoritative idempotency**; the consumer cursor is a non-authoritative scan optimization (W8). **Idempotency mechanics differ by table (C3-W / C4-B1):** the **inbox** insert is a conditional terminal **promotion** — `ON CONFLICT (event_id) DO UPDATE SET processing_status = <terminal>, processed_at = NOW() WHERE settlement_payroll_inbox.processing_status = 'RETRY_PENDING'` (promotes a non-terminal row toward terminal; idempotent no-op against an already-terminal row), NOT a bare `DO NOTHING` (which could not promote a `RETRY_PENDING` row left by a prior transient failure); the **line** insert is the verify-on-conflict rule of TASK-6904 (byte-identical / same-`source_event_id` ⇒ benign idempotent; mismatch ⇒ dead-letter + report). Ship the **legacy-upgrade guarded ALTER/CREATE** for pre-existing DBs (S68 B1 lesson — mirror the `s68-vacation-reset-month-check` `schema_migrations`-guarded DO-block; a fresh CREATE is needed because `CREATE TABLE IF NOT EXISTS` at the top of init.sql is a no-op on a legacy DB). Regenerate `docs/generated/db-schema.md`; `tools/check_docs.py` must pass.

**Validation Criteria**:
- [ ] `settlement_payroll_inbox` (PK `event_id`) + `settlement_export_lines` (UNIQUE business key) created; line table has `amount` CHECK = 0 and NO mutable delivery-status column.
- [ ] `processing_status` CHECK admits exactly {RETRY_PENDING (non-terminal), PROCESSED, SKIPPED_RECONCILED, DEAD_LETTER}; `attempts`/`last_error` columns present.
- [ ] Legacy-upgrade guarded ALTER/CREATE present + idempotent (fresh + pre-existing DB both converge); not inline-CHECK-only.
- [ ] `tools/generate_db_schema.py` re-run; `tools/check_docs.py` green; table count +2 recorded (61).

**Files Changed**: `docker/postgres/init.sql`, `docs/generated/db-schema.md`.

---

### TASK-6903 — §24 placeholder `wage_type_mappings` seed (legacy-guarded) + precise semantic collision assertion

| Field | Value |
|-------|-------|
| **ID** | TASK-6903 |
| **Status** | complete |
| **Agent** | Payroll Integration (`wage_type_mappings` seed section of `docker/postgres/init.sql` — declared Payroll scope) |
| **Components** | init.sql (wage_type_mappings seed rows + legacy guard), the new settlement `time_type` constant |
| **KB Refs** | ADR-033 D1/D7, ADR-020 (versioned natural key + effective-dating) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | pending (OPTIONAL P6 — Orchestrator elects to review: legal/SLS surface) |
| **External Review (Codex)** | **pending — HIGH-RISK (payroll export / SLS codes)**: per-task Codex at Step 5a |
| **Orchestrator Approved** | no |

**Description**: Add `wage_type_mappings` rows mapping the new §24-settlement `time_type` (`VACATION_SETTLEMENT_PAYOUT` — pinned name) → the **placeholder lønart `SLS_TBD_S24`**, as ADR-020 **versioned** rows (full natural key + `effective_from` + the partial-unique-open-row index — NOT bare rows), for the agreement codes / OK versions a §24 payout can occur under. The placeholder is a sentinel the outbound guard (TASK-6906) refuses to deliver; swapping it for the real code later is a supersede-and-create (one row) — expect a possible **line-format** adjustment too at SLS time (ADR-033 D1 Risk). **Legacy-seed guard (W3):** land the seed via a `schema_migrations`-guarded idempotent INSERT block (mirror `s68-vacation-reset-month-check`), NOT a bare top-of-file INSERT — so legacy DBs (which never re-run the top-of-file seed) also get the rows; otherwise the emitter's `GetByKeyAtAsync` returns null on a legacy env. **Precise collision assertion (W4):** `SLS_TBD_S24` may occur ONLY for the new `VACATION_SETTLEMENT_PAYOUT` `time_type`; that `time_type` must NOT map to any non-sentinel wage type this sprint; and no existing unrelated `time_type` may use the sentinel. (This is NOT the same as "no duplicate natural key" — the sentinel is intentionally reused across many agreement/OK keys.)

**Validation Criteria**:
- [ ] New `time_type` → `SLS_TBD_S24` rows are ADR-020 versioned (effective-dated, partial-unique-open-row), landed via a legacy-guarded idempotent block (fresh + legacy converge).
- [ ] `time_type` is net-new (no collision with `NORMAL_HOURS`/`OVERTIME_*`/`MERARBEJDE`/`VACATION`(SLS_0510, the consumed-absence code, distinct)/etc.).
- [ ] The collision assertion holds: sentinel only on the §24 `time_type`; that `time_type` maps to no non-sentinel code; no unrelated `time_type` uses the sentinel — checked against the actual seeded set.
- [ ] db-schema regen + check_docs green.

**Files Changed**: `docker/postgres/init.sql` (wage_type_mappings seed).

---

### TASK-6904 — The Payroll settlement-export emitter (`events`-sourced consumer) + advisory-locked exactly-once inbox+line tx + snapshot-keyed dated mapping + money-free + fail-closed; AND the claim/reconcile mutual-exclusion design (with TASK-6905)

| Field | Value |
|-------|-------|
| **ID** | TASK-6904 |
| **Status** | complete |
| **Agent** | Payroll Integration (extended into `src/Infrastructure` for the `events`-read seam, cross-domain authorized) |
| **Components** | `src/Integrations/StatsTid.Integrations.Payroll/**` (new `SettlementExportEmitter` BackgroundService + inbox/line repository + DI in `Program.cs`), reads the canonical `events` table + `wage_type_mappings` |
| **KB Refs** | ADR-033 D4/D5/D6/D7/D13, ADR-018 D3/D6, ADR-020 (`GetByKeyAtAsync`), ADR-032 D4 (advisory lock), ADR-016 (replay), ADR-013 (bucket/sequence) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | pending (MANDATORY — P1/P3/P5 new pattern + cross-domain) |
| **External Review (Codex)** | **pending — HIGH-RISK (payroll export)**: per-task Codex at Step 5a |
| **Orchestrator Approved** | no |

**Description**: A **new Payroll-context** `SettlementExportEmitter` BackgroundService consumes the settlement event family (this sprint: `VacationAutoPaidOut` only, event-type-discriminated) from the **canonical `events` table** (where the Backend publisher lands it; deserializable via `EventTypeMap`), with the emitter's OWN high-water cursor (a scan optimization, NOT the dedup — B1/W8). It does **NOT** read the Backend's `outbox_events` partition and does **NOT** modify External (the `EventConsumerService` is a shape-only precedent — poll + `FOR UPDATE SKIP LOCKED` + circuit-breaker + dead-letter — but its source is `outbox_messages`, W7). Per event, in ONE shared-DB tx **under the employee advisory lock `pg_advisory_xact_lock(hashtext('employee-'||id))` acquired FIRST** (B2):
- **Reconciled check (B2):** read `payout_reconciled_at` for the settlement row; if set → write a terminal `settlement_payroll_inbox` row `SKIPPED_RECONCILED` (no line) and commit.
- **Claim + stage (B1/B4):** else resolve the §24 lønart via the dated natural key `GetByKeyAtAsync(VACATION_SETTLEMENT_PAYOUT, snapshot.OkVersion, snapshot.AgreementCode, snapshot.Position ?? "", asOf = snapshot.SettlementBoundaryDate)` — **replay-deterministic, no live lookup**. Build the line **money-free**: `Hours = PayoutDays`, `Amount = 0`, **no rate read**. INSERT the immutable `settlement_export_lines` row keyed `(identity, sequence, bucket=AUTO_PAYOUT_24)` AND the `settlement_payroll_inbox` row `PROCESSED` (keyed by source `event_id`). **Conflict verification (C2-B2):** the line INSERT is NOT a bare `ON CONFLICT DO NOTHING` — on a business-key conflict the tx SELECTs the existing line and verifies it is **byte-identical / shares the same `source_event_id`** (a benign redelivery ⇒ idempotent success, commit `PROCESSED`); a **non-identical** conflict ⇒ roll back, set the inbox `DEAD_LETTER`, and **report** the collision (never silently no-op a semantic mismatch). A redelivered event whose inbox row is already terminal is a no-op (at-least-once + dedup = exactly-once effect, ADR-033 D4).
- **Fail-closed + the durable retry lifecycle (B5 / C2-B1; terminal-aware per C3-B1):** missing snapshot / missing key datum / null `GetByKeyAtAsync` ⇒ **no line**. The success/skip path commits its terminal inbox state (+ line) in the ONE advisory-locked tx; on a failure that tx **rolls back** (no line, no terminal state), then a **SEPARATE committed write** records the diagnostics. **Because the rollback released the xact advisory lock, that separate write is terminal-aware, NOT a lock-free unconditional upsert (C3-B1): it re-acquires the employee advisory lock and uses a conditional transition `UPDATE … WHERE event_id = E AND processing_status = 'RETRY_PENDING'` (insert-if-absent only when the row is still unclaimed), so it can NEVER overwrite a concurrently-committed terminal status.** It does `attempts += 1` + sets `last_error` + `RETRY_PENDING`, transitioning to terminal `DEAD_LETTER` once `attempts` reaches the durable retry budget. **The C2-B2 non-identical-collision `DEAD_LETTER` write is the SAME post-rollback path and uses the SAME terminal-aware guard** — one mechanism, not two. **Symmetric completion (C4-B1):** the success/skip path's terminal inbox write is itself a conditional **PROMOTION**, not a bare `DO NOTHING` — `INSERT … <PROCESSED|SKIPPED_RECONCILED> … ON CONFLICT (event_id) DO UPDATE SET processing_status = <terminal>, processed_at = NOW() WHERE settlement_payroll_inbox.processing_status = 'RETRY_PENDING'` — so a retry that succeeds after a prior `RETRY_PENDING` is **promoted to its terminal state** (a bare `DO NOTHING` would leave it stuck non-terminal with a committed line, re-selected forever), while a truly already-terminal redelivery is an idempotent no-op. **Complete bidirectional invariant: inbox writes move MONOTONICALLY toward terminal — a terminal write promotes `RETRY_PENDING`→terminal; a non-terminal/diagnostics write never overwrites a terminal; neither ever rewrites one terminal into another.** The poll **re-selects events whose inbox row is absent or `RETRY_PENDING`** (never terminal). **Forbid** live-data, empty-key, or hard-coded-wage fallback — a missing key produces no line, not a guessed one.

The emitter is naturally dormant pre-launch (no `VacationAutoPaidOut` events until the D13-gated close runs post-go-live). DI-register the BackgroundService in the Payroll `Program.cs`.

**Mutual exclusion (B2, jointly with TASK-6905 — one shared lock/tx design, NOT independently dispatched):** the reconcile endpoint is retrofitted (TASK-6905) to acquire the SAME advisory lock first and to refuse (409) / skip when a line/checkpoint already exists for the bucket; the emitter skips when `payout_reconciled_at` is set. Across {Backend close, Backend reconcile, Payroll emitter} exactly one disposition per `(identity, sequence, bucket)`.

**Validation Criteria** (proven by TASK-6907):
- [ ] Source = canonical `events` (type-discriminated), own cursor; does not read `outbox_events`; does not modify External.
- [ ] One advisory-locked shared-DB tx writes the inbox checkpoint (+ line, unless skipped/dead-lettered); redelivery/replay yields **at most one** line, **byte-identical**; reconciled → SKIPPED_RECONCILED, no line; poison → DEAD_LETTER, no line.
- [ ] §24 lønart resolved at `asOf = snapshot.SettlementBoundaryDate` via the exact dated natural key; a mapping superseded AFTER settlement does not change a later first-consumption's chosen mapping (replay-stable).
- [ ] `Hours = PayoutDays`, `Amount = 0`, no rate/kroner path touched; missing-key/mapping fails closed (no line), no live/hardcoded fallback.
- [ ] Build 0E; BackgroundService registered.

**Files Changed**: `src/Integrations/StatsTid.Integrations.Payroll/Services/SettlementExportEmitter.cs` (new), `.../SettlementInboxAndLineRepository.cs` (new), `.../Program.cs` (DI), a SharedKernel/Infrastructure `events`-reader seam (cross-domain authorized).

---

### TASK-6905 — Reconcile-endpoint advisory-lock retrofit (the Backend half of the B2 mutual exclusion — sequential extension of TASK-6904, same lock/tx design)

| Field | Value |
|-------|-------|
| **ID** | TASK-6905 |
| **Status** | complete |
| **Agent** | Backend API + Payroll Integration (cross-domain authorized — the same single lock/tx design as TASK-6904; **NOT** dispatched independently) |
| **Components** | `src/Backend/StatsTid.Backend.Api/Endpoints/VacationSettlementEndpoints.cs` (`reconcile-payout`) |
| **KB Refs** | ADR-033 D4, ADR-032 D4 (employee advisory lock) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | pending (MANDATORY — P5/P7 concurrency invariant) |
| **External Review (Codex)** | **pending — HIGH-RISK (payroll export + double-pay path)**: per-task Codex at Step 5a |
| **Orchestrator Approved** | no |

**Description**: The slice-1a `reconcile-payout` endpoint currently relies on an **If-Match `version` CAS alone — it does NOT take the employee advisory lock** (verified `VacationSettlementEndpoints.cs:626-768`). For the XOR to hold across the emitter and the operator, **retrofit the endpoint to acquire the SAME `pg_advisory_xact_lock(hashtext('employee-'||id))` first**, and to **check line/checkpoint absence before** setting `payout_reconciled_at` (refuse 409 if a settlement line/checkpoint already exists for the bucket). This is the Backend half of TASK-6904's single mutual-exclusion design — designed and reviewed jointly, dispatched as a sequential extension (TASK-6904 is not "done" until this lands), so the lock/tx designs cannot diverge. The endpoint keeps its HROrAbove + OrgScope + If-Match contract.

**Validation Criteria** (proven by TASK-6907):
- [ ] `reconcile-payout` acquires the employee advisory lock first; checks line/checkpoint absence before the marker write; 409 if a line/checkpoint exists.
- [ ] A concurrency D-test proves reconcile XOR emitter-claim for one bucket — tested in **both** winner orderings.
- [ ] HROrAbove + OrgScope + If-Match preserved.

**Files Changed**: `src/Backend/StatsTid.Backend.Api/Endpoints/VacationSettlementEndpoints.cs`.

---

### TASK-6906 — Fail-closed delivery guard at the OUTBOUND boundary: settlement-line delivery disabled + sentinel-lønart refusal (B3 of the refinement)

| Field | Value |
|-------|-------|
| **ID** | TASK-6906 |
| **Status** | complete |
| **Agent** | Payroll Integration (`src/Integrations/**/Payroll/**`) |
| **Components** | `PayrollExportService` / the `outbox_messages` insertion point, a config flag, the settlement-line discriminator |
| **KB Refs** | ADR-033 D1/D7/D13, ADR-018 (outbox delivery) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | pending (MANDATORY — P5/P7 delivery guard) |
| **External Review (Codex)** | **pending — HIGH-RISK (payroll export)**: per-task Codex at Step 5a |
| **Orchestrator Approved** | no |

**Description**: Two independent outbound locks at the **single** delivery / `outbox_messages`-insertion point in `PayrollExportService` (**NOT** the shared static `SlsExportFormatter`, which export paths bypass). (1) **Settlement-line external delivery disabled** via a config flag that **defaults to disabled when the config key is absent** (fail-closed, W5) — a settlement line cannot enter `outbox_messages` / reach a sink. (2) A **fail-closed sentinel-lønart guard**: a line whose `wage_type` is a `SLS_TBD_*` sentinel is refused at the outbound point even if delivery were enabled. **Pin the settlement-line discriminator (W5):** the outbound point identifies a settlement line by a dedicated mechanism (a dedicated method / a typed `SourceTimeType` discriminator), NOT a caller-supplied flag a caller could omit; test mixed batches and direct `ExportAsync` calls. (Note: this sprint nothing actually feeds settlement lines to `PayrollExportService` — the emitter only stages them in `settlement_export_lines` — so the guard is defense-in-depth for when delivery is wired in a later slice.)

**Validation Criteria** (proven by TASK-6907):
- [ ] Settlement-line delivery is disabled by default (config absent ⇒ disabled); a staged line cannot enter `outbox_messages`.
- [ ] A `SLS_TBD_*` sentinel line is refused at the outbound boundary even with delivery enabled.
- [ ] The discriminator is not caller-supplied/bypassable; mixed-batch + direct `ExportAsync` tested; existing non-settlement export paths unaffected; the guard is at the outbound point, not in `SlsExportFormatter`.

**Files Changed**: `src/Integrations/StatsTid.Integrations.Payroll/Services/PayrollExportService.cs` (+ config).

---

### TASK-6907 — Test & QA: the exactly-once / mutual-exclusion / no-emission / money-free / replay-stable D-test suite

| Field | Value |
|-------|-------|
| **ID** | TASK-6907 |
| **Status** | complete |
| **Agent** | Test & QA (`tests/**`) |
| **Components** | Docker-gated regression D-tests + unit tests |
| **KB Refs** | ADR-033 (all D), ADR-016 D10 (byte-identical replay), FAIL-002 (Docker churn protocol), PAT-008 (FixedTimeProvider WAF) |
| **Constraint Validator** | pending |
| **Reviewer Audit** | pending |
| **External Review (Codex)** | covered by the per-task high-risk reviews + Step 7a |
| **Orchestrator Approved** | no |

**Description**: Prove every acceptance criterion. The crash/exactly-once matrix is written around the ACTUAL atomic-tx + checkpoint design (W6): **before-commit** (crash before the inbox+line tx commits ⇒ nothing staged, re-consumed cleanly); **after-commit-before-cursor-advance / consumer restart** (re-sees the event ⇒ inbox `event_id` UNIQUE absorbs it ⇒ exactly one line); **duplicate concurrent claims** (two workers, same event ⇒ one wins via the UNIQUE, one no-ops); **reconciled-skip** (`payout_reconciled_at` set ⇒ `SKIPPED_RECONCILED` checkpoint, NO line); **retry lifecycle / poison→dead-letter (C2-B1)** (a deterministic failure rolls back the stage tx ⇒ a `RETRY_PENDING` inbox row with `attempts`/`last_error` persists across the rollback ⇒ re-selected next poll ⇒ after the retry budget transitions to terminal `DEAD_LETTER`, NO line, no live/hardcoded fallback; a transient failure that later clears resolves to `PROCESSED`); **non-identical line collision (C2-B2)** (a business-key conflict whose existing line came from a different `source_event_id`/payload ⇒ roll back + `DEAD_LETTER` + report, NOT silently swallowed; a same-event/byte-identical redelivery ⇒ idempotent `PROCESSED`); **failure-diagnostics vs concurrent success (C3-B1)** (worker B fails + rolls back, then its lagging terminal-aware diagnostics write runs AFTER worker A has committed `PROCESSED`+line ⇒ the conditional `WHERE processing_status='RETRY_PENDING'` write is a no-op ⇒ the terminal `PROCESSED` survives, no clobber, no second line); **retry-then-success promotion (C4-B1)** (a transient failure leaves `RETRY_PENDING` ⇒ the retry succeeds ⇒ assert the inbox row is **promoted to `PROCESSED`** (not left stuck `RETRY_PENDING`) and is not re-selected on the next poll; plus the diagnostics-lands-first ordering); **delayed-first-consumption** (create the event → supersede the `wage_type_mappings` row AFTER the boundary → perform the FIRST consumption → prove the historical as-of-`SettlementBoundaryDate` mapping is chosen — NOT a re-consume of an already-checkpointed event, which only proves dedup, B4). **Mutual exclusion (B2):** reconcile XOR emitter-claim in BOTH winner orderings. **No external emission (B3):** a staged line cannot enter `outbox_messages` (delivery disabled-by-default); a `SLS_TBD_*` sentinel refused at the outbound boundary; mixed-batch + direct `ExportAsync`. **Money-free (B5):** `Hours=day-count`, `Amount=0`, no rate path. **Semantic SLS collision (W4):** sentinel only on the §24 `time_type`. Follow the FAIL-002 close protocol (fresh Docker session + exclusive full-suite runs).

**Validation Criteria**:
- [ ] Every B1–B5 + W4 acceptance criterion has a passing test; the crash matrix matches the atomic-tx design (before-commit / after-commit-restart / dup-claim / reconciled-skip / dead-letter / delayed-first-consumption).
- [ ] Full pyramid green, recorded with previous + delta = current arithmetic (`sprint-test-validation`).

**Files Changed**: `tests/**` (new D-test classes).

---

### TASK-6908 — Sprint close: db-schema/check_docs verify, KB, ROADMAP/INDEX/QUALITY, Step-7a

| Field | Value |
|-------|-------|
| **ID** | TASK-6908 |
| **Status** | complete |
| **Agent** | Orchestrator |
| **Components** | docs/, KB INDEX, ROADMAP, sprints INDEX, QUALITY |
| **KB Refs** | ADR-033, ADR-018, ADR-020 |
| **Orchestrator Approved** | no |

**Description**: Verify `db-schema.md` regen + `check_docs` green (reconcile the delta against the doc-authoritative **59→61**, not a raw `CREATE TABLE` grep — Reviewer N1); consider a new PAT (the **`events`-sourced consumer + event_id-keyed inbox checkpoint + uniquely-keyed immutable line, one advisory-locked tx** exactly-once pattern, if it generalizes for slices 2-4 — state the invariant as "**one inbox row per source `event_id`; one line per `(identity, sequence, bucket)`**", Reviewer N2); update ROADMAP S69 row + slice roadmap, sprints INDEX, QUALITY grades (Payroll Integration / Integration Isolation); run the Step-7a dual-lens (Codex + Reviewer) on the full sprint diff; close through `sprint-close-guard.ps1`.

---

## Open Questions (Step-0b forks)

1. **OQ-1 — RESOLVED in-plan (Step-0b B1).** Two tables: `settlement_payroll_inbox` (PK source `event_id`, `processing_status` ∈ terminal {PROCESSED, SKIPPED_RECONCILED, DEAD_LETTER} + non-terminal RETRY_PENDING) + immutable `settlement_export_lines` (UNIQUE `(identity, sequence, bucket)`), written in one advisory-locked shared-DB tx; source = the canonical `events` table with the emitter's own non-authoritative cursor. The remaining detail (exact cursor column / `events`-reader seam) is a Step-1 mechanism, not an architecture fork.
2. **OQ-2 — narrowed (Step-0b W5).** The outbound guard pins a non-bypassable settlement-line discriminator + a fail-closed (config-absent ⇒ disabled) default at the single `PayrollExportService` delivery point. Placement settled; the exact discriminator mechanism (dedicated method vs typed `SourceTimeType`) is a Step-1 detail.
3. **OQ-3 — RESOLVED (Step-0b B3/NOTE).** The additive `AgreementCode`/`Position`/`SettlementBoundaryDate` ride the existing JSONB snapshot column (no DDL, forward-only, settlement dormant ⇒ no backfill); the capture is in-tx dated + fail-closed (TASK-6901).

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending | §24 = automatic post-period payout of the >4-week tranche (Ferielov §24, LBK 152/2024). |
| Wage type mappings produce correct SLS codes | **pending — PLACEHOLDER** | `SLS_TBD_S24` sentinel; the real §24 SLS code + line format are UNVERIFIED (deferred to an SLS-dialogue task). Triple-locked against real payout. |
| Overtime/supplement calculations are deterministic | N/A | Untouched. |
| Absence effects on norm/flex/pension are correct | N/A | Untouched. |
| Retroactive recalculation produces stable results | pending | The staged line is replay-stable (snapshot-keyed dated mapping at `SettlementBoundaryDate`); reversal/compensating lines are later slices. |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — dual-lens (Codex + Reviewer) on the uncommitted sprint diff |
| **Sprint-start commit** | `75dee50` (S68 close-polish — HEAD at S69 start; `reviewed-against-commit` in both artifacts) |
| **Command** | `codex review "<prompt>"` (prompt-alone, uncommitted) + Reviewer Agent |
| **Review Cycles** | 2 per lens (cycle cap respected; cycle 2 clean ⇒ no halt) |
| **Findings** | Codex c1: **1 BLOCKER + 4 WARNING** → all absorbed → c2 "no new findings". Reviewer c1: **APPROVED-WITH-WARNINGS** (1W + 3N). |
| **Resolution** | all resolved (fix-forward) |

### Findings (Step 7a)

- **Codex BLOCKER (P1) — poison/un-deserializable `VacationAutoPaidOut` never dead-letters** → resolved: inbox identity columns nullable + a terminal `DEAD_LETTER` row keyed by `source_event_id` on deserialize failure (terminal-aware, no lock); new `PoisonEvent_…` test.
- **Codex WARNING ×3 (P2) — green-but-weak tests** (exactly-once redelivery / terminal-no-clobber / mutual-exclusion exercised the poll-filter/pre-checks not the dedup/under-lock race) → exactly-once strengthened to a true duplicate-claim (line-UNIQUE + BenignRedelivery + byte-identical); the two genuinely-concurrent tests documented as single-emitter-instance-moot for slice 1b.
- **Codex WARNING (P5/P6) — `SourceTimeType` discriminator caller-omissible** → documented: the `SLS_TBD_` sentinel `WageType` prefix is the non-bypassable guard this sprint; strengthen to a typed line-kind when a real §24 code replaces the sentinel.
- **Reviewer WARNING W1 (P4/P6) — `SettlementBoundaryDate` is the ferieår end (Aug 31), doc said "31 Dec".** Functionally inert this sprint (all §24 mappings open-from-2020 ⇒ identical `asOf` resolution); inherited from S68. → comments corrected; **recorded follow-up:** the legal §21/§24 anchor is 31 Dec — owner ruling on the §24 mapping `asOf` when the real §24 SLS code lands. Value unchanged.
- **Reviewer NOTEs ×3** — legacy-runbook S69 backfill (docs-debt); reconcile 409-before-CAS ordering (harmless); test-harness BackgroundService restart pattern (low risk).

Artifacts: `.claude/reviews/SPRINT-69-step7a-{codex,reviewer}.md`.

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 652 | all passing (+7 — the delivery guard) |
| Regression tests | 527 | exclusive local run GREEN (0 failures, 25m19s) + CI green (+16) |
| Smoke tests | 5 | CI green (docker-compose harness) |
| Frontend | 176 | unchanged (no FE this slice) |
| **Total** | 1360 | +23 vs S68 |

Baseline (S68): 645 unit + 511 regression + 5 smoke + 176 FE = 1337. **CI whole-workflow GREEN — all 6 jobs — run [27226277321](https://github.com/fanboy1000000/StatsTid/actions/runs/27226277321)** (6th consecutive CI-verified close).

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 8 (7 implementation + 1 close) |
| Constraint Violations | 0 (self-check PASS) |
| Reviewer Findings (internal) | Step-0b: 2B/5W/3N(c1)→0/0/2N(c2)→1B/0/2N(c3)→1B(c4); Step-5a: 0B/0W/3N; Step-7a: 0B/1W/3N |
| External Review Cycles | Codex: 4 (Step-0b) + 2 (Step-5a) + 2 (Step-7a) = 8; Reviewer matched each |
| External Findings (Codex) | Step-0b: 5B(c1)/2B(c2)/1B(c3)/0(c4); Step-5a: 1B/3W; Step-7a: 1B/4W — all absorbed |
| Re-dispatches | 0 task-failure re-dispatches; 2 planned review-absorption fix-forwards (Step-5a, Step-7a) |
| First-Pass Rate | 100% (all 6 implementation agents accepted first-pass; the 2 fix-forwards absorbed review findings, not failures) |

## Sprint Retrospective

**What went well:** The Step-0b plan-review gate did heavy, convergent work (7B→2B→1B→1B across 4 cycles, both lenses) BEFORE any code — restructuring the exactly-once design from a "line-is-the-checkpoint" lean to an `events`-sourced consumer + a separate `event_id`-keyed inbox checkpoint with a terminal-aware monotonic lifecycle, fixing the consumer source (`events` not the per-service `outbox_events` drain), the advisory-lock concurrency axis, and the B4 capture contract — all as cheap markdown edits. The Codex lens repeatedly caught real concurrency / fail-closed correctness bugs (Step-5a TOCTOU + live-fallback + sequence-binding; Step-7a poison-event dead-letter) that the internal Reviewer's happy-path trace missed — **lens complementarity earned its keep at every gate**. All 6 implementation agents were first-pass; the schema/snapshot/seed/guard/emitter integrated with 0W/0E.

**What to improve:** The Test & QA agent's first-pass D-tests were **green-but-weak** on the concurrency/dedup paths (the "redelivery" re-drained after terminal so the poll excluded it; the terminal-no-clobber + mutual-exclusion tests exercised only the poll-filter/pre-checks) — caught at Step-7a's test-quality focus. Reminder: *a green test is not a proof of the invariant.* The non-exclusive background regression hit the FAIL-002 Docker testcontainer-churn host-crash (366/366 passed before the abort, 0 failures) — re-run exclusively per the close protocol.

**Knowledge produced:** candidate **PAT** — the *`events`-sourced consumer + `event_id`-keyed inbox checkpoint (terminal-aware monotonic lifecycle) + uniquely-keyed immutable line, one advisory-locked tx* exactly-once pattern (invariant: one inbox row per source `event_id`; one line per `(identity, sequence, bucket)`; generalizes to slices 2-4). **Follow-up (W1):** the §24 mapping `asOf` is the ferieår end (Aug 31); the legal §21/§24 anchor is 31 Dec — owner ruling when the real §24 SLS code lands.
