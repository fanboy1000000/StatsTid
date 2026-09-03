# Sprint 138 — Time-control Increment 3: temporal editing (backdating + the HR diagnostic worklist)

| Field | Value |
|-------|-------|
| **Sprint** | 138 |
| **Status** | complete |
| **Start Date** | 2026-09-02 |
| **End Date** | 2026-09-03 |
| **Orchestrator Approved** | yes — 2026-09-03 (Step-5a dual-lens terminal, 2 BLOCKER absorbed; Step-7a Codex terminal at cycle 2 clean; Reviewer close verdict `CLOSE-WITH-WARNINGS` in `.claude/reviews/SPRINT-138-step7a-reviewer.md` — all 5 WARNINGs + 6 NOTEs absorbed before this commit) |
| **Build Verified** | yes — `dotnet build StatsTid.sln -c Release --no-incremental` **0 errors** on the final tree (145 pre-existing warnings; CA2100 distinct sites 115 = CI baseline) |
| **Test Verified** | local: unit **1235/1235** (+146) · DemoSeed **165/165** · regression non-Docker **102/102** · frontend **735/735** + `tsc` clean — all green on the final tree; Docker-gated pins (~70 new facts) + smoke: **CI-pending** — watched close run, backfilled here when green (Docker unavailable on the owner's machine, standing instruction) |

## Sprint Goal

History becomes CORRECTABLE, truthfully. HR can record that an employee's part-time fraction, position,
agreement code or employment category actually changed on a PAST date: the dated history is split at that
date (a new row inserted between existing rows, or the live row superseded from the past date), the absence
consumption in exactly that interval is re-recorded under the corrected values, and every already-EXPORTED
payroll month the correction touches lands on an HR-only diagnostic worklist that points at the existing
manual recalculation path (ADR-013: no cascade, no auto-recalculation). Settled holiday years are never
rewritten silently: the revaluation skips them and the worklist flags them for the reverse-then-re-settle
path. Future-dating is DEFERRED to Increment 4 by owner ruling (the open-ended-row readers and the live
caches would treat a not-yet-effective row as current — recorded as the ADR-040 D8 amendment). Also lands
the S137 deferrals: category editability + the NOT-NULL tightening, the leaver-send dead-end, the
`EmploymentWindow` helpers, the retroactive-correction window pin.

**Inputs:** ADR-040 D8 (as amended 2026-09-02) · refinement
`.claude/refinements/REFINEMENT-s138-increment3-temporal-editing.md` **rev 4.2** — seam-recon-grounded;
dual-lens converged: Reviewer cycle 1 (1 BLOCKER / 9 WARNING / 5 NOTE — the BLOCKER: rev-1's settled-year
recommendation confused fraction-flat EARNING with fraction-dependent CONSUMPTION) → rev 2 → cycle 2 (0B/2W/5N
on the rewrite) → rev 3 → cycle 3 (0B/2W/5N, all absorbed); Codex cycle 1 on rev 2 (4 BLOCKER / 3 WARNING /
1 NOTE — two coincided with the Reviewer's; the others: the replay rule was impossible as written for an
interior backdate, and the agreement-code If-Match token is `users.version`) → rev 4 → cycle 2 (0B/2W textual,
absorbed) · **owner rulings 2026-09-02:** OQ-1 (a) backdating + today now, future-dating → Increment 4 under
the "current ≠ live" read-model precondition (ADR-040 §Amendment); OQ-2 (i) skip settled (type, year) groups +
flag as SETTLED_YEAR worklist rows for all three trigger kinds; OQ-3 (a) QUAL-149/150 OUT, visible on the
worklist; OQ-4 (a) `PeriodStart` sentinel deferred. S137 CI green (`33622368503`, backfilled `95ae0b2`) — the
CI-health close gate is satisfied at open. **Owner-raised during the refinement's Step 5 (2026-09-03):** an inventory + analysis of every process HR must follow up on (the worklist is one of ~9 such hand-offs) — registered on ROADMAP as its own backlog theme "HR operations — the follow-up processes"; feeds Increment 4.

## Entropy Scan Findings (Step 0a)

- **KB path validation:** CLEAN — every `src/**/*.cs` path cited in a KB entry exists (the single regex hit `src/styles/tokens.cs` is a false positive on the frontend `tokens.css`).
- **Pattern compliance spot-check:** CLEAN — `FindFirst("scopes")` 0 hits; `http://localhost` in non-test source 0 hits; 136 `Map(Get|Post|Put|Delete)` definitions vs 138 `RequireAuthorization` calls in the endpoint files (every route file authorizes).
- **Orphan detection:** CLEAN for the S136/S137 file set (every new file is referenced by its tests or DI).
- **Documentation drift:** the deferred-items list lives in ROADMAP (no MEMORY.md); S137 close routed every
  deferral there — CLEAN.
- **Quality grades:** updated at the S137 close (`docs/QUALITY.md` anchor 137) — CLEAN.

## Step 0b (plan review)

Served by the refinement's Step 4 dual-lens (2 lenses × 3 and 2 cycles; the S134/S137 precedent). Load-bearing
absorptions: backdating is cache-safe, future-dating is not (the central claim, verified by both lenses); the
generalized covering-row writer with a PURE dispatch router over six cases (A / B' / C' / E / G / T) — T at the
repository level only, the profile PUT keeps its "no open row → 404" so an edit can never resurrect a retired
profile; ONE concurrency token per aggregate (profile: the live row's version; agreement code: `users.version`,
bumped atomically) — two backdates against the same token serialize and the second 412s; the pre-image, the
mutation predicate, `previous_data` and the event predecessor fields come from the COVERING row, never the live
row (else a backdated code equal to today's silently no-ops and the cache is corrupted from the request value);
the revaluation window is the new row's interval `[from, oldTo)` and SKIPS settled (type, year) groups; the
worklist has TWO row kinds (EXPORTED_MONTH keyed on the export record by REFERENCE — no FK across the ADR-034
Payroll ownership line — and SETTLED_YEAR keyed on `(entitlement_type, entitlement_year)`), a JSONB trigger set
with per-trigger `baselineContentHash` / `baselineSettlementSequence` captured at APPEND time so
`recalculatedSince` / `reversedSince` are derived exactly, Created + Resolved outbox events, If-Match on
resolve, per-trigger `recalcBlockedBy` (QUAL-149 profile / QUAL-150 agreement); replay stays historical-
manifest replay — an interior backdate is invisible to it by construction, the correction RE-PLAN is the only
path to the corrected truth (three legs pinned); the general users PUT stays active-only (no reactivation
side-door) while HR corrects a departed employee's agreement code through a dedicated terminated-inclusive
endpoint; the same-values no-op adopts the S23 repository-decided shape; the category field is "the fourth
field" of the writer, not a parallel task; RED-first Docker matrices derived from the spec.

## Scope & Task Decomposition

| Task | Wave | Agent | Deliverable |
|------|------|-------|-------------|
| TASK-13801 | 1 | Infrastructure (cross-domain authorized into SharedKernel/Events — four additive members) | The generalized dated writer in BOTH repositories (`EmployeeProfileRepository`, `UserAgreementCodeRepository`): lock the live row (timeline lock) + the row covering `req.EffectiveFrom`; pure `TemporalWriteRouter.Decide` over A / B' / C' / E / G / T; result records gain the covering pre-image, `NewEffectiveFrom/To`, `Kind` (incl. `Inserted`), `IsNoOp` (S23 shape, decided post-lock/post-If-Match); the fourth field `employment_category`; cache refresh FROM the row covering today (a users-row write: bumps `users.version` + `users_audit`); `EmployeeProfileCreated/Updated/Superseded` + `EmploymentCategory`, both `…Superseded` + `NewEffectiveTo` (additive, non-required); backdate floor 422 semantics surfaced to the caller; DB-free router matrix |
| TASK-13803 | 1 | Data Model + Infrastructure + Backend (cross-domain authorized) | `hr_backdate_worklist` (two kinds, CHECK-tied keys, `triggers JSONB`, per-kind partial UNIQUEs, `version`) — init.sql segment (Orchestrator approves) + migration replay test; in-tx writers `WriteForExportedMonthsAsync` / `WriteForSettledYearsAsync` (active `vacation_settlements` row via `EntitlementPeriodResolver`); `BackdateWorklistRowCreated` / `…Resolved` events (DEP-003 ×2 + ADR-026 mappers); `GET /api/hr/backdate-worklist` (per-employee terminated-inclusive scope / org-subtree filter) with derived `recalcBlockedBy` (set), `recalculatedSince`, `reversedSince`; `POST …/{id}/resolve` (If-Match + audit + event); typed contracts |
| TASK-13805 | 1 | Backend | Leaver-send dead-end: BOTH active-only reads in `ExecuteSendAsync` switched (scope check → `ValidateEmployeeAccessIncludingTerminatedAsync(…, LocalHR)`; subject read → `GetByIdIncludingTerminatedAsync`) with the S136 in-lock subject re-check; pins: HR sends a deactivated leaver's approved month; the leaver's own token cannot (SEC-046) |
| TASK-13806 | 1 | SharedKernel + Payroll tests (Step-5a listed — `PeriodPlanner` is payroll-boundary) | `EmploymentWindow.Overlaps/ClipTo/FirstNotEmployedDay` + one fencepost matrix; the three hand-rolled predicates consume them byte-identically (S137 pins green); the pure compliance union test moves to Tests.Unit; the `REST_PERIOD_CHECK` mirror pinned; the retroactive-correction window pin (Docker) |
| TASK-13802 | 2 | Backend (Step-5a target) | `EmployeeProfileEndpoints` PUT: validator `<= today`; covering-row-sourced pre-image / predicate / `previous_data` / event predecessor fields; `RevalueAbsencesInIntervalAsync(from, oldTo)` with the settled-group SKIP (pinned on SPECIAL_HOLIDAY); worklist writers called for all three trigger kinds; `EmploymentCategory` accepted (fourth field); terminated-inclusive scope path (LocalHR floor); `IsNoOp` → skip revaluation + worklist. `AdminEndpoints` users PUT: agreement-code validator `<= today` for ACTIVE subjects only (no widening of the `is_active` lock); the NEW dedicated `PUT /api/admin/users/{id}/agreement-code` (terminated-inclusive; If-Match on `users.version`; the full emission/audit set) |
| TASK-13804 | 2 | Data Model (+ Infrastructure resolver file) | NOT-NULL tightening: census FAILS LOUD then `ALTER COLUMN employment_category SET NOT NULL` (segment + replay test incl. the RED path); COALESCE reads retired (resolver; the repository's one line sequenced after 13801); typed-contract regen (`docs/api/openapi.json` + `api-types.ts`) at merge (Orchestrator) |
| TASK-13809 | 2b | Test & QA (dispatched 2026-09-03 from 13804's declared blocker) | Test-seed sweep: every `INSERT INTO employee_profiles` in 25 Regression/Smoke files supplies `employment_category` (the production scalar-subselect pattern; the shared `RegressionSeed` first) + the `EmploymentProfileResolverDateOverlayTests` retired-COALESCE flip. The two Migrations files EXCLUDED (historical-schema replays by design) |
| TASK-13810 | 3 | Backend + Infrastructure (owner rulings at Step-5a, 2026-09-03) | (1) The settled-year worklist flag: keyed on the settlement's valuation boundary — the last day it counted, renamed at Step-7a from "crystallization date" — CONJOINED with the entitlement-window geometry (Orchestrator ruling — each predicate alone over-flags on a different axis), plus a third writer entry point raising a row whenever the revaluation ACTUALLY skipped a group; both paths collapse to one row with two triggers. (2) Both correction endpoints answer with the as-of-today state (no wire-shape change) |
| TASK-13807 | 3 | Test & QA | RED-first Docker matrices derived from the spec: writer A/B'/C'/E/G/T × live/history/gap for BOTH repos; concurrency (same token → 412; sequential retries succeed); revaluation interval + settled skip; cache-after-write; silent-no-op shapes; worklist rows per exported month + settled year incl. append + derived flags; NOT-NULL census RED; leaver send; replay-vs-re-plan three legs (leg (ii) service-level with the injected straddle-safe provider; HTTP leg = the QUAL-149 refusal); users PUT 412 after a category change |
| TASK-13808 | close | Orchestrator | Step-5a dual-lens on 13801/13802/13803/13804/13806, Step-7a, registers (QUAL-149/150 worklist visibility; the S138 residuals), ADR-040 amendment (recorded at open), ROADMAP precondition (recorded at open), sprint-test-validation, watched close push |

**Sequencing:** Wave 1: 13801 ∥ 13803 ∥ 13805 ∥ 13806 (disjoint files) → Wave 2: 13802 ∥ 13804 (endpoint write
handlers / migration + resolver + regen) → 13807 → close. Fixed at dispatch: the 13802↔13803 writer signatures
`WriteForExportedMonthsAsync(conn, tx, employeeId, trigger, from, toExclusive)` /
`WriteForSettledYearsAsync(…)`; the event members live in 13801 so Wave 2 compiles standalone.

## Wave 1 acceptance (Orchestrator, 2026-09-03)

**TASK-13805 (leaver-send dead-end) — accepted.** Plain language: a departed employee's FINAL month is exactly
the one payroll needs certified, yet deactivation made the send command unable to address them — in-scope HR got
a 403, a GlobalAdmin a 404. Both active-only reads inside `ExecuteSendAsync` are switched to the S136
terminated-inclusive pair: the scope check → `ValidateEmployeeAccessIncludingTerminatedAsync(actor, id,
sendFloor)` (self → null, other → LocalHR); the subject read → a new step 5a′ `GetByIdIncludingTerminatedAsync
(conn, tx, …)` + the ADR-040 D3 floor (deactivated subject ⇒ LocalHR+ or a date-free 403 — the writers' exact
string). Because the send takes the advisory lock as its FIRST statement, its single in-tx read IS the
authoritative check (the writers' pre-tx/in-lock two-step collapses to one — stated in the comment). Two
recorded consequences, both mirroring S136 decisions: a terminated Employee-self now gets an explicit 403 (was
an accidental 404); access is checked before period status, so a terminated self against an approved month gets
403 (was 409). A deactivated LocalHR sending their own month is admitted (D3's letter — the S136 Step-5a
adjudication (a), pinned "take back to the owner if it reddens"). SEC-046 HELD. Tests (Docker, CI):
`SendCommandTerminatedSubjectTests` 10 methods / 13 cases incl. the composed final-month case (end 03-13, facts
through the last day → 200, whole-month geometry per D6) and the leaver's-own-token refusals on both adapters.
Orchestrator follow-through (Small Tasks): the R9c allowlist inventories in `UserRepository` + `OrgScopeValidator`
now list the send command; SEC-046 register row noted; the SPRINT-136 deferral marked FIXED.

**TASK-13806 (`EmploymentWindow` helpers + pins) — accepted.** Plain language: three production sites each
did the inclusive-end / unbounded-side date comparison by hand — three chances to be off by one paid day. The
arithmetic now lives ONCE on the record (`FirstNotEmployedDay`, `Overlaps`, `ClipTo`, `FirstEmployedDayWithin`
instance + a static union overload for spells-proof lists) with a 26-row fencepost matrix; `PeriodPlanner.
ResolveSegmentEmploymentStatus`, `EmploymentWindowResolver.GetWindowsAsync` and `ComplianceEndpoints.
FirstEmployedDayInMonth` (now a PRIVATE one-liner) consume it. **Byte-identity proof: all 59 S137 pins
(planner 16, truncation 16, parity 9, hydration 9, skip 6, merge 3) untouched and green.** The 11 pure union
tests moved to Tests.Unit (re-targeted at the helper; the Regression copy deleted); the `REST_PERIOD_CHECK`
string mirror pinned by reflection (no visibility widening). Inverted ranges fail loud (unreachable by every
current caller). The retroactive-correction window pin (Docker, CI): windowless April export → end date 04-15
recorded → `RetroactiveCorrectionService.RecalculateAsync` → every re-run rule call covers [04-01..04-15], the
NORMAL_HOURS correction line's difference is exactly −Σ(post-end entries), the correction manifest carries the
`EmploymentEnded` NOT_EMPLOYED suffix — four runtime assertions flagged as compiler-only pre-validation.
Observation recorded: PCS's hand-rolled End+1 (`BuildPlanForLegacyCallersAsync`) can become
`window.FirstNotEmployedDay` later (Payroll-scoped; pinned by the hydration tests).

**TASK-13801 (the generalized dated writer) — accepted.** Plain language: until now an employee's profile and
agreement history could only be EXTENDED at the end; "her fraction actually changed on the 10th, not today" was
unrecordable. Both writers now accept any past-or-today date, split the row covering it, fill gaps, and refuse
the future — with the routing decision in ONE pure, database-free class. `Temporal/TemporalWriteRouter.cs` (new):
`Decide(rows, from, today)` over A (create) / B' (update-in-place, incl. re-extending a zero-width same-day
create+delete row — ADR-020 D2 Case C) / C' (split the covering row: live → `Superseded`, history → `Inserted`) /
E (before the first row) / G (in a gap) / T (trailing gap after a soft-delete → becomes the open row); futures
refused (`TemporalWriteRejectedException`, date-free) and the employment-start floor as a caller-supplied
predicate; `TimelineSnapshot.Build` fails LOUD on an overlapping or duplicate-start timeline. Both
`SupersedeAndCreateAsync` methods: pure refusals → ONE `SELECT … FOR UPDATE` over the WHOLE timeline (`ORDER BY
(effective_to IS NULL) DESC, effective_from` — every writer locks the open row first, so no deadlock and no
overlapping gap inserts) → If-Match vs the open row → router → S23-shape `IsNoOp` → executors under one 23505
backstop → cache refresh FROM THE ROW COVERING TODAY. One token per aggregate: profile = the open row's version,
bumped on every timeline write (`BumpTokenAsync`; history rows' own version untouched; T = max+1 so a pre-delete
ETag can never match); agreement code = `users.version`, bumped atomically inside the repository, the row's own
version kept for its audit contract. Result records gain (trailing, defaulted): `Kind`, `IsNoOp`, `NewEffectiveFrom
/To`, the covering `PreImage`, `TimelineVersionBefore`, `UsersVersionBefore/After`, old/new cache values,
`UsersCacheWritten`; both `Outcome` enums gain `Inserted` + `NoOp`. The fourth field written on every row
(`COALESCE(@category, users.employment_category)`). Events: `EmploymentCategory` on the three profile events,
`NewEffectiveTo` on both Superseded events (additive, non-required; `EventSerializerCoverageTests` green).
Tests: `TemporalWriteRouterTests` 43 (every case × live/history/gap/empty, every fencepost, two per-day SWEEP
tests asserting no overlaps / exactly one open row / the requested day covered). Build 0 errors; Unit
**1169/1169**; every existing caller compiles unchanged. **Declared for Wave 2 (13802 MUST absorb):** the users PUT
still runs its own `UPDATE users … agreement_code, version + 1` BEFORE calling the writer → until 13802 drops
that assignment and sources the ETag/`version_after` from `UsersVersionAfter`, an agreement-code change via the
users PUT double-bumps `users.version` (Docker users-PUT tests asserting +1 may be red between the waves —
acceptable inside the sprint, must be gone at close). Also for 13802: map `TemporalWriteRejectedException` → 422,
honour `IsNoOp`, source pre-image/predicate/audit from `Covering`, write a `users_audit` row when
`UsersCacheWritten`. Deviations accepted with reasoning: the employment-start floor is caller-supplied (the
admin create POST may store a FUTURE hire date while writing the first row at today — an in-repo floor would
break creation); the profile cache write is value-conditional, the agreement cache write unconditional (its
bump IS the token). Audit-table doc comments (init.sql, Orchestrator-only) to state the two version meanings —
done at merge.

**TASK-13803 (the HR backdate diagnostic worklist) — accepted; init.sql segment APPROVED.** Plain language: when
HR corrects history backwards, any month already SENT to payroll and any holiday year already SETTLED become
stale; nothing may recalculate automatically (ADR-013), so this is the LIST that tells HR exactly which — with
honesty DERIVED at read time (has payroll's export hash or the settlement sequence actually moved since each
correction?), never taken from HR's verb. Table `hr_backdate_worklist` (`S138-BACKDATE-WORKLIST-SEGMENT`, file-
scope CREATE inside the markers + a ledger-only DO block — a CREATE inside the DO block would double-count in
`generate_db_schema.py`): two kinds under CHECK-tied keys (EXPORTED_MONTH: year/month/`export_id` as a REFERENCE
— no FK across the ADR-034 line; SETTLED_YEAR: `entitlement_type`/`entitlement_year`), `triggers JSONB` (non-empty
array; each element `{kind, eventId, effectiveFrom, appendedAt, actorId, baselineContentHash |
baselineSettlementSequence + State}` captured AT APPEND TIME), resolution-paired CHECK, two partial UNIQUEs on
open rows, `version`; `employee_id REFERENCES users` (the `vacation_settlements` precedent — ratified). Repository
`HrBackdateWorklistRepository` (models + the pure `BackdateWorklistDerivation` + writers in one file): the two
FIXED in-tx writers `WriteForExportedMonthsAsync` / `WriteForSettledYearsAsync(conn, tx, employeeId,
WorklistTrigger(Kind, EventId, EffectiveFrom, ActorId), from, toExclusive?, ct)` — exported months = every
`payroll_export_records` row intersecting `[from, toExclusive)` (null = open-ended, clipped at the current
Copenhagen month); settled years = every (type, year) with an ACTIVE settlement (highest sequence, state ≠
REVERSED — PENDING_REVIEW counts) whose accrual OR taking window intersects (`EntitlementPeriodResolver`, live
reset month; a type with no live config is flagged CONSERVATIVELY); insert-or-append via `ON CONFLICT` on the
partial UNIQUE; `BackdateWorklistRowCreated` (append ⇒ `Appended = true`) + the ADR-026 row in the caller's tx;
reads incl. the org-subtree listing; `ResolveAsync` → `…Resolved` event, 412/404/409 mapped. Derived, pure,
unit-pinned: `recalcBlockedBy` (QUAL-149 for profile/category triggers, QUAL-150 for agreement triggers, when
`effectiveFrom` is strictly inside the month; empty for SETTLED_YEAR), per-trigger + row-level
`recalculatedSince` (hash moved) / `reversedSince` (sequence advanced OR baseline REVERSED). Endpoints
`GET /api/hr/backdate-worklist?employeeId&open` (per-employee terminated-inclusive scope with the LocalHR floor /
org-subtree filter) and `POST …/{id}/resolve` (If-Match 428/412; non-blank reason; 404 before scope — random UUIDs
on an HR-only surface) — both HROrAbove, typed (PAT-012), mapped from `ApiEndpoints.MapAll` (the `--openapi`
path needs them there — deviation ratified). Events registered (DEP-003; no count comment exists in the map),
ADR-026 mappers TENANT_TARGETED (employee's home org). `db-schema.md` hand-written in the generator's format (68
tables). Tests: Unit **1225/1225** (+56: 47 derivation, 4 mapper, 5 serialization); non-Docker Regression 102/102
(the 11 union tests moved to Unit by 13806); **17 RED-first Docker facts deferred to CI** (migration replay ×2,
repository ×10, endpoint ×5 — expected row sets derived from the spec). CA2100 115 = baseline. Declared for the
Orchestrator: audit-projection-catalog rows (done at merge), OpenAPI + `api-types.ts` regen (after Wave 2),
QUAL-149/150 register notes (close). KB proposals accepted at close: PAT "baseline-at-append, derive-at-read";
PAT "brand-new-table migration segment shape"; FAIL "Bash heredocs over ~10 KB truncate on this machine — use
the Write tool" (the Orchestrator hit the same failure twice this sprint).

Unit suite after 13805 + 13806: **1127/1127** (1089 → +38). CA2100 **115 = baseline** (both agents replicated the
CI grep).

**Orchestrator validation of the merged Wave 1 tree (CI-faithful):** `dotnet build StatsTid.sln -c Release
--no-incremental` **0 errors** (145 pre-existing warnings); **CA2100 distinct sites 115 = baseline**; Unit
**1225/1225** (1089 → +136: 13806's 38, 13801's 44, 13803's 56, minus/plus moves); DemoSeed **165/165**; non-Docker
Regression **102/102** (113 → 102: the 11 union tests moved to Unit). Step-5α file-scope self-check: every changed
file inside its task's declared scope (13803's `ApiEndpoints.cs` map call and `employee_id REFERENCES users`
ratified as deviations). Orchestrator Small-Tasks follow-through: the two audit tables' version-meaning comments
in `init.sql` (profile = the aggregate token; agreement = the row version, client token = `users.version`); the two
audit-projection-catalog rows. **Wave 2 dispatched:** 13802 ∥ 13804.

**Step 5b — knowledge proposals adjudicated (Wave 1, 6 received → 4 entries).** ACCEPTED as entries:
**PAT-025** (13801's temporal write router + one concurrency token per aggregate — the two proposals merged:
the router/lock shape and the token contract are one pattern, useless apart) · **PAT-026** (13803's
"baseline-at-append, derive-at-read" — an honest diagnostic list over facts owned by another context) ·
**PAT-027** (13806's "date-range predicates live on the value type" — the fencepost single-source, with the
S137 pins as the byte-identity proof) · **FAIL-008** (the >10 KB Bash-heredoc truncation on this machine —
proposed by 13803, independently hit twice by the Orchestrator this sprint; now a standing working practice:
use the Write tool for sizeable files). FOLDED, not separate entries: 13803's "brand-new-table migration
segment shape" (a paragraph inside PAT-026 — markers wrap the file-scope CREATE, the ledger DO block records
only the key, never `CREATE TABLE` inside the DO block or `generate_db_schema.py` double-counts) · 13805's
"terminated-inclusive reads pair with a D3 floor on the in-lock row" (the S136 precedent it cites is already
the pattern; the S138 addition — when the command takes its advisory lock BEFORE any subject read, the single
in-tx read IS authoritative and no two-step is needed — recorded in the code comment + this log).
Register: **QUAL-149 / QUAL-150** gained S138 notes — both limitations are now VISIBLE where HR works, derived
per trigger as `recalcBlockedBy` on the worklist row.

## Wave 2 acceptance (Orchestrator, 2026-09-03)

**TASK-13804 (category NOT NULL) — accepted; init.sql segment APPROVED; one consequence RULED.** Plain language:
S137 added the dated category with a fallback — if the dated cell was empty, reads used the employee's live
value. That was safe THEN because the live value was write-once, so the two could never disagree. S138 makes the
category editable per date, and the live column becomes merely a cache of today's row — at which point the same
fallback stops rescuing and starts LYING (it would answer a March question with today's value). So the posture
flips to the house default: fail at INSERT, loudly, where the bug is. Delivered: the file-scope `ADD COLUMN` is
now `NOT NULL` (greenfield); a new ledger-guarded `S138-PROFILE-CATEGORY-NOTNULL-SEGMENT` (placed after both the
S137 segment and the Wave-1 worklist segment — pinned by a test) runs a census that RAISES naming every
offending `profile_id`, then `SET NOT NULL`; the RAISE rolls back the segment's own ledger row, so recovery is
fix-then-plain-rerun. Both read-side COALESCEs retired (`EmploymentProfileResolver`, `EmployeeProfileRepository`)
with the docs rewritten as posture + history; NO write path and NO history-row UPDATE touched (the backfill
write-once precondition holds). Tests: `ProfileCategoryNotNullMigrationTests` ×3 (legacy replay ×2 — incl. a
DELIBERATELY DIVERGED history row surviving byte-identical, which makes the write-once precondition falsifiable;
the RED path; greenfield ×2) + four honest flips stated old-vs-new (the two S137 "NULL dated cell → live"
posture pins reversed to "the UPDATE is refused 23502"; the greenfield-nullable assertion; the legacy S137-era
facts left untouched because they replay the S137 segment alone). Build 0 errors / 0 warnings; Unit 1225/1225.

**RULED (Orchestrator, 2026-09-03) — the pre-S137 upgrade ordering.** Because the doc generator parses only
`ADD COLUMN` text, the file-scope column must itself say NOT NULL for `db-schema.md` to be truthful; and
PostgreSQL refuses `ADD COLUMN … NOT NULL` on a non-empty table. Consequence: a PRE-S137 database that still
holds profile rows can no longer be upgraded by applying the current init.sql directly — it must pass through an
S137-era release first, or be reseeded. **Accepted:** the failure is loud and immediate (never silent
mislabelling), nothing is deployed, and the runbook now carries the ordering constraint explicitly (S137 + S138
rows added — the S137 row was MISSING from the runbook, a gap from that close, now filled). The rejected
alternative — branch on table-emptiness to pick NULL vs NOT NULL — would make the doc generator's first-match
parse order load-bearing, the kind of cleverness that rots.

**Two scope deviations accepted:** `ProfileCategoryMigrationTests` (one assertion + doc — leaving a knowingly-red
test was worse than the stretch) and `IEmploymentProfileResolver`'s contract doc (comment-only; it explicitly
promised the COALESCE the change retires).

**BLOCKER surfaced by 13804, dispatched as TASK-13809 (Test & QA sweep):** 25 test files seed
`employee_profiles` without a category and would fail 23502 in Docker CI — verified by the Orchestrator
(26 sites; the shared `RegressionSeed` is the highest-leverage one). The two Migrations test files (9 further sites — 27 files / 35 sites in the whole corpus) are EXCLUDED
by design: their inserts run against reconstructed HISTORICAL schemas where the column is absent or still
nullable — that is the point of those tests. The sweep also flips
`EmploymentProfileResolverDateOverlayTests`'s retired-COALESCE fact. Production is unaffected (every production
write path already populates the column); this is purely test-corpus catch-up.

**TASK-13809 (test-seed category sweep) — accepted.** All 25 files / 26 sites done with the PRODUCTION pattern (a scalar
subselect against the `users` row each test already seeds) — **zero literals needed**: at every site the users
row is committed or written earlier in the same statement batch, so the subselect always resolves. Notable
sites handled correctly rather than pattern-matched: the 3-VALUES-row contract test (one subselect per row);
the smoke test's `INSERT … SELECT … WHERE NOT EXISTS` (the subselect went into the SELECT list, so `emp001`
idempotence is untouched); two Payroll files that use `RegressionSeed` AND their own supersession insert (both
live, both needed it). The item-23 flip: `…DatedCellNull_DegradesToLiveValue` →
`…NullingDatedCell_RefusedByNotNull_RowKeepsItsOwnValue` — the manufactured "missed write" now throws 23502,
and the second leg is genuinely falsifiable because the seeder moves the LIVE value to `'Fuldmægtig'` while the
row keeps `'Standard'`, so any residual fallback would go red. Class doc rewritten to explain WHY the posture
reversed. The two Migrations files untouched, verified by an automated audit (their 5 pre-tightening inserts
are the only remaining category-less ones in `tests/`). Build 0 errors; Unit 1225/1225; CA2100 115 = baseline.
**Verification that matters:** the agent grepped every `EmploymentCategory` assertion in Regression/Smoke to
confirm no existing test's RESOLVED category changes — previously `'Standard'` via COALESCE-to-live, now
`'Standard'` via the copy — so nothing keyed on `(employment_category, agreement_code, ok_version)` (e.g.
`role_config_overrides`) shifts. Also confirmed: TASK-13802's new test file already seeds the column.

**Entropy finding (surfaced, NOT actioned — the Orchestrator did not create these):** the two S131 sweep
worktrees at `.claude/worktrees/s131-docdrift` and `s131-scored` are real registered git worktrees, detached at
`7e4bb1b` (the S131 sweep baseline), untouched since 2026-08-19, **164 MB**, gitignored, holding PRE-S137 copies
of the same test files this sweep just fixed — so they pollute repo-wide greps and could mislead a future sweep
(exactly how the agent hit them). Their working trees contain ONLY deletions of files present in the commit —
no new or modified work — so removal loses nothing recoverable (`git worktree remove`, and the commit stays in
history). The S131 quality sweep is CLOSED. **Registered on ROADMAP for the owner's call rather than deleted
unilaterally.**

**TASK-13802 (the two write endpoints + the dedicated agreement-code endpoint) — accepted.** Plain language: HR
can now say "her fraction actually changed on the 10th, not today" and the system splits the dated history at
that date, re-records absence consumption ONLY inside the corrected stretch, never rewrites a settled holiday
year, and lists every already-exported month for a human to recalculate. **Profile PUT:** validator `<= today`
(both refusals date-free); scope + subject read moved to the terminated-inclusive pair with the LocalHR floor
(safe here — this endpoint has no `is_active` switch, so it cannot become a reactivation path, and the leaver's
own token is still refused); the 404 pre-check REDUCED TO AN EXISTENCE PROBE but kept, so router case T can
never resurrect a retired profile; everything "before"-shaped (mutation predicate, audit `previous_data`,
Superseded predecessor fields) sourced from `result.Covering`; `users_audit` written when the cache refresh
bumped `users.version`; revaluation renamed to `RevalueAbsencesInIntervalAsync` over `[NewEffectiveFrom,
NewEffectiveTo)` with the settled-group SKIP via `GetActiveAsync`; both worklist writers called, one trigger per
changed dimension; `IsNoOp` → 200, unchanged ETag, nothing written. **Emission table is TOTAL against the
router** (`Inserted` ⇒ Superseded WITH `NewEffectiveTo`; the three pure-insert kinds ⇒ Created; `Created`/
`InsertedTrailing` unreachable through this surface but mapped rather than thrown). **Users PUT:**
`agreementCodeMutated` is now the WRITER's verdict, which kills the silent no-op where a backdated code equal to
today's code was discarded; the writer call hoisted AHEAD of the endpoint's `UPDATE users` so the one-bump rule
can see its verdict, with emissions left in place so the outbox order stays byte-preserved. **THE ONE-BUMP
RULE:** if the request carries an agreement code and the writer actually wrote, the REPOSITORY owns the bump and
the endpoint's UPDATE omits `version = version + 1`; otherwise the ENDPOINT performs the single bump. Post-commit
token is `lockedVersion + 1` in every case, and the ETag / `users_audit.version_after` are SOURCED from the party
that bumped rather than recomputed — one authority per fact. **New `PUT /api/admin/users/{userId}/agreement-code`**
with the full emission/audit set, terminated-inclusive, If-Match on `users.version`. 27 Docker facts (13 profile,
14 agreement) + one in-scope flip. Build 0 errors; Unit 1225/1225; non-Docker Regression 102/102; CA2100 115.

**RULED (Orchestrator) — the `EffectiveFrom` presence guard (the agent's declared deviation): ACCEPT.**
`EffectiveFrom` is a non-nullable `DateOnly`, so a request that OMITS it binds `0001-01-01`. Pre-S138 the
`== today` rule rejected that as a side effect; once any past date is legal, the sentinel would route as a
SILENT correction covering all recorded history — and `0001-01-01` is the backfill seeder's own anchor. That is
the worst failure mode a history-correction feature can have: silent and total. The guard (`== default` → 422,
date-free, all three surfaces) is accepted. Given up: the `0001-01-01` anchor row can no longer be addressed
through these EDIT surfaces — never a designed use case, and it stays reachable through the repository/seeder
paths. **Registered follow-up — `QUAL-152`** (registered at close, Step-7a Reviewer W4: the row did not exist when this
paragraph first claimed it did): the cleaner fix is to make the member `required` in C# so the binder refuses an
omitted field with a 400 instead of the endpoint catching a sentinel — a wire-visible change (the spec's
`required` array), so not taken mid-sprint.

**FOR STEP-5a ADJUDICATION (Orchestrator-raised, not a defect claim).** The agent notes that a plain TODAY-dated
profile edit now also calls `WriteForSettledYearsAsync`, so an employee with an ACTIVE settlement whose window
still reaches today gets a SETTLED_YEAR worklist row on an ORDINARY edit. That follows the letter of the OQ-2 (i)
ruling ("for ANY of the three trigger kinds"), but the ruling's PURPOSE was the BACKDATE case: a correction
reaching INTO a frozen year. A today-forward change does not alter anything the settlement crystallized, so the
row may be a false positive that trains HR to ignore the list — the classic failure of a diagnostic surface. Both
lenses are asked: should SETTLED_YEAR rows be raised only when the corrected interval STARTS before the
settlement boundary (i.e. genuine backdates), or is the current "any intersection" shape right?

**Orchestrator follow-through (Small Tasks + declared dependencies):** the out-of-scope RED pin
`AdminEndpointsAgreementCodeTests.PUT_BackdatedEffectiveFrom_Returns422` FLIPPED to
`…_SplitsTheDatedTimeline` (RED-on-old: old expectation 422 with `provided`/`expected`, stated in the doc;
new: 200 + both sides of the split resolve correctly + the live cache follows the row covering today); the R9c
allowlist inventories in `UserRepository` + `OrgScopeValidator` gained the two new sanctioned callers;
**typed contracts regenerated** (`docs/api/openapi.json` + `frontend/src/lib/api-types.ts`) — the spec delta is
exactly the three new operations (the dedicated agreement-code PUT + the two worklist endpoints), their typed
schemas, and the additive optional `employmentCategory`; nothing removed, so the convention gate's grandfather
manifest is untouched. **The frontend typed-contract gate then caught the new operations** (`tsc --noEmit`
failed on the exhaustive `TypedPathIn` unions in `api-typed-overloads.test.ts`) — exactly the PAT-012 pipeline
working: the two new write ops registered on the PUT/POST unions with a dated comment; `tsc --noEmit` clean.

**TASK-13810 (the two Step-5a owner rulings) — accepted, with one Orchestrator ruling on top.**

**Ruling 2 — both correction endpoints answer with the state AS OF TODAY.** Plain language: after a backdated
edit the profile PUT used to echo the values it had just written into HISTORY, so a client showing the response
as current state would display a March fraction as though it were today's; the agreement endpoint already
answered with today's value. Now both re-read the row covering today (an as-of read, deliberately NOT "the open
row" — they coincide only because future-dating is refused, and Increment 4 makes them diverge) and the shared
rule is stated at all four return sites, including the no-op branch. **No response record changed shape**, so no
contract regeneration is owed.

**Ruling 1 — the settled-year flag, and the Orchestrator ruling that corrected it.** The owner ruled: narrow the
date test onto the settlement's VALUATION BOUNDARY (called its "crystallization date" at the time of the ruling;
renamed at Step-7a because the date is the last day the settlement COUNTED, not when it was taken), and ALSO
raise a row whenever the revaluation actually
skipped a group. The agent implemented that faithfully — keyed on `VacationSettlementSnapshot.
SettlementBoundaryDate` (the `asOf` a settlement was valued at, reachable from the writer's existing query with
one extra column; an absent or `0001-01-01` value reads as "unknown" and flags conservatively), added the third
entry point `WriteForSkippedSettledYearsAsync`, and funnelled both paths through one upsert so a year hit by
both yields ONE row with two triggers — **and then flagged the consequence rather than burying it.**
**ORCHESTRATOR RULING (2026-09-03), correcting the Orchestrator's own brief:** the two predicates are
**CONJOINED, not swapped**. Each is wrong on a DIFFERENT axis: the window test alone fires on an ordinary
present-day edit (settled years' taking windows commonly run past today — the false positive the owner ruled
out); the freeze-date test alone fires for EVERY settlement frozen after an old correction, so fixing one week
of 2020 would raise rows for 2020, 2021, 2022 and every year since — six dismissals where one row is true. The
brief said "instead of the entitlement period's window"; the review's actual recommendation was to ADD a
condition. Conjoined, the rule states the only thing true of a threatened settlement: *this correction reaches
into a year that was already frozen.* Implemented as one named authority (`SettledYearThreatened`) with half (a)
short-circuiting first (it is free and exonerates the common case, so an ordinary edit never reads
`entitlement_configs` at all); each half keeps its own conservative leg. **The skip path stays UNCONDITIONAL by
design, and the code now says why: the date path PREDICTS, so it must be as accurate as possible; the skip path
REPORTS something that definitely happened, and a test there could only withhold the notice as well as the
correction.** The residual is RESOLVED, not accepted — the predicate's doc records why both halves are
necessary, with the concrete failing case for each.

**Quality note worth recording:** restoring the geometry half disturbed an existing Docker pin dated 2030, which
would have gone red for the WRONG reason (its geometry half failing rather than the boundary half under test);
the agent found and re-dated it, and added a second employee proving an unknown boundary does not rescue a
correction that misses the window. That is the failure mode a mechanical restoration usually ships.

**Counts:** Unit **1225 → 1235 (+10)** — 13 geometry facts retired then restored with the rule, 3 new conjunction
facts (the residual, its mirror, and the conservative legs through the conjunction) + the 7 boundary facts.
Non-Docker Regression 102/102. Build 0 errors; CA2100 115 = baseline. New Docker facts deferred to CI incl. the
one the Orchestrator asked for: an old narrow correction flags only the year whose window it overlaps.

## Architectural Constraints Verified

- [x] Architectural integrity — ONE concurrency token per aggregate; the PUT stays an edit surface (T is repository-only); no FK across the ADR-034 line; the router is pure; agent scopes disjoint per wave
- [x] Domain correctness — the revaluation window is the new row's interval and skips settled years; cache = the row covering today, never the request; the covering-row pre-image drives the predicate; replay-vs-re-plan legs pinned; the QUAL-149/150 limitations visible, not hidden
- [x] Auditability — SUPERSEDED + CREATED per insert-between with `NewEffectiveTo`; per-trigger baselines make "recalculated"/"reversed" derivable; Created + Resolved events via the outbox; `users_audit` on cache writes; no history row rewritten en masse (the S137 backfill premise ends with a new dated row, never an UPDATE of migrated history)
- [x] Integration isolation & delivery — worklist rows written in-tx + outbox events; no Payroll-context schema coupling; rule engine untouched
- [x] Security & access control — HR-only worklist (rows carry employment-adjacent dates); terminated-inclusive paths with the LocalHR floor; the general users PUT cannot reactivate a leaver; SEC-046 held; date-free 422s
- [x] CI/CD enforcement — RED-first Docker matrices; ≥ 2 CI iterations budgeted; CA2100 at baseline; the typed-contract gates satisfied by regen at merge

## Review (Step 5a / 7a — both lenses)

### Step 5a — external lens (Codex, per-task high-risk: schema migration + legal rule logic + payroll-adjacent + auditability)

`codex review "<per-task prompt>"` (prompt-alone form, auto-targets the uncommitted diff) on the FULL S138 tree,
steered at nine domain edges: the router's interval arithmetic at every fencepost, the lock-order claim, the two
token contracts, the revaluation window + settled-year SKIP, the worklist's derived honesty, the NOT-NULL
census, ADR-040 D7 leakage, and the terminated-inclusive widening.

**Cycle 1 — 1 finding, WARNING (P1), reported as ONE defect present SYMMETRICALLY in both writers.**

- **WARNING — the same-values no-op swallowed the zero-width REOPEN.** Plain language: soft-deleting a profile
  that was created earlier the SAME day leaves a row spanning `[today, today)` — an interval containing no days,
  so the employee is invisible to every dated read. Recreating at that date is the sanctioned recovery (ADR-020
  D2 Case C), and the router says so via `ReopensZeroWidthAnchor`. But BOTH writers ran their same-values
  short-circuit FIRST, so an operator recreating with the values already on the dormant row — the overwhelmingly
  likely case, since they are recreating exactly what was just deleted — got "nothing changed", a 200, no audit
  trail, and a still-invisible employee. **Verified against the code by the Orchestrator before acting** (the
  branch at `EmployeeProfileRepository` step 4 and its twin in `UserAgreementCodeRepository` both preceded the
  case executor and did not consult the flag). **FIXED (Orchestrator, Small Tasks Exception — the diagnosis was
  exact and the change is two guards):** the no-op branch in each writer now also requires
  `!decision.ReopensZeroWidthAnchor`, with a comment stating the rule the defect violated — *value equality is
  about FIELDS; the no-op branch is about COVERAGE*. Pinned by a NEW Docker-gated
  `TemporalWriteZeroWidthReopenTests` with one fact per writer, each asserting the RECOVERY (the dated read
  resolves again) and not merely the result flags, and each stating the old behaviour it inverts. Build 0 errors;
  Unit 1225/1225; non-Docker Regression 102/102 after the fix.

  *Why this one mattered:* it is exactly the class the sprint was built to prevent. The router was designed as
  the single authority on what a write must do, and it correctly decided "reopen" — the executor simply never

**Cycle 2 (verification of the fix):** **"No findings. The router identifies every zero-width start-date anchor,
both guards preserve the required reopen write, both B' executors apply the router's new effective end, and both
regression tests would fail before the fix while verifying dated-read visibility."** The verification was steered
at five specific questions — does the guard cover every path that reaches a zero-width anchor; can it now let a
GENUINE no-op through to a write; do the B' executors actually re-extend; are the new pins falsifiable and do
they assert COVERAGE rather than only flags; and is there any OTHER early return of the same class in these two
writers — and answered all five clean. **Step-5a external lens TERMINAL at cycle 2** (cycle 1: 1 WARNING, fixed;
cycle 2: clean; no BLOCKER at any cycle, so the halt-and-prompt condition never arose).

  asked. A pure decision record only helps if every early return honours it.




### Step 7a — external lens (Codex, sprint-end) — cycle 1: 1 WARNING → FIXED; cycle 2: clean

`codex review` on the FULL uncommitted S138 tree, told what Step-5a had already settled (so it could not
re-spend the budget there) and steered to weight TASK-13810 — the two owner rulings plus the Orchestrator's
conjunction ruling, which landed after every other review pass and were therefore the least-reviewed code in
the sprint. (The first invocation died on a `code-mode host exited during handshake` transport error, not a
verdict; re-run.)

- **WARNING (P2) — the as-of-today read assumed a row covers today, and turned that assumption into a lost
  correction.** `EmployeeProfileEndpoints.ReadProfileAsOfTodayAsync` threw when no dated row covered today, on
  the stated ground that "the handler's 404 pre-check guarantees one". **That guarantee is about an OPEN row,
  which is not the same thing** — an open row whose `effective_from` is in the FUTURE covers no day today.
  These endpoints refuse future-dating so they cannot create the shape, but seeded, imported or legacy data
  can, and nothing in the schema forbids it. The severity is not the bad response: **the read runs INSIDE the
  transaction, so the throw rolled back an otherwise VALID correction and answered 500** — discarding the write
  to protect a courtesy view of it. Exactly the "does anything silently depend on future-dating being refused?"
  hazard the Orchestrator put in the review brief. **FIXED (Orchestrator, Small Tasks Exception):** the helper
  returns a nullable tuple and `null` instead of throwing; the real-write branch falls back to what the request
  wrote, the no-op branch to the covering row's pre-image. The correction now survives in every shape — the
  response is a view of the write, never its gate — and the helper's doc records why throwing was the worse
  failure so the assumption is not reinstated.
- **Cycle 2 (verification):** **"No findings. The nullable tuple cleanly distinguishes no row from a row with a
  NULL position, both branches provide defensible fallbacks, and the transaction commits after the non-fatal
  read."** Steered at four questions — is the degenerate path genuinely non-fatal, are the fallback values
  defensible per branch, can a legitimate NULL position be confused with "no row", and does anything ELSE in the
  late changes assume a row covers today — all four answered clean. **Step-7a external lens TERMINAL at cycle 2**
  (no BLOCKER at any cycle, so the halt-and-prompt condition never arose).


### Step 5a — internal lens (Reviewer Agent, per-task, same scope as the external pass)

Two BLOCKERs, both about a capability that looked shipped and was not. Neither was a coding slip; both were
seams nobody owned, which is exactly what the internal lens is for.

- **BLOCKER 1 — the create path bumped a version nobody could see, and left a hole in the audit chain.** The
  admin user-create POST writes the `users` row and its dated agreement-code row in one transaction. The
  agreement-code writer bumps `users.version` (it owns the `users.agreement_code` cache), but the endpoint
  returned the ETag it had computed BEFORE that bump, and wrote no `users_audit` row for the second write. Two
  consequences: the client's first `If-Match` on a freshly created user would fail 412 against a version it was
  never told about, and the audit chain skipped a version — the thing an audit chain exists to make impossible
  (ADR-019). **FIXED:** the version token is now sourced FROM the writer rather than recomputed, step (2d′)
  writes the paired `users_audit` UPDATED row, and the ETag is built from the post-write value. The test was
  rewritten to assert chain CONTINUITY (no gap between consecutive audit versions) instead of a single row's
  presence — the original pin would have passed with the hole open.
- **BLOCKER 2 — the profile GET refused terminated employees, so the whole capability was unreachable
  end-to-end.** The sprint's point is correcting HISTORY. The most common reason to correct an employee's
  history is that they have LEFT. The GET used `ValidateEmployeeAccessAsync`, which rejects a terminated
  employee, so HR could not open the profile they needed to correct — the PUT worked, but no one could reach
  it through the UI. **FIXED:** switched to `ValidateEmployeeAccessIncludingTerminatedAsync`, the variant that
  already existed for exactly this case. Worth naming as a lesson: every unit test passed while the feature was
  unusable, because the tests exercised the writer and the reader separately and never the path a human takes.
- **NEW-1 (raised against the Orchestrator's own fix) — a 422 body that read as garbled English.** My first
  cut at the missing-category rejection passed a whole sentence into a template slot that expected a noun
  ("This {x} change …"), producing a message no HR user could parse. **FIXED** with a distinct enum member
  (`NoRecordedEmploymentCategory`) carrying its own sentence, rather than bending the shared template.
- **Terminal:** both Step-5a lenses closed with no open BLOCKER.

### Step 7a — internal lens (Reviewer Agent, sprint-end) — `verdict: CLOSE-WITH-WARNINGS`

No BLOCKER. Five WARNINGs and six NOTEs, all absorbed before the close commit; the artifact is at
`.claude/reviews/SPRINT-138-step7a-reviewer.md`. The three that changed behaviour or the record:

- **W1 — the settled-year rule missed a correction landing exactly ON a settlement's boundary, while calling
  that date something it is not.** Half (a) compared `correctedIntervalStart < boundary`, but the settlement's
  own day query is inclusive at both ends (`date >= @start AND date <= @end`), so a correction ON the boundary
  day changes a day the settlement counted and must select the year. **FIXED:** the comparison is now `<=` and
  the unit pin flipped from `false` to `true` with the corrected rationale. The naming half is fixed too: the
  code and docs called this a "crystallization date" / "freeze moment", which describes WHEN the settlement was
  taken. It is not that. It is the last day the settlement VALUED — a fact about coverage, not about clock
  time. Every reference now says "valuation boundary — the last day it counted", because the wrong name is
  what made the off-by-one easy to write and hard to see.
- **W2 — ADR-040's amendment still stated the rule the sprint replaced.** The 2026-09-02 amendment says a
  backdate "whose interval reaches a SETTLED ferieår", which reads as the window test alone; the sprint ships a
  CONJUNCTION. **FIXED:** a dated sub-amendment (2026-09-03) states the implemented rule, explains why neither
  half works alone, names the accepted under-flag, and the older sentence now points forward to it.
- **W3 — the code's own explanation of the two-path asymmetry was wrong in the same direction.** A comment
  claimed PROFILE *and* EMPLOYMENT_CATEGORY corrections revalue. Category is not an input to full-day hours —
  the norm config lookup is keyed on (ok-version, agreement code, position, fraction, org) and category selects
  `role_config_overrides`, which the norm path does not read — so only a PROFILE correction can have a skip to
  report, and the endpoint never revalued for category. **FIXED**, with a tripwire paragraph naming what would
  have to change for that to stop being true.
- **W4 — a "registered follow-up" was registered nowhere.** The `required EffectiveFrom` item was declared
  registered in this log with no row behind it. **FIXED:** it is `QUAL-152`, and the paragraph now cites it.
- **W5 — a diagnostic read could destroy the correction it was describing.** The settled-year path threw when
  a settlement carried no baseline. **FIXED** by graceful degradation: the row is raised with `reversedSince`
  reading "unknown" rather than 500-ing and rolling back a valid write. Same shape as the external lens's
  Step-7a WARNING, found independently — a diagnostic surface must never be able to veto the fact it reports.
## Test Summary

Per the `sprint-test-validation` procedure — every local count below was RUN on the final S138 tree (Release,
`--no-build` after a `--no-incremental` rebuild), never carried forward or estimated. Previous = the S137 close
as CI-verified in run `33622368503`.

| Suite | Previous (S137) | Current (S138) | Delta | Status |
|-------|-----------------|----------------|-------|--------|
| Unit | 1089 | **1235** | **+146** | green locally. 13801's router matrix 44 (incl. 2 per-day sweeps) · 13803's derivation/mapper/serialization 56 · 13806's `EmploymentWindow` fencepost matrix 26 + the 11 compliance-union tests MOVED here from Regression + the rule-id mirror 1 · 13810's conjunction/boundary facts (net +10 after 13 geometry facts were retired with the old rule and restored with the conjunction) · the Step-5a coupling pins 4 |
| DemoSeed | 165 | **165** | ±0 | green locally |
| Regression, non-Docker subset (`Category!=Docker`) | 113 | **102** | **−11** | green locally — the 11 pure compliance-union tests MOVED to Unit (13806); no test was deleted |
| Regression, Docker-gated (CI) | 1734 | — | **~+70 new facts** (writer matrix A/B'/C'/E/G/T incl. the zero-width reopen ×2 and case E ×2 · the worklist repository + endpoints 17 · migration replays incl. the NOT-NULL census RED path · payroll/compliance/settlement/leaver-send/backdating endpoint pins · the conjunction's old-narrow-correction pin) | **CI-pending — verifies in the watched close run** (Docker unavailable on the owner's machine, standing instruction) |
| Smoke | 6 | — | ±0 | CI (composed stack) |
| Frontend | 735 | **735** | ±0 | green locally (`npm run test`), `tsc --noEmit` clean after the typed-contract regen |
| Full solution build | — | Release `--no-incremental`, **0 errors / 145 pre-existing warnings**; **CA2100 distinct sites 115 = baseline** | — | ✅ |

**Arithmetic check (locally-run suites):** 1089 + 165 + 113 + 735 = 2102 → 1235 + 165 + 102 + 735 = **2237 (+135)**.
The Docker-gated and smoke suites are reported by the CI run and backfilled on the header line when green.

**Evidence honesty (the standing posture while Docker is unavailable):** the DB-free half is genuinely proven —
the temporal router's whole case matrix, the worklist's derived-flag logic, the `EmploymentWindow` fenceposts,
the conjunction predicate. Everything that touches SQL is asserted by tests that have never executed anywhere:
both writers' lock query and interval arithmetic, the worklist read/write surface, both migration segments and
the NOT-NULL census, the settled-year skip, and every endpoint pin. Two CI reds were caught by review before
they shipped (the create-POST version chain; the leaver profile GET); ≥ 2 CI iterations remain budgeted.


## Sprint Retrospective

**What shipped, in one paragraph a non-engineer can use.** Until this sprint, an employee's profile could only
be changed as of today. If HR learned in September that someone dropped to 80 % back in March, the system had
no way to record that truth — the March-to-September months kept saying 100 %. Sprint 138 makes dated
corrections legal: a change carries the date it took effect, the old row is closed at that date instead of
overwritten, and both sides of the split stay readable forever. Because a correction can reach back into months
already sent to payroll or years already settled for holiday, the sprint also builds the honest counterpart —
an HR worklist that says, in plain language, "this correction touches something already closed; here is what
and here is the path to fix it." The worklist is a LIST, not an approval gate: the correction always lands, and
no downstream number is silently rewritten (ADR-013's bound, held).

### What went well

- **The wave split held.** Three waves with disjoint agent scopes meant no merge conflicts between agents
  across roughly 90 changed files. The one cross-wave dependency (13804 needing the writer from 13801) was
  declared rather than worked around.
- **The router paid for itself.** Pulling the write decision into a pure `TemporalWriteRouter` — no database,
  no clock, no I/O — made the six cases (A / B' / C' / E / G / T) testable as a 44-case matrix with no Docker.
  That is why the hardest logic in the sprint was also the best-pinned logic, on a machine that cannot run the
  database at all.
- **Both review lenses earned their keep, and diverged as designed.** The external lens found the as-of-today
  read that could roll back a valid correction; the internal lens found the create-path audit gap and the
  unreachable profile GET. Neither lens found the other's headline. Two Step-7a findings (external W-as-of,
  internal W5) turned out to be the SAME failure shape reached from different directions: a courtesy read
  vetoing the write it was describing.

### What to do differently

- **Test the path a human takes, not just the parts.** BLOCKER 2 (the profile GET refusing terminated
  employees) shipped a feature that every unit test passed and no user could reach. The writer was tested, the
  reader was tested, the sequence "open a leaver's profile, then correct it" was not. The lesson is not "write
  more tests" — it is that a capability needs at least one pin that walks it end to end in the order a person
  would.
- **Name the concept before you compare against it.** The settled-year off-by-one (Step-7a W1) was easy to
  write and hard to see precisely because the date was called a "crystallization date" / "freeze moment" —
  names that describe WHEN the settlement was taken. The value is the last day the settlement COUNTED. With the
  wrong name, `<` looks right; with the right name, `<=` is obvious. The rename went through every reference.
- **A "registered follow-up" is a claim that must be checkable.** The `required EffectiveFrom` item was written
  as registered with nothing behind it (now `QUAL-152`). Registration means a row exists, not that the sentence
  was typed.
- **My own two errors, recorded.** I wrote a 422 message by passing a sentence into a noun slot, producing text
  no HR user could parse — the internal lens caught it. And I stated the settled-year rule as "the boundary test
  INSTEAD OF the window test" when the correct answer was a CONJUNCTION; I ruled it and had the agent restore
  the geometry half. Both were mine, not an agent's, and both were caught by review rather than by me.

### Carried forward

- **Future-dating is Increment 4**, under the named "current ≠ live" precondition: four open-ended readers must
  become as-of-today readers and the two `users.*` caches need an explicit refresh strategy. Recorded on
  ROADMAP and in the ADR-040 2026-09-02 amendment.
- **`QUAL-149` / `QUAL-150` stay coupled and stay visible.** The worklist now derives `recalcBlockedBy` so the
  two known limitations appear where HR works instead of only in a register.
- **`QUAL-152`** — make the dated-edit DTO members `required` so the contract, not a guard, refuses an omitted
  date. Wire-visible, so it lands with Increment 4's date picker.
- **Docker-gated pins (~70 new facts) and the smoke suite are CI-pending** — Docker does not run on the owner's
  machine, so those go green when CI says so, backfilled into this log.
