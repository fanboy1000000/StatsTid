# Sprint 66 — ADR-032: Vacation consumption correctness — norm-based day basis + recorded valuation (clears the ADR-031 D6 launch gate)

| Field | Value |
|-------|-------|
| **Sprint** | 66 |
| **Status** | complete |
| **Start Date** | 2026-06-07 |
| **End Date** | 2026-06-07 |
| **Orchestrator Approved** | yes — 2026-06-07 |
| **Build Verified** | yes — `dotnet build` 0 warnings 0 errors (production projects; warn-as-error) |
| **Test Verified** | yes — 631 unit + 466/466 regression (consecutive leg fully pristine; pristine leg owner-adjudicated 3× FAIL-002-signature single flakes, § Test Summary) + 5 smoke + 176 FE = 1278 total passing |

## Sprint Goal

Implement ADR-032 (`docs/knowledge-base/decisions/ADR-032-vacation-consumption-correctness.md`): make entitlement **consumption** legally correct per the verified state-sector mechanism — (D1) canonical day basis `hours ÷ fullDayHours(e,d)` (the Ferievejledning Example-3.5 mechanism; owner-classified bug-with-no-past-impact), (D2) per-absence recorded feriedage as the authoritative consumption record with backfill parity, (D3) norm-based per-day guard with type scoping, (D4) profile-change revaluation, (D5) `POST /api/absences` bypass retirement. **The originally-scoped 5÷N conversion + `work_days_per_week` field were dropped at Step 0** — the premise was refuted against primary sources (research: `docs/references/vacation-consumption-mechanism-research.md`; owner re-adjudication 2026-06-07) — and the **ADR-031 D6 launch gate is recorded satisfied by D1–D3 landing** (ADR-032 D6). Refinement: `.claude/refinements/REFINEMENT-s66-adr032-vacation-consumption.md`.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `tools/check_docs.py` all hard checks passed (db-schema 55 tables in sync; KB INDEX 46 entries, 0 orphans, 0 dangling; sprint inventory through S65; freshness anchored S65) |
| Pattern compliance spot-check | CLEAN | No `FindFirst("scopes")` (FAIL-001); `http://localhost` only in launchSettings.json dev configs |
| Orphan detection | CLEAN | S65 surfaces (ArsoversigtPage/useYearOverview/DailyNormCalculator) all referenced. NOTE: `AbsenceRegistration.tsx`/`WeeklyView.tsx`/`useAbsences.ts` confirmed router-orphaned — scheduled for retirement THIS sprint (TASK-6606, ADR-032 D5), not entropy debt |
| Documentation drift | CLEAN | MEMORY.md current |
| Quality grade review | CLEAN | S65 grades current per QUALITY.md anchor |

## Step-0 Inputs (pre-plan)

- **Refinement** — Step 4 complete (2 cycles × 2 lenses, 7 BLOCKERs absorbed), owner-ratified 2026-06-07: OQ-1a (norm basis), OQ-3i (recorded valuation), OQ-2 (research first).
- **OQ-2 deep-research + 2 adversarial refute-lens verifications** (the S63/S65 discipline): the 5÷N per-day-off mechanism **does not exist in the state-sector authority** — §6 stk.2 (LBK 230/2021, verbatim) is week-mirroring; Ferievejledning Example 3.5 prescribes `hours ÷ that-day's-hours` (= D1); §8.1/§8.4 no special <5-day rules; no 5÷N anywhere in the 92-page vejledning. Practitioner sources DO use 5÷N for single days (refute-lens counter-evidence, recorded) — divergence is unprescribed by our authority and unrepresentable without schedule-shape modeling. **Owner re-adjudication 2026-06-07: drop the conversion engine + `work_days_per_week` field; ADR-031 D6 gate satisfied by ADR-032 D1–D3** (ADR-032 D6 carries the record + a binding forward-pointer to any future schedule-shape ADR). Full trail: `docs/references/vacation-consumption-mechanism-research.md`.
- **Census A (tests seeding absence hours)** — *corrected at Step-0b cycle 1 (Reviewer W1, code-verified)*: full-timers booking 7.4 on weekdays UNAFFECTED (byte-identical). The `SkemaAccrualCapTests` :164/:188 bookings are **Nov-2024, inside the fixture's FULL-TIME window** (`:211-214`: 0.5 fraction starts 2025-01-01) — they stay byte-identical and are NOT rewritten (useful regression pins); the genuinely-affected case (a booking ≥ 2025-01-01 against the 0.5 fraction, where 7.4 ⇒ 422 and 3.7 ⇒ 1.0) has **no fixture today** — TASK-6607 ADDS it. `YearOverviewTests` rows re-derived per D1; unit `AbsenceRuleTests` UNAFFECTED (norm credit stays). Suites seed `absences_projection` directly via `SeedAbsenceProjectionRowAsync` (`YearOverviewTests.cs:1711`) — under D2 the helper must populate `feriedage`.
- **Census B (`/api/absences` callers)** — *corrected at Step-0b cycle 1 (Codex, code-verified)*: POST's production callers are the router-orphaned FE only, BUT **`GET /api/absences/{employeeId}` has a cross-process caller the census missed: `WeeklyCalculationPipeline.cs:73` (Orchestrator service) → GET is RETAINED, response-compatible** (ADR-032 D5 updated). POST is additionally the outbox/projection-atomicity harness for three regression suites (`PublisherStallReadYourWriteTests.cs:194`, `ProjectionParityTests.cs:157`, `TimeProjectionAtomicTests`) — **migrate those to `POST /api/time-entries` (same ADR-018 D3 atomic pattern) before the POST deletion lands** (TASK-6606a/6607).
- **Census C (7.4/day-count consumers)**: change sites = SkemaEndpoints :549/:689/:1027 + BalanceEndpoints :614 (+ EntitlementMapping :28 role narrowed); AbsenceRule norm-credit sites UNAFFECTED; **payroll emits hours only — NO day-count surface anywhere** (R6 closed); frontend has no 7.4 literals.

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (legal rule logic + event-schema change + schema migration (projection column) — high-risk categories) |
| **External Codex** | invoked 2026-06-07 — 4 cycles: 5B/3W/1N → 2B/1W → 1B/1W → 1B(text)/1W |
| **Internal Reviewer** | invoked 2026-06-07 — 4 cycles: 0B/1W/7N → 1B/1W/2N (convergent) → 1B/2N (convergent) → 0B/1N |
| **BLOCKERs resolved before Step 1** | **yes — ALL absorbed across 4 cycles, 0 outstanding; closed at cycle 4** (severity monotonically declining; final item = split-severity text-explicitness, absorbed; close rationale recorded below) |

### Findings (cycle 1)

