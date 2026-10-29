# D-1 — F-1: build the new overtime/flex model in one cut-over, or in stages?

*Brief, draft 2, 2026-10-28: adjudicated (`.claude/reviews/S147-D-batch1-adjudication.md` § D-1). The owner has not been asked yet. Asked next. Drafted by `planner` (Opus 5.5); facts verified at `e7478f24`, draft 2's added citations at `b2ece3a1` (no code changed between the two).*

**The question in one sentence.** Should S148 switch payroll to the new model all at once, or in a few self-contained stages? Each stage would be correct for ordinary (unordered) hours. The cost is that, until the stage that records a leader's order lands, ordered hours go to flex hour for hour and earn no supplement.

**The question as it will be asked** (settled by the adjudicator, D-1.1; verbatim).

> *Should S148 move payroll to the new overtime/flex model in one cut-over, or in self-contained stages — where each stage is correct for ordinary (unordered) hours, and the sprint log names what the stage does not yet handle: until the stage that records a leader's order lands, ordered hours go to flex hour for hour and earn no supplement?*

- **(a) One cut-over**
- **(b) Stages (recommended)**

*Narrowed:* the brief's option (c), keeping today's weekly overtime until stage 2, is **not offered**. It keeps one hour both paid as overtime and kept in flex, the double count ADR-043 rejects as its alternative (c). The Orchestrator's prose names it as excluded, so the owner sees why it is missing.

**Why it matters.** The new model (ADR-043) changes almost everything payroll computes: the norm, flex, overtime, supplements, and AC's extra-work claims.
- **One cut-over:** pay does not change until all of that is built and reviewed.
- **Stages:** each step is reviewed and lands sooner, with a known, named gap between steps.

Nothing is deployed and the database is reseeded, so the gap underpays no one. What this answer really decides is how S148 and the sprints after it are cut and reviewed. Every later answer lands in some stage, so this question comes first.

**What StatsTid does today** (verified).
- **One marker for all the arithmetic.** An "evaluation-model marker" is stored with every payroll calculation; it names the version of the arithmetic that produced it. One marker covers every rule payroll calls (`src/RuleEngine/StatsTid.RuleEngine.Api/Rules/WeeklyTimelineCore.cs:126-134`, value `weekly-timeline-v1`). It must change whenever any of that arithmetic changes (`:131-132`).
- **A replay refuses a different marker by name.** A replay is the after-the-fact proof that a paid figure follows from its recorded inputs. When the marker differs, it refuses (`src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs:2758-2767`; `…/ReplayRefusedException.cs:41`, `replay-evaluation-model-changed`).
- **Payroll evaluates six rules, the supplement rule among them** (`PeriodCalculationService.cs:2155-2163`; `SUPPLEMENT_CALC` at `:3205`). The weekly overtime rule is one of the six (`OVERTIME_CALC`, `:3203`), and it serves every agreement, AC included.
- **S148 starts from v2.** S147's TASK-14708 (wave 2a) moves the marker once, to `weekly-timeline-v2`, for the sick-day credit (`.claude/plans/s147/TASK-14708.md:39`). ADR-043 gives the new core a new name rather than a version number (`ADR-043:198-200`).
- **Already placed, whatever this answer is.** S148 stage 0 holds QUAL-236 (owner Q-2 = (a)) and the QUAL-232 interim refusal (OQ-4 = (b)). If D-6 is answered (a), that work is its own stage, placed before the first stage that needs a local value (`REFINEMENT-s147…:518-524`).

**The options** (ADR-043 F-1, `:244-252`).
- **(a) One cut-over.** Everything changes together, behind one marker change.
  - *Gives up:* time and reviewability. It is one very large change that waits on its slowest part, AC claims, which also depend on the answers to D-4 and D-5. Nothing reaches pay until all of it does.
  - *Gains:* no interim state.
