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
  `AlignedWindow`/`Reject` rules — in the live rule set (four AlignedWindow rules) every mid-month hire or
  leave would have REFUSED the month instead of paying it. Ruled: the D4 refusal keys on how many EMPLOYED
  segments a whole-window rule would be EVALUATED in, not on boundary count. A whole-window rule is unsafe to
  SPLIT because two half-evaluations cannot be merged; an employment edge does not split evaluation (the
  NOT_EMPLOYED side evaluates nothing), so the rule runs once over a shorter span — the same class of input
  it already sees at every month edge. ≤ 1 EMPLOYED segment plans; ≥ 2 (a profile change while employed, two
  spells) still refuses. Windowless callers are behavior-identical. The remaining genuine-split refusal is
  registered as QUAL-149 (the S64 F4-1(b) gap with its true scope) — the "mid-month fraction change pays per
  span" AC of Increment 2 is proven under the straddle-safe test rule set and pinned as REFUSED under the
  live set until norm/overtime are reclassified with a pro-rating merger.
- **D7 precision note (S137 Reviewer NOTE, 2026-09-02):** the planner's D4 refusal messages carry the
  period, the EMPLOYED-segment count and the interior boundary CAUSE names (e.g. `EmploymentStarted`) — never
  a segment date. On a ≥ 2-EMPLOYED refusal that message can reveal that an employment edge EXISTS inside the
  period, via the Payroll host's unhandled-exception path only (audience: payroll operators; no client-facing
  handler echoes it). Acceptable under D7 as written — recorded so a later sweep does not re-find it. **Two further
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

**Replay vs re-plan (stated, S138).** Manifest replay stays historical-manifest replay: frozen segments, each
resolved at its frozen start from CURRENT dated history. A backdate INTERIOR to a frozen segment is therefore
invisible to replay by construction; the corrected truth reaches payroll only through the correction RE-PLAN
(`POST /api/payroll/recalculate`), which is exactly what the worklist row points at.

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
`CorrectionReachesSettlementBoundary` (half a) and `CorrectionTouchesEntitlementWindow` (half b);
pinned in `tests/StatsTid.Tests.Unit/Worklist/BackdateWorklistDerivationTests.cs`, including the
boundary-day case.
