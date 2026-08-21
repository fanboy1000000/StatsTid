# Day-attribution of post-midnight hours (midnight-crossing shifts) — S132 TASK-0 research

**Status:** research verdict (S132, 2026-08-20). Produced to satisfy the owner-ruled OQ-2b
("fix day-attribution now") domain-truth PRE-TASK. **Ratification (2026-08-21 close):** the owner ratified
the DESIGN + the fix-now scope — OQ-2b (fix now), the "full fix" representation choice, and **ADR-039**
(projection-time normalization) — so TASK-1b shipped on that ratified approach. The underlying **legal
attribution rule itself remains analyst-interpretation, still PENDING Phase-B expert sign-off** (see §2's
sourcing flag + §6 open questions); this is acceptable BECAUSE ADR-039's projection layer keeps the rule
cheaply revisable — a Phase-B correction re-derives the read-model without touching the immutable events.
Sibling of `ferie-transfer-timing-research.md` and `vacation-consumption-mechanism-research.md`.

**Provenance:** analysis by a research subagent over the repo at baseline `455e34d`, reviewed by the
Orchestrator. Legal points are an analyst's reading of EU WTD 2003/88/EC + Arbejdstidsloven from general
knowledge — **the repo does not source working-time law** (see §2). Treat legal claims as interpretation
pending Phase-B expert sign-off, not as sourced fact.

> **⚑ ADJUDICATION OUTCOME (2026-08-20) — supersedes §3's "at ingest/registration" wording.** A dual-lens
> design adjudication (Codex + internal Reviewer, both CONVERGENT) ruled the normalization LAYER:
> **projection-time, NOT write-time.** Keep the raw crossing shift in the immutable `TimeEntryRegistered`
> event; derive the per-day split in the existing rebuildable `time_entries_projection` read-model (which
> `RestPeriodRule` + `PeriodCalculationService` already consume) BEFORE segmentation. Write-time event
> split/reshape is a **BLOCKER** (violates events-record-facts, ADR-001/018 — it bakes a contested,
> Phase-B-pending interpretation irreversibly into the event). Recorded in **ADR-039**; amends ADR-016
> row 6. Where §3 below says "normalize at ingest/registration," read "normalize in the projection
> derivation." Rest checks read the CONTINUOUS stint (instants, TASK-1a); only the hours-summing checks
> read the per-day split, with a shared work-stint continuity link between the halves.

---

## Plain-language summary (for a PM)

A shift that crosses midnight (e.g. 23:00 → 02:00) is one work stint whose hours fall on two calendar
dates. Every working-time check keys off a time entry's single `Date`. So: when 2 of those 3 hours are
worked after midnight, which day does the code count them toward — and is that the day the law and the
agreement-version machinery expect?

The four checks split into two groups:
- **Hours-summing checks** (max-daily-hours, the 48-hour ceiling) care *how much* work landed on a day.
  Post-midnight hours belong to the **next day (D+1)** — the wall clock says so, and so does the project's
  own rule that an entry's agreement version is fixed by its date (ADR-003). Today the code files *all*
  hours under the shift's start-date; that is wrong for a crossing shift, but the error is invisible in a
  whole-period sum and only bites at a period edge or an agreement-version boundary.
- **Rest checks** (daily 11-hour rest, weekly rest) are not really "which day" questions — they ask "was
  there a long-enough continuous gap between work?" That is an absolute-instant question; the calendar-day
  framing is a simplification the code already carries. (TASK-1a is moving daily rest to absolute instants;
  weekly rest should follow.)

**The sharp risk this task exists to surface:** the moment you make the hours-checks clock-accurate (push
post-midnight hours to D+1), you can push them across an **OK-version boundary** — a midnight crossing is
by definition a date boundary, and 2026-04-01 (OK24→OK26) is exactly such a date. If that movement happens
*inside* a rule pass that has already been handed one segment's entries, hours get judged under the wrong
agreement version, silently. That breaks ADR-003 (version-by-entry-date) and ADR-016's segment-safety
assumption.

*Glossary (first use):* **OK-version** = which collective-agreement edition applies (OK24 vs OK26);
**segment** = a slice of a calculation period lying entirely within one OK-version; **ADR-003** = an
entry's OK-version is resolved from its own date; **ADR-016** = the temporal-segmentation framework that
classifies each rule as safe/unsafe to split at a boundary.

---

## 1. Recommended attribution rule (per check)

