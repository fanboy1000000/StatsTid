# S148 build input — the overtime/flex model, stage by stage

| | |
|---|---|
| **What this is** | The input S148's refinement starts from (`PLAN-s147.md:299`): ADR-043 as amended in S147, cut into the stages the owner chose (D-1 = (b), 2026-10-29). For each stage: what it does in plain words, its scope items (each pointing to the ADR decision or recorded proposal it comes from), the census and retirement lists the S147 briefs gathered (re-verified here), its marker, its reseed, its HR hand-offs, the limits its sprint log must name, and its open points. **It proposes; S148's refinement decides scope and order.** It contains no owner question: every fork is answered or told |
| **Status** | DRAFT 2 (`planner`), for the adjudicator's check (`PLAN-s147.md:299`). Written beside the amended ADR; draft 2 follows the ADR through its dual-lens review, cycle 1 (`.claude/reviews/S147-ADR-043-adjudication-c1.md`) |
| **Base commit** | `c4eb6184` (master). Every file:line below was opened at that commit unless it is marked otherwise |
| **Orchestrator model** | S147: Opus 5.5 seat; drafting `planner` (Opus 5.5); judgment `adjudicator` / `reviewer` on Fable 5.1 while available |
| **Sources** | `ADR-043-working.md` (the amended ADR: D1–D12, the stage map, the provisional list, the owed research); the briefs in `decide/`; `.claude/reviews/S147-D-batch1-adjudication.md`, `S147-D-10-adjudication.md`, `S147-D-batch2-adjudication.md`, `S147-D-batch3-adjudication.md`; `docs/sprints/SPRINT-147.md`, "Owner rulings in force" |

*Labels.* **Dn** is a decision of the amended ADR-043; **D-n** an S147 decide-track question; **R-1 … R-7** and **D-n.Pm** the adjudicator's recorded proposals; **R-A … R-E** owed research; **Prov. n** an entry of the ADR's list "Provisional items". A "census" is a list of every place in the code that reads or writes something that changes or retires.

## The model in one paragraph

StatsTid today judges overtime week by week and pays every hour above 37 a week automatically, while also keeping those hours in flex. The state's texts say otherwise: the norm is a calendar month, every surplus hour is flex hour for hour, and only hours a leader ordered or accepted as necessary become overtime (+50 %, or an extra-work claim for AC), leaving flex. Supplements are paid only for ordered or rostered evening and weekend work. Overtime waits in an account with a 3-month clock. Each institution sets its own flex rules. S148 builds this in five steps, each correct for ordinary hours on its own, so that each can be reviewed and land without waiting for the rest.

## How the stages work

