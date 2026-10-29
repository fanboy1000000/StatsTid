# D-2 — QUAL-234: how do the clock times entered in Skema reach payroll and the rest check?

*Brief, draft 2, 2026-10-28: adjudicated (`.claude/reviews/S147-D-batch1-adjudication.md` § D-2). The owner has not been asked yet. Drafted by `planner` (Opus 5.5); facts verified at `e7478f24`, draft 2's added citations at `b2ece3a1` (no code changed between the two).*

**The question in one sentence.** An employee records "worked 08:00–12:00 and 18:00–22:00" in Skema. Should payroll and the 11-hour rest check use those times? If so, how should they get them:
- by reading the times where Skema already keeps them,
- by stamping invented times on the project hours, or
- by asking the employee to enter times per project?

**The question as it will be asked** (settled by the adjudicator, D-2.1; verbatim).

> *How should the clock times an employee enters in Skema reach the supplement calculation and the 11-hour rest check: should payroll read them where Skema already keeps them (one more Backend table in payroll's read-only contract, frozen into the calculation's proof), should the Skema save stamp derived start/end times onto each project's hours, or should the employee enter clock times per project?*

- **(a) Read Skema's work intervals (recommended)**
- **(b) Stamp derived times on project hours**
- **(c) Clock times per project (a Skema redesign)**

The sub-question on manual hours is **not asked**. The adjudicator settled it as a recorded proposal that the owner is told in one sentence (below).

**Why it matters.** Two things depend on *when* hours were worked:
- the unsocial-hours supplement for evenings, nights and weekends (under ADR-043, one +25 %);
- the 11-hour daily rest check.

Today neither sees Skema's times. No Skema hour ever earns a supplement, and the server's rest check skips every Skema day (QUAL-234, rated Critical). S147's T7 makes the check say "not checked" instead of looking compliant, but only this decision fixes the cause. Without it, ADR-043's new supplement changes nothing for Skema users. Who notices: evening workers, the manager reading Teamoversigt (the team overview screen), and payroll.

