# Sprint 137 — Time-control Increment 2: calculation correctness

| Field | Value |
|-------|-------|
| **Sprint** | 137 |
| **Status** | complete |
| **Start Date** | 2026-08-26 |
| **End Date** | 2026-09-02 |
| **Orchestrator Approved** | yes — 2026-09-02 (Step-5a dual-lens terminal; Step-7a Codex terminal at cycle 3; Reviewer close verdict in `.claude/reviews/SPRINT-137-step7a-reviewer.md`) |
| **Build Verified** | yes — `dotnet build StatsTid.sln -c Release --no-incremental` **0 errors** on the final tree (CA2100 distinct sites 115 = CI baseline) |
| **Test Verified** | **✅ CI GREEN `33622368503`** (all 7 jobs, 2026-09-02, watched to verdict): unit **1089/1089** (+84) · DemoSeed **165/165** · regression **1734/1734** (+62 vs the S136 run — every S137 Docker-gated pin passed on FIRST CI execution: window resolver, category dating, migration replay ×2, legacy-row replay, 4 payroll E2E + 3 live-ruleset, 10 resolver date-overlay, 6 compliance window, 9 leaver end-cap, 4 settlement RED-on-old, the flipped admin-create pin, the two marquee fixtures on the real window resolver) · smoke + E2E + frontend + docs + gitleaks + lizard green; CA2100 115 = baseline in CI. Local before push: unit 1089 · DemoSeed 165 · regression non-Docker 113 (Docker unavailable on the owner's machine). |

## Sprint Goal

The CALCULATION side learns to obey the employee timeline (ADR-040 D4/D5/D9/D10): payroll segments
become typed EMPLOYED/NOT_EMPLOYED (non-employed spans evaluate no rules and export no lines but
stay explicit in the manifest), the reserved `EmployeeProfileChange` boundary activates (a
mid-month part-time change finally splits pay — **narrowed 2026-09-02: proven under the straddle-safe test rule set, REFUSED under the live rule set until QUAL-149 lands; hire/leave edges DO pay correctly in the live set by owner ruling**), running balances stop accruing at the leave date,
the OK-version live-read closes structurally (QUAL-147), compliance gains its window behavior,
and `employment_category` becomes a dated profile column (read-side half). **Payroll boundary:
windowless employees byte-identical by construction (the `[property: JsonIgnore
WhenWritingDefault]` mechanism + frozen per-writer literals); Step-5a mandatory.**

**Inputs:** ADR-040 (ratified) · refinement
`.claude/refinements/REFINEMENT-s137-increment2-calculation-correctness.md` rev 3 — seam-recon-
grounded; dual-lens converged (Codex 3 BLOCKER → cycle-2: B1/B2 resolved, B3's residual was a
textual signature inconsistency fixed in rev 3 at the 2-cycle cap, reported not re-cycled;
Reviewer 1 BLOCKER / 4 WARNING / 6 NOTE → cycle-2 "resolution confirmed", one editorial NOTE
fixed) · **owner ruling 2026-08-26 (OQ-1a): the leaver SPECIAL_HOLIDAY settlement over-count —
a LIVE domain defect a scope cut had hidden, surfaced by the Reviewer — is FIXED in-sprint as a
deliberate settlement-value change on the ADR-033 rails — RED-on-old by ARITHMETIC (unit-pinned) + documented old values; the settlement pin verifies the new value in CI.**

## Step 0b (plan review)

Served by the refinement's Step 4 dual-lens (2 lenses × 2 cycles; the S134 precedent). Load-bearing
absorptions: the skip point precedes profile resolution (D10's letter + kills the pre-hire resolver
500); the total-failure short-circuit fix covers BOTH halves (first-EMPLOYED capture × employed
count); compliance owed and now has D7/D10 window behavior specified over ALL windows
(spells-proof union semantics); the dated category gained its missing WRITE-path story (nullable +
COALESCE + all four production INSERT sites + census pin); file-ownership re-cut into two waves;
the end-date fenceposts (`EmploymentEnded` boundary = end+1; last employed day PAYS) pinned as ACs;
per-writer frozen literals + the `[property:]` attribute trap named; `GetWindowsAsync` range-scoped.

## Scope & Task Decomposition

| Task | Wave | Agent | Deliverable |
|------|------|-------|-------------|
| TASK-13701 | 1 | SharedKernel/Infrastructure (cross-domain authorized) | Segmentation core: `EmploymentStarted`/`EmploymentEnded` causes; `PlannedSegment.EmploymentStatus` (`[property: JsonIgnore(WhenWritingDefault)]`, not `required`, positional ctors updated); `BoundarySources` trailing-optional lists; `BoundaryDetector` ruled tie-break order + `OrderedCauses` doc + `HasAnyInteriorBoundary` mirror (all three new sources); planner typing with the D1-inclusive fenceposts; `IEmploymentWindowResolver.GetWindowsAsync(id, from, to)` + impl. **Folded-in parity pins:** per-writer frozen EMPLOYED literals (byte-identity by construction), NOT_EMPLOYED-carries-key, legacy-row replay → EMPLOYED default |
| TASK-13704 | 1 | Data Model + Infrastructure (+1 authorized Backend INSERT touch) | `employee_profiles.employment_category` (nullable) + history-covering backfill + migration segment (Orchestrator approves init.sql) + replay test; ALL FOUR production INSERT paths populate same-tx from users; repo live-JOIN reads → COALESCE; the NEW `GetEffectiveFromDatesAsync` history read (13702 consumes); Case C + census pins |
| TASK-13702 | 2 | Payroll (Step-5a target) | Planner hydration (windows via `GetWindowsAsync`; profile-change dates via 13704's read); skip-BEFORE-profile-resolution; completed short-circuit fix; flex pass-through; Payroll DI (window resolver, self-managed surface); PCS overlay kept + comment fixed; the OQ-1-core + fencepost + fraction-change + ADR-031-negative pins |
| TASK-13703 | 2 | Backend + Infrastructure (cross-domain authorized; Step-5a target) | Resolver `OkVersion = OkVersionResolver.ResolveVersion(asOf)` (owns the resolver file entirely, incl. the category COALESCE + stale-doc fix); `BalanceEndpoints:152-157` month-resolved OK; compliance window behavior (union semantics, first-employed-day profile, empty→zero-violations); `AccrualMath` optional `employmentEnd` clamp + opt-ins (sites 1–5, 7) + **site 8 per the owner ruling (RED-on-old settlement pin)**; leaver running-balance pins |
| TASK-13707 | 2b | SharedKernel Segmentation (cross-domain authorized into tests; owner-ruled addition 2026-09-02) | ADR-016 D4 refusal keys on EVALUATED (EMPLOYED) segments, not interior boundaries: hire/leave edges are truncations (≤ 1 EMPLOYED segment plans), profile-change-while-employed / two-spell months (≥ 2 EMPLOYED) still refuse; `HasAnyInteriorBoundary` mirror retired (count is complete by construction); messages carry counts + causes, no dates; live-`RuleRegistry` unit pins + a Docker-gated live-ruleset PCS pin incl. the fraction-change limitation pinned as REFUSED |
| TASK-13708 | 2c | Backend (owner-ruled addition 2026-09-02, from Reviewer W4) | Admin user-create DEFAULTS `employment_start_date` to the profile row's `effective_from` (today) when omitted — one same-tx variable for both rows; `users_audit` CREATED `new_data` records the effective date + a defaulted marker; supplied dates and seeded/legacy NULLs unchanged; the S136 "omitted → NULL" pin flipped RED-on-old; Docker-gated pins (CI) |
| TASK-13706 | close | Orchestrator | Step-5a dual-lens (13701+13702+13703), Step-7a, registers (site-6 cut · OkVersion family+11th caller · QUAL-146 partial-reversal note · QUAL-147 closure · ADR-040 D9-premise precision note), sprint-test-validation, watched close push |

## Wave 1 acceptance (Orchestrator, 2026-09-02)

**Plain-language summary.** Wave 1 delivered the two foundations everything else in this sprint
stands on: (1) the segmentation core now KNOWS about employment — a payroll period can be split at a
hire date, a leave date, or a profile change, and each resulting segment is typed EMPLOYED or
NOT_EMPLOYED; (2) the employee's category (which drives which agreement rules apply) is now recorded
WITH A DATE on the profile row, so "what category in March?" is answerable. Wave 1 was produced on
2026-08-26 by two parallel agents (TASK-13701 ∥ TASK-13704) and validated on 2026-09-02 after the
producing session ended on its token limit; nothing was lost — the deliverables were on disk.

**Verified locally:** `dotnet build StatsTid.sln` 0 errors (145 pre-existing warnings); Unit
Segmentation + `EventSerializerCoverage` filter **39/39 green** (incl. the new
`EmploymentBoundaryPlannerTests` and the per-writer `SegmentSerializationParityTests` — the
by-construction byte-parity proof is falsifiable and passing).

**Orchestrator review of the schema diff (init.sql) — APPROVED:** `employee_profiles.employment_category
TEXT NULL` via file-scope `ADD COLUMN IF NOT EXISTS` (the S59/S74 idiom) + a ledger-guarded
`S137-PROFILE-CATEGORY-SEGMENT` with a HISTORY-covering backfill from `users.employment_category`
(correct, not approximate: the live value has been write-once since inception). Nullable by ruled
design, no CHECK, no fail-loud census (unlike S136's window CHECK — a NULL category has a
definitionally-correct repair via the COALESCE read; an inverted window does not). NOT-NULL
tightening is Increment 3. `docs/generated/db-schema.md` regenerated.

**Code review notes (the load-bearing details, all present):** `[property: JsonIgnore]` target on the
positional record (the attribute-on-parameter trap avoided); EMPLOYED == 0 default → absent key on
both writers; `BoundarySources` trailing-optional lists (every pre-S137 positional construction
compiles); detector tie-break by literal foreach order with `OrderedCauses` re-synced; the
`HasAnyInteriorBoundary` mirror covers all three new sources; typing fails SAFE toward EMPLOYED when
a caller supplies windows without boundary dates; `EmploymentEnded` = end+1 documented at three
places (`EmploymentWindow`, `BoundarySources`, `BoundaryCause`); `GetWindowsAsync` list-shaped
(spells-proof), empty = "known, nothing overlaps"; all four production INSERT paths populate the
category same-tx; repo reads COALESCE; `GetEffectiveFromDatesAsync` reads live + history rows.

**Deferred to CI (Docker unavailable on the owner's machine — owner instruction 2026-09-02: "Docker
does not work on this device. We must continue and try docker later"):** the Docker-gated Wave 1
pins — `EmploymentWindowResolverTests` (GetWindowsAsync range/NULL/fail-loud legs),
`ProfileCategoryDatingTests` (Case C supersession, census, fencepost reads),
`ProfileCategoryMigrationTests` (segment replay ×2 against a reconstructed pre-S137 schema),
`BoundaryCauseEncodingTests` legacy-row replay legs. They compile; they verify in the watched close
run per the standing convention.

**Wave 2 dispatched 2026-09-02:** TASK-13702 (Payroll) ∥ TASK-13703 (Backend + Infrastructure,
cross-domain authorized into SharedKernel/Calendar for the one optional `AccrualMath` parameter).
Both run in the main tree on disjoint file sets (worktree isolation would not see the uncommitted
Wave 1 types).

## Wave 2 acceptance + the alignment ruling (Orchestrator, 2026-09-02)

**Plain-language summary.** Wave 2 made the calculation side actually USE the Wave 1 foundations.
The payroll planner now reads the employee's employment window and profile-change dates from the
database inside the payroll host (never over the wire), skips non-employed spans before it ever asks
for a profile, and fixes a latent "everything failed" safety check that was wrong in both halves. On
the read side, the profile resolver now derives the OK-version (which collective-agreement version
applies) from the DATE being asked about instead of from the live user row — one fix that corrects
every consumer at once (QUAL-147 closed); the compliance check consults the employment window first
and answers "nothing to check" for a fully pre-hire month; running vacation balances stop accruing at
the leave date; and the owner-ruled leaver settlement over-count is fixed with a test that proves the
old value was wrong.

**TASK-13702 (Payroll) — accepted.** `PeriodCalculationService`: two null-tolerant optional ctor **(superseded at Step-5a — Reviewer W1: the DI-wired pairing, profile resolver WITHOUT window resolver, now FAILS LOUD; two Regression marquee fixtures updated to supply the real window resolver — see the internal-lens section)**
params (`IEmploymentWindowResolver?`, `EmployeeProfileRepository?` — all six direct-construction
fixtures compile unchanged); `BuildPlanForLegacyCallersAsync` hydrates `EmploymentStartedDates`
(= start) / `EmploymentEndedDates` (= end + 1, the D1 fencepost) / `EmployeeProfileEffectiveDates`
and passes the windows for typing; the NOT_EMPLOYED skip is the FIRST statement of the per-segment
loop (empty result list + empty line list, never a synthesized row; flex carry untouched); the
short-circuit budget is captured from the first EMPLOYED segment × employed count — an all-
NOT_EMPLOYED plan returns Success with nothing to calculate and an explicit manifest (ruled
semantics); the stale overlay comment + the L144 byte-identity comment carry dated notes; Payroll
DI registers the self-managed window resolver (NOT the in-tx surface) + the profile repo. Tests: 15
unit (skip/short-circuit/flex/hydration-fencepost/tie-break pins — run green) + 4 Docker-gated
regression (the OQ-1 core leaver/starter with fencepost asserts, the fraction change with the
ADR-031 negative AC, the non-zero flex carry — deferred to CI).

**TASK-13703 (Backend + Infrastructure, cross-domain authorized) — accepted.** Resolver: `u.ok_version`
dropped from the SELECT, `OkVersion = OkVersionResolver.ResolveVersion(asOfDate)`, category
`COALESCE(ep., u.)`, docs rewritten (QUAL-147 closure explained); `BalanceEndpoints:152-157` → month-end-
resolved OK; compliance: `GetWindowsAsync` after the access checks + leader gate, union-of-windows
first-employed-day profile resolution, empty union → existing shape with zero violations and NO
resolver/rule-engine/projection call; `AccrualMath.EarnedToDate(…, DateOnly? employmentEnd = null)`
clamps INSIDE the single-source math (`AccrualMathSingleSourceTests` untouched + green); opted in at
sites 1–5 (Balance), 7 (Skema SPECIAL_HOLIDAY cap), **8 (the owner-ruled settlement fix; RED-on-old by ARITHMETIC — the unit pin asserts the uncapped call still returns the full quota — plus documented old values; the Docker settlement pin verifies the NEW value in CI:
a 30-Jun leaver now settles 2.5 særlige feriedage where the old code observed 5.0)**; sites 6, 9, 10 and
the rule-engine `AccrualCalculator` untouched. Tests: 21 unit end-cap cases + 11 pure union-semantics
tests (run green) + 29 Docker-gated regression (resolver date-overlay ×10, compliance window ×6,
leaver end-cap ×9 incl. the L152-157 pin made observable by a per-test OK24 norm mutation, site-8
RED-on-old ×4 — deferred to CI). Step-5a nuance recorded by the agent: via the settlement poller the
site-8 over-count was LATENT (the S80 BLOCKER-1 guard fails a passed end date closed first); the fix
is what makes the value right when the R12 termination-interaction slice routes leavers here.

**Orchestrator validation of the merged Wave 2 tree:** `dotnet build StatsTid.sln -c Release
--no-incremental` **0 errors**; Unit **1065/1065** (Release, `--no-build`); **CA2100 distinct sites 115 =
CI baseline** (replicated with the ci.yml grep on a fresh Release log — 13703's first draft had added
3 test-helper sites and self-corrected to literal SQL); **Step-5α file-scope self-check: every changed
file is inside its task's declared scope; no agent touched `docs/**`**.

**The alignment finding (TASK-13702 observation #1) → OWNER RULING 2026-09-02.** The planner's
ADR-016 D4 safety rule refuses any month with an INTERIOR boundary when a whole-window rule
(`SplitBehavior.AlignedWindow`: OVERTIME_CALC, weekly NORM_CHECK, daily/weekly rest) is in the rule
set and `AllowUpstreamAlignment` is false — and the Payroll host uses the LIVE `RuleRegistry` set with
`PlannerOptions.Default`. Sprint 64 (F4-1(b), owner-ruled 2026-06-04) deferred this as a latent gap
because interior boundaries were then RARE (OK transitions fall on month starts). S137 makes them
COMMON: every mid-month hire, leave, or profile change is now an interior boundary → in the live
wiring the month would be REFUSED (an unhandled `PlannerInvariantViolation` → 500) instead of paid
correctly, and the Wave 2 regression pins pass only under the straddle-safe TEST rule set. Verified
facts: the merge step buckets per rule only the segments that produced a result, so a rule evaluated
in exactly one EMPLOYED segment reaches `RejectIfMultipleSegments` with one segment; the composed-
stack smoke probe's `emp001` has NULL employment dates (unaffected); no E2E test calls
`calculate-and-export`; demo-seed leavers DO have end dates in recent months. **Options put to the
owner:** (1) treat hire/leave edges as TRUNCATIONS — the refusal keys on how many EMPLOYED segments a
whole-window rule would run in, not on boundary count (≤ 1 → not a split; ≥ 2 → refuse as today);
(2) ship as-is and register the widened blast radius; (3) also reclassify norm/overtime in-sprint so a
mid-month fraction change pays per span (needs an unsourced domain decision — advised against).
**RULED: (1).** Rationale the owner accepted: a whole-window rule is unsafe to SPLIT because two
half-evaluations cannot be merged; an employment edge does not split evaluation — the NOT_EMPLOYED side
evaluates nothing (D5) — so the rule runs once over a shorter span, the same class of input rules see at
every month edge (months rarely start on a Monday). Windowless callers are behavior-identical
(every segment EMPLOYED ⇒ interior boundary ⟺ ≥ 2 EMPLOYED). **Consequence registered:** a mid-month
part-time-fraction change while employed (two EMPLOYED segments) still refuses in the live rule set
until the norm/overtime rules are reclassified with a pro-rating merger — the S64 ADR-016 D4 follow-up,
now re-registered with its true scope (it had fallen out of ROADMAP). Dispatched as **TASK-13707**.

## TASK-13707 acceptance + final tree validation (Orchestrator, 2026-09-02)

**Plain-language summary.** The owner-ruled truncation change landed: the planner's ADR-016 D4 safety
rule now asks "in how many EMPLOYED spans would a whole-window rule actually RUN?" instead of "are there
any splits at all?". A hire or leave edge leaves exactly one employed span, so the rule runs once and the
month plans; a profile change while employed leaves two, so the month still refuses (QUAL-149). Callers
without employment windows see no change whatsoever — for them every span is employed, so "two or more
employed spans" is exactly the old "any interior boundary".

**Accepted — `PeriodPlanner.cs` (+270/−86):** the D4 policy moved AFTER boundary detection and range
typing (a `TypedRange` record struct; `ApplyAlignmentPolicy` keeps its name, now takes the detected
boundaries + typed ranges, returns void; the dead `effectivePeriodStart/End` plumbing of the never-
implemented S20 shrink stub removed; the stub's pass-through semantics for `AllowUpstreamAlignment=true`
preserved with its comment); `HasAnyInteriorBoundary` and its "MUST mirror every source" doc REMOVED —
counting the detector's own typed output is complete by construction; exception messages gain the
EMPLOYED count + the distinct interior-boundary causes in date order and state that an employment edge
alone would not have refused — NO segment dates (ADR-040 D7). Wave 1's mirror pin in
`EmploymentBoundaryPlannerTests` was re-documented and renamed
(`Plan_WindowlessCaller_RejectRule_StillTripsOnEachNewInteriorBoundarySource`) — its windowless inputs
remain a valid behavior-identity pin under the new rule. Tests: `EmploymentTruncationAlignmentTests`
(16, LIVE `RuleRegistry` set: windowless OK-straddle + each new source still refuse; leaver / starter /
hire+leave / post-S136 hire-dated-profile-row shapes PLAN with one EMPLOYED segment; leaver + profile
change while employed and two spells REFUSE with count + causes and no dates; Reject rule follows the
same truncation rule; `AllowUpstreamAlignment` stub semantics; `FromManifest` replay of a 2-segment
1-EMPLOYED manifest) — run green; `EmploymentWindowLiveRulesetTests` (3, Docker-gated: leaver + starter
calculate end-to-end under the live set with OVERTIME_CALC merged as a success; the fraction-change split
REFUSES — pinned by name as the QUAL-149 limitation) — compile-verified, CI.

**Orchestrator validation of the FINAL S137 tree (CI-faithful):** `dotnet build StatsTid.sln -c Release
--no-incremental` **0 errors / 145 warnings (all pre-existing)**; **CA2100 distinct sites 115 = baseline**;
Unit **1081/1081** (+76 vs the S136 close's 1005: Wave 1 segmentation + parity pins, 13702's 15, 13703's
21 accrual cases, 13707's 16, minus/plus renames); DemoSeed **165/165** (unchanged). Regression: compiles;
the S137 Docker-gated pins (Wave 1: window resolver, category dating, migration replay, legacy-row
replay legs · Wave 2: 4 payroll + 29 backend/settlement · 2b: 3 live-ruleset) verify in the watched CI
run. Frontend untouched.

**Step 5b — knowledge proposals adjudicated (7 received):** ACCEPTED → **PAT-022** (13702's skip pattern +
13703's month-read pattern MERGED — one D10 caller-ordering pattern, two shapes; the DB-free PCS harness
folded in as Agent Guidance) · **FAIL-007** (the planner's "cause names the ENDING transition" convention —
verified against `PeriodPlanner.cs` before recording) · **PAT-023** (13707's "derive invariants from the
pipeline's output, not a hand-maintained mirror of its inputs"). FOLDED, not separate entries → 13703's
CA2100 gotcha (an Agent-guidance append on the QUAL-073 register row) · 13703's site-8-vs-S80-guard note
(carried in ADR-040's D9 precision note + this log) · 13702's DB-free harness (in PAT-022).

## TASK-13708 acceptance — the default hire date (Orchestrator, 2026-09-02)

**Plain-language summary.** An admin could create an employee without a hire date; the system then knew the
employee's first profile started "today" but believed the employment had no beginning. Once profile dates
became payroll boundaries (this sprint), that contradiction made the employee's first month un-plannable. The
owner ruled: an omitted hire date means "hired today" — the same date the profile starts. The request stays
optional (no wire-shape change); the audit row records that the date was inferred rather than supplied.

**Accepted — `AdminEndpoints.cs` POST user-create:** `effectiveFrom` (today UTC) is computed ONCE before the
transaction and threads into `users.employment_start_date` (when omitted), `employee_profiles.effective_from`,
AND the `EmployeeProfileCreated.EffectiveFrom` event value — the row and the event can no longer straddle
midnight (a latent parity hole the agent closed in passing; value-only, no event-schema change, DEP-003
respected); `users_audit` CREATED `new_data` gains `employmentStartDate` (the EFFECTIVE stored value) +
`employmentStartDateDefaulted` (bool) — additive JSONB, origin reconstructable; supplied dates verbatim;
seeded/legacy NULLs untouched (D2 semantics intact). `UserCreated` carries no start date (verified) — nothing to
pass. **Tests (Docker-gated, CI):** `AdminUserCreate_WithoutEmploymentStartDate_DefaultsToProfileEffectiveFrom`
REPLACES the S136 `…_StoresNull` pin (FLIPPED, RED-on-old by construction — old expectation stated in the test
doc: omitted ⇒ NULL + JSON-null audit): one JOIN reads both rows on the same snapshot, asserts both non-NULL
and equal, the audit marker true, and the profile event's `effectiveFrom` equal (row/event parity);
`…_WithEmploymentStartDate_StoresAndAuditsIt` extended with marker false. Blast-radius sweep by the agent:
no Regression test POSTs an undated user and then acts on a PAST date (the only way the new bounded window
could trip a registration gate) — all clear. Orchestrator Small-Tasks follow-through: the request DTO's
`//` comment ("Omitted ⇒ NULL") corrected — it is a plain comment, not XML doc, and the property description
is not part of the generated OpenAPI spec (no `IncludeXmlComments`), so no spec/type regeneration is owed.
KB: the agent's "one clock per transaction" proposal accepted as **PAT-024**. **Consequence recorded (Step-7a Reviewer WARNING):** the defaulted date is also the window START for the S136 write gates — an undated create can no longer back-fill PRE-CREATION registrations until the hire date is corrected (supply it at create, or edit it first); the create form surfacing the hire date pre-filled + editable, and backdating on the edit path, are registered as the Increment-4 lifecycle-UX item (ROADMAP + SPRINT-135 program note).

## Architectural Constraints Verified

- [x] Architectural integrity — SharedKernel stays Npgsql-free (the planner takes `IReadOnlyList<EmploymentWindow>`, not repositories; the window/profile reads are Infrastructure); the Payroll host registers only the self-managed `IEmploymentWindowResolver` surface (lock-free planning read); trailing-optional records everywhere (every pre-S137 positional construction compiles); the ADR-016 D4 refusal re-derived from the pipeline's own typed output (PAT-023) rather than a hand-maintained mirror; the DI-wired PCS path fails loud when its window resolver is dropped (Reviewer W1 absorbed). Two ADR collisions escalated and owner-ruled, not traded ad hoc.
- [x] Domain correctness / payroll boundary — windowless byte-identity BY CONSTRUCTION (`[property: JsonIgnore(WhenWritingDefault)]`, EMPLOYED == 0) and PINNED per writer against the REAL `JsonOptions` object; fenceposts pinned at detector/planner/hydration/compliance layers (end + 1; last employed day PAYS; day `start` pays; hire on 2026-04-01 records `EmploymentStarted`); the NOT_EMPLOYED skip precedes profile resolution (D10); both short-circuit halves fixed + the all-NOT_EMPLOYED semantics ruled and pinned; the ADR-031 negative AC (day-count fraction-flat) pinned (CI); QUAL-147 closed structurally (OK-version date-derived inside the resolver); the D9 end-cap inside the single-source `AccrualMath`; site 8 fixed RED-on-old by arithmetic. Named limitation, not hidden: profile-change splits refuse under the live rule set (QUAL-149) with the plan-wide wage-type key coupled (QUAL-150).
- [x] Auditability — NOT_EMPLOYED segments stay explicit in the persisted manifest; pre-D5 manifests replay to EMPLOYED (pinned, CI); an all-NOT_EMPLOYED plan still emits its manifest; the migration segment is ledger-guarded + `IS NULL`-guarded (idempotent across greenfield/legacy/re-apply) with a HISTORY-covering backfill whose write-once premise is carried to Increment 3 as a precondition; the admin-create audit row records a defaulted hire date as defaulted (origin reconstructable); the settlement change is a deliberate, RED-on-old, owner-ruled value change on the ADR-033 rails.
- [x] Integration isolation & delivery — `SegmentManifestCreated` gains only an omitted-when-default key; `EventSerializer` registrations untouched; the PCS shared `JsonOptions` (the rule-engine wire format) untouched (QUAL-146 bound, partial-reversal noted); the rule engine reached over HTTP only; `AccrualCalculator` (rule-engine side) untouched — employment dates never cross into the rule engine (D7); `EmployeeProfileCreated.EffectiveFrom` changed in VALUE only (same-tx clock), no schema change (DEP-003).
- [x] Security & access control — employment dates stay server-side (D7): no wire DTO, response body, error body, or export line carries one; the planner's refusal messages carry counts + cause NAMES, never dates (pinned in culture and ISO renderings; the residual "an edge exists" existence signal recorded in ADR-040 D7); the compliance handler's access check → user lookup → leader-month gate all run BEFORE the new window read (pinned by the 403-before-any-read test, CI); `RequireAuthorization` unchanged on every touched endpoint; the admin-create INSERT uses a same-parameter scalar subselect (no new injection surface).
- [x] CI/CD enforcement — Release `--no-incremental` build 0 errors on the final tree; CA2100 distinct sites 115 = baseline (replicated with the ci.yml grep; one agent draft self-corrected from 118); Unit 1089/1089, DemoSeed 165/165, non-Docker regression 113/113 locally; ~43 Docker-gated pins compile and verify in the watched close run (Docker unavailable on the owner's machine — standing instruction); Step-5a dual-lens terminal (Codex cycle 2 clean; Reviewer W1–W3 absorbed, W4 owner-ruled + implemented); Step-7a dual-lens recorded below; the untracked-source close gate satisfied by staging every new file.

## External Review (Step 5a / 7a)

### Step 5a — external lens (Codex, per-task high-risk: payroll export + legal rule logic) — cycle 1 of 1

`codex review "<per-task prompt>"` (prompt-alone form, auto-targets the uncommitted diff; codex-cli 0.147.0,
read-only sandbox) on the FULL S137 tree (13701 + 13702 + 13703 + 13704 + 13707). Steered at the eight
domain edges: fenceposts, windowless byte-parity on both writers, the short-circuit shapes, flex/merge with
empty segment lists, the truncation policy, the resolver's OK derivation, the site-8 operand, D7 leakage.

**Verdict: "generally consistent with the specified fenceposts"; ONE finding, WARNING (Codex P1).**

- **WARNING — the wage-type-mapping natural key is snapshotted per PLAN, not per segment**
  (`PeriodCalculationService.BuildPlanForLegacyCallersAsync` hydrator → `MapSegmentToExportLinesAsync`).
  Verified by the Orchestrator against the code: the hydrator runs once against the CALLER profile (ADR-020
  D1.5 "uniform-per-plan binding", LOCKED in the S29 refinement); the mapping lookup reads
  `OkVersion / AgreementCode / Position` from that shared snapshot and only `asOfDate` per segment. Rules
  evaluate with the correct per-segment dated `segmentProfile`, but after a mid-period POSITION change the
  post-change segment's export lines would map with the OLD position's lønart. **Disposition: REGISTERED
  as QUAL-150, COUPLED to QUAL-149, not fixed in-sprint.** Why not fix now: (i) production wiring cannot
  reach it — a profile-change split refuses under the live rule set (QUAL-149), so no wrong export can ship;
  (ii) the code fix is three field reads, but it reverses a LOCKED ADR-020 D1.5 decision whose S29 rationale
  ("the caller profile may drift from the forward-calc profile on replay") is only PARTIALLY obsoleted by
  the ADR-023 dated resolver — that reversal needs an ADR amendment + its own refinement and plan review, not
  a Step-5a bolt-on in a payroll-boundary sprint; (iii) the Step-5a cycle rule requires a verification cycle
  for every FIX — a register entry + a pointer comment is not a fix, so cycle 1 is terminal. A pointer
  comment above the hydrator registration names QUAL-150 so the next agent touching it sees the coupling.
  **Owner-reviewable:** if the owner prefers the complete fix now, it lands as a post-close follow-up with
  its own Step-7a cycle (the process allows it); the recommendation is to land it WITH QUAL-149 in the
  increment that makes profile-change splits plannable — they are one change from the payroll's point of
  view ("a mid-month position change pays per span, with the right lønart").
- No BLOCKER. Codex confirmed the fencepost handling, the byte-parity mechanism, and the truncation policy
  without findings.

**Cycle 2 (verification of the cycle-1 dispositions + the Reviewer absorptions + TASK-13708):** `codex review`
scoped to the five fixes — (A) the PCS ctor fail-loud coupling + fixture wiring, (B) the DB-free merge-backstop
pins, (C) the replica→reflection parity binding, (D) the hire-dated starter seeds, (E) the admin-create hire-date
default with its same-tx clock and audit marker. **Verdict: "No findings. The five scoped absorptions are
internally consistent and their regression pins are non-vacuous."** Step-5a external lens TERMINAL at cycle 2
(cycle 1: 1 WARNING → registered QUAL-150, not fixed; cycle 2: clean).

### Step 7a — external lens (Codex, sprint-end) — cycle 1: 1 WARNING → FIXED; cycle 2: see below

`codex review "<sprint-end prompt>"` on the FULL uncommitted S137 tree (all five tasks + the Step-5a absorptions;
told not to re-raise QUAL-150). Focus: cross-task consistency, drift, spec alignment, seams, test-evidence
honesty, D7 leakage, doc claims vs code.

- **WARNING (P2) — an employment date could leak through a fail-closed exception (ADR-040 D7).**
  `ComplianceEndpoints.cs:220`: when the resolver finds no profile at the FIRST EMPLOYED DAY, the handler threw
  `EmployeeProfileNotFoundException(employeeId, firstEmployedDay)` — for a mid-month hire that as-of IS the hire
  date, and the exception's message embeds its as-of date, so exception logs (and a detailed 500 body, if one
  were ever enabled) would carry an employment date. A real D7 hole on a client-triggerable path that the
  success-path wire assertions could not see. **FIXED (Orchestrator, Small Tasks Exception — two lines + a
  comment):** the resolver is still asked at `firstEmployedDay` (the correct D10 as-of); the exception is now
  anchored on `monthStart` — CALLER input — so diagnostics keep the employee id + the month and never the hire
  date. Type and fail-closed mapping unchanged; the compliance tests assert the RESOLVER recorder's as-of dates
  (unaffected). Release build 0 errors; Unit 1089/1089 after the fix.

- **Cycle 2 (verification of the compliance fix):** "The compliance fix preserves fail-closed behavior and safely
  reports the caller-supplied month." **NEW WARNING (P2), same class one layer down:** the PCS per-segment loop's
  fail-closed throw used `segment.StartDate` as the exception's as-of — for a starter's first EMPLOYED segment
  that IS the hire date. **FIXED (Orchestrator, Small Tasks Exception):** the resolver is still asked at
  `segment.StartDate`; the exception is anchored on `plan.PeriodStart` (caller input). Release build 0 errors;
  Unit 1089/1089. The remaining pre-existing segment-range diagnostics in `MapSegmentToExportLinesAsync` (three
  throw messages + one LogWarning — Payroll-host logs only, no client handler echoes them) are REGISTERED as
  **QUAL-151** (Low, D7 class, hardening-pass assessment) rather than churned at cycle 3.
- **Cycle 3 (verification of the cycle-2 fix):** **"No findings. The resolver still uses segment.StartDate, the fail-closed exception type and null-coalescing behavior are preserved, diagnostics retain the employee ID and caller-supplied period start, and the legacy null-resolver path still returns profile."** Step-7a external lens TERMINAL at cycle 3 (c1: 1W fixed → c2: 1W same class fixed → c3: clean; no BLOCKER at any cycle — the halt-and-prompt condition never arose).


### Step 7a — internal lens (Reviewer Agent, close review) — `verdict: CLOSE-WITH-WARNINGS`

**Part 1 — the four Step-5a WARNINGs verified RESOLVED in the tree** (W1: guard condition right, all nine PCS
construction sites enumerated, only the Payroll host consumes PCS via DI and registers both resolvers; W2: the
merge-backstop pins fail in both directions; W3: the reflection target is the real private field with an
identity pin, the four frozen literals now serialize through the production options; W4: one clock threads
users row / profile row / profile event, the audit marker reconstructs origin, no wire/event-schema change, the
flipped pin states old vs new; the starter-seed NOTE absorbed).

**Part 2 — close review: 0 BLOCKER / 1 WARNING / 5 NOTE.**
- **WARNING (trade-off: usability) — the "hired today" default hard-gates back-dated registrations for undated
  creates, and nothing recorded it.** Before S137 an undated create meant "employed since forever", so earlier-
  month hours could be registered; now the creation day is the window START for the Increment-1 write gates,
  so pre-creation registrations are refused until the hire date is corrected — a real onboarding-flow change
  the ruling's fork did not state. **RECORDED (docs):** the consequence sentence added to ADR-040's "D2 × admin
  create" note and to this log's TASK-13708 paragraph; the Increment-4 lifecycle-UX item registered (the create
  form surfaces the hire date pre-filled with today + editable; the edit path allows backdating) in ROADMAP and
  the SPRINT-135 program note. No code change.
- **NOTE — early sections of this log predated the rulings and contradicted later ones.** FIXED: the Sprint Goal
  carries the QUAL-149 qualifier; the Inputs paragraph and the ADR-040 D9 note say "RED-on-old by ARITHMETIC";
  the TASK-13702 acceptance carries the "superseded at Step-5a — Reviewer W1" marker; the constraints checklist
  was rewritten with evidence.
- **NOTE — the retroactive-correction seam inherits window typing through the shim (a positive: corrections of a
  month whose end date was recorded after export will claw back post-end days) but is UNPINNED.** ROUTED: one
  Docker pin → Increment 3 (ROADMAP "S137-owed items").
- **NOTE — admin-create → planner integration pinned at each layer, not end-to-end** (POST an undated user, plan
  its month under the live set). Optional one-fact extension; recorded, not done.
- **NOTE — the PCS constructor is becoming a policy site** (five optional collaborators + a coupling guard — a
  symptom of the living `[Obsolete]` planless shim). RECORDED on TASK-2010's scope in ROADMAP: S137's guard +
  hydration block are part of the shim-removal scope.
- **NOTE — close-artifact prerequisites** (header, checklist, Test Summary, retrospective) — all filled in this log.

Verified with no finding: cross-task doc consistency (resolver/interface/repository docs agree), PAT-022/023/024
+ FAIL-007 indexed, QUAL-147 fixed / 148-150 registered, ROADMAP re-lists the S64 gap with its coupling; every
ADR amendment matches the code it describes; no architectural drift; all integration seams verified.

**Final Reviewer check (after the Codex cycle-2 fix):** the PCS re-anchoring verified (fail-closed + type + legacy
path + diagnostics preserved); QUAL-151 verified honest with two precisions applied (the enumeration reads
"including" — the caller-supplied-OK mismatch `LogWarning` also logs segment dates; the "no client body echoes"
clause holds for the composed stack's Production environment, a local Development launch's developer exception page
would echo); **one more pre-existing instance of the class found** — the resolver's OWN fail-loud throw
(`EmploymentProfileResolver.cs:197`, agreement-row-missing data-integrity path) carries the caller's as-of (= the hire
date from the compliance GET or a starter's first segment) — **ENUMERATED in QUAL-151** (fix routed to the hardening
pass: throw without a date, or callers re-anchor); by-design surfaces named so no sweep re-finds them (export-line
period stamps ARE the payroll boundary's legitimate content — ADR-040 D7 precision added; `PlannedCalculation`
geometric-invariant messages fire only on planner bugs). Everything else clean (date-free 422s, D4 refusal messages,
merger failure rows, compliance empty body, admin-create response). **Final `verdict: CLOSE-WITH-WARNINGS`** — the
warning being the enumeration owed before commit, now done.




### Step 5a — internal lens (Reviewer Agent, first pass + absorption) — 0 BLOCKER / 4 WARNING / 9 NOTE

Scope: every changed file under `src/**`, `tests/**`, `init.sql` vs `86bdf8c`, plus the refinement's ACs.
Verified on the Orchestrator's eight concerns with no finding: SharedKernel Npgsql-free; the Payroll host
registers only the self-managed window-resolver surface; windowless preservation of the D4 refusal (identical
interior predicate, now complete by construction); skip precedes profile resolution; both short-circuit
halves; the `[property:]` byte-parity mechanism robust on pre-D5 replay under both readers; fenceposts at
detector/planner/hydration/compliance layers; compliance security ordering (access check → user → leader
gate → window read); every changed file inside its declared scope; every Docker-gated test references
helpers that exist with matching signatures. Full AC table: 14 ACs met / pinned-locally / pinned-Docker-
deferred, one narrowed-by-ruling (QUAL-149), none unmet.

**WARNINGs and dispositions:**
- **W1 — a dropped Payroll-host DI registration would silently reinstate leaver over-payment** (PCS's optional
  window resolver null ⇒ "everyone employed all month"; the smoke probe's `emp001` has NULL dates so no test
  would see it). **ABSORBED (TASK-13702 re-dispatch):** the PCS ctor now FAILS LOUD when given a profile
  resolver (the DI-wired, fail-closed path) without a window resolver — a dropped registration fails the
  host's first calculation (and the CI smoke probe) instead of paying wrong; the two Regression marquee
  fixtures that had that pairing now supply the real `EmploymentWindowResolver`; legacy no-resolver fixtures
  untouched; `PcsConstructionCouplingTests` ×4 pin all four pairings locally.
- **W2 — the MERGE half of the truncation ruling was pinned only by a Docker-gated test** (the DB-free fixture
  had no `RejectIfMultipleSegments` rule). **ABSORBED:** `EmploymentWindowAlignedWindowMergeTests` ×3 with a
  rule classified exactly like production OVERTIME_CALC — one EMPLOYED segment ⇒ the merged row succeeds;
  two EMPLOYED under `AllowUpstreamAlignment=true` ⇒ the merger's failure row for that rule only (the
  backstop the ruling assumes); negative control (Default options ⇒ planner refuses). Fixture-only option
  seam; PCS untouched.
- **W3 — the PCS-writer byte-parity pins bound to a HAND-COPIED replica of the private `JsonOptions`** ("KEEP
  IN SYNC" ×3). **ABSORBED:** all three replicas removed; the pins bind by reflection to the REAL
  `PeriodCalculationService.JsonOptions` (`PcsJsonOptions.Real` in Unit, `TestFixtures.ManifestReadOptions`
  in Regression) with an `Assert.Same` reference-identity pin so a copy can never sneak back; the real
  options untouched (QUAL-146).
- **W4 — "the pre-hire 500 is dead" holds only for HIRE-DATED employees:** an employee created via the admin
  screen WITHOUT a hire date gets a profile row dated today but an unbounded window, so the creation month
  has a mid-month profile boundary with no employment edge — payroll REFUSES it (a genuine 2-EMPLOYED split
  under the live rule set) and compliance resolves at month-start → the old profile-not-found 500. Not a
  regression (that month failed before too) but a residual reachable from the admin UI. **OWNER RULING
  2026-09-02 (fork presented even-handedly: register / default / reject): DEFAULT the hire date to the
  profile's `effective_from` when omitted** — "unknown hire date" means "hired today"; every new hire's first
  month is plannable by construction (the edge becomes a truncation by tie-break — pinned by
  `PostS136HireShape_HireDatedProfileRow_LiveSet_Plans`); explicitly supplied dates unchanged; seeded/legacy
  NULLs untouched (D2 semantics intact). Dispatched as **TASK-13708** (Backend).

**NOTEs and dispositions:** refusal messages may reveal that an employment edge EXISTS (never a date) on
≥2-EMPLOYED refusals via the Payroll host's unhandled-exception path — recorded in ADR-040 D7 consequences ·
window∩range arithmetic hand-rolled in three places (`EmploymentWindowResolver.GetWindowsAsync`,
`PeriodPlanner.ResolveSegmentEmploymentStatus`, `ComplianceEndpoints.FirstEmployedDayInMonth`) + End+1 twice
→ Increment-3 tidy: `EmploymentWindow.Overlaps/ClipTo/FirstNotEmployedDay` with one fencepost test (registered
below) · compliance testability seams (public static helper on an endpoints class; pure test parked in
Regression; the `REST_PERIOD_CHECK` string mirror unpinned) → same tidy · the Balance L152-157 config
mutation is isolated by construction (fresh container per test) — no action · an all-NOT_EMPLOYED manifest
carries the pre-existing `OkTransition` sentinel as its cause — a `PeriodStart`/`None` sentinel is a later
increment (serialization-safe append) · "RED-on-old-proven" for site 8 is proven by ARITHMETIC (the unit pin
asserts the uncapped call still returns the full quota) + documented old values; the Docker settlement pin
verifies the NEW value in CI — wording corrected in this log · the payroll-side Docker starter pin seeded the
profile from 0001-01-01 → **ABSORBED** (both starter facts now seed `effectiveFrom: hire`, so the real
resolver has no covering row and the pin passes only because PCS never asks) · inverted-period shim now
throws from the resolver before the planner (same caller-bug class) — awareness only · the backfill's
"live == history" premise (users.employment_category write-once) is an Increment-3 PRECONDITION: editability
must not retro-apply to migrated history rows — carried into the SPRINT-135 program note.

**Absorption verification (Orchestrator):** the re-dispatched TASK-13702 reported `dotnet build` 0 errors,
Unit **1089/1089** (+8: 4 coupling + 3 merge + 1 parity-identity), CA2100 **115 = baseline**; re-verified on the
final tree below.




## Test Summary

Per the `sprint-test-validation` procedure — every count below was RUN on the final S137 tree (Release,
`--no-build` after a `--no-incremental` rebuild), never carried forward, except where marked. Previous = the
S136 close (`docs/sprints/SPRINT-136.md` Test Summary + the INDEX row).

| Suite | Previous (S136) | Current (S137) | Delta | Status |
|-------|-----------------|----------------|-------|--------|
| Unit | 1005 | **1089** | **+84** | green locally (Wave 1 segmentation + parity pins; 13702's 15; 13703's 21 accrual cases; 13707's 16 truncation pins; Step-5a absorptions: 4 coupling + 3 merge-backstop + 1 parity-identity; net of renames) |
| DemoSeed | 165 | **165** | ±0 | green locally |
| Regression, non-Docker subset (`Category!=Docker`) | 102 | **113** | **+11** | green locally (the pure compliance union-semantics pins) |
| Regression, Docker-gated (CI) | 1672 total in the S136 CI run | **1734 total in run 33622368503** | **+62** new facts (Wave 1: window-resolver range legs, category dating, migration replay ×2, legacy-row replay legs · Wave 2: 4 payroll E2E, 10 resolver date-overlay, 6 compliance window, 9 leaver end-cap, 4 settlement RED-on-old · 2b: 3 live-ruleset · 2c: 1 flipped + 1 extended admin-create) | **✅ CI GREEN — all passed on first CI execution** (Docker unavailable on the owner's machine, standing instruction 2026-09-02) |
| Smoke | 6 (CI) | — | ±0 | CI (composed stack) |
| Frontend | 735 (S128, last change) | 735 | ±0 (not re-run — no frontend change this sprint) | CI |
| Full solution build | — | Release, `--no-incremental`, **0 errors / 145 pre-existing warnings**; **CA2100 distinct sites 115 = baseline** | — | ✅ |

**Arithmetic check:** locally-run total 1005 + 165 + 102 = 1272 → 1089 + 165 + 113 = **1367 (+95)**; the Docker-
gated and CI-only suites: run `33622368503` — regression **1734/1734** (+62), smoke + E2E + frontend green; backfilled 2026-09-02.


## Sprint Retrospective

**What went well:** the increment's load-bearing safety claim — a windowless employee's payroll is byte-
identical — was made TRUE BY CONSTRUCTION (`[property: JsonIgnore(WhenWritingDefault)]` on an EMPLOYED == 0
default, absent key on both writers) and then made FALSIFIABLE (frozen per-writer literals, bound at Step-5a to
the real options object) — the two halves the S131 audit taught us to demand together. Implementation found
two collisions between earlier decisions and this one that no review of the plan had seen: the ADR-016 D4
refusal would have turned every mid-month leaver into a 500 in the live rule set (a Sprint-64-deferred gap
whose blast radius this increment changed), and the admin-create undated hire made the "pre-hire 500 is dead"
claim only half true. Both were escalated with even-handed forks and OWNER-RULED the same day — truncation
semantics and the default hire date — instead of being traded ad hoc; both rulings are recorded in the ADRs,
not just the log. The lens-per-altitude structure did its job again: the Payroll agent surfaced the D4
collision as an observation, Codex found the plan-wide wage-type key (QUAL-150), the Reviewer found the DI
silent-fallback and the replica-bound parity pins — three different lenses, three different classes of hole,
none visible to the task that created it. The session-limit interruptions (the producing session's token
exhaustion before validation; the rate-limit mid-Reviewer) cost nothing: Wave 1 was on disk and validated a
week later; the Reviewer resumed from its transcript.

**What to improve:** two facts about our own machinery were rediscovered rather than known: the S64 F4-1(b)
deferral had fallen out of ROADMAP entirely (re-registered as QUAL-149 with its true scope — a deferral that
lives only in a census file is not a deferral), and the KB INDEX had three FAIL rows misfiled in the pattern
table (fixed in passing). The refinement's Reviewer-N1 "mirror" finding was RIGHT about the hole and WRONG
about the fix — the mirror it asked for was the mechanism that made the D4 collision fire; the durable lesson
is PAT-023 (derive invariants from the pipeline's output, not from a hand-maintained mirror of its inputs).
The composed-stack smoke probe's only employee has no employment dates, so it is blind to the entire
increment — giving it one windowed employee is a cheap, owed follow-up. Docker remains unavailable on the
owner's machine: ~40 of this sprint's pins are executed only by CI, and the "green when CI says so" posture
must stay explicit in every close until that changes.

**Knowledge produced:** PAT-022 (ask "employed?" before "which profile?" — planner skip + month-scoped reads),
PAT-023 (output-derived invariants), FAIL-007 (the planner's cause convention); the ADR-016 D4 amendment and
four ADR-040 consequence notes (D9 premise precision, D5 × D4 truncation, D7 existence-signal, D2 × admin-
create default); register rows QUAL-147 fixed, QUAL-148/149/150 registered with routes; the Increment-3
precondition (the backfill's write-once premise). Increment 2 of the ADR-040 program is COMPLETE pending the
watched CI run; Increment 3 (temporal editing + the S137 deferrals: category editability, the leaver-send
dead-end, the `EmploymentWindow` helpers, the QUAL-149/150 coupled pair as a candidate) is next.
