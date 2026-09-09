# Sprint 135 — Employment lifecycle time-control: the temporal-model ADR + program plan

| Field | Value |
|-------|-------|
| **Sprint** | 135 |
| **Status** | complete |
| **Start Date** | 2026-08-25 |
| **End Date** | 2026-08-25 |
| **Orchestrator Approved** | yes — 2026-08-25 (ADR-040 owner-ratified in-sprint) |
| **Build Verified** | n/a — docs-only sprint, no product code changed (baseline: S134-close CI-green `32859859712`) |
| **Test Verified** | n/a — docs-only; the `docs` CI job (check_docs.py) gates this sprint's push |

## Sprint Goal

Open the employment-lifecycle time-control program (ROADMAP arc item 3, owner-raised 2026-08-25)
with its governing decision record: **ADR-040 "Employee timeline & as-of resolution"** — the single
written rule for what was true about an employee on any date, and how the whole system (registration,
approval, calculation, accrual, access, editing) respects it. Code follows the ADR in increments
S136+; this sprint ships **documents only** (ADR + program plan + SYSTEM_TARGET lifecycle section +
KB debt pass). All authoring is Orchestrator-direct — `docs/**` is Orchestrator-only by the
CLAUDE.md constraint, so no domain agents are dispatched for the deliverables; the dual-lens
reviewers are the external checks.

**Inputs (all converged before this sprint opened):**
- Refinement rev 2 (`.claude/refinements/REFINEMENT-employment-lifecycle-time-control.md`) —
  three-agent recon-verified gap inventory (a–h + a2); dual-lens converged (Codex 2 BLOCKER →
  cycle-2 clean; internal Reviewer 0 BLOCKER, 3 WARNING absorbed). Readiness: READY.
- Owner rulings 2026-08-25: **OQ-1 = (a′) typed segments** · **OQ-2 = (c) record + flag
  (diagnostic worklist)** · OQ-3 lean (ii) + OQ-4 defaults ride into the ADR for ratification ·
  **re-hiring flagged as a potential situation** (single-window vs spells must be decided).
- Registered independents: SEC-046 (terminated-token time-entry write), QUAL-147 (live
  `ok_version` at compliance/balance consumers).

## Scope & Task Decomposition

| Task | Deliverable | Owner |
|------|-------------|-------|
| TASK-13501 | **ADR-040** authored (all seven decided sections per refinement §Proposed Approach 1) + KB INDEX row | Orchestrator |
| TASK-13502 | Dual-lens design review of ADR-040 + this program plan (Codex + internal Reviewer; cycle cap 2 per lens) → absorb → **owner ratification** | Orchestrator + both lenses |
| TASK-13503 | SYSTEM_TARGET.md gains an employee-lifecycle section aligned with the ratified ADR (spec currently ABSENT on lifecycle) | Orchestrator |
| TASK-13504 | Program plan finalized (§Program Plan below) + ROADMAP routing (backlog entry updated to point at ADR-040 + increments) | Orchestrator |
| TASK-13505 | KB debt pass: the two S134 proposed patterns written (**PAT-020** ambient correlation-id scope; **PAT-021** denial-trace decorate-and-delegate) + INDEX rows; **PAT-019** (already written S133) gains a usage-note append (the S134-close startup-seeder trap) + its INDEX gotcha clause | Orchestrator |
| TASK-13506 | Close bookkeeping (sprints/INDEX row, register cross-links, docs gate) | Orchestrator |

## Program Plan (increments S136+ — each opens with its own refinement + Step 0b)

**Increment 1 — enforcement core** (candidate S136): employment-window guards on ALL registration
paths (time-entries POST, skema save, absence) + the approval surface per ADR-040 D6; **SEC-046
closed** (+ pinning regression test); start/end cross-field validation + DB CHECK; user-create
carries the start date; date-free rejection bodies (D3); the D1 re-hire guard + the D3
window-edit strand guard; payout-listing audience adjudication.
*ACs:* a pre-hire/post-leave entry is 422-rejected on every path with a date-free body, pinned; a
terminated employee's live token cannot write (incl. the self-exemption yielding to subject
deactivation), pinned; `end < start` refused at API + DB; API user-create accepts + stores
`employmentStartDate`; a start-date edit past a closed spell's recorded end is 409-refused
regardless of settlement state, pinned; a window edit that would strand existing
entries/absences/approved months is 409-refused with the affected-month list, pinned; a
partially-employed month is approvable and lockable **as a whole**, pinned (D6).

