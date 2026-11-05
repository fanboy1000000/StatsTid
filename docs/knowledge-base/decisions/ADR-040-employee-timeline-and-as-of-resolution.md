# ADR-040 — Employee timeline & as-of resolution (employment lifecycle time-control)

| Field | Value |
|-------|-------|
| **Status** | accepted — owner-ratified 2026-08-25 (S135 dual-lens: Codex 2 BLOCKER → cycle-2 clean; Reviewer 0 BLOCKER, all findings absorbed) |
| **Sprint** | S135 (employment-lifecycle time-control program, ROADMAP arc item 3; owner-raised 2026-08-25) |
| **Domains** | Data Model, Backend, Rule Engine, Payroll Integration, Infrastructure, Security, Frontend |
| **Tags** | employment-window, temporal-model, as-of-resolution, effective-dating, employment-spells, re-hire, segmentation, typed-segments, backdating, lifecycle, termination |
| **Supersedes / amends** | **Reopens ADR-023 D8** (same-day-only profile edits → dated edits, D8 below). Builds on ADR-003 (resolve by entry date — generalized here to every employee attribute), ADR-016 (segmentation; activates its D5b-reserved boundary causes), ADR-018 D8 (end-exclusive `[from, to)`), ADR-020 (3-case versioned writes), ADR-027 (temporal reporting lines — the org-history precedent), ADR-030 (start date as plain input — preserved for current storage, constrained by D1's re-hire guard), ADR-033 as implemented by S70 R1 (settlement + last-day-employed-inclusive end-date semantics — untouched). **Preserves ADR-013** (manual-only corrections; D8's worklist is its "cascade assistant", not a workflow) and **ADR-031** (day-count accrual fraction-flat — named guard-rail). |

## Context

The owner's requirement (2026-08-25): the system must be time-controlled at the employee level —
an employee starts on a date, leaves on a date, changes position on a date — and registration,
approval, balances/accrual, payroll calculation, and access must all respect those boundaries.

A three-agent recon (refinement rev 2, dual-lens converged) established that the **storage**
largely exists — effective-dated `employee_profiles` and `user_agreement_codes`, employment date
columns on `users`, a working end-date deactivation poller (`SettlementCloseService` Step A) —
but is consulted only by the settlement/accrual corner. In plain terms: the system *stores* the
timeline but does not *obey* it. Registration accepts any date (gap a; SEC-046); the approval
model is whole-month with no window awareness (a2); payroll segmentation is blind to profile and
employment boundaries (c — `BoundaryCause.EmployeeProfileChange` has sat reserved-unused since
ADR-016 D5b); running balances never stop accruing at the end date (d); the as-of resolver covers
3 of 6 attributes with `ok_version` live-read at two consumers (e; QUAL-147); a terminated
employee's live token can still write (f); nothing can be future-dated or backdated — every
change is force-stamped "today" (g/h), so the system cannot record reality when HR learns of a
change late.

Owner rulings feeding this ADR: **OQ-1 = typed segments (a′)** · **OQ-2 = record + flag (c)** ·
OQ-3 lean (ii) and OQ-4 defaults ratified with this ADR · **re-hiring is a potential situation**
(the current one-start/one-end schema would erase a prior employment on re-hire).

## Decision

