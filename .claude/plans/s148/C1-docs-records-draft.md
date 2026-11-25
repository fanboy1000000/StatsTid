# S148 C-1 — the remaining non-KB docs and the close records (draft)

*Planning Drafter (Opus 5.5), for the Orchestrator, 2026-11-25. Drafted at master `ec789343` (the brief named
`bda18b03`; master moved by one commit while I worked, the C2-R4 census registration). Every claim below was
opened at `ec789343`. This file only drafts; the Orchestrator applies each block by exact replacement.*

## What this is, in plain words

S148's code is finished and merged. What is left before the close candidate K can be committed is paperwork:
documents that S148's own changes made untrue, records the sprint promised to write "at close", and the close
records themselves (the quality re-grade, the sprint index row, the sprint log's header). This file drafts each of
those as an exact text replacement, so the Orchestrator can apply them mechanically and check each one.

Another drafter is writing the ADR and KB notes (ADR-014 … ADR-044, DEP-004, `docs/SECURITY.md`, RES-002, the
rest-period dossier, the CLAUDE.md dossier row and the KB INDEX). This file does not touch those. **Three blocks
here name ADR-044 and the dossier `docs/references/rest-period-24h-research.md`** (D-10, D-16, D-19); they read
correctly only if that drafter's work lands in the same commit (see "Needs the Orchestrator's choice", item 1).

## How to apply

- **Every block is a REPLACE.** `old` is the exact text to find, `new` its replacement. An "insert" is written as
  a REPLACE whose `new` repeats the anchor line and adds the new text, so there is one operation to apply.
- **Exactness.** Each block has an `old` fence and a `new` fence (four backticks, then `old` or `new`); the string is
  every line between that opening line and the next line of four backticks alone, without the final line break. Each `old` occurs exactly once in its file, checked by a node script over this file at
  `ec789343`, applying the blocks in order D-1 … D-43 (so a later block is checked against the file as earlier
  blocks leave it). Report of that check: end of this file.
- **Line endings.** LF files: `ROADMAP.md`, `docs/QUALITY.md`, `docs/sprints/INDEX.md`, `docs/sprints/SPRINT-148.md`,
  `docs/references/danish-agreements.md`, `docs/operations/legacy-db-upgrade-runbook.md`. **CRLF files:**
  `docs/FRONTEND.md`, `docs/references/agreement-source-register.md`, `docs/references/phase-b-handoff-package.md`,
  `docs/sprints/SPRINT-72.md`. The strings are written here with LF; for a CRLF file read every line break in
  `old` and `new` as CRLF (the check normalised the file to LF before matching).
- **Placeholders** use single guillemets so one search finds them all: `‹C-2 run id›`, `‹Step 7a verdicts›`,
  `‹close sha›` (as the brief asked), and four more the records needed: `‹close date›`, `‹K sha›`,
  `‹C-2 Regression n›`, `‹C-5 record›`. When each can be filled is in "Needs the Orchestrator's choice", item 2.

## The edits: 43 blocks

**Group A, owed by the sprint log, the plan or addendum A1 (D-1 … D-31): 31 blocks.**

| File | Blocks |
|---|---|
| `docs/FRONTEND.md` | D-1 … D-5 (the retired time-entry route; the live-region line) |
| `docs/operations/legacy-db-upgrade-runbook.md` | D-6 (stored `timer` rows; the typed-row rebuild, QUAL-273) |
| `docs/references/danish-agreements.md` | D-7 … D-10 (O-8's `SLS_0530`; the rest section, RR-8) |
| `docs/references/agreement-source-register.md` | D-11 … D-15 (O-8's wage-type rows: the five OK26 bundles) |
| `docs/references/phase-b-handoff-package.md` | D-16 (RR-6's five silent rest questions) |
| `ROADMAP.md` | D-17 … D-23 (§ 1: S149 next with the passed-on items; the clearing row out, O-26; the two owed lines) |
| `docs/QUALITY.md` | D-24, D-25 (the S148 re-grade, `anchor-sprint: 148`) |
| `docs/sprints/INDEX.md` | D-26 (the S148 row) |
| `docs/sprints/SPRINT-148.md` | D-27 … D-31 (Status, Plan, Orchestrator model and Refinement rows; O-28's record) |

**Group B, beyond the brief, optional (D-32 … D-43): 12 blocks.** Each is a statement S148 made untrue, or an item
the brief allowed but a ruling argues against. My lean for each is in its header and in "Needs the Orchestrator's
choice", item 3.

---

## Group A — owed

### D-1 · docs/FRONTEND.md · the deleted entry form's row (`:115`; BC-4 of TASK-14803)

Why: TASK-14803 deleted `TimeEntryForm.tsx` and the unrouted `TimeRegistration` page when the old time-entry route
was retired (commit `b8316737`, "the unrouted time-registration page, entry form and register path go"); no file of
that name exists under `frontend/src/components/` at HEAD. The row is rewritten in the file's own style for deleted
components (`~~ProjectPicker~~ | — | Deleted in S72 …`, `docs/FRONTEND.md:114`).

````old
| TimeEntryForm _(legacy)_ | `TimeEntryForm.tsx` | Superseded by SkemaPage; only referenced by unrouted legacy pages |
````

````new
| ~~TimeEntryForm~~ | — | Deleted in S148 (TASK-14803, `b8316737`) with the unrouted `TimeRegistration` page, when the old time-entry route `POST /api/time-entries` was retired (TASK-14802); hours are saved only through Skema |
````

### D-2 · docs/FRONTEND.md · the unrouted-pages sentence (`:185`)

Why: `TimeRegistration.tsx` is deleted (same commit); `EntitlementConfigEditor` is still in source
(`frontend/src/pages/admin/EntitlementConfigEditor.tsx:129`) and still not imported by `App.tsx` (its old route
only redirects, `App.tsx:161`).

````old
Legacy/orphaned pages still in source but **not routed**: `TimeRegistration` (superseded by `SkemaPage`); `EntitlementConfigEditor` (not imported by `App.tsx`). (`AbsenceRegistration` and `WeeklyView`, formerly listed here, have since been deleted from source.
````

````new
Legacy/orphaned pages still in source but **not routed**: `EntitlementConfigEditor` (not imported by `App.tsx`). (`AbsenceRegistration` and `WeeklyView`, formerly listed here, have since been deleted from source, and so has `TimeRegistration`, deleted in S148 with the old time-entry route, TASK-14803.
````

### D-3 · docs/FRONTEND.md · `useTimeEntries` has no production consumer (`:217`; BC-4)

Why: the hook now only reads (`frontend/src/hooks/useTimeEntries.ts:5-7`, "There is no register call: POST
/api/time-entries is retired"); `git grep useTimeEntries -- frontend/src` finds the hook and one test,
`frontend/src/hooks/__tests__/employeeTypedWire.test.ts:22`, and nothing else. The GET it calls still exists
(`src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs:15`). The sprint log owes this line at `:649`.

````old
| `useTimeEntries` | Time entry CRUD operations |
````

````new
| `useTimeEntries` | Read-only list of an employee's time entries (`GET /api/time-entries/{employeeId}`). Its register call left with the retired `POST /api/time-entries` (S148, TASK-14803), and since then **no production code uses it**: its only caller is its typed-wire test (`hooks/__tests__/employeeTypedWire.test.ts`) |
````

### D-4 · docs/FRONTEND.md · the deleted form leaves the file tree (`:292`)

````old
      ComplianceWarnings.tsx    # Domain: EU-WTD warnings + the rest-not-checked notice (S147)
      TimeEntryForm.tsx         # Domain: legacy (unrouted)
````

````new
      ComplianceWarnings.tsx    # Domain: EU-WTD warnings + the rest-not-checked notice (S147)
````

### D-5 · docs/FRONTEND.md · the live-region line (owed by the wave-3 brief adjudication; log `:1418`)

Why: the wave-3 adjudication found that "`docs/FRONTEND.md` is silent on live regions"
(`.claude/reviews/S148-wave3-briefs-adjudication.md:67`) and owed this line (`:165`; plan "Records owed",
`PLAN-s148.md:845`). The pattern is in the code at HEAD: `ComplianceWarnings.tsx:85-92` (one `role="status"` on the
outermost element, always mounted, none on items), `ComplianceWarnings.module.css:1-3` (`.region:empty`) and `:40-43`
(`.info`, `--color-info`), `SkemaPage.tsx:1509-1519` (the month status and the separate removal announcement),
`ui/Alert.tsx:16` (`role="alert"`). TASK-14807's W-1 is the source of the "static note" rule (log `:428`).
Teamoversigt's per-line regions (`TeamOversigt.tsx:309`, `:315`, `:321`, `:326`, `:332`) were ruled below the
register's floor at C-1 (log `:4703`, `:4720`), so they appear here as a known gap, not as a register row.

````old
- Import pattern: `import styles from './Component.module.css'`
````

````new
- Import pattern: `import styles from './Component.module.css'`

## Accessibility patterns

### Live regions (screen-reader announcements) — S148
- **One polite live region per changing panel, always mounted.** A panel whose content should be announced when it changes is a single `role="status"` (or `aria-live="polite"`) element that stays in the page and is filled in place; a region inserted with its text already in it is often not announced. The items inside carry no role of their own, so a changed set is announced once. Examples: the compliance warnings panel (`components/ComplianceWarnings.tsx:85-92`, S148 TASK-14814; empty while loading, and `.region:empty` takes the empty region out of the layout) and Skema's month status and removal announcement (`pages/SkemaPage.tsx:1509-1519`; the removal has a region of its own, because the status region speaks "Gemmer…" during a save and would overwrite it).
- **A note that does not change with what the user does is not a live region.** `Alert` is `role="alert"` (`components/ui/Alert.tsx:16`), which interrupts a screen reader and is announced again after every save, so a fixed explanatory note uses a plain element (S148 TASK-14807, review W-1).
- **Information is not a warning.** The "Hviletid ikke kontrolleret for N dage" notice uses the informational style (`--color-info`, the `.info` rule in `ComplianceWarnings.module.css`), not the breach style (S148, owner ruling R-22).
- **Known gap** (below the quality register's floor; recorded in `docs/sprints/SPRINT-148.md`, C-1): Teamoversigt's detail row gives each warning line its own `role="status"` (`pages/approval/TeamOversigt.tsx:309-332`), against this pattern.
````

### D-6 · docs/operations/legacy-db-upgrade-runbook.md · the two kinds of legacy hours data payroll no longer pays (A6; QUAL-273)

Why: two runbook lines are owed. **(1)** "a line in the legacy-database upgrade runbook that stored `timer` rows are
refused" (log `:395`; A6, told to the owner at `:388`). **(2)** "a line in the legacy-database upgrade runbook: carry
no historical typed `TimeEntryRegistered` events into a live payroll; reseed instead" (log `:636`; QUAL-273's row
says the same line is owed, `quality-finding-register.md:530`). Verified at HEAD: payroll's refusal and its exact
comparison (`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:1009-1054`, the comparison at `:1038`, the
"writes nothing" at `:1016-1017`, QUAL-BF-003's comment at `:1021-1022`); the Skema save writes `NORMAL`
(`SkemaEndpoints.cs:2993`); the rebuild replays `TimeEntryRegistered` with its activity type
(`ProjectionBackfillService.cs:186`, `:451-472`), at Backend start-up (`Backend.Api/Program.cs:600-603`) and from
`tools/ProjectionBackfill`. The section goes after the S139 section, before "Known Ordering Gap", the file's
position for sprint sections.

````old
## Known Ordering Gap
````

````new
## S148 — Two kinds of stored hours data that payroll no longer pays

**What changed and why it matters for an upgrade.** S148 retired the old time-entry route (`POST /api/time-entries`, TASK-14802 and TASK-14803). The Skema save is now the only way hours are written, and it stores every entry as ordinary work (`ActivityType = "NORMAL"`). Payroll refuses any month whose calculation window holds a stored time entry typed as anything other than empty or `NORMAL`: both calculating routes answer 422 `payroll-activity-type-refused` before anything is calculated, so nothing is written (`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:1009-1054`). The comparison is exact, so `timer` and a lowercase `normal` are refused too. A fresh database holds no such rows. A legacy database can hold them in two ways:

- **Stored `timer` rows.** ADR-028 D5 counted the retired timer's `timer` entries as ordinary time. Since S148 payroll refuses a month holding one, like any other typed entry (A6, told to the owner 2026-11-10). No route removes or re-types a stored entry, so such a month has no in-product remedy (QUAL-BF-003). **Runbook step:** do not carry a database holding `timer` (or any other non-`NORMAL`) time entries into a live payroll; reseed per the Pre-Launch Posture above.
- **Historical typed `TimeEntryRegistered` events.** The projection rebuild (`ProjectionBackfillService`, run at Backend start-up and by `tools/ProjectionBackfill`) replays every stored `TimeEntryRegistered`, its activity type included. If a database carries a typed event from before S148 whose projection row is missing, a rebuild that runs between payroll's check and its calculation could restore the row, and the month would be paid on it (QUAL-273, accepted). **Runbook step:** carry no historical typed `TimeEntryRegistered` events into a live payroll; reseed instead.

## Known Ordering Gap
````

### D-7 · docs/references/danish-agreements.md · "All mappings are identical for OK24 and OK26" is no longer true (`:190`; O-8)

Why: a node comparison of every `wage_type_mappings` seed tuple in `docker/postgres/init.sql` at HEAD gives 107
OK24 rows and 112 OK26 rows; the OK26 set is the OK24 set plus exactly five rows, `CHILD_SICK_CALL_HOME → SLS_0530`
for AC, HK, PROSA (`init.sql:318-320`), AC_RESEARCH (`:1446`) and AC_TEACHING (`:1482`), each under a
`shortcut(QUAL-249)` comment (`:317`, `:1445`, `:1481`).

````old
Source: `wage_type_mappings` seed data in `init.sql`. All mappings are identical for OK24 and OK26.
````

````new
Source: `wage_type_mappings` seed data in `init.sql`. The mappings are identical for OK24 and OK26 except one: since S148 the child's call-home day (`CHILD_SICK_CALL_HOME`) has a row under OK26 only, for every agreement (the Absence Types table below).
````

### D-8 · docs/references/danish-agreements.md · the call-home day's row in the Absence Types table (O-8)

Why: the five rows above exist (D-7); "Y" in all three columns because the AC, HK and PROSA rows are seeded.
QUAL-249 registers the code as provisional (R-16; `REF:36`, `REF:181`).

````old
| SPECIAL_HOLIDAY_ALLOWANCE | SLS_0570 | Special holiday allowance | Y | Y | Y |
````

````new
| SPECIAL_HOLIDAY_ALLOWANCE | SLS_0570 | Special holiday allowance | Y | Y | Y |
| CHILD_SICK_CALL_HOME | SLS_0530 | Child's sick day, call-home hours: **OK26 only**; the code is provisional (S148, QUAL-249) | Y | Y | Y |
````

### D-9 · docs/references/danish-agreements.md · the shared-code note gains `SLS_0530` (`:234-241`; O-8, R-16, BC-27)

Why: `PLAN-s148.md:789` (O-8): "the shared-code note `docs/references/danish-agreements.md:234` gains `SLS_0530` for
the call-home day, provisional (R-16, BC-27)". The refinement's ruling: `REF:181` ("the shared-code note gains it
at close (BC-27)"). The note's existing text is kept; a second paragraph is added after its last line.

````old
> registered the analogous `MERARBEJDE`/`OVERTIME_50 → SLS_0210` collision but not this one).
````

````new
> registered the analogous `MERARBEJDE`/`OVERTIME_50 → SLS_0210` collision but not this one).
>
> **S148 addition (2026-11-25; R-16, provisional):** `CHILD_SICK_CALL_HOME` and `CHILD_SICK_DAY` both map to
> `SLS_0530` under OK26. The call-home day (the rest of the working day on which a child falls ill after the parent
> has arrived at work; OK26 only) is credited toward the norm like a sick day since S148 (TASK-14811, QUAL-236), and
> payroll skips a line with no mapping, so each agreement needed a row. StatsTid exported it under the child's first
> sick day's code as a stated, provisional choice: the hours are right, only the payroll code may be wrong. The
> Phase B payroll experts name the real wage type, and the five rows are then replaced (QUAL-249; the seed rows
> carry `shortcut(QUAL-249)` in `docker/postgres/init.sql`).
````

### D-10 · docs/references/danish-agreements.md · the rest section (RR-8; owner Q2)

Why: the rest-rule research adjudication orders it: "`docs/references/danish-agreements.md` gets the rest section"
(`.claude/reviews/S148-RESEARCH-rest-24h-adjudication.md:117`; plan "Records owed", `PLAN-s148.md:855`). The file
has no rest section today. Every value is cited to the code at HEAD: `RestPeriodRule.cs` (the rule `:354-360`, the
13 hours `:377-380`, the zone `:121-125`, dating and one finding per day `:370-384`, the shown value `:432-434`, the
8-hour floor `:65-73` and `:428-430`, the warning text `:82-83`, the weekly check `:540-560`, the 48-hour check
`:618-654`, no configured value read `:33`); `ComplianceEndpoints.cs:188` (the read window); the seeds
`init.sql:1628-1682` (11.0 hours for all ten agreement and OK pairs; the derogation flag TRUE for HK and PROSA only;
the reference period 17). The section goes before "Entitlement Quotas".

````old
## Entitlement Quotas
````

````new
## Rest Periods (S148)

> **In plain words.** Since S148 the 11-hour daily rest is checked the way the authority (Arbejdstilsynet) applies
> the law: at the end of every piece of work, look back 24 hours; those 24 hours must hold at least 11 unbroken
> hours of rest. So nobody may still be working more than 13 hours after their last full rest ended. Before S148
> StatsTid grouped work by the day it started and measured only between days, so one shift over 13 hours, a day
> shift followed by a night shift, or a split day over 13 hours showed no finding (QUAL-253, fixed in S148 wave 3b
> by owner Q2, 2026-11-13). The verified research is `docs/references/rest-period-24h-research.md`; the model is
> ADR-044. The checks read the clock times typed in Skema.

| Rule | In StatsTid since S148 | Source and register |
|------|------------------------|---------------------|
| Daily rest | At least `MinimumRestHours` unbroken hours inside the 24 elapsed hours before each work end. Exactly the minimum passes; short rests are never added together | Arbejdsmiljøloven § 50, BEK nr. 606 § 3; QUAL-253 (fixed) |
| The minimum | 11.0 hours for every agreement and OK version (seed) | |
| "13 hours" | Not a setting: 24 minus the minimum | |
| Time | Elapsed hours in Europe/Copenhagen, the zone passed in by the host, so the clock-change nights are 23 and 25 hours long | |
| Dating | A finding is dated the day its work ends and belongs to that day's month; one finding per day, the lowest value | |
| Read window | From two days before the month | |
| Shown value | Rounded down to one decimal, so 10 h 59 min shows 10.9, never "11.0" | |
| Permitted cut (HK, PROSA) | Where the agreement allows derogation (`RestPeriodDerogationAllowed`: HK and PROSA), a rest of at least 8 hours is a warning and a shorter one a violation. The warning says what StatsTid does not check: at most once per 7 days (twice by local agreement), never two days in a row, and equal compensatory rest straight after | BEK 606 § 19; Statens arbejdstidsaftale § 12; QUAL-254 (frequency and compensatory rest not modelled) |
| AC staff | No derogation flag (the AC agreement has no rest clause). AC staff and managers are checked like everyone else, although some may be exempt person by person | BEK 606 § 6, never per group (PAV 19.8); QUAL-255 |
| Weekly rest | Simplified: a 7-day block, counted from the month's first day, in which every day has a work start is flagged. The legal test (35 unbroken hours, at most 6 days apart; 10 working days under the state agreement § 12 stk. 3; on-call never counts toward it) is not built | QUAL-256 (High) |
| 48-hour average | Average weekly hours over the checked period above 48.0 is a violation; the configured reference period (17 weeks in every seed) is not read | QUAL-123 |
| Days not on the clock | A day whose hours are not all typed as clock times is listed once a month as "Hviletid ikke kontrolleret for N dage", as information, not as a breach | Owner ruling R-22; QUAL-234 |

**Open legal questions.** Five points the sources leave silent are in `phase-b-handoff-package.md`, "S148
additions": the 7-day anchor, "døgn" in "not two days in a row", CIR 9807/1983, where compensatory rest sits, and
how a night shift counts toward the 10 working days. A sixth, what to assume before the first record, has a default:
time with no registered work is rest.

## Entitlement Quotas
````

### D-11 · docs/references/agreement-source-register.md · SR-AC-OK26-003, the AC OK26 wage-type bundle (O-8, third part)

Why: O-8 asks that "the agreement-source register's wage-type rows [are] checked" (`PLAN-s148.md:789`). Checked:
the five OK24 bundles (SR-AC-OK24-037, SR-HK-OK24-030, SR-PROSA-OK24-008, SR-AC_RESEARCH-OK24-006,
SR-AC_TEACHING-OK24-006) stay true, since no OK24 mapping changed (D-7's comparison). The five OK26 bundles say
"identical to OK24", which S148 made untrue. This row itself foresaw the case ("OK26 may introduce new time types
requiring new mappings", `agreement-source-register.md:953`). The register's own rule for "an OK version
transition introduces a new value" is a `supersession_history` entry plus the current value
(`agreement-source-register.md:95-101`), which is what D-11 … D-15 do. The call-home day's AC source is the AC
settlement item 4, its circular pending (`quality-finding-register.md:493`, QUAL-236; QUAL-BF-004).

````old
| `current_encoded_value` | Identical to AC OK24 (see SR-AC-OK24-037 bundle). Rows at init.sql:290 (NORMAL_HOURS), 297 (MERARBEJDE), 306 (VACATION) and continuing through L1201 (NORM_DEVIATION OK26 AC). |
| `authoritative_source` | pending — SLS wage type codes are Personalestyrelsen-administered and typically stable across OK transitions; OK26 cirkulær may introduce new wage types (e.g., for new compensation categories) that would require new mappings |
| `interpretation` | AC OK26 wage type mappings mirror AC OK24. SLS codes stable across OK24 → OK26 transition. New mappings (if any) will be added as Phase B identifies them from the OK26 cirkulær. |
| `confidence_level` | HIGH for the existing mappings (SLS codes are stable infrastructure; the inheritance is correct); LOW for "completeness" (OK26 may introduce new time types requiring new mappings that don't exist in the OK24 set) |
| `interpretation_authority` | Personalestyrelsen (SLS technical) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
````

````new
| `current_encoded_value` | Identical to AC OK24 (see SR-AC-OK24-037 bundle) **plus one mapping since S148: `CHILD_SICK_CALL_HOME → SLS_0530`** (the OK26 call-home day; provisional, QUAL-249; `init.sql:318` at S148's close). Rows at init.sql:290 (NORMAL_HOURS), 297 (MERARBEJDE), 306 (VACATION) and continuing through L1201 (NORM_DEVIATION OK26 AC). |
| `authoritative_source` | pending — SLS wage type codes are Personalestyrelsen-administered and typically stable across OK transitions; OK26 cirkulær may introduce new wage types (e.g., for new compensation categories) that would require new mappings |
| `interpretation` | AC OK26 wage type mappings mirror AC OK24. SLS codes stable across OK24 → OK26 transition. New mappings (if any) will be added as Phase B identifies them from the OK26 cirkulær. |
| `confidence_level` | HIGH for the existing mappings (SLS codes are stable infrastructure; the inheritance is correct); LOW for "completeness" (OK26 may introduce new time types requiring new mappings that don't exist in the OK24 set) |
| `interpretation_authority` | Personalestyrelsen (SLS technical) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | `[{date: 2026-11-12, from_value: "(no CHILD_SICK_CALL_HOME mapping)", to_value: "CHILD_SICK_CALL_HOME → SLS_0530 (provisional)", source_url: "pending — the code is StatsTid's provisional choice (S148 refinement ruling R-16); the call-home day itself: the AC settlement item 4 (in force 1 Apr 2026), circular pending (QUAL-236, QUAL-BF-004)", commit: "26065e1a (S148 TASK-14811 stack merge)"}]` |
````

### D-12 · docs/references/agreement-source-register.md · SR-HK-OK26-003 (O-8)

Why: as D-11. HK's call-home source: 021-26 § 14 (`quality-finding-register.md:493`); the row is `init.sql:319`.

````old
| `current_encoded_value` | Identical to HK OK24 (see SR-HK-OK24-030 bundle). |
| `authoritative_source` | pending — SLS technical documentation; codes stable across OK24 → OK26 transition |
| `interpretation` | HK OK26 wage type mappings mirror HK OK24. SLS codes stable. |
| `confidence_level` | HIGH for existing mappings (stable infrastructure); LOW for completeness (OK26 may introduce new wage types) |
| `interpretation_authority` | Personalestyrelsen (SLS technical) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
````

````new
| `current_encoded_value` | Identical to HK OK24 (see SR-HK-OK24-030 bundle) **plus one mapping since S148: `CHILD_SICK_CALL_HOME → SLS_0530`** (the OK26 call-home day; provisional, QUAL-249; `init.sql:319` at S148's close). |
| `authoritative_source` | pending — SLS technical documentation; codes stable across OK24 → OK26 transition |
| `interpretation` | HK OK26 wage type mappings mirror HK OK24. SLS codes stable. |
| `confidence_level` | HIGH for existing mappings (stable infrastructure); LOW for completeness (OK26 may introduce new wage types) |
| `interpretation_authority` | Personalestyrelsen (SLS technical) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | `[{date: 2026-11-12, from_value: "(no CHILD_SICK_CALL_HOME mapping)", to_value: "CHILD_SICK_CALL_HOME → SLS_0530 (provisional)", source_url: "pending — the code is StatsTid's provisional choice (S148 refinement ruling R-16); the call-home day itself: 021-26 § 14 (QUAL-236)", commit: "26065e1a (S148 TASK-14811 stack merge)"}]` |
````

### D-13 · docs/references/agreement-source-register.md · SR-PROSA-OK26-003 (O-8)

Why: as D-11. PROSA's call-home source: 020-26 § 14 (`quality-finding-register.md:493`); the row is `init.sql:320`.
The "22 mappings" count is the S36 author's and is left as written. (The three base rows are `init.sql:318`, `:319`,
`:320`: AC, HK, PROSA in that order.)

````old
| `current_encoded_value` | Identical to PROSA OK24 (22 mappings, same as HK). |
| `authoritative_source` | SLS technical documentation |
| `interpretation` | Placeholder inheritance. SLS codes stable. |
| `confidence_level` | HIGH for existing mappings; LOW for completeness |
| `interpretation_authority` | Personalestyrelsen (SLS technical) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
````

````new
| `current_encoded_value` | Identical to PROSA OK24 (22 mappings, same as HK) **plus one mapping since S148: `CHILD_SICK_CALL_HOME → SLS_0530`** (the OK26 call-home day; provisional, QUAL-249; `init.sql:320` at S148's close). |
| `authoritative_source` | SLS technical documentation |
| `interpretation` | Placeholder inheritance. SLS codes stable. |
| `confidence_level` | HIGH for existing mappings; LOW for completeness |
| `interpretation_authority` | Personalestyrelsen (SLS technical) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | `[{date: 2026-11-12, from_value: "(no CHILD_SICK_CALL_HOME mapping)", to_value: "CHILD_SICK_CALL_HOME → SLS_0530 (provisional)", source_url: "pending — the code is StatsTid's provisional choice (S148 refinement ruling R-16); the call-home day itself: 020-26 § 14 (QUAL-236)", commit: "26065e1a (S148 TASK-14811 stack merge)"}]` |
````

### D-14 · docs/references/agreement-source-register.md · SR-AC_RESEARCH-OK26-003 (O-8)

Why: as D-11; the row is `init.sql:1446`.

````old
| `current_encoded_value` | Identical to AC_RESEARCH OK24 mappings (12 entries; init.sql:980–991 byte-identical to L965–976 except OK version). Inherits same SLS code divergence from AC base. |
| `authoritative_source` | pending (Phase B — same finding as OK24) |
| `interpretation` | Same wage-type-code-divergence pattern as OK24. |
| `confidence_level` | LOW (inherits SR-AC_RESEARCH-OK24-006) |
| `interpretation_authority` | Personalestyrelsen (SLS technical) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
````

````new
| `current_encoded_value` | Identical to AC_RESEARCH OK24 mappings (12 entries; init.sql:980–991 byte-identical to L965–976 except OK version). Inherits same SLS code divergence from AC base. **Plus one mapping since S148: `CHILD_SICK_CALL_HOME → SLS_0530`** (the OK26 call-home day; provisional, QUAL-249; `init.sql:1446` at S148's close). |
| `authoritative_source` | pending (Phase B — same finding as OK24) |
| `interpretation` | Same wage-type-code-divergence pattern as OK24. |
| `confidence_level` | LOW (inherits SR-AC_RESEARCH-OK24-006) |
| `interpretation_authority` | Personalestyrelsen (SLS technical) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | `[{date: 2026-11-12, from_value: "(no CHILD_SICK_CALL_HOME mapping)", to_value: "CHILD_SICK_CALL_HOME → SLS_0530 (provisional)", source_url: "pending — the code is StatsTid's provisional choice (S148 refinement ruling R-16); the call-home day itself: the AC settlement item 4 (in force 1 Apr 2026), circular pending (QUAL-236, QUAL-BF-004)", commit: "26065e1a (S148 TASK-14811 stack merge)"}]` |
````

### D-15 · docs/references/agreement-source-register.md · SR-AC_TEACHING-OK26-001..004, its wage-type part (O-8)

Why: as D-11; the compound row covers four domains, of which 003 is `wage_type_mappings`; the row is `init.sql:1482`.

````old
| `current_encoded_value` | Identical to AC_TEACHING OK24 across all 4 domains. AnnualNormHours = 1680 maintained. |
| `authoritative_source` | pending — OK26 cirkulær under finalization. |
| `interpretation` | Placeholder inheritance for all 4 data domains. Inherits all AC_TEACHING OK24 findings (entitlement gap, wage-type-code divergence, AnnualNormHours divergence from AC_RESEARCH). |
| `confidence_level` | LOW (placeholder) |
| `interpretation_authority` | Personalestyrelsen (anticipated) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | (none) |
````

````new
| `current_encoded_value` | Identical to AC_TEACHING OK24 across all 4 domains. AnnualNormHours = 1680 maintained. **Exception since S148, in 003 (`wage_type_mappings`): plus `CHILD_SICK_CALL_HOME → SLS_0530`** (the OK26 call-home day; provisional, QUAL-249; `init.sql:1482` at S148's close). |
| `authoritative_source` | pending — OK26 cirkulær under finalization. |
| `interpretation` | Placeholder inheritance for all 4 data domains. Inherits all AC_TEACHING OK24 findings (entitlement gap, wage-type-code divergence, AnnualNormHours divergence from AC_RESEARCH). |
| `confidence_level` | LOW (placeholder) |
| `interpretation_authority` | Personalestyrelsen (anticipated) |
| `last_verified_by` | pending |
| `decision_date` | pending |
| `supersession_history` | `[{date: 2026-11-12, domain: "003 wage_type_mappings", from_value: "(no CHILD_SICK_CALL_HOME mapping)", to_value: "CHILD_SICK_CALL_HOME → SLS_0530 (provisional)", source_url: "pending — the code is StatsTid's provisional choice (S148 refinement ruling R-16); the call-home day itself: the AC settlement item 4 (in force 1 Apr 2026), circular pending (QUAL-236, QUAL-BF-004)", commit: "26065e1a (S148 TASK-14811 stack merge)"}]` |
````

### D-16 · docs/references/phase-b-handoff-package.md · RR-6's five silent rest questions (a)–(e)

Why: "The Orchestrator adds (a)–(e) to `docs/references/phase-b-handoff-package.md`"
(`.claude/reviews/S148-RESEARCH-rest-24h-adjudication.md:90`; plan "Records owed", `PLAN-s148.md:855`). The questions'
wording is the verification's (`.claude/reviews/S148-RESEARCH-rest-24h-verification.md:245-251`), the dispositions
and blocked rows the adjudication's (`…-adjudication.md:80-87`), the CIR 9807 detail the verification's R5 verdict
(`…-verification.md:243`). The package is untouched since S36 (`a049f7cb`); the section goes after the last
candidate bug, before "Engagement Protocol".

````old
**Severity**: MEDIUM-HIGH — workflow gate affecting all HK + PROSA overtime registration going forward.
````

````new
**Severity**: MEDIUM-HIGH — workflow gate affecting all HK + PROSA overtime registration going forward.

---

## S148 additions — rest-rule questions the sources leave silent (2026-11-25)

*Added at S148's close from the S148 rest-rule research and its adjudication (ruling RR-6). The expert review has not started (the dashboard below is unchanged since S36), so these wait with the rest of the package. Unlike the candidate bugs above, they are not about a seeded value: they are points where Danish law and the state agreement say nothing, so StatsTid must either take a stated default or leave the rule unmodelled.*

**Background, in plain words.** Since S148 StatsTid checks the 11-hour daily rest the way Arbejdstilsynet applies the law: at the end of every piece of work, the 24 hours before it must hold 11 unbroken hours of rest (`docs/references/rest-period-24h-research.md`; ADR-044; `danish-agreements.md`, "Rest Periods"). A sixth silent point, what to assume before the first record in the data, was given a default for that build: time with no registered work is rest. These five need an expert:

| # | Question for the expert | Why StatsTid needs it | What waits on it |
|---|-------------------------|-----------------------|------------------|
| (a) | The 7-day ("7 døgn") period of the rest rules: where does it start, and does it roll? | Counting how often a rest may be cut, and the weekly rest day | QUAL-254, QUAL-256 |
| (b) | What does "døgn" mean in the state agreement's "ikke 2 døgn i træk" (a cut to 8 hours may not be used two days in a row), and in VEJ 9638's "det normale arbejdsdøgn"? | The "not two days in a row" check | QUAL-254 |
| (c) | Is CIR 9807/1983 (central administration, deadline tasks: cut or postpone the rest, up to 22 hours of work in a row) still a valid derogation under BEK 606, and how does its "arbejdsdøgn" map onto the rolling 24-hour test? | Whether StatsTid must model postponement at all; today it does not | Nothing is built on it |
| (d) | Are the compensatory hours after a cut added on top of the next 11-hour rest, or part of it? | The compensatory-rest check; if built before an answer, StatsTid takes "on top", the stricter reading | QUAL-254 |
| (e) | How does a night shift count toward the state agreement's § 12 stk. 3 "arbejdsdage" (at most 10 working days between two days off)? | The weekly rest-day check | QUAL-256 |
````

### D-17 · ROADMAP.md · § 1's "where we are" (`:28`; O-22)

````old
Loosely sequenced buckets between where we are (S148) and a system we would consider production-ready.
````

````new
Loosely sequenced buckets between where we are (S148 closed, S149 next) and a system we would consider production-ready.
````

### D-18 · ROADMAP.md · the overtime/flex program's status line (`:45`; O-22)

````old
   **The overtime/flex model program: ADR-043 ACCEPTED 2026-10-29 (S147); S148 opens on its stage map.**
````

````new
   **The overtime/flex model program: ADR-043 ACCEPTED 2026-10-29 (S147); S148 built stage 0 and the config stage; S149 builds stage 1.**
````

### D-19 · ROADMAP.md · § 1: what S148 built, and S149 next with the items S148 passes on (`:55-69`; O-22)

Why: O-22 owes "ROADMAP § 1 (S149 next, with the passed-on items)" (`PLAN-s148.md:804`); the audit confirms it is
not written (`.claude/reviews/S148-C1-audit-report.md:61`). The passed-on items are the refinement's table,
`REF:630-642` (`.claude/refinements/REFINEMENT-s148-stage0-and-config.md`): S149 gets QUAL-225's opening flex
balance (`:636`), AC's annual-norm variants (`:640`), HRP-024 (`:641`) and the C-10 and C-11 writers joining the
lock and the worklist (`:642`); stage 2 gets D8's storage (`:637`), R-C (`:638`) and saved days (`:639`). Added from
the log: the re-pin at seeds 47 and 45 and the demo's customary days (`SPRINT-148.md:4644-4646`), and the eighteen
settings' first reader (already a § 2 line, `ROADMAP.md:302`). The six tidy inputs were all in scope
(`REF:200`, `:227-232`) and every task carrying them is DONE (the task ledger). QUAL-233 and QUAL-237 are closed
(`quality-finding-register.md`, their status cells). The stage list and the scope ruling are kept as written.

````old
S148's refinement starts from the build input
   (`.claude/plans/s147/S148-BUILD-INPUT.md`) and decides how many stages S148 carries; a stage it cannot carry is
   pointed to here. **Owner scope ruling 2026-11-09: S148 carries stage 0 and the config stage** (no demo payslip changes, so
   the sprint's acceptance pin is identical demo pay lines; the one arithmetic change, QUAL-236's new credited
   call-home day, has no demo row); stage 1, where every HK and PROSA payslip moves, gets its own sprint (S149), then
   stage 2 and stage 3. Closes QUAL-189, QUAL-216..222, QUAL-226, QUAL-227, QUAL-232..234 and QUAL-237 when built;
   retires S145's weekly settlement machinery (frozen by OQ-2, with QUAL-209/210/212/213).
   The source register's unreliable citations (QUAL-215) make the Phase-B engagement above more urgent.
   **S147's tidy pass handed S148 stage 0 six small inputs** (recorded at S147's C-1; detail in
   `docs/sprints/SPRINT-147.md`, "C-1 — the close candidate's records"): two optional Docker pins, a særlige
   feriedage config ending mid-month and the April 2026 OK-transition month; a repository pin for the four-eyes SQL
   predicate's legacy `submitted_by` half; payroll's four-eyes refusal getting a kind of its own (QUAL-246); which
   compliance items are announced to screen readers, and a neutral style for the "not checked" notice, both with
   D10's rest-check half, which replaces the notice; and the HRP-013 orphan roll-up counting a manager in the 5-day
   grace as covering. S148's refinement places each.
````

````new
S148's refinement started from the build input
   (`.claude/plans/s147/S148-BUILD-INPUT.md`). **Owner scope ruling 2026-11-09: S148 carries stage 0 and the config stage** (no demo payslip changes, so
   the sprint's acceptance pin is identical demo pay lines; the one arithmetic change, QUAL-236's new credited
   call-home day, has no demo row); stage 1, where every HK and PROSA payslip moves, gets its own sprint (S149), then
   stage 2 and stage 3.
   **S148 built stage 0 and the config stage, and no demo payslip moved** (PIN-1; closed ‹close date›, detail in
   `docs/sprints/SPRINT-148.md`). In plain words: on-call, call-ins and travel can no longer be registered, and payroll
   refuses a stored one rather than paying it as ordinary work (QUAL-202; QUAL-232's interim); the child's sick days
   follow the agreements (QUAL-236); a day's paid absence is worth at most its planned hours minus the hours worked
   (QUAL-237; ADR-043 D2); the 11-hour rest check reads Skema's clock times and applies the law's 24-hour look-back
   (QUAL-253; ADR-044); a Skema cell can be emptied (QUAL-248); and payroll computes only with the published, dated
   agreement configuration, position override and local profile in force on each day, frozen into the month's proof
   (QUAL-233, QUAL-204). S147's six tidy inputs were built in stage 0. The employee's organisation is dated (owner
   Q-1; QUAL-206), and the editors gained an institution's customary days, the local profile's eighteen new
   settings and two HR facts per person, recorded now and read by pay from S149 or stage 2.
   **Next: S149, stage 1** — the monthly norm and flex by default. Every HK and PROSA payslip moves, so S149 re-pins
   PIN-1 at the recorded seeds (47 and 45). **Passed on to S149's refinement** (the S148 refinement's "Items ADR-043
   assigned to this refinement, passed on", ruling R-11; ADR-043's S148 notes): QUAL-225's HR-entered opening flex
   balance; how AC's annual-norm variants settle flex; whether HRP-024 ("approve the previous month first") retires
   or is re-stated; the customary-day and HR-facts writers joining the pay-inputs lock and the correction worklist
   with their first reader (S1-6, S1-9); the local profile's eighteen settings frozen with their first reader (§ 2,
   Correctness / domain); and whether the demo's generated hours honour customary days, decided before the re-pin
   (the demo worlds now hold customary days in their measured months). **Passed on to stage 2's refinement:** where
   the overtime movements are stored (D8); a leaver's owed time off and saved days (R-C); saved days, after
   research cell R-A. Still to close when built: QUAL-189, QUAL-216..222, QUAL-226, QUAL-227, QUAL-232 and QUAL-234
   (QUAL-233 and QUAL-237 closed in S148); retires S145's weekly settlement machinery (frozen by OQ-2, with
   QUAL-209/210/212/213).
   The source register's unreliable citations (QUAL-215) make the Phase-B engagement above more urgent.
````

### D-20 · ROADMAP.md · the absence-day rules' clearing line made true (`:472-473`; addendum O-26)

Why: O-26 asks that "the absence-day-rules entry's clearing lines [are] made true"
(`ADDENDUM-A1-cell-clearing.md:445`; its cites `:417-418`, `:428` have drifted to `:472-473`, `:482-483`). After
D-22 the "(Usability, below)" pointer would point at nothing. The other clearing line, (d)'s "Users fix them by
clearing a cell" (`:482-483`), is true as written since S148 and is left.

````old
  - It needs **Clearing a Skema cell** (Usability, below): rule 2's warning asks the user to remove the first
    absence, which cannot be done today.
````

````new
  - It needed **Clearing a Skema cell**, which shipped in S148 (TASK-14840/14841, QUAL-248; the row left the
    Usability list at S148's close): rule 2's warning asks the user to remove the first absence, which is now possible.
````

### D-21 · ROADMAP.md · the absence-day rules' S148 note, with the grid's copy of the bound (log `:765`, `:826`)

Why: the log owes "the grid mirror of the bound (roadmap)" (`SPRINT-148.md:765`) and records it as "the roadmap row
already owed" (`:826`). The absence-day rules entry is that row (`ROADMAP.md:474-475`, "the grid copy of the rules").
Verified at HEAD: the grid's advisory check sums absence cells only and excludes work
(`frontend/src/components/SkemaGrid.tsx:338-366`); the server's bound is QUAL-237's fix (its status cell). The entry's
owner rulings are kept verbatim; the note is added under it.

````old
  (gitignored, so this entry carries the rulings)]
````

````new
  (gitignored, so this entry carries the rulings)]
  **S148 note (2026-11-25).** Two prerequisites shipped in S148. **QUAL-237 is fixed** (TASK-14804): the Skema save
  judges the day as it will stand, inside the per-employee lock, and a day's credit-class paid absence may not exceed
  its planned hours minus the hours worked (ADR-043 D2), which is rule 3's server half for paid absence.
  **Clearing a Skema cell shipped** (TASK-14840/14841, QUAL-248). Still open: rules 1, 2 and 4, and the grid's own
  copy of the bound. The grid's advisory check counts a day's absence hours only, never its work
  (`frontend/src/components/SkemaGrid.tsx:338-366`), so it gives no warning before the server's bound refuses a save;
  the employee learns from the refusal's message, which since S148 says exactly why (TASK-14810;
  `docs/sprints/SPRINT-148.md:763`, `:826`).
````

### D-22 · ROADMAP.md · the "Clearing a Skema cell" row leaves (`:651-664`; addendum O-26)

Why: the row says of itself "this row leaves at S148's close, once its two riders below are confirmed placed"
(`ROADMAP.md:652`), and § 2's rule is "delete the row when done" (`ROADMAP.md:103`). **Both riders are placed:**
(1) the "away today" filter is pinned by `TeamOverviewAggregateTests.AwayToday_False_AfterTodaysAbsenceCleared`
(`tests/StatsTid.Tests.Regression/Approval/TeamOverviewAggregateTests.cs:704`; AC-CL-24, green at CI-W2,
`SPRINT-148.md:1437`; proved by M-19 at CI-M2, `:1461`); (2) QUAL-196 is FIXED (TASK-14842;
`quality-finding-register.md:453`). The row's stale cites (`useSkema.ts:203`, `ApprovalEndpoints.cs:1003`) leave with
it. The closure is recorded in the sprint log (D-31), so the row's history is not lost. `old` ends with the next
row's first line so no blank line is left behind.

````old
- **Clearing a Skema cell (S72 R17 — still open; kept out of S145 by the Fable seat's ruling R24, owner informed
  2026-10-01).** **→ Promoted into S148 (TASK-14840/14841, addendum A1); this row leaves at S148's close, once its two riders below are confirmed placed.** Today an emptied cell is never sent (`useSkema.ts:203`), so a booked hour or absence cannot be
  taken back from the timesheet. S145 makes a re-save replace the cell (QUAL-192..194) and refuses a save of
  0 hours with a 400, so a cell can be corrected but not emptied. The right design is a *retraction*: mark the
  current row superseded and insert nothing. A 0-hour booking would be wrong, for three reasons. The strand
  check would count it (`EmploymentWindowStrandCheck.cs:64-70`). `GET /api/time-entries` would return it. And
  the norm rule would emit a 0-hour `NORMAL_HOURS` line (`NormCheckRule.cs:179-186`). Full-day-only absence
  types also need their clear rule stated. S145's storage design leaves room for this. A small UX + backend
  task; source: the S145 refinement, ruling R24.
  **Now also needed by the absence day rules** (Correctness / domain, owner 2026-10-28): their warning
  asks the user to remove the first absence, which this item makes possible.
  When it is designed, two items from S145's Step 5a review of TASK-14507 come with it:
  - the team overview's "away today" filter (`ApprovalEndpoints.cs:1003`) gets its pin, since a retraction is the first change that can make it matter;
  - QUAL-196 is revisited: a missing balance row is seeded at `used = 0`.
- **Accessibility (WCAG)** — rises from "polish" to a genuine requirement as the target firms toward
````

````new
- **Accessibility (WCAG)** — rises from "polish" to a genuine requirement as the target firms toward
````

### D-23 · ROADMAP.md · the two child's-sick lines owed at close (log `:989`)

Why: "Two roadmap lines owed at close: the stored-day message wording (Low, rare, safe) and the call-home entry order
(an e2e check)" (`SPRINT-148.md:989`), from Fable's cycle-2 NOTEs on TASK-14811
(`.claude/reviews/S148-TASK-14811-5a-reviewer-c2.md:64-71`, `:82-90`; the second marked `(unproven)` there, "Concrete
case: Not shown"). Cites re-opened at HEAD: the two messages `SkemaEndpoints.cs:2374`, `:2384`; Rule B's kind `:2405`.
Neither line is in `ROADMAP.md` today (`grep -n -i "stored-day\|call-home" ROADMAP.md` finds only `:59`).

````old
  (`frontend/src/pages/admin/opfoelgning/FollowUpLists.tsx:191`). A small UX task.
````

````new
  (`frontend/src/pages/admin/opfoelgning/FollowUpLists.tsx:191`). A small UX task.
- **Child's sick days: two small follow-ups from S148** (TASK-14811 Step 5a, Fable cycle-2 NOTEs; owed at S148's close).
  (1) **The refusal names the wrong day (Low; rare and safe).** When a child's first sick day is posted on the day
  before one already stored, the refusal names the stored day as the one that cannot be registered: saving Monday
  8 June next to a stored Tuesday 9 June reads "Barns 1. sygedag den 09-06-2026 kan ikke registreres: dagen før
  (08-06-2026) er registreret som barns sygdom …" (`SkemaEndpoints.cs:2374`; the call-home twin at `:2384`). A
  wording pass names the posted date when the conflict comes from a row posted before a stored one.
  (2) **The entry order for a call-home day (`(unproven)`; an e2e check).** The call-home day is refused unless the
  day's work is stored or posted in the same save (`kind` `child-sick-call-home-without-work`,
  `SkemaEndpoints.cs:2405`). If the grid ever saves the absence cell before the day's work periods, the user gets
  that refusal and must enter the work first. An e2e fact settles which order the grid sends.
````

### D-24 · docs/QUALITY.md · the freshness marker

Why: `tools/check_docs.py:148-181` reads the first `anchor-sprint` marker; for `docs/QUALITY.md` only, trailing the
latest sprint log by more than 3 (`ANCHOR_SLACK`, `:45`) is a hard failure (`:176-179`, QUAL-141). The latest log is
S148, so 147 would still pass; the marker moves because the file now carries the S148 snapshot (D-25).

````old
<!-- anchor-sprint: 147 -->
````

````new
<!-- anchor-sprint: 148 -->
````

### D-25 · docs/QUALITY.md · the S148 re-grade (O-22; addendum O-30)

Why: O-22 (`PLAN-s148.md:804`) and "Records owed" (`PLAN-s148.md:840`: "Payroll Integration; Domain Correctness;
Security; Concurrency, saying what moved and why"); addendum O-30 asks that it "names the Skema change and the
balance seed" (`ADDENDUM-A1-cell-clearing.md:449`). The file is a stack of dated snapshots, newest first, never
rewritten (`docs/QUALITY.md:6-8`), so this is a new section above S147's. **Every grade is held**; no grade moves
without a named cap closing, and each domain still has a named cap open (each row says which). "Concurrency" has
never been graded in this file, so it is given observations, not a grade (see "Needs the Orchestrator's choice",
item 4). Evidence: the registers' status cells at `ec789343` (closures: QUAL-042, -182, -184, -190, -196, -202, -204,
-206, -233, -236, -237, -240, -246, -248, -253, -257, -260, -263, -265, -267, -268; new rows QUAL-248 … QUAL-282,
QUAL-BF-001 … 011, SEC-059 … SEC-070, F9 … F11); the CI judgements in the sprint log (CI-W0 `:344`, CI-W0b `:354`,
CI-W1 `:727`, CI-W1b `:783`, CI-W2 `:1427`, CI-M2/M2b `:1447-1465`, CI-W3 `:1670`, CI-W3b `:1718`, CI-W4a `:1793`,
`:1798`, CI-W4 `:1993`, CI-M4a/b `:2150-2157`, CI-W5a `:2547`, CI-W5 `:3558`, CI-M5 `:3659`, CI-W6 `:4564`); the C2-R4
census (`:4765-4782`); the C-1 audit (`.claude/reviews/S148-C1-audit-report.md:3`).

````old
## S147 re-grade (2026-11-04, at C-1) — confirmed defects closed; the overtime/flex model decided
````

````new
## S148 re-grade (2026-11-25, at C-1) — stage 0 and the config stage of the overtime/flex model; every demo payslip unchanged

**The sprint that stopped paying what was wrong today, and made the configuration editors' values the ones payroll computes with, without moving a single demo payslip.**
- **What changed:** 21 register rows closed, five of them High. Payroll and the Rule Engine compute only with the published configuration in force on each day (central version, position override and local profile, each dated), frozen into the month's proof, with no compiled fallback on the pay path (QUAL-233, QUAL-204, QUAL-184, QUAL-042, QUAL-182). The child's sick days follow the agreements (QUAL-236). The 11-hour rest check reads the clock times typed in Skema and applies the law's 24-hour look-back (QUAL-253; QUAL-234's rest arm). A day's paid absence is worth at most its planned hours minus the hours worked (QUAL-237, QUAL-240). Stored typed hours are refused by payroll instead of being paid as ordinary work (QUAL-202; QUAL-232's interim). In Skema a cell can now be emptied (QUAL-248), and a missing holiday balance record is created from the real bookings with the year's quota (QUAL-196). The employee's organisation is dated (QUAL-206). Payroll no longer pays the days after a profile ended on that profile (QUAL-265); a restart no longer invents a full-time profile, and HR can give a closed profile a successor (QUAL-267, QUAL-268); two readers of the dated record agree (QUAL-263); plus QUAL-190, QUAL-246, QUAL-257 and QUAL-260. Security: SEC-059, SEC-061 and SEC-062 fixed, and SEC-065 for pay inputs, eligibility and birth date. Performance: F10 done (the year overview 728 ms → 386 ms in CI).
- **What was found and registered:** QUAL-248 … QUAL-282 (35 rows, 8 of them also fixed inside the sprint; QUAL-256 High), QUAL-BF-001 … QUAL-BF-011 (the sprint's accepted shortcuts, each with its code comment), SEC-059 … SEC-070 (12 rows; of those still open, SEC-060 and SEC-070 are Medium, the rest Low), F9 and F11.
- **The acceptance pin held:** PIN-1 compares every demo pay line of two seeded worlds, one per OK version; no golden moved at any checkpoint through CI-W6 (run `45582399602`). Its final run, on the reseeded demo worlds, is C-2 (‹C-2 run id›).

| Domain | Grade | Evidence / what changed |
|--------|-------|-------------------------|
| **Payroll Integration** | A− → **A−** (held, ▲) | The pay path computes only with the published configuration in force on each day and freezes it into the month's proof; an unreadable or missing configuration refuses the month (503 or 422) instead of falling back to compiled values (QUAL-233, QUAL-204, QUAL-184, QUAL-042, QUAL-182; ADR-014 on the pay path). A configuration published with a past date puts the paid months it reaches on HR's correction worklist (owner Q-2). Payroll refuses a month holding a stored typed entry (QUAL-202), names its four-eyes refusal (QUAL-246), and no longer pays the days after a profile ended on that profile (QUAL-265, High). PIN-1: no demo payslip moved. **Held below A** by the model itself, still weekly and automatic until S149 and stage 2 (QUAL-189, QUAL-216..222, QUAL-232, QUAL-234's supplement arm); by QUAL-211, QUAL-243 and QUAL-244; by three legacy paths still computing with compiled rules (QUAL-262); a replay that can show a different wage-type code without refusing (QUAL-259; owner ruling: to be closed after S148); the provisional call-home wage type (QUAL-249); and a rebuild that could restore a typed row after the check (QUAL-273, accepted) |
| **Domain Correctness** | B+ → **B+** (held, ▲) | Three of S147's named hold-backs are closed: the child's-sick-day rules (QUAL-236, High: 2 days per illness under OK24, 3 plus the call-home day under OK26, no yearly cap) and the paid-absence day bound (QUAL-237, QUAL-240). A fourth, QUAL-234, has its rest arm built: the rest check now applies the authority's test to Skema's clock times (QUAL-253, High; verified research; owner Q2). On-call, call-ins and travel can no longer be registered and are refused at payroll (QUAL-232's interim). The engine's marker moved once, to `weekly-timeline-v3`, for the call-home credit. **Held** because the overtime model is still weekly and automatic (stage 1 is S149) and by new findings: the weekly rest-day check is not the legal test (QUAL-256, High); a permitted cut and the per-person exemption are not modelled (QUAL-254, QUAL-255); the other parent's days (QUAL-250); QUAL-234's supplement arm (re-rated Critical → High at C-1) |
| **Security & Access Control** | A− → **A−** (held, ▲) | Fixed in the sprint: a manager no longer sees neighbouring-month clock data through the rest check (SEC-059); the local-profile save validates its values (SEC-061); person-record writes compare the organisation they authorised with the one the lock found (SEC-062: the user edit, six heads and the remaining 22 sites); nobody changes their own pay inputs, eligibility or birth date, GlobalAdmin included (SEC-065, owner P-26 and 2026-11-18). Each fix has CI-verified facts, and SEC-062's pins were also proved red under mutation in CI (CI-M4a). **Held:** new rows SEC-060 (no second signer on pay-affecting configuration, Medium; owner: compliance epic E11), SEC-063, SEC-064, SEC-066, SEC-067, SEC-068 and SEC-069 (Low), and SEC-070 (Medium, pre-existing since Sprints 7–9: a local administrator can rename or deactivate another organisation's project; its fix timing is with the owner); SEC-065's bare self-reversal stays open |
| **Auditability** | A → **A** (held, ▲) | Every new pay input is dated and audited and emits an event with a catalog row: the organisation history, the employment start date, customary days, HR facts, and the three clear events. A cleared Skema day is kept and marked, never deleted, so the startup rebuild replays clears. The month's proof freezes the dated configuration, and a result recorded under the old marker is refused at replay by name. A correction and a payroll recording of the same month take one lock (the pay-inputs lock), so neither can miss the other, which closes S138's timing gap. **Held at A, not raised:** the compensation-choice save writes no event or audit row (QUAL-272); rebuilding the Skema tables from empty loses facts not yet published (QUAL-274); a replay can show a different wage-type code (QUAL-259) |
| **Backend API** | A− → **A−** (held) | The legacy time-entry POST is retired; a cell clear is part of the Skema save (QUAL-248); the overtime pre-approval decision cannot land twice (QUAL-257); the local-profile save checks its values and its "no change" check compares all twenty-two settable columns; HR has a route to give a closed profile a successor (QUAL-268); new routes for customary days and HR facts, each dated, scoped and audited. **Held:** a version that restarts at 1 lets a stale `If-Match` through (QUAL-261, QUAL-282); QUAL-264, QUAL-266, QUAL-269, QUAL-270, QUAL-271, QUAL-280 and QUAL-281 |
| **Frontend** | A → **A** (held) | An emptied cell is saved as a removal, and of one day's saves the newest confirmed one wins (QUAL-248; TASK-14841); the compliance warnings are one polite live region and the narrowed "not checked" notice is informational, not a breach; the screen runs the engine's own algorithm for joining shifts across midnight, proved by one shared parity table; new editors for the local profile's eighteen settings, an institution's customary days and two HR facts per person; the edit drawer's transfer date and HR's repair of a closed profile; notices that say which settings do not yet reach pay. **Held:** QUAL-252 (save ordering holds only within 15 s), QUAL-278 (Skema shows nothing when the compliance call fails), QUAL-279 (the position-override page can overwrite another administrator's change), and the below-floor accessibility items in the sprint log (Teamoversigt's per-line live regions, a publish dialog without a focus trap) |
| **Test Suite (cross-cutting)** | A → **A** (held) | PIN-1, a golden of every demo pay line from two seeded worlds, stayed byte-identical through every checkpoint. 18 CI evidence runs (17 checkpoints; CI-W4a ran twice), five of them mutation runs (CI-M2, CI-M2b, CI-M4a, CI-M4b, CI-M5): every mutation turned red the facts on its frozen list, and CI-M5's one unlisted red was attributed. Counts, S147 → S148: Unit 1722 → 1848, DemoSeed 170 → 180, Regression 2436 → 2969 at CI-W6 (C-2: ‹C-2 Regression n›), smoke 7 → 10, vitest 1089 → 1405. CI-W4a's first attempt went red before any test ran: an `init.sql` constraint-name clash that both review lenses had passed by reading and only PostgreSQL running the script shows; the GATE now applies `init.sql` to a throwaway local cluster. **Held, not raised:** the Regression step's CI time grew from about 75 minutes to about 2 h 15 min (F11) |
| **Infrastructure** | B+ → **B+** (held) | The dated organisation (`user_organisations` beside the live column), dated override versions, one "in force" definition shared by every configuration read and the worklist's reach, and the pay-inputs lock taken first by every pay-input writer; reuse of a resolved configuration across unchanged days restored (F10); the two readers of the dated record made to agree (QUAL-263); the restart seeder fixed (QUAL-267). **Held:** a long configuration publish holds the pay-inputs lock while it lists its worklist rows, and writers behind it fail after 30 s (F9); the lock couples organisations (SEC-066); QUAL-195 and QUAL-197 stay open |
| **Rule Engine** | B → **B** (held, ▲) | The engine computes only with the configuration each request carries (QUAL-233); the daily rest check is rewritten to the authority's test, with a pure Copenhagen elapsed-time helper and the 8-hour floor for a permitted cut (QUAL-253); applying a position override keeps every base member (QUAL-190). **Held at B:** the blocker named at S133 is still open (QUAL-123, the 48-hour average ignores its reference period; confirmed by grep at C-1), with QUAL-256 (High), QUAL-254, QUAL-189 and QUAL-191 |
| **Concurrency (cross-cutting)** | — | Observations, not a grade (no earlier snapshot grades it). The pay-inputs lock serializes every pay-input writer with payroll's recording. Two check-then-act races were fixed (QUAL-257, a double overtime decision; QUAL-260, a replaced predecessor). The C-1 census of write routes (C2-R4) classified about 80 routes in 38 rows: 33 protected (a guarded update, a row lock, a version check or the pay-inputs lock), two registered as vulnerable (QUAL-280, QUAL-281), and a version that restarts at 1 after a supersession reaching the wage-type mapping (QUAL-282, decided with QUAL-261). Also open: QUAL-252 (Skema's save ordering), QUAL-271 (latent), F9 and SEC-066 |
| **Documentation (canon)** | materially recovered → **materially recovered** (held) | The verified rest-rule research dossier and ADR-044; the close's ADR notes; the registers carry every closure and the new rows; two comment-only sweeps made the code's comments true (48 blocks in 28 files, then 8 more remarks; PAT-032). **Held:** most of the paperwork owed at the merges was written at C-1 rather than at the merges; the C-1 audit found about 20 of the plan's 35 O-items fully done before C-1 |
| **Governance / review effectiveness** | — | Observations, not conclusions. The internal lens ran on Fable 5.2 for the whole build (the floor moved on 2026-11-10); the refinement's cycles 2–5 had run on a labelled Opus stand-in. The Ponytail-ideas trial's first measured sprint: every shortcut left in the code is registered: the shortcut check reports 23 found, none bad. The owner allowed one review round past the cap of five (Q6, TASK-14841) and made the design-sync drift gate advisory (2026-11-24). CI caught what both lenses passed: CI-W4a's `init.sql` clash and, at CI-W1, four unregistered shortcut comments. Step 7a: ‹Step 7a verdicts› |

*(Domains not listed — SharedKernel, PostgreSQL Schema, Docker, CI/Tooling — are unchanged in grade by S148. SharedKernel gained the call-home code's classification and lost the retired flex carryover maximum. The schema gained seven tables (the organisation history, customary days and HR facts, each with its audit table, and the override version history), eighteen nullable settings on the local profile, an applies-from date on agreement configs and overrides, and a cleared marker on the work-time projection, and lost five retired columns (`flex_carryover_max` from four tables, `manual_hours`); CI-W4a found its one defect. CI/Tooling: the shortcut check ran its first sprint, and the design-sync gate became advisory.)*

**PM reading.** S148 is the sprint in which the configuration screens stopped being decoration: until now payroll ignored most of what an administrator set and computed with values compiled into the code. It also stopped paying a few things wrongly (typed time, child's sick days, over-credited absence days) and made the rest check apply the law's actual test. The grades hold rather than rise, on purpose: the defects that capped them are mostly the overtime model itself, which is still weekly and automatic until S149 and stage 2, and the sprint's own careful reviews found new ones (35 quality rows, 12 security rows) that the next sprints own. The figure to watch is PIN-1: thirty-odd tasks changed the pay path, and no demo payslip moved.

## S147 re-grade (2026-11-04, at C-1) — confirmed defects closed; the overtime/flex model decided
````

### D-26 · docs/sprints/INDEX.md · the S148 row (in the S147 row's style)

Why: `docs/sprints/INDEX.md` has no S148 row (audit § 4, `S148-C1-audit-report.md:198`). The S147 close changed one
thing in INDEX: it added the S147 row after the S146 row (`c13024cd`, then text-only edits in `bf3235b1` and
`e553777e`; `git show` of each shows one hunk), so this adds S148's after S147's (`INDEX.md:116`). Test figures:
at K, Unit 1848, DemoSeed 180, Regression non-Docker 148 (`SPRINT-148.md:4655`, `:4672`, `:4759`); CI-W6 Regression
2969, smoke 10, vitest 122 files / 1405 (`:4566-4567`). Run ids are the log's (cites in D-25's "Why").

````old
 `41708962373`, CI-W4b `41825042200`, all green; C-2 on K′ `42013444380` red 1/2430 (a test-fixture fault, fixed by TASK-14725-T fix-up 4) |
````

````new
 `41708962373`, CI-W4b `41825042200`, all green; C-2 on K′ `42013444380` red 1/2430 (a test-fixture fault, fixed by TASK-14725-T fix-up 4) |
| [Sprint 148](SPRINT-148.md) | **The overtime/flex model's first two stages: stage 0 stops paying what is wrong today, and the config stage makes the editors' dated values the ones payroll computes with (ADR-043; QUAL-233, QUAL-204, QUAL-236, QUAL-237, QUAL-253, QUAL-248), with every demo payslip unchanged.** What shipped:<br>• On-call, call-ins and travel can no longer be registered; the old time-entry route and its unrouted page are gone, and payroll refuses a stored typed entry instead of paying it as ordinary work (QUAL-202).<br>• Child's sick days follow the agreements: 2 days per illness under OK24, 3 plus the call-home day under OK26, with no yearly cap (QUAL-236).<br>• A day's paid absence is worth at most its planned hours minus the hours worked (QUAL-237, QUAL-240).<br>• The 11-hour rest check reads the clock times typed in Skema and applies the law's test: the 24 hours before every work end must hold 11 unbroken hours (QUAL-253; owner Q2, after verified research). The "not checked" notice narrows to days not on the clock.<br>• A Skema cell can be emptied (QUAL-248, which closes S72's R17), and a missing holiday balance is created from the real bookings with the year's quota (QUAL-196).<br>• Payroll computes with the published configuration in force on each day (central version, position override and local profile, each dated), frozen into the month's proof, and refuses rather than falling back to compiled values (QUAL-233, QUAL-204). A configuration published with a past date puts the paid months it reaches on HR's correction worklist (owner Q-2).<br>• The employee's organisation has a dated history (owner Q-1; QUAL-206), and every writer of a pay input takes one lock first, so a correction and a payroll recording of the same month cannot miss each other.<br>• New editors: the local profile's eighteen new settings, an institution's customary days and two HR facts per person, all dated and audited, read by pay from S149 or stage 2. HR can give a person whose profile was closed a new one (QUAL-268).<br>Fixed on the way: payroll paid the days after a profile ended on the ended profile (QUAL-265); a restart invented a full-time profile (QUAL-267); two leaders could decide one overtime request at once (QUAL-257); SEC-059, SEC-061, SEC-062, and SEC-065 for pay inputs, eligibility and birth date. Registered: QUAL-248 … QUAL-282 (eight also fixed in the sprint), QUAL-BF-001 … 011, SEC-059 … SEC-070, F9, F11. PIN-1 held at every checkpoint. Step 7a: ‹Step 7a verdicts› | complete | 2026-11-09 → ‹close date› | 1848u + 180 demoseed + 148r non-Docker (the local GATE at K) · CI-W6: **2969r** (all executed) + 10s + **1405fe** · C-2: ‹C-2 Regression n›r | ✅ CI GREEN ‹C-2 run id› (C-2, all 7 jobs, on K ‹K sha›; the close commit ‹close sha› after it is docs only). Earlier checkpoints: CI-W0 `42805305917` red only on PIN-1's two golden-capture facts, as designed; CI-W0b `42816549335` green; CI-W1 `43011040973` tests green, docs red on four unregistered shortcut comments (fixed `76c6da46`), CI-W1b `43027618243` green; CI-W2 `43233683529`, CI-W3 `43412751883`, CI-W3b `43851473962` green; CI-W4a `43943858508` red before any test (an `init.sql` constraint-name clash), re-run `43946317628` green; CI-W4 `44352425811` green; CI-W5a `44728481380` green on the evidence (docs red on one PAT-032 item, fixed `14a73d6f`); CI-W5 `45280970629` green on every test job (docs red on the design-sync gate, advisory by owner ruling 2026-11-24); CI-W6 `45582399602` green. Mutation runs: CI-M2 `43238030414`, CI-M2b `43238051149`, CI-M4a `44481246221` and CI-M4b `44481256823` each red on exactly its frozen list; CI-M5 `45362153082` killed every mutation (one unlisted red, attributed) |
````

### D-27 · docs/sprints/SPRINT-148.md · the Status row (`:5`)

Why: it still reads "OPEN — refinement READY … wave 1 briefed and adjudicated, dispatch next" (audit § 4,
`S148-C1-audit-report.md:198`). Written in S147's closed-status style (`SPRINT-147.md:5`), with placeholders filled at
C-4 and C-5. **Nothing in the old row is lost:** its floor-move narrative moves verbatim to the Orchestrator model row
(D-29) and its Step 0b cycle-1 Codex narrative to the Plan row (D-28). Closures checked against the registers'
status cells at `ec789343` (D-25's list); "53 task ids, all DONE" against the Task ledger (`SPRINT-148.md:34-89`);
R17 per addendum O-28 (`ADDENDUM-A1-cell-clearing.md:447`).

````old
| **Status** | **OPEN — refinement READY (rev 5.1); plan `.claude/plans/PLAN-s148.md` FINAL (draft 1.5.1), Step 0b READY on both lenses 2026-11-10 (five cycles; see "Plan Review (Step 0b)"); wave 0 merged and confirmed (CI-W0b green); wave 1 briefed and adjudicated, dispatch next** (opened 2026-11-09; owner "Go" to the plan, 2026-11-09). **The review floor moved to Fable 5.2 on 2026-11-10** (the owner reset the Fable limit and named the new model, 2026-11-09). After the owner's restart the session runs client 2.1.322; the floor id changed in all its places (reviewer and adjudicator self-checks, the close guard's `$reviewFloor`, its harness fixtures, WORKFLOW's reference line and gate sentence; harness 37/37). The bump procedure listed four places and missed the adjudicator's self-check; it now lists five. Three probes (reviewer, adjudicator, reviewer again) ran on `claude-fable-5-2`, but all three refused: they still read the 5.1 self-check, although the file on disk was changed. Agent definitions were not re-read inside the running session, so the owner restarted a second time. After it, the reviewer and adjudicator probes both read the 5.2 floor. The bump is committed as **`636bc7da`**, with WORKFLOW's "Last verified" record and the procedure now ending in a restart and the probes. **Step 0b cycle 1:** Codex ran detached from 09:11 to 09:16 on draft 1.2 and returned **CHANGES-REQUIRED, 0 BLOCKER / 2 WARNING** (`.claude/reviews/PLAN-s148-codex-c1.raw.md`). Both warnings were confirmed against the code. **W-1:** NR-P2's "with no exported month, no call" would also skip the settled-holiday-year hand-off, which is selected independently of exports (`HrBackdateWorklistRepository.cs:1027`). **W-2:** TASK-14830 retires `ConfigFingerprint` but is not authorized for its remaining engine callers (`RuleRegistry.cs:467`, engine `Program.cs:105`). The Fable 5.2 lens gave the first Fable read to the refinement material that cycles 2–5 had reviewed only on the Opus stand-in, and found the cycle-1 BLOCKER there (REF-1) |
````

````new
| **Status** | **CLOSED ‹close date› at C-4** (opened 2026-11-09, owner "Go" to the plan the same day; close candidate K committed at C-1, 2026-11-25; the final candidate is K ‹K sha›, and the close commit is a docs-only child of it). **Everything planned is merged:** all 53 task ids, TASK-14800 … TASK-14852, are DONE (see "Task ledger"); nothing was cut or deferred. **Built:** ADR-043's stage 0 and its config stage, with every demo payslip unchanged (PIN-1; no golden moved through CI-W6, run `45582399602`). **Closed in the registers:** QUAL-042, QUAL-182, QUAL-184, QUAL-190, QUAL-196, QUAL-202, QUAL-204, QUAL-206, QUAL-233, QUAL-236, QUAL-237, QUAL-240, QUAL-246, QUAL-248, QUAL-253, QUAL-257, QUAL-260, QUAL-263, QUAL-265, QUAL-267 and QUAL-268; SEC-059, SEC-061 and SEC-062; SEC-065 for pay inputs, eligibility and birth date; F10. SPRINT-72's R17 (a Skema cell could not be emptied) is closed by S148 (addendum O-28). **C-2 on K:** ‹C-2 run id›. **Step 7a (C-3):** ‹Step 7a verdicts›. **C-5:** ‹C-5 record› |
````

### D-28 · docs/sprints/SPRINT-148.md · the Plan row (`:6`)

Why: it describes only drafts 1 … 1.2. At HEAD the plan is FINAL draft 1.5.1 with post-READY rows up to "final + 32"
(`PLAN-s148.md:3`, `:7`); its Size row counts 82 agent tasks (`:19`); addendum A1 is FINAL draft 1.3.2
(`ADDENDUM-A1-cell-clearing.md:3`). The old row's text is kept verbatim as "Draft history", followed by the Step 0b
cycle-1 narrative moved from the old Status row.

````old
| **Plan** | `.claude/plans/PLAN-s148.md` **draft 1** (2026-11-09, `planner`): 63 agent tasks (40 dispatch ids TASK-14800 … 14839 + 23 paired `-T` passes), waves 0–7 with CI checkpoints CI-W0 … CI-W6 and four CI mutation runs; every one of the refinement's 111 acceptance criteria owned by exactly one task (script-checked); the five carried NOTEs placed. **Draft 1.1** (Orchestrator): NR-P1 … NR-P6 ruled — the T14b specialist stack merged once; the open-interval start; the Teamoversigt slowness threshold (300-person median doubled or > 2 s → an F-series row); **NR-P4: the edit drawer's organisation change with a future agreement date, which S148's new transfer rule would break, is fixed in S148**; CI mutation runs for the six Docker pins' RED; a day no record covers uses that day's OK version (**told to the owner:** Teamoversigt then reads each day's OK version, invisible on seeded data). **Draft 1.2** (Orchestrator, 2026-11-10): NR-P4 had been ruled but not written into the plan body, the same gap the refinement's cycles 3 and 4 caught, so it is now carried in: TASK-14838 takes the drawer's date block and runs after 14837, since both touch `PersonDrawer.tsx`; new acceptance criterion **AC-C13j** (112 ids). The plan's model rows moved to Fable 5.2 |
````

````new
| **Plan** | `.claude/plans/PLAN-s148.md` **FINAL (draft 1.5.1)**: Step 0b READY on both lenses on 2026-11-10 after five cycles (see "Plan Review (Step 0b)"); afterwards changed only by ruling, each change a "final + n" row of its Revision history, up to **"final + 32"** (2026-11-24, the wave-7 brief adjudication). Its Size row counts 82 agent tasks (`PLAN-s148.md:19`); the task ledger above lists 53 task ids, TASK-14800 … TASK-14852, every one DONE. **Addendum A1** (`.claude/plans/s148/ADDENDUM-A1-cell-clearing.md`, FINAL draft 1.3.2; READY on both lenses at cycle 5, 2026-11-11) added cell clearing and the holiday-balance seed (TASK-14840 … 14842). **Draft history:** `.claude/plans/PLAN-s148.md` **draft 1** (2026-11-09, `planner`): 63 agent tasks (40 dispatch ids TASK-14800 … 14839 + 23 paired `-T` passes), waves 0–7 with CI checkpoints CI-W0 … CI-W6 and four CI mutation runs; every one of the refinement's 111 acceptance criteria owned by exactly one task (script-checked); the five carried NOTEs placed. **Draft 1.1** (Orchestrator): NR-P1 … NR-P6 ruled — the T14b specialist stack merged once; the open-interval start; the Teamoversigt slowness threshold (300-person median doubled or > 2 s → an F-series row); **NR-P4: the edit drawer's organisation change with a future agreement date, which S148's new transfer rule would break, is fixed in S148**; CI mutation runs for the six Docker pins' RED; a day no record covers uses that day's OK version (**told to the owner:** Teamoversigt then reads each day's OK version, invisible on seeded data). **Draft 1.2** (Orchestrator, 2026-11-10): NR-P4 had been ruled but not written into the plan body, the same gap the refinement's cycles 3 and 4 caught, so it is now carried in: TASK-14838 takes the drawer's date block and runs after 14837, since both touch `PersonDrawer.tsx`; new acceptance criterion **AC-C13j** (112 ids). The plan's model rows moved to Fable 5.2. **Step 0b cycle 1** (moved here from the Status row at C-1): Codex ran detached from 09:11 to 09:16 on draft 1.2 and returned **CHANGES-REQUIRED, 0 BLOCKER / 2 WARNING** (`.claude/reviews/PLAN-s148-codex-c1.raw.md`). Both warnings were confirmed against the code. **W-1:** NR-P2's "with no exported month, no call" would also skip the settled-holiday-year hand-off, which is selected independently of exports (`HrBackdateWorklistRepository.cs:1027`). **W-2:** TASK-14830 retires `ConfigFingerprint` but is not authorized for its remaining engine callers (`RuleRegistry.cs:467`, engine `Program.cs:105`). The Fable 5.2 lens gave the first Fable read to the refinement material that cycles 2–5 had reviewed only on the Opus stand-in, and found the cycle-1 BLOCKER there (REF-1) |
````

### D-29 · docs/sprints/SPRINT-148.md · the Orchestrator model row (`:11`)

Why: it names the seat and the judgment models but not the implementers, which the model-routing register's S148
row needs (`docs/operations/model-routing-register.md`). Verified: the plan's implementation line
(`PLAN-s148.md:12`), `test-qa` and `ux` passes logged as Sonnet 5.5 (e.g. `SPRINT-148.md:3337`, `:4627`), the
C-1 sweeps on the `sweep` agent, Haiku 5.5 (`:4663`, `:4749`). The floor-move narrative from the old Status row is
appended verbatim.

````old
| **Orchestrator model** | Opus 5.5 seat, never switched (owner ruling 2026-09-29); client 2.1.301 to 2026-11-09, 2.1.322 from 2026-11-10. Drafting: `planner` (Opus 5.5). Judgment: `reviewer` and `adjudicator` on Fable 5.1 through the refinement (cycle 1 only; cycles 2–5 on the labelled Opus stand-in), **Fable 5.2 from 2026-11-10** |
````

````new
| **Orchestrator model** | Opus 5.5 seat, never switched (owner ruling 2026-09-29); client 2.1.301 to 2026-11-09, 2.1.322 from 2026-11-10. Drafting: `planner` (Opus 5.5). Judgment: `reviewer` and `adjudicator` on Fable 5.1 through the refinement (cycle 1 only; cycles 2–5 on the labelled Opus stand-in), **Fable 5.2 from 2026-11-10**. Implementation by role name: `payroll-integration`, `backend-infrastructure` and `rule-engine` on Opus 5.5; `data-model`, `test-qa`, `ux`, `security`, `constraint-validator` and `trace` on Sonnet 5.5 (`PLAN-s148.md:12`); the C-1 comment sweeps on the `sweep` agent, Haiku 5.5. External lens: Codex. **The floor move** (moved here from the Status row at C-1): **The review floor moved to Fable 5.2 on 2026-11-10** (the owner reset the Fable limit and named the new model, 2026-11-09). After the owner's restart the session runs client 2.1.322; the floor id changed in all its places (reviewer and adjudicator self-checks, the close guard's `$reviewFloor`, its harness fixtures, WORKFLOW's reference line and gate sentence; harness 37/37). The bump procedure listed four places and missed the adjudicator's self-check; it now lists five. Three probes (reviewer, adjudicator, reviewer again) ran on `claude-fable-5-2`, but all three refused: they still read the 5.1 self-check, although the file on disk was changed. Agent definitions were not re-read inside the running session, so the owner restarted a second time. After it, the reviewer and adjudicator probes both read the 5.2 floor. The bump is committed as **`636bc7da`**, with WORKFLOW's "Last verified" record and the procedure now ending in a restart and the probes |
````

### D-30 · docs/sprints/SPRINT-148.md · the Refinement row (`:13`)

Why: it reads "rev 1 … Step 4 cycle 1 running"; the refinement closed READY at rev 5.1 (`SPRINT-148.md:221`; the
cycle-5 verdicts `:210`, `:213`; the file's own header, `REFINEMENT-s148-stage0-and-config.md:1`).

````old
| **Refinement** | `.claude/refinements/REFINEMENT-s148-stage0-and-config.md` — **rev 1** (2026-11-09, `planner`, at `fee2a635`; source `.claude/plans/s147/S148-BUILD-INPUT.md` draft 2.6, every citation re-opened at the base; 20 brief contradictions recorded, one owner question proposed, seven points for the Orchestrator's ruling). Step 4 cycle 1 running on both lenses |
````

````new
| **Refinement** | `.claude/refinements/REFINEMENT-s148-stage0-and-config.md` — **rev 5.1, READY** (2026-11-09): five cycles per lens; at cycle 5 Codex READY 0 B / 0 W and the internal lens READY 0 B / 0 W / 5 N (cycle 1 on Fable 5.1, cycles 2–5 on the labelled Opus stand-in); the record is "Refinement record (Steps 1–4)" below. It began as **rev 1** (2026-11-09, `planner`, at `fee2a635`; source `.claude/plans/s147/S148-BUILD-INPUT.md` draft 2.6, every citation re-opened at the base; 20 brief contradictions recorded, one owner question proposed, seven points for the Orchestrator's ruling) |
````

### D-31 · docs/sprints/SPRINT-148.md · addendum O-28: SPRINT-72 R17 closed, and the "told" list (in the 14840 merge entry, `:1317`)

Why: O-28 is due "the sprint log, at 14840's merge": "**SPRINT-72 R17 closed by S148** (the historical log is not
edited; the closure is recorded in `SPRINT-148.md`). **Told to the owner, no decision asked:** no confirmation dialog
and no undo on clearing (NR-A9); under W-a a posted empty work-time day is now refused and the screen posts a removal
instead; the frontend now posts what it shows; a new register row records the from-empty rebuild gap (O-31)"
(`ADDENDUM-A1-cell-clearing.md:447`). The audit found no such note (`S148-C1-audit-report.md:184`); `grep -n R17`
in the log finds only `:440` and `:564`, both before the build. Three of the four told items are already in the log
(`:676-679`, 2026-11-11); "the frontend now posts what it shows" is not. Facts re-opened at HEAD: R17's text
(`docs/sprints/SPRINT-72.md:56`); the W-a refusal (`SkemaEndpoints.cs:1064-1076`, 400 `skema_work_time_day_empty`);
QUAL-248's closure (`quality-finding-register.md:505`); the clearing facts at CI-W2 (`SPRINT-148.md:1436-1437`); M-19
(`:1461`); QUAL-274 (`quality-finding-register.md:531`). The note goes at the end of the 14840 merge's "In the merge
commit / Registered" list, labelled as written at C-1.

````old
  - `check_docs`: 12 shortcuts, all registered. No `shortcut(pending)`, and the `shortcut(QUAL-248)` line is gone.
````

````new
  - `check_docs`: 12 shortcuts, all registered. No `shortcut(pending)`, and the `shortcut(QUAL-248)` line is gone.
  - **Addendum A1's O-28, owed at this merge and written at C-1 (2026-11-25).**
    - **SPRINT-72's R17 is closed by S148.** R17 (`docs/sprints/SPRINT-72.md:56`) recorded that an emptied Skema cell was never saved: it looked empty, and the value came back on reload. Since this merge an emptied project cell, absence cell or work-time day is saved as a removal: the cell's current row is kept and marked with the clear's outbox id, nothing is stored as 0 hours, and the removal is its own event with the actor and an audit row (QUAL-248, closed FIXED above). CI-verified at CI-W2 (run 43233683529): the 46 `SkemaCellClear*` and 9 `ProjectionBackfillClearTests` facts. The historical log is not edited (O-28).
    - **The ROADMAP row "Clearing a Skema cell" leaves at the close (O-26).** Its two riders are placed: the team overview's "away today" filter is pinned (`TeamOverviewAggregateTests.AwayToday_False_AfterTodaysAbsenceCleared`, AC-CL-24, proved by M-19 at CI-M2), and QUAL-196 is fixed (TASK-14842).
    - **Told to the owner, no decision asked (O-28):** (1) clearing has no confirmation dialog and no undo; retyping the value registers it again (NR-A9); (2) a posted empty work-time day is now refused (400 `skema_work_time_day_empty`), and the screen sends a removal instead (W-a); (3) the screen now saves what it shows: an emptied cell is sent as a removal, where before it looked empty while the stored value stayed and came back on reload; (4) the from-empty rebuild gap is registered, as QUAL-274 (addendum O-31). Items (1), (2) and (4) were told on 2026-11-11 (above, "Told, not asked"); item (3) is recorded here first.
````

---

## Group B — beyond the brief (optional)

### D-32 · docs/sprints/SPRINT-72.md · a dated R17 closure note — **lean: do not apply**

Why offered: the brief allows "a note in `docs/sprints/SPRINT-72.md` … as a dated note only". **Why my lean is no:**
addendum O-28, as ruled, says "the historical log is not edited; the closure is recorded in `SPRINT-148.md`"
(`ADDENDUM-A1-cell-clearing.md:447`), and no older sprint log carries a later dated note (a grep for "Note (S1"
over `docs/sprints/SPRINT-[0-9][0-9].md` finds none). D-31 records the closure where O-28 says. If the Orchestrator
rules otherwise, this is the text (the file is CRLF).

````old
**R17 — Recorded inherited limitation (Reviewer N2): cell-clearing.** The shipped save path filters `hours == null || 0` cells — a cleared cell's clearing never persists (pre-existing; the save contract is R5-frozen). Pinned FE behavior: clearing a cell renders empty locally and the value returns on reload; the limitation is RECORDED here + in the close-out (a follow-up may add delete semantics server-side later). The 7202 agent must NOT "fix" this by touching the save contract.
````

````new
**R17 — Recorded inherited limitation (Reviewer N2): cell-clearing.** The shipped save path filters `hours == null || 0` cells — a cleared cell's clearing never persists (pre-existing; the save contract is R5-frozen). Pinned FE behavior: clearing a cell renders empty locally and the value returns on reload; the limitation is RECORDED here + in the close-out (a follow-up may add delete semantics server-side later). The 7202 agent must NOT "fix" this by touching the save contract.

> *Note, 2026-11-25 (S148): closed by S148. An emptied cell is now saved as an audited removal (QUAL-248; TASK-14840/14841). The closure is recorded in `docs/sprints/SPRINT-148.md`; the text above is left as written.*
````

### D-33 · docs/FRONTEND.md · the ComplianceWarnings row (`:113`) — lean: apply

Why: it says the notice "retires when the rest check reads Skema's intervals (S148 stage 0)". The rest check now reads
them, and the notice stayed, narrowed and informational, by owner ruling R-22 (`ComplianceWarnings.tsx:45-57`,
`:120-125`; `.info` at `ComplianceWarnings.module.css:40-43`; R-22 at `SPRINT-148.md:122-123`). Beside D-5's
live-region line.

````old
| ComplianceWarnings | `ComplianceWarnings.tsx` | EU-WTD compliance warnings, and (S147, QUAL-234 interim) one month-level notice, "Hviletid ikke kontrolleret for N dage", for days the 11-hour rest check could not measure. The sentence is built by `restNotCheckedNotice`, exported from this module by design so Teamoversigt shows the same words; it retires when the rest check reads Skema's intervals (S148 stage 0) |
````

````new
| ComplianceWarnings | `ComplianceWarnings.tsx` | EU-WTD compliance warnings in one polite live region (S148; see "Accessibility patterns"), and (since S147) one month-level notice, "Hviletid ikke kontrolleret for N dage", for days the 11-hour rest check could not measure. The sentence is built by `restNotCheckedNotice`, exported from this module by design so Teamoversigt shows the same words. Since S148 the rest check reads Skema's clock intervals, so the notice lists only days whose hours are not all on the clock; it stays, in the informational style rather than the warning style (owner ruling R-22; QUAL-234) |
````

### D-34 · docs/FRONTEND.md · the WeekGrid row (`:116`) — lean: apply

Why: it says WeekGrid is "only referenced by unrouted `WeeklyView`", but `WeeklyView` was deleted in S66 (`0abcc84e`,
"delete router-orphaned S9 absence FE surfaces (AbsenceRegistration/WeeklyView/useAbsences)"), and `git grep WeekGrid
-- frontend` finds only the component itself. Not S148's doing, but D-1 edits the row above it.

````old
| WeekGrid _(legacy)_ | `WeekGrid.tsx` | Superseded by SkemaGrid; only referenced by unrouted `WeeklyView` |
````

````new
| WeekGrid _(legacy)_ | `WeekGrid.tsx` | Superseded by SkemaGrid; imported by nothing since `WeeklyView` was deleted (S66, `0abcc84e`) |
````

### D-35 · docs/operations/legacy-db-upgrade-runbook.md · an S148 row in the schema table — lean: apply

Why: the table's rule is "Each sprint's additions are documented below" (`legacy-db-upgrade-runbook.md:40`), and
S144 and S145 have rows. S148 changed the schema more than either. Verified at HEAD from the `db-schema.md` diff
`e553777e..HEAD`: seven new tables; eighteen new columns on `local_agreement_profiles`; `applies_from DATE NOT NULL`
on `agreement_configs` and `position_override_configs`; `cleared_by_outbox_id` on `work_time_projection`;
`flex_carryover_max` gone from four tables and `manual_hours` from one. `init.sql` has no `DROP COLUMN` and no guarded
`ADD COLUMN IF NOT EXISTS` for any of the new columns (`grep -n "DROP COLUMN" docker/postgres/init.sql` is empty);
the new columns are inline in the CREATE bodies (`init.sql:906-951`, `:1497-1566`, `:1703-1720`, `:2196-2205`), and no
file under `src` names `flex_carryover_max` or `manual_hours`. The insert-failure consequence is `(unproven)`: argued
from the old schema (`flex_carryover_max DECIMAL NOT NULL`, no default), not run against a legacy database.

````old
 `S144-WORKLIST-HANDLED-MANUALLY-SEGMENT-BEGIN/END`, after the S138 worklist segment; the base CREATE carries the same constraints) |
````

````new
 `S144-WORKLIST-HANDLED-MANUALLY-SEGMENT-BEGIN/END`, after the S138 worklist segment; the base CREATE carries the same constraints) |
| S148 | user_organisations + _audit, customary_days + _audit, employee_hr_facts + _audit, position_override_config_versions; local_agreement_profiles, agreement_configs, position_override_configs, role_config_overrides, work_time_projection | **New tables (CREATE IF NOT EXISTS):** the employee's dated organisation history, an institution's customary days, two HR facts per person (each with its audit table), and the position override's dated version history. **New columns, inline in the CREATE bodies only (no guarded ALTER):** eighteen nullable settings on `local_agreement_profiles` (flex periods, caps and actions, office hours, the paid-absence credit basis); `applies_from DATE NOT NULL` on `agreement_configs` and `position_override_configs`; `cleared_by_outbox_id` on `work_time_projection`. **Removed (no DROP COLUMN in init.sql):** `flex_carryover_max` from four tables and `manual_hours` from `work_time_projection`. Applying the current init.sql to a pre-S148 database therefore adds the new tables but none of the new columns, and keeps the old ones, among them `agreement_configs.flex_carryover_max`, NOT NULL with no default, which no current insert supplies (`(unproven)`: not run against a legacy database). **Pre-S148 databases are reseeded, not upgraded** (the owner's standing "reseed, don't migrate"). | search `CREATE TABLE IF NOT EXISTS user_organisations`, `customary_days`, `employee_hr_facts`, `position_override_config_versions`; the `-- S148 C-13`, `-- S148 C-10` and `-- S148 C-11` comments; `cleared_by_outbox_id` |
````

### D-36 · docs/references/danish-agreements.md · "OK26 configs are … identical to OK24" (`:25`) — lean: apply

Why: a node comparison of the seeds at HEAD: the `agreement_configs` OK24 and OK26 rows differ only in `ok_version` and
the description (`init.sql:1628-1682`); `entitlement_configs` differ only in CHILD_SICK (2 vs 3 days, `init.sql:2004-2010`,
`:2043-2048`); `wage_type_mappings` only in the five call-home rows (D-7).

````old
- OK26 configs are currently placeholders identical to OK24 values
````

````new
- OK26 configs are currently placeholders identical to OK24 values, with two exceptions since S148: the child's sick days per illness (2 under OK24, 3 under OK26) and the call-home day's wage-type rows (OK26 only); see "Entitlement Quotas" and "Wage Type Mappings" below
````

### D-37 · docs/references/danish-agreements.md · the CHILD_SICK quota rows (`:161-163`) — lean: apply

Why: they still give AC 1 day, HK 2 and PROSA 3; QUAL-236 (FIXED in S148) says the sources give 2 for all under OK24
and 3 plus the call-home day under OK26, per illness, with no annual cap (`quality-finding-register.md:493`). The seeds
at HEAD: `init.sql:1998-2010` (the comment "whole sick days PER ILLNESS (is_per_episode; no yearly cap)" and the
rows, `annual_quota` 2 or 3, `reset_month` 1, `is_per_episode` true). The SR rows the table cites were not updated in
S148 (they are entitlement rows, outside O-8's "wage-type rows"); the new text says so.

````old
| CHILD_SICK (AC) | 1 day | January (1) | Yes | No | 0 | -- | Barns sygedag | SR-AC-OK24-016 + SR-AC-OK24-034 |
| CHILD_SICK (HK) | 2 days | January (1) | Yes | No | 0 | -- | Barns sygedag | SR-HK-OK24-028 |
| CHILD_SICK (PROSA) | 3 days | January (1) | Yes | No | 0 | -- | Barns sygedag | SR-PROSA-OK24-005 (divergent quota) |
````

````new
| CHILD_SICK (all five agreements) | **Per illness:** 2 days under OK24; 3 days under OK26, plus the call-home day (AC and its two variants provisional, pending the AC circular) | January (1) (stored; no yearly cap since S148) | Yes (day 2 follows day 1, day 3 day 2; a day with no planned hours is skipped) | No | 0 | -- | Barns sygedag. Corrected in S148 (QUAL-236, TASK-14811); before, AC had 1 day, HK 2 and PROSA 3, counted against the calendar year | SR-AC-OK24-016 + SR-AC-OK24-034 / SR-HK-OK24-028 / SR-PROSA-OK24-005 (these SR rows still record the pre-S148 values) |
````

### D-38 · docs/references/danish-agreements.md · the Credit list and the marker (`:180`, `:184`) — lean: apply

Why: `AbsenceClassification.cs:121` classifies `ChildSickCallHome` as Credit, and the marker moved to
`weekly-timeline-v3` in S148 (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/WeeklyTimelineCore.cs:154`; QUAL-236's
status cell). The section is headed "As built (S147 …)", so S148's change is added, not rewritten into it.

````old
The Skema save refuses any code outside the vocabulary with 422. The change moved the weekly core's replay marker once, to `weekly-timeline-v2` (ADR-016 D10).
````

````new
The Skema save refuses any code outside the vocabulary with 422. The change moved the weekly core's replay marker once, to `weekly-timeline-v2` (ADR-016 D10).

**S148 addition (TASK-14811, QUAL-236).** `CHILD_SICK_CALL_HOME`, the OK26 call-home day, joins the **Credit** class, so its hours count toward the norm like a sick day. That was S148's one change to the pay arithmetic, and it moved the marker once more, to `weekly-timeline-v3`.
````

### D-39 · docs/references/danish-agreements.md · `FlexCarryoverMax` is retired (after `:51`) — lean: apply

Why: S148 removed the field from code and schema (`SPRINT-148.md:1759`, TASK-14816, C-6/C-7; `git grep
FlexCarryoverMax -- src` finds nothing), yet the Key Behavioral Differences table (`:42`) and the Position Overrides
table (`:310-315`) still list it. A dated note under S147's note, rather than editing the tables, keeps the S36
record. The eighteen local settings: `LocalAgreementProfile.cs:30-32` ("no calculation reads them yet (S149)").

````old
> - **`OnCallDutyRate` 0.33 and `NonWorkingTravelRate` 0.5** (D11). Each time type gets three facts per agreement instead: a worked-time fraction, a supplement basis and a rest flag. For AC-agreement staff, travel is the only special time type. Built with D11's later stage, S148 stage 2 or later.
````

````new
> - **`OnCallDutyRate` 0.33 and `NonWorkingTravelRate` 0.5** (D11). Each time type gets three facts per agreement instead: a worked-time fraction, a supplement basis and a rest flag. For AC-agreement staff, travel is the only special time type. Built with D11's later stage, S148 stage 2 or later.

> **S148 note (2026-11-25).** **`FlexCarryoverMax` is retired:** no calculation ever read it, and S148 removed it from the code, the schema and the editors (C-6/C-7, TASK-14816). Its row above and the Position Overrides table's `FlexCarryoverMax` column below are history. `MaxFlexBalance` keeps the values above in the central configuration. An institution's own flex settings (plus and minus caps, settlement and test periods, the actions above the cap and on leaving) are recorded and validated in the local profile since S148, and no calculation reads them until S149 (stage 1).
````

### D-40 · docs/references/danish-agreements.md · the Config Resolution Chain (`:323-331`) — lean: apply

Why: it names `CentralAgreementConfigs` and the `local_configurations` table as the layers. Since S148 the chain reads
the database, each layer the one in force on the day, and never falls back to compiled values
(`src/Infrastructure/StatsTid.Infrastructure/ConfigResolutionService.cs:8-20`, `:51-86`); `local_configurations` is
written by nothing since S21 (`LocalConfigurationRepository.cs:12`). ADR-014's own note is the other drafter's;
this only stops the reference doc contradicting it.

````old
1. **Central config** (CentralAgreementConfigs) -- base values per agreement/OK version
2. **Position override** (PositionOverrideConfigs) -- partial overrides per position, null fields fall through
3. **Local override** (local_configurations table) -- org-specific overrides within central constraints

This chain is implemented in `ConfigResolutionService` (Infrastructure layer).
````

````new
1. **Central config** (the `agreement_configs` table) -- the published version in force on the day, per agreement/OK version
2. **Position override** (the override's dated versions) -- partial overrides per position, null fields fall through
3. **Local profile** (`local_agreement_profiles`, ADR-017) -- the organisation's dated row covering the day; NULL inherits

This chain is implemented in `ConfigResolutionService` (Infrastructure layer). Since S148 each layer is the one in force on the day asked for, and the lookup never substitutes a compiled value: a database error or a day with no published central version refuses (ADR-014). `CentralAgreementConfigs` and `PositionOverrideConfigs` in SharedKernel are compiled tables that `init.sql` mirrors; they are not this lookup's answer, and the three legacy paths that still use them are QUAL-262.
````

### D-41 · ROADMAP.md · § 1 item 3's "org/unit membership history" (`:40`) — lean: apply

Why: S148 dated the organisation half (C-13, owner Q-1; `init.sql:719-727`); a person's unit is still one undated
column, `users.unit_id` (`init.sql:515-524`).

````old
   enforcement core, next up); named follow-ups: org/unit membership history, re-hire spells,
````

````new
   enforcement core, next up); named follow-ups: org/unit membership history (the organisation half dated in S148,
   C-13; unit membership still undated), re-hire spells,
````

### D-42 · docs/references/phase-b-handoff-package.md · the call-home wage type as a Phase B question — lean: apply

Why: QUAL-249's own disposition is "→ Phase B: the payroll experts name the call-home wage type; the five rows are
replaced" (`quality-finding-register.md:506`), and R-16 makes `SLS_0530` "provisional until the Phase B payroll experts
confirm it" (`REF:36`). The brief named only the rest questions for this file; this one has the same destination.
Applied after D-16 (its anchor is D-16's last table row).

````old
| (e) | How does a night shift count toward the state agreement's § 12 stk. 3 "arbejdsdage" (at most 10 working days between two days off)? | The weekly rest-day check | QUAL-256 |
````

````new
| (e) | How does a night shift count toward the state agreement's § 12 stk. 3 "arbejdsdage" (at most 10 working days between two days off)? | The weekly rest-day check | QUAL-256 |

### S148 addition — a wage type to name (2026-11-25)

Since S148 the OK26 call-home day (the rest of the working day on which a child falls ill after the parent has arrived at work) is credited toward the norm and exported under the child's first sick day's code, `SLS_0530`, for all five agreements. That code is StatsTid's provisional choice, not the payroll experts' (S148 refinement ruling R-16). **Question:** which SLS wage type should carry the call-home hours? When answered, the five `wage_type_mappings` rows are replaced (QUAL-249; `danish-agreements.md`, the shared-code note; register rows SR-AC-OK26-003, SR-HK-OK26-003, SR-PROSA-OK26-003, SR-AC_RESEARCH-OK26-003, SR-AC_TEACHING-OK26-001..004).
````

### D-43 · docs/sprints/INDEX.md · the freshness marker 141 → 148 — **lean: do not apply**

Why offered: CI's docs job reports INDEX as trailing (141 < 148 − 3), a soft warning (`tools/check_docs.py:165-181`),
logged as DEBT at Step 0a (`SPRINT-148.md:14`). **Why my lean is no:** S147's close did not move it, and the file's
other tables (Test Progression, Agent Effectiveness and so on) have not been kept since about S101
(`INDEX.md:325-336`); moving the marker would claim the whole file is current, which hides that debt rather than
paying it.

````old
<!-- anchor-sprint: 141 -->
````

````new
<!-- anchor-sprint: 148 -->
````

---

## The exactness check (node, at `ec789343`)

Run over this file: for each block in order D-1 … D-43, the `old` string is matched in its file (normalised to LF,
with every earlier block of the same file already applied). Result: **43 of 43 `old` strings occur exactly once;
every `new` differs from its `old`.** The script is `verify.js` in the drafter's session scratchpad; its path is in the drafter's report.

## Brief contradictions (claim · what the code or docs show · where)

1. "Work … at master HEAD" (`bda18b03` at the session's start) · master is `ec789343`, the C2-R4 census registration
   (QUAL-280 … 282, SEC-070) · `git log -1`. Drafted at `ec789343`.
2. "QUAL-281+ from the C2-R4 census" · the census registered **QUAL-280**, QUAL-281 and QUAL-282 ·
   `SPRINT-148.md:4769-4775`.
3. "SEC-066 to SEC-069" · S148 registered **SEC-059 … SEC-070**; SEC-070 (Medium) arrived with `ec789343` ·
   `security-finding-register.md`, the SEC-070 row; `SPRINT-148.md:4779-4782`.
4. Item 9 allows "a note in `docs/sprints/SPRINT-72.md` … as a dated note only" · addendum O-28 says "the historical
   log is not edited; the closure is recorded in `SPRINT-148.md`" · `ADDENDUM-A1-cell-clearing.md:447`. Drafted as
   ruled (D-31); the SPRINT-72 note is offered as D-32 with a "do not apply" lean.
5. `‹close sha›` "to be filled at C-2, C-3 and C-4" · the close commit cannot carry its own hash; S147 named its close
   sha at C-5 · `SPRINT-147.md:16`. See choice 2.
6. Addendum O-26's cites (`ROADMAP.md:490-503`, `:417-418`, `:428`) have drifted · the row is `:651-664`, the clearing
   lines `:472-473` and `:482-483` · `ROADMAP.md` at `ec789343`.

**Found while drafting, not drafted** (outside this brief, for the record): `docs/FRONTEND.md:222` and `:294` say
"28 hook files"; there are 32 (`frontend/src/hooks/*.ts`), and five are missing from its hooks table
(`useCalendarBootstrap`, `useEmploymentHistory`, `useHrBackdateWorklist`, `useHrFollowUp`, `useTermination`). The
source register's CHILD_SICK entitlement rows (SR-AC-OK24-016/-034, SR-HK-OK24-028, SR-PROSA-OK24-005 and the OK26
`-002` bundles) still record the pre-S148 quotas; O-8 named only the wage-type rows. QUAL-249's location cell cites
`init.sql:1286`, `:1322`; its shortcut comments are at `:1445`, `:1481` (and `:317`). The plan's Size row counts 51
dispatch ids (`PLAN-s148.md:19`) where the ledger lists 53 task ids; I did not reconcile the two `(unproven)`.
**For the KB drafter (ruling 6's governing-day evidence, O-22):** the `agreement_configs` seed rows for OK24 and OK26
differ only in `ok_version` and the description, for all five agreements (`init.sql:1628-1682`), so no seeded
agreement's `HasOvertime`, `HasMerarbejde` or `NormModel` differs between OK versions.

## Needs the Orchestrator's choice

1. **The KB drafter dependency.** D-10, D-16 and D-19 name ADR-044 and `docs/references/rest-period-24h-research.md`,
   neither of which exists at `ec789343`. Apply them in the same commit as the KB draft, or hold them.
2. **The placeholders and when each can be filled.** `‹C-2 run id›` and `‹C-2 Regression n›` at C-2;
   `‹Step 7a verdicts›` at C-3/C-4; `‹K sha›` and `‹close date›` at C-4. **`‹close sha›` cannot be filled in the
   close commit itself** (a commit cannot contain its own hash); S147 named its close sha only at C-5, in the Test
   Verified line (`SPRINT-147.md:16`). I placed `‹close sha›` only in D-26's last cell, to be filled at C-5.
   `‹C-5 record›` (D-27) is filled at C-5. The brief named three placeholders; the four others are mine.
3. **Group B (D-32 … D-43).** My leans: apply D-33 … D-42; do not apply D-32 (O-28 rules the historical log
   unedited) or D-43 (it would hide INDEX's backfill debt).
4. **"Concurrency" in QUALITY.md.** The plan lists it among the domains to re-grade (`PLAN-s148.md:840`), but no
   snapshot has ever graded it. D-25 gives it observations and no grade. Grading it means choosing a first baseline.
5. **The Status row's wording.** D-27 is written in S147's closed style, as the brief asked, so K itself will carry
   "CLOSED ‹close date›" until C-4. The alternative is S147's close-candidate wording at C-1 ("OPEN …; close
   candidate K committed at C-1"), rewritten at C-4.

---

## Revision history

| Draft | Date | By | What |
|---|---|---|---|
| 1 | 2026-11-25 | Planning Drafter (Opus 5.5) | First draft for S148 C-1: 31 owed blocks and 12 optional ones, every `old` checked once-only at `ec789343` |
