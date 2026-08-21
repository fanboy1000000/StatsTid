# ADR-039 — Midnight-crossing time-entry normalization at the projection layer

| Field | Value |
|-------|-------|
| **Status** | accepted — owner-ratified 2026-08-20 |
| **Sprint** | S132 (fix-next remediation; QUAL-001 day-attribution, owner ruled "full fix" OQ-2b) |
| **Domains** | Data Model, Backend, Rule Engine, Payroll Integration, Infrastructure |
| **Tags** | day-attribution, midnight-crossing, time-entry, projection, event-sourcing, ok-version, segmentation, working-time, arbejdstidsloven |
| **Supersedes / amends** | **Amends ADR-016 row 6** ("day is atomic at date-aligned boundaries"). Builds on ADR-001 (event immutability), ADR-003 (OK-version resolved by entry date), ADR-018 (transactional outbox + rebuildable projections). Implements the S132 TASK-0 research verdict (`docs/references/day-attribution-midnight-crossing-research.md`). |

## Context

A work shift crossing midnight (e.g. `23:00 → 02:00`) is one stint whose hours fall on two calendar dates.
Every working-time check in `RestPeriodRule.cs` keys off a `TimeEntry`'s single `Date`; entries reach a
calculation segment filtered wholesale by `e.Date` (`PeriodCalculationService.cs:360`), and each segment
resolves its OK-version from `segment.StartDate` (ADR-003). Today a crossing shift is representable as ONE
row (`Date=D, 23:00→02:00, Hours=3`) and is filed entirely under its start-date D.

Two consequences (S132 TASK-0 research): (1) the post-midnight hours are attributed to the wrong calendar
day for the hours-summing checks; (2) once OK26 diverges from OK24 (currently placeholder-identical), those
hours would be evaluated under the D-side OK-version though they belong (by clock + ADR-003) to D+1's — a
**silent cross-OK-version leak** that breaks ADR-016's segment-safety assumption ("day is atomic"). The
defect is latent today (OK26≡OK24; the live compliance path is monthly/unsegmented) but must be fixed
before OK26 diverges.

The owner ruled **"full fix now"** (OQ-2b). The design question — settled here by a dual-lens adjudication
(Codex + internal Reviewer, both convergent) — was **at which layer** to normalize crossing shifts into
per-calendar-day entries without violating the **events-record-facts** invariant (ADR-001/018). The S132
refinement had earlier rejected "split-on-ingest"; that rejection was correctly aimed at **write-time event
mutation** and does not bind projection-time normalization.

## Decision

### D1 — The immutable event records the raw stint; normalization is projection-time
`TimeEntryRegistered` continues to record the shift **exactly as submitted** (one crossing row, untouched).
The per-calendar-day split is derived in the existing **`time_entries_projection`** read-model
(`TimeEntryProjectionRow.cs`, `init.sql:1181-1202`) — already the shared shape that `RestPeriodRule` and
`PeriodCalculationService` consume (`ComplianceEndpoints.cs:90`, `TimeEndpoints.cs:244`). The seam already
exists; this changes how that projection is derived, not the infrastructure. **Write-time split (emitting
two `TimeEntryRegistered` events) or reshaping the event into per-day slices is PROHIBITED** — it bakes a
contested, Phase-B-pending interpretation irreversibly into the immutable audit stream (events-record-facts
violation). Rationale it is invariant-safe: event = the submitted fact; projection = deterministic, derived,
replaceable; a replay under the same versioned normalization policy reproduces the same rows (ADR-018).

### D2 — Deterministic, versioned normalization; conservation invariant
Normalization is a pure function of the raw entry: a crossing shift `(Date=D, Start, End)` with `End ≤ Start`
splits into `(D, Start→24:00)` and `(D+1, 00:00→End)`. It carries a **version tag** (so a future Phase-B
rule change re-derives cleanly) and MUST satisfy a **conservation invariant**: the split rows' durations and
`Hours` sum exactly to the original — no hours dropped or double-counted. A non-crossing entry passes
through unchanged.

### D3 — Per-half OK-version by each half's own Date (ADR-003)
Each projected day-row resolves its `OkVersion` from its own `Date`, so the D+1 half is OK26 at the
2026-04-01 boundary. This is consistent with the per-segment OK re-resolution already at
`PeriodCalculationService.cs:349-353` (OK-version is already treated as re-derivable downstream, not an
immutable fact — which is exactly why projection-time is safe).

### D4 — Continuity link: the halves know they are one stint
The two projected halves carry a shared **source-stint identity** (the source event / outbox id). Without it,
the instant-based rest checks would misread the halves as two separate work periods. `TimeEntry` has no such
field today (`TimeEntry.cs`) — this ADR adds one to the projection row / consumed model.

### D5 — The four checks consume different shapes (they do NOT all want the split)
- **Hours-summing checks** — `CheckMaxDailyHours`, and `CheckWeeklyMaxHours` at segment edges — consume the
  **per-day normalized rows** (so post-midnight hours count on D+1 and route to the correct OK segment).
