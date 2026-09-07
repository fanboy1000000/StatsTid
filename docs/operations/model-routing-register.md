# Model-routing register — is the delegation framework holding?

<!-- anchor-sprint: 138 -->

**Purpose.** Since 2026-09-07 planning and review run on the most capable model and implementation on
cheaper ones (`docs/WORKFLOW.md`, section "Model Routing"). This register is the thin monitor the owner
asked for: one row per sprint, filled at close from artifacts the close guard already requires, so that if
the routing starts producing errors the trend shows it. It is deliberately small — no per-task attribution,
no score, no gate. **Read it for direction over three sprints, never for a verdict from one row**: a sprint
has roughly ten tasks and the numbers cannot carry more than that.

**Where the numbers come from.**
- *Spawns by model* and *guard blocks*: `.claude/telemetry/model-routing.log` (local, gitignored; one line
  per Agent spawn written by `model-routing-guard.ps1`, plus one `close` line per sprint written by
  `sprint-close-guard.ps1`). Count a sprint's lines by date range.
- *Review findings*: the Step-7a artifacts under `.claude/reviews/` and the Step-5a subsections of the
  sprint log — counts the close guard already requires to exist, never a self-filled field.
- *CI iterations to green* and *post-close defects*: the sprint log's CI sections and any post-close
  review section.
- *Tokens by model* (on demand, when a row looks odd — not every sprint): the session transcript under
  `%USERPROFILE%\.claude\projects\C--Users-b200895-source-repos-StatsTid\<session>.jsonl` carries a
  `"model"` and a `"usage"` object per assistant message, and each subagent transcript under the session's
  `tasks\` folder does the same. Three commands answer "which model spent what":

  ```bash
  J=~/.claude/projects/C--Users-b200895-source-repos-StatsTid/<session>.jsonl
  grep -o '"model":"claude-[a-z0-9-]*"' "$J" | sort | uniq -c                       # messages per model
  grep -o '"model":"claude-[a-z0-9-]*"\|"output_tokens":[0-9]*' "$J" | paste - - | awk -F'[":]' '{t[$4]+=$NF} END {for (m in t) print m, t[m]}'   # output tokens per model
  grep -o '"name":"Agent","input":{[^}]*' "$J" | grep -o '"subagent_type":"[^"]*"\|"model":"[^"]*"' | sort | uniq -c   # spawns as requested
  ```
  If the transcript format changes and these stop working, record "not measured" in the row rather than a
  stale number, and decide then whether a script is worth writing.

**When to reopen the routing.** Any of these, sustained over the trailing three sprints, puts the routing
on the entropy-scan agenda for the owner's decision (nothing moves automatically):
1. a defect found AFTER close that traces to a cheaper-tier implementer's output;
2. CI needing more than two runs to go green;
3. any reviewer refusal (`verdict: REFUSED`) or any guard BLOCK on a reviewer spawn.

| Sprint | Orchestrator model by phase | Agent spawns by model (guard blocks) | Review findings — 5a B/W · 7a B/W (internal lens) | CI runs to green | Post-close defects | Notes |
|--------|-----------------------------|--------------------------------------|---------------------------------------------------|------------------|--------------------|-------|
| **S138 (baseline, pre-routing)** | Fable through refinement, plan and wave 1; **Opus from mid-sprint** (owner switched for token reasons) through close and CI remediation | 19 spawns, all `general-purpose`, **all inherited the session model** (Fable or Opus; no routing existed). Guard blocks: n/a. Session profile: 704 tool calls, 65 CI polls, ~240 build/test invocations, 100 direct Orchestrator edits | 5a: **2 B** (create-POST audit gap; profile GET refused terminated employees) / 0 W · 7a: 0 B / **5 W** + 6 N (boundary `<`→`<=` + concept rename; ADR stated the replaced rule; asymmetry comment wrong; follow-up registered nowhere; diagnostic read could destroy the correction). Codex 5a 1 W, 7a 1 W | **3** (6 red → 2 red → green; 1 product defect = worklist order by UUID, 5 test defects incl. 2 calendar-dependent pins) | Post-close external review: 1 W (weak `NotEqual` pin), 1 N that exposed a small product gap (`reversedSince` answered false when unknown); Orchestrator pass: 1 phantom method name in ADR-040, 7 rename leftovers | The control row. Every defect here was produced on Fable or Opus — capability did not prevent them; review caught them. `QUAL-153` (calendar-floating pins) registered. |
| S139 | | | | | | |
