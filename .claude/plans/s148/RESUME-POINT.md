# S148 resume point — updated 2026-11-23

The Orchestrator's handoff. Read it together with the tail of `docs/sprints/SPRINT-148.md` and `.claude/plans/PLAN-s148.md` ("final + 24").

## Where the work stands

**Wave 4 is merged into local master, with each GATE green:**
- TASK-14815;
- the TASK-14816 stack;
- TASK-14818 (CI-W4a green);
- TASK-14819;
- TASK-14820 + -T;
- TASK-14821-S + 14821 + -T (`e58df211`);
- **TASK-14845 + -T** (`53c2793d`; paperwork `30d382d4`; GATE `c17db07c`). This includes **QUAL-257** (owner ruling 2026-11-18 (a): fixed inside TASK-14845's fix-up round; AC-C13s, M-48).

The TASK-14845 reviews:
- 5a: three cycles, with Codex and Fable both APPROVED in cycle 3. Rulings are `.claude/reviews/S148-TASK-14845-5a-rulings-c1.md` and `-c2.md`.
- Security consults: the parent's and fix-up 1's.
- 5α: `S148-TASK-14845-5alpha.md` and `-c2.md`.

Registers: SEC-062's 22 sites, SEC-065's two doors and QUAL-257 are all marked FIXED. Worktrees are pruned.

**CI-W4 is green** (run 44352425811; Regression 2691/2691; watch items R6, R7 and R8 met; ruling 1.9's non-Monday run settled). It is logged in `d7278932`, PR #63 is closed unmerged, and `ci/s148-w4` is deleted.

**Nothing on master is pushed.** Only the evidence branches `ci/s148-m4a` and `ci/s148-m4b` are on the remote.

## Where wave 4 ended
- **CI-W4, CI-M4a and CI-M4b are all green and judged** (`d7278932`, `86e33f5b`).
  - CI-M4a: 41 of 41 frozen facts red at their assertions; CI-M4b: 7 of 7. No unlisted red.
  - The registers read "CI-verified".
  - PRs #63, #64 and #65 are closed unmerged, and every `ci/s148-*` branch is deleted.
  - Correction logged: under M-45 the profile PUT answered 200 in CI, not the local 403.

## In flight (wave 5) — 2026-11-23
1. **Merged with GATE green:**
   - TASK-14822 + -T (`6fadd1fe`);
   - the TASK-14823 stack (`90828667`);
   - **TASK-14825 + -T** (`7af9176b`; paperwork `26974e77`: QUAL-184 and QUAL-042 FIXED, the QUAL-148 note, F10; GATE `4a58e603`). Its worktrees are being removed.
2. **CI-W5a: judged green on the evidence** (run 44728481380 at `4a58e603`; PR #66 closed unmerged, branch deleted; logged in the sprint log with the PLAN:715 items, the 84 re-pointed facts and the three timing lines; F9 and F10 carry the CI figures). Its docs job was red only on the F10 re-tag, fixed on master at `14a73d6f`; CI-W5 proves it.
3. **The T14b stack, in progress** (TASK-14826 → 14826-T → 14827 → 14828 → 14828-T → REGEN → 14829, merged together; NR-P9). The briefs are at draft 2 ("final + 26").
   - **TASK-14826 returned:** `9f621abb` on `bb0677df` (report `.claude/reviews/S148-TASK-14826-report.md`; S1 … S10 checked byte for byte).
   - **TASK-14826-T returned:** tip `df670468` (report `.claude/reviews/S148-TASK-14826-T-report.md`); gate green; 60 local database reds, each for its stated reason (payroll S1/S3 → 14827; compliance S6 → 14828). The 30 March question is refuted: payroll already cuts at 1 April.
   - **TASK-14827 returned:** `5873c29c` on `df670468` (report `.claude/reviews/S148-TASK-14827-report.md`; the agent stopped on a login expiry on 2026-11-20 and was resumed on 2026-11-23). Three files in scope. Unit has 37 reds for TASK-14828-T (36 hand-built PCS facts without a lookup, plus the `RuleEngineFailureLoggingTests` reflection arity); DemoSeed and non-Docker Regression are green. Local: PIN-1 green; WeeklyPayroll 12, SplitMonth 8 and SkemaCellClear's replay stay red until TASK-14828-T gives the hand-built fixtures a lookup.
   - **TASK-14828 returned:** `f68ed12a` on `5873c29c` (report `.claude/reviews/S148-TASK-14828-report.md`). Ten files in scope (`ConfigResolutionService.cs` comment-only, checked). Three `shortcut(pending)` comments to re-tag in the merge commit: `OvertimeEndpoints.cs:122`, `TaskDispatcher.cs:85`, `WeeklyCalculationPipeline.cs:88`. Unit 37 red (TASK-14827's list). Local: the compliance classes green; no seeded figure moved. Reads: Skema month 90 → 14, year overview about 526 → 30.
     - **For the stack review:** deviation 2 (a workless month still sends one compliance run, against ruling 18's letter, because of S6); deviation 8 (an incomplete in-month record serves a null minimum rest); `DatedRecordTimeline` duplicates `EmploymentProfileResolver`'s rules; request-scoped reuse through `HttpContext.Items` in a singleton.
     - **Fix at the stack's merge:** Backend `Program.cs:456-459`'s calculator remark ("keeps no cache") is untrue; `ConfigResolutionService.cs:149-153` logs the ruled `monthEnd + 1` refusal at Error.
   - **TASK-14828-T returned:** tip `b55a7a96` on `f68ed12a` (report `.claude/reviews/S148-TASK-14828-T-report.md`). Only `tests/**` changed; every carried item was delivered (the 37 Unit reds cleared, R9 and C2-R5 green locally, M-50 and the AC-C1l halves proven). Orchestrator's gate at `b55a7a96`: Unit 1751, DemoSeed 170, Regression non-Docker 146; CA2100 113.
     - **Two older production defects found, for the stack review** (the stack touches neither):
       - AC-C13k (2) is red: the payroll week check (`PeriodCalculationService.cs:4099`) counts a same-code agreement row boundary (a backdated new hire) as a mid-week change, so the month answers 422.
       - `WeeklyCalculationPipeline.cs:43-45`'s `Convert.ToDecimal` on `JsonElement` makes the Orchestrator's `weekly-calculation` task fail through `/execute` (since March), so the new smoke probe is expected red in CI.
     - Also for the review: the compliance smoke probe forbids only 503.
   - **REGEN** `061f8d55` on `b55a7a96`: `openapi.json` +7, the `minimumRest` member only, as predicted.
   - **TASK-14829 dispatched** (`ux`) on `061f8d55`, with the served member as built and deviation 8 as a third null source.
   - **Carry to TASK-14830:** `EnsureConfigUnchanged`'s remark and text; `ReplayRefusedException.cs:37`; `IOrganisationHistoryReader.cs:12-15`; the `TryResolveDatedProfileAsync` log label.
   - **For the stack's review:** payroll answers an anonymous 500 for `EmployeeProfileNotFoundException` (a record gap or a missing organisation); register coverage `(unproven)`.
   - **Then** the stack review at TASK-14829's tip (5α on the union, Fable reviewer and Codex), with every item above.
4. **Then** 14830 + -T, 14831 + -T, 14846, CI-W5 and CI-M5 (M-9, M-12, M-13, M-15, M-49).
5. **Owner ruling 2026-11-19 (a):** QUAL-259 is built after S148.
6. **Housekeeping:** about 55 stale local `worktree-agent-*` branches from earlier waves. The T14b stack's worktrees for 14826, 14826-T and 14827 are removed (branches kept); 14828's and 14828-T's remain until the merge.

## After that
- **Wave 5** ends with **TASK-14846** (`ux`; the edit drawer locks one's own refused fields, with the contract Danish note).
- **Wave 6:** TASK-14838 keeps 14846's picker disable.
- **The sprint close and its owed items,** including the **C2-R4 census**: other decision routes may share QUAL-257's check-then-act shape. That is `(unproven)`; each hit needs a concrete two-request ordering.

## Owner rulings, 2026-11-18 (all recorded in the sprint log)
1. The self refusal is extended to one's own eligibility and birth date (SEC-065; now FIXED).
2. The self-refusal UI is option (a), TASK-14846 in S148.
3. Self-deactivation is (b), register only (SEC-067).
4. QUAL-257 is (a), fixed now inside TASK-14845 (done).
5. The pool carry (SEC-066) is still reported, not struck; it is carried to TASK-14831-T's third measured figure.

## Helpers and practice
- **Helpers** are in `.claude/reviews/S148-session-helpers/`:
  - `msgmatch.js`, which saves the last assistant message containing a marker, plus later messages as an addendum. Use it instead of `lastmsg.js`, which takes only the final message.
  - The Codex run scripts and prompts, for cycles 1 to 3.
- **No Python on this machine.** `check_docs.py` and `check_openapi_convention.py` run in CI only. **Every merge GATE runs `node .claude/reviews/S148-session-helpers/shortcut-check.js .`** (a Node mirror of the PAT-032 shortcut check; it must report 0 bad). Re-tag every `shortcut(pending)` in the merge commit itself (merge-checklist item 8).
- **Getting the counts right:** count CA2100 with a `--no-incremental` build (113 distinct). Read files' line endings with node; Git Bash's `grep -c $'\r'` miscounts.
- **Standing practice:**
  - Save every review verbatim before acting on it.
  - Review on both lenses.
  - Run at most two Anthropic agents at once.
  - Run builds one at a time, in the foreground.
  - No agent edits `docs/**`.
  - Prune worktrees after each merge (15 GB free).
  - Keep the session's working directory at the repo root.