- **Rest checks** — `CheckDailyRest`, `CheckWeeklyRest` — evaluate the **continuous stint via absolute
  instants** (the TASK-1a instant reconstruction), NOT the blunt per-day split. A naive per-day split fed to
  the rest checks would manufacture a false 0-hour gap at midnight and a false extra "worked day".
  **Rationale-of-record correction (Step-5a):** the rest checks are safe under OK-segmentation NOT because of
  ADR-016's aligned-window/`RejectIfMultipleSegments` classification, but because **`PeriodCalculationService`
  never runs the rest checks at all** — `EvaluateSegmentAsync` calls only `/api/rules/evaluate`,
  `evaluate-absence`, `evaluate-flex`; `RestPeriodRule` runs ONLY via `/api/rules/check-compliance`, the
  UNSEGMENTED monthly compliance path, where the full continuous stint is present. (If a future change ever
  routes compliance through the planner, this assumption must be revisited — do not inherit the aligned-window
  reason.) The rest reconstruction must run BEFORE the in-rule period filter so a boundary crossing's pre-half
  still informs its stint's true start day (Step-5a BLOCKER — else weekly-rest falsely marks the period's
  first day as worked). The normalizer must also emit **rejoinable** halves even when the source
  `SourceStintId` is null (a deterministic shared id), else a caller-supplied crossing cannot be rejoined.

### D5a — Scope: normalize on the calculation/rule INPUT path, not the display surfaces
`time_entries_projection` is shared by **display** surfaces (Skema-month grid, the Time-entries list,
Balance) AND the **calculation/rule** path. Display MUST continue to show the shift **as the user entered
it** (one crossing row) — users did not enter two shifts. Therefore the per-day split is applied on the
**calculation/compliance/rule input path, BEFORE the segment filter** (`PeriodCalculationService.cs:360`
and the compliance read that builds the `TimeEntry` list), as a pure derived transform — NOT by mutating
the stored display rows. Whether realized as a dedicated normalized read or a transform at the calc-input
boundary is an implementation choice, provided (a) display is unaffected, (b) it runs before segmentation,
and (c) it is deterministic + rebuildable (D2/D6). This keeps the event untouched (D1) AND the display
faithful, while the rules see clock-correct per-day rows.

### D5b — Boundary fetch: no dropped hours at a period edge (Step-5a finding)
Because normalization moves a crossing's post-midnight half to `Date = D+1`, a crossing on the LAST day of a
queried period would have that half filtered out AND — since the immutable source row stays dated D — the
next period's fetch (keyed on `Date ≥ nextStart`) would never see it, dropping the hours from BOTH periods
(a payroll underpayment + the OK-version hours unjudged at the boundary). Therefore every calc/rule-input
fetch MUST include the prior day's boundary-crossing source rows (extend the fetch lower bound by one day),
normalize, THEN apply the period/segment filter — so each half lands in its owning period and nothing is
dropped. This applies at EVERY time-entry→rule-engine input boundary (D6); a boundary that does not normalize
must be a documented exclusion.

**Where the widen lives (S132 finding):** the COMPLIANCE path fetches `time_entries_projection` in-repo, so
its read is widened there (done). The PAYROLL calc, however, receives its entries via the REQUEST CONTRACT
(`CalculateAndExportRequest.Entries` / `RecalculateRequest.Entries`) — `PeriodCalculationService` does no
server-side projection read to widen. So on the payroll path the widen is a **caller-contract obligation**:
any assembler of a payroll calc request MUST read `[periodStart-1 .. periodEnd]` from
`time_entries_projection` (PCS then normalizes before its segment filter, routing the boundary half to the
right segment). There is no in-repo request-assembler today (the endpoints are integration-boundary
contracts). **Until a caller widens, the payroll path retains the last-day-of-period drop for crossing
shifts = a payroll underpayment → a tracked go-live precondition** (disposition pending the Step-5a internal
Reviewer ruling: document-as-contract + track, vs. an in-repo PCS guard that refuses/flags a request whose
entries cannot cover `periodStart-1`).

### D6 — ONE shared normalization implementation, at the calc/compliance INPUT boundary
**As delivered (Step-7a reconciliation):** the one shared `MidnightCrossingNormalizer.Normalize` runs at each
calc/compliance INPUT boundary — the sole `PeriodCalculationService` logic entry point
(`CalculateWithOutcomeAsync`, applied BEFORE the `:360` segment filter; the single entry point covers forward
calc AND replay) and the compliance read (`ComplianceEndpoints`, before shipping to the rule engine). It does
**NOT** run in the display projection writer or the rebuild path — the stored `time_entries_projection` rows
stay display-faithful (D5a); normalization is a derived transform on the calc/rule input only. ONE shared
implementation; two divergent copies would recreate the QUAL-002 writer/rebuilder split-encoding defect.
*(The original phrasing "runs in the projection writer + rebuild path" was superseded by the D5a
input-only refinement during implementation — recorded here so future work does not split stored display rows.)*

