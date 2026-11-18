# S148 resume point — saved 2026-11-18 before a machine restart

This is the Orchestrator's handoff. Read it together with the tail of `docs/sprints/SPRINT-148.md` and `.claude/plans/PLAN-s148.md` ("final + 20"). Nothing was running when this was saved.

## Where the work stands

**Wave 4.**
- Merged into local master:
  - TASK-14815, the TASK-14816 stack, TASK-14818 (CI-W4a green);
  - TASK-14819 (`7703f940`);
  - TASK-14820 + -T (`d7458466`).
- Each GATE was green.
- Master HEAD is `d9d56e79` (plan "final + 20"). Nothing is pushed yet: every S148 commit is pushed at sprint close.

**In flight: TASK-14821-S + TASK-14821 + TASK-14821-T, reviewed together on one branch.**
- **Branch:** `worktree-agent-a467b1b10f230a888`, tip **`d5466748`**. The chain on top of `ac47d9f1` is:
  - `51c61ac7` (14821-S: validator siblings)
  - `3513d2b1` (14821)
  - `fd13174c` (14821-T)
  - `d5466748` (fix-up 1, comments only)
- **Reviews done.**
  - 5α PASS.
  - Security consult: RESIDUALS, all Low.
  - Cycle 1: Codex CHANGES-REQUIRED; Fable CHANGES-REQUIRED.
  - Adjudication: `.claude/reviews/S148-TASK-14821-5a-adjudication-c1.md`. Read § 3, § 4 and § 5.
- **Done:** § 3a, the backend comment fix-up (`d5466748`, report `S148-TASK-14821-fix1-report.md`).
- **NEXT STEP: § 3b, the `test-qa` fix-up (TASK-14821-T fix-up 1)** on `d5466748`. First action: `git merge --ff-only d5466748`. It covers:
  - the BLOCKER: the `pg_locks` helper must COALESCE the null `classid`/`objid`/`objsubid` columns;
  - four GlobalAdmin self-change facts (AC-C13h (5));
  - the self close with no If-Match (AC-C13h (6));
  - the opening-Monday fact (AC-PIL-h (12));
  - the start cleared when already none (AC-PIL-h (13));
  - the profile PUT's drift fact while waiting on `(148, 1)` (AC-C13n (7));
  - M-45 re-run with a frozen list of seven.
- **Then:**
  1. 5α on the fix-up delta.
  2. Cycle 2 by both lenses (Codex and Fable) on `fd13174c..<3b tip>`.
  3. Merge `--no-ff` with § 5's paperwork:
     - SEC-062 second status group FIXED;
     - SEC-065 and SEC-066;
     - SEC-060's sentence;
     - the `EmployeeEmploymentStartDateSet` remark ("Nothing emits it yet") and its catalog row;
     - the `BackdateWorklistRowCreated` remarks;
     - plan "final + 21";
     - the ROADMAP backlog line for the plausible-date bound;
     - the sprint log.
   4. **Also at merge (Orchestrator, comment only):** add `StrandedRegistrations` to the 409 list in `SettlementReversalEndpoints.cs:62-68` (found by fix-up 1).
   5. GATE.
- **Stale worktree:** `.claude/worktrees/agent-a94cd019d00c5c17c` (TASK-14821's, `3513d2b1`) is still locked by a dead agent pid. After the restart: `git worktree unlock .claude/worktrees/agent-a94cd019d00c5c17c`, then `git worktree remove` it. Its branch is not needed; the chain lives on `worktree-agent-a467b1b10f230a888`.

## Owner rulings this session (recorded in the sprint log, except the last)

1. **SEC-062: fix all thirteen remaining sites in S148, in TASK-14845** (2026-11-17).
2. **NEW, 2026-11-18, not yet in the sprint log or the briefs.** The self-change refusal is extended. An HR user may not change their **own entitlement eligibility** or their **own birth date** (403, a second administrator must), added in **TASK-14845**. The bare reversal of one's own settlement stays registered (SEC-065), not fixed.
   - Before TASK-14845 dispatches, the planner amends `TASK-14845.md` / `-T.md` and the plan with this: the refusal on `EntitlementEligibilityEndpoints.cs`'s eligibility writes and the birth-date PUT, its facts, and a census of existing tests and the demo loader for self-edits.
   - SEC-065's status then becomes "fixed in TASK-14845" for those two doors.
3. **Told to the owner, not struck:** the pool-starvation item (SEC-066) is carried to TASK-14831's measurement. The owner may strike that and ask for a task that moves the profile correction's revaluation reads into its own transaction.

## After TASK-14821 merges

1. **TASK-14845 + -T** (draft 2 plus the owner ruling above): the drift guard on 22 sites, plus the two self-refusals. Security consult and both lenses.
2. **CI-W4** (the non-Monday Regression run is owed; today is not a Monday), **CI-M4a**, **CI-M4b**.
3. **Waves 5 and 6.** Then the sprint close and its owed items (listed in the plan and the sprint log).

## Helpers

Copied to `.claude/reviews/S148-session-helpers/` (gitignored, on disk). The session scratchpad is temporary.
- `lastmsg.js`: extracts a subagent's final report from its transcript `.jsonl` for verbatim saving.
- `codex-14821-run.ps1` and its prompt: the Codex run pattern. Pipe the prompt to `codex.ps1 exec --dangerously-bypass-approvals-and-sandbox -C <worktree> --output-last-message <raw.md>` and write an `.exit` file. Change the worktree, prompt and raw paths for each run.
- `check_names.py`: the `init.sql` constraint-name collision scan.

**Standing practice, unchanged:**
- Save every review verbatim before acting on it.
- Dual-lens review; at most two Anthropic agents at once.
- Builds one at a time in the foreground.
- No agent edits `docs/**`.
- Prune worktrees after each merge; the disk is at about 4.8 GB free.