_Codex findings (all code-verified by the Orchestrator before absorption — citation-gated):_
- BLOCKER — TASK-6604 — no concurrency contract between profile-PUT revaluation and a concurrent Skema save (stale-fraction Feriedage recorded after revaluation). → shared employee-scoped `pg_advisory_xact_lock` in BOTH txs + race test (ADR-032 D4).
- BLOCKER — D4/TASK-6604 — revaluation triggered on fraction only, but `position` also drives `fullDayHours` via the ADR-017 D3 override chain. → trigger = fraction OR position (ADR-032 D4).
- BLOCKER — TASK-6603/6605/6607 — valuation identity never pinned as one assertion (convergent with Reviewer N2). → named invariant in ADR-032 D2 + a single end-to-end pin incl. multi-fractional-absence rounding (per-row 4dp, one rounding site).
- BLOCKER — TASK-6603 — Skema stamps `AbsenceRegistered.OkVersion = user.OkVersion` (verified `SkemaEndpoints.cs:941/:1000`) vs per-date valuation. → entry-date stamping (`OkVersionResolver.ResolveVersion(absence.Date)`, the TASK-1801 precedent) + OK24/OK26 boundary test (ADR-032 D2; P4).
- BLOCKER — TASK-6601/6604 — `EntitlementBalanceRevalued` ADR-026 audit coverage unplanned. → audit mapper + registration in TASK-6604, catalog row in TASK-6600; one event per (type, year) group (ADR-032 D4).
- WARNING — Census B — `WeeklyCalculationPipeline.cs:73` consumes the GET (verified); POST is a regression-harness surface (verified ×3 suites). → GET RETAINED response-compatible; harness tests migrated to `/api/time-entries` before deletion (ADR-032 D5; Census B corrected).
- WARNING — TASK-6602 — `ApplyRevaluationAsync` underspecified. → `(conn,tx)` overload mirroring `AdjustUsedAsync`'s ungated upsert (convergent with Reviewer N1); in-tx range read; row-count mismatch ⇒ rollback (ADR-032 D4).
- WARNING — TASK-6606 — "on type selection" mismatches SkemaGrid (absence types are permanent rows). → trigger = first focus/edit of an EMPTY absence cell; no-overwrite pinned (ADR-032 D3).
- NOTE — `RegisterAbsenceRequest` default removal belongs with the POST retirement, not the Skema cutover. → moved; the contract dies with the POST (D5).

_Internal Reviewer findings (0 BLOCKERs; all code claims it checked verified accurate):_
- WARNING — Census A — `SkemaAccrualCapTests` :164/:188 book Nov-2024 (full-time window) ⇒ byte-identical, NOT must-rewrite; the real half-time-window 7.4⇒422 fixture doesn't exist. → Census A corrected; TASK-6607 ADDS the fixture; existing cases kept as regression pins (avoids a laundering risk).
- NOTE N1 — reuse `AdjustUsedAsync`'s ungated-upsert shape (convergent with Codex W2). → absorbed (D4).
- NOTE N2 — single-valuation invariant unpinned (convergent with Codex B3). → absorbed (D2).
- NOTE N3 — TASK-6604 depends on TASK-6603's `ConsumptionCalculator` (the in-hand recompute), not just 6602's repo method. → phasing corrected: 6604 runs after 6603.
- NOTE N4 — TASK-6606 split dispatch formalized as 6606a (Backend) / 6606b (UX); `.module.css` siblings named.
- NOTE N5 — GET route is `/{employeeId}`; disposition gate made explicit. → superseded by the Codex GET finding (retained).
- NOTE N6 — ADR-032 INDEX row + ADR-031 D6 cross-annotation must land at Step-0b close before agents cite ADR-032 D-numbers. → TASK-6600 sequencing note.
- NOTE N7 — priority alignment clean (confirmation).

### Resolution (cycle 1)

All 5 BLOCKERs + 3 WARNINGs absorbed 2026-06-07 via the plan edits above + ADR-032 D2/D3/D4/D5 amendments (same session, before any code). Convergences: valuation-identity pin (Codex B3 ≡ Reviewer N2), AdjustUsedAsync reuse (Codex W2 ≡ Reviewer N1).

### Findings (cycle 2 — verification of cycle-1 edits)