**Increment 2 — calculation correctness**: ADR-040 D5 typed segments (new boundary causes
EmploymentStarted/EmploymentEnded in the D5 tie-break order + activate the reserved
`EmployeeProfileChange`); D9 accrual end-cap in running balances; **QUAL-147 closed** (date-overlay
moves into the resolver); D10 caller-ordering (window before profile); `employment_category`
becomes a dated `employee_profiles` column (D4). *ACs:* **the OQ-1 core, pinned:** a mid-month
leaver's plan contains an `EmploymentEnded`-caused NOT_EMPLOYED segment, present in the manifest,
with zero rule evaluations and zero export lines for that span (mirror AC for a mid-month
starter); a mid-month `part_time_fraction` change produces a segment boundary and different
**norm/pay** outputs per span — with the negative AC that vacation **day-count accrual stays
fraction-flat per ADR-031**; a windowless employee's export is byte-identical to pre-increment
(parity test); a pre-D5 segment manifest replays with the EMPLOYED default (replay-parity test);
a leaver's running balance stops accruing at the end date; compliance + historical balance reads
use the date-resolved OK version. Step-5a high-risk review mandatory (payroll boundary).
**[S137-dated note, 2026-09-02 — Increment 2 landed with ONE AC narrowed by a pre-existing gap]:** the
"mid-month `part_time_fraction` change produces a segment boundary and different norm/pay outputs per span"
AC is PROVEN under the straddle-safe test rule set and PINNED AS REFUSED under the live `RuleRegistry` set:
ADR-016 D4 refuses a whole-window (`AlignedWindow`) rule evaluated in ≥ 2 EMPLOYED segments (the S64 F4-1(b)
gap, re-registered as QUAL-149). Owner ruling 2026-09-02: hire/leave edges are TRUNCATIONS (≤ 1 EMPLOYED
segment plans — the leaver/starter ACs hold in the live wiring); a profile-change split while employed
stays refused until norm/overtime are reclassified with a pro-rating merger (Increment 3 candidate). The
leaver SPECIAL_HOLIDAY settlement over-count (D9's premise exception) was fixed in-sprint by owner ruling
(OQ-1a); category EDITABILITY is Increment 3 — **PRECONDITION (S137 Reviewer NOTE):** the S137 backfill copied the LIVE `users.employment_category` onto every history row on the premise that the column has been write-once since inception; when editability lands it must NOT retro-apply a new value to already-migrated history rows (a dated change starts a new row). Also owed to Increment 3 from S137: `EmploymentWindow.Overlaps/ClipTo/FirstNotEmployedDay` helpers replacing three hand-rolled window∩range predicates; a `PeriodStart` cause sentinel for boundary-less manifests; a retroactive-correction window pin. **Increment 4 gains (S137 TASK-13708 consequence):** the admin CREATE form surfaces the hire date pre-filled with today + editable, and the edit path allows backdating — an undated create now defaults to "hired today", which blocks back-filling pre-creation registrations until corrected.

**Increment 3 — temporal editing** *(AMENDED 2026-09-02 by owner ruling — S138 refinement: BACKDATED + today-dated changes + the worklist ship in Increment 3; FUTURE-dating moves to Increment 4 with the date picker under the "current ≠ live" read-model precondition — see ADR-040 §Amendment 2026-09-02)*: ADR-040 D8 — future-dated + backdated profile/agreement
changes (ADR-023 D8 reopened); the backdate-across-exported-month diagnostic worklist. *ACs:* a
future-dated position change applies on its effective date (not before); a backdated change
crossing an exported month is recorded truthfully AND lands a worklist row; ADR-033's settlement
rails (409 span guard, reverse-then-re-settle) are untouched, pinned.

**Increment 4 — lifecycle UX**: termination screen (the API-only endpoint gains UI), dated
position/agreement changes with an effective-date picker, **HR-gated** employee history timeline.
*ACs:* E2E flows for terminate / schedule a future change / view history.

> **Status after S140 (2026-09-09): UNCHANGED and NEXT. None of the three ACs above is met.** S140 was ruled (owner OQ-1, at
> refinement) to be the second-tranche fixed-clock conversion plus **the HR follow-up surface** — the separate, owner-raised item
> that S139 analysed — and explicitly *not* any part of this increment. An early draft of the S140 refinement called that work
> "Increment 4a"; the plan review rejected the label as misrepresenting this ledger, since the HR surface delivers none of
> terminate / schedule / history. Two items owed by earlier increments were pulled forward into S140 only because they needed no
> design: the admin CREATE form's hire date (the S137 consequence noted above) and routing the orphaned
> `OvertimePreApprovalManagement` page. Everything else here is owed.
>
> **What S141 inherits that this plan did not anticipate:** a live HR follow-up surface at `/admin/opfoelgning` with eight backing
> reads, so the termination screen's "last month sent?" and §26-request items have a place to live rather than needing their own
> page; and four **named** write forms deferred from S140 under owner ruling OQ-4 (reconcile payout, resolve a flagged settlement,
> record a §21 transfer agreement, settlement reversal) which belong with this increment's own write flows. The **"current ≠ live"
> read-model precondition** (ADR-040 § Amendment 2026-09-02) still gates future-dating and is the stated reason this increment was
> not folded into S140: four "current" readers must become as-of-today readers and two live caches need an explicit strategy, a
> change with roughly 200 read sites in its blast radius, which warrants its own refinement and plan review rather than a wave.

**Named follow-up program (not an increment):** org/unit membership history (ADR-040 D4 tail,
OQ-3 ruling ii) — model decided in the ADR, implementation scheduled separately; until then
historical org questions remain unanswerable (recorded consequence).

**Interim posture (between Increments 1 and 2):** an employee given a real window gets
registration blocks while payroll/balances still treat them as full-period. Per ADR-040 D2 this
bites nobody by default — ~~real windows stay OUT of the demo seed and shared fixtures until
Increment 2 lands~~; window-dependent tests opt in per employee.
**[S136-dated correction, 2026-08-26 — the struck sentence rested on a FALSE premise]:** the demo
seed had carried real employment windows all along (start dates on 100% of users, past end dates
on ~3% — `DemoGenerator.cs`), unnoticed when this posture was ratified. Owner ruled (S136 OQ-2a):
**reconcile, don't strip** — the seed keeps its windows; S136 TASK-13607 clamped the two collision
cohorts (tenure-0 pre-hire activity; inverted `end < start` leaver pairs, RED-proven 11+4 at full
scale) so enforcement lands green. Consequence accepted: demo users are window-enforced subjects
one increment before payroll/balances respect windows.

## Architectural Constraints Verified

- [x] Architectural integrity — the ADR builds on existing seams (ADR-020 3-case, ADR-018 D8 predicate, reserved BoundaryCause values); no new framework (both lenses verified the seam claims against code)
- [x] Domain correctness — OQ-1 (a′) preserves PlannedCalculation exact-coverage (verified at `PlannedCalculation.cs:98-129`); ADR-003 generalized, not violated; ADR-031 guard-rail named + carried into Increment 2's negative AC
- [x] Auditability — spells never silently erased (settlement-independent re-hire guard); backdating records truth + HR worklist; NOT_EMPLOYED segments explicit in the manifest; EMPLOYED replay default pinned
- [x] Integration isolation & delivery — no event-contract changes in this sprint (docs only); increments name theirs
- [x] Security & access control — employment dates never on the wire (D7); date-free error bodies + strand guard (D3); timeline view HR-gated; worklist HROrAbove (D8)
- [x] CI/CD enforcement — docs gate green in-sprint (`32888523721` docs job); every ruled behavior carries a pinned per-increment AC

## External Review (Step 0b/design — TASK-13502)

**Cycle 1 (2026-08-25), both lenses on ADR-040 rev 1 + the program plan:**
- *Codex:* 2 BLOCKER / 2 WARNING / 1 NOTE. B1 — the re-hire guard conditioned on settlement left a
  closed-unsettled spell overwritable → **absorbed** (D1 now triggers on the re-hire signature —
  start moved past a closed spell's end — settlement-independent). B2 — D6's "window-clean" held
  only post-enforcement → **absorbed** (D3 window-edit strand guard: 409 + affected-month list;
  rollout-safe by D2's NULL-unbounded). W1/W2 — missing D6 + OQ-1-core ACs → **absorbed**
  (Increments 1+2 AC lists extended). NOTE: all code-reality claims verified accurate.
- *Internal Reviewer:* 0 BLOCKER / 5 WARNING / 5 NOTE, all **absorbed**: the ADR-018 **D9→D8**
  citation drift (originated in the refinement — fixed in all three documents); boundary-cause
  tie-break placement specified in D5 (employment causes outrank all; `EmployeeProfileChange`
  after `PositionOverrideEffective`); the stranded-registrations hole (convergent with Codex B2);
  `employment_category` pinned to Increment 2 + the re-hire guard added to Increment 1's
  text/ACs; the two missing ruled-behavior ACs added; D3 actor-vs-subject + self-exemption
  sentence; D10 retitled as caller-ordering (ADR-023 D3 contract unchanged); EMPLOYED replay
  default + parity test; interim-posture note; D8 worklist HR-gated.
- **Cycle 2 (Codex re-verify of its BLOCKER fixes, 2026-08-25): "Clean — cycle 2 confirms
  resolution."** Both lenses converged; 0 residual findings. **Owner ratified ADR-040 the same
  day** — TASK-13502 complete.

## Close Review (Step 7a)

Both lenses on the full uncommitted close set (2026-08-25), run in parallel:
- *Codex:* 1 BLOCKER (close bookkeeping deliberately deferred until after review — filled on
  receipt: status/dates/approval + the sprints/INDEX S135 row) + 2 WARNINGs, both summary-fidelity
  drifts: the KB INDEX row restated the pre-fix "closed+settled" re-hire guard (→ corrected to the
  ratified settlement-independent wording) and SYSTEM_TARGET §N stated accrual pro-rating without
  the IMMEDIATE-grant exception (→ clause added). PAT-019/020/021, ROADMAP pointers, and the rest
  of the INDEX verified accurate.
- *Internal Reviewer:* **CLOSE-WITH-WARNINGS** — its 1 WARNING and first NOTE were the SAME two
  drifts Codex caught (independently converged; both already absorbed), plus 3 NOTEs: stale
  "ratification remaining" line (→ fixed above), TASK-13505 wording implying PAT-019 was written
  this sprint (→ reworded: usage-note append only), and PAT-019's INDEX row missing the new
  startup-seeder gotcha (→ appended). NOTE 4 informational (docs gate CI-only on this machine —
  established posture). **Every code-reality claim in the three PATs verified accurate; SYSTEM_TARGET
  §N / ROADMAP / registers verified consistent with the ratified ADR.**

## Test Summary

Docs-only sprint — no product code changed; counts carried from the S134-close CI-green baseline
(run `32859859712`: unit 995 + regression 1598 + DemoSeed 153 + smoke 7 + E2E + frontend, all
green). The `docs` CI job (check_docs.py: KB INDEX completeness incl. ADR-040/PAT-020/PAT-021
rows, sprint-log inventory, anchors) verifies this sprint's artifacts on its push.

## Sprint Retrospective

**What went well:** the recon-first refinement meant the ADR was written against verified code
reality, and both lenses then confirmed every load-bearing claim — the review argument moved to
DESIGN quality, where it caught real holes: Codex's two BLOCKERs (the settlement-conditional
re-hire guard; the window-edit stranding case) and the Reviewer's convergent stranding finding +
the ADR-018 D9→D8 citation drift that was one ratification away from being canon. The
AskUserQuestion rulings (OQ-1/OQ-2) before authoring meant zero design rework after owner input.

**What to improve:** the citation drift (D9 vs D8) originated in MY refinement and propagated
into two more documents before a lens caught it — cite-by-reading, not by memory, when naming
another ADR's decision numbers. Also: the first refinement draft over-attributed the OK-version
gap to payroll (Reviewer-corrected) — a reminder that a finding's blast radius needs the same
verification as its existence.

**Knowledge produced:** ADR-040 (accepted) · PAT-020 ambient-correlation-id · PAT-021
denial-trace decorate-and-delegate · a PAT-019 usage note (the throwing-outbox startup-seeder
trap from the S134 close remediation) · SYSTEM_TARGET §N (the spec's first employee-lifecycle
section) · SEC-046 + QUAL-147 registered (S134-close work, cross-referenced here as program
inputs).