| Check (`RestPeriodRule.cs`) | Rule | Impact |
|---|---|---|
| `CheckMaxDailyHours` (:178-203) | each hour attributed to the wall-clock day it falls in; post-midnight → **D+1** | **changes outcome** (today sums all under `e.Date`=D, :184-186) |
| `CheckDailyRest` (:211-278) | *not a day-attribution problem* — gap between absolute end-instant and next start-instant; a crossing shift is ONE continuous work period | no-op for attribution; the load-bearing fix is the instant reconstruction (TASK-1a) — done naively a split manufactures a false 0-hour gap at midnight |
| `CheckWeeklyMaxHours` (:345-375) | whole-period/segment sum — attribution only matters at the **period or OK-segment edge** | no-op except at edges (48h *averaging* itself = QUAL-123, deferred) |
| `CheckWeeklyRest` (:285-338) | counts distinct dates worked; D+1 attribution can flip a 6-day week into a flagged 7-day week | **changes outcome** — recommend retiring the distinct-date proxy for a continuous-gap test; interim: count the shift under start-day D only |

Genuinely outcome-changing: **max-daily-hours** and **weekly-rest**. Attribution-invariant except at edges:
**48h ceiling** and **daily rest**.

## 2. Legal basis (and an honesty flag)

**`docs/references/danish-agreements.md` carries NO working-time-law text** — no 11h rest, weekly rest, 48h
ceiling, Arbejdstidsloven, or EU WTD. The basis lives only in a code comment (`RestPeriodRule.cs:6-8`) and
config defaults (`AgreementRuleConfig.cs:70-74`: `MaxDailyHours=13.0`, `MinimumRestHours=11.0`,
`VoluntaryUnsocialHoursAllowed`, `RestPeriodDerogationAllowed`). All law below is analyst interpretation:

- **Daily rest** (WTD Art. 3 / Arbejdstidsloven §3): 11 consecutive hours within each rolling 24h — an
  instant/continuous-gap concept, not a calendar-day bucket. The current wall-clock/day-bucket framing is a
  simplification; TASK-1a's move to instants is the correct direction. Attribution matters only insofar as
  splitting the shift into two day-buckets must NOT read a false 0-hour gap at midnight.
- **Weekly rest** (WTD Art. 5 / §5): ~35h uninterrupted per 7-day period — again continuous-gap. The check
  labels itself "Simplified" (:283). A 2-hour bleed past midnight should not by itself deny a rest day →
  count under start-day only, pending a rigorous instant-based rewrite.
- **48h ceiling** (WTD Art. 6 / §4): average weekly hours ≤ 48 over a reference period (up to 4 months) —
  a whole-period total; attribution is a no-op except at the edge. (Averaging method = QUAL-123, deferred.)
- **Max-daily-hours** has no direct statutory cap; `13.0` is the derived 24−11 complement. Whether it is a
  real per-day guard (→ clock-accurate attribution correct) or should be subsumed into the instant-based
  daily-rest check is an owner question.

## 3. The OK-version segmentation interaction (the core finding)

**How entries reach a segment today:** the planner splits a straddling period at the OK boundary;
`PeriodCalculationService` filters entries into each segment wholesale by `e.Date`
(`PeriodCalculationService.cs:360`), and the rule's `NormalizeEntries` does the same
(`RestPeriodRule.cs:151-159`); each segment resolves its OK-version from `segment.StartDate` (ADR-003).

**Precedent:** `SupplementRule` already detects a midnight crossing (`SupplementRule.cs:135`) and stamps
ALL hours — pre- and post-midnight — under the entry's single `Date` (:100,:111). The established
convention today is **whole-shift-under-start-date**.

**Where it breaks:** ADR-016 classifies `REST_PERIOD_MAX_DAILY` as **segment-safe** with the note *"Day is
atomic at date-aligned boundaries"* (row 6). That safety rests ENTIRELY on every calendar day's hours
living in exactly one segment. A midnight-crossing shift on 2026-04-01 is the counterexample. Two failure
modes:
1. **Silent mis-versioning** — a shift filed under `Date=2026-03-31` is placed entirely in the OK24
   segment; a clock-accurate attribution change then credits its post-midnight hours to 2026-04-01, so they
   are evaluated under OK24 config yet belong to a date the rules resolve to OK26. Hours cross the OK
   boundary silently (violates ADR-003 + ADR-016 segment-safety).
2. **Lost hours / broken atomicity** — those hours never appear in the OK26 segment (filtered out by
   `e.Date` at :360), so 2026-04-01's OK26 totals are missing work that clock-wise occurred that morning.

The rest checks are safer by classification: `DAILY_REST`/`WEEKLY_REST` are aligned-window →
`RejectIfMultipleSegments` (the planner refuses rather than mis-attributes); `48H_CEILING` is
period/mergeable.

**Ruling (analyst recommendation):** the governing invariant is that an hour's OK-version is fixed by the
wall-clock day it is worked (ADR-003), so post-midnight hours are D+1's by both clock and version. The only
way to honour this WITHOUT cross-segment logic leaking into a rule pass is to make the entry itself
clock-correct **before** segmentation:

