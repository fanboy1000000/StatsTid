# S148 C-5 — the model-routing register's S148 row, and the FAIL-009 evidence (DRAFT 1)

| | |
|---|---|
| **Status** | DRAFT 1, for the Orchestrator to apply (`docs/**` is Orchestrator-only). Nothing here is decided. |
| **Drafted by** | `planner` (Opus 5.5), 2026-11-26, after the close |
| **Base** | the close commit `df582c20` (2026-11-26 09:13:04 +01:00 = 08:13:04Z), a docs-only child of K `b284f210` |
| **Window counted** | 2026-11-09T00:00Z (S148 opened) → 2026-11-26T08:13:04Z (the close commit). This C-5 drafting spawn (log line 1284, 08:15:33Z) is after the window. |
| **Sources** | the subagent and session transcripts under `~/.claude/projects/C--Users-b200895-source-repos-StatsTid/`; `.claude/telemetry/model-routing.log`; `docs/sprints/SPRINT-148.md`; the S148 artifacts in `.claude/reviews/`; `.claude/plans/PLAN-s148.md:767-773`; `docs/operations/model-routing-register.md:23-102`; FAIL-009; `ROADMAP.md:780-790` |

**In one paragraph, for the owner.** S148 is the first sprint measured for the Ponytail trial and the first
test of the FAIL-009 fix. The fix worked: the routing guard checked every one of the 441 agent launches
(in S147 it silently missed 51 of 200), and the close guard, which runs before every shell command, missed one
command out of about 22,300 (a reviewer's harmless read) because it took longer than the 60-second limit. The
close commit itself was checked and passed. Every review ran on the newest Fable from 2026-11-10 on, and the close
needed no exception or waiver. The trial's numbers are descriptive and only mean something over three sprints;
this sprint had more review cycles per task than the S145–S147 average, one review cycle per lens at Step 7a
instead of five, no post-close defect so far, and no `(unproven)` note that later turned out to hide a real
defect. Part C puts the fail-open/fail-closed question to the owner with the evidence, without deciding it.

---

## Part A — the register row

*One table line, to go after the S147 row (`docs/operations/model-routing-register.md:128`). Copy the fenced line
exactly. One placeholder, ‹owner's FAIL-009 decision›, is for the Orchestrator to fill after the owner rules
(Part C). The `anchor-sprint` marker at `:3` moves to 148 with it.*

```markdown
| **S148 (tenth live run — the first measured sprint of the Ponytail trial and the first test of FAIL-009's 60 s limit: the routing guard saw all 441 agent launches, one shell command in about 22,300 ran unchecked, and the close passed on the floor model with no exception and no waiver)** | **Opus 5.5 seat throughout** (sessions `0175149f`, carried over from S147, → `389c418e` from 2026-11-18, after a machine restart, → `a53199d4` from 2026-11-25; client 2.1.301 on 2026-11-09, 2.1.322 from 11-10, 2.1.326 from 11-18, 2.1.332 from 11-25; every seat message from 11-09 on is `claude-opus-5-5`), never switched (the 2026-09-29 ruling).<br>**Judgment:**<br>• **2026-11-09, the refinement:** cycle 1's review and adjudication on Fable 5.1. Then Fable hit its rate limit (HTTP 429): the cycle-2 attempt (a resume of the cycle-1 reviewer) and three new `reviewer` spawns for cycles 3–5 got no answer. By the owner's S146 direction the internal lens ran as a labelled Opus stand-in (four `general-purpose` spawns, `reviewed-by-model: claude-opus-5-5`), and the seat ruled adjudications 2–4, labelled.<br>• **From 2026-11-10, Fable 5.2,** the new floor (`636bc7da`; two client restarts; the first three probes refused on the stale 5.1 self-check). Everything after ran on it: Step 0b and addendum A1's Step 0b (five cycles per lens each), all 95 Step-5a internal reviews, 40 adjudications and Step 7a. Where no finding was contested, the seat ruled (23 Step-5a rulings files, and Step 7a's rulings).<br>**The close** passed the guard on the floor model: one `close` line for S148, on `claude-fable-5-2`, PASS (2026-11-26T08:12:57Z), seven seconds before the close commit `df582c20`. That commit's Bash call carries no `hook_cancelled` and no `hook_non_blocking_error`. No `SPRINT-148-review-floor-WAIVED.md` exists, and none was needed.<br>**Drafting:** `planner` (Opus 5.5), 59 spawns.<br>**Research:** `general-purpose` on Opus 5.5 (the 11-hour-rest research and its verifier).<br>**Comment sweeps:** `sweep` on Haiku 5.5.<br>**External lens:** Codex, 108 review runs (refinement 5, Step 0b 5, addendum A1 5, Step 5a 92, Step 7a 1), plus one cut short by Codex's own usage limit | **439 subagent transcripts** from 2026-11-09 to the close commit, against **440 `ALLOW` spawn lines and 1 `BLOCK`** (log lines 842–1282). **The log and the transcripts reconcile one to one** ("log ALLOW 440 / transcripts 439"):<br>• 438 transcripts start within a second of an `ALLOW` line for the same role.<br>• The one `fork` transcript opens with the history it copied from its parent; its own first message falls on its line's second.<br>• The one `ALLOW` without a transcript is a launch that failed: a `test-qa` spawn refused by worktree isolation ("git metadata that could not be resolved", 2026-11-10T15:13:09Z), re-issued at 15:25 and run.<br>So the guard saw all 441 Agent calls (439 from the seat, 2 from inside agents). In S147 it saw 149 of 200.<br>**By role:** 114 `reviewer`, 59 `planner`, 59 `constraint-validator`, 55 `test-qa`, 43 `adjudicator`, 34 `backend-infrastructure`, 31 `ux`, 10 `security`, 9 `payroll-integration`, 7 `trace`, 7 `general-purpose` (6 `opus`, 1 `sonnet`), 5 `rule-engine`, 2 `sweep`, 2 `data-model`, 1 `Explore`, 1 `fork`. One was stopped before it finished: a `planner` the seat stopped at a usage limit before it wrote anything (2026-11-19). Two were launched by implementers, not by the seat: a `fork` inside TASK-14804 and an `Explore` inside TASK-14821, both read-only test censuses.<br>**Resolved ids, from the transcripts' assistant messages:**<br>• **`claude-fable-5-2` ×151:** 109 `reviewer` (3 of them floor-move probes), 42 `adjudicator` (2 probes).<br>• **`claude-fable-5-1` ×3,** all on 2026-11-09, before the floor move: 2 `reviewer` (refinement cycle 1; an evening probe), 1 `adjudicator`. Three more `reviewer` spawns that day answered nothing (HTTP 429).<br>• **`claude-opus-5-5` ×115:** 59 `planner`, 34 `backend-infrastructure`, 9 `payroll-integration`, 6 `general-purpose`, 5 `rule-engine`, 1 `Explore`, 1 `fork`.<br>• **`claude-sonnet-5-5` ×165:** 59 `constraint-validator`, 55 `test-qa`, 31 `ux`, 10 `security`, 7 `trace`, 2 `data-model`, 1 `general-purpose`.<br>• **`claude-haiku-5-5` ×2:** `sweep`.<br>**Guard blocks: 1, not a routing fault.** At 2026-11-25T12:25:49Z a `general-purpose` spawn (the close audit) had no model; it was re-issued 15 s later with `model: sonnet` and allowed. No block on a reviewer spawn, and none by the reviewer-brief detector.<br>**Reviewer refusals on real work: 0** (the floor-move probes' `REFUSED` answers are in the Notes).<br>The eleven spawn lines between S147's close line and 2026-11-09 (lines 831–841: S147's C-5, the FAIL-009 fix, the Ponytail trial set-up) belong to neither row | **5a, cycle 1, summed over chains (M2, descriptive only):**<br>• **Internal (Fable 5.2, 42 chains): 5 B / 88 W / 253 N.** Blockers on 4 chains: 14818 (2: the regenerated API client did not type-check, and the regeneration broke the OpenAPI convention gate), 14821 (eight lock tests would have thrown in CI on correct code), the 14831 stack (a profile closed just before an export was recorded silently), the QUAL-268 stack (a re-create reopening a leftover row).<br>• **Codex (37 chains): 22 B / 54 W / 74 N.** Blockers on 14 chains: 14804 (dismissed: the Orchestrator's prompt had misstated the rule), 14842 (= Fable's W-2), 14818 (2), 14820 (SEC-R1, which the adjudicator fixed wider than either lens framed it), 14821, 14822 (2), 14823, the T14b stack (2), the 14831 stack, the 14832 stack (on 14833), the 14834 stack (3, on 14835), the HR-facts stack (2, on 14837), 14838 (2), the QUAL-268 stack (2).<br>• Every blocker was fixed before its merge, except 14804's, which was dismissed. One chain ran past the cap: TASK-14841's sixth internal cycle, by the owner's one-time exception (Q6).<br>**7a (one cycle per lens, on K `b284f210`):** Fable 0 B / 2 W / 5 N; Codex 0 B / 2 W / 1 N; both APPROVED-WITH-WARNINGS, absorbed docs-only in the close commit. Codex's W1 made Fable's `(unproven)` NOTE concrete: a backdated norm change leaves stored absence above the day's new worth (8.8 h of surplus flex in its worked example). Registered as QUAL-284, High, S149's first task (owner, 2026-11-26).<br>**Not M1 cells (descriptive):** the refinement, 5 cycles per lens (internal cycle 1 on Fable 5.1, cycles 2–5 the Opus stand-in); Step 0b, 5 per lens; addendum A1's Step 0b, 5 per lens | **19 runs listed by id: 14 checkpoint runs and 5 CI-M mutation runs.**<br>• **The 5 CI-M runs** were red by design, and each matched its frozen list: CI-M2 `43238030414`, CI-M2b `43238051149`, CI-M4a `44481246221`, CI-M4b `44481256823`, CI-M5 `45362153082` (one unlisted red, attributed to M-12 as an extra kill).<br>• **9 checkpoint runs green:** CI-W0b `42816549335`, CI-W1b `43027618243`, CI-W2 `43233683529`, CI-W3 `43412751883`, CI-W3b `43851473962`, CI-W4a `43946317628`, CI-W4 `44352425811`, CI-W6 `45582399602`, C-2 `45700916293` (Regression 2978/2978, PIN-1 final).<br>• **5 checkpoint runs not green:**<br>– CI-W0 `42805305917`: red by design (the PIN-1 golden capture).<br>– CI-W1 `43011040973`: tests green; the docs job red on four unregistered `shortcut(pending)` comments.<br>– CI-W4a, first attempt, `43943858508`: red before any test ran. `init.sql` could not run (a constraint-name collision from TASK-14815, `data-model`, Sonnet 5.5). The one product defect CI caught.<br>– CI-W5a `44728481380`: tests green; the docs job red on one shortcut not re-tagged, fixed on master and proved at CI-W5.<br>– CI-W5 `45280970629`: tests green; the docs job red only on the design-sync gate, advisory by the owner's ruling of 2026-11-24.<br>No checkpoint needed more than two runs. The close push's run: ‹recorded at C-5› | **0** so far (C-5, 2026-11-26) | **What this row says about the routing.**<br>• **FAIL-009's fix held, at its first test.** The routing guard ran on all 441 Agent calls, with no timeout and no start failure (S147: 51 of 200 missed). The close guard ran before about 22,300 shell commands and timed out once at 60 s (a reviewer's read-only `git show`, 2026-11-10, which then ran unchecked); none failed to start. In S147, counting the agents' transcripts too, which FAIL-009 did not, it missed about 1,590 of about 7,430. The slowest close-guard start on record took 29.8 s; 48 of the 58 slow starts on record took longer than the old 10 s limit. ‹owner's FAIL-009 decision›.<br>• **The Ponytail trial, M1–M6** (first measured sprint; descriptive; read over S148–S150 against the S145–S147 baseline):<br>– **M1:** Step 5a internal 42 chains / 94 cycles, mean **2.24** (baseline 2.02); Codex 37 chains / 87 cycles, mean **2.35** (baseline 2.19). Step 7a one cycle per lens (baseline 5.33). Three chains reached the cap: the 14834 stack and 14838 (5 per lens), and 14841 (6 internal by Q6, 5 Codex).<br>– **M2:** cycle 1 internal 5/88/253, Codex 22/54/74; a blocker in 4 of 42 internal and 14 of 37 Codex first rounds (baseline: 6 of 55 and 8 of 43).<br>– **M3:** 0 post-close defects so far; 19 CI runs listed (descriptive).<br>– **M4:** 120 `(unproven)` NOTEs (89 internal, one of them at Step 7a; 31 Codex). **Hidden: 0.** The one that proved to be a real defect, Step 7a's backdated norm, was made concrete by Codex in the same cycle (QUAL-284). The split between followed up and left open is not reconstructable: the per-task M4 entries the plan asked for (`PLAN-s148.md:771`) were not kept.<br>– **M5: 0** traced to the block. No "Not built" item was overruled at a merge, and no review finding names the block as a cause; the records do not trace each missing-test finding to its cause.<br>– **M6:** 23 `shortcut(<tag>)` comments at the close (the docs job's count at C-2), pointing at 16 register rows; 2 more rows were registered and closed in-sprint (QUAL-248, F10). **Check-5 false positives: 0.** Check 5 caught unregistered `shortcut(pending)` comments three times (CI-W1: four; TASK-14818's merge, found locally; CI-W5a: one), each a miss of merge-checklist item 8, not of the block.<br>• **Where the escapes came from.** CI's one product catch, `init.sql`'s constraint-name collision (CI-W4a), came from a Sonnet-tier implementer and passed both Step-5a lenses, which read the schema but cannot run it on this machine. It is the same class as S144's and S147's escapes: facts no lens can execute. The Orchestrator now applies `init.sql` to a throwaway local PostgreSQL at every gate that changes it.<br>• **Session events, none of which lost work:** Fable's rate limit on 2026-11-09; the floor move on 11-10 (two restarts); a machine restart on 11-17/18; usage limits that stopped a `planner` (11-19), interrupted TASK-14820 once and TASK-14827 (login expired 11-20, resumed 11-23), and cut one Codex run short (TASK-14838 cycle 5, re-run after the reset); several low-memory reaps of local vitest runs and of a CI watcher, none restarted automatically.<br>• **Housekeeping found by this row:** (1) `docs/WORKFLOW.md:447` names `claude-haiku-4-5-20251001` as the newest Haiku, but both `sweep` spawns resolved to `claude-haiku-5-5`. (2) `SPRINT-148.md:11` stops at client 2.1.322; the seat ran 2.1.326 from 11-18 and 2.1.332 from 11-25. (3) FAIL-009's S147 close-guard counts (83 timeouts, 72 start failures) cover the session transcript only; S147's agent transcripts hold 1,086 more timeouts and 349 more start failures.<br>**Signals 1–4:** (1) no post-close defect; (2) no checkpoint needed more than two runs; (3) no refusal on real work, no block on a reviewer spawn, no detector block; on a literal reading the three floor-move probes' `REFUSED — wrong model` answers trip it: a harness lag (the running session still held the 5.1 self-check), fixed by a restart; (4) three Fable spawns ran 5.1 on 2026-11-09, the day the owner named 5.2; client 2.1.301 could not yet resolve it (that evening's probe still got 5.1); acted on at once (client 2.1.322 and the floor move on 11-10). Every other tier ran the newest id of its family |
```

---

## Part B — the reconciliation, and the commands that produce it

**In plain words.** Two independent records say which agents ran. The *transcripts* are what actually ran: one
file per agent. The *log* is what the routing guard saw: one line per launch it checked. If the guard runs every
time, the two agree. In S147 they disagreed by 51, because the guard silently did not run; in S148 they agree
exactly, and the two small differences are explained by records that show what happened.

### B1. Log versus transcripts, by role

| Role | Log `ALLOW` | Transcripts | Gap and its explanation |
|---|---|---|---|
| `reviewer` | 114 | 114 | — (3 transcripts are HTTP 429 non-answers, 2026-11-09; they ran and returned nothing) |
| `planner` | 59 | 59 | — (1 not finished: stopped by the seat at a usage limit, 2026-11-19, `agent-ae6541dc82908ec19`) |
| `constraint-validator` | 59 | 59 | — |
| `test-qa` | 56 | 55 | **1 launch refused by worktree isolation** (log 2026-11-10T15:13:09Z; the session's `tool_result` for `toolu_0146x8EPucWu7pQFbbXJDdHi` is an error, "Refusing to use … as an isolation worktree: … git metadata that could not be resolved"); re-issued at 15:25:02Z and run (`agent-a7f734124e435385e`) |
| `adjudicator` | 43 | 43 | — |
| `backend-infrastructure` | 34 | 34 | — |
| `ux` | 31 | 31 | — |
| `security` | 10 (8 `inherit`, 2 `sonnet`) | 10 | — |
| `payroll-integration` | 9 | 9 | — |
| `trace` | 7 | 7 | — |
| `general-purpose` | 7 (6 `opus`, 1 `sonnet`) **+ 1 `BLOCK`** | 7 | the `BLOCK` (2026-11-25T12:25:49Z, no model) starts no agent; re-issued 15 s later with `sonnet` |
| `rule-engine` | 5 | 5 | — |
| `sweep` | 2 | 2 | — |
| `data-model` | 2 | 2 | — |
| `Explore` (the log writes `explore`) | 1 | 1 | launched inside TASK-14821's agent (`spawnDepth` 2) |
| `fork` | 1 | 1 | launched inside TASK-14804's agent (`spawnDepth` 2, `isFork`); its transcript starts at 14:03:39Z with the parent's copied history; its own first message is 14:11:27.731Z, the log line's second |
| **Total** | **440 + 1 `BLOCK`** | **439** | **"log ALLOW 440 / transcripts 439"**: 438 matched within 1 s, 1 fork matched after skipping its copied history, 1 failed launch; no transcript without a log line |

**Cross-check by Agent calls.** The seat's three session transcripts hold 439 `Agent` tool calls in the window, and
the two parent agents hold 2 more: 441 calls, against 441 spawn lines (440 `ALLOW` + 1 `BLOCK`). Paired in time
order, they agree role for role, except two adjacent swaps on 2026-11-10 where a `reviewer` and an `adjudicator`
probe were launched one second apart and their guards finished in the other order.

**Codex runs are not agents.** Codex ran as a shell script, so its 108 review runs (plus one cut by its usage
limit) have no routing-log line and no subagent transcript; each passed the close guard as a shell command.

**Resumes are not counted.** A resumed agent (`SendMessage`) writes into its existing transcript and the guard
logs nothing for it (for example, the refinement's cycle-2 Fable attempt was a resume of the cycle-1 reviewer,
which hit HTTP 429 at 13:19:15Z).

### B2. The hooks: did the 60-second limit hold?

| Guard | Runs in S148 | Timed out at 60 s | Failed to start | Slow starts on record |
|---|---|---|---|---|
| **Routing guard** (before each Agent call) | **441** (one per Agent call; each wrote its log line) | **0** | **0** | No hook record (it prints nothing when it works). Time from the Agent call to its log line: median 3.3 s, 90 % within 7.4 s, 24 over 10 s, 8 over 20 s, 3 over 30 s: 33.0 s (the failed `test-qa` launch, 2026-11-10T15:12:36Z) and 155 s and 157 s (the two floor-move probes right after the 2026-11-10 client restart). Those two include a wait before the guard started: a guard still running at 60 s is cancelled and recorded, and no such record exists. So these intervals are upper bounds on the guard's own time. |
| **Close guard** (before each Bash or PowerShell command) | **about 22,287** (unique shell tool calls: the seat 2,548 Bash + 129 PowerShell; agents 19,287 Bash + 323 PowerShell) | **1**: 61.69 s, in a `reviewer` (TASK-14801's 5a, `agent-a9dc80a1f14fc6a34`), on a read-only `git show` called at 2026-11-10T15:13:31Z; Claude Code let it run, unchecked, and it succeeded | **0** (S147's "-File … does not exist" fault is gone) | **58** records, written only because PowerShell printed a start-up error (`Attempting to perform the InitializeDefaultDrives operation on the 'FileSystem' provider failed`, exit 0): 53 in agents' transcripts, 5 in the seat's; one of them on a `Monitor` call. Durations 4.2 s to **29.8 s** (the slowest, 2026-11-10T14:48:23Z); 48 over 10 s, 20 over 20 s, none between 30 and 60 s. |
| *S147, for comparison* (same method; 2026-10-27 → S147's close line) | 200 spawns; about 7,426 shell commands (seat 975; agents 6,451) | routing 38; close guard 80 in the seat's transcript (FAIL-009 recorded 83 over a slightly longer span) + **1,086 in agents' transcripts** (1,042 Bash, 44 PowerShell), at the old 10 s limit | routing 13; close guard 72 in the seat's + **349 in agents'** | — |

**What the timing records can and cannot show.** Claude Code writes a hook record only when the hook fails, times
out or prints something. The close guard prints nothing when it works, so about 22,230 of its S148 runs left no
timing at all. "Did 60 s hold?" is therefore answered for every failure (one) and every run that happened to
print PowerShell's start-up error (58), not for the whole distribution.

**The slow moments cluster.** The two slowest close-guard starts (27.4 s and 29.8 s, 14:48Z), the routing guard's
22–33 s intervals (15:11–15:13Z), the one 60 s timeout (15:13–15:15Z) and the refused worktree (15:12–15:18Z) all
fall on the afternoon of 2026-11-10, during wave 1's dispatch, with several agents running on the 8 GB machine.

### B3. The close guard on the close commit

- The log's S148 `close` lines: exactly one, `2026-11-26T08:12:57Z | close | S148 | claude-fable-5-2 | PASS | step-7a reviewer artifact on the floor model` (line 1283). No other `close` line for S148, and no seam-test (`S99`) line in the window.
- The close commit's own Bash call in `a53199d4…jsonl`: `toolu_01N4aC6bqcSfJAq8bDdYEw9o`, called 08:12:54.202Z (`cd /c/Users/b200895/source/repos/StatsTid && git add … && git commit -m "S148 sprint close…"`); the guard's line at 08:12:57Z; the result at 08:13:04.949Z, `is_error: false`; the commit's own time 08:13:04Z. No `hook_cancelled` or `hook_non_blocking_error` on that call; the session file holds none at all.
- So the guard ran on the close commit, in about three seconds, and passed. No waiver file for S148 exists in `.claude/reviews/`.

### B4. Where the review counts come from

- **Chains (M1).** One lens on one scope, its fix-up re-reviews included. A stack reviewed task by task at cycle 1 and then as one stack counts as one chain whose cycle 1 was split: wave 3 (14813, 14814; both lenses), and on Codex only the 14832, 14834 and HR-facts (14836) stacks and the QUAL-268 stack. So 95 internal review files make 94 cycles, and 92 Codex runs make 87.
  - Internal cycles per chain: 1 cycle × 11 chains (14800, 14801, 14803, 14805, 14809, 14810, 14816 stack, 14819, 14848, 14849, 14839); 2 × 18 (14802, 14806, 14807, 14808, 14812, wave 3, wave 3b, 14815, 14818, 14820, 14821, 14830, 14831 stack, 14846, 14832 stack, QUAL-268 stack, 14850, 14853); 3 × 9 (14804, 14811, 14840, 14845, 14822, 14823 stack, 14825, T14b stack, HR-facts stack); 4 × 1 (14842); 5 × 2 (14834 stack, 14838); 6 × 1 (14841).
  - Codex cycles per chain: 1 × 8 (14801, 14805, 14809, 14816 stack, 14819, 14848, 14849, 14839); 2 × 15 (14800, 14806, wave 3, wave 3b, 14815, 14818, 14820, 14821, 14830, 14831 stack, 14846, 14832 stack, QUAL-268 stack, 14850, 14853); 3 × 10 (14802, 14804, 14811, 14840, 14845, 14822, 14823 stack, 14825, T14b stack, HR-facts stack); 4 × 1 (14842); 5 × 3 (14841, 14834 stack, 14838).
  - No Codex chain: 14803, 14807, 14808, 14810, 14812.
  - *Sensitivity:* counting the split cycle-1 reviews as separate cycles gives internal 95 / 42 = 2.26 and Codex 92 / 37 = 2.49.
- **Cycle-1 B/W/N (M2).** Where the sprint log states the counts (wave 0 to wave 3, 14840, 14841, 14842), they are the log's (the register's rule). Elsewhere they are the artifact's finding headings, each list read by hand. Three counter false hits were removed: 14825's "B12" item, and the summary bullets that repeat 14842's and 14845's warnings. The internal per-chain list, as B/W/N: 14800 0/0/5 · 14801 0/0/5 · 14802 0/2/10 · 14803 0/0/4 · 14804 0/4/3 · 14805 0/1/4 · 14806 0/2/7 · 14807 0/2/4 · 14808 0/1/2 · 14809 0/1/5 · 14810 0/0/6 · 14811 0/2/4 · 14812 0/1/5 · 14842 0/2/6 · 14840 0/1/8 · 14841 0/4/9 · wave 3 0/7/10 · wave 3b 0/1/6 · 14815 0/1/8 · 14816 stack 0/1/7 · 14818 2/4/9 · 14819 0/1/6 · 14820 0/4/11 · 14821 1/3/6 · 14845 0/2/6 · 14822 0/1/6 · 14823 stack 0/3/6 · 14825 0/3/5 · T14b 0/4/5 · 14830 0/4/7 · 14831 stack 1/2/3 · 14846 0/2/6 · 14848 0/0/6 · 14849 0/1/3 · 14832 stack 0/1/5 · 14834 stack 0/7/8 · HR-facts stack 0/4/6 · 14838 0/3/9 · QUAL-268 stack 1/4/4 · 14850 0/1/7 · 14839 0/1/5 · 14853 0/0/6.
- Codex, as B/W/N: 14800 0/2/1 · 14801 0/0/1 · 14802 0/1/3 · 14804 1/4/1 · 14805 0/2/4 · 14806 0/2/3 · 14809 0/0/3 · 14811 0/0/2 · 14842 1/0/4 · 14840 0/1/1 · 14841 0/2/2 · wave 3 0/4/6 · wave 3b 0/2/1 · 14815 0/1/1 · 14816 stack 0/1/1 · 14818 2/1/0 · 14819 0/0/2 · 14820 1/1/4 · 14821 1/1/4 · 14845 0/0/2 · 14822 2/0/1 · 14823 stack 1/1/0 · 14825 0/1/2 · T14b 2/4/3 · 14830 0/2/1 · 14831 stack 1/1/3 · 14846 0/1/1 · 14848 0/2/1 · 14849 0/0/2 · 14832 stack 1/3/3 · 14834 stack 3/3/2 · HR-facts stack 2/3/4 · 14838 2/2/2 · QUAL-268 stack 2/4/1 · 14850 0/1/1 · 14839 0/0/1 · 14853 0/1/0.
- **Models.** All 95 internal 5a files and the Step-7a file carry `claude-fable-5-2` (93 as `reviewed-by-model:`, 3 as `reviewed-by:`); of the 40 S148 adjudication files after the refinement, 39 carry `claude-fable-5-2` on their model line, and one (`S148-TASK-14815-5a-adjudication-c1.md`) has no model line, only its save note "returned by the adjudicator agent (Fable 5.2)". Its spawn is among the 40 `adjudicator` transcripts on `claude-fable-5-2`. The four refinement adjudications carry `claude-fable-5-1` (cycle 1) and `claude-opus-5-5` (cycles 2–4, the seat).
- **`(unproven)` NOTEs (M4).** 131 heading lines carry "unproven" across the 5a and 7a files; removed: one duplicate file (14845's Codex cycle 1 is saved twice), two lines that are not findings (a summary sentence; Step 7a's NOTE *about* two unproven census items), five repeats the files mark as carried or restate (14804 c3, 14811 c2 ×2, 14834 c5, 14846 Codex c2, 14811 Codex c2: six lines), and two lines without the marker form (14820's "reinstated users stay unproven", 14842 Codex c3). That leaves 120: internal 89 (88 in Step 5a, 1 in Step 7a), Codex 31; 53 and 24 of them in cycle 1.
  - **Hidden = 0, and how that was checked.** Every defect found later in S148 was looked for among the earlier `(unproven)` NOTEs: CI-W4a's `init.sql` collision (14815's two lenses raised none on it); the docs-job reds (merge-checklist misses); Step 7a's backdated norm (raised as `(unproven)` by Fable and made concrete by Codex in the same cycle, so followed up, not hidden); the C-1 census's QUAL-282 (it came from 14845's cycle-2 `(unproven)` NOTE 2, which the seat turned into the census, ruling C2-R4: followed up). The 14834 stack's 9999-12-31 warning is the same *class* as 14821's earlier `(unproven)` NOTE 5, but a different route and code line, so it is not counted as that NOTE coming true.
  - **Followed up, named in the sprint log (not a total):** 14807's and 14822's "a split alone moves no pay" → ruling R9 → pinned by TASK-14828-T (`SPRINT-148.md:2257`); 14822's c2 correction-after-transfer → C2-R5 (`:2303`); 14845's lock interactions → a CI-W4 watch item (`:2009`, likelier safe, not proven); 14845's sibling check-then-act routes → the C-1 census (`:4770-4783`); 14831's delivery envelope → an assertion added (W11, `:3331`); 14818's "today is a Monday" → a non-Monday run owed and served by CI-W4 (`:1803`); 14839's "payslips unchanged" → C-2 green; Step 7a's backdated norm → QUAL-284.

### B5. The commands

Run from Git Bash. `T` is any scratch folder. Python is not installed on this machine; everything is
`grep`/`awk`/`join`.

```bash
P=~/.claude/projects/C--Users-b200895-source-repos-StatsTid
R=/c/Users/b200895/source/repos/StatsTid
L=$R/.claude/telemetry/model-routing.log
T=<scratch folder>
FROM=2026-11-09; TO=2026-11-26T08:13:04Z      # S148 opened; the close commit df582c20

# 1. Spawns by role, from the transcripts (439), and which did not finish (1)
cd "$P"
grep -m1 -o '"timestamp":"[^"]*"' */subagents/agent-*.jsonl \
 | sed -E 's#^([0-9a-f]{8})[^/]*/subagents/(agent-[a-z0-9]+)\.jsonl:"timestamp":"([^"]*)"#\2 \1 \3#' | sort > $T/ts.txt
grep -H -o '"agentType":"[^"]*"' */subagents/agent-*.meta.json \
 | sed -E 's#^[^/]*/subagents/(agent-[a-z0-9]+)\.meta\.json:"agentType":"([^"]*)"#\1 \2#' | sort > $T/types.txt
grep -H -o '"finished":[a-z]*' */subagents/agent-*.meta.json \
 | sed -E 's#^[^/]*/subagents/(agent-[a-z0-9]+)\.meta\.json:"finished":([a-z]*)#\1 \2#' | sort > $T/fin.txt
join $T/ts.txt $T/types.txt | join -a1 - $T/fin.txt | awk -v f=$FROM -v t=$TO '$3>=f && $3<=t' | sort -k3 > $T/win.txt
wc -l < $T/win.txt
awk '{print $4}' $T/win.txt | sort | uniq -c | sort -rn
awk '$5!="true"' $T/win.txt

# 2. Resolved model per transcript, from assistant messages only
#    ("model" also appears in request blobs, naming the advisor tool's model; those are not the agent's)
files=$(awk '{print $2"*/subagents/"$1".jsonl"}' $T/win.txt)
grep -H -o '"message":{"model":"claude-[a-z0-9-]*"' $files \
 | sed -E 's#^[^/]*/subagents/(agent-[a-z0-9]+)\.jsonl:"message":\{"model":"([^"]*)"#\1 \2#' | sort -u > $T/models.txt
join $T/models.txt <(awk '{print $1, $4}' $T/win.txt | sort) | awk '{print $3, $2}' | sort | uniq -c
awk '{print $1}' $T/models.txt | sort -u | join -v1 <(awk '{print $1, $4, $3}' $T/win.txt | sort) -   # 3 answerless reviewers

# 3. The log's lines in the window (842 .. 1283)
sed -n '842,1283p' $L > $T/log.txt
awk -F' [|] ' '{print $2, $5}' $T/log.txt | sort | uniq -c             # 440 spawn ALLOW, 1 spawn BLOCK, 1 close PASS
awk -F' [|] ' '$2=="spawn"{print $5, $3, $4}' $T/log.txt | sort | uniq -c
grep -c S99 $T/log.txt                                                  # 0 seam-test lines

# 4. Match each ALLOW line to a transcript of the same role, 0-300 s later
awk -F' [|] ' '$2=="spawn" && $5=="ALLOW"{print $1, tolower($3)}' $T/log.txt > $T/allow.txt
awk '{print $3, tolower($4), $1}' $T/win.txt > $T/tr.txt
awk 'function s(t){split(substr(t,1,19),a,/[-T:]/);return mktime(a[1]" "a[2]" "a[3]" "a[4]" "a[5]" "a[6])}
 FNR==NR{n++;lt[n]=s($1);lr[n]=$2;lx[n]=$1;next}{m++;tt[m]=s($1);tr[m]=$2;tx[m]=$1" "$3}
 END{for(i=1;i<=n;i++){b=0;for(j=1;j<=m;j++)if(!u[j]&&tr[j]==lr[i]){d=tt[j]-lt[i];if(d>=-5&&d<=300&&(!b||d<bd)){b=j;bd=d}}
  if(b){u[b]=1;k++}else print "LOG-ONLY",lx[i],lr[i]}
  for(j=1;j<=m;j++)if(!u[j])print "TRANSCRIPT-ONLY",tx[j],tr[j]; print "matched",k}' $T/allow.txt $T/tr.txt
#   -> matched 438; LOG-ONLY 2026-11-10T14:11:27Z fork; LOG-ONLY 2026-11-10T15:13:09Z test-qa;
#      TRANSCRIPT-ONLY 2026-11-10T14:03:39.097Z agent-a48292d169d5f69a2 fork
grep -o '"isFork":true\|"parentAgentId":"[^"]*"\|"spawnDepth":[0-9]' 0175149f*/subagents/agent-a48292d169d5f69a2.meta.json
grep -m1 -o '"timestamp":"2026-11-10T14:1[^"]*"' 0175149f*/subagents/agent-a48292d169d5f69a2.jsonl     # 14:11:27.731Z
grep -o 'Refusing to use [^"]\{0,170\}' 0175149f-*.jsonl                                                # the failed test-qa launch

# 5. Every hook record in the window, seat and agents
grep -H '"attachment":{"type":"hook_' *.jsonl */subagents/agent-*.jsonl | awk -v f=$FROM -v t=$TO '
 function g(re,  x){ if(match($0,re)){x=substr($0,RSTART,RLENGTH); sub(/^"[a-zA-Z]+":"?/,"",x); sub(/"$/,"",x); return x} return "" }
 { ts=g("\"timestamp\":\"[^\"]*\""); if(ts<f||ts>t) next;
   print ts, g("\"type\":\"hook_[a-z_]*\""), g("\"hookName\":\"[^\"]*\""), "dur=" g("\"durationMs\":[0-9]+"),
         "timedOut=" g("\"timedOut\":[a-z]+"), "limit=" g("\"timeoutMs\":[0-9]+") }' > $T/hooks.txt
awk '{print $2, $3, $5, $6}' $T/hooks.txt | sort | uniq -c
#   -> 1 hook_cancelled PreToolUse:Bash timedOut=true limit=60000; 57 hook_success PreToolUse:Bash;
#      1 hook_success PreToolUse:Monitor; 1 hook_system_message PostToolUse:Bash (a client tip, not a failure)
grep hook_success $T/hooks.txt | sed 's/.*dur=\([0-9]*\).*/\1/' | sort -n | tr '\n' ' '
#    (for S147, set FROM=2026-10-27 TO=2026-11-05T16:33 and re-run steps 5 and 6)

# 6. Denominators: shell and Agent tool calls, unique by tool_use id
grep -h -o '"type":"tool_use","id":"toolu_[A-Za-z0-9]*","name":"\(Bash\|PowerShell\|Monitor\|Agent\)"' $files \
 | sort -u | grep -o '"name":"[A-Za-z]*"' | sort | uniq -c                          # agents
for s in 0175149f 389c418e a53199d4; do
  grep '"type":"tool_use","id":"toolu_[A-Za-z0-9]*","name":"\(Bash\|PowerShell\|Agent\)"' $s*.jsonl \
   | awk -v f=$FROM -v t=$TO '{ if (match($0,/"timestamp":"[^"]*"/)) ts=substr($0,RSTART+13,24); if (ts>=f && ts<=t) print }' \
   | grep -o '"type":"tool_use","id":"toolu_[A-Za-z0-9]*","name":"[A-Za-z]*"' | sort -u | grep -o '"name":"[A-Za-z]*"' | sort | uniq -c
done                                                                                  # the seat

# 7. The routing guard's interval: each Agent call to its log line (441 pairs, in time order)
{ for s in 0175149f 389c418e a53199d4; do grep '"type":"assistant"' $s*.jsonl; done
  grep -h '"type":"assistant"' 0175149f*/subagents/agent-{a7b9324ca7deb4f36,a94cd019d00c5c17c}.jsonl; } \
 | grep '"name":"Agent"' | awk -v f=$FROM -v t=$TO '{ ts=""; if (match($0,/"timestamp":"[^"]*"/)) ts=substr($0,RSTART+13,24);
   if (match($0,/"type":"tool_use","id":"toolu_[A-Za-z0-9]*","name":"Agent","input":\{[^}]*"subagent_type":"[^"]*"/)) {
     x=substr($0,RSTART,RLENGTH); match(x,/toolu_[A-Za-z0-9]*/); id=substr(x,RSTART,RLENGTH);
     match(x,/"subagent_type":"[^"]*"/); st=substr(x,RSTART+17,RLENGTH-18); if (ts>=f && ts<=t) print ts, id, tolower(st) } }' \
 | sort -u -k2,2 | sort > $T/calls.txt                                               # 441
awk -F' [|] ' '$2=="spawn"{print $1, tolower($3), $5}' $T/log.txt > $T/spawnlines.txt   # 441
paste -d' ' $T/calls.txt $T/spawnlines.txt | awk 'function s(t){split(substr(t,1,19),a,/[-T:]/);return mktime(a[1]" "a[2]" "a[3]" "a[4]" "a[5]" "a[6])}
 {print s($4)-s($1)}' | sort -n | awk '{a[NR]=$1} END{print "median",a[int(NR/2)],"p90",a[int(NR*.9)],"max",a[NR]}'

# 8. The close commit's own call
grep 'toolu_01N4aC6bqcSfJAq8bDdYEw9o' a53199d4*.jsonl | grep -o '"is_error":[a-z]*\|"timestamp":"[^"]*"'
grep -c '"type":"hook_cancelled"\|"type":"hook_non_blocking_error"' a53199d4*.jsonl     # 0
grep '| close |' $L | tail -2
ls $R/.claude/reviews | grep -i 'WAIVED'                                                 # no SPRINT-148 file

# 9. Review artifacts
cd $R/.claude/reviews
ls S148-*5a-reviewer*.md | wc -l                                                         # 95
ls S148-*5a-codex* | sed -E 's/\.(exit|log|raw\.md|md)$//; s/-c1$//' | grep -v limited | sort -u | wc -l   # 92
grep -h -m1 -o 'reviewed-by[a-z-]*: *claude-[a-z0-9-]*' S148-*5a-reviewer*.md SPRINT-148-step7a-reviewer.md \
 | sed 's/.*: *//' | sort | uniq -c                                                      # 96 claude-fable-5-2
cnt(){ printf '%s B%s W%s N%s\n' "$1" \
  $(grep -c -E '^(#+ |\*\*|[0-9]+\. \*\*|- \*\*)(BLOCKER|B-?[0-9]+[:,. —])' "$1") \
  $(grep -c -E '^(#+ |\*\*|[0-9]+\. \*\*|- \*\*)(WARNING|W-?[0-9]+[:,. —])' "$1") \
  $(grep -c -E '^(#+ |\*\*|[0-9]+\. \*\*|- \*\*)(NOTE|N-?[0-9]+[:,. —])' "$1"); }   # then read each hit list by hand
grep -i -E '^(#+ |\*\*|[0-9]+\. \*\*|- \*\*).{0,40}(NOTE|N-?[0-9]+).{0,120}unproven' S148-*5a-*.md SPRINT-148-step7a-*.md   # M4, then dedupe

# 10. Shortcuts at the close (M6)
git -C $R grep -n -i -E '(//|#|--|/\*|^\s*\*)\s*shortcut\s*[:(]' df582c20 -- src docker frontend tests tools ':!tools/check_docs.py' | wc -l   # 23
git -C $R grep -h -o -i -E 'shortcut\((QUAL|SEC|F)[A-Z0-9-]*\)' df582c20 -- src docker frontend tests tools ':!tools/check_docs.py' | sort -u | wc -l   # 16
```

---

## Part C — FAIL-009: the evidence for the owner's decision

### What the question is, in plain words

Two small PowerShell scripts guard this project's process.
- The **routing guard** runs before every agent launch. It checks that the agent gets the right model tier: for
  example, that a review runs on Fable and not on a cheaper model.
- The **close guard** runs before every shell command. Almost always it just sees "this is not a sprint close"
  and lets the command through. On the one command per sprint that commits the close, it refuses unless the
  reviews are on file, on the right model, and the other close conditions hold.

Both guards **fail open**: if Claude Code cannot start the script, or the script has not answered within
60 seconds, the action goes ahead as if the guard had approved it. **Failing closed** (Claude Code's
`"onFailure": "block"`) would refuse the action instead. In S147 the guards silently missed a quarter of the
agent launches (FAIL-009). The fix went in before S148: absolute script paths, and 60 seconds instead of 10. The
owner chose to keep fail-open until S148's close and decide then, on this sprint's evidence (`ROADMAP.md:788-790`).

One thing the choice does **not** change: speed. A slow guard makes the action wait the same time either way. The
choice only decides what happens when the guard has not answered after 60 seconds, or cannot start at all:
fail-open lets the action through unchecked, fail-closed refuses it.

### What failed open in S148, and how often

| | S148 | S147, for comparison |
|---|---|---|
| Routing guard: launches that went ahead unchecked | **0 of 441** | 51 of 200 (38 timeouts, 13 start failures) |
| Close guard: shell commands that went ahead unchecked | **1 of about 22,300** (a timeout) | about 1,590 of about 7,430 (1,166 timeouts, 421 start failures; seat and agents together) |
| Close guard on the close commit itself | ran and passed, in about 3 s | ran and passed |

The one S148 miss: on 2026-11-10 at 15:13Z, a reviewer agent ran `git show` (a read-only look at a file) and the
close guard had not answered after 61.7 seconds. Claude Code let the command run unchecked, and it succeeded. It
was not a close, so nothing the guard protects was at stake.

### Did 60 seconds hold?

- **Routing guard: yes, every time.** All 441 runs finished and wrote their line. From the agent launch to the
  guard's line took 3.3 seconds in the middle case, and over 30 seconds three times. Two of those (155 s and 157 s)
  were right after a client restart, and the wait was before the guard started (a guard still running at 60 s
  would have been cancelled and recorded; none was).
- **Close guard: every time but one.** The slowest start on record took 29.8 seconds. 48 of the 58 starts on
  record took longer than the old 10-second limit, so the old limit would have failed them all; the new one failed
  one run, at 61.7 seconds.
- **The limit of this evidence.** A guard that works prints nothing, and Claude Code then records no duration.
  So we see the one failure and 58 slow starts that happened to print a PowerShell start-up error; the other
  roughly 22,200 close-guard runs left no timing.

### What failing closed would cost on this machine

- **Routing guard.** In S148 it would have refused **no** launch. When a launch is refused, the Orchestrator sees an
  error at once and re-issues it; that happened once in S148 for a different reason (the 2026-11-25 `BLOCK`,
  re-issued 15 seconds later).
- **Close guard.** In S148 it would have refused **one** command: the reviewer's `git show`. The reviewer would
  have seen an error and, most likely, retried. The 58 slow starts would not have been refused; they took under
  30 seconds, and they cost the same wait in both modes.
- **The risk is not the average, it is a bad stretch.** The slow starts bunch together when this 8 GB machine is
  under memory pressure. On the afternoon of 2026-11-10, during wave 1 with several agents running, the two
  slowest starts on record, routing-guard waits of 22–33 seconds, the one 60-second timeout and a worktree that
  Claude Code refused to create all fell within half an hour. If PowerShell could not start at all for such a
  stretch, a fail-closed close guard would refuse **every shell command of the Orchestrator and of every running
  agent** for as long as it lasted, each after a 60-second wait. Agents in mid-task would see their commands fail;
  fail-open would let those commands run unchecked instead. S148 had no such stretch; its worst produced one
  timeout.

### What failing closed would buy

- **Routing guard.** No agent could ever launch unchecked. But each role's definition file already fixes its
  model, so the guard is the second layer, not the only one (FAIL-009); and this register's reconciliation finds a
  missed launch after the fact (S148: none).
- **Close guard.** Only one command per sprint matters to it: the close commit. A close that slipped through
  unchecked would still be visible afterwards: the log would have no `close` line, or the commit's call would carry
  a hook-failure record. This register checks both every sprint. The catch is timing: that check happens at C-5,
  after the push.

### The options, with S148's evidence for each

| Option | What it protects | What S148 says it would have cost | What S148 says it would have caught |
|---|---|---|---|
| **(a) Fail closed on the routing guard only** | every agent launch | 0 refused launches | nothing: no launch went unchecked |
| **(b) Fail closed on the close guard only** | the close commit (one command per sprint) | 1 refused read-only command in about 22,300; and the exposure to a bad stretch stopping all shell work | nothing: the close commit was checked anyway |
| **(c) Both** | both of the above | the sum: 1 refused command; the same exposure as (b) | nothing |
| **(d) Neither (today's setting)** | relies on the reconciliation and the close commit's hook record to notice misses after the fact | none | the detective checks saw everything in S148: 441 of 441 launches and the close commit's own check |

Two facts for whichever option is chosen, neither a recommendation:
- `"onFailure": "block"` needs client 2.1.295 or later (the seat runs 2.1.332) and has not yet been exercised on
  this machine (FAIL-009, "Residual").
- The 60-second limit itself is unchanged by the choice. S148 shows it is wide enough for every start on record but
  one; it does not show how often a start would fail outright, because none did.

---

## Part D — what could not be reconstructed, and why

1. **The close guard's run time for runs that printed nothing** (about 22,230 of about 22,290). Claude Code
   records a hook's duration only when the hook fails, times out or writes output. So Part C's "did 60 s hold"
   rests on the one failure and the 58 noisy runs, not on the full distribution.
2. **The routing guard's own run time.** Only the interval from the Agent call to the guard's log line can be
   measured; it includes any wait before the guard starts, so it is an upper bound.
3. **M4's split between "followed up" and "left open".** The plan asked each task's sprint-log entry to record
   every `(unproven)` NOTE with its disposition (`PLAN-s148.md:771`); the log does not label these. Tracing all 120
   NOTEs through the rulings and adjudication files was not done, so the split is "not reconstructable" (Part B4
   lists the follow-ups the log names). "Hidden = 0" is reconstructable, and how it was checked is in Part B4.
4. **M5's full cause trace.** The records show no "Not built" item overruled and no finding that names the block,
   but they do not trace each "no test can fail" or "missing guard" finding to its cause (brief, refinement, block
   or implementer). So "0 traced to the block" is what the records show, not a proven zero.
5. **TASK-14802's internal cycle-1 artifact is condensed** ("notes and checks condensed by the Orchestrator"), so
   its 10 NOTEs come from the sprint log, and any `(unproven)` marks among them are not visible.
6. **Two small log-versus-artifact disagreements,** resolved by the register's rule in the log's favour: 14840's
   cycle 1 (log 8 NOTEs, artifact 7 NOTE headings) and 14841's (log 9, artifact 8).
7. **Tokens by model:** not measured (the register asks for them on demand only).
8. **The close push's CI run:** not in the sprint log when this was drafted; C-5 records it.
9. **Resumes:** not counted. The log has no line for a resume, and the transcripts count agents, not resumes.

---

## Notes for the Orchestrator (not for the register)

### Brief contradictions

1. **"M6: shortcuts registered (23 at the close)."** The code shows 23 `shortcut(<tag>)` *comments* (the docs
   job's "shortcut registry (23)", `SPRINT-148.md:4902`), pointing at **16 register rows** (QUAL-249, -250, -262,
   QUAL-BF-001 … -011, SEC-059, SEC-066; `git grep` at `df582c20`). Two more rows were registered and closed inside
   the sprint (QUAL-248, F10). The row says "23 comments, 16 rows, 2 more closed".
2. **"Reviews and adjudications on Fable 5.2."** True from 2026-11-10 only. On 2026-11-09: refinement cycle 1's
   review and adjudication on Fable 5.1 (`claude-fable-5-1` in their transcripts), cycles 2–5 on the labelled Opus
   stand-in, and adjudications 2–4 by the seat (`SPRINT-148.md:140-204`). The row says so.
3. **"The `sweep` agent on Haiku 5.5"** matches the transcripts (`claude-haiku-5-5` ×2), but
   `docs/WORKFLOW.md:447`, the reference for "newest per tier", still names `claude-haiku-4-5-20251001`. Recorded as
   housekeeping in the row; the fix is a WORKFLOW edit.
4. **"There are several sessions, because the machine restarted."** Four session files have activity in the
   window: `0175149f` (carried over from S147, to 2026-11-18), `af6dbb52` (40 seconds on 2026-11-10 at 08:00Z, client
   2.1.322, no spawns: a check after the client update), `389c418e` (2026-11-18 → 11-25) and `a53199d4` (2026-11-25 →
   the close and C-5). One change was a machine restart (2026-11-17/18, `SPRINT-148.md:1863-1867`); the others were
   client updates and session restarts.
5. **Not from the brief, but a record correction:** FAIL-009's S147 close-guard counts (83 and 72) are from the
   session transcript only; S147's agent transcripts hold 1,086 more timeouts and 349 more start failures. FAIL-009's
   conclusion is unchanged, and stronger.

### Needs a ruling

1. **The FAIL-009 decision** is the owner's (Part C), asked once and explained first; the row carries a placeholder.
2. **The chain convention for split cycle-1 reviews.** A stack reviewed task by task at cycle 1 and then as a stack
   is counted here as one chain with a split cycle 1 (internal 42 / 94, Codex 37 / 87). Counting each split review
   as its own cycle gives 95 and 92. S149 and S150 should count the same way; the Orchestrator picks one.
3. **Signal 3, literally tripped.** The three floor-move probes answered `REFUSED — wrong model` (2026-11-10). The
   row calls it a harness lag, fixed by a restart, not a refusal on real work. Whether the register should count it
   as a trip is the Orchestrator's call.
4. **Agents launching agents.** Two implementers each launched a read-only helper (a `fork`, an `Explore`). The
   guard saw both, and both ran on the expected tier. Whether this needs a rule is not for this draft; the row
   records it neutrally.
5. **M4's split.** Accept "not reconstructable" for S148, or order a trace of the 120 NOTEs; either way, S149's
   task entries could carry the M4 line the plan asked for.

---

## Revision history

| Draft | Date | By | Change |
|---|---|---|---|
| 1 | 2026-11-26 | `planner` (Opus 5.5) | First draft: the S148 register row; the log-versus-transcripts reconciliation (log ALLOW 440 / transcripts 439, one to one); the hook evidence (0 routing-guard misses of 441, 1 close-guard timeout at 60 s in about 22,300 commands, 0 start failures); M1–M6; the FAIL-009 evidence for the owner; the commands. |