### D1 — The employment window is a first-class domain fact, with spells-shaped semantics
The conceptual model is a list of **employment spells**: non-overlapping date ranges
`[start, end]`, **end inclusive = last day employed** (ADR-033 semantics as implemented and
pinned by S70 R1). The implementation NOW is exactly one spell, stored in the existing
`users.employment_start_date` / `employment_end_date` columns (ADR-030's "plain fact column,
corrections re-derive uniformly" stance is preserved for this storage). **All consumers read
through a new `IEmploymentWindowResolver`** (given employee + date → EMPLOYED / NOT_EMPLOYED,
honoring D2's NULL rule), so growing to a spells table later changes storage + resolver only —
never the consumers. **Re-hire guard:** the guard triggers on the **re-hire signature itself** —
moving `employment_start_date` to a date AFTER the recorded end of a **closed** spell (end date
set and passed), which is the one edit that unambiguously writes a second employment over the
record of the first. It is refused (409) **regardless of settlement state** until the spells
increment ships. Corrections to the same spell stay legal: boundary adjustments that keep the
range overlapping the recorded one, and clearing the end date (reactivation — "they didn't
actually leave"), which keeps its existing lifecycle + settlement-span 409 guard. Re-hire itself
(a second spell) is a **named deferred increment** of this program, not an error case forever.

### D2 — NULL means unbounded
`NULL employment_start_date` = employed since the beginning of time; `NULL employment_end_date` =
open-ended employment. Every guard passes through on a NULL side. Consequence: **no data backfill
is required** before enforcement activates — existing users, the demo seed, and ~3,200 tests keep
working; windows are opt-in per employee. (This is also why the employment window must be its own
check and can never be inferred from `employee_profiles` coverage — profile rows are deliberately
backfilled to `0001-01-01` per the S33 lesson.)

### D3 — One write-gate predicate (and date-free rejections)
Three gates, each with exactly one job:
- **The employment window** governs **what dates** are registrable: time entries, absences, and
  skema saves dated outside the subject's window are refused (422) for every writer, employee and
  admin alike. If the window is wrong, HR corrects the window — not the data past it.
- **Role** governs **who** may write for a deactivated leaver: HROrAbove, via the S70
  `IncludingTerminated` repository pattern. This FIXES the current accident where blanket
  `is_active = TRUE` repo filters lock even HR out of a leaver's final in-window month (the
  routine Danish-payroll correction case).
- **`is_active`** governs **login/session only** for the ACTOR. The SUBJECT's deactivation state
  is what selects the role floor above (writes for a deactivated leaver require HROrAbove) — and
  the existing self-write exemption (`writeFloor = null` when subject == actor) **yields to it**:
  a deactivated self is not exempt. This is the precise closure of SEC-046.
  *(Amended S147, 2026-11-03 — see "Amendment 2026-11-03" below: login also ends the day after the
  last day employed; the floors read "departed" (deactivated, or past the last day), not the flag
  alone; and a departed self is refused whatever their role.)*

**The window-edit strand guard.** Setting, narrowing, or backdating an employment date validates
against the subject's EXISTING registered data: if time entries, absences, or approved months
would be stranded outside the new window, the edit is refused (409) with a pointer list of the
affected months — mirroring the R13 settlement-span guard's contract — until the data is
corrected first. This keeps D6's premise airtight in BOTH directions (D3 blocks new out-of-window
data; the strand guard blocks windows that would orphan old data). At rollout there is no legacy
conflict by construction: every existing employee is NULL-unbounded (D2), so the guard only ever
fires on deliberate future narrowing.

**Rejection bodies are date-free**: a generic "outside the employment period" class message,
uniform for before-start and after-end, so employment dates (HR-scoped per `User.cs:38-62`) never
leak through error shapes and cannot be binary-searched by probing dates. This extends the
existing redaction rule from DTOs/JWTs/exports to error bodies.

### D4 — One as-of rule for all six employee attributes
"What was true about this employee on date X" resolves per attribute as:

| Attribute | As-of source | State today → target |
|-----------|-------------|----------------------|
| `position`, `part_time_fraction` | `employee_profiles` dated row | already dated — unchanged |
| `agreement_code` | `user_agreement_codes` dated row | already dated — unchanged |
| `ok_version` | **a pure function of the date** (`OkVersionResolver`, ADR-003) — needs no storage | the date-overlay **moves INTO `EmploymentProfileResolver`** so every caller is correct by construction (closes QUAL-147: today only PCS overlays per-caller; compliance + historical balance reads take the live column) |
| `employment_category` | becomes a dated column on `employee_profiles` | schema addition, **Increment 2** (where the resolver is reworked) |
| org/unit membership (`primary_org_id`, `unit_id`) | effective-dated history per the ADR-027 reporting-lines precedent | **model decided here; implementation is a NAMED FOLLOW-UP program** (OQ-3 ruling ii). Recorded consequence: until it ships, "which org in March?" stays unanswerable |

### D5 — Typed segments (OQ-1 = a′): the window enters payroll as a boundary, not as shrunken geometry
`PlannedCalculation`'s exact-full-coverage invariant is **unchanged** (the fail-closed constructor
stays byte-identical). New boundary causes `EmploymentStarted` / `EmploymentEnded` are added, and
the ADR-016 D5b-reserved `EmployeeProfileChange` is **activated** (position / part-time-fraction
effective dates split segments — closing the "UI shows what payroll will not pay" divergence).
`PlannedSegment` gains an employment state: **EMPLOYED / NOT_EMPLOYED**. NOT_EMPLOYED segments
evaluate no rules and emit no export lines, but appear in the segment manifest — non-employment is
structurally explicit and auditable, not encoded as "norm 0". A windowless employee's plan and
export are **byte-identical** to today (regression parity is an increment AC).
Two implementation constraints an agent must not guess:
- **Tie-break placement (extends the ADR-017 D9b order):** when boundaries coincide on a date, the
  recorded cause resolves as `EmploymentStarted > EmploymentEnded > OkTransition >
  AgreementConfigPromotion > LocalProfileActivation > PositionOverrideEffective >
  EmployeeProfileChange > EuWtdRulesetVersion` — employment-window causes outrank everything
  (non-employment is the strongest fact about a date; a hire on 2026-04-01 coincides with the
  OK24→OK26 transition and must record as the hire), and `EmployeeProfileChange` slots after
  `PositionOverrideEffective`. The manifest's `boundary_cause_summary` audit filter sees this
  choice.
- **Replay default = EMPLOYED:** every pre-D5 segment manifest's JSON lacks the new employment
  state; deserialization (`FromManifest`) MUST default it to EMPLOYED — a NOT_EMPLOYED default
  would make historical replays silently evaluate zero rules. A replay-parity test on a pre-D5
  manifest pins this.

