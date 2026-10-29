# D-9 — F-6: over what period is an AC employee's working time measured?

*Brief, draft 2, 2026-10-28: adjudicated (`.claude/reviews/S147-D-batch1-adjudication.md` § D-9). The owner has not been asked yet. Drafted by `planner` (Opus 5.5); facts verified at `e7478f24`, draft 2's added citations at `b2ece3a1` (no code changed between the two).*

**The question in one sentence.** For staff on the AC agreement (academics), two things need a period:
- **flex:** should it settle each calendar month, as for HK and PROSA?
- **merarbejde** (extra work): should it be judged claim by claim, over whatever period each claim covers, or should StatsTid impose a fixed period such as a quarter?

**The question as it will be asked** (settled by the adjudicator, D-9.1; verbatim).

> *For staff on the AC agreement, should flex settle each calendar month (as for HK and PROSA; a local agreement may still set another settlement period under D-8), with extra work (merarbejde) judged claim by claim over the period each claim covers and reported at least quarterly — or should StatsTid impose a fixed assessment period per AC group (quarter for administrative AC, year for special consultants)?*

- **(a) Monthly flex; merarbejde per claim, no imposed period (recommended)**
- **(b) A configured assessment period per AC group**

*Narrowed to two options.* The brief's option (c), "per local agreement", is **not offered** as a separate choice: it is (a) plus a D-8 local parameter. The question's own wording keeps that door open ("a local agreement may still set another settlement period under D-8"), so nothing is lost by folding it in.

**Why it matters.**
- **HK and PROSA have a period from their agreement:** their norm runs one calendar month (ADR-043 D1).
- **AC's agreement fixes none.** Pay assumes "an average of 37 hours a week". StatsTid must still settle AC flex at some interval, and judge AC merarbejde claims.
- **Today's answer is retired.** StatsTid judges AC week by week and automatically pays every hour above 37 a week as merarbejde. ADR-043 retires that (D5).
- **Who notices:** AC employees, the leaders who attest their claims, and HR.

