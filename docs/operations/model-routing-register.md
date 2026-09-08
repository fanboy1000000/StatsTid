# Model-routing register — is the delegation framework holding?

<!-- anchor-sprint: 139 -->

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
| **S139 (first live run)** | **Fable throughout** — the owner declined the Opus switch at dispatch ("dispatch wave 1" on Fable); the switch points were offered and recorded, not taken | **19 spawns + 5 resumes, all by role name, zero `model` overrides:** 4 `trace` (Sonnet), 2 `test-qa` (Sonnet; one resumed three times), 1 `backend-infrastructure` (Opus; resumed twice), 1 `sweep` (Haiku), 2 `constraint-validator` (Sonnet), 9 `reviewer` (Fable). **Guard blocks: 0. Reviewer refusals: 0.** One Sonnet resume lost to a rate limit — tree verified clean, restarted | 5a: **0 B / 2 W** (the soft-delete read the clock twice; the probe was blind to the repository guards) + cycle 2 **0 B / 2 W** (RED comments overclaiming; a conditional pin) · 7a: **0 B / 2 W** (doc-vs-code cite drift after concurrent edits) → cycle 2 verified. Codex: 5a CLEAN ×2; 7a 1 W (the projection read twice — an Orchestrator one-liner) → CLEAN at cycle 3. The register (Part A): Codex 2 B / 4 W → 0; Reviewer 0 B / 5 W → 0 | **1** — green on the first run (`34148997832`, all 7 jobs; S138 needed 3) | none yet | **Every defect this sprint was caught by review, none by CI so far; every code defect sat on the cheaper tier or in the Orchestrator's own one-liners** — consistent with the S138 control row: capability did not prevent them, review caught them. The Fable-seat cost was the one number the routing could not lower (owner's choice). Signals 1–3 (a post-close defect from a cheaper implementer; CI > 2 runs; a reviewer refusal or a guard block on a reviewer): none tripped at close |
