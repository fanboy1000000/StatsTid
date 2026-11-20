# S148 resume point — updated 2026-11-20 (midday)

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

## In flight (wave 5) — 2026-11-20 midday
1. **Merged with GATE green:**
   - TASK-14822 + -T (`6fadd1fe`);
   - the TASK-14823 stack (`90828667`);
   - **TASK-14825 + -T** (`7af9176b`; paperwork `26974e77`: QUAL-184 and QUAL-042 FIXED, the QUAL-148 note, F10; GATE `4a58e603`). Its worktrees are being removed.
2. **CI-W5a: judged green on the evidence** (run 44728481380 at `4a58e603`; PR #66 closed unmerged, branch deleted; logged in the sprint log with the PLAN:715 items, the 84 re-pointed facts and the three timing lines; F9 and F10 carry the CI figures). Its docs job was red only on the F10 re-tag, fixed on master at `14a73d6f`; CI-W5 proves it.
3. **The T14b stack, in progress** (TASK-14826 → 14826-T → 14827 → 14828 → 14828-T → REGEN → 14829, merged together; NR-P9). The briefs are at draft 2 ("final + 26").
   - **TASK-14826 returned:** `9f621abb` on `bb0677df` (report `.claude/reviews/S148-TASK-14826-report.md`; S1 … S10 checked byte for byte).
   - **TASK-14826-T dispatched** (`test-qa`) on `9f621abb`.
   - **TASK-14827 next, on 14826-T's tip.** Carry into its dispatch: (1) the 30 March question (TASK-14826 report, Gate 6: `WeeklyPayrollTests.OkBoundary_April2026_…`; whether payroll joins the context days to the first period piece across 1 April; if so, cut there, item 3); (2) the PCS cites moved (`CallTimelineAsync` posts at about `:4466-4479`, `evaluate` at `:4342-4352`); (3) `PeriodCalculationService.cs:4461-4463` says the engine splits a piece at 1 April, now untrue.
   - **TASK-14828's plan row carries** R8 and R9 (from 14822), C2-R5 (14822), R4 (14825) and N2 (14825 c3).
   - **TASK-14831's plan row carries** R6 (14823, the "Blocks until granted" remark).
4. **Then** 14830 + -T, 14831 + -T, 14846, CI-W5 and CI-M5 (M-9, M-12, M-13, M-15, M-49).
5. **Owner ruling 2026-11-19 (a):** QUAL-259 is built after S148.
6. **Housekeeping:** about 55 stale local `worktree-agent-*` branches from earlier waves.

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