- **Each stage is correct for unordered hours** (most hours) and its sprint log names what it does not yet handle ("named interim limits" below).
- **A stage that changes payroll's arithmetic moves the evaluation-model marker** (`WeeklyTimelineCore.cs:126-134`; ADR-016 D10's convention), so a month calculated before the stage is refused on replay, by name (`replay-evaluation-model-changed`, `ReplayRefusedException.cs:41`; checked at `PeriodCalculationService.cs:2770-2779`). S148 will open on `weekly-timeline-v2` once TASK-14708 merges (S147's one marker move). The names below are proposals.
- **Reseed, don't migrate, at every stage.** There is no real data.
- **The order** is stage 0 → the config stage → stage 1 → stage 2 → stage 3 (D-1's build line, `decide/D-01-order-of-work.md:61`). D9's calendar split lands before the norm subtracts holidays (C1's trap), inside stage 1.

| Stage | In plain words | Marker (proposed) |
|---|---|---|
| **0** | Stop paying what is wrong today; let the rest check see Skema's times | none for payroll (one move if a stage-0 item changes the engine's arithmetic or the replay contract: QUAL-236's call-home day, D2's bound computed engine-side, or the digest's line form) |
| **Config** | Make the editors' values reach pay, dated and frozen | `weekly-timeline-v3` |
| **1** | The monthly norm and flex by default; the calendar split; forfeiture recorded | `monthly-norm-v1` |
| **2** | Decided hours: orders and marks, overtime, supplements, the overtime account | `monthly-norm-v2` |
| **3** | AC extra-work claims | `monthly-norm-v3` |

---

## Stage 0 — stop paying what is wrong today; let the rest check see Skema's times

*In plain words.* Before any new arithmetic, S148 closes the places where today's system pays or approves something it should not, using fixes that change no payroll arithmetic. On-call, call-ins and travel are refused at registration instead of being paid twice. The child's-sick-day quotas are corrected. The 11-hour rest check starts reading the clock times employees actually enter in Skema, so S147's "not checked" notice can go. A day of paid absence can no longer be credited more than the day was worth. And the configuration editors say plainly that they do not yet affect pay.

| # | Scope item | From | Where it lives today (verified at `c4eb6184`) |
|---|---|---|---|
| S0-1 | **Refuse non-`NORMAL` time.** `POST /api/time-entries` answers 422 for any activity type other than `NORMAL`; payroll refuses, by name, any stored entry of another type | D11; OQ-4 = (b); mechanism R-18 (`REFINEMENT-s147-defects-and-model-rulings.md:446-449`) | The route `TimeEndpoints.cs:24` stores `ActivityType = request.ActivityType` unchecked (`:157`; QUAL-202). Skema always writes `NORMAL` (`SkemaEndpoints.cs:2055`). Rules reading the types: `OnCallDutyRule.cs:36-48` (`ON_CALL`; payroll-evaluated as `ON_CALL_DUTY`, `PeriodCalculationService.cs:3218`, in the set `:2167-2175`); `CallInWorkRule.cs:39` and `TravelTimeRule.cs:37` (not evaluated by payroll). The demo seed writes no activity type (a search of `tools/StatsTid.DemoSeed` finds none) |
| S0-2 | **QUAL-236 fixed in full:** quotas per agreement and OK version (OK24: 2 for all; OK26: 3 plus the call-home day for HK and PROSA, AC the same, labelled provisional pending its circular); the per-episode limit counted per episode, not per year; the call-home day given a representation | Owner Q-2 = (a), 2026-10-28; the route in `quality-finding-register.md:460` | `DefaultEntitlementConfigs.cs:123-148`; `init.sql:1796-1802`, `:1833-1836`; `SkemaEndpoints.cs:44-56` (the register row's `:46-58`, shifted by S147 TASK-14701), `:1146`, `:1642` (the row's `:1148`, `:1644`); `EntitlementValidationRule.cs:34`, `:57-84` |
| S0-3 | **The rest check reads Skema's intervals.** The compliance endpoint reads `work_time_projection` beside the time entries and passes the intervals to the engine's rest rule. S147's T7 notice ("Hviletid ikke kontrolleret for N dage") retires | D10 (rest half); D-2 = (a) | `ComplianceEndpoints.cs:179` (reads time entries), `:210-215` and `:320` (calls the engine); `RestPeriodRule.cs:222-223` (only timed entries are analysed); intervals `init.sql:1973-1984`. T7's notice: S147 TASK-14706/14707 (wave 2, not on master at `c4eb6184`; cite by brief until merged) |
| S0-4 | **A consistency check between the screen's rest analysis and the engine's,** or the server serving its verdict to the screen | D10; D-2.5 | `frontend/src/hooks/useSkema.ts:563` (`analyzeRestPeriods`); `frontend/src/components/SkemaDayPanel.tsx:13` |
| S0-5 | **Manual hours retired** (reseed), so every worked hour has clock times and T7's notice can retire for every day | D10; D-2.4. *Stage placement is this file's proposal:* while manual hours exist, a day with them can never be rest-checked | Census below |
| S0-6 | **One source of time of day per hour.** The legacy route's own start/end times and its voluntary flag are routed through the interval table, or retired with the route | D10; D-2.2. *Placement proposal:* with S0-1 on the same route; at the latest before stage 2's supplement half | Census below |
| S0-7 | **A day of paid absence never exceeds its worth:** *"a day's paid-absence hours, of any credit-class type, may not exceed the credit basis minus the hours worked that day; judged at the Skema save in both directions (QUAL-237)"*, against the day's stored state; or the engine computes min(stored paid-absence hours, max(0, planned − worked)). For quota types, clip the consumption with the credit, or refuse the month. On the central basis until the config stage | D2; R-7 as widened by batch-3 ruling 3 | `SkemaEndpoints.cs:924-1003` (the day cap; the absence-only sum `:968`) and in-lock `:1940-1989` (sum `:1954`): absence hours are capped at the day's amount, worked hours are never counted, and only the posted cells are summed (QUAL-237, `quality-finding-register.md:461`) |
| S0-8 | **The editors' label:** "affects screens and booking; does not yet affect pay" | D12; D-6.5 | `frontend/src/pages/admin/AgreementConfigEditor.tsx`; `frontend/src/components/config/ProfileEditor.tsx` (and, if position overrides are in scope, `frontend/src/pages/admin/PositionOverrideManagement.tsx`) |
| S0-9 | **QUAL-239's interim:** the compensate route reads its guarded update's result and answers 409 before enqueueing `OvertimeCompensationApplied`; no marker; retires with the route in stage 2. S147's tidy pass may do it instead; stage 0 is the latest | D8; QUAL-239; batch-3 ruling 4(c); `.claude/reviews/S147-ADR-043-adjudication-c1.md` R-3 | `OvertimeEndpoints.cs:567-583` (the update's result discarded `:567-576`, the enqueue `:578-583`); `OvertimeBalanceRepository.cs:76-99`, `:106-129` (the guarded updates return `Success`); `quality-finding-register.md:463` (verified at `9de1ae79`) |

**Census: manual hours** (S0-5; D-2's census re-verified, production code).
- *Storage and event:* `init.sql:1977`; `WorkTimeRegistered.cs:17`; `WorkTimeProjectionRepository.cs:22`, `:87-106`, `:129-150`; `ProjectionBackfillService.cs:220-228`, `:324`, `:502`.
- *Skema API:* `SkemaEndpoints.cs:341`, `:454` (served), `:851-856` (validated), `:890` (day total), `:2095` (written), `:2692` (the request field); `SkemaResponses.cs:66`.
- *Approval surfaces that add it to worked hours:* `ApprovalEndpoints.cs:930-934` (team overview), `:1356-1360` (allocation breakdown), `:2224-2230` (the send gate); `BalanceEndpoints.cs:873`.
- *Demo:* `tools/StatsTid.DemoSeed/Loading/DemoLoader.cs:620` (writes 0).
- *Frontend:* `useSkema.ts:389-407` (the carry-forward pin), `:479-480`, `:697-722`; `SkemaDayPanel.tsx:66`, `:167`, `:331-333` ("Manuelt registreret"); `SkemaGrid.tsx:51`, `:76`, `:301`; `frontend/src/pages/SkemaPage.tsx`, 39 matching lines from `:25` to `:1090`; `frontend/src/pages/approval/ManagerSkemaGrid.tsx:76-79`; `frontend/src/lib/api-types.ts:6741`, `:7534` (generated).
- *Tests:* 27 tracked files, 21 under `tests/` and 6 frontend (`BalanceSummary.test.tsx`, `SkemaDayPanel.test.tsx`, `SkemaGrid.test.tsx`, `skemaWorkTime.test.ts`, `SkemaPage.test.tsx`, `TeamRowDetail.test.tsx`).

**Census: the legacy route's clock times and voluntary flag** (S0-6).
- The route: `TimeEndpoints.cs:24`, its entry built at `:150-157` (`StartTime`, `EndTime` at `:154-155`).
- `VoluntaryUnsocialHours`: the event `TimeEntryRegistered.cs:16`; models `TimeEntry.cs:15`, `TimeEntryProjectionRow.cs:24`; written `TimeEntryProjectionRepository.cs:121`, read `:283`; `ProjectionBackfillService.cs:436`; `MidnightCrossingNormalizer.cs:226`; mapped for the engine `PeriodCalculationService.cs:3188` and `ComplianceEndpoints.cs:192`; read by the rest rule `RestPeriodRule.cs:276-278`, `:505`.
- **It is in the replay digest** (`HoursDigest.cs:107`, the entry line form `:31-44`; `ADR-016:185`), so retiring or rerouting the flag changes the digest's canonical form: a contract change to name in S148's refinement. The entry line form without the flag is owed in ADR-016 D10 (the amended ADR's "Documents owed"), and if the change lands in stage 0 it moves the marker (below).
- The agreement field `VoluntaryUnsocialHoursAllowed` (`AgreementRuleConfig.cs:74`) is the policy, not a clock time; it stays unless the flag goes.

**Marker.** None for payroll unless a stage-0 item changes the engine's arithmetic or the replay contract — QUAL-236's call-home day joining the credit class (S0-2), D2's bound computed engine-side (S0-7), or the digest's line form changing when the voluntary flag is rerouted or retired (S0-6); then the weekly core's marker moves once (ADR-016 D10, `ADR-016:180`, `:185`), or S148's refinement names the refusal that does the same job. The other items are Backend and validation changes, and payroll never runs the rest rule (`PeriodCalculationService.cs:2140-2141`).

**Reseed.** Yes (manual hours; QUAL-236's quotas).

**HR hand-offs.** None new.

**Named interim limits (the sprint log).**
- On-call, call-ins and travel cannot be registered until D11's later stage.
- The editors' changes do not reach pay (labelled).
- Every pay figure is still today's weekly model: weekly overtime and its tiers, the supplement table, the automatic flex payout.
- The paid-absence bound uses the central basis (§ 7 nr. 2's 7.4 h × fraction) until the config stage.

**Open for S148's refinement.** Whether `ON_CALL_DUTY` leaves payroll's evaluated set now (after S0-1 it never sees an entry) or with D11; leaving the set changes planning (`PeriodCalculationService.cs:2129-2166`). Whether S0-7 is a save-time check or an engine computation.

---

## The config stage — make the editors' values reach pay, dated and frozen

*In plain words.* Today a GlobalAdmin can publish agreement rules, and an institution can set its local profile, but payroll never uses either; it reads rules compiled into the code. This stage gives every configured value a date it applies from, has payroll fetch the values in force on each day and hand them to the calculation, and freezes them into the month's proof, so a payslip can always be traced to the exact rules it used. It also adds every new local setting the later stages need (flex caps, office hours, customary days off, the paid-absence credit basis) and the two facts HR records about each person (their overtime regime and whether they are on the flex scheme). Pay does not change yet: the weekly formula remains, on today's central caps; the pin is identical demo pay lines.

| # | Scope item | From | Where it lives today |
|---|---|---|---|
| C-1 | **Payroll resolves configs per day** (central → position → local) at the service layer and passes them to the engine in the request; the engine stays pure | D12; D-6 = (a); ADR-014 | The engine reads compiled tables: `RuleRegistry.cs:254`, `:393`, `:446-449` → `AgreementConfigProvider.cs:12` → `CentralAgreementConfigs.cs`; it refuses an unknown agreement (`RuleRegistry.cs:446-447`). The Backend's resolver `ConfigResolutionService.cs:115-203` reads the *current* local profile (`:185`). Payroll registers it and never calls it (`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:147`) |
| C-2 | **Dated configs and dated position overrides** (QUAL-182); editor publishes take effect from an "applies from" date | D12 | `agreement_configs` (`init.sql:1328`) has `published_at` (`:1384`) and no applies-from date; QUAL-182 (`quality-finding-register.md:405`) |
| C-3 | **Configs frozen into the manifest,** replacing "fingerprint what is code"; `replay-config-changed` changes meaning or retires | D12; ADR-016 D10 amended | `ADR-016:169`, `:181`; `ConfigFingerprint.cs:13-17`; `EnsureConfigUnchanged` `PeriodCalculationService.cs:2787-2796`; `ReplayRefusedException.cs:38` |
| C-4 | **The local profile gains D6's columns** (settlement period with the one value `month`; cap-test period; plus and minus caps; daily credit cap; above-cap and leaving actions; office-hours windows; the paid-absence credit basis), typed, dated, audited | D6 (R-1, R-2); D3 (D-7.P1); D2 (R-6) | `local_agreement_profiles` `init.sql:856-872` (values `:864-868`; one active row per key `:874-876`; audit `:885-895`) |
| C-5 | *Moved to stage 1, into S1-7* (`.claude/reviews/S147-ADR-043-adjudication-c1.md` R-6: the cap's pay effect is stage 1's, so the central caps stay as they are in this stage) | — | — |
| C-6 | **`ValidateLocalOverride` retires** (its "local ≤ central cap" premise is gone) | D6 | `ConfigResolutionService.cs:324-327` (`[Obsolete]`, no production caller); called only by tests: `tests/StatsTid.Tests.Unit/Sprint12AgreementConfigTests.cs:92-169` and `Sprint7ConfigTests.cs:99-262` |
| C-7 | **`flex_carryover_max`** retires, or becomes the cap-test period's form; decided here | D6; R-2 amendment (iv) | `init.sql:866`; no reader in the Rule Engine or Payroll (a source search of `src/RuleEngine` and `src/Integrations` finds none); 132 production lines in 31 files, including the position and role overrides (`PositionOverrideRepository.cs`, `RoleConfigOverrideRepository.cs`, `PositionOverrideConfigs.cs`, `CentralAgreementConfigs.cs`, three editors) |
| C-8 | **Office-hours windows** per weekday, off unless set; the editor warns, and does not refuse, when the five windows do not add up to the full-time week's norm (37 h; the profile's own `weekly_norm_hours` where one is set, Prov. 18); how a part-timer's windows scale is Prov. 14 (D-7.P1 as corrected) | D3; D-7.P1 as corrected (`.claude/reviews/S147-ADR-043-adjudication-c1.md` R-5) | No concept exists (a search for *kontortid*, *fikstid*, office or core hours finds none, `decide/D-07-office-hours.md:23-24`) |
| C-9 | **The paid-absence credit basis**, default § 7 nr. 2, with AC's "by analogy" label recorded beside it | D2; R-6; D-8b = (a) | S147 TASK-14708 builds the AC label (wave 2a, not on master at `c4eb6184`) |
| C-10 | **The customary-days table** beside the profile and its editor (`LocalAdminOrAbove`), dated, audited, with a validity period and an optional agreement scope; the part as a clock time or full-time hours (D-12.P3) | D9; D-12.P1, P3 | The nearest patterns: the profile PUT `ConfigEndpoints.cs:119` (authorized at `:430`); absence-type visibility `ConfigEndpoints.cs:525-546`, `init.sql:1125-1134` |
| C-11 | **Two HR-recorded facts per person,** with one HR editor, audited, resolved as of each day (ADR-040) and frozen beside fraction and position (`ADR-016:171`): the **overtime regime** (one value set for HK, PROSA and, provisionally, AC; seeded from ADR-024's per-category overrides; for AC the values are ADR-024 D2's three states, CONTRACTUAL / DISCRETIONARY / NONE, all provisional, so the seed maps one to one: D4, Prov. 5) and **flex membership** (seeded from position) | D4 (D-11 = (b), D-11.4); D2 (D-8a = (b)); D-8a.2 item 4 ("one HR editor") | `employee_profiles` holds fraction, position and category (`docs/generated/db-schema.md:258-271`); the overrides `role_config_overrides` (`init.sql:2448`), seeded at `:2524-2533` (`CONTRACTUAL` / `DISCRETIONARY` / `NONE` per category); payroll passes the engine an empty category (`PeriodCalculationService.cs:2690`) |
| C-12 | **Demo seed:** profile rows for the demo institutions, seeded with their cap values (37 h, as SDU and DTU), which have no pay effect until stage 1 (S1-7; R-6); and example customs. The demo caps are seeded into D6's new typed plus-cap column; the legacy `max_flex_balance` column stays null, so the resolver (`ConfigResolutionService.cs:237-239`) keeps forwarding today's central cap to the weekly core until stage 1 maps the new column; the editor shows the seeded values | D6 (ruling 2); D9 (D-12.P9) | The demo seed writes no `local_agreement_profiles` row today (`decide/D-08-local-flex-parameters.md:42`); the legacy column `init.sql:865`, which the resolver maps over the central cap when set (`ConfigResolutionService.cs:237-239`) |

**Marker (proposal).** The arithmetic is unchanged, but the replay contract changes: configs become frozen inputs, and a manifest written before the stage carries fingerprints, not configs. Proposed: move the marker to `weekly-timeline-v3`, so such a manifest is refused by name. S148's refinement confirms, or names the refusal reason that does the same job.

**Reseed.** Yes.

**HR hand-offs (HRP rows).** The overtime regime (HR records it; no system check: `decide/D-11-pay-ceiling.md:44`); flex membership (`decide/D-08a-flex-membership.md:77`).

**Named interim limits.**
- Values reach the calculation dated and frozen, but the weekly formula still computes pay, on today's central caps; the new columns are read by pay from stage 1 (flex) and stage 2 (overtime).
- **The caps' sequencing, ruled: the cap's pay effect is stage 1's** (`.claude/reviews/S147-ADR-043-adjudication-c1.md` R-6, settling draft 1's sequencing point). Today's automatic flex payout (`FlexBalanceRule.cs:44-48`, `:68-82`) keeps running on today's central caps until stage 1, so no demo payslip changes in this stage; the central fields going null, the demo caps going live and the automatic payout's retirement land together in stage 1 (S1-7). The stage's acceptance pin: every demo month's pay lines identical before and after.

**Open for S148's refinement.** Whether the HR-recorded regime is built here (with flex membership, one editor) or in stage 2, its first reader; this file proposes here. (How ADR-024's three-state seed maps onto the regime's AC values is settled: one to one, R-4; see C-11.)

---

## Stage 1 — the monthly norm and flex by default

*In plain words.* This is the big visible change. The holiday calendar is split by what each day does, then the norm becomes a calendar month with weekday public holidays subtracted. Every hour above the norm goes to flex, hour for hour, for every agreement: the weekly overtime rule retires. A day of paid absence is credited with what the day was worth. Flex above an institution's cap is forfeited and shown (or, with no cap set, nothing is forfeited and the calculation says so). A leaver's flex balance is settled in the leaving month. Paid customary days off are credited. Every HK and PROSA demo payslip moves. Ordered hours earn nothing extra yet: that is stage 2.

| # | Scope item | From | Where it lives today |
|---|---|---|---|
| S1-1 | **The calendar split, first.** One date-keyed authority with three kinds of day; Grundlovsdag, 24 and 31 December and 1 May are not holidays; Store Bededag ended by law, not by OK version. A test pins C1's trap: June 2026 stays 162.80 h, not 155.40 h | D9 | Three lists: the C# list `DanishPublicHolidays.cs:36-62` (Grundlovsdag `:49`; the OK-version key for Store Bededag `:54-58`), read by `SupplementRule.cs:56`; the table `init.sql:165-170`, rows `:358-400` (Grundlovsdag `:367`, `:380`, `:397`), read only by the send-month check `ApprovalEndpoints.cs:2119`; the demo's list `tools/StatsTid.DemoSeed/Generation/DanishHolidays.cs:68` |
| S1-2 | **The monthly norm** for HK, PROSA and AC (AC by D-9.4's labelled default), one function for payroll and the screens | D1; F-6; QUAL-226 | `DayNorm.cs:28-29` (the "holidays do NOT reduce the norm" sentence goes), `:44-45` (the building block stays); the screens' norm `DailyNormCalculator.cs:154-166`; the period setting `AgreementRuleConfig.cs:13`, `:61`; AC's 4-week overrides `PositionOverrideConfigs.cs:26-46` (retire or translate) |
| S1-3 | **Flex by default; weekly overtime retires for every agreement** | D2, D3, D4 | `WeeklyTimelineCore.cs:152-193` (the weekly overtime pass), `:172` (every entry counts as worked); `OvertimeRule.cs:33-95` (tiers `:61-91`, the 100 % line `:82-90`, AC's 1:1 merarbejde `:50-60`, `MERARBEJDE` emitted at `:55`); thresholds census below; the timeline lane's three results (`NORM_CHECK_37H`, `OVERTIME_CALC`, `FLEX_BALANCE`; `PeriodCalculationService.cs:2130-2135`, ids `:3215-3216`) are redesigned around the month |
| S1-4 | **F1's machinery retires** | ADR-043 "What it retires" | `WeeklyContext.cs:42`, `:57`, `:91`; `EmployedContextDays` `PeriodCalculationService.cs:3313`; `PriorMonthRequiringApproval` `:3691`; `EnsurePriorMonthApprovedAsync` `:3727` (called `:795`); `EnsureNoMidWeekAgreementChange` `:3648` (called `:793`, `:1850`) with `PeriodNotCalculableException.cs:46`; Do 7 `PayrollExportService.cs:450-459`; Q7 (b) `RetroactiveCorrectionRequested.cs:56-63`; `PRIOR_MONTH_REOPENED` `HrBackdateWorklistRepository.cs:80`, `ApprovalEndpoints.cs:1810`; `PriorMonthNotApprovedException` (mapped in `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:450-455`, `:549-557`, `:718`, helper `:891`) |
| S1-5 | **The paid-absence credit** through one served "planned hours" figure, read by the credit, the Skema absence cap and the vacation divisor; the office-hours credit where set; R-7's bound on the configured basis. If built in steps, the sprint log names which absence classes read the basis | D2; D-7.P3, P4; R-6 | `ConsumptionCalculator.FullDayHoursAsync` `ConsumptionCalculator.cs:87-99` (delegates to `DailyNormCalculator`); the cap `SkemaEndpoints.cs:946-977`, in-lock `:1941-1963`; the divisor `ConsumptionCalculator.cs:184-194`; the engine's credit set `AbsenceRule.cs:34-45` (S147 TASK-14708 adds `SICK_DAY` and the classification `AbsenceClassification.cs`, not on master at `c4eb6184`) |
| S1-6 | **Flex membership read:** off the scheme, no balance, no settlement, no forfeiture | D2; D-8a = (b) | — (built in the config stage) |
| S1-7 | **The caps' pay effect, all together** (R-6; draft 1's C-5 moved here). **No central flex cap:** the central fields nullable, seeded null; the editor shows caps unset; "no flex cap configured". **The demo caps live** (37 h, as SDU and DTU; the values seeded in the config stage, C-12): stage 1 maps D6's new typed plus-cap column, where the config stage seeded them, and nulls the central fields; until then the resolver forwards today's central cap (`ConfigResolutionService.cs:237-239`), because the legacy `max_flex_balance` column stays null. **Forfeiture recorded, or "no flex cap configured"; minus cap (recorded, never deducted) and daily credit cap; the automatic `FLEX_PAYOUT` retired, surviving only as "1:1 cash by agreement"** | D6; batch-2 ruling 2; R-2, R-3; `.claude/reviews/S147-ADR-043-adjudication-c1.md` R-6 | `CentralAgreementConfigs.cs:23`, `:46`, `:77`, `:108`, `:131`, `:162` (and the AC variants `:193`, `:218`, `:243`, `:268`); `AgreementRuleConfig.cs:20` (`required decimal`); `FlexBalanceRule.cs:44-48`, `:68-82`; `FLEX_PAYOUT` census below; the demo seed writes no `local_agreement_profiles` row today (`decide/D-08-local-flex-parameters.md:42`) |
| S1-8 | **A leaver's flex balance settled in the leaving month** (a dated, recorded movement; the labelled defaults), never carried to a rehire | D6; R-4 | A rehire opens at 0 (`FlexLedgerOpening.cs:44`); nothing records a leaver's balance |
| S1-9 | **Paid customary days** (both modes) credited, a part day by the window after the clock time, or the stated hours × fraction; closure days shown in Skema and kept a working day by the send-month check; the same-day rules; a vacation or særlige-feriedage booking consumes nothing on a søgnehelligdag or the credited part of a paid custom | D9; D-12.P2–P6; ADR-032 D7 | `LEAVE_WITH_PAY` is today's only way (`SkemaEndpoints.cs:34-38`; `AbsenceRule.cs:44`); the send check `ApprovalEndpoints.cs:2116-2127`; `ADR-032:47` |
| S1-10 | **`SUPPLEMENT_CALC` stays evaluated and returns no lines** | D7; D-1.4 | `PeriodCalculationService.cs:3217` (id), the set `:2167-2175` |
| S1-11 | **The opening flex balance,** if S148's refinement schedules R-5 here | D6; R-5 (ruling 3) | `LEDGER_START` opens at 0 (`FlexLedgerOpening.cs:35-36`); QUAL-225 (`quality-finding-register.md:449`) |

**Census: the overtime thresholds** (S1-3; production code, lines per file). `init.sql` 12; `AgreementConfigEditor.tsx` 6; `ProfileEditor.tsx` 2; `AgreementConfigResponses.cs` 4; `AgreementConfigEndpoints.cs` 16; `AgreementConfigRepository.cs` 8; `AgreementConfigSeeder.cs` 2; `ConfigResolutionService.cs` 4; `WeeklyTimelineCore.cs` 2; `DayNorm.cs` 1; `PositionOverrideConfigs.cs` 2; `AgreementConfigEntity.cs` 4; `AgreementRuleConfig.cs` 2 (`:43-44`).

**Census: `OVERTIME_100`** (retires in stage 1 as a line; the wage types retire in stage 2 with D8). `OvertimeRule.cs:86` (and `OvertimeResult.cs`, `OvertimeCompensationApplied.cs`); wage types `init.sql:223-224`, `:295-296` (plain), `:2051-2054`, `:2070-2073` (pay/time-off split).

**Census: `FLEX_PAYOUT`** (S1-7). The rule `FlexBalanceRule.cs:65-82`; `WeeklyTimelineCore.cs:61`, `:109`; `RuleRegistry.cs:381-398`; the engine host `src/RuleEngine/StatsTid.RuleEngine.Api/Program.cs:86-104`; `TimelineEvaluationResponse.cs:11`; payroll: `FlexLedgerOpening.cs:111-120`, `PeriodCalculationService.cs:1155`, `:1428-1434`, `:2599`, `RetroactiveCorrectionService.cs:147`, `:320`, `:397-413`, `:603-605`, `PayrollExportService.cs:133`, `src/Integrations/StatsTid.Integrations.Payroll/Program.cs:592`; wage types `init.sql:268-270`, `:336-338`.

**The `MERARBEJDE` line.** The weekly 1:1 line (`OvertimeRule.cs:55`; wage types `init.sql:226`, `:297`, `:1258`, `:1275`, `:1292`, `:1309`) stops in stage 1. Which merarbejde wage types stage 3's claims use (the pay/time-off pair at `init.sql:2056-2061` is the obvious candidate) is stage 3's design.

**Tests.** A grep of `tests/` and `tools/` for the retired identifiers finds 51 files at `c4eb6184` (the pattern is in the amended ADR's Consequences), an upper bound. They include S145's September example (`WeeklyTimelineCore.cs:85-87`) and the demo's June deficits.

**Marker.** `monthly-norm-v1` (proposed; a new name, because the core is no longer weekly).

**Reseed.** Yes.

**HR hand-offs (HRP rows).** The leaver's flex settlement (beside HRP-011, `hr-follow-up-process-register.md:117`); the manager's cap exception (a dated record for one person, never on the decider's own balance); the opening balance, if R-5 is scheduled. **Retire:** HRP-001b (`:98`) and HRP-024 (`:123`), whose reason is Q3 = F1's settlement of the previous month's days.

**Named interim limits.**
- Ordered hours go to flex hour for hour: no +50 %, no overtime account.
- No § 13 +25 % supplement and no § 14 weekend +50 %.
- AC extra work is flex only.
- Every HK and PROSA demo payslip moves.
- If the planned-hours figure lands in steps, which absence classes read it.

**Open for S148's refinement.** How AC's annual-norm variants (`AC_RESEARCH`, `AC_TEACHING`; `init.sql:1492`, `:1498`; `CentralAgreementConfigs.cs:206`, `:256`) settle flex (D-9.4's gap). Whether HRP-024 retires outright or is re-stated for the flex chain's own months-in-order need (QUAL-229's refusal `payroll-prior-month-stale` is a different reason).

---

## Stage 2 — decided hours: orders and marks, overtime, supplements, the overtime account

*In plain words.* Now an hour can become overtime. A leader can record an order or a roster span in advance; an employee can mark hours nobody ordered in advance, and the leader accepts or rejects the mark when approving the month. Each record names its hours, so payroll knows exactly which hours earn +50 % and which evening or weekend placements earn the supplement. Decided hours wait in an overtime account with a 3-month clock; the employer gives time off first, and untaken time is paid at the deadline unless the employee postpones it or saves it. No one can decide their own hours. The employee's "choose pay or time off" setting and the old compensate route go away.

| # | Scope item | From | Where it lives today |
|---|---|---|---|
| S2-1 | **Records of decided hours.** `overtime_pre_approvals` changes from an hours budget into records naming a date and clock span; kind (ordered beforehand / accepted afterwards); a placement fact separate from the surplus fact; an activity type (`NORMAL`); a creator, a decider, a status; a roster's recurrence on the order record. The matching rule: ordered hours = the overlap with the worked intervals; beyond the span, flex unless marked; short of it, ordered only as worked | D3; D-4 = (c); D-4.P3, P5 | The table `init.sql:2027-2038` (no creator column, `db-schema.md:927-944`), ADR-024 D7's dormant columns `:2551-2555` and audit `:2557-2568`. Routes: create `OvertimeEndpoints.cs:147` (scope only, `:162-164`), list `:244`, `:300`, approve `:359`, reject `:439`, all `LeaderOrAbove`. The page `OvertimePreApprovalManagement.tsx`, routed at `/godkend/overtid` (`App.tsx:104`). The acknowledgement endpoint (ADR-024 D7 point 2) was never built; its event names entry ids (`OvertimeNecessityAcknowledged.cs:23`) |
| S2-2 | **The acceptance half:** a Skema way to mark a span (`ux`); marks shown and decided in Teamoversigt's month approval; the month cannot be approved while a mark is undecided | D3; D-4.P1 | The approve route `ApprovalEndpoints.cs:199`; approvals live in Teamoversigt since S87 (`App.tsx:98-100`) |
| S2-3 | **The order half** (may land after the acceptance half) | D3; D-4.3 | — |
| S2-4 | **Four-eyes on batch-2 ruling 1's four conditions,** T9's rule carried into every deciding record (order, mark decision, compensation, cap exception; 403); the authority is the month approver's | D3; D-4.P4; SEC-056 (`security-finding-register.md:216`) | T9: S147 TASK-14705 (wave 2a, not on master at `c4eb6184`) |
| S2-5 | **Late decisions as dated movements** in the decision month; never a reopening | D3; D-4.P2 | — |
| S2-6 | **Payroll reads the decided records** as a frozen input under ADR-016 D10's read-only contract; they are frozen into the manifest by value, with their state at calculation time; the digest covers them (the ADR's "Frozen by value", R-9) | D3 | The contract today covers two tables (`ADR-016:179`); a decided record's status is editable, so pinning its id alone would not show the state a calculation used |
| S2-7 | **The supplement half of D10:** payroll's snapshot read adds `work_time_projection`; the intervals are frozen by value into the manifest (a latest-wins table cannot be pinned by id) and join the digest, with an interval line form owed in ADR-016 D10; the engine's supplement rule takes intervals | D10; D7; R-9 | One snapshot for both reads today `ProjectionHoursReader.cs:86-101`; the digest `HoursDigest.cs:31-44` (`ADR-016:185`); the supplement rule skips untimed entries `SupplementRule.cs:30-31`; the intervals are latest-wins, one row per employee and date overwritten on each save (`init.sql:1973-1984`; `WorkTimeProjectionRepository.cs:29-32`, `:93-101`) |
| S2-8 | **D7's supplements:** one +25 % for § 13's windows, gated by the placement fact; § 14's +50 % outside the hours count, earning into the account | D7 | `SupplementRule.cs:28-117` (holiday 2.0 `:54-66`, weekend `:68-73`); rates `AgreementRuleConfig.cs:40` and the seeds `init.sql:1468`, `:1474`, `:1480`, `:1486` |
| S2-9 | **D4:** a flat +50 %, routed by the regime (overtime / merarbejde claim / nothing for exempt HK); the part-timer variant (ii) behind the `PartTimeBandRule` slot, its name and from-date in every manifest; no comparable-full-timer field | D4; D-10, D-11 | — |
| S2-10 | **The overtime ledger** (D-5.P1): earned, taken, paid, postponed, converted; derived balance and per-earning deadlines; one writer per table. Storage (one table, or a Backend fact table plus Payroll's own movement table) is the refinement's call; whichever storage, each movement kind has exactly one named writer; across contexts each side reads the other's table read-only (ADR-034 D4) | D8 | The yearly row `overtime_balances` `init.sql:1992-2008`, never fed (`AdjustAccumulatedAsync` `OvertimeBalanceRepository.cs:50`, callers only in tests); `Remaining` `OvertimeBalance.cs:12` |
| S2-11 | **The employer's form choice,** time off at +50 % by default, pay with a reason; the clocks (§ 10, § 14; the default pay in the run after the deadline month); the two elections (postponement both parties sign; saved-days election before the default is calculated) | D8; D-5.P3, P4, P6 | — |
| S2-12 | **Time off in lieu as a Skema absence** in the credit class; earliest deadline first; never more than owed; R-7's bound with its draw clipped or the month refused | D8; D-5.P5 | No such type in Skema's catalogue (`SkemaEndpoints.cs:34-38`) or `AbsenceTypes` (`AbsenceEntry.cs:13-25`) |
| S2-13 | **HRP-021 decision-ready:** the leaders' "time off due" list and the employee's account screen (`ux`) | D8; D-5.P7 | The balance is served (`BalanceEndpoints.cs:227`, `:461-467`), shown nowhere; HRP-021 `hr-follow-up-process-register.md:131` (NOT READY) |
| S2-14 | **A leaver's owed time off paid** (in the leaving month, a labelled default until R-C) | D8; D-5.P9 | — |
| S2-15 | **The saved-days interim:** the election is a movement; a saved day taken is registered as `LEAVE_WITH_PAY`; named in the sprint log | D8; D-5.P10 | `AbsenceRule.cs:44` |
| S2-16 | **D3's office-hours filter** (V8's three conditions; § 14 first) | D3; D-7.P2 | — |
| S2-17 | **D6's stage-2 parameters:** "transfer to the overtime account" as an above-cap action; `MaxOvertimeHoursPerPeriod` as an optional warning over decided hours | D6; R-2 (iii); D-4.P6 | The profile column `init.sql:867`; the field `AgreementRuleConfig.cs:79` |
| S2-18 | **D11's sourced model,** if placed here (else later): the closed catalogue, three facts per type and agreement, the 13 h travel cap and 3 h call-in minimum, Skema registration (`ux`), the refusal lifted, the send gate's predicate revisited | D11; D-3 = (c) | Census below; the send gate counts only `NORMAL` hours naming a project (`ApprovalEndpoints.cs:2241`) |

**Retirement census: the pre-approval switch** (S2-1; D-4.P6; production code). `AgreementRuleConfig.cs:80`; `AgreementConfigEntity.cs:70`, `:142`; `LocalAgreementProfile.cs:11`, `:27`, `:69`, `:128`; `PositionOverrideConfigs.cs:105`; `ConfigResolutionService.cs:218`, `:228`, `:258-262`, `:309`, `:371-373`; `AgreementConfigRepository.cs:127`, `:144`, `:325`, `:665`, `:722`; `AgreementConfigSeeder.cs:91`; `LocalAgreementProfileRepository.cs:352`, `:366`, `:471-489`, `:527-537`, `:591-593`; `LocalAgreementProfileMigrator.cs:400`, `:569-587`, `:643`; `ConfigEndpoints.cs:178`, `:423`, `:569`, `:604`, `:640`; `ConfigResponses.cs:103`; `AgreementConfigEndpoints.cs:249`; `OvertimeGovernanceRule.cs:8`, `:114` (the check `:106-114`); `init.sql:868` (local), `:1379` (central column) and the seed rows `:1454-1508`; `frontend/src/components/config/ProfileEditor.tsx:36`, `:46`, `:196`, `:484`, `:533`; `frontend/src/pages/config/ConfigManagement.tsx:198`; `api-types.ts:6116`, `:7299` (generated). The governance route `OvertimeEndpoints.cs:60`, with its caller-supplied flag `:65`, `:122`, called by no screen. ADR-024's "Bug #4 seed flip" (`ADR-024:228`, `:254`) resolves by retirement.

**Retirement census: the employee's compensation choice** (S2-11; D-5.P3; production code). `AgreementRuleConfig.cs:78`; `AgreementConfigEntity.cs:68`, `:140`; `PositionOverrideConfigs.cs:103`; `CentralAgreementConfigs.cs:36`, `:67`, `:98`, `:121`, `:152`, `:183`, `:208`, `:233`, `:258`, `:283` (true for HK and PROSA at `:67`, `:98`, `:152`, `:183`); `ConfigResolutionService.cs:70`, `:307`; `AgreementConfigRepository.cs:126`, `:143`, `:323`, `:663`, `:720`; `AgreementConfigSeeder.cs:89`; `AgreementConfigEndpoints.cs:247`; `init.sql:1377` (column) and the seed rows `:1454-1508` (TRUE in HK/PROSA's `:1468`, `:1474`, `:1480`, `:1486`). The routes: GET `OvertimeEndpoints.cs:606`, PUT `:670-751` (the gate `:707-708`; a zero row created `:721`, an existing figure kept `:737`). The hook `frontend/src/hooks/useCompensationChoice.ts:28`, imported by no page; its test `frontend/src/hooks/__tests__/employeeTypedWire.test.ts:29`, describe block `:295-317`.

**Retirement census: the yearly row and the compensate route** (S2-10). `overtime_balances` `init.sql:1992-2008`; `OvertimeBalanceRepository.cs:50` (`AdjustAccumulatedAsync`; test caller `tests/StatsTid.Tests.Regression/Config/S122CompensationVocabularyTests.cs:133`), the guarded updates `:76-99`, `:106-129`; `OvertimeBalance.cs:12`. The compensate route `OvertimeEndpoints.cs:512-603`: scope `:524`, remaining-hours check outside the transaction `:535-541`, no audit row `:543`, the event with `ConvertedHours = Hours` `:549-560`, the update whose result is discarded `:567-576` (QUAL-239, `quality-finding-register.md:463`). T9's guard on it is defence in depth until it retires (batch-3 ruling 4(a)). The 100 % wage types `init.sql:2051-2054`, `:2070-2073`; the survivors `:2047-2050`, `:2066-2069`.

**Census: typed time for D11** (S2-18, whenever it lands). On-call's 0.33: `OnCallDutyRule.cs:36-48`; `AgreementRuleConfig.cs:47-48`; `CentralAgreementConfigs.cs:57`, `:88`, `:142`, `:173`. The non-working travel rate 0.5 (`NonWorkingTravelRate`): `AgreementRuleConfig.cs:58`; `AgreementConfigEntity.cs:57`, `:131`; `CentralAgreementConfigs.cs:33`, `:63`, `:94`, `:118`, `:148`, `:179`, `:203`, `:228`, `:253`, `:278`; `PositionOverrideConfigs.cs:97`; `ConfigResolutionService.cs:297`; `AgreementConfigRepository.cs:123`, `:140`, `:316`, `:656`, `:713`; `AgreementConfigSeeder.cs:82`; `AgreementConfigResponses.cs:75`, `:145`; `AgreementConfigEndpoints.cs:236`, `:768`, `:838`, `:908`, `:976`, `:1002`, `:1018`, `:1124`; `TravelTimeRule.cs:48`; `init.sql:1368` and the seed rows `:1454-1508`; `frontend/src/pages/admin/AgreementConfigEditor.tsx:76`, `:116`, `:601`. The call-in and travel rules `CallInWorkRule.cs:39`, `TravelTimeRule.cs:37`.

**Marker.** `monthly-norm-v2` (proposed).

**Reseed.** Yes.

**HR hand-offs (HRP rows).** The leader's decision step on marked hours (a new row; HRP-018, `hr-follow-up-process-register.md:151-156`, describes the budget page that retires, so it is closed or re-pointed); HRP-021 made decision-ready.

**Named interim limits.**
- AC merarbejde is still flex only (until stage 3).
- A saved day taken is registered as paid leave until the saved-days balance lands (P10, after R-A).
- On-call, call-ins and travel are still refused, unless D11 lands in this stage.
- If the order half lands after the acceptance half, rosters are marked afterwards each time.

**Open for S148's refinement.**
- **Marks and Skema saves must stay consistent.** The intervals are a JSON list per employee and date, with no ids, replaced on each save (`init.sql:1973-1984`). A save that removes work under a decided mark is either refused or reopens the mark (`decide/D-04-marking-workflow.md:142`).
- **PROSA pay groups 3–4:** is their merarbejde a D5-style claim (stage 3) or a decided hour here? Declined as an owner question by the adjudicator as a build-input gap (batch-2 "Declined").
- **The ledger's storage** (D-5.P1) and the **sequencing** of the acceptance and order halves (D-4.3).
- **An opening overtime balance at go-live** (the R-5 pattern; `decide/D-05-overtime-account.md:183`).

---

## Stage 3 — AC extra-work claims

*In plain words.* For staff on the AC agreement, extra work is not overtime but a claim the employer decides. The employee claims hours over a period of their choosing (normally at least 4 weeks), the leader attests, the employer decides, and the agreement's quarterly reporting floor becomes a reminder. An accepted claim's hours leave flex in the month of the decision and earn +50 % into the same overtime account, time off where possible.

| # | Scope item | From | Notes |
|---|---|---|---|
| S3-1 | **The claim record,** reusing D3's shape (hours, period, creator, decider); the quarterly reporting reminder; the fuldmægtig standby condition (only work substantially beyond 20 h a quarter counts) and its local interaction parameter | D5; F-6 = (a); D6 | Who "the employer" is when it decides: stage 3's design |
| S3-2 | **The regime gate:** the HR-recorded regime decides whether an AC employee can claim at all: CONTRACTUAL only; the gate reads DISCRETIONARY as NONE | D4; D5; D-11.4; R-4 | The AC values are ADR-024 D2's three states, CONTRACTUAL / DISCRETIONARY / NONE, all provisional (Prov. 5); the seed maps one to one. Whether HR is shown a DISCRETIONARY employee's surplus as information is this stage's design, never a pay line |
| S3-3 | **A decided claim** leaves flex as a dated movement in the decision month and earns into D8's ledger at +50 %, after R-B | D5; D3; D8; R-B | R-B blocks the earning (R-1); S3-1 and S3-2 may be built first |
| S3-4 | **AC's time-off deadline,** after R-B | D8; R-B | R-B blocks S3-3 and this item |
| S3-5 | **The AC part-timer check** (D-10.2 item 8; the Orchestrator's ruling of 2026-10-29: "D-5's build" means ADR-043 D5, stage 3) | D4 | — |
| S3-6 | **Merarbejde wage types** for claims | D5 | The pay/time-off pair `init.sql:2056-2061` |

**Marker.** `monthly-norm-v3` (proposed). **Reseed.** Yes.

**HR hand-offs.** The AC claim workflow (an HRP row when designed; `decide/D-09-ac-period.md:61`).

**Named interim limits.** None of the model's own, except the items placed later.

---

## Later, or not yet placed

- **D11's sourced model,** if not in stage 2 (`decide/D-03-worked-time-classes.md:66`: it may come after S148).
- **The saved-days balance** (D-5.P10), after R-A; its sprint is S148's refinement's.
- **The HR-entered opening flex balance** (R-5), if not placed in stage 1.
- **Suspension of flex in peak periods** (R-2; "last"; mechanics for the expert).
- **AC's annual-norm flex** (D-9.4's gap).

## S147 interim behaviours S148 retires

| Interim, or S147 state | Where it lives | What replaces it | Stage |
|---|---|---|---|
| T7's "Hviletid ikke kontrolleret for N dage" notice (QUAL-234's interim; OQ-4 = (b)) | S147 TASK-14706/14707 (wave 2, not on master at `c4eb6184`) | D10's rest-check half reading the intervals; for every day once manual hours are retired | 0 |
| QUAL-232: S147 built nothing (OQ-4 = (b)); stage 0 builds the refusal, itself an interim | `TimeEndpoints.cs:157` today | The refusal (stage 0), lifted by D11's sourced model | 0, then D11's stage |
| QUAL-236: S147 corrected only the docs rows (O-9) | `quality-finding-register.md:460`'s pointers | The code fix (Q-2 = (a)) | 0 |
| The editors silently not affecting pay | — | The stage-0 label, then D12 | 0, then config |
| T9's self guard on the compensate route (SEC-056's self part, Q-A = (a)) | S147 TASK-14705 (wave 2a) | Defence in depth until D8 retires the route (batch-3 ruling 4(a)); T9's rule is carried into every deciding record (D3) | 2 |
| TASK-14708's AC sick-day label | S147 TASK-14708 (wave 2a) | Stays (D-8b = (a)); recorded with the credit basis's default | config |
| T6's "flex not tracked" month before the ledger start (OQ-3 = (a) + (i)) | S147 TASK-14715/14716 | R-5's HR-entered opening balance, when scheduled | 1 or later |
| The marker `weekly-timeline-v2` (TASK-14708) | S147 | The config stage's and stage 1's markers | config, 1 |

## HR hand-offs, by stage

| Stage | Hand-off | Register action |
|---|---|---|
| Config | HR records the overtime regime | new row |
| Config | HR records flex membership | new row |
| 1 | The leaver's flex settlement | new row, beside HRP-011 (`:117`) |
| 1 | A manager's cap exception | new row |
| 1 or later | The HR-entered opening balance | new row, when built |
| 1 | HRP-001b (`:98`), HRP-024 (`:123`) | retire with F1 |
| 2 | The leader's decision step on marked hours | new row; HRP-018 (`:151-156`) closed or re-pointed |
| 2 | The 3-month deadline | HRP-021 (`:131`) made decision-ready |
| 3 | AC merarbejde claims | new row, when designed |
| — | Customary days | none: configuration (D-12.P8) |

## Open research and expert items

| Item | What it blocks |
|---|---|
| **R-A** § 11 in full (saved days) | Building the saved-days balance only |
| **R-B** AC's merarbejde time-off deadline | A decided claim's earning into D8's ledger (S3-3) and the deadline item (S3-4); the claim record, the reminder and the regime gate (S3-1, S3-2) may be built first. Input: C5 already records § 14 stk. 5's following-quarter rule (`agreement-cells-reverification-research.md:190`, `:589`) |
| **R-C** A leaver's owed time off and saved days: timing and form | An expert item for S148's refinement; Prov. 9 meanwhile |
| **R-D** Verify the directive clause behind "comparable full-timer" held as met | Quoting it; not the default |
| **R-E** Watch for FV2025-795's implementing text | Nothing; ends Prov. 1 |
| Expert questions Prov. 13–20 | Each names its decision; none blocks a stage |

## Points reported to the Orchestrator (not resolved here)

None open. Draft 1's two points were settled by the adjudication of the ADR review (`.claude/reviews/S147-ADR-043-adjudication-c1.md`): the regime's AC values by R-4 (C-11, S3-2), the office-hours warning's basis by R-5 (C-8).

## Deferred branch

S148 may not carry every stage (D-1.5). If it cannot, its sprint log records which stages landed and with which markers; each remaining stage keeps its scope, marker and named interim limits for the next sprint; the HRP rows of stages not built stay unwritten; and ROADMAP § 1 points the next sprint at what is left. Nothing in a later stage is a prerequisite for an earlier one, except as the order above states.

---
*Revision history.*
- Draft 1 (2026-10-29, at `c4eb6184`): `planner` (Opus 5.5). Written from the amended ADR-043 (WC-1 of the working copy), the twelve briefs and four adjudications. The censuses are re-taken at `c4eb6184` (manual hours; the pre-approval switch; the compensation choice; `NonWorkingTravelRate`; `flex_carryover_max`; `FLEX_PAYOUT`; the thresholds; `ValidateLocalOverride`, which two test files call, not one). Placement proposals made here, for S148's refinement: manual hours and the one-source rule in stage 0; the regime with flex membership in the config stage; the config stage's marker move. Findings made here: the config-stage cap/payout sequencing point; HRP-024's retirement with F1; the voluntary flag's place in the replay digest.
- Draft 2 (2026-10-29, at `9de1ae79`): `planner` (Opus 5.5). The adjudication of the ADR's dual-lens review, cycle 1 (`.claude/reviews/S147-ADR-043-adjudication-c1.md`), applied. R-1: R-B blocks S3-3's earning and S3-4, not S3-1/S3-2 (S3-3, S3-4, the research table). R-2: the customary part day as a clock time or full-time hours (C-10, S1-9). R-3: new S0-9, QUAL-239's interim 409. R-4: the AC regime's three states, seed one to one (C-11, S3-2); reported point 1 removed, and the config stage's "Open" sentence on the mapping marked settled. R-5: the office-hours warning against the full-time week (C-8); reported point 2 removed. R-6: the cap's pay effect is stage 1's (C-5 moved into S1-7, C-12's cap values into S1-7; the config stage's plain words and interim limits). R-7: stage 0's marker exception widened (the stage table, stage 0's marker). R-9: frozen by value (S2-6, S2-7, the voluntary-flag census). N3: "will open" on `weekly-timeline-v2`. N8: one named writer per movement kind (S2-10). N9: the regime's placement stays open. New citations checked at `9de1ae79` (`OvertimeEndpoints.cs:567-583`, `OvertimeBalanceRepository.cs:76-99`, `quality-finding-register.md:463`, `WorkTimeProjectionRepository.cs:29-32`, `:93-101`, `agreement-cells-reverification-research.md:190`, `:589`).
- Draft 2.1 (2026-10-29): `planner` (Opus 5.5), on the Orchestrator's ruling (it follows R-6, no new adjudication). C-12: the demo profile rows are seeded with their cap values in the config stage, and the caps have no pay effect until stage 1; S1-7 makes them live. This replaces draft 2's "C-12's cap values move to stage 1", so the build input reads the same as the ADR's config-stage "Lands".
- Draft 2.2 (2026-10-29, at `a522865f`): `planner` (Opus 5.5), on the Orchestrator's rulings on the cycle-2 internal review (`.claude/reviews/S147-ADR-043-reviewer-c2.md`). C-12 names the mechanism (option (a)): the demo caps go into D6's new typed plus-cap column, the legacy `max_flex_balance` (`init.sql:865`) stays null, so the resolver (`ConfigResolutionService.cs:237-239`) keeps forwarding today's central cap until stage 1; S1-7 mirrors it (stage 1 maps the new column and nulls the central fields). S0-9 cites both guarded updates, `OvertimeBalanceRepository.cs:76-99`, `:106-129`.