- **(b) Coherent stages, each changing the marker.**
  - Stage 1: the holiday-calendar split, the monthly norm, flex by default, recorded forfeiture.
  - Stage 2: the marking workflow (who orders or accepts hours), the overtime account, and supplements (which read the marking's "ordered placement" fact).
  - Stage 3: AC claims.
  - *Gives up:* between stages 1 and 2 no hour can be overtime. Ordered work lands in flex hour for hour (1:1) instead of at +50 %, and earns no supplement (the recorded proposal below). Stage 1 also retires the weekly `OvertimeRule` for **all** agreements, so between stages 1 and 3 AC extra work is flex only as well: the same interim class, lasting until AC claims land. The sprint log names these limits.
- **(c) Stages that keep today's weekly overtime until stage 2.** **Not offered** (D-1.1): it keeps one hour paid as overtime and kept in flex, ADR-043's rejected alternative (c) (`:233-235`). It is not a valid path, and the prose names it as excluded.
- *Considered, not proposed:* build the stages behind a switch and move pay over once. That gives stages for review and a single change for pay. With nothing deployed, (b)'s interim limit costs nothing, so the switch buys little and doubles the code paths for a while.

**The lean: (b), confirmed by the adjudicator** (D-1.3; ADR-043 `:251-252`). The adjudicator's reasons:
- Nothing is deployed, so the interim underpays no one.
- Each stage is right for most hours, because most hours are unordered.
- Each stage has its own marker. A month proved under stage 1 is therefore refused by name under stage 2, not silently re-proved under arithmetic that did not produce it.
- (a) ties the whole model to its slowest part, AC claims, which also depend on D-4 and D-5.
- The research (RR-1 … RR-13) does not change this lean.

**No supplement in stage 1** — *recorded proposal — adjudicated, told to the owner, not asked* (D-1.4). The plan records it, and the owner hears it in the prose as part of the interim limit.
- *Why:* under the new rule, the +25 % is due only when the work's placement was ordered or rostered (D7, `:130-134`). Only stage 2 can record that fact.
- *Why not the alternatives:* both are wrong for unordered hours, the very hours stage 1 claims to get right. Keeping today's table is wrong because the sources contradict it (QUAL-227). Paying +25 % on every timed hour is wrong because it pays placements nobody ordered.
- *Mechanism:* `SUPPLEMENT_CALC` stays among the rules payroll evaluates (`PeriodCalculationService.cs:2160`) and returns no lines in stage 1, so payroll's list of evaluated rules (`:2155-2163`) does not have to change for stage 1.
- The sprint log names this limit beside the 1:1 one.

**What the owner should know** (D-1.5).
- **Stage 1 is the big visible change.** It replaces the weekly norm with the monthly one and retires weekly overtime, so every HK and PROSA demo payslip moves in stage 1. Later stages add lines back (overtime, supplements, AC claims) only for ordered, rostered or claimed hours.
- **S148 may not carry all the stages.** The S148 build input says how many it carries. The plan already has a valve for running out of room, its "deferred branch" (acceptance criterion AC-D3, `PLAN-s147.md:301`): the sprint log records what was decided, and the next sprint opens on what is left.
- **Each stage makes older developer months un-replayable.** Every stage moves the marker, so a month calculated before the move is refused on replay, by name. Nothing real is lost, because there is no real data, but the development database is reseeded at each stage.

**What the S148 build gets from each answer.**
- **(a):** stage 0, then one stage that holds the whole model. The S148 build input names it as one branch that may span sprints.
- **(b):** stage 0 → D-6's config stage (if D-6 = (a)) → stage 1 → stage 2 → stage 3. Each stage has its own marker and acceptance criteria, and the build input says how many stages S148 carries. Stage 1 also carries the "no supplement" limit above.
- **(c):** none (not offered).

**The ADR-043 text it rewrites.**
- F-1 (`:244-252`) becomes a decision.
- Consequences, "The evaluation-model marker changes" (`:198-200`).
- D9's fixed order (`:162-163`) stays as written.
- The recorded proposal above becomes ADR text when this question is answered (listed in `../ADR-043-working.md`, "Provisional items and recorded proposals").

**Waits on.** Nothing.

---
*Revision history.*
- Draft 1 (2026-10-28): first brief.
- Draft 2 (2026-10-28): the adjudication applied (D-1.1–.5). Added "The question as it will be asked" (option (c) not offered); (a)'s cost names D-4/D-5; (b)'s cost adds AC's flex-only interim (D-1.2); the lean confirmed with the adjudicator's reasons; "no supplement in stage 1" marked as a recorded proposal with its `SUPPLEMENT_CALC` mechanism; "What the owner should know" added.