### D6a — Boundary coverage: normalize the of-record boundaries; document the secondary exclusions
A boundary sweep (S132) enumerated every time-entry→rule-engine input. **Results-of-record boundaries — MUST
normalize (D6, done by 1b-1):** (1) `PeriodCalculationService` (`/api/rules/evaluate` + `evaluate-flex`;
pay-of-record via `/api/payroll/calculate-and-export` + retroactive corrections; emits the ADR-016 D10
manifest); (2) `ComplianceEndpoints` (`/api/rules/check-compliance`; the live Arbejdstidslov compliance
verdicts — the QUAL-001 surface). **Documented EXCLUSIONS (not of-record; not normalized):**
`WeeklyCalculationPipeline` (`/api/rules/evaluate` + `evaluate-flex`) and `TaskDispatcher`
(`/api/rules/evaluate`) — both reachable only via `/api/orchestrator/execute`, emit NO manifest/export/
official verdict, and forward raw `JsonElement`/passthrough (normalizing them would require materializing to
`TimeEntry` and coupling this fix to a secondary/legacy path). The version/payroll invariant is satisfied
without them (they produce nothing of-record); the defect is latent regardless (OK26≡OK24). **Tracked
follow-up:** normalize-or-retire these two secondary paths (register row, S132-discovered).

### D7 — `Hours ≠ elapsed time` allocation (default + Phase-B flag)
`TimeEntry.Hours` is supplied independently of `Start`/`End` and can differ from elapsed wall-clock time
(breaks, rounding, manual entry). When it does, allocating `Hours` across the midnight split is a **policy**,
not a mechanical split. **Default (this ADR):** allocate `Hours` **proportionally to each half's elapsed
duration**, preserving the D2 conservation invariant. This default is **Phase-B-confirmable** — the same
"analyst-interpretation-pending-expert-sign-off" status as the underlying attribution rule (research §2/§6);
the projection layer is precisely what keeps it revisable without touching events.

### D8 — Amend ADR-016 row 6 + the enforced ordering
ADR-016 row 6 changes from the unconditional *"Day is atomic at date-aligned boundaries"* to a
normalize-before-segmentation contract (see the ADR-016 row-6 wording for the exact text).

**Enforced mechanism (as delivered — Step-7a reconciliation):** it is NOT a "reject `EndTime ≤ StartTime`
at the filter" rule — a legitimate normalized pre-half is `[Start → 00:00]` (its `EndTime` IS `00:00`, i.e.
`≤ StartTime`) and DOES reach the filter by design. The real enforcement is (a) the **normalize-before-
segment-filter ORDERING** — `MidnightCrossingNormalizer` runs before `PeriodCalculationService.cs:360`, so
the filter only ever sees clock-correct per-day rows — plus (b) the **`AssertNoDroppedBoundaryCrossing`
fail-closed guard**, which trips when a normalized half is dated after `periodEnd` with an in-period stint
sibling (a boundary-last-day crossing whose post-half would be silently dropped → payroll underpayment).

**The `/time` `RequestValidator` tightening was CONSIDERED and REVERTED (S132).** Rejecting an un-split
`end ≤ start` crossing at `/time` would force a TWO-registration model whose two per-day rows lack a shared
continuity id → the rest checks would read a false 0-hour midnight gap. ADR-039's model is ONE registration
per crossing (the immutable event records the whole stint) + the normalizer splitting it into REJOINABLE
halves (shared/derived `SourceStintId`). So `/time` continues to ACCEPT un-split crossings; the normalizer +
the fail-closed guard handle them. No `/time` wire-contract change was made.

## Consequences

- **All three invariants hold:** events-record-facts (event untouched), domain-correctness / OK-by-entry-date
  (per-half `Date`→OkVersion), architectural integrity (ADR-016 segment-safety true by construction of the
  normalized projection).
- **Revisable:** because the attribution rule + weekly-rest semantics are analyst interpretation pending
  Phase-B, keeping them in a rebuildable projection means nothing immutable is ever wrong — the projection
  re-derives when the rule is confirmed/changed. This is the decisive reason projection beats write-time.
- **Named precondition (go-live-class):** the fix must land before `OK26` config diverges from `OK24`; until
  then the defect is numerically invisible. The S132 RED-on-old fixture MUST give OK26 a `MaxDailyHours`
  differing from OK24 or the version error is unobservable.
- **New model field** (source-stint continuity link) on the consumed `TimeEntry` shape + a shared
  input-boundary normalization function. **No `RequestValidator`/wire-contract change** (the `/time`
  tightening was reverted, above), no new table, no event-schema change, no change to stored display rows.
- **Legal-sourcing residual:** `danish-agreements.md` sources no working-time law (research §2); the
  attribution rule + the D7 allocation default are flagged for Phase-B confirmation and a sourced KB entry.
- **Dual-lens adjudication caught** the write-time-split invariant trap, the "four checks want different
  shapes" hazard, the shared-implementation requirement, and the `Hours≠elapsed` policy gap — all pre-code.
