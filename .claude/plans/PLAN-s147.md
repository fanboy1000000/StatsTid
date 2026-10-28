# PLAN — Sprint 147: confirmed defects (QUAL-229, QUAL-230, QUAL-231, QUAL-235, SEC-053, SEC-054, SEC-056) and the overtime/flex rulings round (ADR-043)

*Draft 3.1, 2026-10-28: absorbs the research rulings RR-1 … RR-13 (`.claude/reviews/RESEARCH-s147-adjudication.md`, Fable adjudicator; inputs `.claude/refinements/RESEARCH-s147.md` and `RESEARCH-s147-VERIFICATION.md`, the verification governing): T3b folds into TASK-14708; TASK-14717/-T are DROPPED; the served classification and TASK-14709-N are dropped; QUAL-236 is registered and routed; the decide-track briefs gain the research. Draft 3.1 is folded into draft 3 for one Codex cycle 3 with the internal lens. Draft 3, 2026-10-28: absorbed the Step 0b cycle-2 rulings P-10 … P-15 (`.claude/reviews/PLAN-s147-adjudication-c2.md`, Fable adjudicator; findings in `PLAN-s147-codex-c2.md` (NOT-READY, 1 B / 1 W) and `PLAN-s147-reviewer-c2.md` (READY, 2 W)) under the owner's answer **Q-C = (b)** (keep the route-by-route design; no front-door write gate), for Codex cycle 3 and the narrow internal-lens pass. R1-dependent content in TASK-14708/14717 is unchanged in draft 3 (a separate R1 adjudication is running; draft 3.1 follows). Draft 2, 2026-10-28: absorbed the Step 0b cycle-1 rulings P-1 … P-9 (`.claude/reviews/PLAN-s147-adjudication-c1.md`, Fable adjudicator, RULED-WITH-QUESTIONS; findings in `PLAN-s147-codex-c1.md` (NOT-READY, 2 B / 2 W) and `PLAN-s147-reviewer-c1.md` (READY, 5 W)) and the owner's answer to Q-B (= (c), 2026-10-28); for Codex cycle 2 and a narrow internal-lens pass on the edited sections. Draft 1 was 2026-10-28, for Step 0b on both lenses. Drafted by the `planner` (Opus 5.5) under "Fable judges — Opus drafts". Source of truth: `.claude/refinements/REFINEMENT-s147-defects-and-model-rulings.md` **rev 4.1, READY**, plus the four folds the owner ordered at the refinement's review cap (option (b), "fold + sweep, verify at Step 0b", 2026-10-28; `.claude/reviews/REFINEMENT-s147-codex-c5.md`). The refinement and every review are gitignored, so each brief under `.claude/plans/s147/` is written to be pasted whole, with the refinement lines it names pasted verbatim beside it (`docs/AGENTS.md:212-240`). Every file:line below was re-opened at master `02ef91f1`, which is code-identical to the S146 close `b510e45b` (`git diff --stat b510e45b HEAD -- src tests frontend tools docker` is empty).*

| | |
|---|---|
| **Status** | **DRAFT 3.1** — Step 0b cycles 1 and 2 absorbed (P-1 … P-15; owner Q-B = (c), Q-C = (b)) and the research rulings RR-1 … RR-13 (Q-1 inside D-8; **Q-2 = (a)**, 2026-10-28: QUAL-236 fixed complete in S148 stage 0); awaiting Codex cycle 3 with the internal lens. The four cap folds F1–F4 are verified at Step 0b, not by a sixth refinement cycle. The R research pass and O-1 (no code) may start now; code dispatch waits for Step 0b READY on both lenses |
| **Theme** | Two tracks. **Build:** eight confirmed defects at the payroll boundary and in segregation of duties: a stale flex chain (QUAL-229), særlige feriedage with no Skema row (QUAL-235), sick days costing flex (QUAL-231), one person signing a month twice (SEC-053), a leaver's final month that nobody can approve or pay (QUAL-230, with the owner's 5-day manager grace), the raw payroll routes (SEC-054), the rest check that hides what it did not check (QUAL-234 interim), and a leader deciding their own overtime (SEC-056). The owner's departed-self sweep closes the class: **84 write routes = 32 / 19 / 33** (P-1). **Decide:** ADR-043's F-1..F-9 plus QUAL-232/233/234, twelve questions asked one at a time, ending in an ACCEPTED ADR-043 and a scoped S148 build input |
| **Predecessor** | S146: close `b510e45b` · C-2 GREEN `40324407582` on `b510e45b` (all seven jobs) · post-close docs-only commits `1e105f61` … `02ef91f1` |
| **Base commit (Step 7a)** | **`b510e45b`**, the S146 close commit (`docs/WORKFLOW.md:38`), so the post-close docs commits fall inside S147's Step-7a diff (`SPRINT-147.md:8`) |
| **Master at draft** | `02ef91f1` |
| **Orchestrator model** | Opus 5.5 seat throughout (client 2.1.301), never switched (owner ruling 2026-09-29, `docs/WORKFLOW.md:473-484`). Drafting: `planner` (Opus 5.5). Judgment: `adjudicator` and `reviewer` (Fable 5.1) while Fable is available; if Fable is at its usage limit, the owner's interim direction applies (an Opus stand-in, labelled honestly; `SPRINT-147.md:9`). Implementation by role name, never with a `model` override: `payroll-integration`, `backend-infrastructure`, `rule-engine` (Opus); `test-qa`, `ux`, `security`, `constraint-validator` (Sonnet). Research: `general-purpose` with an explicit `model: opus` (the guard blocks a bare generic spawn, `docs/WORKFLOW.md:343`). External lens: Codex |
| **Refinement** | rev 4.1 READY after five external and four internal cycles (`SPRINT-147.md:38-60`) + the cap folds F1–F4 (below; owner, 2026-10-28) |
| **Owner rulings in force** | **S147 scope** "Defects + model rulings" · **OQ-1 = (a)** strict four-eyes, reopen allowed · **OQ-2** HR only after a 5-calendar-day manager grace; the reminder notification is a ROADMAP item · **OQ-3 = (a) + (i)** retire both raw routes; a case-3a month is paid with flex "not tracked" · **OQ-4 = (b)** build T7 only (T8 → S148 stage 0) · **Q-A = (a)** build T9 · **at the cap: (b) fold + sweep, verify at Step 0b** · **Q-B = (c)** (2026-10-28): a stand-in a manager or HR appointed before the manager left keeps approving until its end date or HR's revocation; `/remove` closes it · **Q-C = (b)** (2026-10-28): keep the route-by-route departed-self design; no front-door write gate (P-11) · **Q-1** (AC's sick-day credit default) is asked inside D-8; S147 builds its lean (a) as a labelled default · **Q-2 = (a)** (2026-10-28): QUAL-236 is fixed complete in S148 stage 0, including the call-home day; no S147 task · **S146 Q-1** QUAL-229 first · **S146 OQ-3 = (b)** · standing: reseed, don't migrate; review cap 5 per lens; owner questions one at a time, explained first (`SPRINT-147.md:12`) |
| **Entropy scan (Step 0a)** | Done 2026-10-27 at `6b987c5b` (`SPRINT-147.md:13`): no extra worktrees; DEBT `origin/s144-red-mutations` and `origin/add-claude-github-actions-…`; untracked `.claude/plans/s145/{b0,ca1,ca3,cb1,cb5}/`, `.claude/plans/s146/ADR-043-draft.md`, `Tidligere udbud/`; anti-patterns clean; endpoint authorization CLEAN; DRIFT (QUAL-227/ADR-043 "paid today" examples; ADR-043 F-5(a)) resolved inside the rulings round (O-1) |
| **Baseline** | S146 final: build 0 errors / 145 warnings · CA2100 115 · Unit 1566 · DemoSeed 170 · Regression 2225 in CI / non-Docker 130 locally · Smoke 7 · vitest 1010 (`SPRINT-147.md:14`). Re-measured at the first gate, never carried forward |
| **Pre-merge gate G-0** | SATISFIED: `40324407582` green on `b510e45b`; everything after it is docs-only |

---

## Plain-language summary (read this first)

**What S147 builds, in the order it lands.**
1. **A stale flex balance can no longer be built on (QUAL-229, TASK-14700, first, as ruled).** Each paid month opens at the previous month's recorded closing. When a correction changes a closing, every later month is out of date until it is recalculated. Today payroll checks only the month directly before, so a stale month further back slips through. After this task, payroll checks the employee's whole recorded chain before it pays a month and names the earliest month that must be recalculated first.
2. **Særlige feriedage get a row in Skema (QUAL-235, TASK-14701/14702).** Everything behind the row already works; only the row was missing.
3. **A departed person can no longer change their own record, from any door (QUAL-230's access rule plus the owner's sweep, TASK-14703).** One rule, "a departed self is refused", now runs inside the organisation-scope check that every admin route uses, and inline at the routes that check something other than the employee. This task also contains the **census the owner asked for**: every write route in the three hosts, each either closed by the rule or classified out with a reason. It is the foundation for the leaver work, so it lands first in its wave and is proven in CI before anything builds on it.
4. **Two people per month, for the month and for overtime (SEC-053, TASK-14704; SEC-056's self part, TASK-14705).** The person who sent a month cannot approve or reject it; a leader cannot approve, reject or pay out their own overtime pre-approval. Reopening stays allowed.
5. **The rest check says "not checked" instead of looking compliant (QUAL-234 interim, TASK-14706/14707).**
6. **One spelling of absence codes; sick days credited as the sourced rule says (QUAL-231, TASK-14708/14709).** The research (R1) found that under the state working-time agreement a paid sick day and a child's sick day count with the day's planned hours; StatsTid applies that to HK and PROSA by the text and to AC-agreement staff as a labelled default that the owner confirms in the decide track (D-8, Q-1). So the planned follow-up task (TASK-14717) folded into TASK-14708 and was dropped. The research also found that the child's-sick-day quotas are wrong for every agreement (QUAL-236, registered); its code fix is S148 stage 0 (owner Q-2 = (a), 2026-10-28: complete, including the call-home day; no S147 task).
7. **The leaver's final month works as the owner ruled (QUAL-230, TASK-14710 … 14714).** The leaver's own login and own writes end the day after the last day; their manager keeps 5 calendar days to act; from day 6, HR only, from a list that now shows whose turn it is; a departing manager's own authority over others ends on day 1, while a stand-in appointed before they left keeps signing until its end date or HR's revocation (owner Q-B = (c)).
8. **The two raw payroll routes are retired (SEC-054, TASK-14715/14716),** after a month before an employee's first recorded month gets a proper path.

**Beside the code, the decide track.** Twelve questions about the overtime/flex model are asked one at a time, each explained first, after an adjudicator has confirmed or replaced the lean. Their answers become ADR-043's decisions, reviewed once by both lenses, and an S148 build list.

**How it is checked.** Every acceptance criterion names its test and its kind: **F** fails on today's code; **N** needs this sprint's new API and proves its RED by one named mutation; **B** a boundary pin that holds today; **R** a regression pin. Docker is unavailable on the owner's machine, so Docker facts are "CI-pending" until a checkpoint run shows them green. There are five wave checkpoints (CI-W1, CI-W2, CI-W3, CI-W3b, CI-W4) and one on the close candidate (C-2).

**What the owner's sweep found (TASK-14703; draft 2, P-1).** The census sorts routes by one rule: a route is guarded when the departed person's own id is the *subject* of a record it writes (their user row, their role assignments, their unit leadership, their own reporting line, or a stand-in standing in **for them**); an id that only *holds* authority over someone else is left to TASK-14712, which makes a departed holder's authority inert from day 1. Of the **84 write routes** in the Backend, Payroll and Orchestrator hosts, **32** reach the employee through the scope validator's employee checks, so the central rule closes them (one of them, removing a manager, also gets a check on the reports it rewrites). Of the **52** that do not, **19 are guarded** (13 by the inline rule, 2 by the self-service rule, 4 by retirement) and **33 are classified out** (31 have no employee subject; 1 cannot address an existing person; 1 is the anonymous login). Beyond the refinement, the sweep added the reporting-line import, unit delete, the manager-removal side effect, a manager's own delegation (set and clear), HR's stand-in for a manager (set and clear) and unit leadership (add and remove). Two side effects are deliberately left alone (P-10, the consequence clause): when HR removes or transfers a colleague who happens to be the departed person's own stand-in, the stand-in arrangement closes too; it could not have survived that removal or transfer anyway, so the departed person's choice does not shape their own record. **The owner kept this door-by-door design (Q-C = (b), 2026-10-28)** rather than one check at each service's front door that would refuse every write by a departed person; the residual (a departed HR, admin or leader's last login can still change other people's data until it expires) stays SEC-003's, as ruled.

**The stand-in question, answered by the owner (Q-B = (c), 2026-10-28).** A stand-in that a manager or HR appointed before the manager left keeps approving the team's months until its end date or until HR revokes it; removing the manager (`/remove`) closes it. That is today's behaviour (S74 R3a), so nothing is built for it; a stand-in a departed manager tries to create *after* leaving is refused by TASK-14703.

---

## How the waves are built — read before the ledger

1. **Foundations land first and alone in their wave's merge order, and are seen green in CI before anything builds on them.** TASK-14703 (the departed-self rule inside `OrgScopeValidator`, `src/Infrastructure/StatsTid.Infrastructure/Security/OrgScopeValidator.cs:56-113`, `:209-288`) changes the one validator every org-scoped route in three hosts runs through and adds a dependency to two hosts' startup. It merges first in wave 2a, and wave 2b (which builds on it) starts only after **CI-W2** is green.
2. **File overlap is designed out by order, not by merge luck.** The table below lists every production file two tasks touch; each pair is in different waves or merges in a stated order.
3. **Tests that need the new API are written after it exists**, on the implementer's branch (the paired `-T` pass), and prove RED by one named mutation (Unit) or an argued docstring (Docker). Where a fact can seed the state directly (a user who is still active with a passed end date: the state that exists today between Copenhagen midnight and Step A's poll, and for D+1..D+5 under the grace), it is **F** on today's code rather than RED-by-mutation.
4. **Contract-changing backend tasks are stacked with their `ux` follow-up** (S146 wave 3, R-25): REGEN regenerates `docs/api/openapi.json` on the backend branch; the `ux` task branches from it and runs `npm run gen:api`; the stack merges together, so master's `tsc` is never red between the two merges.
5. **Worktrees.** Every implementer and every stacked `ux` task runs with `isolation: "worktree"`, branched from LOCAL master (the freshness instruction is the first standing constraint). A paired `-T` pass works on its implementer's branch after the implementer returns. No `git stash` anywhere.
6. **Inside a wave, the later-merging task merges master before its gate** (it rebuilds and re-runs its suites); a test file that two `-T` passes in one wave both report is resolved by that rebuild, never by hand-merging assertions.

**Shared production files and their order**