**What StatsTid does today** (verified).
- **AC is weekly.** The AC config is 37 h a week, merarbejde, a one-week period (`src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs:20-22`, `:34`). The engine settles every Monday–Sunday week (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/WeeklyTimelineCore.cs:150-193`). It pays every hour above the week's norm as merarbejde at 1:1 (`OvertimeRule.cs:50-59`).
- **Two AC positions override the period to 4 weeks.** `DEPARTMENT_HEAD` and `RESEARCHER` carry a 4-week norm period for OK24 and OK26 (`src/SharedKernel/StatsTid.SharedKernel/Config/PositionOverrideConfigs.cs:26-46`).
- **Two AC variants already use a yearly norm.** The research and teaching variants have an annual-activity norm of 1924 h and 1680 h (`CentralAgreementConfigs.cs:204-206`, `:254-256`).
- **The period setting cannot express a month.** `NormPeriodWeeks` allows 1, 2, 4, 8 or 12 weeks (`src/SharedKernel/StatsTid.SharedKernel/Models/AgreementRuleConfig.cs:13`, `:61`; `SYSTEM_TARGET.md:247`).

**What the sources say** (C5, verified in S146; `docs/references/agreement-cells-reverification-research.md:572-605`).
- **No period in the agreement.** It sets none (§ 13 stk. 1: *"gennemsnitlig 37 timer ugentlig"*, an average of 37 hours a week).
- **Merarbejde has a duration and a reporting floor.** It must normally have lasted at least 4 weeks (§ 14 stk. 2), and it must be reported when it ends and at least every quarter (stk. 3).
- **Some groups are on an annual norm.** Teachers under Bilag 6 have one. Special and chief consultants work to one by common understanding, and they are outside the merarbejde rules.
- **Flex periods are set locally,** typically monthly.
- **Never weekly** (HIGH confidence). "Quarterly for administrative AC" is a union reading (MEDIUM).
- **C5 itself recommended a quarter, and ADR-043 departed from it** (D-9.2). C5's "what StatsTid should encode" proposes a calendar quarter as the default merarbejde assessment period (`:599`). ADR-043 F-6 does not follow it, because the quarter is a union derivation from the reporting floor, at MEDIUM confidence (`:605`), not a period the agreement sets. The owner is told this (below).

**The options** (ADR-043 F-6, `:299-305`).
- **(a) Flex monthly; merarbejde without a fixed period.** Each claim states its own period (normally ≥ 4 weeks) and is reported at least quarterly. Whether the work was of "larger extent" stays the employer's call. A local agreement may still set another flex settlement period, as a D-8 local parameter.
  - *Gives up:* an automatic period test. The system tracks and reminds, but the claim drives merarbejde.
- **(b) A configured period:** quarterly for administrative AC, annual for special consultants.
  - *Gives up:* it encodes a union reading (MEDIUM) as if it were the agreement, and the period setting must gain quarter and year assessment values.
- **(c) Per local agreement.** **Not offered** (D-9.1): it is (a) plus a D-8 parameter, and (a)'s wording already allows it.
- **Under every option** (D-9.2):
  - `NormPeriodWeeks` changes, because D1's monthly norm needs a month, which the 1–12-week setting cannot express;
  - the AC `DEPARTMENT_HEAD` and `RESEARCHER` overrides with 4 weeks retire or translate to the new setting.

**The lean: (a), confirmed by the adjudicator** (D-9.3; ADR-043 `:305`; plan `:288`). The adjudicator adds no new reason beyond the narrowing; the brief's reasons stand:
- It follows the agreement, which deliberately sets no period.
- The annual-norm groups keep their own model (`AC_RESEARCH`, `ANNUAL_ACTIVITY`).
- *What the research changes (RR-12):* it corroborates the lean. MedSt's guide (PAV) says AC working time is *"ikke nærmere fastlagt"*, not defined as a concept (PAV 19.3.4; `.claude/refinements/RESEARCH-s147-VERIFICATION.md:29`, `:57`, `:125`).

**AC's month norm uses D1's function** — *recorded proposal — adjudicated, told to the owner, not asked* (D-9.4).
- **The need:** under (a), AC's monthly flex needs a month norm. ADR-043 D1 is written for HK and PROSA only (`:68-81`).
- **The proposal:** use the same month-norm function (7.4 h a working day, pro rata), as the sampled local flex agreements do (SDU 2026 § 2.2; `RESEARCH-s147-VERIFICATION.md:387`).
- **A labelled default:** on søgnehelligdage (public holidays falling on a weekday) the AC norm falls, as it does for HK and PROSA. No AC text says so either way, so the value is labelled as a default for the domain expert to confirm.
- **It lives in one list:** ADR-043's working copy keeps one list of provisional items (`../ADR-043-working.md`, "Provisional items and recorded proposals").
- **A gap for the S148 build input:** how flex settles for the annual-norm variants (`AC_RESEARCH`, `AC_TEACHING`) is not yet designed.

**What the owner should know** (D-9.5, with D-9.2's "tell the owner").
- **Under (a), an AC employee's extra hours are paid only by claim.** The employee claims, the leader attests and the employer decides. That makes it a hand-off to HR and leaders: it gets a row in the HR follow-up register (the HRP list of processes the system hands to HR) when D-4 and D-5 design the workflow.
- **Fuldmægtige on the standby allowance are a special case.** Case officers paid the standby allowance owe up to 20 hours of extra work a quarter, so only extra work *"væsentligt"* (substantially) beyond that counts (V9, `docs/references/overtime-flex-model-research.md:39`). That becomes a condition on the claim.
- **The recommendation departs from the researcher's.** The S146 research proposed a quarter as the default assessment period. ADR-043 recommends no imposed period, because the quarter is a union derivation (MEDIUM confidence), not the agreement's text. Option (b) is the closest to the researcher's proposal.

**What the S148 build gets from each answer.**
- **(a):** AC flex runs on D1's month-norm function, settled monthly (with the labelled holiday default). Merarbejde becomes a claim record with its own dates (D5, through F-2's workflow), with a quarterly reporting check. The weekly merarbejde line retires. `NormPeriodWeeks` still changes (D1 needs a month), and the AC 4-week overrides retire or translate, but no quarter or year assessment value is needed. The annual-norm variants' flex is a gap to design.
- **(b):** a period setting with month, quarter and year (replacing 1–12 weeks; SYSTEM_TARGET `:247` text owed to the owner). The engine judges claims against that period, per AC group. The AC 4-week overrides retire or translate.
- **(c):** none as a separate option (folded into (a) through D-8).

**The ADR-043 text it rewrites.**
- F-6 (`:299-305`) becomes a decision.
- D1's AC bullet (`:78-80`).
- D5's conditions (`:110-117`), only if the period reading changes.
- The recorded proposal above becomes ADR text when this question is answered.

**Waits on.** Nothing.

---
*Revision history.*
- Draft 1 (2026-10-28): first brief.
- Draft 2 (2026-10-28): the adjudication applied (D-9.1–.5). Added "The question as it will be asked", narrowed to (a)/(b) with (c) marked not offered; folded D-9.2 into the facts and options (`NormPeriodWeeks` changes under every option; (b)'s quarter/year values; the AC 4-week overrides; C5's quarter proposal and ADR-043's departure); the lean confirmed; AC's month norm marked as a recorded proposal with its labelled holiday default and the annual-norm flex gap; "What the owner should know" added.
