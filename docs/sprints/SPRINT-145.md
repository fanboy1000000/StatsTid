# Sprint 145 — payroll works week by week (QUAL-186), then months with a mid-month change become payable (QUAL-149/150)

| | |
|---|---|
| **Status** | **planning** — refinement READY (rev 3.4, Step-4 cycle 5 of 5: both lenses READY-WITH-WARNINGS, no blocker); plan draft 1 in progress (`.claude/plans/PLAN-s145.md`) |
| **Test Verified** | not yet — no S145 code written |
| **Theme** | Payroll stops judging a whole month against one week's norm, which today makes every ordinary month's overtime, merarbejde and flex wrong (QUAL-186). Then a month with a mid-month change to hours, position or agreement becomes payable, with each part under its own wage-type codes (QUAL-149/150). Along the way, a Skema correction stops being counted twice (QUAL-192..194) |
| **Predecessor** | S144: close `cbaa5e0` (CI red: nine test-setup failures) · governance `6b0d982` (review cap 3 → 5; the close guard's CI-health query fix) · post-close fix `8a87d16` (CI green `36610948637`) · docs-only `018ef2a` (QUAL-186), `6849b84` (QUAL-187..191), `de6c228` (the S144 C-9 record), `62fcdc3` (QUAL-192..194) |
| **Base commit (Step 7a)** | **`cbaa5e0`**, the S144 close commit, not HEAD. So `6b0d982` (which touches `.claude/hooks/sprint-close-guard.ps1`, never reviewed on its own), `8a87d16` and the docs commits all fall inside S145's Step-7a diff (`docs/WORKFLOW.md` step 7a) |
| **Orchestrator model** | Opus 5.5 seat throughout (client 2.1.285), under the 2026-09-29 ruling that the seat never switches. Drafting: `planner` (Opus). Judgment: `adjudicator` and `reviewer` (Fable 5.1). External lens: Codex (`gpt-6`, full repository read with its sandbox bypassed, per the owner's standing authorisation) |
| **Refinement** | `.claude/refinements/REFINEMENT-s145-qual149-150-genuine-splits.md` rev 3.4 — READY after five Step-4 cycles (see below) |
| **Owner rulings in force** | **Q1** both stages, with a hard gate between (2026-10-01) · **Q2 = A**, a mid-week hours change is counted per working day (each weekday carries that day's contract; the payslip equals the employee's screen) · **Q3 = F1**, a week that crosses a month boundary is settled by the month containing its Sunday, dated that Sunday · **Q7 = (b)**, a recalculation that changes a week the next month already paid names that month in its response and audit row · reseed, don't migrate (standing) |
| **Owner informed, no objection** | Q4: replay freezes what is data and fingerprints what is code (Fable seat R4) · R14: payroll reads the recorded hours itself instead of taking them from the request · a Skema save replaces the cell, fixed in Stage A before payroll reads the hours · R24: clearing a Skema cell stays out of S145 (ROADMAP backlog) |
| **Entropy scan (Step 0a)** | 2026-10-01 at `62fcdc3`: tree clean except an uncommitted ROADMAP line; 0 worktrees; no non-master branches except pre-existing dependabot/actions remotes; 0 untracked source files; no `FindFirst("scopes")`; no hard-coded `http://localhost` in non-test `src/` |

## Plain-language summary

**What was asked.** Months in which something about an employee changes partway through (hours, position, agreement) are refused by payroll today and must be handled by hand. The owner asked for them to become payable.

**What the refinement found first.** Payroll gets *every* month wrong, not only split months. Its overtime, merarbejde and flex rules are written for one week, and payroll hands them a whole month as if it were one week. A full-time employee working a normal September is exported with about 126 hours of overtime and a flex payout (QUAL-186). Opening split months on top of that arithmetic would pay them wrong just as confidently. So the sprint has two stages, with a hard gate between them:
- **Stage A** makes every ordinary month correct. Payroll works week by week, takes the wage-type codes from the employee's dated record, reads the recorded hours itself, and records a replayable proof of each calculation. Split months stay refused exactly as today.
- **Stage B** opens split months, each part paid under its own codes. If Stage B cannot pass review within the cap, Stage A ships alone.

**What else surfaced.** Re-saving a Skema cell stores a second row, and every server total counts both. The send gate then refuses the month, and the employee cannot repair it. A re-saved ferie day consumes quota twice. Registered as QUAL-192..194, this is fixed first in Stage A, because payroll is about to read those same rows.

## Refinement — Step 4, five cycles (both lenses each cycle)

| Cycle | Draft | External (Codex) | Internal (Fable reviewer) | What it turned on |
|---|---|---|---|---|
| 1 | rev 1 (`8a87d16`) | BLOCKER | NOT-READY | Both lenses confirmed the planner's discovery, QUAL-186 (registered at once, `018ef2a`). The replay promise contradicted the ruled replay contract, the month-boundary weeks were unsound, a month-start change was exempted, and flex was underspecified |
| 2 | rev 2 | NOT-READY | NOT-READY (1 B) | The "safe stop point" was contradicted by Stage A's own text. The manifest could not reproduce a calculation. F1's correction paths were incomplete. The month-vs-week window was unspecified |
| 3 | rev 3.1 (after Q1, Q2, Q3, Q7) | NOT-READY (2 B) | NOT-READY (1 B) | **Convergent:** payroll had no source for the previous month's last days of hours, and a replay could not recover its exact inputs. Ruled R14: payroll reads its own hours. Lossless digest (R15) |
| 4 | rev 3.3 (after the Skema ruling) | NOT-READY (1 B) | READY-WITH-WARNINGS | Convergent: the vacation-settlement reader was missing from the Skema fix's census. Quota pre-validation; fixture preconditions |
| 5 | rev 3.4 | **READY-WITH-WARNINGS** | **READY-WITH-WARNINGS** | No blocker under the final-cycle definition. Carried to the plan: the pre-check must let a reduction through even when `used` is above the cap; a stale test rationale; the host preconditions for the months-in-order facts; wording that separates "positive hours" from "≤ 0" |

Each cycle's findings were adjudicated by the Fable seat (rulings R1–R24) before the `planner` revised. The halt-and-prompt never fired. Register rows opened during the refinement, each committed at once because the refinement file is gitignored: QUAL-186 (`018ef2a`), QUAL-187..191 (`6849b84`), QUAL-192..194 (`62fcdc3`).

## Carried from S144 — the CI-health gate waiver, recorded as the waiver directs

At the S144 close the sprint-close guard's CI-health check queried `gh run list --branch master --event push --status completed --limit 1`. It received a stale answer from GitHub's filtered listing: an old S138 red run instead of the newest green one. On that basis it refused to close S144 "on top of a red sprint". The close went ahead under a written waiver (`.claude/reviews/SPRINT-144-ci-health-WAIVED.md`), because master's latest completed push run was in fact green. The fix landed in the governance commit `6b0d982`: the guard now lists ten push runs and takes the newest completed one client-side, with its harness at 22/22. That hook change falls inside this sprint's Step-7a diff, and Step 7a reviews it.

## Handoff block (written at every gate — the state lives here, not in the conversation)

| | |
|---|---|
| **As of** | 2026-10-01, sprint opened at planning; master `62fcdc3` (+ this log, uncommitted) |
| **Phase** | Refinement READY. Plan draft 1 in progress (`planner`, Opus) |
| **Dispatched tasks / worktrees** | None. No S145 code exists |
| **Pending gates** | Plan draft 1 → Step 0b (both lenses). *Cleared 2026-10-01:* CI on master `62fcdc3` (S144's record push), run `36842927335`, **green on all 7 jobs** — S144 is sealed and S145 code may merge once the plan is READY |
| **Open rulings** | None. The cycle-5 warnings are carried into plan draft 1 |
| **Next action** | Review plan draft 1 (Fable seat), then run Step 0b |