| File | Tasks, in merge order |
|---|---|
| `src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs` | 14701 (w1: catalogue, labels) → 14708 (w2a: codes from constants; the save's 422) → 14710 (w2b: the self-service floor `:692`, `:1770`, `:2343`) → 14713 (w3: HR tier `:206-236`) |
| `…/Endpoints/ApprovalEndpoints.cs` | 14704 (w2a: approve/reject sender guard) → 14710 (w2b: send floor `:2044`, reopen employee arm `:1621-1623`) → 14713 (w3: the terminated-inclusive swaps, the bound, the tree lock `:325`, `:533`, `:1691`) |
| `…/Endpoints/OvertimeEndpoints.cs` | 14705 (w2a, merges after 14703: approve/reject/compensate) → 14710 (w2b: compensation-choice `:691`) |
| `src/Integrations/StatsTid.Integrations.Payroll/Program.cs` | 14703 (w2a: `TimeProvider`, `/recalculate` guard) → 14713 (w3: `:348` swap and bound) → 14715 (w4a: the case-3a note) → 14716 (w4b: route deletion) |
| `…/Payroll/Services/FlexLedgerOpening.cs` | 14700 (w1: the walk) → 14715 (w4a: case 3a) |
| `…/Payroll/Services/PeriodCalculationService.cs` | 14700 (w1: the two `WindowsReadRange` callers `:897-906`, `:942-976`) → 14708 (w2a: the marker comment `:547`) → 14715 (w4a, if the case-3a path needs it) |
| `…/Payroll/Services/PayrollExportService.cs` | 14704 (w2a: `:395-412`) → 14715 (w4a: `:179-180`) → 14716 (w4b: `:237-243`) |
| `src/Infrastructure/StatsTid.Infrastructure/ApprovalPeriodRepository.cs` | 14704 (w2a: `:1872-1923`) → 14712 (w2b: `:387-396`) |
| `src/Infrastructure/StatsTid.Infrastructure/ReportingLineRepository.cs` | 14712 (w2b: `:1251-1259`) → 14713 (w3: the terminated-inclusive tree-lock overload beside `:741`) |
| `src/Infrastructure/StatsTid.Infrastructure/Security/OrgScopeValidator.cs` | 14703 (w2a: the clause, the inventory doc `:115-124`) → 14713 (w3: the inventory doc only) |
| `src/Infrastructure/StatsTid.Infrastructure/UserRepository.cs` | 14710 (w2b: the R9c banner `:254-256`) → 14713 (w3: the inventory comment only) |
| `…/Endpoints/Helpers/ApprovalSelfGuard.cs` | 14705 only (14704 puts its sender predicate in a new file, so the two never share it) |
| `src/Backend/StatsTid.Backend.Api/Services/AbsenceTypeLabels.cs` (new) | 14701 (w1: created) → 14708 (w2a: keyed by the constants) |

| Wave | Tasks (parallel within a wave unless an order is stated) | Gate before the next wave |
|---|---|---|
| **1** | 14700 + 14700-T (T1) · 14701 + 14701-T (T2) · 14702 (T2 vitest) · **R** first pass + verifier · **O-1** drift fixes → D-1 brief | merge checklist per task → GATE after each merge → REGEN (expect no drift) → **CI-W1** |
| **2a** | **14703 + 14703-T merges first** · 14704 + 14704-T (T4) · 14705 + 14705-T (T9; merges after 14703) · 14706 + 14706-T (T7) → REGEN → 14707 (stacked) · 14708 + 14708-T (T3a with T3b folded in, RR-1) · 14709 (from master; RR-4) | GATE after each → **CI-W2** |
| **2b** | 14710 + 14710-T → REGEN on 14710's branch → 14711 (stacked) · 14712 + 14712-T (T5c; no shared file with 14710) | GATE → **CI-W3** |
| **3** | 14713 + 14713-T (T5b) → REGEN → 14714 (stacked) | GATE → **CI-W3b** (P-7) |
| **4a** | 14715 + 14715-T (T6 step 1) | GATE → **CI-W4** (AC-230-2 and AC-054-1 green: the condition for 4b, AC-054-6) |
| **4b** | 14716 + 14716-T (T6 step 2) | GATE |
| **D** | D-1 … D-12 from wave 1 on; ADR-043's dual-lens review after the last ruling | the deferred branch if the round overruns (AC-D3) |
| **close** | C-1 … C-5 | the close guard at C-4 |

---

## The merge checklist — every task, paired pass, stack and fix-up

1. **Report read.** Brief contradictions and declared deviations are dispositioned: factual ones by the Orchestrator; a requirement, invariant or ruling question goes to the `adjudicator`.
2. **Build.** `dotnet build` 0 errors; warnings ≤ 145; CA2100 ≤ 115.
3. **Step 5α — `constraint-validator`** on `git diff <base>...<branch>`, scope = the task's authorized files (a paired branch: the union; any `src/**` change in a `-T` pass's own commits is a violation).
4. **Security consult (14703 and 14712 only, the refinement's "security consulted").** A `security` spawn (Sonnet), told it is read-only, reads the brief and the diff and returns the negative-test list and any residual it would register. Saved as `.claude/reviews/S147-TASK-<n>-security-consult.md` before the Orchestrator acts on it; it is input to the Step-5a reviewer, not a lens.
5. **Step 5a — `reviewer` (Fable)** on the branch diff with the pasted brief; one verdict line. **Saved as `.claude/reviews/S147-TASK-<n>-5a-reviewer.md` BEFORE acting on it** (`docs/WORKFLOW.md:32`).
6. **Step 5a — Codex** for the rows marked high-risk (`docs/AGENTS.md:585-595`: JWT/auth and security code, payroll export, legal rule logic, retroactive corrections). **Run it inside the task's worktree, with the commit range named in the prompt** (S146's practice, P-8: the prompt-alone form targets only the working copy's uncommitted diff): `codex exec "<prompt naming <base>..<branch-tip>>" < /dev/null --output-last-message <file>` from the worktree; saved as `.claude/reviews/S147-TASK-<n>-5a-codex.md` (+ `.log`) before acting. If the sandbox blocks its shell, the inlined-source fallback, and the verdict says "performed on inlined source".
7. **Merge**, then GATE on master; the sprint-log row `task · branch · build · 5α · 5a-R · 5a-C · merged sha` and the handoff row are written. **An empty 5α or 5a cell blocks the merge.** Cap: 5 cycles per lens per task; halt-and-prompt after cycle 5 only if it finds new BLOCKERs.

| Task(s) | 5α | Security consult | 5a Reviewer (Fable) | 5a Codex (high-risk) |
|---|---|---|---|---|
| 14700 + 14700-T | ✓ | — | ✓ | ✓ payroll export, retroactive corrections |
| 14701 + 14701-T | ✓ | — | ✓ | ✓ legal rule logic (an entitlement's consumption reaches payroll, SLS_0570) |
| 14702 | ✓ | — | ✓ | — (a vitest only) |
| 14703 + 14703-T | ✓ | ✓ | ✓ | ✓ auth/security (the validator, `src/Infrastructure/**/Security/**`, `src/SharedKernel/**/Security/**`), payroll (`/recalculate`) |
| 14704 + 14704-T | ✓ | — | ✓ | ✓ security (segregation of duties), payroll export |
| 14705 + 14705-T | ✓ | — | ✓ | ✓ security (segregation of duties on a pay action) |
| 14706 + 14706-T, then the 14707 stack | ✓ (union) | — | ✓ | ✓ legal rule logic (compliance checks) |
| 14708 + 14708-T | ✓ | — | ✓ | ✓ legal rule logic, payroll (flex credit) |
| 14709 | ✓ | — | ✓ | — (a dead frontend constant deleted) |
| 14710 + 14710-T, then the 14711 stack | ✓ | — | ✓ | ✓ auth (login), security |
| 14712 + 14712-T | ✓ | ✓ | ✓ | ✓ security (approver authority) |
| 14713 + 14713-T, then the 14714 stack | ✓ | — | ✓ | ✓ security (the R9c allowlist), payroll export |
| 14715 + 14715-T | ✓ | — | ✓ | ✓ payroll export, retroactive corrections |
| 14716 + 14716-T | ✓ | — | ✓ | ✓ payroll export (route retirement) |
| ~~14717 + 14717-T~~ | DROPPED (RR-1) | — | — | — |
| Every fix-up | ✓ | as its parent | ✓ | as its parent |

**GATE (the Orchestrator's post-merge gate; S146 called it "O-1").** Build 0 errors, warnings ≤ 145, CA2100 ≤ 115; Unit; DemoSeed; Regression `Category!=Docker`; all in the foreground with `-m:2` (a backgrounded build can be reaped for memory). When the frontend changed: `npx tsc --noEmit`, `npm run lint`, `npm run typecheck:e2e`, `npm run build` and `npx vitest run` (`docs/WORKFLOW.md:29`; the S146 lint-only CI failure). Exit statuses are read from the unpiped command.

**REGEN (S146's "O-2").** `dotnet run --project src/Backend/StatsTid.Backend.Api -- --openapi`, then (by the stacked `ux` task, or by the Orchestrator when no drift is expected) `npm run gen:api`. Expected drift: 14706 (the compliance result gains a member), 14710 (the project select routes disappear), 14713 (the HRP-011 row gains the manager window). Expected no drift: after 14701, 14704, 14705 and 14708 (RR-4: the served classification is dropped; report any).

---

## Standing constraints — pasted verbatim into every agent prompt

```
★ FIRST, BEFORE ANY WORK: check your worktree against master.
Run `git log --oneline -1` and compare with the main checkout's LOCAL master — not `origin/master`,
which lags by design because this project pushes only at sprint close. Fast-forward if you are
behind, and say in your report what you found.
```
```
Do NOT use `git stash` — it is repository-global, not per-worktree, and will collide with
other agents running concurrently. To prove a RED by reverting a production change:
    git diff > <scratch>/mychange.patch
    git checkout -- <the production files>
    ... run the test, capture the failure output ...
    git apply <scratch>/mychange.patch

RED-FIRST (docs/AGENTS.md § "RED evidence"). Every new or changed fact says in its docstring which kind
it is, and its "Red conditions":
 F  an assertion that FAILS ON TODAY'S CODE. Name that assertion under "Red conditions". Unit: run it
    on master before the implementation is merged and quote the failure. Docker: you cannot run it
    locally; name the production behaviour that makes that assertion fail today. If no assertion
    of the fact fails today, it is NOT F: label it B or R and give it a discriminating assertion.
 N  names a type or member this sprint creates. Unit: prove RED by ONE named mutation at your gate,
    never by reverting: `git worktree add --detach <path outside the repo> master`, apply the
    mutation (keep the API, break the one behaviour), run the fact, capture the assertion failure,
    `git -C <path> checkout -- .`, then `git worktree remove --force <path>` and `git worktree prune`.
    A compile error never counts. Docker: argue it in the docstring as for F.
 B  a boundary pin that holds today. Label it "boundary pin — holds today" so nobody "fixes" it.
 R  a regression pin (an existing behaviour kept). Label it so.
TEST NAMES ARE CONTRACT where a brief gives them; if you must rename, report old → new.

SEEDING THE GRACE STATE. A user who is still is_active = TRUE with employment_end_date < today
(Copenhagen) is a real state: it exists today between Copenhagen midnight and Step A's poll, and
for D+1..D+5 under the grace. Seed it directly where a brief says so; dates are relative to the
host's Copenhagen "today" (CopenhagenBusinessDate.Today), and absences fall on WEEKDAYS (S138).

CENSUS RULE. Where a brief changes a constructor, a signature, a vocabulary or a route, it states the
census COMMAND (`git grep`, so bin/obj never match) and you classify EVERY hit as (a) authorized
for edit (named, with what may change), (b) expected and unchanged (named, one-line reason), or
(c) generated (named; regenerated by REGEN, never hand-edited). Re-run the command and report the
true counts. An unclassified hit is a STOP and report, never an edit.

GREP GATES. Every "grep is empty" gate runs over `src tests frontend/src tools` only. Hits under
docs/**, .claude/** and the sprint logs are expected history.
```
- **`docs/CONVENTIONS.md` verbatim** — the Orchestrator pastes it into every prompt.
- **KB entries:** the ledger's "KB pasted" column; pasted whole.
- **Docker is unavailable locally.** Report Docker facts as **"CI-pending"**; the sprint log flips a fact to "CI-verified" only with the checkpoint run id that showed it green. The local gate is build + Unit + DemoSeed + Regression `Category!=Docker` (+ `npx tsc --noEmit`, `npm run lint`, `npm run typecheck:e2e`, `npm run build`, `npx vitest run` for frontend work), in the foreground, `-m:2`.
- **Read exit status from the unpiped command** (S140).
- **Never edit a generated file by hand:** `docs/api/openapi.json`, `frontend/src/lib/api-types.ts`, `docs/generated/db-schema.md`. **No agent edits `docs/**`** (Orchestrator-only).
- **No employment date in any employee-facing response body, error body or DTO** (ADR-040 D7). HR-scoped admin reads carry one only where a ruling says so — in S147 only HRP-011's leaver row (R-27, TASK-14713). Every 403 this sprint adds carries a date-free reason; the server log names the clause.
- **"Today" is read once per handler**, from the injected `TimeProvider` through `CopenhagenBusinessDate.Today` (PAT-028), and passed down.
- **Report anything the brief does not name** (an enumeration site, a caller, a test, a harness copy).
- **Contract vs. hint.** A "*Hint (HOW)*" is one way; the contract is the WHAT. Deviate from a hint freely and say so; from a contract only by reporting.
- **Brief challenge:** if the code contradicts the brief, go by the code and report it (claim · what the code shows · file:line). A contradiction is a report, never a permission.
- **Paired passes (`-T`) only:** you are on another agent's branch. Edit only the listed files and **never `src/**`**; a production defect is reported, not fixed. Change an expected value only where the brief lists it as intentional, labelled `// S147 <ruling>:`; any other value that changes is a **STOP and report**.

---

## Task ledger

*Each brief is `.claude/plans/s147/TASK-<id>.md`; paste it whole, with the standing constraints, the refinement lines it names (verbatim) and the KB entries in the "KB pasted" column. The sprint log carries a "## Task ledger" section with DONE / CUT / DEFERRED / DROPPED for every TASK id below (close guard, `docs/WORKFLOW.md:47`).*

| Task | Wave | Role (model by definition) | What | Authorized files | KB pasted | Depends on | Size |
|---|---|---|---|---|---|---|---|
| **[TASK-14700](s147/TASK-14700.md)** | 1 | `payroll-integration` (Opus) | T1, QUAL-229: `DeriveLink` factored out of `Derive`; one ascending walk from S to P; `payroll-prior-month-stale`; `WindowsReadRange(month, ledgerStart)` | `…/Payroll/Services/FlexLedgerOpening.cs`, `…/FlexLedgerRefusalException.cs`, `…/PeriodCalculationService.cs` (the two callers only) | ADR-034 D3–D5; ADR-016 D10 | G-0 | L |
| **[TASK-14700-T](s147/TASK-14700-T.md)** | 1 (paired) | `test-qa` (Sonnet) on 14700's branch | AC-229-1..7: the walk's Unit matrix (N), the host facts (F), the race; the existing fixtures under the walk; the chain census re-run | listed in the brief | ADR-034 D3–D5 | 14700 returned | M–L |
| **[TASK-14701](s147/TASK-14701.md)** | 1 | `backend-infrastructure` (Opus) | T2, QUAL-235: the Skema row, its config gate, the admin visibility list, one shared label map | `…/Endpoints/SkemaEndpoints.cs`, `…/Endpoints/ConfigEndpoints.cs`, new `…/Services/AbsenceTypeLabels.cs` | ADR-033; ADR-032 D3, D5 | G-0 | S |
| **[TASK-14701-T](s147/TASK-14701-T.md)** | 1 (paired) | `test-qa` on 14701's branch | AC-235-1..5, -7 | listed in the brief | ADR-033 | 14701 returned | S |
| **[TASK-14702](s147/TASK-14702.md)** | 1 | `ux` (Sonnet) | AC-235-6: the row renders (vitest); report whether any component change is needed | listed in the brief | — (`docs/FRONTEND.md`) | G-0 | XS |
| **[TASK-14703](s147/TASK-14703.md)** | 2a, **merges first** | `backend-infrastructure` — **`Backend + Infrastructure (extended into Infrastructure/Security, SharedKernel/Security, and the Payroll and Orchestrator hosts' Program.cs, cross-domain authorized)`** | T5a's departed-self rule (R-30, R-31) and the owner's sweep (F1–F4, P-1): `DepartedSubject.IsDeparted`; the validator clause; `TimeProvider` in both hosts; thirteen inline guards (users PUT, unit PUT, roles grant/revoke, reporting-line import, unit delete (pre-check), payroll `/recalculate`, delegate set/clear, admin vikar set/clear when `managerId` = actor, unit leaders add/remove) and `/remove`'s derived guard | listed in the brief (the census table is there; 32 / 19 / 33) | ADR-040 D3, D7; ADR-007; PAT-028; RES-003; FAIL-001 | CI-W1 green | **L** |
| **[TASK-14703-T](s147/TASK-14703-T.md)** | 2a (paired) | `test-qa` on 14703's branch | The seven validator fixtures; AC-230-16 (both overloads) and the three host-start facts; AC-230-5's elevated routes (F3: LocalAdmin for grant/revoke) and its LocalHR self-service facts incl. the leader-arm reopen; AC-230-17 (F1); AC-230-18 (sixteen facts, 10 F / 6 B; P-1, P-10); the two S136 pins flipped as intentional (P-12); the predicate's Unit facts | listed in the brief | ADR-040 D3 | 14703 returned | **L** |
| **[TASK-14704](s147/TASK-14704.md)** | 2a | `backend-infrastructure` — **`Backend + Infrastructure (extended into Payroll PayrollExportService.cs and tools/StatsTid.DemoSeed, cross-domain authorized)`** | T4, SEC-053 (OQ-1 = (a)): the sender guard at the endpoint, in the transaction and at payroll; the demo loader's second signer | listed in the brief | RES-003; ADR-034 D1 | CI-W1 green | M |
| **[TASK-14704-T](s147/TASK-14704-T.md)** | 2a (paired) | `test-qa` on 14704's branch | AC-053-1..6; the census of tests that send and approve as one actor | listed in the brief | RES-003 | 14704 returned | M |
| **[TASK-14705](s147/TASK-14705.md)** | 2a, merges after 14703 | `backend-infrastructure` | T9, SEC-056's self part (Q-A = (a)): approve, reject and compensate refuse a self target, before the status check | `…/Endpoints/OvertimeEndpoints.cs` (three handlers), `…/Endpoints/Helpers/ApprovalSelfGuard.cs` | RES-003 | CI-W1 green | S |
| **[TASK-14705-T](s147/TASK-14705-T.md)** | 2a (paired) | `test-qa` on 14705's branch | AC-056-1..3 | listed in the brief | RES-003 | 14705 returned | S |
| **[TASK-14706](s147/TASK-14706.md)** | 2a | `rule-engine` — **`Rule Engine (extended into SharedKernel/Models/ComplianceCheckResult.cs, SharedKernel/Segmentation/ComplianceCheckResultMerger.cs and Backend ComplianceEndpoints.cs's result construction, cross-domain authorized)`** | T7, QUAL-234 interim (OQ-4 = (b)): the rest rule reports the days it could not analyse; the period compliance route serves them | listed in the brief | ADR-039; ADR-016 D10 | CI-W1 green | S–M |
| **[TASK-14706-T](s147/TASK-14706-T.md)** | 2a (paired) | `test-qa` on 14706's branch | AC-234i-1 (server half) and the marker-unchanged assertion | listed in the brief | ADR-016 D10 | 14706 returned | S |
| **[TASK-14707](s147/TASK-14707.md)** | 2a (stacked on 14706 after REGEN) | `ux` | T7's notice on `ComplianceWarnings` and Teamoversigt; vitest | listed in the brief | — | REGEN on 14706's branch | S |
| **[TASK-14708](s147/TASK-14708.md)** | 2a | `rule-engine` — **`Rule Engine (extended into SharedKernel Models/Segmentation (incl. the new `Models/AbsenceClassification.cs`, P-14), the Backend absence-code sites and one Payroll comment, cross-domain authorized)`** | T3a, QUAL-231: one vocabulary (the stored spelling), a total classification in SharedKernel (P-14), 422 for an unknown code, **`SICK_DAY` credited for every agreement (T3b folded in, RR-1; AC by the labelled default, Q-1 in D-8)**, the marker to `weekly-timeline-v2` (one move); the variant gating `SkemaEndpoints.cs:46-58` unchanged (QUAL-236 is separate, RR-10); nothing served to the screen (RR-4) | listed in the brief | ADR-016 D10; ADR-014; PAT-005 | T1 and T2 merged; CI-W1 green | M |
| **[TASK-14708-T](s147/TASK-14708-T.md)** | 2a (paired) | `test-qa` on 14708's branch | AC-231-1..8; the literal census; the 10 marker test files | listed in the brief | ADR-016 D10 | 14708 returned | M–L |
| **[TASK-14709](s147/TASK-14709.md)** | 2a (from master; RR-4) | `ux` | `npm run gen:api` (expect no drift); delete the dead `ABSENCE_TYPES` copy (`frontend/src/types.ts:37-45`) | listed in the brief | — | CI-W1 green | XS |
| **[TASK-14710](s147/TASK-14710.md)** | 2b | `backend-infrastructure` — **`Backend + Infrastructure (extended into tools/openapi-bodyless-declared.txt, cross-domain authorized)`** | T5a's grace: one constant and both deactivation paths; login refused from D+1; the self-service write floor (R-21, R-22, R-24) at every site; K-3; project select/deselect retired (R-23, R-33); the R9c banner | listed in the brief | ADR-040 D3, D7; ADR-033; PAT-028 | CI-W2 green | L |
| **[TASK-14710-T](s147/TASK-14710-T.md)** | 2b (paired) | `test-qa` on 14710's branch | AC-230-1, -3, -4, -5 (Employee self-service, LocalHR self-only routes, project 404, others), -14; the floor's Unit truth table (M-9); the K-8 census; the retired routes' tests | listed in the brief | ADR-040 D3 | 14710 returned | L |
| **[TASK-14711](s147/TASK-14711.md)** | 2b (stacked on 14710 after REGEN) | `ux` | `npm run gen:api`; the typed-overload test edits for the retired routes | listed in the brief | — | REGEN on 14710's branch | XS |
| **[TASK-14712](s147/TASK-14712.md)** | 2b | `backend-infrastructure` | T5c (NR-10 (ii)): "acting user = active AND end date not passed" at the five authority reads; the stand-in census item: no change (P-2; owner Q-B = (c)) | listed in the brief | ADR-040 D3; ADR-038 D4; RES-003 | CI-W2 green | M |
| **[TASK-14712-T](s147/TASK-14712-T.md)** | 2b (paired) | `test-qa` on 14712's branch | AC-230-15 incl. the census item | listed in the brief | ADR-040 D3 | 14712 returned | M |
| **[TASK-14713](s147/TASK-14713.md)** | 3 | `backend-infrastructure` — **`Backend + Infrastructure (extended into the Payroll host's Program.cs, cross-domain authorized)`** | T5b: HR only from D+6 on approve, reject, reopen's leader arm, the Skema HR tier and `/calculate-and-export`; the terminated-inclusive tree-lock overload; the bound after the gate; the HRP-011 row's manager window | listed in the brief | ADR-040 D3, D7; ADR-034 D1; RES-003 | 14710 stack, 14712, 14704 merged; CI-W3 green | L |
| **[TASK-14713-T](s147/TASK-14713-T.md)** | 3 (paired) | `test-qa` on 14713's branch | AC-230-2, -6 … -13 | listed in the brief | ADR-040 D3 | 14713 returned | L |
| **[TASK-14714](s147/TASK-14714.md)** | 3 (stacked on 14713 after REGEN) | `ux` | HRP-011's leaver rows: open-month and send on every row; approve and reject where a period exists; "manager until …, HR only from …"; vitest (AC-230-9, -10) | listed in the brief | — | REGEN on 14713's branch | S–M |
| **[TASK-14715](s147/TASK-14715.md)** | 4a | `payroll-integration` | T6 step 1 (OQ-3 (i)): case 3a pays untracked; zero lines write no record | listed in the brief | ADR-034 D3, D5; ADR-016 D10 | 14713 merged | M |
| **[TASK-14715-T](s147/TASK-14715-T.md)** | 4a (paired) | `test-qa` on 14715's branch | AC-054-1, -2; the `payroll-export-out-of-order` census | listed in the brief | ADR-034 D3 | 14715 returned | M |
| **[TASK-14716](s147/TASK-14716.md)** | 4b | `payroll-integration` — **`Payroll Integration (extended into src/Orchestrator, cross-domain authorized)`** | T6 step 2: the raw routes and the dead Orchestrator task type deleted; `ExportAsync` requires a period | listed in the brief | ADR-034 OQ-6, D3; DEP-004 | CI-W4 green (AC-054-6) | M |
| **[TASK-14716-T](s147/TASK-14716-T.md)** | 4b (paired) | `test-qa` on 14716's branch | AC-054-3 … -5; the deleted facts with their guards | listed in the brief | ADR-034 | 14716 returned | M |
| **[TASK-14717](s147/TASK-14717.md)** | — | `rule-engine` | **DROPPED (RR-1):** R1 landed before TASK-14708's dispatch, so T3b folded into TASK-14708 (one branch, one marker move). The brief is kept as the record | — | — | — | — |
| **[TASK-14717-T](s147/TASK-14717-T.md)** | — | `test-qa` | **DROPPED (RR-1)** with TASK-14717; AC-231-8 is satisfied in TASK-14708-T | — | — | — | — |

*Conditional tasks, not in the ledger until needed (each gets a sprint-log disposition like every other id once it exists):*
- **TASK-14701-F**: **not built: R2 CONFIRMED** ADR-032 D3 (RR-6; Aftale om ferie § 13 stk. 2), so the SPECIAL_HOLIDAY seed stays `false`.
- **TASK-14709-N**: **not built: RR-4** (R1 is applied as CONFIRMED, every code is credit or reduction, and the screen's rule equals the engine's; P-3's premise is gone).
- **TASK-14712-F and TASK-14710-F** (the stand-in rules P-2 sketched for owner answers (a), (b) or (d)): **not built: Q-B = (c).**

*Orchestrator rows (no agent):* **GATE** after every merge (above). **REGEN** contract regeneration (above). **R** the research pass. **D-1 … D-12** the decide track. **O-1 … O-12** the docs items. **CI-W1 … CI-W4, C-1 … C-5** below.

*Two declared deviations from the refinement's task table (`REFINEMENT…:563-577`), for the adjudicator at Step 0b:* (1) **T5a is split by dependency** into 14703 (the departed-self rule and the sweep: a foundation every later leaver task builds on) and 14710 (the grace). The refinement's T5a scope is unchanged; only its delivery is two tasks with a CI checkpoint between them. (2) **T7 is one `rule-engine` task with a cross-domain extension** plus a stacked `ux` task, because the change is one result shape travelling from the rule to the screen (`src/SharedKernel/StatsTid.SharedKernel/Models/ComplianceCheckResult.cs:3`; `…/Segmentation/ComplianceCheckResultMerger.cs:32`), which the refinement's role list (`:444`) did not name. A third deviation, on where two AC-230-5 facts live and how their RED is shown, follows from (1); see NR-2.

---

## The departed-self sweep (F4, with P-1) — summary; the full census is in TASK-14703

*Plain language.* The owner asked for proof that "a person who has left cannot change their own record" holds on **every** door, not just the ones the reviews happened to find. So every route in the three hosts that changes data was listed and sorted into three groups by one rule.

**The sorting rule (P-1, the subject key).** A route is guarded when the departed actor's own id is the *subject key* of any employee-keyed row it inserts, updates or closes — from the route, the body, or derived inside the transaction. The subject keys are `users.user_id`, `role_assignments.user_id`, `unit_leaders.user_id`, `reporting_lines.employee_id` and `manager_vikar.absent_approver_id`. An id that appears only as the **holder** of authority over someone else (`reporting_lines.manager_id`, `manager_vikar.vikar_user_id`, a replacement in `/remove`'s map) is classified out: TASK-14712's acting-user rule makes a departed holder's authority inert from D+1 at every read that honours it. A guard inside a transaction fires before that transaction's first write. **Consequence clause (P-10):** a row whose subject key is the actor is classified out when its write is forced by a permitted write to **another** person and the row could not have survived that write whichever way — the two derived closures of `manager_vikar` in `src`: `/remove` Step 4 (`ReportingLineEndpoints.cs:1589-1595`, `:3045`) and a cross-organisation transfer in the users PUT (`AdminEndpoints.cs:2455` → `ManagerVikarRepository.cs:353-356`), when the person removed or moved is the departed actor's own stand-in. Two boundary facts pin it. **Q-C = (b):** the owner kept this route-by-route design; the front-door write gate (P-11's option (c)) was declined and nothing from it is built.

**The command** (reproducible at Step 0b): `git grep -n -E "\.Map(Post|Put|Delete|Patch)\(" -- src` → **92** write routes: Backend 78, Payroll 5, Orchestrator 1, plus External 1 and Rule Engine 7 (outside the three hosts; classified out below for completeness).

| Group | Count | What closes it |
|---|---|---|
| **A. Reach the employee through the validator's employee overloads** | **32** (Backend 30, Payroll 1, Orchestrator 1) | The R-30 clause inside both overloads (elevated actors). Where an Employee token can reach the route, the self-service floor (14710) as well: send, employee-approve, reopen's employee arm, Skema save, time entries. **One route also gets a derived guard (P-1 (a)):** `POST /api/admin/reporting-lines/{employeeId}/remove` validates the *removed* manager but rewrites every incoming report's own line, so an actor among those reports is refused before the transaction's first write. Orchestrator `/execute` writes only its own task log, so an Employee-only self there writes no employee record |
| **B. Do not, and can write a row whose subject key is the actor → guarded** | **19** | **Inline predicate (13):** `PUT /api/admin/users/{userId}`, `POST /api/admin/roles/grant`, `POST /api/admin/roles/revoke` (F2), `PUT /api/admin/users/{userId}/unit`, `POST /api/admin/reporting-lines/import`, `DELETE /api/admin/units/{id}` (a pre-check before the first write), Payroll `/recalculate` (F1), `POST`/`DELETE /api/reporting-lines/delegate`, `POST`/`DELETE /api/admin/reporting-lines/{managerId}/vikar` when `managerId` = actor, `POST /api/admin/units/{id}/leaders` and `DELETE …/leaders/{userId}` when the leader = actor. **Self-service floor (2):** compensation-choice, row-preferences. **Retirement (4):** project select and deselect (14710); Payroll `/export` and `/export-period` (14716; if the owner cuts 14716, the inline predicate goes into both handlers in the cut's fix) |
| **C. Do not, and are classified out** | **33** | **No employee subject (31):** organisations ×4, agreement configs ×5, agreement entitlements ×3, config ×2, entitlement configs ×3, position overrides ×4, project create/update/delete ×3, unit create/update/move ×3, wage-type mappings ×3, Payroll `/export-corrections` (formatting only). **Cannot address an existing person (1):** `POST /api/admin/users` (an existing id answers 409, `AdminEndpoints.cs:919-927`; its optional approver is a holder). **Anonymous (1):** login, which 14710 refuses for a departed user. *Every group-C reason holds whatever the stand-in policy is (owner Q-B = (c))* |

**Counts (P-1; unchanged by P-10): 84 = 32 (A) + 19 (B: 13 inline, 2 self-service floor, 4 retirement) + 33 (C).** Holder notes in the census mark the ids that stay allowed: the manager in an assign, the acting manager, an import row's `managerId`, `/remove`'s replacement, a stand-in named as `VikarUserId`, `users` POST's approver.

Also out, beyond the three hosts: External `/api/external/send` (forwards an envelope; no StatsTid employee record, `src/Integrations/StatsTid.Integrations.External/Program.cs:46-54`) and the Rule Engine's seven pure POSTs. **Already self-refusing** (in group A, closed twice): the employment start- and end-date PUTs (`EmploymentDateEndpoints.cs:154`, `:452`) and the reversal's end-date arm (`SettlementReversalEndpoints.cs:127`).

---

## Research pass (R) — done; verdicts adjudicated (RR-1 … RR-13)

*Plain language.* Five questions had no verified source. A research agent answered each with a quoted primary source; an independent second agent tried to break each answer; the adjudicator ruled on how each verdict shapes the sprint (`.claude/reviews/RESEARCH-s147-adjudication.md`, RR-1 … RR-13). **The verification governs** where the two passes differ (`.claude/refinements/RESEARCH-s147-VERIFICATION.md` over `RESEARCH-s147.md`).

| Cell | Question | Verdict (verification) | What it changes (ruling) |
|---|---|---|---|
| **R1** | Does a paid sick day count toward the norm in state flex, and is the credit the registered hours or the day's norm? The child's sick days likewise | **PARTLY.** HK and PROSA are sourced: Statens arbejdstidsaftale § 7 nr. 2 and its remark (022-26, the same text as 014-21): a paid sick day or child's sick day counts with the day's **planned hours**, else 7.4 h × fraction, never the hours worked. AC-agreement staff: **no central text** (PAV 19.3.4). A partial sick day: max(worked, planned), MEDIUM confidence, open to local variation (022-26 § 3) | **RR-1:** applied as CONFIRMED for every agreement StatsTid encodes; AC by analogy, a **labelled default** the owner confirms inside D-8 (**Q-1**, lean (a) = this default, which S147 builds). T3b folds into TASK-14708 (one branch, one marker move); TASK-14717/-T DROPPED. **RR-2:** "planned hours" is StatsTid's per-day norm (`DayNorm.ForWeekday(WeeklyNormHours, fraction)`); the credit is the stored sick hours, already capped at it by the Skema save; T3b is one entry in the credit classification. **RR-3:** one partial-day consistency fact; the over-credit case is a QUAL-231 residual. **RR-4:** no served classification; TASK-14709-N not built. **RR-5:** AC-231-1/-2/-8 reworded |
| **R2** | Re-verify ADR-032 D3's "brøkdele af dage" (`ADR-032:37`), with its quote | **CONFIRMED:** Aftale om ferie § 13 stk. 2 (021-24 / CIR1H 9298). Nuance: days the **employer** imposes are whole days only (Ferievejledning Dec 2019) | **RR-6:** T2 stands (the seed stays `false`); TASK-14701-F not built; O-3 cites the source and states the nuance in one sentence (no code); the seniordage source (Medst. 035-24) is recorded with the decide-track record |
| **R3** | On-call standby, call-in and non-working travel as working time; on-call's rate | **CONFIRMED,** per agreement: HK/PROSA travel counts up to 13 h per working døgn; home on-call counts 1/3, on-call at the workplace 3/4; call-in at least 3 h, with its conditions; AC-agreement staff: travel only | **RR-7:** D-3's option (a) stays open, with this table in its brief; the sourced model is a **fraction of hours**, not `OnCallDutyRule`'s rate; the EU rest-check flag is a separate classification (a D-2 input) |
| **R4** | Has the implementing text for the 4 February 2026 part-timer award appeared? | **CONFIRMED not yet** (as of 28 Oct 2026; the MedSt list through 068-26) | **RR-8:** D-10's brief carries the evidence and two citation fixes (FV2025-795's Annex A names the agreement, not 022-26; FV2026.573's five-year limitation is general law, a private-sector award) |
| **R5** | The OK24 texts of § 13 and § 14 (`ADR-043:206-210`) | **CONFIRMED:** §§ 13–14 are token-identical between 014-21 and 022-26 | **RR-9:** one supplement-window set and one § 14 rule, no OK-version split; D-2 still settles the clock-time mechanism |
| *(found in passing)* | The child's-sick-day quotas | The encoded quotas (`docs/references/danish-agreements.md:148-150`: AC 1, HK 2, PROSA 3 per episode, reset yearly) are wrong: **OK24** AC 2, HK 2, PROSA 2; **OK26 from 1 Apr 2026** HK and PROSA 3 plus the remaining hours of the call-home day, AC settled the same with the circular pending; no annual cap; calendar days from the first whole sick day; discretionary | **RR-10: QUAL-236 registered (High)**; the docs are corrected in S147 (O-9); the code fix (quotas and catalogue per OK version; the per-episode limit enforced per episode, today per year: `SkemaEndpoints.cs:1148`, `:1644` pass `perEpisodeLimit = null`) is routed to **S148 stage 0** by the owner's **Q-2 = (a)** (2026-10-28: the complete fix, including the call-home day; no S147 task). TASK-14708 leaves the per-agreement variant gating (`SkemaEndpoints.cs:46-58`) unchanged |

- **Who ran it:** a first-pass `general-purpose` agent (`model: opus`, web search) and an independent adversarial verifier (`general-purpose`, `model: opus`), per `.claude/plans/s147/R-RESEARCH.md`. Both wrote only under `.claude/refinements/`.
- **AC-R:** every cell has a verdict, a quoted primary source and the verifier's agreement or dissent; R2 was framed as a re-verification. The dossier is filed at O-9 and `check_docs.py` must be green in CI.
- **Filing (O-9, RR-11):** below in the O-table.

---

## D — the decide track (Orchestrator + adjudicator + owner)

*Plain language.* S146's research showed the overtime/flex model is wrong in kind; ADR-043 sets the direction (flex by default, overtime only when ordered, a monthly norm). Nine design forks remain in ADR-043, and three tender findings belong in the same round. The model is built in S148; S147 settles it.

**How each question runs (`REFINEMENT…:494-499`).**
1. **The brief.** The `planner` drafts a one-page brief, plain language first: the question, the options with what each gives up, the lean, the research it waits on, and the ADR-043 text it will rewrite. File: `.claude/plans/s147/decide/D-NN-<slug>.md`.
2. **Adjudication.** The `adjudicator` (Fable) confirms or replaces the lean and settles the question's wording; saved as `.claude/reviews/S147-D-NN-adjudication.md` before the ask.
3. **The ask.** The Orchestrator explains the question in prose, then asks it **alone** (one AskUserQuestion per question; never batched).
4. **The record.** The ruling goes **verbatim** into the `SPRINT-147.md` ledger and into the working copy `.claude/plans/s147/ADR-043-working.md` (planner-maintained, seeded from `docs/knowledge-base/decisions/ADR-043-overtime-flex-monthly-norm.md`).
5. **Research gates.** A question that rests on a research cell waits for its verdict.

**The order (`REFINEMENT…:503-516`; new questions just before the first fork that depends on them).** Before D-1: **O-1**, the four drift fixes (`ADR-043:52`; QUAL-227's "every *timed* hour"; `ADR-043:291-293`; `ADR-043:126-128`).

| # | Question | Lean (adjudicator confirms) | Waits on |
|---|---|---|---|
| D-1 | **F-1** One cut-over or stages | (b) | — |
| D-2 | **QUAL-234** How Skema's clock times reach payroll and the rest check — (a) read work intervals: **an architecture change** to ADR-016 D10's hours contract and digest, with a marker move; (b) invented times; (c) intervals per project; sub-question: manual hours. T7 is the interim. *Brief additions (RR-9, RR-7):* §§ 13–14 are identical in OK24 and OK26, so there is one supplement-window set and one § 14 rule, no OK-version split (R5); the EU rest-check flag (whether a type of time counts for the 11-hour rest) is a separate classification from "counts as worked time" | (a) | R5 ✓ |
| D-3 | **QUAL-232** Which time counts as worked time — (a) a closed, sourced set; (b) the interim refusal; (c) both. (b) is S148 stage 0 with its mechanism settled (R-18). *Brief addition (RR-7), R3's per-agreement table:* HK/PROSA travel counts up to 13 h per working døgn; home on-call 1/3, on-call at the workplace 3/4; call-in at least 3 h, with its conditions; AC-agreement staff: travel only. The sourced model is a **fraction of hours**, not `OnCallDutyRule`'s rate; option (a) stays open | (c) | R3 ✓ |
| D-4 | **F-2** The marking workflow. **SEC-056 enters here:** the S148 workflow needs creator-not-approver (a creator column, reseed) and keeps T9's self-decision guard; ADR-043's decision text records it. *Brief addition (RR-12):* § 14 applies to ordered or rostered work (R5) | (c) | D-2, D-3 |
| D-5 | **F-3** The overtime account. **SEC-056 here too:** compensation only behind an approved movement | (b) | D-4 |
| D-6 | **QUAL-233** (with QUAL-204) Does payroll read what the admin editors write — (a) as `SYSTEM_TARGET.md` §I and §G require; (b) and (c) would change requirements (BC-4). Sized as its own stage (`REFINEMENT…:518-524`) | (a) | — |
| D-7 | **F-4** Office hours. *Brief addition (RR-2, RR-12):* per-weekday office hours become "the hours fixed for the day", and the sick-day credit follows them (PAV 19.5.13), so F-4 changes what a sick day is worth | (b), off by default | D-6 |
| D-8 | **F-5** Local flex parameters; the leaver's last closing; QUAL-219; QUAL-225 (also T6's residual). *Brief additions (RR-12, RR-3):* a **tenth local parameter, the sick-day credit basis** (022-26 § 3 lets a local agreement depart from § 7; default § 7 nr. 2); **owner question Q-1 inside D-8 — AC's sick-day credit default:** (a) apply the HK/PROSA rule as the default (**S147 builds this as the labelled default**, TASK-14708), (b) no default (payroll refuses until configured), (c) credit nothing; lean (a); and **QUAL-231's residual** (a partial sick day that over-credits), an S148 candidate validation | (a) | D-6 |
| D-9 | **F-6** AC's period. *Brief addition (RR-12):* R1's AC finding corroborates lean (a): AC working time is "ikke nærmere fastlagt" (PAV 19.3.4; `RESEARCH-s147-VERIFICATION.md:29`, `:57`) | (a) | — |
| D-10 | **F-7** The part-timer default — **labelled a provisional owner default, pending a qualified expert**. *Brief addition (RR-8):* the award's implementing text has not appeared (as of 28 Oct 2026; MedSt list through 068-26); FV2025-795's Annex A names the agreement (not 022-26); FV2026.573's five-year limitation is general law in a private-sector award | (c) | R4 ✓ |
| D-11 | **F-8** Pay ceiling; PROSA 3–4 | (b) | — |
| D-12 | **F-9** Customary days off | (c) | D-8 |

**Also from the research (RR-12, RR-6):** ADR-043's F-5 text gains one sentence naming the sick-day credit basis as a local parameter (default § 7 nr. 2). The seniordage source (Medst. 035-24: whole days as the starting point, half days or single hours by agreement; StatsTid's hours-based SENIOR_DAY consumption is lawful only where a part day is agreed) is recorded with the decide-track record for the owner's later use.

**What each ruling produces:** the ruling verbatim; ADR-043's fork rewritten as a decision (QUAL-234 → D10, QUAL-232 → D11, QUAL-233 → ADR-014 and, under (a), ADR-016 D10); register updates; S148 items placed in stages; any HRP row owed.

**The track's output.**
- **ADR-043 amended**, then **one dual-lens review in S147** (NR-5): `reviewer` (Fable) and Codex on the amended ADR, artifacts `.claude/reviews/S147-ADR-043-{reviewer,codex}.md`, cap 5 per lens; then **ACCEPTED** (O-10).
- **The S148 build input**, `.claude/plans/s147/S148-BUILD-INPUT.md` (planner-drafted, adjudicator-checked): stages, D-6 (a)'s placement as its own stage, and the QUAL-232 interim as stage 0.
- **SYSTEM_TARGET text** owed by any ruling goes to the owner (CLAUDE.md: Orchestrator approval required).
- **The deferred branch:** if the round cannot finish before C-1, the sprint log records every ruling given and the questions remaining; ADR-043 stays PROPOSED with the decided forks recorded; ROADMAP § 1 points S148's opening at the remaining questions (AC-D3).

---

## Orchestrator docs items (O; `docs/**` is Orchestrator-only)

| # | When | Item |
|---|---|---|
| O-1 | Wave 1, before D-1 | The four drift fixes (above) |
| O-2 | After 14700 | QUAL-229's row (the one-link route superseded by R-2, then closed); HRP-003; ADR-034 D4 |
| O-3 | After 14701 (R2 CONFIRMED) | QUAL-235 closed; ADR-033 note; `danish-agreements.md`'s taking unit with R2's quote and citation: **Aftale om ferie § 13 stk. 2 (021-24 / CIR1H 9298)** — særlige feriedage may be taken in part days — plus one sentence (no code): days the **employer** imposes are whole days only (Ferievejledning Dec 2019) (RR-6) |
| O-4 | After 14708 | QUAL-231 closed, **with a residual note (RR-3):** a partial sick day reads as max(worked, planned) (MEDIUM, open to local variation under 022-26 § 3); StatsTid credits the stored sick hours, which can over-credit when worked + sick exceeds the day's norm — an S148 candidate validation beside D-8. **The sickness row** (`danish-agreements.md`): a paid sick day and a child's sick day are credited with the day's planned hours, never the hours worked — HK and PROSA by Statens arbejdstidsaftale § 7 nr. 2 and its remark (022-26 = 014-21); AC-agreement staff by analogy, a labelled default pending the owner's Q-1 (D-8; PAV 19.3.4 has no central text); civil servants are not modelled. ADR-016 D10's marker footnote (`weekly-timeline-v2`, the one move) |
| O-5 | After 14704 | SEC-053 closed; `SECURITY.md` § "Flat approval authority"; `SYSTEM_TARGET.md` §H (approved via OQ-1); the tender row EM A1 (e) (O-12) |
| O-6 | After 14713 (all of T5 merged) | QUAL-230 closed. **The ADR-040 amendment** (`REFINEMENT…:392-399`, plus the sweep: the inline guards and `/recalculate`). The amendment also records (P-2, owner Q-B = (c)): "a leaver's active stand-in survives the leaver's deactivation until its end date; HR revokes or reassigns (`/remove`)". **The HRP-014 row** (stand-in cover) gains the same sentence: a departed manager's stand-in keeps signing until its end date, and HR revokes or reassigns it. **The amendment also names the superseded S136 Step-5a (a) residual (P-12):** a lifecycle-deactivated HR actor's still-valid token may no longer register or send their own facts (OQ-2; the two S136 pins flipped in TASK-14703-T). **SEC-046's row** gets the S147 note: closed for every role at the self-service sites and inside the validator (R-21, R-30). The amendment records Q-C = (b): the route-by-route design, no front-door write gate. HRP-011: the grace, HR actions, the hand-over date, aging unchanged. `SECURITY.md`: the leaver's access end, the allowlist surfaces, the org-scope departed-self paragraph, and the sweep's census summary (the subject-key rule; 32 / 19 / 33). **SEC-003's row** (own data closed centrally; the residual is other people's data; the grace does not widen it). The settlement comments (K-2). The R9c banner checked. SEC-055 stays registered |
| O-7 | After 14716 | SEC-054 closed; QUAL-183's follow-up; ADR-034 OQ-6 and D3 with an S146 R-14 trail note; QUAL-225; HRP-003; SEC-048; SEC-023; DEP-004; a register candidate for case 4b's raw arm |
| O-8 | After 14706/14707 | QUAL-234's row: the interim is in place, the real fix pending D-2. QUAL-232's row: the interim mechanism recorded for S148 stage 0 |
| O-9 | Now (R is adjudicated) | **The research dossier (RR-11):** filed as `docs/references/sick-day-credit-and-agreement-texts-research.md` (proposed name), **Part 1 the verification, Part 2 the first pass verbatim**, no `anchor-sprint` marker (a dossier, like S146's), with the corrections RR-11 lists applied. **The CLAUDE.md row** (Historical & research dossiers): (reworded from RR-11's text; the ruled wording is in `.claude/reviews/RESEARCH-s147-adjudication.md` RR-11) "S147 verified research (R1–R5): a paid sick day and a child's sick day count with the day's planned hours for HK/PROSA (Statens arbejdstidsaftale § 7 nr. 2), AC by analogy pending the owner; særlige feriedage may be taken in part days (Aftale om ferie § 13 stk. 2); on-call, call-in and travel as fractions of hours per agreement; the 2026 part-timer award's implementing text not yet issued; OK24 §§ 13–14 identical in OK26; the child's-sick-day quotas corrected (QUAL-236)". **The docs half of QUAL-236 (RR-10):** `danish-agreements.md:148-150`'s child-sick rows corrected per OK version (OK24: AC 2, HK 2, PROSA 2; OK26 from 1 Apr 2026: HK and PROSA 3 plus the call-home day, AC the same with its circular pending; no annual cap; calendar days from the first whole sick day; discretionary) |
| O-10 | After D | ADR-043 ACCEPTED, with SEC-056 in the F-2 and F-3 decisions and the F-5 sick-day-credit sentence (RR-12); ADR-014 and ADR-016 D10 per D-6; QUAL-232/233/234 routes; **QUAL-236's route** (S148 stage 0, owner Q-2 = (a), 2026-10-28); Q-1's answer on AC's sick-day default; ROADMAP § 1 |
| O-11 | After 14705 | SEC-056's row: Q-A = (a); the self part closed; creator-not-approver routed to S148 (D-4/D-5). `SECURITY.md` § "Flat approval authority" names the overtime guard beside SEC-009 and SEC-053 |
| **O-12** (plan-added) | At C-1 | **The tender analysis re-rated where the build changed a rating** (`docs/references/tender-requirements-analysis.md`): the defect table `:91-98`; EM A1 (e) `:145` (SEC-053, SEC-056); EM A33 `:163` and VD 33 `:181` (QUAL-230); the summary bullets `:189-190`; any row the build moved, each with the sprint's evidence. A row the build did not change is not touched |

---

## Acceptance criteria → verifying test → task

*Kind: F fails on today's code · N needs the new API (Unit: one local mutation at the gate) · B boundary pin, holds today · R regression pin. "CI" = Docker-gated, "CI-pending" until a checkpoint run shows it green. Test names are contract; a renamed test is reported old → new.*

| Criterion (refinement rev 4.1 + the cap folds) | Verified by | Kind | Task |
|---|---|---|---|
| **AC-229-1** March's first export after January is corrected → 409 `payroll-prior-month-stale` naming February, nothing written | `FlexLedgerStaleChainHostTests.MarchFirstExport_AfterJanuaryCorrected_Refuses409_NamingFebruary_NothingWritten` | F (CI) | 14700-T |
| **AC-229-2** the prefix shape: May names April (re-derives `REHIRE 0` against its recorded `CHAINED`); February and March skipped; the repair | `…PrefixShape_StartMovedToApril_MayNamesApril_FebruaryAndMarchSkipped`; `…PrefixShape_AprilRecalculated_RecordsRehire_MayExportsChained` | F (CI); the repair fact B | 14700-T |
| **AC-229-3** after February is recalculated, March exports `CHAINED` | `…FebruaryRecalculated_MarchExportsChained_FromCorrectedClosing` | B (CI) — holds today; discriminates on the opening = February's corrected closing | 14700-T |
| **AC-229-4** a correction between admission and the recheck → `payroll-ledger-changed`; the retry → `payroll-prior-month-stale`; order forced by observation | `FlexLedgerStaleChainRaceTests.CorrectionBetweenAdmissionAndRecheck_LedgerChanged_ThenRetryNamesStaleMonth` | F (CI) — today the retry answers 200 | 14700-T |
| **AC-229-5** the Unit matrix | `FlexLedgerChainWalkTests.*` (one fact per bullet, `REFINEMENT…:683-693`) | N (M-1 … M-6) | 14700-T |
| **AC-229-6** the deep chain: April names February | `…AprilFirstExport_AfterJanuaryCorrected_NamesFebruary_ThenRepairChains` | F (CI) | 14700-T |
| **AC-229-7** the class doc states the guarantee; one `WindowsReadRange(month, ledgerStart)`; the census re-run | review + `git grep -n WindowsReadRange -- src tests`; the census in the report | — | 14700, 14700-T |
| **AC-235-1** the month GET serves "Særlig feriedag" | `SkemaSpecialHolidayRowTests.MonthGet_ServesSpecialHolidayRow_LabelledSaerligFeriedag` | F (CI) | 14701-T |
| **AC-235-2** not served without a config | `…NoSpecialHolidayConfig_RowNotServed` | B (CI) — holds today; paired with AC-235-1 | 14701-T |
| **AC-235-3** in the admin list; hiding works | `…AdminVisibilityList_IncludesSpecialHoliday_HidingRemovesRow` | F (CI) | 14701-T |
| **AC-235-4** a day saved in the taking window shows as used on the balance and the snapshot | `…DaySavedInTakingWindow_ShowsUsedOnBalanceAndSnapshot` | F or R (CI) — the agent reports which (does today's save accept the code?) | 14701-T |
| **AC-235-5** one label map | build + `git grep -n "Særlig feriedag\|Barns 1. sygedag" -- src` (one definition) | — | 14701 |
| **AC-235-6** the row renders | `SkemaPage.specialHoliday.test.tsx` (name per `ux`) | F (local vitest) or B — reported | 14702 |
| **AC-235-7** R2 recorded; expected: seed `false`, a part-day save accepted | `…PartDaySpecialHoliday_Accepted` (or, on a contradiction, `…_Refused_AbsenceFullDayOnly` in 14701-F) | R (CI) | 14701-T |
| **AC-231-1 (RR-5)** the stored literals (`SICK_DAY`, `CHILD_SICK_DAY`, `_2`, `_3`) through Skema and payroll change the closing by exactly the screen total, for HK and PROSA by the sourced rule and for an AC-agreement employee by the labelled default (Q-1); RED today for the child codes and `SICK_DAY` | `SickCodeVocabularyPayrollTests.StoredChildSickCodes_ThroughSkemaAndPayroll_ChangeClosingByScreenTotal`, `…StoredSickDay_ThroughSkemaAndPayroll_ChangesClosingByScreenTotal`, `…StoredSickDay_AcAgreementEmployee_CreditedByTheSameRule` | F (CI) | 14708-T |
| **AC-231-1, the partial day (RR-3)** hours worked plus the day's remaining hours registered as sick close with a delta of 0 (the reading max(worked, planned); the over-credit case is QUAL-231's residual) | `SickCodeVocabularyPayrollTests.PartialSickDay_WorkedPlusRemainingSickHours_ClosingDeltaZero` | F (CI) | 14708-T |
| **AC-231-2** reflection over `AbsenceTypes` equals the accepted set, one classification per code in `AbsenceClassification` (SharedKernel, P-14), `SICK_DAY` credit (the engine side; RR-5 removes the served half) | `AbsenceClassificationTests.Reflection_EqualsAcceptedSet_OneClassificationPerCode` | N (mutation M-7) | 14708-T |
| **AC-231-3** an unknown code → 422 | `SkemaSaveUnknownAbsenceCodeTests.UnknownCode_Returns422` | F or R (CI) — reported | 14708-T |
| **AC-231-4** no literal outside the vocabulary and the label map; dead lists gone | grep (command in 14708-T) | — | 14708, 14708-T |
| **AC-231-5** the old-marker replay is refused; the 14 files updated | the existing replay-refusal fact re-pinned to `weekly-timeline-v2` | R (re-pinned) | 14708-T |
| **AC-231-6** absence lines identical | `AbsenceLinesGoldenTests.AbsenceLines_UnchangedByVocabularyChange` | R | 14708-T |
| **AC-231-7** cap crossing on export and on correction | `FlexCapCrossingTests.ExtraCredit_CrossesCap_PayoutLineAppears_OnExport`, `…_OnCorrection` | F (CI) | 14708-T |
| **AC-231-8 (RR-1, RR-5)** R1 applied: `SICK_DAY` credited, delta 0 between screen and payroll | satisfied by AC-231-1's `SICK_DAY` facts in 14708-T (T3b folded; no TASK-14717-T) | F (CI) | 14708-T |
| **AC-053-1** the sender approving → 403; another HR user → 200 | `SenderSeparationTests.Sender_Approves_403_AnotherHr_200` | F (CI) | 14704-T |
| **AC-053-2** sender reject 403; sender reopen of EMPLOYEE_APPROVED 200; legacy `SubmittedBy` 403; APPROVED month 403 not 409 | `…Sender_Rejects_403`, `…Sender_ReopensEmployeeApproved_200`, `…LegacySubmittedBySender_Approves_403`, `…Sender_OnApprovedMonth_403_Not409` | F; reopen R (CI) | 14704-T |
| **AC-053-3** a forced reopen-and-re-send never ends APPROVED by the sender | `SenderSeparationRaceTests.ForcedReopenAndResend_NeverEndsApprovedBySender` | F (CI) | 14704-T |
| **AC-053-4** payroll refuses approver = sender or employee; a null sender passes | `PayrollSenderSeparationHostTests.*` | F; null sender R (CI) | 14704-T |
| **AC-053-5** the demo load is clean; DemoVerifier check 21 green | the DemoSeed suite + CI e2e/demo job | R | 14704, 14704-T |
| **AC-053-6** SEC-009's matrix unchanged | the existing SEC-009 facts, unchanged | R (CI) | 14704-T |
| **AC-056-1** self approve/reject → 403; self compensate → 403, balance unchanged; another leader in scope → 200 ×3 | `OvertimeSelfDecisionTests.*` | F (CI) | 14705-T |
| **AC-056-2** self against a non-PENDING pre-approval → **403, not the 400** the status check answers (`OvertimeEndpoints.cs:383-384`; brief contradiction BC-P5); WARNING log, no audit row | `…SelfOnNonPending_403_Not400`, `…SelfDenied_LogsWarning_NoAuditRow` | F (CI) | 14705-T |
| **AC-056-3** create naming oneself still 200/201 | `…CreateForSelf_StillAllowed` | R (CI) | 14705-T |
| **AC-234i-1** an 8-hour rest gap month returns its not-checked days; both screens show the notice; payroll figures and marker unchanged | `RestCheckNotCheckedDaysTests.*` (Regression); the marker-unchanged assertion; vitest in 14707 | F (CI) + F (vitest) + R | 14706-T, 14707 |
| **AC-230-1** the manager approves Mette's March on 18 March: 200 (reject, reopen likewise) | `LeaverGraceTests.ManagerOnDPlus5_Approves_Rejects_Reopens_200` | F (CI) | 14710-T |
| **AC-230-2** on 19 March: manager 403; HR 200; LocalAdmin `/calculate-and-export` 200 with the record | `LeaverHrOnlyTests.DPlus6_Manager403_Hr200_PayrollExports` | F (CI) | 14713-T |
| **AC-230-3** one constant: due predicate (Unit); the poll D+5/D+6 (Docker); the writer 5/6 days back and a correction into the grace (Unit + Docker); grep | `GraceConstantTests.*` (N, M-8); `SettlementCloseGraceTests.*` (F, CI); `EndDateLifecycleGraceTests.*` (N + F) | N · F (CI) | 14710-T |
| **AC-230-4** login 200 on 13 March, 401 from 14 March with `employment_ended`; NULL end date as today | `LeaverLoginTests.*` (seeded active + end date passed) | F (CI) | 14710-T |
| **AC-230-5, Employee self-service** Mette's Skema save, time entry, send, employee-approve, employee-arm reopen, row-preferences PUT and compensation-choice PUT → 403 with D3's reason | `DepartedSelfServiceTests.Employee_*` (seeded) | F (CI) | 14710-T |
| **AC-230-5, LocalHR self-service** own Skema save, time entry, send → 403, the same for another employee 200; leader-arm reopen of her own EMPLOYEE_APPROVED month → 403. *These four routes all pass the validator's employee overloads (`SkemaEndpoints.cs:658`, `TimeEndpoints.cs:87`, `ApprovalEndpoints.cs:1986`, `:1658`), so TASK-14703's clause already closes them; written there, they are F against today's master (seeded), with no mutation needed (the T5a split replaces R-32's "RED by mutation")* | `DepartedSelfServiceElevatedTests.LocalHr_OwnSkemaSave_TimeEntry_Send_403_OtherEmployee_200`, `…LocalHr_LeaderArmReopenOwnMonth_403` | F (CI) | 14703-T |
| **AC-230-5, LocalHR self-only routes (plan-added coverage)** her own row-preferences PUT and compensation-choice PUT → 403. These two routes never reach the validator, so the self-service floor's identity arm is the **only** layer: the discriminating facts for that arm | `DepartedSelfServiceTests.LocalHr_OwnRowPreferences_CompensationChoice_403` | F (CI) | 14710-T |
| **AC-230-5, the elevated routes (R-30, R-31, F2, F3)** token from D, used on D+1. **LocalLeader:** pre-approval create, approve/reject of her own pending pre-approval, compensate for herself → 403. **LocalHR:** settlement resolve, reconcile-payout, termination-payout request, agreement-code PUT, users PUT, unit PUT for herself → 403. **LocalAdmin (F3; the routes need LocalAdminOrAbove, `AdminEndpoints.cs:3266`, `:3416`):** role grant to herself, revoke of her own assignment (compared on `assignmentUserId`, read before the mutation, F2) → 403. Each write for another employee in scope → 200 | `DepartedSelfElevatedRouteTests.*` (seeded active + end date yesterday; one deactivated variant per terminated-inclusive route) | F (CI) | 14703-T |
| **AC-230-5, project select/deselect** both 404; the spec, typed client and bodyless list no longer name them | `ProjectSelectionRetiredTests.*` (F, CI); REGEN drift; `tools/check_openapi_*` in CI | F (CI) | 14710-T, 14711 |
| **AC-230-5, others** on 14 March HR's write for Mette allowed; the manager's approve 200 | in `LeaverGraceTests` | F (CI) | 14710-T |
| **AC-230-6** floors and the bound after the gate | `LeaverHrOnlyFloorTests.*` (a differential fact per handler) | F / B (CI) | 14713-T |
| **AC-230-7** the tree-lock overload and the audit read resolve a deactivated subject; audit org is the subject's | `LeaverTreeLockAndAuditTests.*` | F (CI) | 14713-T |
| **AC-230-8** a month approved within the grace exports after D+6 | `LeaverHrOnlyTests.ApprovedInGrace_ExportsAfterDPlus6` | F (CI) | 14713-T |
| **AC-230-9** HR opens Mette's Skema month on D+6; the row's actions; "manager until 18 March, HR only from 19 March"; aging unchanged | `LeaverHrScreenPathTests.*` (CI) + `FollowUpLists.leaverActions.test.tsx` | F (CI) + F (vitest) | 14713-T, 14714 |
| **AC-230-10** the orphan leaver through the UI: HR-A sends, HR-A's approve shows the four-eyes reason, HR-B approves, the month exports | `LeaverOrphanFlowTests.*` (CI) + vitest | F | 14713-T, 14714 |
| **AC-230-11** four-eyes across the grace | `LeaverFourEyesTests.*` | F (CI) | 14713-T |
| **AC-230-12** the R9c inventories name each new surface, the overload and the validator's clause; the shared helper and its six call sites unchanged | review + diff; `git grep -n "AcquireTreeLockForEmployeeAsync" -- src` (6 reporting-line sites unchanged) | — | 14703, 14713 |
| **AC-230-13** `/recalculate` of a deactivated leaver's exported month by another admin works | `LeaverRecalculateCharacterizationTests.OtherAdmin_RecalculatesDeactivatedLeaverMonth_Works` | R (CI) | 14713-T |
| **AC-230-14** K-3 (a grace leaver not enumerated by the SPECIAL_HOLIDAY active pass); K-2 re-pinned; K-8 suites updated | `SettlementCloseGraceTests.SpecialHolidayActivePass_SkipsGraceLeaver` (F, CI); K-2 re-pin; the K-8 census | F · R | 14710-T |
| **AC-230-15** Mette, **a LocalLeader whose authority over R is the designated edge** (P-6; an HR-tier Mette would be admitted by approve's org-scope fallback, `ApprovalEndpoints.cs:274-276`, R-25), on D+1: her approve of R's month 403; **(1b)** Mette as a designated leader of R's unit with no edge → 403; the month routes to the escalated manager (200); the pending list and the resolver agree; **the stand-ins (P-2, owner Q-B = (c)):** a self-appointed and an HR-appointed stand-in of Mette each still approve (200) | `DepartedApproverTests.*` (seeded); `StandInOfDepartedManager_SelfAppointed_StillApproves_200`, `…_HrAppointed_StillApproves_200` | F (CI); the two stand-in facts **B** (boundary pins of the ruled behaviour) | 14712-T |
| **AC-230-16** the validator clause, both overloads (`REFINEMENT…:786-792`), plus one host-start fact per host (BC-6) | `DepartedSelfValidatorTests.*` (seeded; F on both overloads: today's validator admits an active self with a passed end date); `TimeProviderRegistrationHostTests.{Backend,Payroll,Orchestrator}_HostStarts_ValidatorResolves` | F (CI) · N (host facts, CI; M-10 argued) | 14703-T |
| **AC-230-17 (F1, new)** a departed GlobalAdmin's `/recalculate` of their own exported month → 403, nothing written (no correction event, baseline and balances unchanged, no idempotency marker); the same token on another employee's month → as today | `RecalculateDepartedSelfTests.DepartedGlobalAdmin_OwnMonth_403_NothingWritten`, `…_DeactivatedVariant_403`, `…_OtherEmployee_AsToday`, `…DepartedGlobalAdmin_OwnMonth_AlreadyDeliveredToken_Still403` (P-4: today `Payroll/Program.cs:646-654` answers 200 "Correction already processed" first) | F (CI) | 14703-T |
| **AC-230-18 (F4, P-1, P-10)** the sweep's guards beyond R-31, **sixteen facts (10 F, 6 B)** in `DepartedSelfElevatedRouteTests`: import (row naming the departed actor → 403, nothing written; without it → as today); unit delete (departed direct member → 403 before the first write; another actor → as today); `/remove` (departed actor is a direct report → 403, rolled back; is an ACTING report → 403, rolled back; named as the replacement → as today); delegate set → 403, no row; delegate clear → 403, the row still open; admin vikar for herself as manager → 403; **the departed actor herself** naming herself as another manager's stand-in → as today (P-13); admin vikar clear for herself → 403; unit-leader add and remove of herself → 403; **the consequence clause (P-10):** the departed actor removes, or transfers, her own stand-in X → as today, her own stand-in row closed, `ManagerVikarEnded` naming her as actor | `Import_RowNamingDepartedSelf_403_NothingWritten`, `Import_WithoutSelfRow_AsToday`, `UnitDelete_DepartedSelfIsDirectMember_403_NothingWritten`, `UnitDelete_OtherActor_AsToday`, `Remove_DepartedActorIsDirectReport_403_RolledBack`, `Remove_DepartedActorIsActingReport_403_RolledBack`, `Remove_DepartedActorNamedAsReplacement_AsToday`, `Delegate_DepartedActor_403_NoVikarRow`, `DelegateClear_DepartedActor_403_RowStillOpen`, `AdminVikar_DepartedActorAsManager_403`, `AdminVikar_DepartedActorAsStandIn_AsToday`, `AdminVikarClear_DepartedActorAsManager_403`, `UnitLeaderAdd_DepartedSelf_403`, `UnitLeaderRemove_DepartedSelf_403`, `Remove_DepartedActorsStandInRemoved_OwnVikarRowClosed_AsToday`, `UsersPut_DepartedActorsStandInTransferred_OwnVikarRowClosed_AsToday` | F (CI) ×10; B ×6 (the four "as today" facts and the two consequence-clause facts) | 14703-T |
| **AC-054-1** (a) a case-3a month with lines: 200, the note, a null-balance record, no `FLEX_PAYOUT`; re-run 409, record unchanged. (b) zero lines: 200, the note, no record; reopenable; re-export writes nothing; `/recalculate` refuses | `FlexLedgerCase3aHostTests.*` | F (CI) | 14715-T |
| **AC-054-2** the 3a sentence gone; no body says "raw export"; `RetroactiveCorrectionService.cs:402-405` updated | grep + census | — | 14715, 14715-T |
| **AC-054-3** both raw routes 404 to a GlobalAdmin | `RawExportRoutesRetiredTests.*` | F (CI) | 14716-T |
| **AC-054-4** no `payroll-export` task type in `src/Orchestrator` | grep + the Orchestrator suites | — | 14716, 14716-T |
| **AC-054-5** `ExportAsync` requires a period; the deleted facts listed with their guards | `ExportAsync_NullPeriod_Refuses` (P-4) + the list in 14716-T's report | N (M-13) | 14716-T |
| **AC-054-6** step 2 merges only after AC-230-2 and AC-054-1 are green in CI | CI-W4's run id in the sprint log | — | Orchestrator |
| **AC-R, AC-D1 … AC-D5** | § R, § D | — | Orchestrator |

**The mutation list (N pins and RED-by-mutation facts).** Unit mutations (M-1 … M-9, M-11, M-12, and M-13 if its fact is Unit) are run by a re-spawned `test-qa` in an isolated worktree at the wave gate (`docs/AGENTS.md:171-180`); the Orchestrator reads the captured output. The one Docker-gated mutation, **M-10**, is argued in the docstring (a missing registration fails dependency injection at the host's first resolve, loudly), not run. *No Docker fact in this plan relies on a mutation for its RED:* the seeded grace state makes each of them F against the master it is written on.

| Id | Pin | Mutation (keeps the API, breaks one behaviour) | Assertion it must trip |
|---|---|---|---|
| M-1 | AC-229-5 deep stale | the walk compares only X = P | the earliest stale month (not P) is named |
| M-2 | AC-229-5 several stale | return the latest differing X | only the earliest is named |
| M-3 | AC-229-5 R-48 skip | do not skip a month with no employed day | the successor, not the R-48 month, is named |
| M-4 | AC-229-5 before a REHIRE | start the walk at the latest `REHIRE` instead of S | the stale month before the boundary is named |
| M-5 | AC-229-5 evaluated once | call `DeriveLink(M)` a second time | the link-evaluation count equals the walk length + 1 |
| M-6 | AC-229-5 refusing link | swallow an inner refusal as "consistent" | the month is named with its inner kind |
| M-7 | AC-231-2 | drop `SickDay` from `src/SharedKernel/StatsTid.SharedKernel/Models/AbsenceClassification.cs` (P-14) | reflection equality and "one classification per code" |
| M-8 | AC-230-3 due predicate | grace constant 0 | "5 days back is not due" |
| M-9 | the self-service floor's truth table (`DepartedSubjectWriteFloorTests`, Unit, 14710-T) | drop the "actor is the subject" arm | "departed subject, HR actor who is the subject → refuse" |
| M-10 | AC-230-16 host facts | delete `AddSingleton(TimeProvider.System)` in the Payroll (then the Orchestrator) host | the host-start fact fails to resolve the validator |
| M-11 | `DepartedSubjectTests` | `d < today` → `d <= today` | "end date today → not departed" |
| M-12 | `DepartedSubjectTests` | drop `!subject.IsActive` | "deactivated, no end date → departed" |
| M-13 | AC-054-5 `ExportAsync_NullPeriod_Refuses` (P-4) | drop the null-period check in `ExportAsync` | the call without a period throws and writes nothing (Unit if the service is reachable without a database; otherwise Docker, argued — 14716-T says which) |

---

## CI checkpoints and the close sequence (Orchestrator on Opus; Fable-seat judgments marked)

*Plain language:* Docker cannot run here, so Docker-only facts are first seen in CI. Each wave that lays a Docker-only foundation is run in CI once, on a throwaway branch, before the next wave builds on it. The sprint is then reviewed on a fixed commit **K** that has itself run green, and the close commit (docs-only on top of K) is pushed and watched.

**The mechanism (every checkpoint):** `git push origin <sha>:refs/heads/s147-ci-<name>`; open a **draft** PR to master whose first body line is "EVIDENCE ONLY — S147 checkpoint <name>; never merge"; watch with ONE background command and its completion notification (never polled, `docs/WORKFLOW.md:506-509`); confirm `headSha == <sha>`; **all seven jobs green**. Then close the PR unmerged, `git push origin --delete s147-ci-<name>`, `git branch -D` locally. Red → a fix task through the merge checklist → repeat. The sprint log records each run id and only then flips the facts it showed green from "CI-pending" to "CI-verified".

1. **CI-W1 (after wave 1's GATE):** T1's host and race facts and every existing fact the walk touched; T2's facts; the re-run chain census holds no surprise.
2. **CI-W2 (after wave 2a's GATE) — the foundation checkpoint:** AC-230-16 on both overloads and the three host-start facts (the Payroll and Orchestrator hosts start with the new dependency); the seven fixtures' suites with a fixed provider; AC-230-5's elevated routes and LocalHR self-service facts; AC-230-17; AC-230-18; AC-053-*, AC-056-*, AC-234i-1, AC-231-* (AC-231-8 included: T3b folded, RR-1); the frontend-build job on the regenerated client (14707) and on 14709's deletion. **Wave 2b does not start until this is green.**
3. **CI-W3 (after wave 2b's GATE):** AC-230-1, -3, -4, -5 (Employee self-service, LocalHR self-only routes, project 404, others), -14, -15; the frontend-build job on the regenerated client.
4. **CI-W3b (after wave 3's GATE; P-7):** AC-230-2, -6 … -13 (TASK-14713's nine Docker-only facts, seen before TASK-14715 edits the same `Payroll/Program.cs`); the frontend-build job on the regenerated client (TASK-14714).
5. **CI-W4 (after wave 4a's GATE):** AC-054-1, -2, and AC-230-2 re-confirmed on the same sha. **The condition for 4b (AC-054-6).**
6. **C-1 (after wave 4b's GATE and the build-dependent O-items):** the records (O-2 … O-12) with the Step-7a outcome and CI ids as marked placeholders. Commit **K** on master, message `S147 close candidate: <summary>`, **without** the phrase "sprint close" (the guard reads the whole Bash command; write such text through a file). The sprint log's "## Task ledger" names every TASK id with a disposition. Not pushed.
7. **C-2 — the checkpoint on K**, branch `s147-ci-k`. Red → fix → K′ → repeat.
8. **C-3 — Step 7a, both lenses:** base **`b510e45b`** (`codex review --base b510e45b`, since the wave merges are commits on master), `reviewed-against-commit: <K>`, the carried items below. **The review floor:** the `reviewer` on Fable 5.1 (`reviewed-by-model: claude-fable-5-1`). If Fable is at its usage limit, the owner's interim direction applies: an Opus stand-in labelled honestly, plus the narrow waiver `.claude/reviews/SPRINT-147-review-floor-WAIVED.md` naming the model and the owner's direction (`docs/WORKFLOW.md:42`, `:361`); the sprint log and the routing register record it. Artifacts saved before acting: `.claude/reviews/SPRINT-147-step7a-{codex,reviewer}.md`. A code-touching fix makes K′ → C-2 again → the verification cycle.
9. **C-4 — the close:** `S147 sprint close — …`, a docs-only child of K. The guard's gates (`docs/WORKFLOW.md:41-47`): both Step-7a artifacts with `verdict:` and a `reviewed-against-commit` prefixing HEAD, the reviewer's floor line (or the narrow waiver); CI-health (the latest push run on master not red); not a second consecutive CI-pending; no untracked files under `src/`, `tests/`, `tools/`, `frontend/`; **no worktree but the main checkout**; every TASK id with a ledger disposition. Confirm `git diff --stat K HEAD` lists only `docs/**`. Push; watch CI on the close sha to green.
10. **C-5 — follow-up:** the `**Test Verified**` line with the close run id; the model-routing register's S147 row; the handoff block "closed"; push; watch green; teardown (worktrees, local and evidence branches). Housekeeping DEBT carried from Step 0a: `origin/s144-red-mutations` and `origin/add-claude-github-actions-…` (owner's call; not deleted silently).

**Carried into Step 7a:** (1) the post-close docs commits `1e105f61` … `02ef91f1` (inside the diff from `b510e45b`); (2) the Step-0b verification list below, re-checked against the code as built; (3) the sweep's census re-run on K (the same command; any new write route since draft classified); (4) the ADR-043 review record.

---

## Step 0b — what the lenses must verify (cycle 3: Codex in full; the internal lens narrow, under Q-C = (b))

1. **The four cap folds, with P-1 and P-10.** **F1** → TASK-14703 Contract 5 and AC-230-17 (plus P-4's already-delivered-token fact). **F2** → TASK-14703 Contract 5 (revoke: `assignmentUserId`, `AdminEndpoints.cs:3297`; the subject read before the mutation at `:3340-3343`) and AC-230-5. **F3** → AC-230-5's LocalAdmin bullet and TASK-14703-T. **F4 with P-1 and P-10** → the subject-key rule, the consequence clause and the census in TASK-14703, reproducible by its command at `02ef91f1`: 92 write routes, 84 in the three hosts, **32 / 19 / 33** (unchanged by P-10).
2. **P-10's two classified side effects** against the code: `/remove` Step 4 (`ReportingLineEndpoints.cs:1589-1595`, the helper's predicate `:3045`) and the users PUT's transfer arm (`AdminEndpoints.cs:2452-2474` → `ManagerVikarRepository.cs:353-356`); that these are the only derived `manager_vikar` closures in `src`; the two B facts in TASK-14703-T point 7.
3. **P-11 = (b):** nothing from the gate shape is built anywhere; the departed-self rule stays R-30 + R-31 + P-1 + the self-service floor.
4. **P-12:** the two S136 pins renamed `…_Is403_ReDecidedS147` and flipped as intentional (TASK-14703-T point 7b); the census (`git grep -n -e "AdjudicatedAccepted" -e "take it back to the owner" -- tests` → 2 files, 4 hits); `SendCommandTerminatedSubjectTests.cs` authorized; the `:637` sentence; O-6's SEC-046 note.
5. **P-13:** `AdminVikar_DepartedActorAsStandIn_AsToday` has the departed actor as the caller.
6. **P-14:** `AbsenceClassification.cs` in SharedKernel; `AbsenceRule.cs` reads it; the Backend's vocabulary gate reads the same set without touching the Rule Engine (nothing is served to the screen since RR-4); M-7's site; TASK-14708's authorized files (no shared-files row is needed since RR-1: one task touches the file).
7. **P-15:** the "thirteen routes … and one derived check at `/remove`" wording (TASK-14703-T); B6's member-invariant sentence; "six B facts"; `…IsActingReport…`.
8. **Carried from cycle 2, unchanged:** P-1 … P-9 as verified by both lenses in cycle 2 (`PLAN-s147-reviewer-c2.md`, "Verified as ruled"); the P-5 sentence in the plan and all 31 briefs (`git grep --untracked -l "employee-facing response body" -- .claude/plans` → 32 files); the census counts (7 `new OrgScopeValidator(`; 14 `weekly-timeline-v1` files; 16 `Derive(` and 3 `WindowsReadRange` calls in tests; 6 + 3 tree-lock sites); file-overlap order.
9. **Draft 3.1 (the research rulings RR-1 … RR-13), in scope for cycle 3:** TASK-14708 (one branch, `SICK_DAY` credit; point 3a dropped; the RR-2 mapping; the variant-gating sentence; no REGEN drift), TASK-14708-T (the four AC-231-1 facts incl. the partial day; the served fact removed; AC-231-8 satisfied), TASK-14709 (points 0 and 1 only, from master), TASK-14717/-T DROPPED (ledger rows with the reason), the AC-231 rows, the cut order, § R, the D-table brief additions, O-3, O-4, O-9, O-10. The citations RR-2 adds were re-opened at `02ef91f1`: `DailyNormCalculator.cs:166`, `DayNorm.cs:44-45`, `AbsenceRule.cs:120-127`, `SkemaEndpoints.cs:778`, `:926-979`, `:1148`, `:1644`, `:46-58`, `useSkema.ts:712`.

---

## Records owed (the O-items above, plus)

- **Quality:** `docs/QUALITY.md` S147 re-grade (Payroll Integration; Security; Domain Correctness), saying what moved and why. Registers: QUAL-229 … 235, SEC-053 … 056 dispositions; SEC-003's line; SEC-046's S147 note (P-12); a register candidate for the pre-existing defect in BC-P10.
- **ADRs:** ADR-040 amendment; ADR-034 (OQ-6, D3, D4); ADR-016 D10 footnote; ADR-033 note; ADR-043 ACCEPTED; ADR-014 per D-6.
- **`SECURITY.md`, `SYSTEM_TARGET.md` §H, the HRP register (HRP-003, HRP-011), `danish-agreements.md`, the tender analysis (O-12), CLAUDE.md's dossier row (O-9), ROADMAP § 1.**
- **`docs/generated/db-schema.md`:** no schema change is planned; if 14701-F flips the seed, it is data only (no regeneration). The legacy-db runbook gets no row unless a schema change appears.
- **Sprint log** `docs/sprints/SPRINT-147.md` + the INDEX row: the header (base `b510e45b`), the task ledger, the 5α/5a table, each GATE line, the CI run ids and the facts each flipped, the mutation table, the sweep's census counts, the research verdicts, every D ruling verbatim, the Step-7a outcome, the **Test Verified** line at C-5. **Model-routing register** S147 row.
- **The research records (RR-13):** the sprint log gets a Research row (each cell's verdict, the verifier's agreement, the RR rulings, QUAL-236 registered, Q-1 and Q-2 and their answers when given); the ledger keeps TASK-14717 and TASK-14717-T as DROPPED with the reason (RR-1) and lists TASK-14701-F, TASK-14709-N, TASK-14712-F, TASK-14710-F as "not built" with theirs; O-4's sickness row; **the refinement is not revised** (rev 4.1 stays the record; this plan supersedes its R1 branches).
- **Housekeeping at the plan commit:** `.claude/plans/PLAN-s147.md` and `.claude/plans/s147/*.md` are committed (S144/S146 precedent); the S145 evidence directories stay untracked.

---

## Cut order (if the sprint must shrink) — from the refinement (`:579-589`)

1. **Nothing is cut without the owner** (RR-1: T3b folded into TASK-14708, so the refinement's one Orchestrator-cuttable item is gone).
2. **Cut only by owner direction:** T2 (14701, 14701-T, 14702), T3 (14708 with T3b folded in, 14708-T, 14709), T5c (14712, 14712-T), T6 (14715 … 14716-T), T7 (14706, 14706-T, 14707), T9 (14705, 14705-T); the D track falls back to its deferred branch. **If T6 step 2 is cut, the sweep's inline guard is added to `/export` and `/export-period`** in the same change, so the class stays closed.
3. **Never cut:** T1 (14700, 14700-T), T4 (14704, 14704-T), T5a (14703, 14703-T, 14710, 14710-T, 14711), T5b (14713, 14713-T, 14714).

---

## Handoff block (the Orchestrator writes it at every gate into `SPRINT-147.md`)

| | |
|---|---|
| **As of** | <date, master sha> |
| **Phase** | <wave / gate / C-n> |
| **Tasks** | `task · worktree/branch · build · 5α · consult · 5a-R · 5a-C · merged sha` — one row per task, paired pass, stack and fix-up |
| **Research** | R1 PARTLY (applied as CONFIRMED, RR-1), R2–R5 CONFIRMED; QUAL-236 found and registered; dossier filed (O-9) <yes/no> |
| **Decide track** | D-1 … D-12: <ruled (verbatim in the ledger) / asked / brief drafted / waiting on R-n>; ADR-043 review <cycle, verdicts> |
| **Pending gates** | <next gate and its open boxes> |
| **CI** | G-0 `40324407582` · CI-W1 <run, headSha, result> · CI-W2 <…> · CI-W3 <…> · CI-W3b <…> · CI-W4 <…> · C-2 <…> · close <run> |
| **Open rulings** | <id · question · who> |
| **Next action** | <one line> |

---

## Brief contradictions (claim · what the code shows · file:line)

*The refinement and its adjudications made these claims about the existing code; I went by the code. None changes a requirement.*

- **BC-P1 (R-31, the users PUT's `today`).** *Claim:* "`today` already hoisted at `:1612`". *The code:* the handler's one business-date read is at `AdminEndpoints.cs:1720`, after the scope checks (`:1646-1666`); `existingUser` is at `:1626` as cited. *Disposition:* TASK-14703 moves the single read above the guard (still one read, PAT-028).
- **BC-P2 (R-31, revoke; the owner's F2).** *Claim:* revoke compares `request.UserId` against an existing subject read. *The code:* revoke takes `AssignmentId` (`:3291`), derives `assignmentUserId` (`:3297`), mutates at `:3340-3343` and reads the user only afterwards (`:3386`). *Disposition:* as F2 rules, in TASK-14703 Contract 5.
- **BC-P3 (R-31's classification of the reporting-line import).** *Claim:* import grants only authority over others. *The code:* each row names an `employeeId` and a `managerId` (`ReportingLineEndpoints.cs:945-955`), and the route checks only the tree's organisation (`:931-934`); a row with `employeeId` = the actor writes the actor's own reporting line, which decides who approves their own months — the very reason R-31 guards the unit PUT. *Disposition:* guarded in TASK-14703; NR-1 = (a), confirmed by P-1 (e). A row's `managerId` naming the actor is a holder and stays allowed (P-1).
- **BC-P4 (not in the refinement: unit delete).** *The code:* `DELETE /api/admin/units/{id}` re-homes the unit's direct members in its transaction (`UnitEndpoints.cs:567`, emitting `UserUnitChanged` per member, `:568-580`), so a departed HR actor who is a member moves their own unit. *Disposition:* guarded in TASK-14703 by a pre-check in the transaction, before its first write (P-1 (b): one `SELECT` of the actor's `users.unit_id` after the lock `:519`, before `:528`); NR-1 = (a), confirmed.
- **BC-P5 (AC-056-2's status code).** *Claim:* "self against a non-PENDING pre-approval gets 403, not 409". *The code:* the status check answers **400** (`OvertimeEndpoints.cs:383-384`, `:462-463`). *Disposition:* the AC reads "403, not 400"; the requirement (the self check fires before the status check) is unchanged.
- **BC-P6 (R-30's list of closed writes).** *Claim:* `EntitlementEligibilityEndpoints.cs:110`, `:281`, `:341`, `:386` are writes. *The code:* `:281` and `:341` are the two GET routes (`:269`, `:331`); the writes are `:110` and `:386`. *Disposition:* no effect (reads are refused too, K-13).
- **BC-P7 (R-30 on the Orchestrator).** *Claim:* `/execute` is closed by the clause. *The code:* true for elevated actors; an Employee-only actor takes the own-data branch (`OrgScopeValidator.cs:65-71`), which R-30 deliberately leaves alone. The route writes only its own task log (`OrchestratorControlLoop.cs:228-238`), and its pipeline only reads (`WeeklyCalculationPipeline.cs`, two GETs). *Disposition:* classified in the census; no change.
- **BC-P8 (the host count).** *Claim:* three hosts write. *The code:* a fourth host, External, has one POST (`src/Integrations/StatsTid.Integrations.External/Program.cs:54`). *Disposition:* classified out (no StatsTid employee record).
- **BC-P9 (scope of two vocabularies).** `AbsenceTypes` lives in `src/SharedKernel/StatsTid.SharedKernel/Models/AbsenceEntry.cs:13` (the `data-model` scope), and T7's result shape in `…/Models/ComplianceCheckResult.cs:3` with its merger in `…/Segmentation/ComplianceCheckResultMerger.cs:32`; the refinement's role lists name neither. *Disposition:* named in the cross-domain labels of 14708 and 14706.
- **BC-P10 (a pre-existing defect, outside scope).** Revoking a role from a **deactivated** user throws a 500 today: the post-mutation audit read is active-only and throws on null (`AdminEndpoints.cs:3386-3394`); the transaction rolls back. TASK-14703's guard turns the *self* case into a 403; the other-user case is unchanged. *Disposition:* reported to the Orchestrator as a register candidate (Low), not fixed here.
- **BC-P11 (line drift, minor).** `ComputeEndDateLifecycle` starts at `EmploymentEndDateLifecycleWriter.cs:245` (the refinement's `:256` is the `passed` comparison inside it); the "Særlig feriedag" label is `frontend/src/lib/entitlementConstants.ts:14`, not `:15`. *Disposition:* the briefs cite the code.
- **BC-P12 (a naming collision).** The brief's "O-1 gate" and the refinement's docs item "O-1" share a name. *Disposition:* this plan keeps the refinement's O-1 … O-11 for the docs items and calls the post-merge gate **GATE** and the contract regeneration **REGEN**.
- **BC-P13 (R-33's retirement list).** *Claim:* `S119ProjectSpecRuntimeTests`' `SelectAsync` helper (`:434`) goes with Op 4 and Op 5. *The code:* the helper is at `:431`, and a **surviving** fact (the available-projects read) also calls it to mark a project selected (`:183`). *Disposition:* TASK-14710-T re-seeds that step through the row-preferences PUT, assertions unchanged; a STOP if `selected` cannot be produced that way.
- **BC-P14 (R-31's census item: the premise).** *Claim:* a stand-in edge from a departed delegator gives nothing, "because the authority delegated has ended". *The code:* a stand-in for an **inactive** manager is honoured **by design** (S74 R3a: the vikar consult "beats BOTH M-if-active AND the inactive-M escalation walk", `ReportingLineRepository.cs:1196-1213`, checking only the stand-in's activity at `:1209`). So the census answer is already known: today the edge persists. R-31's fallback (apply the acting-user rule on the delegator side) would reverse S74 R3a. *Disposition:* **P-2 vacated the fallback; the owner answered Q-B = (c) (2026-10-28): the edge survives; TASK-14712 changes no stand-in read and pins the behaviour.** The only new hole (a stand-in minted after departure) is closed by P-1 (B14, B16).
- **BC-P15 (T5b's Skema HR tier).** *Claim:* the Skema view's HR tier admits a deactivated subject at `ApprovalReadTier.cs:60`. *The code:* that helper (`src/Backend/StatsTid.Backend.Api/ApprovalReadTier.cs:40-52`) is shared with three other reads — the breakdown (`ApprovalEndpoints.cs:1325`), balance (`BalanceEndpoints.cs:101`) and compliance (`ComplianceEndpoints.cs:127`) — so changing it in place would widen reads outside S147's scope. *Disposition:* TASK-14713 adds a terminated-inclusive tier method used only by the Skema view (an R9c entry) and leaves the shared one unchanged, the same pattern R-7 chose for the tree lock.

## Needs a ruling — all settled at Step 0b cycles 1 and 2

- **NR-1 (R-31 vs F4 on the import and unit delete) → (a), keep both guards: confirmed (P-1 (e)),** and widened by P-1 into the subject-key rule with six more guards and `/remove`'s derived guard.
- **NR-2 (three declared deviations: the T5a split; T7's role; where two AC-230-5 facts live and how their RED is shown) → confirmed (P-9).**
- **NR-3 (a stand-in's authority after the manager it stands in for has left) → P-2 vacated R-31's delegator-side fallback; the owner answered Q-B = (c) on 2026-10-28:** a stand-in a manager or HR appointed before the manager left keeps approving until its end date or HR's revocation; `/remove` closes it. TASK-14712 builds nothing for it and pins it (two boundary facts); O-6 records it in the ADR-040 amendment and the HRP-014 row. The sketched TASK-14712-F and TASK-14710-F are not built.
- **Q-C (raised at Step 0b cycle 2, P-11): one check at each service's front door that refuses every write by a departed person, or the route-by-route design → the owner answered (b), 2026-10-28: keep the route-by-route design.** The write gate is declined; nothing from its shape is built; SEC-003's residual (other people's data until the token expires) stands as ruled in S129 and R-25.

- **The research rulings (RR-1 … RR-13)** raise two owner questions, both asked by the Orchestrator, neither blocking a dispatch: **Q-1** (AC's sick-day credit default) inside D-8 — S147 builds lean (a) as a labelled default; **Q-2** (QUAL-236's code fix) — **answered (a), 2026-10-28: the complete fix, including the call-home day, is S148 stage 0**; no S147 task.

No planner-raised item is open.

---

## Step 0b cycle 1 findings absorbed

*Cycle 1: Codex NOT-READY (2 B / 2 W; `.claude/reviews/PLAN-s147-codex-c1.md`); the internal lens (Fable) READY with W-1 … W-5 (`…-reviewer-c1.md`); adjudicated P-1 … P-9 with owner question Q-B (`…-adjudication-c1.md`, RULED-WITH-QUESTIONS); the owner answered Q-B = (c) on 2026-10-28.*

| Finding | Ruling | What changed, where |
|---|---|---|
| Codex B1 — `/remove` rewrites the reports' own lines; 32/13/39 is not a sound closure | **P-1** | The subject-key rule (TASK-14703 Contract 4, plan § F4); `/remove`'s derived guard before Step 1 (Contract 5; census row A); facts `Remove_DepartedActorIsDirectReport_403_RolledBack`, `…HoldsActingEdge_403_RolledBack` (renamed `…IsActingReport…` by P-15), `…NamedAsReplacement_AsToday` (TASK-14703-T, AC-230-18). *Narrowed, as ruled:* ids that only hold authority are classified out, pinned by B facts |
| Codex B2 — NR-3's implementation misses unit-leader stand-ins; NR-3 needs explicit adjudication | **P-2**, owner **Q-B = (c)** | No delegator-side rule is built (TASK-14712 point 4: no change); two boundary pins (TASK-14712-T point 4); NR-3 settled; O-6 adds the ADR-040 line and HRP-014; TASK-14712-F / TASK-14710-F not built |
| Codex W1 — a late negative R1 has no delivery path | **P-3** | TASK-14708 point 3a (the served classification on every branch; one member of REGEN drift); TASK-14709 stacked on every branch and always regenerates; TASK-14709-N (plan, conditional tasks); R1's row in § R; TASK-14717 dropped on CONTRADICTED |
| Codex W2 — two security contracts lack discriminating facts | **P-4** | `ExportAsync_NullPeriod_Refuses` (N, M-13; TASK-14716-T, AC-054-5); `DepartedGlobalAdmin_OwnMonth_AlreadyDeliveredToken_Still403` (F; TASK-14703-T, AC-230-17) |
| Codex NOTE — NR-1 (a) and NR-2 sound; TASK-14717's cross-domain label; three `WindowsReadRange` calls | P-1 (e), P-9, P-3, P-8 | TASK-14717's Role row; TASK-14700's report count; Step 0b item 6 |
| Fable W-1 — the standing constraint misquotes ADR-040 D7 | **P-5** | The constraint now reads "employee-facing … HR-scoped admin reads carry one only where a ruling says so — in S147 only HRP-011's leaver row (R-27, TASK-14713)", in the plan and every brief's tail |
| Fable W-2 — group C's "authority" reason is conditional on NR-3 | **P-1** | Delegate set/clear, admin vikar set/clear (manager = actor), unit leaders add/remove move to group B (B14–B19); group C's reasons are now unconditional |
| Fable W-3 — NR-3 is the owner's | **P-2**, Q-B = (c) | As Codex B2 |
| Fable W-4 — AC-230-15 does not pin Mette's role | **P-6** | Mette is a LocalLeader whose authority is the edge, with the docstring's reason; fact 1b (unit leader, no edge → 403) (TASK-14712-T; AC-230-15) |
| Fable W-5 — "before any write" vs unit delete's design | **P-1 (b)** | Unit delete's pre-check before `ApplyUnitDeletedAsync` (`:528`); Contract 4: a guard inside a transaction fires before its first write; the fact renamed `UnitDelete_DepartedSelfIsDirectMember_403_NothingWritten` |
| Fable N-1, N-7, N-8 — confirmations | — | N-7: the Orchestrator carries 14703's cross-domain label verbatim into the sprint log |
| Fable N-2 — no CI checkpoint after wave 3 | **P-7** | CI-W3b after wave 3; CI-W4 lists AC-054-1, -2 and re-confirms AC-230-2 |
| Fable N-3 — per-task Codex mechanism | **P-8** | Merge checklist step 6: Codex runs inside the task's worktree with the commit range named in the prompt |
| Fable N-4 — the direct-seed census | **P-8** | TASK-14704-T point 5 |
| Fable N-5 — 14703's local gate | **P-8** | TASK-14703's header: `src` build + Unit + DemoSeed |
| Fable N-6 — three `WindowsReadRange` calls | **P-8** | TASK-14700's report; Step 0b item 6 |
| Capacity observation (twelve owner questions beside 31 dispatches) | noted | AC-D3's deferred branch remains the valve |

**Sizes changed:** TASK-14703 M–L → **L**; TASK-14703-T M–L → **L** (P-1 (c), (d)).

**For `docs/sprints/SPRINT-147.md` (Orchestrator):** the sweep's counts are **84 write routes in the three hosts = 32 (A; one, `/remove`, with a derived guard) + 19 (B: 13 inline, 2 self-service floor, 4 retirement) + 33 (C: 31 no employee subject, 1 cannot address an existing person, 1 anonymous)**; 92 write routes in `src` overall (External 1 and Rule Engine 7 beyond the three hosts, classified out).

---

## Step 0b cycle 2 findings absorbed

*Cycle 2: Codex NOT-READY (1 B / 1 W / 2 N; `.claude/reviews/PLAN-s147-codex-c2.md`); the internal lens (Fable, narrow) READY with W-1, W-2, N-1 … N-4 (`…-reviewer-c2.md`); adjudicated P-10 … P-16 with owner question Q-C (`…-adjudication-c2.md`, RULED-WITH-QUESTIONS); the owner answered **Q-C = (b)** on 2026-10-28, so draft 3 applies the (b) branch of every ruling.*

| Finding | Ruling | What changed, where |
|---|---|---|
| Codex B — `/remove` Step 4 and the users PUT's transfer arm close the actor's own `manager_vikar` row when the person removed/moved is the actor's stand-in | **P-10** (classify out; Fable W-1 (b)) | TASK-14703 Contract 4: the consequence clause; the `/remove` and B1 census rows point to it; TASK-14703-T point 7: `Remove_DepartedActorsStandInRemoved_OwnVikarRowClosed_AsToday`, `UsersPut_DepartedActorsStandInTransferred_OwnVikarRowClosed_AsToday` (B); AC-230-18 = sixteen facts (10 F, 6 B); plan § F4; counts unchanged (84 = 32 / 19 / 33). *Declined as ruled:* adding the two derived-subject checks |
| The Orchestrator's structural question (a front-door write gate) | **P-11**, owner **Q-C = (b)** | Recorded in the owner-rulings row, the summary, § F4, TASK-14703 Facts and the rulings section; nothing from the (c) shape is built |
| The adjudicator's own finding — two S136 owner-adjudicated pins flip under R-30 | **P-12** | TASK-14703-T point 7b: renamed `…_Is403_ReDecidedS147`, intentional flips labelled `// S147 OQ-2 / R-30:`, the census (2 files, 4 hits), `SendCommandTerminatedSubjectTests.cs` authorized, the `:637` sentence (and its twin at `:293-294`); TASK-14703 Facts names the superseded residual; O-6: SEC-046's note and the ADR-040 line; records owed |
| Codex W — the stand-in holder pin uses the wrong caller | **P-13** | `AdminVikar_DepartedActorAsStandIn_AsToday`: the departed actor calls the route for another manager, naming herself (TASK-14703-T; AC-230-18) |
| Fable W-2 — the served classification needs a SharedKernel home | **P-14** | New `src/SharedKernel/StatsTid.SharedKernel/Models/AbsenceClassification.cs`; `AbsenceRule.cs` reads it; M-7's site; TASK-14708 (point 2, Role row, authorized files), TASK-14708-T (AC-231-2's served fact covers every catalogue code), TASK-14717 (Role row, authorized files); the shared-files row; AC-231-2's served row |
| Fable N-1 — "seven routes" is stale | **P-15** | TASK-14703-T: "thirteen routes … and one derived check at `/remove`" |
| Fable N-2 — the P-5 count via `git grep` misses untracked plan files | **P-15** | Step 0b item 8 uses `git grep --untracked -l` → 32 files (the plan and 31 briefs; the reviewer's "33" is not reproduced at draft 3 — see the final report) |
| Fable N-3 — B6 and the actor's `unit_leaders` row | **P-15** | TASK-14703 B6: the member-invariant sentence with its cites, to be verified by the implementer |
| Fable N-4 — "four B facts"; the `HoldsActingEdge` name | **P-15** | "six B facts"; renamed `Remove_DepartedActorIsActingReport_403_RolledBack` |
| Codex N ×2 — NR-3 resolved; cycle-1 warnings addressed | — | confirmations |
| P-16 — record lines for the sprint log | Orchestrator | not a plan edit |

**For `docs/sprints/SPRINT-147.md` (Orchestrator):** the counts are unchanged — 84 write routes in the three hosts = 32 (A) + 19 (B: 13 inline, 2 self-service floor, 4 retirement) + 33 (C); AC-230-18 has sixteen facts (10 F, 6 B).

---

## Revision history

| Rev | Date | By | What |
|---|---|---|---|
| draft 1 | 2026-10-28 | `planner` (Opus 5.5) | From refinement rev 4.1 READY and the owner's cap ruling (b): 18 tasks + 13 paired passes (TASK-14700 … 14717), the research pass and its brief, the decide track D-1 … D-12, O-1 … O-11 plus O-12 (the tender re-rating), four wave checkpoints and C-2; the folds F1 (`/recalculate`, AC-230-17), F2 (revoke on `assignmentUserId`, read before the mutation), F3 (LocalAdmin for grant/revoke) and F4 (the census: 92 / 84 / 32 / 13 / 39, in TASK-14703); fifteen brief contradictions; three items for the adjudicator (NR-1 … NR-3) |
| draft 2 | 2026-10-28 | `planner` (Opus 5.5) | Applies the Step 0b cycle-1 rulings P-1 … P-9 (Fable adjudicator) and the owner's Q-B = (c). **P-1:** the subject-key rule; TASK-14703 gains six inline guards (delegate set/clear, admin vikar set/clear when `managerId` = actor, unit leaders add/remove), `/remove`'s derived guard and unit delete's pre-check; Contract 4's "before the transaction's first write"; census and counts **32 / 19 / 33** in TASK-14703, plan § F4, the Theme row and Step 0b; AC-230-18 grows to fourteen facts; 14703 and 14703-T sized L. **P-2 / Q-B = (c):** TASK-14712 point 4 builds nothing; two boundary pins; NR-3 settled; O-6's ADR-040 and HRP-014 line; TASK-14712-F and TASK-14710-F not built. **P-3:** TASK-14708 point 3a unconditional (one member of REGEN drift); TASK-14709 stacked on every branch; the TASK-14709-N path; TASK-14717 dropped on CONTRADICTED, its cross-domain Role row. **P-4:** `ExportAsync_NullPeriod_Refuses` (M-13) and the already-delivered-token fact. **P-5:** the ADR-040 D7 constraint text in the plan and all 31 briefs. **P-6:** AC-230-15's LocalLeader setup and fact 1b. **P-7:** CI-W3b. **P-8:** Codex inside the worktree with the commit range; the direct-seed census; 14703's local gate; three `WindowsReadRange` calls. **P-9:** NR-2 confirmed. Brief-contradiction dispositions BC-P3, BC-P4, BC-P14 updated; the cycle-1 absorbed-findings table added |
| draft 3 | 2026-10-28 | `planner` (Opus 5.5) | Applies the Step 0b cycle-2 rulings under the owner's **Q-C = (b)** (keep the route-by-route design; no front-door write gate). **P-10:** the consequence clause in TASK-14703 Contract 4 and § F4, pointed to from the `/remove` and B1 rows; two B facts; AC-230-18 = sixteen facts (10 F, 6 B); counts unchanged 84 = 32 / 19 / 33. **P-11 = (b):** Q-C's answer recorded; the gate declined and nothing from it built. **P-12:** the two S136 pins flipped as intentional and renamed `…_Is403_ReDecidedS147`; the census; `SendCommandTerminatedSubjectTests.cs` authorized; the `:637` sentence; O-6's SEC-046 note and ADR-040 line. **P-13:** the departed actor as the stand-in pin's caller. **P-14:** `AbsenceClassification.cs` in SharedKernel; TASK-14708/14717 authorized files and Role rows; M-7's site; the shared-files row. **P-15:** N-1 … N-4 text. P-16 is the Orchestrator's. R1-dependent content in TASK-14708/14717 left for draft 3.1. The cycle-2 absorbed-findings table and the cycle-3 Step 0b list added |
| draft 3.1 | 2026-10-28 | `planner` (Opus 5.5) | Applies the research rulings RR-1 … RR-13 (Fable adjudicator; the verification governs). **RR-1:** T3b folds into TASK-14708 (one branch: `SICK_DAY` credit for every agreement; AC by a labelled default pending Q-1 in D-8; one marker move, `weekly-timeline-v2`); TASK-14717/-T DROPPED in the ledger with the reason; cut-order item 1 replaced by "nothing is cut without the owner". **RR-2:** "planned hours" = the per-day norm (`DayNorm.ForWeekday`), the credit = the stored, capped sick hours; T3b = one classification entry; D-7's brief sentence. **RR-3:** the partial-day fact; O-4's wording; QUAL-231's residual. **RR-4:** TASK-14708 point 3a and the served member dropped (REGEN: no drift); TASK-14709 keeps points 0 and 1 and branches from master in wave 2a; no TASK-14709-N; the stack and the "two regenerated clients" sentence removed. **RR-5:** AC-231-1/-2/-8 reworded; 14708-T's facts. **RR-6:** T2 stands; O-3's citation; TASK-14701-F not built. **RR-7 … RR-9, RR-12:** the decide-track brief additions (D-2, D-3, D-4, D-7, D-8 with Q-1, D-9, D-10) and ADR-043 F-5's sentence. **RR-10:** QUAL-236 (registered) — TASK-14708 leaves `SkemaEndpoints.cs:46-58` unchanged; the docs half in O-9; the code route S148 stage 0 pending Q-2. **RR-11:** O-9's filing format and CLAUDE.md row text. **RR-13:** the records. § R rewritten with the verdicts |