### D6 — Approval and period locks: whole-month geometry stays; the window is orthogonal
`approval_periods` keep their monthly anchoring and ADR-034's export lock keeps its geometry. A
partially-employed month is approvable and lockable **as a whole**: its content is window-clean by
construction — D3 blocks out-of-window registration at write time, and D3's strand guard blocks
any window edit that would orphan already-registered data — so what the manager approves is
exactly the employed span's content; NOT_EMPLOYED segments carry nothing approvable.
(Alternative rejected: trimming approval scope to the employed span — it would fork the approval
geometry that ADR-034, the export lock, and the S106/S125 read models all key on, for no
information the typed segments don't already carry.)

### D7 — Employment dates cross into calculation server-side only
The payroll host's planner (and the compliance path) obtain the window via
`IEmploymentWindowResolver` reading the DB inside the host. **`EmploymentProfile` — the wire DTO
crossing the PAT-005 HTTP boundary — never carries employment dates**, preserving the `User.cs`
redaction rule. No employee-facing DTO, JWT, export, or (per D3) error body carries them either.

*Precision (S145 Step 7a, N-9):* since S145 two employment-derived facts do cross from payroll to the Rule Engine,
server to server, in the timeline call: the settlement cutoff (a leaver's last employed day) and the edges of the
pieces (a piece starts at a hire and ends at a leaving date) (`EvaluateTimelineRequest.cs:10-16, :24-25`). Both are
frozen in the manifest (`segment_manifests.plan_inputs_jsonb`; ADR-016 D10). The wire `EmploymentProfile` still
carries none.

### D8 — Temporal editing (reopens ADR-023 D8): future-dating and backdating become legal, with truth preserved
Profile and agreement-code changes accept **future-dated and backdated** `effective_from`
(HROrAbove; the ADR-020 3-case routing extended with an insert-between-rows case). Two policies:
- **Future-dated**: the change simply sits in history until its date arrives; consumers already
  resolve as-of (D4), so no scheduler is needed. The frontend's hardcoded "today" is removed
  (Increment 4 adds the date picker).
- **Backdated across an already-exported month** (OQ-2 = c): the change is **recorded truthfully**
  at its real date, and each affected exported month lands on a **diagnostic worklist** — a list,
  not an approval workflow (ADR-013's "cascade assistant" bound), **readable by HROrAbove only**
  (its rows reference employment-adjacent dates). Recalculation remains the existing audited
  manual path (`POST /api/payroll/recalculate`, single-period, no cascade).
  **End-date corrections that cross a settlement are excluded**: they keep ADR-033's
  reverse-then-re-settle rails and the existing 409 span guard, unchanged.
  **Clarification (owner-ruled 2026-08-26, S136 Step 5a — both lenses hit the tension):** this
  exclusion covers the *worklist/backdating policy* of D8 ONLY. **D3's strand guard is universal**
  — an end-date write on the settlement rails that NARROWS the window runs the same strand check
  (fail-closed, same pointer contract) inside the reversal's own locked transaction; a widening
  write cannot strand anything and runs no check, so legitimate reversals are never blocked.

### D9 — Accrual respects both ends of the spell
`AccrualMath.EarnedToDate` gains an end-cap (`asOf` clamped to the spell end), so the **running**
balance an employee/HR sees stops accruing at the last employed day — closing the current
asymmetry where only the settlement crystallization caps correctly. Employment-start pro-rating
stays as-is (already correct). **IMMEDIATE-grant types keep full-quota-on-hire for now** (OQ-4
default): whether care/child-sick/senior days pro-rate for a mid-year hire is unsourced domain
knowledge — routed to the Phase B expert list; ratifying this ADR ratifies the default, revisit
flagged.

### D10 — Callers ask "employed?" before asking "what profile?" (caller ordering, not a resolver rewrite)
The mechanism is **caller ordering**: consumers consult `IEmploymentWindowResolver` FIRST, and only
resolve a profile for dates inside a spell (D5's planner does this structurally — NOT_EMPLOYED
segments never resolve a profile). **`IEmploymentProfileResolver`'s ADR-023 D3 contract is
unchanged** (null on no covering row; the fail-loud "profile row without agreement row" exception
stays a data-integrity assertion — that state is a seeding bug). Pre-hire dates stop being a
latent 500 path the moment real windows exist, without touching the profile resolver's semantics.

## Consequences

- **Increment mapping** (program plan, `SPRINT-135.md`): Increment 1 = D1–D3 enforcement
  (+ SEC-046); Increment 2 = D4 (resolver/overlay, QUAL-147) + D5 + D9 + D10, Step-5a mandatory
  (payroll boundary); Increment 3 = D8; Increment 4 = lifecycle UX (termination screen, date
  pickers, **HR-gated** history timeline). Org-history (D4 tail) and re-hire spells (D1 tail) are
  named follow-up work, deliberately outside the four increments.
- **What was given up, and why (for the record):** shrunken-coverage segmentation (honest but
  touches the riskiest invariant in the codebase — typed segments buy the same auditability
  without it); auto-recalculation on backdating (contradicts ADR-013 — the worklist keeps the
  human in the loop); blocking backdating entirely (forces false history — the one thing an
  auditable system must never do); immediate spells storage (YAGNI until re-hire is real, but the
  resolver seam + re-hire guard make it a storage-only change later).
- **Test/fixture posture:** D2's NULL-unbounded rule means enforcement lands without a fixture
  red wave; window-dependent tests opt in per employee. Increment 2 carries byte-parity for
  windowless exports.
- **SYSTEM_TARGET.md** gains an employee-lifecycle section stating the requirement this ADR
  implements (the spec is currently silent on hiring, position change, and termination-as-process).
- **D9 premise precision note (S137, 2026-08-26 owner ruling OQ-1a):** the sentence "closing the current
  asymmetry where only the settlement crystallization caps correctly" was true for the VACATION settlement
  sites (valued at `valuationBoundary`, sites 9/10) but NOT for the SPECIAL_HOLIDAY settlement capture
  (`VacationSettlementService` site 8), which computed earned-to-`AccrualEnd` (31 Dec) regardless of the
  leave date — a mid-year leaver was over-credited. Fixed S137 (TASK-13703) as a deliberate settlement-value change,
  RED-on-old by ARITHMETIC (the unit pin asserts the uncapped call still returns the full quota) plus documented old values — the Docker settlement pin verifies the NEW value in CI — on the ADR-033 rails (a 30-Jun leaver settles 2.5 særlige feriedage where
  the old code produced 5.0). Via the settlement poller the over-count was latent (the S80 BLOCKER-1 passed-
  end-date guard fires first); the cap is what makes the value right when the termination-interaction slice
  routes leavers here.
- **D5 × ADR-016 D4 — employment edges are TRUNCATIONS, not splits (owner ruling 2026-09-02, S137
  TASK-13707):** D5's typed segments collided with ADR-016 D4's refusal of interior boundaries for
  `AlignedWindow`/`Reject` rules — in the live rule set (at the time four AlignedWindow rules: overtime, the weekly norm, and the daily and weekly rest rules) every mid-month hire or
  leave would have REFUSED the month instead of paying it. Ruled: the D4 refusal keys on how many EMPLOYED
  segments a whole-window rule would be EVALUATED in, not on boundary count. A whole-window rule is unsafe to
  SPLIT because two half-evaluations cannot be merged; an employment edge does not split evaluation (the
  NOT_EMPLOYED side evaluates nothing), so the rule runs once over a shorter span — the same class of input
  it already sees at every month edge. ≤ 1 EMPLOYED segment plans; ≥ 2 (a profile change while employed, two
  spells) still refuses. Windowless callers are behavior-identical. The remaining genuine-split refusal is
  registered as QUAL-149 (the S64 F4-1(b) gap with its true scope) — the "mid-month fraction change pays per
  span" AC of Increment 2 is proven under the straddle-safe test rule set and pinned as REFUSED under the
  live set until norm/overtime are reclassified with a pro-rating merger.
  **Updated S145 Stage B (2026-10-19, R-63; ADR-016 D2–D4, S145 amendments):** the live registry now has **three**
  AlignedWindow rules (`NORM_CHECK_WEEKLY`, first in registration order and so the rule a refused split names, R-73;
  and the daily and weekly rest rules), and payroll evaluates none of them. Overtime, the 37-hour norm and flex
  became `TimelineAware` (evaluated once across the whole month, each day under its own contract) rather than
  Mergeable with a pro-rating merger, and payroll hands the planner only the six rules it evaluates. So a mid-month
  fraction, position, category or Monday agreement change while employed is now paid part by part under the live
  set (QUAL-149 BUILT, evidence: all four Stage B runs matched in CI, S145 C-6/C-7); the one manual case left is an agreement change inside a
  settled week (QUAL-209). The truncation ruling above is unchanged and still governs any caller whose rule set
  holds an AlignedWindow or Reject rule.
- **D7 precision note (S137 Reviewer NOTE, 2026-09-02):** the planner's D4 refusal messages carry the
  period, the EMPLOYED-segment count and the interior boundary CAUSE names (e.g. `EmploymentStarted`) — never
  a segment date. On a ≥ 2-EMPLOYED refusal that message can reveal that an employment edge EXISTS inside the
  period, via the Payroll host's unhandled-exception path only (audience: payroll operators; no client-facing
  handler echoes it). Acceptable under D7 as written — recorded so a later sweep does not re-find it. **Superseded in
  part by S144:** `/api/payroll/recalculate`, `/api/payroll/calculate-and-export` and (TASK-14410) the raw `/export` and `/export-period` now DO echo the cause names, as the
  structured `interiorBoundaryCauses` of a redacted 422 (`kind: "payroll-recalc-blocked"`, `PayrollPlanRefusalProblem`).
  That list can include `EmploymentStarted`/`EmploymentEnded` when an employment edge coincides with a real split. The
  audience is payroll operators: `/recalculate`, `/export` and `/export-period` are `GlobalAdminOnly`; `/calculate-and-export` is `LocalAdminOrAbove` behind an org-scope check, so a Local Admin can receive it for an employee in their own org. The body carries no date and no employee id, so it stays
  within D7. The message itself is not forwarded. Since S144 its period renders culture-invariant ISO. **Two further
  precisions (S137 Step-7a close):** export-line `PeriodStart/PeriodEnd` stamps that equal a hire or leave date are the
  payroll boundary's LEGITIMATE content — the payroll system must know the paid period; D7's word "export" does not bar
  them. The remaining segment-date-bearing Payroll-host diagnostics (incl. the resolver's own fail-loud throw on a
  data-integrity fault) are registered as QUAL-151 for the production-hardening pass (log-redaction posture).
- **D2 × admin create — the DEFAULT hire date (owner ruling 2026-09-02, S137 TASK-13708):** an employee
  created through the admin endpoint WITHOUT an employment start date used to get NULL (D2 "unbounded past")
  while the same transaction stamped the first profile row `effective_from = today`. With D5 live, that
  creation month had a mid-month profile boundary and no employment edge — a genuine split (refused) for
  payroll and a month-start profile-not-found 500 for compliance. Ruled: when omitted, the hire date DEFAULTS
  to the profile's `effective_from` ("unknown hire date" = "hired today"); explicitly supplied dates are
  unchanged; seeded/legacy NULLs keep D2's unbounded meaning. The audit CREATED row records that the date was
  defaulted. The alternatives — register the residual, or make the field mandatory — were presented; the
  default keeps the S136 optional wire contract while making every new hire's first month plannable by
  construction. **Consequence to know (S137 Step-7a Reviewer WARNING, recorded):** the defaulted date also
  becomes the employee's window START for the Increment-1 write gates — an admin who creates a person WITHOUT a
  hire date can no longer back-fill that person's pre-creation registrations until the hire date is corrected
  (supply it at create, or edit it first). The admin create form surfacing the hire date pre-filled with
  today and editable (and an edit path that allows backdating) is the Increment-4 lifecycle-UX item.

## Amendment 2026-09-02 — D8 split by owner ruling (S138 refinement, dual-lens converged)

**What changes.** D8 named two policies — future-dating and backdating — and claimed "consumers already resolve
as-of (D4), so no scheduler is needed". The S138 seam recon showed that claim holds for RESOLVER-fed readers only.
The open-ended-row readers (`UserAgreementCodeRepository.GetCurrentAsync`, which feeds the login token; the
profile GET/ETag; the profile DELETE pre-read; the profile PUT's lock) and the two live caches
(`users.agreement_code`, `users.employment_category`, read at ~200 sites) all treat "the open-ended row" as
"current". A future-dated row would therefore put a not-yet-effective agreement into login tokens and the
profile editor — a Security AND Domain-correctness breach — and the caches could not refresh on the effective
date without a scheduler. Backdated and today-dated writes have no such problem: the writer knows whether the
row covering today changed.

**Ruling (owner, 2026-09-02):** Increment 3 ships **backdating + today-dated** changes with the diagnostic
worklist; **future-dating moves to Increment 4** together with the date picker, under a named PRECONDITION —
the "current ≠ live" read model: the four open-ended readers become as-of-today readers, and the `users.*`
caches get an explicit strategy (dated reads vs derived-at-write with a refresh). The SPRINT-135 program plan's
Increment-3 AC "a future-dated position change applies on its effective date" moves to Increment 4 accordingly.

**Also ruled with it (S138 OQ-2):** a backdate whose interval reaches a SETTLED ferieår does NOT revalue that
year's consumption (feriedage IS fraction-dependent — ADR-032 D3/D4 — even though the day-count quota is not —
ADR-031): the revaluation SKIPS every (type, year) group with an active ADR-033 settlement, and each such year
lands a SETTLED_YEAR row on the HR worklist pointing at the reverse-then-re-settle path, for every trigger kind.
"Settled" is not "exported" — the two facts are independent — so the flag is keyed on `vacation_settlements`,
not on `payroll_export_records`. (NARROWED 2026-09-03 — "reaches a SETTLED ferieår" is a CONJUNCTION
of two tests, not the entitlement-window test alone; see the 2026-09-03 sub-amendment below for the rule as
implemented.)

**Replay vs re-plan (stated, S138; rewritten S145 for the replay contract, ADR-016 D10 amendment 2026-10-15).**
Manifest replay is historical-manifest replay, and since S145 it consults no dated employee history at all, current or
otherwise. Its one dated read is configuration: the wage-type mapping, looked up with each segment's frozen key at the
segment's start exactly as the forward run did; mapping edits must be dated today, so a past period maps as it did
(ADR-016 D10; corrected S145 Step 7a, W-2). It re-runs the calculation on the inputs the manifest froze (the employment pieces, meaning the part-time
fraction, agreement and position for each stretch of employed days, the opening week's context days included; the
settlement cutoff; the opening flex balance) and re-reads the hours rows by the event ids it recorded, superseded
rows included. Any backdate, interior to a frozen segment or at its start, is therefore invisible to replay by
construction, and a replay whose recorded rows, configs or arithmetic no longer match refuses by name rather than
recomputing. The corrected truth reaches payroll only through the correction RE-PLAN
(`POST /api/payroll/recalculate`), which is exactly what the worklist row points at. *(Until S145 each frozen
segment was resolved at its frozen start from CURRENT dated history, so a backdate AT a segment's start did reach
a replay; the replay marquee pins that tested this now pin the freeze.)*

## Amendment 2026-09-03 — the settled-year selection rule is a CONJUNCTION (S138 implementation, owner ruling)

**Why this exists.** The 2026-09-02 amendment above ruled *that* a correction reaching a settled ferieår skips
revaluation and raises a worklist row. It did not say *which* years a given correction selects, and the sentence
it does carry — "a backdate whose interval reaches a SETTLED ferieår" — reads as a single test on the
entitlement window. Implementing it that way over-flags. This sub-amendment states the rule the code enforces.
It NARROWS the 2026-09-02 wording; it does not reopen the skip-and-flag policy, which stands unchanged.

**The rule.** A year is selected when BOTH halves hold:

- **(a) The correction reaches the settlement's valuation boundary.** A settlement is a photograph: at one
  moment we valued a holiday year and froze the numbers (ADR-033). A correction can only make that photograph
  wrong if it changes something on or before the LAST DAY the settlement counted. Formally, with
  `correctedIntervalStart <= boundary`, INCLUSIVE — a correction landing exactly ON the boundary day changes a
  day the settlement valued, because the settlement's own day query is inclusive at both ends.
- **(b) The corrected interval overlaps that entitlement year's accrual window OR its taking window**
  (per-type geometry from `EntitlementPeriodResolver`; SPECIAL_HOLIDAY's entitlement year is the accrual year
  whose taking window opens the following May).

**Why neither half alone.** Each over-flags on a different axis, which is what makes the conjunction the
answer rather than a preference:

- Window-only (what the 2026-09-02 wording implies) flags a correction that lands entirely AFTER the
  settlement was taken — inside the year's window, but past every day the photograph counted. Nothing the
  settlement valued moved, so there is nothing for HR to reverse.
- Boundary-only flags every old correction against every settlement of every other entitlement type and year,
  because "before the freeze" is true of most of history. HR would get a wall of rows with no year to act on.

**Trade-off accepted.** The conjunction can under-flag in one shape: a correction that starts after a
settlement's boundary but changes an input the settlement extrapolated forward. We take that, because
ADR-033 settlements value days already counted, not future days, so the shape is not reachable today. If
settlements ever project forward, half (a) is the half to revisit.

**Precision, not policy.** The rule selects which HR follow-up rows are raised. It does not gate, block, or
alter the correction itself — the write always lands and the truth is always recorded (ADR-013's bound:
a diagnostic list, never an approval workflow).

Enforced by `BackdateWorklistDerivation.SettledYearThreatened` (the composition),
`CorrectionReachesSettlementBoundary` (half a) and `SettledYearIntersects` (half b);
pinned in `tests/StatsTid.Tests.Unit/Worklist/BackdateWorklistDerivationTests.cs`, including the
boundary-day case.

## Amendment 2026-11-03 — S147: a leaver's last days (QUAL-230; owner rulings OQ-2, Q-B = (c), Q-C = (b))

**Why this exists, in plain words.** Until S147 the system switched a leaver off the day after their last working
day, and from that moment nobody could approve, reject, reopen or pay their final month: not the manager, not HR,
not payroll (QUAL-230). The owner ruled (S147 OQ-2, 2026-10-27) that the leaver's own manager keeps **5 calendar
days** after the last day to finish the month as usual, that from **day 6 only HR** may act, and that the leaver's
**own** access still ends the day after the last day, whatever their role. Building that changed three statements
of D3, so this amendment records the rule as built: TASK-14703, the TASK-14710 stack, TASK-14712 and the
TASK-14713 stack, all merged, their Docker facts green in CI-W2b (run `41310143330`), CI-W3 (run `41335270197`)
and CI-W3b (run `41641201066`). D1, D2, D6 and D7 stand; D7 gains one precision below.

**The three dates of a leaver's final month.** D is the last day employed (`employment_end_date`, inclusive, D1).

| | The leaver, acting | The leaver's record | Their manager | HR and payroll |
|---|---|---|---|---|
| **D** | as any employee | active | as before | as before |
| **D+1 … D+5** | cannot log in; a session opened earlier cannot change anything of their own, whatever their role; their authority as an approver over others has ended | still `is_active`, by design | approves, rejects or reopens from Teamoversigt, as before | as before |
| **D+6 on** | no access | switched off by Step A (the leaver job's switch-off step) | refused *(by date since S147 Step 7a, whether or not Step A has run; see the note at the end)* | HR approves, rejects, reopens and views the month; payroll's LocalAdmin pays it *(from D+6 by date; see the note)* |

Example: last day Friday 13 March 2026 → login refused from 14 March; the manager acts until 18 March; HR only
from 19 March. **The month's own deadline and payroll export cutoff do not move:** month-end + 5 (here 5 April),
owner-ratified and the same for every employee (`InstitutionalDeadlines`). For a month-end leaver the manager's
last day coincides with that deadline; for a mid-month leaver it falls earlier, and the month's deadline stays.
Nothing new is stored: the hand-over dates are derived from the end date and one constant (K-11).

**What changed in D3.**
1. **"`is_active` governs login/session only for the ACTOR" no longer holds.** Login also refuses a person whose
   last day has passed (`AuthEndpoints.cs:93-98`: the same generic 401 as a wrong password, logged
   `employment_ended`). The floors read **"departed"**: deactivated, OR the last day lies before today's
   Copenhagen date (`DepartedSubject.IsDeparted`, `src/SharedKernel/StatsTid.SharedKernel/Security/DepartedSubject.cs:101-116`).
   The last day itself still counts as employed. `is_active` still decides which reads see a person (active-only,
   or the R9c terminated-inclusive reads: the allowlist of surfaces that may address a switched-off person) and,
   from D+6, who may act for them *(decided by date since S147 Step 7a; see the note at the end of this amendment)*.
2. **A departed self has no role to fall back on.** D3 said a deactivated self "is not exempt" from the role
   floor. Under that letter, S136's Step 5a accepted that a deactivated HR-tier actor could still register and
   send their own facts (adjudicated behaviour (a), `SPRINT-136.md:89-90`). **That residual is superseded**
   (S147 Step 0b P-12, under OQ-2's "whatever the role"): when the actor is the subject there is no role fallback;
   the HR floor exists for HR acting on others. The two S136 pins now expect 403 and are renamed
   `…_Is403_ReDecidedS147` (`SendCommandTerminatedSubjectTests.cs:309`; `TerminatedEmployeeAccessTests.cs:653`).
3. **D3's role floor covers the grace.** At the self-service sites the floor refuses when the subject has departed
   AND (the actor is the subject OR the actor is below LocalHR) (`Endpoints/Helpers/DepartedSubjectWriteFloor.cs:94-110`).
   So during D+1 … D+5 the manager decides the month (approve, reject, reopen) but cannot write the leaver's hours,
   exactly as from D+1 before S147. HR may still correct them.

**Where the departed-self rule runs (owner Q-C = (b), 2026-10-28: door by door).**
- **Inside the org-scope validator,** in both employee overloads, right after the target read and before the
  terminated-inclusive overload's HR gate (`OrgScopeValidator.cs:115`, `:286`; the test at `:522-524`). It is the
  identity arm only, so a manager still reaches a leaver during the grace and HR still acts on leavers. **It
  refuses reads too:** a departed self is refused on every org-scoped route, reads included (R-30; ruled intended
  at the TASK-14703 security consult, R1). Self-reads that do not pass through the validator stay as before (the
  Employee own-data branch; K-13).
- **Inline at 13 write routes the validator does not reach:** the users PUT, roles grant and revoke, the unit PUT,
  unit delete, unit leaders add and remove, the reporting-line import, the delegate set and clear, the admin
  stand-in set and clear when the manager is the actor, and Payroll `/recalculate` (`Payroll/Program.cs:670-690`).
  Plus a derived guard on `/remove` for an actor among the removed person's reports
  (`ReportingLineEndpoints.cs:1434-1437`). Each answers 403 before its first write.
- **At the seven self-service sites** an Employee token reaches through the validator's own-data branch (the floor
  in point 3 above; `docs/SECURITY.md` lists the sites).
- **The census (P-1, the subject-key rule).** A route is guarded when the departed person's own id is the subject
  key of a row it writes (`users.user_id`, `role_assignments.user_id`, `unit_leaders.user_id`,
  `reporting_lines.employee_id`, `manager_vikar.absent_approver_id`). An id that only holds authority over someone
  else is left to the acting-user rule below. Of the 84 write routes in the three hosts, 32 close through the
  validator; 19 are guarded another way (13 inline, 2 by the self-service floor, 4 by retirement: the two project
  selection routes, retired by TASK-14710, and Payroll `/export` and `/export-period`, which TASK-14716 retires [merged `169e9980`, 2026-11-04: all 19 are now closed; see ADR-034's amendment of that date]);
  33 are classified out.
- **Alternative declined (Q-C = (b)).** One check at each service's front door, refusing every write by a departed
  actor. It would have covered other people's data as well, but it is a new cross-cutting gate the owner chose not
  to build. So a departed HR, admin or leader's last token can still act on *other people* until it expires:
  SEC-003's residual, and SEC-057 for the grace.

**One message per clause.** The departed-self refusal carries `DepartedSubject.DenyReason`, "A departed employee
cannot act on their own record" (`DepartedSubject.cs:76`), from the validator, the inline guards and the floor. A
below-LocalHR actor on *another* departed subject at a self-service site gets D3's sentence, "Writes for a
deactivated employee require LocalHR or above" (`DepartedSubjectWriteFloor.cs:66`). Where both apply, the self
message wins (`:103-104`). From D+6 a below-HR actor on a switched-off subject at the five surfaces below reads the
validator's "Terminated-employee access requires LocalHR or above" (`OrgScopeValidator.cs:293-297`) where it used
to read "Target employee not found". That reveals the person has left, never when, as S138's send already did.
Every body stays date-free (D3, D7); the server log names the clause.

**HR only from D+6: an R9c allowlist extension (TASK-14713).** From D+6 five surfaces admit a switched-off subject
through `ValidateEmployeeAccessIncludingTerminatedAsync`:
- approve, reject and reopen's leader arm, at the LocalHR floor (`ApprovalEndpoints.cs:409-410`, `:689-690`,
  `:1913-1914`);
- the Skema month view, HR tier only (`SkemaEndpoints.cs:224`, `:262`);
- Payroll `/calculate-and-export`, at the LocalAdmin floor (`Payroll/Program.cs:369-370`).

The manager's edge fails closed for a switched-off subject, so the manager is refused from D+6. *(S147 Step 7a, 2026-11-05: as built here, that rested on Step A's switch-off; since TASK-14725 the manager is refused from D+6 by date, on two layers. See the note at the end of this amendment.)* **An
employed-month bound runs after the gate** (R-15): a switched-off subject is admitted only for a month that holds
an employed day, otherwise D3's date-free 422 `month_outside_employment_period` (`ApprovalEndpoints.cs:61-85`,
`:117-123`). Because it runs after the gate, an actor the gate refuses sees the same 403 for every month, so the
bound cannot be used to probe employment dates. This is D6 unchanged: a month with any employed day is approved
and paid as a whole. The three decision endpoints lock through a terminated-inclusive tree-lock overload with the
same key (`ReportingLineRepository.cs:787`; the shared helper at `:741` and its six reporting-line callers stay
active-only) and audit against the subject's own organisation. Four-eyes holds across the grace (OQ-1): whoever
sent the month cannot also approve it (SEC-053), so if HR sends after D+5, a second HR-tier person approves. The R9c
inventory is `UserRepository.cs:270-292`.

**D7 precision.** The HRP-011 leaver row is the one HR-scoped read that now carries derived dates, "manager until
D+5, HR only from D+6" (`HrFollowUpApprovalReadRepository.cs:733-737`; refinement R-27). The end date itself still
crosses no boundary, and no employee-facing body carries either date.

**A departed approver (TASK-14712).** The grace would otherwise have kept a departed *manager* in charge of their
reports for 5 days. From D+1 an approver counts only while "acting": `is_active` AND the last day has not passed.
That is checked at five reads: the role floor and its prefetch (`DesignatedApproverAuthorizer.cs:633-668`;
`IAuthorityFactsSource.cs:256-259`), the resolver's activity check and its prefetch
(`ReportingLineRepository.cs:1345-1357`; `PrefetchedReportingLineDataSource.cs:211-218`), and the pending-list
walk (`ApprovalPeriodRepository.cs:388-403`). A departed manager is skipped and the month goes up the chain; whoever
may act on a month also sees it (see = act). The rule only removes authority.
- **Stand-ins (owner Q-B = (c), 2026-10-28; Step 0b P-2).** A leaver's active stand-in survives the leaver's
  deactivation until its end date; HR revokes it (`DELETE /api/admin/reporting-lines/{managerId}/vikar`) or
  reassigns the team with `/remove`, which closes it (`ReportingLineEndpoints.cs:1640-1647`). The resolver still
  consults the primary manager's stand-in whatever that manager's own state (`ReportingLineRepository.cs:1255-1260`,
  `:1290`). That is S74 R3a's behaviour; nothing was built for it. A stand-in a departed manager tries to appoint
  after leaving is refused by the departed-self rule.
- **The holder rule (P-1).** A stand-in who has herself left grants nothing; the month goes back to the manager she
  covered, or further up if that manager has left too (`ReportingLineRepository.cs:1293-1297`, `:1304-1306`).
- **Audit (K-4).** `ReportingLineManagerDeactivated` and the switch-off event `EmployeeEndDateDeactivationApplied`
  still fire when the row is switched off, on D+6 (`SettlementCloseService.cs:565`, `:581`): emitting
  "deactivated" on D+1 for a row that is still active would be false. The D+1 routing is reconstructable from
  `EmployeeEmploymentEndDateSet` plus this rule.
- **Not checked for departure (SEC-057).** The HR/Admin org-scope fallback on approve, reject and reopen, and the
  overtime decisions, trust the token's scopes. A departed HR or admin user's last token, minted on D at the
  latest, can still act on others until it expires (480 minutes).

**The switch-off (TASK-14710).** One constant, `ManagerGraceCalendarDays = 5`
(`EmploymentEndDateLifecycleWriter.cs:84`), read through one helper (`IsPastGrace` and
`GraceDeactivationCutoff`, `:99-108`) by both deactivation paths: Step A's `IsDeactivationDue`
(`SettlementCloseService.cs:1052-1054`), whose two SQL copies bind the same cutoff (`:464`, `:476`, `:534`), and
the writer's same-transaction `ComputeEndDateLifecycle` (`EmploymentEndDateLifecycleWriter.cs:303`). Step A and its
events fire on D+6 (K-4). Termination settlement runs from D+6 (`SettlementCloseService.cs:765-776`; K-2), so the
§26 payout request, which needs a SETTLED termination settlement, also opens from D+6
(`TerminationPayoutRequestEndpoints.cs:194-210`, `:245`; K-12). The special-holiday active pass excludes a passed
end date (`SettlementCloseService.cs:912-938`; K-3). `/remove` still deactivates at once, which ends the grace early
(`ReportingLineEndpoints.cs:1677-1686`; K-6, accepted).

**Residuals recorded, not fixed.**
- **Two reads of "today" (PAT-028, compute today once per operation; TASK-14703 consult R6).** The validator reads
  the Copenhagen date from its own `TimeProvider` once per call (`OrgScopeValidator.cs:524`); the handler reads it
  again for its own dates. The two can differ only for a request that straddles Copenhagen midnight, and the
  self-service sites re-check in-lock. *(Widened by TASK-14725: the validator now also reads the date for every subject with a recorded end date; see the note at the end of this amendment.)* **And (consult R4):** the clear routes reuse one date across a retry, so a
  lock wait that crosses midnight acts on the day the request arrived (the delegate clear reads it at
  `ReportingLineEndpoints.cs:2329`, before its retry at `:2346`; the admin stand-in clear at `:2828`, before
  `:2851`). The create routes re-read the clock per attempt.
- **A reversal corrected into the grace (K-14).** A settlement reversal whose corrected end date lands inside the
  grace reactivates the leaver (the writer's rule: a correction into the grace reactivates), so its re-settle finds
  nothing due, refuses `SupersedeNotEligible`, and the whole reversal rolls back
  (`SettlementReversalService.cs:381-391`). The operator bare-reverses, or retries from day 6.
- **The import.** During the grace a departed manager can still be named as a manager in the bulk reporting-line
  import, which checks only `is_active` (`ReportingLineEndpoints.cs:1042-1046`). The acting-user rule gives that
  manager no authority from D+1.

**What was given up, and why.** A front-door gate (declined, Q-C = (b)). Moving the month's deadline with the
grace (rejected: the deadline and cutoff are owner-ratified and the same for every employee; K-11). Storing a
hand-over date (not needed: it is derived). Emitting "manager deactivated" on D+1 (it would be false; the D+1
routing is reconstructable instead).

**Not built here:** telling the manager that the window is open or closing — the ROADMAP item "Notify the manager
to give a leaver's final month its last approval".

**Enforced by** `DepartedSubject`, `DepartedSubjectWriteFloor`, the validator clause and
`EmploymentEndDateLifecycleWriter.IsPastGrace`. **Pinned by** the Unit facts `DepartedSubjectTests`,
`DepartedSubjectWriteFloorTests`, `EndDateLifecycleWriterParityTests` and `SettlementCloseLeaverLogicTests`; the
Docker facts in `tests/StatsTid.Tests.Regression/Leaver/`, `Security/DepartedSelf*Tests.cs`, `LeaverLoginTests`,
`DepartedApproverTests` and `RecalculateDepartedSelfTests`; and the screen's `FollowUpLists.leaverActions.test.tsx`.

**S147 Step 7a, 2026-11-05 (TASK-14725): the manager's day 6 is decided by date.**

*Why, in plain words.* The table above says the manager is refused from D+6. As built by TASK-14713, that refusal
rested on Step A. The manager's paths and every leader-tier org-scope check read `is_active`, and only Step A,
polled every 5 minutes, switches the leaver off. So between Copenhagen midnight on D+6 and the next poll, or for as
long as the job failed, the leaver's own manager could still approve, reject or reopen the month and read the leaver's
data. Both Step 7a lenses found it (cycle 1: Fable W-1, Codex WARNING 1); it is registered as SEC-058 (fixed in code; it closes when C-2 runs its Docker pins green).
The owner ruled "(a) Fix it now". When the first commit showed that the leader-tier org-scope reads had the same
window, the owner ruled "(a) Extend now" (both 2026-11-05). TASK-14725 (merged `2cfae13c`) decides the cut-off
by the date on two layers. Step A's switch-off stays, as a second guard and as the source of the switch-off events
(K-4). Code lines below are at `0a64c1eb`.
- **Layer 1, the shared approver predicate.** Once a manager path has admitted the actor (the designated edge, the
  admin-assigned acting manager, the edge manager's stand-in, the unit leader or the unit leader's stand-in),
  `DesignatedApproverAuthorizer` refuses when the subject's last day employed lies before the authority date minus 5,
  that is from D+6 (`DesignatedApproverAuthorizer.cs:401`; the rule `IsPastManagerGrace`, `:422`, over
  `EmploymentEndDateLifecycleWriter.IsPastGrace`, the one grace constant). Every production authority read calls
  this predicate: approve, reject and reopen's leader arm, the allocation breakdown, the compliance and Skema edge
  arms, my-reports, Teamoversigt and HR's tree counts. So the pending lists and the actions still agree (see = act).
  The refusal is date-free; the server log names the clause `manager-grace-ended`.
- **Layer 2, the org-scope validator.** A still-active subject past the grace by date is treated as switched off
  (`OrgScopeValidator.cs:600-602`).
  - The active-only overload answers "Target employee not found" to every actor (`:160-161`).
  - The terminated-inclusive overload admits it only through a LocalHR-or-above scope (`:349-350`, `:356`, `:385`),
    exactly as a deactivated subject (TASK-14713's rule).
  - The departed-self clause still runs first (`:150-151`, `:339-340`). The log names the clause
    `past-grace-as-switched-off`.
  - Without this layer, a leader whose scope covers the leaver's organisation would still reach the leaver's reads
    and the overtime decisions until the switch-off. On approve, reject and reopen the validator's arm is floored at
    LocalHR, so there the shared predicate alone decides a manager's case.
- **The D+6 row, as built now.** The manager is refused from Copenhagen midnight on D+6, on both layers, whether or
  not Step A has run.
  - HR keeps every route it uses to finish the month: they are terminated-inclusive (TASK-14713's five surfaces and
    the R9c inventory), and payroll's `/calculate-and-export` still admits LocalAdmin (`PayrollPastGraceLeaverScopeTests`).
  - On the active-only routes (33 by the task's census, verified by Fable's review), HR gets at D+6 the answer it
    gets after the switch-off, "Target employee not found". Nothing changes for HR once the job has run; only the
    moment moves from the poll to the date.
  - The leader year-overview reads stay null-floored by design, but a switched-off target is admitted only through
    a LocalHR-or-above scope (`OrgScopeValidator.cs:385`). From D+6 a leader therefore no longer sees a leaver's
    year overview; HR still does.
- **Residuals recorded, not fixed.**
  - **PAT-028, widened.** The validator reads the Copenhagen date at most once per call, and now does so for every
    subject with a recorded end date, not only when the identity matches (`OrgScopeValidator.cs:145`, `:332`; the
    class remark `:48-54`). The handler may read the date again. Traced: for a request that straddles midnight,
    the outcome equals that of a request wholly on one of the two days, so a straddle cannot admit an actor whom
    neither day admits.
  - **The employed-month bound still keys on the flag** (`ApprovalEndpoints.cs:71`; Payroll `Program.cs:876`).
    It never applies to an active subject, so between D+6 and the switch-off it does not fire. The send refuses a
    month with no employed day (`ApprovalEndpoints.cs:2473-2480`), but a month sent first and an end date set
    retroactively before it later survives (the end-date writer touches no `approval_periods` row): such a month can
    be approved and paid until the switch-off, and gets the date-free 422 after it. A consistency residual; S148
    stage 0 keys the bound on "past the grace or switched off", the same two-layer rule (Step 7a cycle 2, Fable N-1).
  - **`/remove` of a leaver whose switch-off was missed works only to D+5.** `/remove` reads the person through the
    active-only check, so from D+6 by date it answers "not found", as it does for a switched-off person. HR reassigns
    the leaver's reports one by one through `POST /api/admin/reporting-lines`, whose check reads the report, not the
    leaver (`ReportingLineEndpoints.cs:87`, `:108`). Step A closes the leaver's own edges when it runs. HR can also
    switch the person off directly: `PUT /api/admin/users/{userId}` (`AdminEndpoints.cs:1579`, "the sanctioned
    deactivation path", `:1742`) or the end-date correction (`EmploymentDateEndpoints.cs:373`), whose writer
    deactivates with provenance when the date is past the grace (`EmploymentEndDateLifecycleWriter.cs:289-315`)
    (Step 7a cycle 2, Fable N-2).
  - **One extra read per admitted manager–employee pair** on my-reports and Teamoversigt: performance register F7.
- **Pinned by** the Unit facts `ManagerGracePredicateTests` (7; mutation M-26 trips 3 of them) and the Docker facts
  `LeaverPastGraceByDateTests`, the four D+5 and D+6 facts in `DepartedSelfValidatorTests`, and
  `PayrollPastGraceLeaverScopeTests`. Each Docker fact keeps the leaver active at D+6, with the leaver job off, and
  has D+5 boundary pins. The Docker facts are CI-pending until C-2 (`<C-2 run id>`).