**What StatsTid does today** (verified).
- **Project hours carry no times.** Skema's save writes each project's hours as a time entry with no start or end time (`src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs:2051-2067`), although the entry has fields for them (`src/SharedKernel/StatsTid.SharedKernel/Events/TimeEntryRegistered.cs:10-11`).
- **The times live elsewhere.** The day's clock intervals, plus a "manual hours" figure, go to a separate event and table, `work_time_projection` (`SkemaEndpoints.cs:2084-2106`; `WorkTimeRegistered.cs:16-17`; `docker/postgres/init.sql:1973-1984`).
- **Payroll reads only time entries and absences** (`src/Integrations/StatsTid.Integrations.Payroll/Services/ProjectionHoursReader.cs:97-100`). Its read contract covers only those two tables (`ADR-016:179`), and so does its replay digest, the fingerprint of exactly the rows a calculation read (`:185`).
- **Both rules skip untimed hours.** The supplement rule skips any entry without times (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/SupplementRule.cs:30-31`). The rest check analyses only timed entries (`RestPeriodRule.cs:222-223`, `:455-456`).
- **The rest check is the Backend's, not payroll's.** The server's 11-hour check is a Backend endpoint: it reads the time entries (`src/Backend/StatsTid.Backend.Api/Endpoints/ComplianceEndpoints.cs:179`) and calls the Rule Engine's rest rule over HTTP (`:210-215`, `:320`). Payroll never runs the rest rules (`PeriodCalculationService.cs:2128-2129`; its six evaluated rules at `:2155-2163`).
- **The times match the paid hours, to the hundredth of an hour.** A month cannot be sent unless, on every day, interval hours plus manual hours equal the project hours (`src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs:2225-2272`). The comparison rounds both totals to hundredths of an hour and then compares them (`src/Backend/StatsTid.Backend.Api/AllocationBalance.cs:7-11`); its tolerance (`:61-74`) absorbs only representation noise, so a real difference of 0.01 h blocks the send. For a sent month, the intervals therefore describe exactly the hours payroll pays. The gate counts only `NORMAL` entries that name a project (`ApprovalEndpoints.cs:2241`).
- **The screen checks what the server does not.** The Skema screen runs its own rest analysis on the intervals (`frontend/src/components/SkemaDayPanel.tsx:13-16`; `frontend/src/hooks/useSkema.ts:563`). The employee can see a warning the server never computes.
- **Manual hours are a leftover.** Their entry field was removed (`SkemaDayPanel.tsx:21-23`); the save still accepts the figure (`SkemaEndpoints.cs:2666-2670`); the demo loader writes 0 (`tools/StatsTid.DemoSeed/Loading/DemoLoader.cs:620`).
- **Only the legacy route carries times:** `POST /api/time-entries` (`src/Backend/StatsTid.Backend.Api/Endpoints/TimeEndpoints.cs:24`, `:150-157`), whose page no screen route reaches. It stores its own start and end times on the entry (`:154-155`) and a "voluntary unsocial hours" flag (`TimeEntryRegistered.cs:16`), which the rest check reads to skip a voluntary stint (`RestPeriodRule.cs:276-278`).

**The options** (refinement `:506`; plan `:281`).
- **(a) Payroll and the rest check read the work intervals.** Flex and pay keep reading the hours from the project entries; the intervals supply the time of day only.
  - *The rest-check half:* the Backend's compliance endpoint reads the intervals and passes them to the engine. It moves **no payroll marker** (payroll does not run the rest rule), so it belongs in **S148 stage 0**, and it retires T7's "not checked" notice at once.
  - *The supplement half:* payroll's read-only contract on Backend tables grows from two tables to three, the replay digest gains the intervals, and the marker moves (ADR-016 D10). It waits for D-4's placement fact, so it lands in stage 2.
  - *Gives up:* an architecture change (the contract extension above).
  - *Gains:* the times used are exactly the ones the employee entered, and screen and server check the same facts.
  - *A condition on ADR-043's new D10* (D-2.2): **one source of time of day per hour.** The legacy route's own start/end times and its `VoluntaryUnsocialHours` flag are either routed through the interval table or retired with the route, so no hour has two competing clock times.
- **(b) Invented times.** The Skema save stamps each project entry with a start and end time, computed by laying the project hours into the day's intervals (for example, in row order).
  - *Gives up:* the stored record asserts times nobody entered for that project, and which project "worked the evening" is arbitrary. Manual hours still have no times.
  - *Gains:* no reader, digest or marker change.
- **(c) Intervals per project.** The employee enters clock times on each project row.
  - *Gives up:* usability. Skema becomes a different screen with more typing, and today's two-step flow (work time first, then allocation to projects; `SkemaDayPanel.tsx:17-20`) is redesigned.
  - *Gains:* no payroll change.
- **Sub-question: manual hours** (a day total with no times). **Settled as a recorded proposal: (ii) retire them** (D-2.4; below).
  - **(i) Keep them.** They are paid as hours, earn no supplement, and the rest check reports the day "not checked" for good.
  - **(ii) Retire them** (reseed). Every worked hour then has times.
  - **(iii) Treat them as daytime hours.** *Not valid* for the rest check: it is the fail-open pattern (a check that passes when it could not look) that QUAL-234 was rated Critical for.

**The lean: (a), confirmed by the adjudicator** (D-2.3; refinement `:506`; plan `:281`). The adjudicator's reasons:
- It uses the facts the employee entered.
- Payroll already reads two Backend tables under a designed read-only contract (`ADR-016:179`); this adds a third of the same kind.
- The proof grows to cover the intervals, so replay keeps proving the result.
- (b) is an auditability smell: the stored record would assert times nobody entered. (c) spends every employee's time on extra typing.
- *What the research changes:*
  - **RR-9:** §§ 13–14 are word for word the same in OK24 and OK26 (R5), the agreement rounds in force from April 2024 and April 2026. So there is one set of supplement windows and one § 14 rule, with no split by OK version.
  - **RR-7:** whether a kind of time counts as *rest* for the 11-hour rule is a separate question from whether it counts as *worked time*. For example, home on-call is rest unless it defeats rest's purpose (PAV 19.7.1). That flag becomes a D-3 column, which the rest check reads.

**Manual hours are retired** — *recorded proposal — adjudicated, told to the owner, not asked* (D-2.4).
- **The one sentence the owner is told:** "the leftover 'manual hours' figure is retired; every worked hour then has clock times".
- *Why:* nothing on screen creates them (the entry field is gone and the grid's edit hook is wired to no page: `frontend/src/components/SkemaGrid.tsx:76`), and the demo writes 0.
- **Census of readers, for the S148 build input** (production code at `b2ece3a1`; every one changes or goes when the figure retires):
  - *Storage and event:* `init.sql:1977` (the column); `WorkTimeRegistered.cs:17`; `src/Infrastructure/StatsTid.Infrastructure/WorkTimeProjectionRepository.cs:22`, `:87-106`, `:129-150`; `ProjectionBackfillService.cs:220-228`, `:324`, `:502`.
  - *Skema API:* `SkemaEndpoints.cs:343`, `:456` (served), `:853-858` (validated), `:892` (day total), `:2097` (written), `:2670` (request field); `SkemaResponses.cs:66`.
  - *The approval surfaces, which add it to worked hours:* `ApprovalEndpoints.cs:930-934` (team overview), `:1356-1360` (allocation breakdown), `:2224-2230` (the send gate); and `BalanceEndpoints.cs:873`.
  - *Demo:* `DemoLoader.cs:620`.
  - *Frontend:* `useSkema.ts:389-407` (the "carry the day's existing value forward" pin), `:479-480`, `:697-722`; `SkemaDayPanel.tsx:66`, `:167`, `:331-333` (shows "Manuelt registreret"); `SkemaGrid.tsx:51`, `:301`; `frontend/src/pages/SkemaPage.tsx:335-379` (39 matching lines in the file); `frontend/src/pages/approval/ManagerSkemaGrid.tsx:76-79`; `frontend/src/lib/api-types.ts:6741`, `:7534` (generated).
  - *Tests:* 27 tracked test files mention it (21 under `tests/`, 6 frontend), a count for the census to settle.

**What the owner should know** (D-2.5).
- **(a) alone pays no supplement.** Reading the clock times is necessary but not enough: the +25 % is due only when the work's placement was ordered or rostered, a fact only the marking workflow (D-4) records. Evening hours therefore start earning the supplement in stage 2, not when the times first reach payroll.
- **The 11-hour rule then lives in two places,** the Skema screen's own check and the engine's. So the build must either test that the two always agree, or have the server send its verdict to the screen. That is a build item.
- **The allocation gate needs another look later.** It counts only ordinary (`NORMAL`) hours that name a project (`ApprovalEndpoints.cs:2241`). When D-3's typed entries (on-call, call-ins, travel) arrive, that rule must be revisited.

**What the S148 build gets from each answer.**
- **(a):**
  - *Stage 0, the rest-check half:* the compliance endpoint reads `work_time_projection` beside the time entries; T7's "not checked" notice retires; no payroll marker moves.
  - *Stage 2, the supplement half:* payroll's snapshot read adds `work_time_projection` inside the same snapshot (`ProjectionHoursReader.cs:86-101`); the digest and the manifest record the interval rows; the engine's supplement rule takes intervals; ADR-016 D10 is amended; the marker moves inside D-1's stage.
  - One source of time of day per hour (the legacy route's times and voluntary flag routed through the intervals or retired).
  - The manual-hours retirement and its census (above), with a reseed.
  - A consistency test between the screen's rest check and the engine's, or a served verdict.
  - How an interval is assigned to a day is S148's refinement (ADR-039 settled it for timed entries).
- **(b):** a Backend change to the Skema save, plus re-stamping whenever intervals change; no payroll change.
- **(c):** a Skema redesign (`ux`) and a new allocation rule; no payroll change.

**The ADR-043 text it rewrites.**
- A new decision, D10 (QUAL-234). It must state **one source of time of day per hour** (D-2.2).
- D7's supplement bullet (`:130-134`) gains the time source.
- Consequences, "Not an OK-version transition" (`:205-209`): the OK24 check it promises is done (R5).
- The worked example's note (`:52-53`).
- Under (a), also ADR-016 D10 (`ADR-016:179`, `:185`).
- The recorded proposal above becomes ADR text when this question is answered (listed in `../ADR-043-working.md`, "Provisional items and recorded proposals").

**Waits on.** Nothing (R5 ✓).

---
*Revision history.*
- Draft 1 (2026-10-28): first brief.
- Draft 2 (2026-10-28): the adjudication applied (D-2.1–.5). Added "The question as it will be asked"; folded D-2.2 into the facts and option (a) (the hundredth-of-an-hour match with `AllocationBalance.cs`; the rest-check half in stage 0 with no payroll marker; the supplement half in stage 2; one source of time of day per hour); the lean confirmed with the adjudicator's reasons; manual hours marked as a recorded proposal with the census of readers; "What the owner should know" added.