> **Normalize a midnight-crossing shift into two per-calendar-day entries at ingest/registration** — D
> carries 23:00→24:00, D+1 carries 00:00→02:00 — each with its own `Date` and its own OkVersion (ADR-003).

Under this: `e.Date` binds each half to the correct segment; ADR-016 row 6's "day is atomic" becomes true
by construction; the 48h per-segment averages get clock-correct hours; **no cross-segment logic is needed
in any rule** (they keep summing by `e.Date`, now clock-correct). Must be paired with TASK-1a's
instant-based rest logic so the two halves are recognised as one continuous work period (no false 0-hour
gap; no false extra worked-day).

**Latency (why this is not on fire today):**
- OK26 configs are placeholder-identical to OK24 (`danish-agreements.md:25`) → mis-versioning produces no
  numeric difference *yet*; the bug goes live the day OK26 diverges.
- The live compliance surface runs per-month, unsegmented (`ComplianceEndpoints.cs:121-133`), and OK
  boundaries fall on month starts → a monthly period never straddles a boundary mid-month, reducing this to
  the month-edge case there. The full per-segment interaction bites on the payroll
  (`PeriodCalculationService`) and retroactive-correction paths.

## 4. The architectural fork (the reason this needs owner + dual-lens adjudication)

The recommended normalize-at-ingest **appears to conflict with a prior dual-lens ruling.** The S132
refinement's Risks section recorded that "split-on-ingest was REJECTED (both lenses): it would bake a
derived interpretation into the immutable `TimeEntryRegistered` event (ADR-001/018 — events record facts,
not interpretations)…". So there is a genuine tension:

- **Interpret-in-rule** (the adjudicated disposition) preserves events-record-facts (auditability /
  architectural integrity) but, per §3, CANNOT correctly do day-attribution across an OK segment boundary.
- **Normalize-at-ingest** (this research's recommendation) achieves domain-correctness + segment-safety but
  appears to compromise events-record-facts — UNLESS it is a *lossless normalization* (same instants, same
  hours, two rows) rather than a derived interpretation.

**A material fact for that reconciliation:** the WorkTime ingest path ALREADY splits crossing shifts at
midnight — the validator drops any interval with `end ≤ start` (`SkemaEndpoints.cs:722`) and the frontend
`calcIntervalHours` uses the same `diff>0` filter (`SkemaGrid.tsx:147`). So on the primary path, crossing
shifts are already two per-day entries. A single crossing entry only reaches the rules via the `/time`
(`TimeEndpoints.cs:97-98`) and compliance-projection (`ComplianceEndpoints.cs:95-96`) paths, which do NOT
validate `end>start` (`RequestValidator.cs:17` only checks `hours ∈ (0,24]`). This suggests normalize-at-
ingest may be a *unification of an existing convention* (arguably lossless), not a new interpretation —
which is exactly the question the review lenses must settle.

## 5. Test guidance — a RED-on-old fixture straddling 2026-04-01

Shift 23:00 on 2026-03-31 → 02:00 on 2026-04-01. **Give OK26 a `MaxDailyHours` that differs from OK24 in
the fixture** (they are identical placeholders in prod), else the version error is numerically invisible.
Assert: 31-Mar receives only the pre-midnight hour(s); 01-Apr receives the 2 post-midnight hours; the 2
post-midnight hours are stamped **OK26**; conservation (total across segments = shift hours, none dropped
or double-counted); daily rest reads no false 0-hour gap; weekly rest does not flip a compliant 6-day week;
48h per-segment sums place the April hours in the OK26 segment only.

## 6. Open questions for the owner

1. **Representation decision (pivotal):** normalize crossing shifts into per-day entries at ingest
   (recommended) · forbid single-entry crossings by validation (close the `/time` + compliance gap) · or
   keep whole-under-start-date as a documented interim (clock-inaccurate but segment-safe; amend ADR-016
   row 6 + record a "revisit before OK26 diverges" residual). Everything else follows from this.
2. **Weekly-rest semantics:** should a shift bleeding past midnight count D+1 as "worked" under the
   "Simplified" proxy, or should weekly rest move to an instant-based continuous-gap (35h) test?
3. **Legal sourcing gap:** confirm the reference units (rolling 24h daily rest / rolling 7-day weekly rest
   / 4-month-averaged 48h) — is the calendar-bucket simplification accepted scope, or should the checks
   model rolling windows? Warrants Phase-B expert sign-off and a sourced `danish-agreements.md` entry.
4. **Max-daily-hours basis:** is `MaxDailyHours` an independent per-day policy, or purely the derived 24−11
   complement (→ possibly subsumed into the instant-based daily-rest check)?