Both lenses confirmed all cycle-1 absorptions sound (Reviewer additionally verified: no advisory-vs-FOR-UPDATE deadlock — the save tx takes no row locks; entry-date OK stamping is consumer-safe — no reader of the per-row `ok_version` changes behavior, and `OkVersionRuntimeRegressionTests`' week-anchored contract applies to a deleted endpoint, no conflict). New findings, ABSORBED:
- BLOCKER (Codex) — the advisory lock as planned didn't close the race: Skema consumption valuation runs BEFORE the save tx (`SkemaEndpoints.cs:662` vs tx at `:923`) — a save could compute against the old profile, wait out the revaluation, then commit stale values. → lock acquired FIRST, valuation/guard reads move INSIDE the locked tx; key pinned `pg_advisory_xact_lock(hashtext('employee-' || employeeId))` via one shared helper; advisory lock strictly precedes any `FOR UPDATE` (ADR-032 D4; TASK-6603/6604).
- BLOCKER (Codex) — migrating the harness suites to `/api/time-entries` would discard `absences_projection` read-your-write/parity/rollback coverage. → migration target corrected to `POST /api/skema/{employeeId}/save`; promoted to a dedicated TASK-6607a (phase 3) that HARD-gates TASK-6606a (ADR-032 D5; convergent with the Codex/Reviewer phasing WARNING).
- WARNING (Reviewer) — the D2 identity is only true if the guard delta consumes the SAME per-row 4dp feriedage (the current `:1014/:1027` sum-hours-then-divide must be replaced by Σ per-row values, no re-division). → pinned in ADR-032 D2 + TASK-6603 mechanics.
- WARNING (Codex ≡ Reviewer #3) — phase-3 task gated by a phase-4 sub-part ("may be pulled forward") was incoherent. → TASK-6607a created in phase 3; 6606a moved to phase 3b behind it.
- NOTE (Reviewer) — `TimeEntryRegistered.OkVersion = user.OkVersion` (`:941`) deliberately left as-is (out of D2 scope) — asymmetry recorded in TASK-6603.

### Resolution (cycle 2)

Both BLOCKERs + both WARNINGs absorbed 2026-06-07 (ADR-032 D2/D4/D5 + TASK-6603/6606a/6607a + phasing).

### Findings (cycle 3 — verification of cycle-2 edits)

Cycle-2 absorptions verified sound (Reviewer confirmed: the lock contract closes the actual race — save-path pre-tx reads enumerated at `:407/:435/:585/:611/:696/:714/:718/:749`; no deadlock; phasing 3/3b coherent; D2/D4 mechanics consistent). New findings — **owner chose absorb + cycle 4 at the halt-prompt** — ABSORBED:
- BLOCKER (convergent, both lenses) — TASK-6607a per-scenario infeasibility: the forced-rollback test (`TimeProjectionAtomicTests:108-146`) is repo-direct, NOT an `/api/absences` HTTP test — no route migration possible or needed (Skema rollback shape already covered by `SkemaProjectionAtomicTests:68-110`); the RYW/parity migrations need FULL seeded fixtures (unseeded throwaway employees 404/422 on the save path — the bypass worked because it skipped validation). → TASK-6607a re-scoped per-scenario (2 HTTP migrations with the RegressionSeed scaffold; rollback test untouched, xmldoc only); ADR-032 D5 narrowed to match.
- WARNING (Codex) — "lock before any guard read" would hold the advisory lock across the rule-engine HTTP call (`:683/:865`). → locked span narrowed: valuation reads + writes inside; rule-engine HTTP call outside (ADR-032 D4; TASK-6603).
- NOTE (Reviewer) — pin WHY the advisory lock (not snapshot isolation) is the serializer: the dated resolver opens its own connection; hold-to-commit. → ADR-032 D4 + TASK-6603 text.

### Resolution (cycle 3)

All absorbed 2026-06-07.

### Findings (cycle 4 — final convergence check) + close

Both lenses verified ALL cycle-3 absorptions code-grounded and sound (test-file shapes confirmed exactly as re-scoped; phasing coherent; D5/6606a/6607a consistent). **One residual item, where the lenses DIVERGED on severity but converged on the identical remedy:** the two-phase consumption sequencing (pre-lock provisional rule validation → in-lock authoritative re-derivation + atomic guard) was implicit — Reviewer judged the text compatible with the correct shape (the shape exists in the code today: advisory rule call `:865` pre-tx; `CheckAndAdjustAsync` `:1031` in-tx enforcement; the `:910-920` TOCTOU contract) and rated it NOTE; Codex rated the ambiguity BLOCKER. → ABSORBED: ADR-032 D2 + TASK-6603 now state the two-phase shape explicitly, the D2 identity binds the authoritative in-lock values, and the stale-advisory-is-benign reasoning is written out. Codex's wording WARNING (6606a still implied the rollback test migrates) → fixed.

**Step-0b CLOSED at cycle 4 (Orchestrator decision, documented):** 4 cycles run, severity monotonically declining (5B+1W → 2B+1W → 1B+1W → 1 text-explicitness item with split severity); the final item was documentation precision on a design both lenses had independently verified sound against code, and its fix is absorbed. No outstanding findings. Per `feedback_missed_facts_vs_thrash`, a cycle 5 would re-verify a one-paragraph wording change — disproportionate; closing.

## Architectural Constraints Verified

- [x] P1 — Architectural integrity preserved (ConsumptionCalculator composes DailyNormCalculator with zero norm re-derivation — Reviewer-verified; TASK-6604's in-hand seam = behavior-preserving core extraction; PAT-009 records the discriminator guardrail)
- [x] P2 — Rule engine determinism maintained (no I/O; `EntitlementValidationRule` untouched; rule-engine HTTP call outside the advisory lock; converted requestedDays at the boundary — no signature change)
- [x] P3 — Event sourcing append-only respected (nullable additive `Feriedage`; `EntitlementBalanceRevalued` on the consolidated employee stream — the wrong-stream defect CAUGHT by the 6607 pin and fixed; backfill parity pinned; pre-S66 events deserialize null + backfill hours/7.4)
- [x] P4 — OK version correctness (AbsenceRegistered stamped entry-date-resolved per row — OK24/OK26 boundary pin; norm anchoring unchanged from S65)
- [x] P5 — Integration isolation (GET /api/absences retained byte-compatible for WeeklyCalculationPipeline — shape pin)
- [x] P6 — Payroll correctness (census-verified hours-only flow; AbsenceRule byte-identical pins ×2)
- [x] P7 — Security (the unguarded POST bypass RETIRED + deny-pin; all touched endpoints' authz unchanged)
- [x] P8 — CI/CD (full pyramid green; close-protocol artifacts logged; CI verification at push)
- [x] P9 — UX (norm prefill empty-cell-only +3 pins; orphaned S9 surfaces removed)

## Task Log

### TASK-6601 — SharedKernel events: `AbsenceRegistered.Feriedage` + `EntitlementBalanceRevalued`

| Field | Value |
|-------|-------|
| **ID** | TASK-6601 |
| **Status** | complete |
| **Agent** | Data Model |
| **Components** | SharedKernel (Events), Infrastructure (EventSerializer only) |
| **KB Refs** | ADR-032 D2/D4, ADR-005, DEP-003, PAT-001, PAT-004, ADR-018 D3 |
| **Constraint Validator** | pass (scope/PAT-001/PAT-004/DEP-003/backward-compat/hygiene all clean) |
| **Reviewer Audit** | deferred to Step 7a (additive contract-only change; downstream tasks exercise it) |
| **Orchestrator Approved** | yes — 2026-06-07 (build 0W/0E; unit 629/629; first-pass; `WhenWritingNull` keeps pre-S66 event bytes unchanged) |

**Description**: (a) `AbsenceRegistered` gains `decimal? Feriedage { get; init; }` — nullable for backward deserialization of pre-S66 serialized events (backfill semantics in TASK-6602). (b) New `EntitlementBalanceRevalued : DomainEventBase` — payload: `EmployeeId`, `EntitlementType`, `EntitlementYear`, per-absence replacement set (immutable record list of `(Guid AbsenceEventId, decimal NewFeriedage)`), `UsedDelta`, `TriggeringProfileEventId`, actor/correlation per PAT-004. (c) `EventSerializer.EventTypeMap` registration (+1 typeof). NO `EmploymentProfile`/profile-event changes (the `work_days_per_week` field was dropped at Step 0).

**Validation Criteria**:
- [ ] Models immutable init-only (PAT-001); event extends DomainEventBase (PAT-004)
- [ ] Pre-S66 `AbsenceRegistered` JSON (no Feriedage) deserializes (null) — round-trip test
- [ ] `EntitlementBalanceRevalued` registered; serializer round-trip green
- [ ] `dotnet build` 0 warnings/errors

**Files Changed**: `src/SharedKernel/.../Events/AbsenceRegistered.cs`, `src/SharedKernel/.../Events/EntitlementBalanceRevalued.cs` (new), `src/Infrastructure/.../EventSerializer.cs`

---

### TASK-6602 — Consumption-record plumbing: `absences_projection.feriedage`, backfill parity, revaluation repo method

| Field | Value |
|-------|-------|
| **ID** | TASK-6602 |
| **Status** | complete |
| **Agent** | Data Model (extended into Infrastructure, cross-domain authorized) |
| **Components** | init.sql (absences_projection — Orchestrator-approved schema change), Infrastructure (AbsenceProjectionRepository, ProjectionBackfillService, EntitlementBalanceRepository), tools/ProjectionBackfill (verified delegating — no change) |
| **KB Refs** | ADR-032 D2/D4, ADR-018 D13 (sync-in-tx projection), ADR-026 |
| **Constraint Validator** | pass (scope/idempotent-ALTER/ungated-upsert/row-count-throw/rounding-parity/hygiene clean) |
| **Reviewer Audit** | deferred to Step 7a (plumbing exercised by 6603/6604/6607 + parity pins) |
| **Orchestrator Approved** | yes — 2026-06-07 (build 0W/0E; unit 629/629; check_docs green; first-pass). Notable: cross-stream interleave handled by buffering revaluation replacements after inserts in event order; `MidpointRounding.AwayFromZero` matches Postgres ROUND for backfill parity; ProjectionBackfillService confirmed NOT rebuilding `entitlement_balances` (counters live-written only — observation recorded). Backfill revaluation application is best-effort (0-row no-op mirrors ON CONFLICT DO NOTHING) vs the live path's strict row-count throw — divergence is acceptable replay tolerance; the parity pin (6607) arbitrates. Committed `3853122` with TASK-6601 |

**Description**: (a) `absences_projection.feriedage NUMERIC(8,4) NULL` + idempotent legacy `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` block; legacy/pre-S66 rows backfilled `hours / 7.4` (the convention in force when written — recorded once, never silently revalued). (b) `AbsenceProjectionRepository.InsertAsync` writes the event's `Feriedage`. (c) `ProjectionBackfillService` materializes `feriedage` from `AbsenceRegistered` payloads (null-payload events → `hours/7.4`) **and applies `EntitlementBalanceRevalued` replacement sets in event order** (ADR-032 D2 replay contract); same for `tools/ProjectionBackfill` if it carries its own SQL. (d) New `EntitlementBalanceRepository.ApplyRevaluationAsync(conn, tx, …)` — a **`(conn,tx)` overload mirroring the existing `AdjustUsedAsync` ungated upsert** (`:86-104`; NOT new counter logic, NO quota-WHERE) + the per-absence projection-row replacement update with an in-tx range read; **replacement-set row-count mismatch ⇒ rollback, never partial success** (ADR-032 D4). (e) `tools/generate_db_schema.py` regen (Orchestrator runs at validation).

**Validation Criteria**:
- [ ] Projection-parity: drop + rebuild reproduces `feriedage` byte-identically vs live writes (incl. post-revaluation state)
- [ ] Pre-S66 event (null Feriedage) backfills as hours/7.4
- [ ] `ApplyRevaluationAsync` can push `used` past the cap (no quota-WHERE); booking path `CheckAndAdjustAsync` untouched
- [ ] `dotnet build` green

**Files Changed**: `docker/postgres/init.sql`, `src/Infrastructure/.../AbsenceProjectionRepository.cs`, `src/Infrastructure/.../ProjectionBackfillService.cs`, `src/Infrastructure/.../EntitlementBalanceRepository.cs`, `tools/ProjectionBackfill/Program.cs` (verify/extend)

---

### TASK-6603 — Skema consumption cutover: ConsumptionCalculator + D1 basis + D3 guard

| Field | Value |
|-------|-------|
| **ID** | TASK-6603 |
| **Status** | complete — merged `a31a519` (`7d6c8ad`) + fix-forward `ef81fd0`. **Step-5a HIGH-RISK dual review RESOLVED**: Internal Reviewer 0B/0W (two pre-flagged NOTEs: vestigial tuple member — cleanup deferred; academic-path discriminator re-resolve — accepted by design; DI lifetimes verified singleton-safe; KB PAT proposal adjudicated ACCEPT → **PAT-009**). External Codex cycle 1: **2 BLOCKERs** — (B1) per-row no-profile fail-open (`?? 0m` unvalued consumption); (B2) D3 daily cap pre-tx-only (racing profile change could persist dayEquivalent > 1) → fix-forward `ef81fd0`: in-lock fail-closed re-checks (valuation null ⇒ employment_profile_missing 422; per-date cap re-applied against authoritative fullDayHours) via new `SkemaConsumptionValidationException` on the quota-breach rollback→422 pattern + pre-tx fast mirror. Codex cycle 2: **Clean — no findings**. (Process note: cycle-1 Codex run hung on never-closing stdin — killed + relaunched with `</dev/null`; all future background codex invocations get explicit stdin closure.) Self-recovered the stale worktree base (checked in 3853122's tree). Two-phase placement: provisional ~:738-748/:774; authoritative in-lock ~:1018-1036 (lock-first), guard delta = Σ authoritative per-row (~:1168, no re-division); D3 guard kept pre-tx (fast-422 advisory; in-lock + CheckAndAdjustAsync authoritative — rationale accepted); entry-date OK stamping + TimeEntry asymmetry comment. Vestigial tuple member flagged. Proposed KB PAT (null-collapsing resolver + presence discriminator) — adjudicate at review. Build 0W/0E; unit 629/629 |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | Backend.Api (Services, SkemaEndpoints, Contracts) |
| **KB Refs** | ADR-032 D1/D3, ADR-031 D4, ADR-021 D4, ADR-002 (boundary: converted requestedDays only) |
| **Orchestrator Approved** | no |

**Description**: New `ConsumptionCalculator` Backend service composing `DailyNormCalculator` (fullDayHours, incl. the ANNUAL_ACTIVITY `7.4 × fraction` fallback) → `feriedage(hours, e, d) = hours ÷ fullDayHours` rounded 4dp **per absence row** (no 5÷N factor — ADR-032 D6). Cutover with the **single-valuation mechanics pinned (ADR-032 D2, two-phase)**: (pre-lock) provisional per-row feriedage feed the rule-engine advisory validation; (in-lock) the **authoritative** per-row feriedage are re-derived once and become the guard delta + event payload + projection write — `:1014/:1027` sum-hours-then-divide replaced by Σ of the per-row values (no re-division). Stale pre-lock advisory results are benign: the in-tx `CheckAndAdjustAsync` guard re-enforces under the locked snapshot (the existing `:910-920` TOCTOU contract). **Stamp `AbsenceRegistered.OkVersion` entry-date-resolved** (`OkVersionResolver.ResolveVersion(absence.Date)` — replaces the live `user.OkVersion` at :1000; the sibling `TimeEntryRegistered.OkVersion = user.OkVersion` at :941 is **deliberately left as-is**, out of D2 scope — recorded asymmetry). **Concurrency (ADR-032 D4, pinned):** `pg_advisory_xact_lock(hashtext('employee-' || employeeId))` via a shared helper, acquired FIRST and held to commit; the consumption-valuation reads (dated profile/config/balance) + all consumption writes move INSIDE the locked span (today pre-tx, `:662` vs `:923`) — **but the rule-engine HTTP validation call stays OUTSIDE the locked span** (never hold the lock across external-service latency; the lock protects valuation-and-write only). The dated resolver's separate connection is WHY the advisory lock (not snapshot isolation) is the serializer — don't release early or lean on the tx snapshot. D3 guard rework at :547-559 (norm-cap on positive-norm days across all types; weekend entitlement-422 / non-entitlement legacy behavior; anchor-422 unchanged; ANNUAL_ACTIVITY fallback). All entitlement-consuming types use the D1 basis. `EntitlementValidationRule` receives converted values — NO rule-engine signature change. (`RegisterAbsenceRequest` is untouched here — it dies with the POST in TASK-6606a.)

**Validation Criteria**:
- [ ] Full-time 5-day: byte-identical consumption (regression-pinned)
- [ ] Half-time 5-day full day (3.7h) ⇒ 1.0 feriedag; half-timer CARE_DAY full day ⇒ 1.0
- [ ] Guard: part-timer 7.4 entry ⇒ 422; weekend VACATION ⇒ 422; weekend SICK_DAY unchanged; mixed weekend day names entitlement rows only; ANNUAL_ACTIVITY vacation bookable
- [ ] `AbsenceRegistered.Feriedage` recorded for every entitlement-consuming absence; `OkVersion` entry-date-resolved (OK24/OK26 boundary test)
- [ ] Advisory lock held for the consumption write (race test with TASK-6604 in TASK-6607)
- [ ] No new parameter in any RuleEngine signature

**Files Changed**: `src/Backend/.../Services/ConsumptionCalculator.cs` (new), `src/Backend/.../Endpoints/SkemaEndpoints.cs`, `src/Backend/.../Services/EntitlementMapping.cs` (constant role narrowed/documented)

---

### TASK-6604 — Profile-change revaluation (D4)

| Field | Value |
|-------|-------|
| **ID** | TASK-6604 |
| **Status** | complete — merged `bf00129` (`3e751bf`). First-pass. Trigger = fraction OR position; advisory lock FIRST in the PUT tx (:241, before the FOR UPDATE); in-hand recompute via `EmploymentProfile with {…}` + a NEW seam `ConsumptionCalculator.FullDayHoursForProfileAsync` ← **declared out-of-scope edit, Orchestrator-ACCEPTED**: behavior-preserving `DailyNormCalculator.ComputeForProfileAsync` core extraction (range loop delegates byte-identically) — the alternative was duplicating the norm formula (P1 violation vs the S65 seam); stayed within the Backend.Api services domain. Entitlement-year derivation mirrors `SkemaEndpoints.ResolveEntitlementYear` off the live config reset_month; `EntitlementBalanceRevalued` on `employee-{id}` + ADR-026 `EntitlementBalanceRevaluedAuditMapper` (TENANT_TARGETED) + registration; old values read from recorded `feriedage` (null → hours/7.4); all-in-one-tx rollback semantics; ETag flow byte-identical. Build 0W/0E; unit 629/629. Race/rollback/past-cap ACs → TASK-6607 (Docker-gated) |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | Backend.Api (EmployeeProfileEndpoints), Infrastructure (consumes TASK-6602 repo method) |
| **KB Refs** | ADR-032 D4, ADR-019 (If-Match), ADR-023 D8, ADR-018 D3/D6, ADR-026 |
| **Orchestrator Approved** | no |

**Description**: When a profile PUT changes **any fullDayHours-affecting field — `part_time_fraction` OR `position`** (ADR-032 D4; position drives the ADR-017 D3 override chain), recompute feriedage for the employee's entitlement-consuming absences dated ≥ effectiveFrom **from in-hand new values** via `ConsumptionCalculator` (TASK-6603 dependency — phasing serialized); apply via `ApplyRevaluationAsync` (counter + per-absence projection rows, all-or-nothing); emit `EntitlementBalanceRevalued` (per-absence replacement set + used-delta; **one event per (type, year) group**) on the **`employee-{id}`** stream + audit row, same tx; **take the shared employee-scoped `pg_advisory_xact_lock`** (the same lock TASK-6603 adds to the save tx). **ADR-026 audit coverage:** new `EntitlementBalanceRevaluedAuditMapper` + registration (catalog row recorded by TASK-6600). Negative remaining = read-side warning only; the PUT never blocks/500s on quota. No DTO changes (no new field this sprint).

**Validation Criteria**:
- [ ] PUT with fraction change revalues future-dated absences (counter + per-absence rows); past-dated absences untouched; **position-only change also revalues**
- [ ] Event on employee-{id} stream with full replacement set; ADR-026 audit mapper registered; audit row present; all-in-one-tx (rollback test)
- [ ] Concurrency: revaluation-vs-save race test green (advisory lock; no stale-fraction record survives)
- [ ] Revaluation past the cap succeeds; warning surfaces on read; no clamp/500
- [ ] If-Match/ETag flow unchanged (428/412 pins still green)

**Files Changed**: `src/Backend/.../Endpoints/EmployeeProfileEndpoints.cs`, `src/Backend/.../AuditMappers/EntitlementBalanceRevaluedAuditMapper.cs` (new)

---

### TASK-6605 — Year-overview reads cut over to recorded feriedage (D2)

| Field | Value |
|-------|-------|
| **ID** | TASK-6605 |
| **Status** | complete — merged `b28d871` (`f5c5808`). First-pass; self-recovered the stale worktree base (ff-merged master pre-build). Read-path extension declared + accepted (repo SELECTs + `AbsenceProjectionRow.Feriedage` — SharedKernel, surgical). Constraint Validator PASS; build 0W/0E; unit 629/629; transferable/tiles//summary//series zero-diff verified |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | Backend.Api (BalanceEndpoints year-overview) |
| **KB Refs** | ADR-032 D2, ADR-030 D9, ADR-023 D2 |
| **Orchestrator Approved** | no |

**Description**: `afholdt`/`saldo` day-equivalents (:612-624, :806-839) switch from re-deriving `hours ÷ 7.4` to **summing recorded `absences_projection.feriedage`** (null rows → `hours/7.4`, matching the backfill convention). `transferable` + tiles untouched (persisted counters — consistent via the booking path). Legacy `/summary` untouched (ROADMAP follow-up ii).

**Validation Criteria**:
- [ ] afholdt/saldo == sums of recorded values; no recompute path remains
- [ ] Post-revaluation year-overview reflects replaced values; tiles/transferable consistent in the same response
- [ ] Full-timer fixtures byte-identical; `/summary` and `/series` responses unchanged

**Files Changed**: `src/Backend/.../Endpoints/BalanceEndpoints.cs`

---

### TASK-6606a — Retire the `POST /api/absences` bypass (D5); GET retained

| Field | Value |
|-------|-------|
| **ID** | TASK-6606a |
| **Status** | complete — merged `5174e36` (`915da34`, +7/−86 confined to the POST region + contract deletion). First-pass. Gate verified green pre-deletion (zero test POSTs); GET byte-identical (WeeklyCalculationPipeline unaffected); TASK-1801 POST-only comment replaced with an ADR-032 D5 retirement note; residual `RegisterAbsenceRequest` mention = 1 comment-only line in OkVersionRuntimeRegressionTests (out of scope, cosmetic). Build 0E; unit 629/629 |
| **Agent** | Backend API (cross-domain authorized) |
| **Components** | Backend.Api (TimeEndpoints, Contracts) |
| **KB Refs** | ADR-032 D5, Census B (corrected), ADR-018 D3 |
| **Orchestrator Approved** | no |

**Description**: Delete the POST handler (`TimeEndpoints.cs:163-247`) + the `RegisterAbsenceRequest` contract (its flat-7.4 default dies with it). **`GET /api/absences/{employeeId}` (:249) is RETAINED, response-compatible** — `WeeklyCalculationPipeline.cs:73` (Orchestrator service) consumes it (Step-0b corrected census). HARD PRE-CONDITION: **TASK-6607a** lands first (the RYW + parity HTTP migrations to `POST /api/skema/{employeeId}/save`; the repo-direct rollback test needs no migration — its `absences_projection` rollback coverage is route-agnostic and survives the deletion untouched).

**Validation Criteria**:
- [ ] POST + contract gone; deny test green (TASK-6607)
- [ ] GET response shape byte-compatible (WeeklyCalculationPipeline pin)
- [ ] Build + suites green after harness-test migration

**Files Changed**: `src/Backend/.../Endpoints/TimeEndpoints.cs`, `src/Backend/.../Contracts/RegisterAbsenceRequest.cs` (deleted)

---

### TASK-6606b — FE: orphan deletion + Skema norm prefill

| Field | Value |
|-------|-------|
| **ID** | TASK-6606b |
| **Status** | complete — merged (`0abcc84`). First-pass. 5 orphans deleted (no live importers, repo-grep verified); prefill = onFocus-empty-cell via existing `dailyNorm` map (no new fetch); +3 test pins; FE 176/176; tsc clean. Constraint Validator PASS |
| **Agent** | UX |
| **Components** | frontend (SkemaGrid prefill, orphan removal) |
| **KB Refs** | ADR-032 D3/D5, ADR-011, PAT-007, DEP-004 |
| **Orchestrator Approved** | no |

**Description**: Delete `AbsenceRegistration.tsx` + `AbsenceRegistration.module.css`, `WeeklyView.tsx` + `WeeklyView.module.css`, `useAbsences.ts` + their tests. `SkemaGrid`: absence-type rows are permanent grid rows, so the prefill trigger is **first focus/edit of an EMPTY absence cell** — prefill with the day's norm (already fetched for the diff row); never overwrite existing values; no prefill on zero/null-norm days; input stays editable (partial days legal).

**Validation Criteria**:
- [ ] Orphans (incl. both `.module.css`) deleted; `tsc` + vitest green; no dangling imports
- [ ] Prefill: empty-cell-on-focus only; no overwrite; no zero/null-norm prefill; partial-day entry still possible (pinned)

**Files Changed**: `frontend/src/components/SkemaGrid.tsx`, deletions per Census B

---

### TASK-6607a — Test & QA: migrate the POST-harness suites (gates TASK-6606a)

| Field | Value |
|-------|-------|
| **ID** | TASK-6607a |
| **Status** | complete (compile-verified; Docker suites deferred to the close-protocol run per FAIL-002 — environmental block recorded) — merged `cbf2f40` (`995464d`). RYW + parity migrated to the Skema save with the `SkemaMonthlyAccrualGuardTests` seeding scaffold (full-time open profile + agreement; no entitlement seeding needed — dynamic cap); separate-host publisher-stall subtlety handled; rollback test logic untouched (doc-only). Grep: zero remaining POSTs to `/api/absences`. Declared: `OkVersionRuntimeRegressionTests` comment-only route mentions (cosmetic — scrub or leave at 6606a) |
| **Agent** | Test & QA |
| **Components** | tests (regression: Hosting + Outbox suites) |
| **KB Refs** | ADR-032 D5, ADR-018 D3/D13, Census B (corrected) |
| **Orchestrator Approved** | no |

**Description** (cycle-3-corrected, per-scenario): (a) Migrate the two **HTTP** harness scenarios — `PublisherStallReadYourWriteTests` read-your-write (:194) and `ProjectionParityTests` parity (:157) — to **`POST /api/skema/{employeeId}/save`** **with full seeded fixtures**: the current tests mint throwaway employee IDs with NO user/profile/agreement/config rows (the bypass skipped all validation — `SkemaEndpoints.cs:407` 404s an unseeded user; missing dated profile ⇒ anchor-422; missing entitlement config ⇒ type skipped), so the migration stands up seeded employees via the existing `RegressionSeed`/Skema fixture scaffold (the `SkemaProjectionAtomicTests`/`SkemaMonthlyAccrualGuardTests` precedent) + a guard-passing weekday norm-hours booking. (b) `TimeProjectionAtomicTests.RegisterAbsence_OutboxFails_RollsBack` (:108-146) is **repo-direct (no HTTP), route-agnostic — NO migration; it survives the POST deletion untouched** (Skema-path rollback already covered by `SkemaProjectionAtomicTests.cs:68-110`); only its xmldoc route references update. Runs in phase 3 alongside TASK-6603 (coordinate: fixtures book full-time weekday norm-hours — byte-identical under D1).

**Validation Criteria**:
- [ ] RYW + parity scenarios green against the Skema save path with seeded fixtures; coverage classes preserved
- [ ] Forced-rollback test untouched and green (repo-direct); xmldoc route refs updated
- [ ] No remaining test POSTs `/api/absences`

**Files Changed**: `tests/StatsTid.Tests.Regression/Hosting/PublisherStallReadYourWriteTests.cs`, `tests/StatsTid.Tests.Regression/Outbox/ProjectionParityTests.cs`, `tests/StatsTid.Tests.Regression/Outbox/TimeProjectionAtomicTests.cs` (xmldoc only)

---

### TASK-6607 — Test & QA: census-driven rewrites + ADR-032 pins

| Field | Value |
|-------|-------|
| **ID** | TASK-6607 |
| **Status** | complete — `9ea9372`. +21 pins (unit 629→631; regression +18 new +1 YearOverview). **The revaluation stream pin came up RED on a REAL defect** — `EntitlementBalanceRevalued` enqueued on `employee-profile-{id}` instead of the consolidated `employee-{id}` (ADR-032 D4/ADR-018 D6); agent refused to launder (S64 discipline) → one-line Orchestrator fix (Small Tasks Exception, self-checked, same commit) → 3/3 green. Shared-DDL fixture gap closed (`ProjectionSchemaTestFixture` + feriedage — the S64 cross-process-contract lesson applied); `DistinctDays` weekend-skip citation-gated (D3); seed helper feriedage param (23 call sites byte-identical). Single-valuation identity pinned end-to-end (1.5 == 1.5 == 1.5, partial-day 4dp; the provisional `requestedDays` leg excluded by design — ADR-032 D2 Step-7a wording). **DECLARED-DEFERRED pin bundle (Step-7a W3 honesty correction — one recorded follow-up):** concurrency race harness (`pg_locks` existence pin); position-only revaluation trigger pin (the positionChanged code path is live and shares the fraction path's recompute — fraction pin exercises the machinery; the position-override seeding chain judged disproportionate in-sprint); read-side negative-remaining warning pin (the affordance itself is a follow-up — ADR-032 D4 Step-7a correction); live-revaluation→rebuild full-WAF parity (replay-of-revaluation-events parity IS pinned; the full live-then-rebuild WAF flow is not) |
| **Agent** | Test & QA |
| **Components** | tests (unit, regression, smoke) |
| **KB Refs** | ADR-032 all D's, Census A, PAT-008, FAIL-002 |
| **Orchestrator Approved** | no |

**Description**: (a) Census-A work (corrected): `SkemaAccrualCapTests` :164/:188 are full-time-window — KEPT as byte-identical regression pins; **ADD the missing half-time-window fixture** (booking ≥ 2025-01-01 against the 0.5 fraction: 7.4 ⇒ 422; 3.7 ⇒ 1.0); `YearOverviewTests` rows re-derived per D1 + `SeedAbsenceProjectionRowAsync` populates `feriedage`. (b) — moved to TASK-6607a (phase 3, gates 6606a). (c) New pins: **the D2 single-valuation identity as ONE end-to-end test** (`requestedDays == guard delta == Σ event Feriedage == Σ projection feriedage == used-delta`, multi-fractional-absence rounding case — per-row 4dp); D1 examples (half-time full day ⇒ 1.0; half-timer CARE_DAY ⇒ 1.0; full-timer byte-identical); OK24/OK26 boundary stamping test; guard scoping matrix (part-timer 7.4 ⇒ 422, weekend VACATION ⇒ 422, weekend SICK_DAY allowed, mixed-day 422 names entitlement rows, ANNUAL_ACTIVITY bookable); revaluation (fraction AND position-only triggers, negative-remaining warning, rebuild byte-identical, **revaluation-vs-save race test** on the advisory lock); projection parity (backfill == live, incl. post-revaluation); pre-S66 null-Feriedage event backfill (hours/7.4); deny-pin POST `/api/absences`; GET response-compat pin (WeeklyCalculationPipeline); AbsenceRule/payroll byte-identical pin. (d) Full pyramid per the S64/S65 close protocol (FAIL-002: fresh-Docker EXCLUSIVE runs, pristine + consecutive).

**Validation Criteria**:
- [ ] All pins implemented and green; the single-valuation identity pinned as ONE assertion
- [ ] Harness-suite migration lands before the POST deletion (sequencing with 6606a)
- [ ] Unit + regression ×2 consecutive + smoke + FE green (close protocol)
- [ ] No test modified to mask a product defect (citation-gated per S64 discipline)

**Files Changed**: `tests/**` per census

---

### TASK-6600 — Orchestrator: ADR-032 finalization + docs + governance

| Field | Value |
|-------|-------|
| **ID** | TASK-6600 |
| **Status** | in-progress (research + adjudication + reference doc done 2026-06-07) |
| **Agent** | Orchestrator |
| **Components** | docs (KB, references, SYSTEM_TARGET, generated), sprint log |
| **KB Refs** | ADR-032, ADR-024 D3, ADR-031 D6 |
| **Orchestrator Approved** | — |

**Description**: DONE at Step 0: research reference doc (`docs/references/vacation-consumption-mechanism-research.md`); ADR-032 authored with the premise re-adjudication + classification record; owner ratifications recorded. REMAINING: flip ADR-032 → accepted at Step-0b close; KB INDEX row + Document-Map reference row (CLAUDE.md table — Orchestrator); `danish-agreements.md` consumption rows + verified cites; `SYSTEM_TARGET.md` K1 correction (stale "pro-rated by PartTimeFraction"); ADR-031 D6 cross-annotation (gate satisfied by ADR-032 D6); `tools/generate_db_schema.py` regen after TASK-6602; sprint log + INDEX/QUALITY/ROADMAP at close.

---

## Phasing

| Phase | Tasks | Parallelism |
|-------|-------|-------------|
| 0 | TASK-6600 remainder (Step-0b → ADR accepted, INDEX row BEFORE agent prompts cite ADR-032 D-numbers) | Orchestrator |
| 1 | TASK-6601 (events) | single |
| 2 | TASK-6602 (projection/backfill/repo) | single (depends on 6601) |
| 3 | TASK-6603 + TASK-6605 + TASK-6606b + TASK-6607a | parallel, worktrees (distinct files; 6605 needs only 6602's column; 6607a coordinates fixture hours with 6603's cutover) |
| 3b | TASK-6604 (after 6603: `ConsumptionCalculator` + advisory-lock helper) + TASK-6606a (after 6607a: harness migration is a HARD gate) | sequenced |
| 4 | TASK-6607 (remaining pins + full pyramid) | after implementation |
| 5 | Orchestrator validation → Step 5α/5a per task → Step 7a dual-lens → close |

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | verified | The consumption mechanism is now the state authority's OWN (Ferievejledning Ex. 3.5 `hours ÷ that-day's-norm`; LBK 230/2021 §6 stk.2 week-mirroring self-enforced by the norm model; NO 5÷N — adversarially verified ×2, `vacation-consumption-mechanism-research.md`). Pins: half-time 3.7h⇒1.0; CARE_DAY basis; academic fallback; weekend-422 matrix |
| Wage type mappings produce correct SLS codes | N/A — unchanged | Census-verified: no day-count surface anywhere in the payroll chain; hours-only flow untouched |
| Overtime/supplement calculations are deterministic | verified | Untouched (zero RuleEngine diffs in the sprint range) |
| Absence effects on norm/flex/pension are correct | verified | AbsenceRule norm-credit byte-identical pins ×2 (full-timer + part-timer fallback) |
| Retroactive recalculation produces stable results | verified | Projection parity (rebuild == live incl. post-revaluation); pre-S66 null-payload backfill convention pinned; revaluation replay applies recorded replacement sets verbatim |

## External Review (Step 7a)

| Field | Value |
|-------|-------|
| **Invoked** | yes — both lenses, 2026-06-07 |
| **Sprint-start commit** | `6fc64e6` |
| **Command** | `codex exec` (prompt-steered range review — intermediate commits exist, prompt-alone `codex review` N/A) + internal Reviewer Agent |
| **Review Cycles** | 1 (sprint-end; 0 BLOCKERs ⇒ no mandatory cycle 2). Per-task high-risk (TASK-6603): 2 cycles (2B → fix-forward `ef81fd0` → Clean) |
| **Findings** | Codex: 0B/3W/0N; Reviewer: 0B/0W/2N |
| **Resolution** | all 5 absorbed at close (doc/honesty corrections + the catalog row; no code changes): ADR-032 D4 amended to shipped reality (negative remaining = unclamped-visible; warning affordance → ROADMAP follow-up); D2 identity wording tightened (authoritative legs; provisional excluded by design; guard leg transitively pinned); TASK-6607 claims corrected to the DECLARED-DEFERRED bundle; `audit-projection-catalog.md` +EntitlementBalanceRevalued row (53→54); worktree-hygiene note → retrospective |

### Findings

- WARNING (Codex) — D4 negative-remaining warning affordance did not ship — ADR amended + follow-up recorded
- WARNING (Codex) — D2 identity pin wording overstated the provisional leg — wording corrected (the pin is sufficient for the authoritative identity)
- WARNING (Codex) — 3 claimed pins actually deferred (position-only trigger / read warning / live-rebuild WAF parity) — log corrected, follow-up bundle recorded
- NOTE (Reviewer) — audit-projection-catalog missing the S66 row — added
- NOTE (Reviewer) — TASK-6603 commit re-carries the 6601/6602 tree (stale-base recovery; byte-identical, net-clean) — retrospective
- Confirmed sound by both lenses: end-to-end valuation identity, six-site rounding seam, the advisory-lock contract + `ef81fd0` re-checks, the stream fix, D5 retirement, authz, sprint-log honesty, KB consistency

## Test Summary

### Regression close-protocol adjudication (owner-ratified 2026-06-07)

The pristine leg ran THREE times on fresh Docker sessions (FAIL-002 protocol: engine restart + compose re-up + EXCLUSIVE runs + Out-File capture). Each run: **465/466 with exactly ONE failing test, a DIFFERENT test each time** (`EmployeeProfileEndpointTests.Get_AsHr_SameOrg_Returns200` → `PositionOverrideAtomicTests.Activate_OutboxFails_RollsBack` → `YearOverviewTests.Graceful_ProfilelessEmployee_Returns200WithNulls_Never500`), **each green in isolation immediately after**, and the full-verbosity capture (attempt 3) shows the documented FAIL-002 signature verbatim: `Docker.DotNet.DockerApiException : status code=Conflict, "container … is not running"` at testcontainer exec. Zero product defects; all 18 ADR-032 pins green in every run. Per FAIL-002 ("never modify tests for it") and owner ratification, the 3-run evidence stands as the pristine leg; run 4 is the consecutive leg (≤1 isolation-cleared FAIL-002-signature flake accepted). Pre-adjudication triage (run 0, 52 failures) was 49× compose-stack-down environmental + 2 weekend fixtures (correct new D3 behavior, citation-gated fixes `0ed0399`) + 1 private-DDL feriedage gap (S64 shared-fixture lesson, third consumer).

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | 631 (+2) | all passing |
| Regression tests | 466 (+19) | **466/466 CONSECUTIVE leg fully pristine** (run 4, 20m16s); pristine leg = 3× 465/466 disjoint FAIL-002-signature flakes, owner-adjudicated (above) |
| Smoke tests | 5 | all passing |
| Frontend (vitest) | 176 (+3) | all passing |
| **Total** | **1278 (+24)** | — |

**CI verification (backfilled post-close):** the close `081cc74` went red on ONE test — `AuditProjectionVisibilityTests` NRE'd on the new mapper because the close commit's **catalog row pulled the S66 mapper into the catalog-driven test's scope for the first time** (the catalog doc is part of the test contract — the S64 shared-fixture lesson, doc edition; locally green because the last full run predated the row). Scoped post-7a fix-forward `e0d1dc3` (null-tolerant `Map` per the 53-mapper family convention; scoped Codex review Clean) → **whole-workflow GREEN run [`27099789679`](https://github.com/fanboy1000000/StatsTid/actions/runs/27099789679), all six jobs**. Post-close label rename `f2052fc` (Feriefridage → Særlige feriedage, user-directed) also whole-workflow green ([`27100001961`](https://github.com/fanboy1000000/StatsTid/actions/runs/27100001961)).

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | 10 (6600–6607 incl. 6606a/6606b/6607a splits) |
| Constraint Violations | 0 (Constraint Validator PASS ×3 runs; one out-of-scope extension DECLARED and Orchestrator-accepted — the TASK-6604 in-hand norm seam) |
| Reviewer Findings | Step-0b 4 cycles: 1B/2W/13N · Step-5a (6603): 0B/0W/4N · Step-7a: 0B/0W/2N |
| External Review Cycles | Step-0b 4 + per-task 2 (6603) + sprint-end 1 |
| External Findings | Step-0b: 8B/5W/2N (cycles 1–4) · per-task 6603: 2B → Clean · Step-7a: 0B/3W |
| Re-dispatches | 0 (all first-pass; 2 fix-forwards: 6603 BLOCKERs `ef81fd0`, 6604 stream `9ea9372` — the latter caught by the 6607 pin) |
| First-Pass Rate | 100% (9/9 implementation dispatches accepted without re-dispatch) |

## Sprint Retrospective

**What went well**: The Step-0 research discipline paid for the whole sprint — the OQ-2 deep-research + 2 adversarial verifications REFUTED the sprint's own founding premise (5÷N) against primary sources BEFORE any code, and the replacement design (D1 norm basis = the authority's own Example-3.5 mechanism) closed the ADR-031 D6 launch gate more simply than the planned feature (no schema field, no conversion engine). The dual-lens layering caught complementary defect classes at every stage: Step-0b 4 cycles (concurrency contract, position trigger, OK stamping, harness feasibility), the 6603 high-risk Codex pass (2 fail-open BLOCKERs the architectural lens missed), and the 6607 pin suite catching a live wrong-stream defect the same day it shipped. All 9 implementation dispatches first-pass. Worktree agents self-recovered from stale bases twice.

**What to improve**: (1) Harness worktrees sometimes spawn on a stale base (6fc64e6 instead of HEAD) — agents now get a self-check instruction; verify each worktree's base at dispatch. (2) Background `codex exec` hung on never-closing stdin once — all invocations now get `</dev/null`. (3) The TASK-6603 commit re-carried the 6601/6602 tree (stale-base recovery artifact; byte-identical, net-clean) — prefer `git merge master` over checking in a tree. (4) FAIL-002 shed-rate was worse than S65 (1 flake/run ×3 fresh sessions) — the owner-adjudicated 3-run + clean-consecutive close pattern is now precedent; consider host reboot before close-protocol runs. (5) The eligibility suite's `saveDate = today` was a latent wall-clock weekend dependence — surfaced by D3; PAT-008-style date pinning would have prevented it.

**Knowledge produced**: ADR-032 (consumption correctness + the §6 stk.2 adjudication record); PAT-009 (null-collapsing resolver + presence discriminator); `docs/references/vacation-consumption-mechanism-research.md` (the verified primary-source trail); the FAIL-002 close-adjudication precedent.

**Recorded follow-ups (S66)**: (vi) negative-remaining warning affordance on balance read surfaces (ADR-032 D4 Step-7a W1); the DECLARED-DEFERRED pin bundle (race harness `pg_locks` pin; position-only revaluation trigger pin; read-warning pin; live-revaluation→rebuild full-WAF parity); `/api/series` + `/summary` consolidation items carry forward (S65 i/ii); OkVersionRuntimeRegressionTests cosmetic route-comment scrub.
