# Sprint 147 — confirmed defects (QUAL-229, QUAL-230, QUAL-231, QUAL-235, SEC-053/054) and the overtime/flex rulings round (ADR-043)

| | |
|---|---|
| **Status** | **OPEN 2026-10-27** — refinement (Steps 1–4) |
| **Theme** | Two tracks. **Build:** QUAL-229 first (owner Q-1, 2026-10-26), then the contained defects the tender analysis confirmed: a leaver's final month that can be neither approved nor paid (QUAL-230), sick days that cost flex in payroll (QUAL-231), særlige feriedage that cannot be registered (QUAL-235), one HR user signing a month twice (SEC-053) and the raw payroll export without approval (SEC-054). **Decide:** ADR-043's forks F-1..F-9, asked one at a time, with three tender-analysis questions folded in: how Skema's clock times reach payroll (QUAL-234), which activity types count as worked time (QUAL-232), and which configuration source payroll reads (QUAL-233). The model program is built in S148 |
| **Predecessor** | S146: close `b510e45b` · **C-2 GREEN `40324407582`** on `b510e45b`, all seven jobs (checked 2026-10-27) · PR #40 closed and `s146-ci-w2` deleted · post-close, docs-only and unpushed: `1e105f61`, `ba6b7be7`, `67809348` (ROADMAP: dependency inventory; standards conformance; the owner's "it can wait"), `6b987c5b` (the tender gap analysis, which registered QUAL-230..235 and SEC-053/054) |
| **Base commit (Step 7a)** | **`b510e45b`**, the S146 close commit (`docs/WORKFLOW.md` step 7a), so the four post-close docs commits fall inside S147's Step-7a diff |
| **Orchestrator model** | Opus 5.5 seat (client 2.1.301), never switched (owner ruling 2026-09-29). Drafting: `planner` (Opus 5.5). Judgment: `adjudicator` and `reviewer` on Fable 5.1 while it is available; Fable reached its usage limit on 2026-10-26, and if it is limited again the owner's interim direction stands: an Opus stand-in, labelled honestly. External lens: Codex |
| **Plan** | — (after the refinement) |
| **Refinement** | `.claude/refinements/REFINEMENT-s147-defects-and-model-rulings.md`, rev 2 in Step 4 cycle 2 (record below) |
| **Owner rulings in force** | **S147 OQ-4 = (b)** (2026-10-27): S147 also builds the interim "not checked" fix to the rest check (QUAL-234's false "no breach"); the activity-type interim (QUAL-232) is not built and stays S148 stage 0 · **S147 OQ-3 = (a) + (i)** (2026-10-27): retire both raw payroll routes; a month before the employee's ledger start is paid through the calculating route with flex "not tracked" (this reverses S146's ruled refusal of that case); a zero-line such month writes no record · **S147 OQ-2** (2026-10-27): "I am leaning towards that it should only be HR, but the day after the last day seems to fast to shut the manager out. Can it be 5 days after, and we add to roadmap they should get a notification for final confirmation?" Read and confirmed as: HR only, after a **5-calendar-day grace** (unit confirmed by the owner the same day) in which the leaver's own manager can still approve as today. Mechanism (Orchestrator): automatic deactivation moves from the day after the last working day to 5 calendar days after it; the leaver's own login still ends the day after the last day; from day 6, HR only. The notification is a ROADMAP item (HR operations) · **S147 OQ-1 = (a)** (2026-10-27): strict four-eyes, no exception: whoever sent a month (the employee, or HR on their behalf) can neither approve nor reject it; reopen stays allowed because it is not a signature; enforced at the endpoint, inside the transaction and at the payroll export (SEC-053) · **S147 scope** (2026-10-27): "Defects + model rulings": build QUAL-229, QUAL-230, QUAL-235, QUAL-231 and SEC-053/054 (after the owner's two rulings on them), and run the ADR-043 rulings round with QUAL-231..234 folded in; the model program is built in S148 · **S146 Q-1** (2026-10-26): QUAL-229 is S147's first payroll item · **S146 OQ-3 = (b)**: flex by default, overtime/merarbejde only for ordered or accepted hours, never both; the detail forks are S147's · standing: reseed, don't migrate; review cap 5 per lens; owner questions one at a time, explained first |
| **Entropy scan (Step 0a)** | 2026-10-27 at `6b987c5b`. **Worktrees:** none beyond the main checkout; local branches: master only. **Remote:** `origin/s144-red-mutations` (an S144 evidence branch, "never merge") and `origin/add-claude-github-actions-…` (2026-02-26) linger (**DEBT**). **Untracked:** `.claude/plans/s145/{b0,ca1,ca3,cb1,cb5}/` (S145 evidence logs, kept untracked as at S146's open), `.claude/plans/s146/ADR-043-draft.md` (the working draft; the ADR itself is in the KB) and `Tidligere udbud/` (the owner's tender files) — none under `src/`, `tests/`, `tools/` or `frontend/`. **Anti-patterns:** no `FindFirst("scopes")`; no hard-coded localhost in `src/`; no rule class constructed outside the Rule Engine (PAT-005). **Endpoint authorization CLEAN:** in every host, the only maps without an authorization call are `/health` and `/api/auth/login`. **KB paths:** 105 cited, 18 non-resolving, all benign: globs, shorthand directory paths, two files deleted since their ADR was written (ADR-018's `EventStoreInterfaceTests.cs`, ADR-023's `UserManagement.tsx`), and three runbooks ADR-025 designs but never built. **DRIFT (known, registered at `6b987c5b`):** QUAL-227's and ADR-043's "paid today" supplement examples assume Skema work earns supplements (QUAL-234), and ADR-043 F-5(a) says local profiles already reach the engine (QUAL-233); both are resolved inside this sprint's rulings round. `check_docs.py` is not runnable locally (no Python); CI's docs job is green on `b510e45b` |
| **Baseline** | S146 final (`SPRINT-146.md`): build 0 errors / 145 warnings · CA2100 115 · Unit 1566 · DemoSeed 170 · Regression 2225 in CI / non-Docker 130 locally · Smoke 7 · vitest 1010. Re-measured at the first gate, never carried forward |
| **Pre-merge gate G-0** | SATISFIED: `40324407582` green on `b510e45b` (the commits after it are docs-only) |
| **Test Verified** | — |

## Plain-language summary

S147 does two things side by side.

**It fixes defects that are already confirmed.** The tender gap analysis (`docs/references/tender-requirements-analysis.md`) read the code closely and found eight defects. An independent trace confirmed them. Five of them are contained enough to fix now:
- **A leaver's final month hits a dead end (QUAL-230).** The system deactivates a leaver the day after their last day. After that, nobody can approve, reject or reopen their month, and payroll refuses to export it.
- **A sick day costs flex in payroll (QUAL-231).** Skema and the calculation spell the sickness codes differently, so payroll never credits the day against the norm, while Skema shows it as neutral.
- **Særlige feriedage can be earned but never registered as taken (QUAL-235).**
- **One HR user can sign a month twice (SEC-053).** They send it on the employee's behalf and then approve it as manager. Both tenders and Økonomistyrelsen's guide require two different people.
- **Two admin-only routes send pay lines to payroll with no approval (SEC-054).** Nothing in the product calls them.

The sprint also builds QUAL-229 first, as the owner ruled at S146's close: a guard against a month opening from a flex balance that a correction has made stale.

**It decides the overtime/flex model before anyone builds it.** S146's verified research found the model wrong in kind; ADR-043 sets the direction. Nine design questions remain, from whether to switch over in one go or in stages (F-1) to how local customary days off work (F-9), and they need the owner's rulings. Three questions from the tender analysis belong in the same round:
- how Skema's clock times reach payroll (QUAL-234, Critical, without which no supplement rule matters);
- which activity types count as worked time (QUAL-232);
- whether payroll should read the configuration the admin editors write (QUAL-233).

The model is then built in S148 on settled ground.

## Refinement record (Steps 1–4)

| Step | Outcome |
|---|---|
| Rev 1 | Planner draft (Opus 5.5): six build items (T1 QUAL-229 … T6 SEC-054), a research pass for the cells with no source (R1–R5), and the decide track (D-1 … D-n), with three owner questions in dependency order (OQ-1 SEC-053 → OQ-2 who approves a leaver's month → OQ-3 SEC-054). It found two things beyond the brief: a leaver's month has no screen path at all, and the demo loader signs every month twice with one login |
| Cycle 1 | **Codex NOT-READY** (2 B: T3's "pay lines unchanged" is false near the flex cap; QUAL-229's one-link check misses a stale month further back. 2 W). **Internal lens NOT-READY**, on Opus: the Fable reviewer spawn failed at its usage limit (HTTP 429, `req_011CgSb6y2kdPCM4wh8uG5Jj`), so the owner's interim direction of 2026-10-26 applied (1 B: retiring the raw routes leaves a case-3a month unpayable. 5 W, 9 N) |
| Adjudication c1 | R-1..R-10, by the Orchestrator seat (Opus 5.5; the Fable adjudicator is at the same limit): the full chain walk for QUAL-229; T3's RED through the real Skema codes; HR leaver actions on screen under both OQ-2 options; a remedy for case-3a months before the raw routes go; a separate terminated-inclusive tree lock; **OQ-4** added (the interim "not checked" rest-check fix for QUAL-234) |
| Owner | **OQ-1 = (a)**, 2026-10-27, asked in parallel with rev 2 |
| Rev 2 | All rulings absorbed. NR-8 (OQ-4's option (c) also covers the activity-type interim) and NR-9 (T6's remedy changes S146's case-3a design, so it is asked inside OQ-3) were both kept as drafted by the Orchestrator |
| Cycle 2 | **Codex NOT-READY** (1 B: the chain walk recursed through `Derive`, exponential under the ledger lock. 3 W: AC-054-1's re-run outcome; an UNTRACKED zero-line month locked for good; OQ-4 misstated who can create on-call entries). **Internal lens NOT-READY, on Fable 5.1** (its limit had reset; 1 B: the leaver's employed-month bound sat before the authorisation gate and would leak the employment window. 3 W, 7 N). All cycle-1 findings resolved |
| Adjudication c2 | **Fable adjudicator**, R-11..R-20: one ascending pass over a non-walking link function; the walk always starts at the ledger start; the bound after the authorisation gate; an UNTRACKED zero-line month writes no record (ADR-034 D3 applied); OQ-4's mechanism settled (registration 422 + payroll fail-loud); R2 re-verifies ADR-032 D3. Its observation is registered as **SEC-055** (Low; outside S147 scope) |
| Owner | **OQ-2**: HR only after a 5-calendar-day manager grace (see "Owner rulings in force"); the notification is on the ROADMAP |
| Rev 3 | R-11..R-20 plus OQ-2's grace, as T5a (deactivation on D+6; the leaver's login and own writes end D+1; an end-date-aware write floor), T5b (HR only from D+6) and T5c. The Orchestrator ruled NR-10 = T5c built (a departed approver loses authority on D+1; it only removes authority) and NR-11 = the write floor covers display-only self-writes too |
| Cycle 3 | **Codex NOT-READY** (1 B: an HR-tier leaver's live token can still write their *own* data, since the floor is a role test. 2 W: the self-write census misses compensation-choice and project select; AC-230-6 vs the accepted session residual). **Fable READY** with 4 W: AC-229-2's seed cannot exist (employment is one spell); the same census gap; the same HR-tier own-data gap; a mid-month leaver's stored manager deadline (month-end + 5) is later than the manager's real cut-off (D+5). Both lenses: OQ-3 and OQ-4 accurate |
| Owner | **OQ-3 = (a) + (i)** and **OQ-4 = (b)** |
| Adjudication c3 | **Fable adjudicator**, R-21..R-29:<br>• **The identity clause.** A departed self is refused whatever their role: the write floor refuses when the subject is deactivated or past their end date AND the actor is below LocalHR or *is* the subject. It is applied at every self-write and at both arms of reopen.<br>• **Compensation-choice** joins the site list.<br>• **The deprecated project select/deselect routes are retired.** This was already a recorded small-task, and the routes have no consumer.<br>• **The self-write census is closed in the text.**<br>• **AC-230-6 is qualified to the edge path.** Org-scope token reach until expiry is SEC-003's accepted residual.<br>• **AC-229-2 takes S146's prefix shape.**<br>• **R-27, ruled rather than asked:** a mid-month leaver's month keeps its stored deadline (month-end + 5, the ratified export cutoff). HR's leaver row now shows "manager until D+5, HR only from D+6", with no stored change.<br>NR-10 and NR-11 confirmed |
| Rev 4 | In progress: R-21..R-29, plus the OQ-3/OQ-4 answers made consistent through the document (T6 unconditional; T7 built; T8 moves to S148 stage 0) |

Artifacts: `.claude/reviews/REFINEMENT-s147-{codex,reviewer}-c{1,2}.md`, `REFINEMENT-s147-codex-c{1,2}.log`, `REFINEMENT-s147-adjudication-c{1,2}.md`.

## Task ledger

*(Written at plan approval.)*

## Handoff block (written at every gate — the state lives here, not in the conversation)

| | |
|---|---|
| **As of** | 2026-10-27, on `6b987c5b` (4 docs-only commits ahead of origin) |
| **Phase** | Refinement: cycle-3 adjudication (Fable), then rev 4 and a verification cycle 4 |
| **Open rulings** | OQ-1..OQ-4 all answered (2026-10-27). Pending, from the adjudicator: possibly the mid-month leaver's manager cut-off (Fable c3 W-4). Then the decide track: ADR-043 F-1..F-9 with QUAL-232/233/234 folded in |
| **Next action** | Adjudication c3 → rev 4 → cycle 4 → refinement READY → plan |
