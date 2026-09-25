# Model-routing governance interlude, 2026-09-24/25 — internal lens (Fable reviewer) verdicts

Tracked archive of the three Reviewer Agent reports on the governance chain `fe0bdb1..bf98e30` (CLAUDE.md: `docs/reviews/` is the ad-hoc external-review archive; the internal lens is archived here for the same reason — the verdict is the evidence that the lens ran). Transcripts: `<session 7b2d3d74>/subagents/agent-<id>.jsonl`.

---

## Cycle 1 — on e8de47b (BLOCKED) — agent-a39ad77eeb29c1084

> [Editorial: this final report opens by superseding a draft the agent had produced minutes earlier; the draft is in the transcript, not archived.]

Nothing further is needed; the background output settles the last open question and changes one finding. The documentation the Orchestrator consulted on 09-24 (subagent `agent-a2b67c4b78ce45aa9`, fetching `code.claude.com/docs/en/sub-agents.md`) says the Agent tool's per-spawn `model` parameter accepts "`sonnet`, `opus`, `haiku`, or full ID", with resolution order per-invocation → definition frontmatter → `CLAUDE_CODE_SUBAGENT_MODEL` → **main conversation's model**. That contradicts the headline of commit `8c45514` and documents the exact fallback observed in BLOCKER-1. Final report follows, superseding the draft above.

reviewed-by-model: claude-fable-5-1

**Scope note.** Reviewed `fe0bdb1`, `8c45514`, `e8de47b` as instructed. Two absorption commits landed during this review (`8d35419` 10:04, `37c2764` 10:06, "absorb Codex"). I read both diffs; each finding is marked **OPEN at HEAD** or **FIXED at 8d35419** so nothing already absorbed is re-raised. Beyond the repo I read the session and subagent transcripts under `C:\Users\b200895\.claude\projects\C--Users-b200895-source-repos-StatsTid\`, because the commits make claims about what ran on which model and the transcripts are the only place those can be checked.

---

### BLOCKER-1 — The recorded cause of the 09-24 "alias lag" is wrong, and the prescribed remedy was tried that day and failed twice, once silently. OPEN at HEAD.

**What it means for the owner.** The new "Version check" teaches the wrong lesson and the wrong fix. It says aliases can lag a release and the fix is to pin the full id in the role's definition file. The transcripts show the real cause was that the **Claude Code client was too old** (2.1.263; Opus 5.5 needs 2.1.280+), and that on that client a definition-file pin did not help: one pin **silently ran on the Orchestrator's session model**, the other returned a hard error. The silent case is the dangerous one, and it is the *documented* behaviour: the resolution chain the Orchestrator's own doc lookup returned ends in "4. Main conversation's model". Under the new "honour the switch points" rule the session model will often be Fable during planning; a pinned implementer on a stale client would then run on Fable with no error, invisible to the guard (`C:\Users\b200895\source\repos\StatsTid\.claude\hooks\model-routing-guard.ps1:50` reads only the per-spawn `model`). That is the routing the guard exists to prevent.

**Evidence** (UTC; 09-24 session `9c422ee4…` carries `"version":"2.1.263"` on every line, 09-25 session `7b2d3d74…` carries `2.1.281`):
- 13:38:25 `sed -i 's/^model: opus$/model: claude-opus-5-5/' .claude/agents/backend-infrastructure.md`
- 13:38:38 `backend-infrastructure` spawn (`agent-a6bbf8668e1a413ce`): API `"model":"claude-opus-5"`, self-report `model: claude-opus-5[1m]` — pin ignored, session model used.
- 13:40:09 `sed … 's/^model: claude-opus-5-5$/model: claude-sonnet-5/'`
- 13:40:18 spawn (`agent-a197e1eb59ab235d4`): `API Error: 400 Claude Code 2.1.263 does not support this model; version 2.1.280 or newer is required. Run 'claude update'`. Its brief: "A previous probe with a different pinned id came back reporting the *main conversation's* model instead of the pinned one, which would mean the pin was silently ignored."
- 13:40:46 restore from `/tmp/bi-backup.md`.
- The "alias may lag behind release" wording originates in the Haiku-run `claude-code-guide` answer that same afternoon, which said in the same breath "I'm uncertain about that notation".

**Where the docs say otherwise** (`C:\Users\b200895\source\repos\StatsTid\docs\WORKFLOW.md`, e8de47b lines): 319-321, 356-358 ("the lag … had cleared, so no pin was needed"), 367-372; `docs/operations/model-routing-register.md:14-15,47-49`; commit message `fe0bdb1`. `8d35419` added "the self-report check … is the only check on a pin" but kept the cause and the remedy.

**Required fix.** (1) Step zero of the Version check = client version (`claude --version`; transcript `"version"` field; the 400 text names the version a model needs). (2) Record both 09-24 pins and outcomes. (3) Re-label the pin remedy: unproven as an override, observed to fall back silently on 2.1.263, documented fallback is the main conversation's model, self-report comparison mandatory after any pin. (4) Replace "the lag had cleared" with "the client was updated 2.1.263 → 2.1.281 between sessions".

### WARNING-1 — A review ran below the floor on 09-24 through a route the four layers do not cover, and it is recorded nowhere. OPEN at HEAD.

Telemetry `2026-09-24T12:16:21Z | spawn | general-purpose | opus | ALLOW`; subagent `agent-aefe86dc4358e4f90` was briefed as the Reviewer Agent with "**The Orchestrator explicitly names the Opus tier as an authorised review floor for this review**, so do not refuse on model grounds", ran on `claude-opus-5`, and returned `reviewed-by-model: claude-opus-5` / `verdict: APPROVED-WITH-WARNINGS` on the post-close fix `5e78941` (`TemporalWriteZeroWidthReopenTests`, 51 mentions). Not in `docs/sprints/SPRINT-143.md`, not in the register. `8d35419` removed the reviewer's "successor named in a prompt" clause (good), but `general-purpose` + `opus` + a reviewer brief still passes `model-routing-guard.ps1:117-126`, and register signal 3 watches `reviewer` spawns only. **Fix:** record the event as a routing deviation (S143 post-close / S144 row), and either block a `general-purpose` spawn whose prompt contains `reviewed-by-model`, or document the hole next to the four layers.

### WARNING-2 — "The Agent tool's `model` override accepts only the aliases" contradicts the documentation the session itself fetched, and was never tested. OPEN at HEAD.

`WORKFLOW.md:368-370` (and the title of `8c45514`) state it as fact and derive the remedy from it. The `claude-code-guide` answer on 09-24 quotes `sub-agents.md`: the Agent tool "accepts the same values as the agent frontmatter field: `sonnet`, `opus`, `haiku`, or full ID". The only counter-evidence is the tool schema enum the Orchestrator saw ("an enum of sonnet | opus | haiku | fable", its own question 4). No per-spawn full-id attempt exists in telemetry or either session's Agent inputs. Either test it and record the result, or state it as "the tool schema exposes an enum; the docs say full ids; untested". It matters for the guard too: its literal alias compares (`model-routing-guard.ps1:94,102,108,113`) would BLOCK `reviewer` on `claude-fable-5-1` as "below the floor" and pass `sweep` on `claude-opus-5-5`, if a full id ever gets through.

### WARNING-3 — The Fable-floor bump checklist omits the guard's own test harness. OPEN at HEAD.

`C:\Users\b200895\source\repos\StatsTid\.claude\hooks\test-sprint-close-guard.ps1:107,115,131,132,140,208` hard-code `claude-fable-5-1`. e8de47b says bump "that pin and the reviewer's self-check"; `8d35419` says "three things in ONE commit" — still not the harness, which would go red on a bump. This exact omission cost five harness tests once (`docs/sprints/SPRINT-141.md:1582-1583`). Make it four.

### WARNING-4 — The transcript instructions name a folder that does not exist, and the register gives two sources for one column. OPEN at HEAD.

`WORKFLOW.md:364` says "the session's `tasks\` folder"; on disk it is `<session-id>\subagents\agent-*.jsonl` (verified; no `tasks` folder). `model-routing-register.md:21-23` still sources *Spawns by model* from the telemetry log, which writes only the alias or `inherit` (`model-routing-guard.ps1:71`) and cannot yield the resolved ids lines 12-18 require. Fix both; add `grep -o '"version":"2\.[0-9.]*"'` to the register's commands so the client version is one grep away.

---

### NOTE-1 — test-qa's brief-challenge text contradicted its core constraint. FIXED at 8d35419.
At e8de47b `.claude/agents/test-qa.md:7` ("derive expected values from the SPEC") vs `:9` ("expected values … verify against the code … follow the code") was a direct contradiction that, read literally, forbids any RED test. I would have raised it as a BLOCKER; Codex did, and the (a)/(b) split plus the role-specific sentence resolves it (also the template at `docs/AGENTS.md:275-279`). Residual: `trace.md` keeps "bend a test, fixture" wording for a read-only role — harmless.

### NOTE-2 — Stale "most capable model" wording. FIXED at 8d35419.
At e8de47b in `WORKFLOW.md:305`, `reviewer.md:3`, `model-routing-guard.ps1:6,95`, `sprint-close-guard.ps1:231`, `.claude/settings.json:12`, `CLAUDE.md:55`, register `:5`. All corrected; remaining mentions are historical citations of the 09-07 ruling.

### NOTE-3 — Internal contradictions in the Version check. FIXED at 8d35419.
"none of them checks the version" (`:349-351`) vs "layer 4 already enforces the newest Fable" (`:373-375`); S138 background spliced into rule 2 (`:321-323`).

### NOTE-4 — Model ids: what I can and cannot confirm.
`claude-fable-5-1`: my own system prompt. `claude-haiku-4-5-20251001`: my knowledge and the API field of the 09-24 guide transcript. `claude-sonnet-5`, `claude-opus-5-5`: API fields on 09-25 (`agent-af8daa3438c92818f`, `agent-aa68fca5279de348f`). Opus 5.5 postdates my knowledge cutoff (June 2026), so I cannot independently confirm it is the newest Opus, nor whether newer Sonnet/Haiku exist today. The self-report is `claude-opus-5-5[1m]` while the API field and reference list say `claude-opus-5-5`: state that the `[1m]` context-window suffix is ignored in the first-spawn check, and note the close guard's literal compare (`sprint-close-guard.ps1:225`) would block a close if Fable ever self-reports with a suffix.

### NOTE-5 — The trace.md pin was a working-tree experiment, not a commit.
`git log -- .claude/agents/trace.md` shows only `1f536ee` and `e8de47b`; the pin was applied and reverted by `sed` around the 07:45Z spawn. `WORKFLOW.md:358-359` reads as if it exists; say "temporarily pinned, then reverted".

### NOTE-6 — The 09-25 ruling is paraphrased; the 09-24 one is quoted.
`WORKFLOW.md:388-395`: for auditability of owner rulings, quote the owner. Also `:316` "twelve agent definitions, two hooks and the close gate" — twelve is right; the close gate *is* one of the two hooks, and the third hook file is the harness.

### NOTE-7 — BRIEF CHALLENGE vs role constraints: no conflicts at HEAD.
`sweep.md` and `constraint-validator.md` were tailored (no-improvise and checklist-only preserved). The "Declared deviations" vs "Brief contradictions" boundary for `backend-infrastructure.md` was unstated at e8de47b and is stated at `8d35419`.

### NOTE-8 — check_docs / anchor hazards: none from these commits.
Register anchored `S141`, latest sprint log 143, `ANCHOR_SLACK = 3` (`tools/check_docs.py:41,161`) → no warning until S145, soft-only. No KB, schema or sprint-inventory changes; `WORKFLOW.md`/`AGENTS.md` carry no anchor; the `#model-routing-owner-ruling-2026-09-07` link still resolves.

### NOTE-9 — CONVENTIONS arrived late.
The brief's GOVERNING MODEL block was the unfilled placeholder `{{CONVENTIONS}}`; the verbatim text arrived mid-review and was applied. No finding changed as a result.

### NOTE-10 — Absorbing this review is a switch point.
The governance commits were drafted on Opus (co-author lines; session API fields). The rule is forward-looking, so not a finding — but under `WORKFLOW.md:388-395` review absorption is a Fable phase, so the Orchestrator should ask the owner for `/model` before absorbing.

---

verdict: BLOCKED
reviewed-against-commit: e8de47b

---

## Cycle 2 — on bbf1e3a (BLOCKED) — agent-a93ecc941892eed6a

reviewed-by-model: claude-fable-5-1

**Scope and a caveat on what I read.** Reviewed the committed chain `b49f781..bbf1e3a`. The working tree has since drifted (six files modified, uncommitted: `reviewer.md`, both hooks, the harness, `WORKFLOW.md`, the register, plus `SPRINT-143.md`), so every citation below is to `git show bbf1e3a:<path>`; where the uncommitted tree already moves on a point I say so, so nothing gets fixed twice. Beyond the repo I read the 09-24 and 09-25 session and subagent transcripts and the local telemetry log, because the commits make claims about what ran on which model and those are the only places to check them. I exercised the routing guard's seam from the scratchpad (its telemetry writes to `<cwd>/.claude/telemetry`), and tested the close guard's strip regex in isolation; I did not execute the harness, which creates and deletes files.

## Part 1 — cycle-1 findings at bbf1e3a

| Cycle-1 finding | Status | Where |
|---|---|---|
| BLOCKER-1 (stale client, not alias lag; two pins) | **RESOLVED in structure, with one evidence error** (→ WARNING-1) | `C:\Users\b200895\source\repos\StatsTid\docs\WORKFLOW.md:372-386`. Verified: session `9c422ee4` carries `"version":"2.1.263"` on all 7,700 lines; the 400 text is verbatim (`API Error: 400 Claude Code 2.1.263 does not support this model; version 2.1.280 or newer is required`); two pins were made at 13:38:25Z and 13:40:09Z; "the lag had cleared" is gone; remedy labelled unproven (:403-408). |
| WARNING-1 (below-floor review of 5e78941 unrecorded) | **NOT RESOLVED — the record made is wrong** (→ BLOCKER-1) | `docs/sprints/SPRINT-143.md:655-674`, `WORKFLOW.md:356-364`, register S143 row |
| WARNING-2 ("aliases only" untested) | **RESOLVED, one detail wrong** (→ WARNING-2) | `WORKFLOW.md:409-414`. Verified as a per-spawn test: 08:17:32Z, `"subagent_type":"trace","model":"claude-sonnet-5"` → `InputValidationError … "code":"invalid_value","values":["sonnet","opus","haiku","fable"],"path":["model"]`. Correctly distinguished from the frontmatter route. Client version misreported. |
| WARNING-3 (floor bump omits harness) | **RESOLVED in WORKFLOW, residual in reviewer.md** (→ NOTE-2) | `WORKFLOW.md:418-421` says four places; `.claude/agents/reviewer.md:9` still enumerates three |
| WARNING-4 (`tasks\`; register sources) | **PARTIALLY RESOLVED** (→ NOTE-3) | Register `:22-31` now sources resolved ids from `subagents\` transcripts and adds the client grep; `:39` still says `tasks\` |
| NOTE-4 (`[1m]` vs literal compare) | RESOLVED | `sprint-close-guard.ps1:224-226`; harness T20/T21 (see Part 2 for what they prove) |
| NOTE-5 (trace.md pin temporary) | RESOLVED | `WORKFLOW.md:391-396` — verified: `trace.md:5` read `model: claude-sonnet-5` at 07:45:05Z, spawn `agent-af8daa…` self-reported `resolved-model: claude-sonnet-5` |
| NOTE-6 (quote the owner; hook count) | RESOLVED | `WORKFLOW.md:437-443` matches the AskUserQuestion card and the recorded answer verbatim; `:317-319` "two hooks (one of them the close gate) and the guard's test harness" |

Also verified as stated: the "Last verified" bullet (`agent-aa68fca…`, 07:44Z, transcript `"model":"claude-opus-5-5"`, self-report `resolved-model: claude-opus-5-5[1m]`), the guard rule placement (Part 1c below), the strip (1d), all eleven BRIEF CHALLENGE blocks, `settings.json`, `CLAUDE.md`, and the AGENTS.md inlined-source note.

**(c) Guard rule.** Correctly placed inside the `$GenericRoles` block (`.claude/hooks/model-routing-guard.ps1:128-135`); a `reviewer` spawn exits at `:96-102` before it. Seam runs: generic+opus+brief → exit 2; unnamed+opus+brief → exit 2; `reviewer`+brief, no model → exit 0; generic+sonnet plain brief → exit 0. Header `:19-21` matches. What it does not cover is WARNING-3.

**(d) Strip and harness.** `'claude-fable-5-1[1m]' → 'claude-fable-5-1'`, `'claude-opus-5-5[1m]' → 'claude-opus-5-5'`, unsuffixed unchanged, trailing junk after the bracket not stripped. T20 is the discriminating test (fails without the strip). T20/T21 ledger isolation is sound: the mock is honoured only for S99 (`sprint-close-guard.ps1:574`), the mock log accounts for its one task, so the real `docs/sprints/SPRINT-99.md` is bypassed; the CI-pending seam still points at the temp dir with SPRINT-98 clean.

## Part 2 — findings

### BLOCKER-1 — The sprint log records a governance failure that did not happen, and omits the review that did

**What it means for the owner.** `SPRINT-143.md` now tells you that a post-close fix was reviewed only by an Opus agent that talked its way past the review floor, that "it does not count as a floor review", and that the fact "was invisible". The transcript says the opposite. On 2026-09-24 **you asked** "Could you do it so you spawn one reviewer agent with Fable and one with Opus?" (session `9c422ee4`, ~12:14Z). The Orchestrator then spawned **both**: a `reviewer` on the floor at 12:15:48Z — telemetry `2026-09-24T12:15:50Z | spawn | reviewer | inherit | ALLOW | review role on the floor model`; subagent `agent-a1996bbf33b633dae`, `"model":"claude-fable-5-1"`, ran 12:15:51–12:25:23Z, output `reviewed-by-model: claude-fable-5-1` / `verdict: APPROVED-WITH-WARNINGS` / "The fix in `5e78941` is correct. I traced both facts through the actual repository, router and resolver code" — and the general-purpose Opus arm thirty seconds later. The Orchestrator's own words at 12:25Z: "The Fable arm is in: APPROVED-WITH-WARNINGS, and it corrected me twice on substance" and "Both arms are in". So `5e78941` **has** a floor review; the Opus run was an owner-requested comparison, which `WORKFLOW.md:447` says is "recorded as the owner's call, with their words". The docs instead recorded a self-granted exception, and none of the sprint log, WORKFLOW or register mentions the Fable review or the head-to-head at all (`git grep` at bbf1e3a for `a1996bbf`, `head-to-head`, `Fable arm`: nothing).

**Why it is a blocker.** Auditability applies to the governance record too: it must be reconstructable and true. Three documents now assert a deviation the evidence refutes, and the uncommitted rewrite of the disposition builds a further paragraph on the same premise ("the compliant review was owed only because the 2026-09-24 one ran below the floor"). Cycle 1 was right that the Opus arm was unrecorded and wrong by omission that no floor review existed; the absorption then trusted the reviewer's account over a transcript the Orchestrator itself produced the day before. Secondary inaccuracy in the same section: "A post-close `src/**` change" — `5e78941` touches one file under `tests/**` (working tree already corrects this).

**Citations.** `C:\Users\b200895\source\repos\StatsTid\docs\sprints\SPRINT-143.md:657-669`; `docs\WORKFLOW.md:356-364` ("the Orchestrator reviewed the post-close fix `5e78941` by spawning `general-purpose`"); `docs\operations\model-routing-register.md:67` (S143 row, "Post-close routing deviation").

**Required fix.** Rewrite all three to the facts: (1) the owner's request, quoted; (2) the Fable review as the floor review of `5e78941` — agent id, time, verdict — and the two substantive corrections it made, with where they were absorbed; (3) the Opus arm as the owner's call per `:447`, with the genuine residual named: the brief's "authorised review floor" clause was the device that got past the reviewer self-check, and the guard let a generic reviewer-shaped spawn through — which is what the new detector addresses; (4) the 2026-09-25 external-lens review (now in the working tree) with a **named base commit** for the S144 Step 7a so `5e78941` is inside its diff (see WARNING-4); (5) `src/**` → `tests/**`.

### WARNING-1 — The second pin was `claude-sonnet-5`, so the 400 is misattributed to Opus 5.5

**What it means.** The step-zero paragraph presents two data points as proof that "Opus 5.5 requires 2.1.280 or newer — the API's own 400 text says so". The transcript: 13:38:25Z `sed -i 's/^model: opus$/model: claude-opus-5-5/'` → spawn `agent-a6bbf8668…` ran on `claude-opus-5` (self-report `model: claude-opus-5[1m]`); 13:40:09Z `sed -i 's/^model: claude-opus-5-5$/model: claude-sonnet-5/'` → spawn `agent-a197e1eb…` got the 400 at 13:40:21Z. The 400 was for a **`claude-sonnet-5`** pin — on a client whose `sonnet` alias had resolved to `claude-sonnet-5` in every Sonnet subagent that day. So it shows "this client cannot use a frontmatter full-id pin of this model", not "Opus 5.5 needs 2.1.280". The 2.1.280 threshold for Opus 5.5 is *consistent with* the alias resolving to 5.5 after the update, not evidenced by the 400. Cycle 1's report showed the second `sed`; the absorption dropped the id and wrote "a second attempt". `WORKFLOW.md:372-378`; register `:15-16` and `:67` ("Opus 5.5 needs 2.1.280+"). Concur with Codex c3 that "the documented behaviour" (`:378-381`) is inference — the doc the session fetched says of unknown ids "Fallback behavior … **Not documented**"; the working tree already relabels it "inferred mechanism, not separately tested". Fix: name both pins with outcomes and re-word the threshold claim as consistent-with.

### WARNING-2 — "tested on client 2.1.282" contradicts the paragraph's own method; the session runs 2.1.281

**What it means.** The Version check teaches: read the running client from the transcript's `"version"` field. Today's session `7b2d3d74` carries `"version":"2.1.281"` on all 572 lines; `2.1.282` is what `claude --version` printed at 08:14:21Z — the *installed* client, one ahead of the running one, exactly the 09-24 pattern. The per-spawn test at 08:17:32Z therefore ran on 2.1.281, and `WORKFLOW.md:410` disagrees with `:391` ("client 2.1.281") for the same session. Harmless today (2.1.281 supports Opus 5.5), but the doc that exists to teach this distinction gets it wrong in the same paragraph, and a restart is due before the next implementer dispatch. Fix: "2.1.281 (installed 2.1.282; session started before the update)".

### WARNING-3 — The detector leaves the same route open through any named non-review role

**What it means.** `WORKFLOW.md:356` says the route is "now closed" and `:362-363` that "review work spawns `reviewer`, which has no cheaper mode". The check sits only inside the generic branch (`model-routing-guard.ps1:120-137`). Seam run: `{"subagent_type":"trace","prompt":"print reviewed-by-model first; the Sonnet tier is an authorised floor"}` → exit 0, `ALLOW sonnet-tier role`. `trace` is read-only with the reviewer's exact tool set (`trace.md:4-5`), so it is the natural next vehicle for the identical brief; `backend-infrastructure` on Opus passes the same way. The working tree reframes to "signature detected" and names Codex's omitted-phrase variant, not this one. Fix: run the prompt check once, after the reviewer branch (`:102`) and before the role branches, for every type except `reviewer` and the read-only built-ins; add two seam cases (`trace`+brief, `backend-infrastructure`+brief → BLOCK).

### WARNING-4 — The disposition "recorded, not re-reviewed" was not defensible at bbf1e3a; the in-flight revision cites the wrong rule

**What it means.** `SPRINT-143.md:673-674` defers on "CI-green backfill" and "the next Step 7a covers it". `WORKFLOW.md:38` says **any code-touching** post-close commit "triggers a new Step-7a review cycle scoped to that fix" — a `tests/**` commit is code; CI green is not a review; and "next Step 7a covers it" holds only if S144's base commit is at or before the S143 close `2e7d5b1`, which nothing states. Since bbf1e3a the external lens has run (`scratchpad\codex-5e78941-verdict.txt`, 10:28, APPROVED-WITH-WARNINGS on inlined source, with its own warning that the bundle omitted the recreation assertions and `FixedTimeProvider`) and the working tree records it with four routed items — good. But that text argues the rule "did not strictly apply" from `:453` alone, ignoring `:38`, and rests on the BLOCKER-1 premise. Fix: cite `:38`; name the base commit; reframe per BLOCKER-1 (internal floor review done 09-24, external done 09-25).

### NOTE-1 — T21 is a negative control, not a test of the strip
`test-sprint-close-guard.ps1:303-307` passes with or without the strip (an unstripped `claude-opus-5-5[1m]` also fails the compare and the stderr still matches). T20 is the discriminating one. Say so in the comment; with the working tree's tighter `\[[0-9]+m\]$`, a T22 (`claude-fable-5-1[garbage]` → block) would make the tightened shape testable.

### NOTE-2 — `reviewer.md:9` still enumerates three bump sites; `WORKFLOW.md:418-421` says four (cycle-1 W-3 residual; Codex c3). Confirm the in-flight `reviewer.md` edit fixes the enumeration, not only the `[1m]` wording.

### NOTE-3 — register `:39` still says `tasks\` (cycle-1 W-4 residual; working tree fixes).

### NOTE-4 — register `:54` signal 3 vs `WORKFLOW.md:364` (working tree fixes).

### NOTE-5 — Register S143 row (`:67`): "zero `model` overrides" and "8 `reviewer` (never overridden)" now share a cell with a post-close `model: opus` generic spawn and a ninth reviewer spawn (12:15:48Z). State that the counts were taken at close and list post-close spawns separately.

### NOTE-6 — The detector now blocks the exact shape of the experiment the owner asked for. Document the sanctioned way to run a Fable-vs-Opus comparison (owner-requested, recorded as their call, brief without the self-granted "authorised floor" clause) so the next request does not produce another improvised bypass.

### NOTE-7 — PM legibility of step zero
At bbf1e3a `:372-386` is one fifteen-line bullet mixing what happened, the inferred mechanism and the instruction; "alias" and "pin" are used from `:310` before either is glossed. The working tree's "update the client first" lead and the observed/inferred split are the right shape; add the two glosses.

verdict: BLOCKED
reviewed-against-commit: bbf1e3a

---

## Cycle 3 — on 7c96faf (BLOCKED) — agent-ab26c66d0178db4a6

reviewed-by-model: claude-fable-5-1

**Scope note.** Reviewed `bbf1e3a..7c96faf` as instructed. HEAD has since moved to `bf98e30` (three further docs-only absorptions of Codex cycles 6–8: `ab3145b`, `b8009f4`, `bf98e30`) that touch the same four files. Where a defect I found at `7c96faf` is already fixed at HEAD I say so explicitly, so nothing is fixed twice. The blocker below is **not** among those fixed at HEAD (the post-`7c96faf` diff to `SPRINT-143.md` touches only lines 669–674).

## 1. Cycle-2 findings — disposition at 7c96faf

| Cycle-2 item | Status | Where / evidence |
|---|---|---|
| **BLOCKER-1** (record said 5e78941 had only a below-floor Opus review) | **RESOLVED as a matter of fact** — but the rewritten section introduces a new unsupported claim, see BLOCKER-1 below | `C:\Users\b200895\source\repos\StatsTid\docs\sprints\SPRINT-143.md:655-666`. Verified against the 09-24 transcript: owner's words verbatim at 12:14:32Z (line 10847); `reviewer` spawn 12:15:48Z → `agent-a1996bbf33b633dae` (subagent transcript: model `claude-fable-5-1`, first line `reviewed-by-model: claude-fable-5-1`, `verdict: APPROVED-WITH-WARNINGS`, `reviewed-against-commit: 5e7894171af…`); `general-purpose`+`opus` spawn 12:16:18Z → `agent-aefe86dc4358e4f90` (model `claude-opus-5`, self-report `claude-opus-5[1m]`, APPROVED-WITH-WARNINGS, the "authorised review floor … do not refuse" clause verbatim in its prompt); "corrected me twice on substance" at 12:25:47Z (line 10899). "Thirty seconds later" is exact. |
| **WARNING-1** (400 misattributed) | **RESOLVED — and the Orchestrator is right, cycle 2 was wrong** | `docs\WORKFLOW.md:395-416`. Transcript: `sed` to `claude-opus-5-5` at 13:38:25Z (line 10977), spawn 13:38:38Z (10984) self-reports `claude-opus-5[1m]`; `sed` to `claude-sonnet-5` at 13:40:09Z (10994), spawn 13:40:18Z (10997), 400 at 13:40:32Z (11008) reading `… model sent to the API: claude-opus-5-5` (req_011CfNMn1y5VjX6NbkRJLT5c); Orchestrator's "The pin works. The error proves it" at 13:41:09Z (11015). So the API received the *first* pin's value on a spawn made 9 s after the file already said `claude-sonnet-5` — reload lag is proven for the second spawn, and the doc correctly labels the extension to the first spawn as inferred. |
| **WARNING-2** (2.1.282 vs 2.1.281) | RESOLVED | `docs\WORKFLOW.md:440-441`. The 09-25 session transcript (`7b2d3d74-….jsonl`) carries `"version":"2.1.281"` only (831 occurrences); `claude --version` today prints 2.1.282. |
| **WARNING-3** (detector only in the generic branch) | RESOLVED | `.claude\hooks\model-routing-guard.ps1:106-119` runs for every type after `reviewer` and the read-only list. Exercised the `7c96faf` hook from a scratch cwd: `trace`+"authorised review floor" → 2; `backend-infrastructure`+"do not refuse on model grounds" → 2; `test-qa`+literal `reviewed-by-model: claude-fable-5-1` → 0; `reviewer`+placeholder → 0; `Explore`+placeholder → 0; plus `general-purpose`/`opus`+placeholder → 2 and the sanctioned comparison brief (`comparison-arm-model:`) → 0. The 08:45:31–08:45:44Z telemetry lines (`.claude\telemetry\model-routing.log:275-282`) corroborate the "exercised by hand" claim at `WORKFLOW.md:376-378`. |
| **WARNING-4** (post-Step-7a coverage; no S144 base) | RESOLVED in form; see BLOCKER-1 and WARNING-A for what is still missing | `SPRINT-143.md:687-698`: both lenses named, base `2e7d5b1` (confirmed: S143 close commit 2026-09-24 09:01 +0200; `5e78941` follows it). The Codex verdict file exists and its four residuals match the four items routed to S144. |
| **NOTE-1** (T21 is a negative control) | RESOLVED, and T22 genuinely discriminates | `.claude\hooks\test-sprint-close-guard.ps1:285-287, 311-317`. I ran both hook versions from a scratch git repo with the S99 seams: `claude-fable-5-1[garbage]` → **bbf1e3a hook exit 0 (over-accepted), 7c96faf hook exit 2**; `[1m]` on the floor → 0 on both; `claude-opus-5-5[1m]` → 2 on both (so T21 is indeed a control, as now labelled). |
| **NOTE-2/3/4** | Cannot verify — their text is not in my brief; nothing in the diff contradicts "since fixed" | — |
| **NOTE-5** (register row mixes close-time and post-close counts) | RESOLVED | `docs\operations\model-routing-register.md:69` ("Counts above were taken at close … Post-close spawns, listed separately"); telemetry `:182-183` corroborates the two spawns and that `opus` was the only override. |
| **NOTE-6** (document the sanctioned comparison) | RESOLVED | `docs\WORKFLOW.md:380-387`; soundness in NOTE-d below. |
| **NOTE-7** (gloss alias/pin) | RESOLVED | `docs\WORKFLOW.md:322` (alias), `:398-399` (pin). |

## 2. Findings in the absorption

**BLOCKER-1 — The corrected record says the floor review's corrections were "absorbed the same day"; none of them landed anywhere, and they are missing from the next sprint's list.**
For a PM: the record now tells you the expert review of the CI fix was taken on board. It was not — the misleading test comment that review flagged is still in the codebase, two rows in this sprint's own log that the review called false are still there, and the S144 to-do list carries only the *external* lens's items. The section that was rewritten to establish "the Fable review exists and counts" has quietly repeated, for the Fable arm, the exact "silence" defect it names as a residual for the Opus arm.
- Claim: `C:\Users\b200895\source\repos\StatsTid\docs\sprints\SPRINT-143.md:664-666` — "The Fable arm's two substantive corrections were absorbed the same day (the Orchestrator at 12:25Z: …)".
- What the transcript says at 12:25:47Z (line 10899): *"I'm deliberately **not** acting on any of this yet."* At 12:32:21Z (line 10923) the Orchestrator lists "four false comments still standing, the backwards interval, the stale log row, and the post-close ledger entry" and asks the owner "Want me to work through those now?"; the owner's next message (13:34Z) is the routing ruling. No code or log fix followed.
- What the code and log say at HEAD: `tests\StatsTid.Tests.Regression\Infrastructure\TemporalWriteZeroWidthReopenTests.cs:144-150` still reads "…rather than through `SoftDeleteAsync` — it was one step away from failing for the same reason" (the Fable arm's WARNING 1: `UserAgreementCodeRepository` has no soft-delete; last commit to this file is `5e78941`). `SPRINT-143.md:596` still classifies `TemporalWriteZeroWidthReopenTests.cs:69,126` as "self-threaded — no independent recompute, so no oracle" (its WARNING 2: the exact claim CI disproved). `SPRINT-143.md:651` still says "one Copenhagen day from the routing they drive" (its WARNING 3: misattributes the mechanism).
- The S144 hand-off (`SPRINT-143.md:690-698`) lists only the four Codex residuals. The floor review's three warnings, and the Opus arm's items (a census of the false comment at five sites in two files; a claimed backwards interval at `TxContractTests.cs:766`, where `today = 2025-03-12` is passed as the close date — unverified by a floor review, and now unrecorded for a day), are in no record.
- Fix: replace "absorbed" with "acknowledged in the session; not landed"; add the Fable arm's W1/W2/W3 to the routed list (and the Opus arm's items, marked "comparison arm, not floor-verified"); or land the three small ones now (the `:144-150` comment, the `:596` row, the `:651` wording) and say so with a sha.

**WARNING-A — The evidence for "both lenses have now run" on 5e78941 lives in a temp folder.**
For a PM: the record cites a review verdict by a filename in a directory the tool deletes. `SPRINT-143.md:687-688` cites `codex-5e78941-verdict.txt` "in the session scratchpad" — it is at `C:\Users\b200895\AppData\Local\Temp\claude\C--Users-b200895-source-repos-StatsTid\7b2d3d74-f156-4b04-ad77-18bf5a64c315\scratchpad\`, which is session-scoped temp. The four residuals are transcribed faithfully (checked), so the substance survives; the verdict itself — the proof that a second lens ran — does not. Copy it to `docs\reviews\` (CLAUDE.md names it as the tracked ad-hoc external-review archive) or `.claude\reviews\` and cite that path.

**WARNING-B — (at 7c96faf; fixed at HEAD `ab3145b`) The corrected record blamed the evidence for cycle 1's error, and the evidence did not deserve it.** `SPRINT-143.md:670-671` at `7c96faf` says the telemetry "shows the `general-purpose | opus` spawn but not the owner's request nor the `reviewer` spawn thirty seconds earlier". The log shows both: `.claude\telemetry\model-routing.log:182` (`12:15:50Z | spawn | reviewer | inherit | ALLOW`) and `:183` (`12:16:21Z | … general-purpose | opus | ALLOW`). Do not re-fix; recorded so the review of `7c96faf` is complete.

**WARNING-C — (at 7c96faf; fixed at HEAD `b8009f4`) The register contradicted itself on what the detector was added after.** `docs\operations\model-routing-register.md:53-55` ("the below-floor review of `5e78941`", "a generic spawn") vs the same file's S143 row at `:69` ("first recorded wrongly as a below-floor review") and the guard, which checks every non-reviewer type. Do not re-fix.

**WARNING-D — (at 7c96faf; fixed at HEAD `b8009f4`) One sentence still carried the explanation step zero retired.** `docs\WORKFLOW.md:427` "the same kind of pin fell back silently (see step zero)" — while step zero (`:413-416`) had just replaced "fell back" with the reload-lag inference. Do not re-fix.

**NOTE-a — Dead code and a stale comment remain in the guard (still at HEAD).** `.claude\hooks\model-routing-guard.ps1:145-150`: the generic branch re-reads `$prompt` with no consumer, under a comment that still frames the event as "reviewed a post-close fix below the floor, unrecorded" while the header (`:19-23`) frames it as an owner-requested comparison arm. Delete the block or point the comment at the shared detector at `:106-119`.

**NOTE-b — (at 7c96faf; fixed at HEAD `ab3145b`) Header line no longer true.** `.claude\hooks\model-routing-guard.ps1:25-27` said a spawn with no override for a role whose definition fixes the model "passes untouched"; the detector blocks such a `trace` spawn (my run: exit 2).

**NOTE-c — Step zero could close the one argument a transcript reader will meet.** The 09-24 session's original reason to read the first probe as a session-model fallthrough was the `[1m]` suffix ("the session model, suffix and all", line 10993). The 09-25 alias-`opus` spawn on a current client (`agent-aa68fca5279de348f`, model `claude-opus-5-5`) *also* self-reports `claude-opus-5-5[1m]`, so the suffix is a client artefact, not a fallthrough signature. One sentence at `docs\WORKFLOW.md:413-416` would remove the strongest surviving counter-argument to the lag inference.

**NOTE-d — The sanctioned comparison procedure is sound for the two gates it names; two gaps.** Verified: the sanctioned brief (no self-check, `comparison-arm-model:`) passes the detector (exit 0), and a comparison-arm output pasted into the Step-7a reviewer artifact would be *blocked* by the close gate for lacking `reviewed-by-model:` (`sprint-close-guard.ps1:213-222`) rather than counted as a floor review (HEAD adds this sentence explicitly). Gaps at `docs\WORKFLOW.md:380-387`: (i) `general-purpose` has write tools — the `reviewer` role's `tools: Read, Grep, Glob, Bash` line hard-limits it, the comparison arm gets only an instruction; say so, or require a clean `git status` check after the arm returns. (ii) A brief that quotes this very paragraph is blocked by the third phrase (reproduced: exit 2) — HEAD's wording acknowledges this.

**NOTE-e — The S144 base commit is pinned only in the previous sprint's tail.** `SPRINT-143.md:691` names `2e7d5b1`; `docs\WORKFLOW.md:38` defines the base generically as "the HEAD commit of the previous sprint on master", which reads naturally as "the last commit before S144 opens" and would put `5e78941` outside the S144 diff. The S143 choice is right; carry it into the S144 log header at open so the close does not fall back to the generic rule.

**NOTE-f — PM legibility of step zero at 7c96faf.** The observed/inferred split (`docs\WORKFLOW.md:396-416`) and the up-front bottom line are good, and "pin"/"alias" are glossed. Two weak spots: the bullet has no list of the actions to take (HEAD's "three actions" lead fixes this), and the parenthetical at `:440-441` is a ~90-word sentence with three nested dashes — split it.

**NOTE-g — "Lost" overstates it.** `SPRINT-143.md:677-679` says the comparison's outcome "was never written down … lost unless someone re-reads the two transcripts". The Orchestrator wrote it down at 12:32:21Z (line 10923): a head-to-head table (both APPROVED-WITH-WARNINGS; 49 vs 73 tool calls; 9.5 vs 14.8 min; 261k vs 218k tokens; what each arm found that the other did not). "Never recorded in a governance doc" is fair; it is one transcript line away and the new procedure (`WORKFLOW.md:385-386`) asks for exactly that to be in the sprint log.

verdict: BLOCKED
reviewed-against-commit: 7c96faf


---

## Cycle 4 — on 9c951d0 (APPROVED-WITH-WARNINGS) — agent-af771ce41f7324938

reviewed-by-model: claude-fable-5-1

**Scope note.** Reviewed `7c96faf..9c951d0` as instructed, read against `b49f781..9c951d0`. HEAD has since moved four docs-only commits past the scope (`e0eb34e`, `9936e99`, `4caea8c`, `8e0cfab`, absorbing Codex cycles 10–13). Every citation below is to `git show 9c951d0:<path>`; where a defect at `9c951d0` is already fixed at HEAD I say so, so nothing is fixed twice. Evidence beyond the repo: the 09-24 session transcript (`9c422ee4…jsonl`), its subagent transcripts `agent-a1996bbf33b633dae` / `agent-aefe86dc4358e4f90`, the 09-25 session and its three reviewer subagents, the scratchpad verdict files, and the local telemetry log.

## 1. Cycle-3 items at 9c951d0

| Item | Status | Evidence |
|---|---|---|
| **BLOCKER-1** ("absorbed the same day") | **RESOLVED** | `C:\Users\b200895\source\repos\StatsTid\docs\sprints\SPRINT-143.md:665-671`. Transcript line 10899 (12:25:47Z) reads verbatim "I'm deliberately **not** acting on any of this yet"; line 10923 (12:32:21Z) lists the open items and asks "Want me to work through those now?"; the owner's next message is line 10926 (13:34:44Z), the routing ruling. Nothing landed: `TemporalWriteZeroWidthReopenTests.cs:144-150` still carries the `SoftDeleteAsync` counterfactual (last commit to the file `5e78941`). Routed items 5–7 match the Fable arm exactly: W1 = `:144-150` counterfactual, W2 = the `:596` row, W3 = `ProfileCategoryDatingTests.cs:91-93, 185-186`. Items 8–9 correspond to the Opus arm's census and its `TxContractTests.cs:766` finding, correctly marked "not floor-verified" — but see WARNING-2/3 for what they get wrong. Appendix: `:596` `TxContractTests.cs:762` stays "no oracle", consistent with its own comment (`TxContractTests.cs:762-765`: only a closeDate to store, assertions count rows across a rollback) and with the Fable arm's "inert" reading; `:597` now records the CI red, the clock pin and that the row stood unlanded — correct. |
| WARNING-A (verdict in temp folder) | RESOLVED | `SPRINT-143.md:697-700` cites `docs/reviews/…external-lens.md`, tracked; its `5e78941` section is byte-identical to `codex-5e78941-verdict.txt`. |
| WARNING-B/C/D, NOTE-b | Confirmed fixed (ab3145b/b8009f4), still in place | `SPRINT-143.md:675-679` (both telemetry lines); register `:54-56`; `WORKFLOW.md:432-433`; guard header `:25-28`. |
| NOTE-a (dead `$prompt` re-read) | RESOLVED | `C:\Users\b200895\source\repos\StatsTid\.claude\hooks\model-routing-guard.ps1:139-149`: the generic branch no longer re-reads the prompt; `:147` points at the shared detector; `:111-112` names `fork`. Guard identical at HEAD. |
| NOTE-c (`[1m]` is not a fallthrough signature) | RESOLVED at 9c951d0, one overclaim → NOTE-5 | `WORKFLOW.md:418`; verified `agent-aa68fca5279de348f` (alias `opus`, current client) self-reports `claude-opus-5-5[1m]`. |
| NOTE-d (comparison arm has write tools) | RESOLVED in form; the check it prescribes is unfollowable → WARNING-6 (fixed at HEAD) | `WORKFLOW.md:386`. |
| NOTE-e (S144 base only in the S143 tail) | RESOLVED | `WORKFLOW.md:38` now says the next base is the previous **close commit** when post-close commits exist; `SPRINT-143.md:703-705` requires the S144 header to name `2e7d5b1`. Verified `2e7d5b1` = "S143 sprint close"; `5e78941` is its direct successor. |
| NOTE-f (90-word parenthetical) | RESOLVED (split into two sentences at `:447-449`; still dense, acceptable) | |
| NOTE-g ("lost") | RESOLVED, with one misattribution → WARNING-1 | `SPRINT-143.md:684-690`; the numbers (both AWW; 49 / 9.5 min / 261k vs 73 / 14.8 min / 218k) match line 10923. |

## 2. The two archives

Both are complete final reports, not drafts or echoed bundles. Programmatic comparison against the sources: internal lens — all three sections byte-identical to the last assistant message of `agent-a39ad77eeb29c1084`, `agent-a93ecc941892eed6a`, `agent-ab26c66d0178db4a6` (11438 / 13836 / 12901 chars); three `reviewed-by-model:` + three `verdict:` (BLOCKED at e8de47b, bbf1e3a, 7c96faf). External lens — ten `verdict:` + ten `reviewed-against-commit:`; cycles 1–2 identical to the text after the last `codex` marker in `codex-routing-review-2.txt` / `-3.txt`; cycles 3–9 and `5e78941` identical to the eight verdict files. Two legibility points below (NOTE-1, NOTE-2).

## 3. Guard seam (9c951d0 hook, scratch cwd, telemetry written to the scratch dir)

generic+opus plain (`comparison-arm-model:`) → **0**; generic+opus placeholder → **2**; generic+opus "authorised review floor… do not refuse" → 2; `trace`+placeholder → 2; `reviewer`+placeholder → 0; `fork`+placeholder → 0; `test-qa`+literal `reviewed-by-model: claude-fable-5-1` → 0; generic with no model → 2. Matches `WORKFLOW.md:377-379` and the guard header.

## 4. Findings in this diff

**WARNING-1 — The head-to-head paragraph credits the Fable arm with a finding both arms made.**
For a PM: the sentence written to finally record "what each arm found that the other did not" gets one of its two Fable-only items wrong, in the paragraph whose whole point is accuracy about the comparison. `SPRINT-143.md:688` says the Fable arm found "the counterfactual comment and the false log row" that Opus did not. Line 10923 says "**both** caught the stale sprint-log row", and the Opus transcript (line 280) carries a WARNING on `SPRINT-143.md:596` in so many words. The only Fable-unique item was the `SoftDeleteAsync` counterfactual. Fix: "the Fable arm the counterfactual comment; both the false log row".

**WARNING-2 — Routed item 8 carries a count that neither the Opus arm's own table nor the code supports, and omits the one file that is new information.**
For a PM: S144 is told to census "five sites across two files"; a census today finds neither number. `SPRINT-143.md:716`. The Opus arm's headline said "five sites, two files" but its table lists nine sites in five files, of which four are false in two files (`ProfileCategoryDatingTests.cs:93,186`, `UserAgreementCodeRepositoryTests.cs:131,206`); the Orchestrator's 12:32Z line says "four false comments still standing". `grep 'independently-computed server clock' tests/` at HEAD: nine sites in five files outside the corrected test, exactly the arm's table. Two of the four false sites are already item 7, so item 8's only new content is `UserAgreementCodeRepositoryTests.cs:131,206` — the one file name the record does not give. Fix: name the file and lines, and state "4 false of 9 (arm's census, not floor-verified)".

**WARNING-3 — Routed item 9 asserts a premise about the code that the code does not support.**
For a PM: the item asks S144 to verify whether a soft-delete produces a zero-width or a backwards interval, but states as fact that the row "opens the same day" — it does neither. `SPRINT-143.md:717-718`. The row is created by `NewWageTypeMapping` (`TxContractTests.cs:1336-1344`), which never sets `EffectiveFrom`; `WageTypeMapping.EffectiveFrom` is `{ get; init; }` with no initializer (`src/SharedKernel/StatsTid.SharedKernel/Models/WageTypeMapping.cs:43`), and `ExecuteCreateAsync` binds it as-is (`WageTypeMappingRepository.cs:155`). So the close at `:766` yields `[0001-01-01, 2025-03-12)` — forward, not same-day (the record's premise) and not "effective_from is the real today" (the Opus arm's premise). The item is hedged and routed, so not a blocker; but the record should say "verify the interval the soft-delete produces" rather than assert one. This is also the answer to the item: it can be closed as "no defect" by whoever lands it.

**WARNING-4 — The seam tests have written BLOCK lines into the live telemetry log the register tells readers to count.**
For a PM: the monitor that is supposed to tell the owner "the routing rule tripped" now contains ~20 fake trips from hand-testing the guard. The guard writes to `<cwd>/.claude/telemetry/model-routing.log` (`model-routing-guard.ps1:71`); the Orchestrator ran the seam from the repo cwd, so `.claude\telemetry\model-routing.log:190-192, 222-225, 272-274, 275-282, 299-301, 302-308` are test payloads, including eight detector BLOCKs. The register says to count guard blocks from that log by date range (`docs/operations/model-routing-register.md:24-27`) and signal 3 fires on "a guard BLOCK by the reviewer-brief detector on **any** role" (`:54-56`) — a literal count for 2026-09-25 trips signal 3 and contradicts the row's "Guard blocks: 0" (`:70`). Pre-existing before this diff for the earlier clusters; `9c951d0` added the 09:08Z cluster ("Guard seam 7/7"). Fix: run seam cases from a scratch cwd (the reviewers did; the telemetry then lands outside the repo) and add one line to the register's method saying seam-test lines are excluded, with the 09-25 clusters named.

**WARNING-5 (at 9c951d0; fixed at HEAD `e0eb34e` — do not re-fix) — The register row said "c3 pending" in the commit that absorbs cycle 3.** `model-routing-register.md:70` at 9c951d0 vs `SPRINT-143.md:670` in the same commit ("the cycle-3 reviewer refuted"). Recorded so the review of `9c951d0` is complete.

**WARNING-6 (at 9c951d0; fixed at HEAD `9936e99`/`4caea8c` — do not re-fix) — "check `git status` is clean when it returns" cannot be followed where it applies.** `WORKFLOW.md:386`. Step 7a runs with all sprint work uncommitted by design (`WORKFLOW.md:38`), so the tree is never clean when a comparison arm returns; HEAD replaced it with a content baseline. Also "`reviewer` has none" overstated — its `Bash` can write; HEAD says so.

**NOTE-1 — The internal archive's cycle-1 section opens mid-thought.** `docs/reviews/2026-09-25-model-routing-governance-internal-lens.md:9` begins "Nothing further is needed… Final report follows, superseding the draft above" — faithful to the transcript, but "the draft above" is not in the archive. One bracketed editorial line ("[the draft it supersedes is in the transcript, not archived]") keeps the file honest and readable.

**NOTE-2 — "Every Codex verdict" omits the aborted first attempt.** `…external-lens.md:3`. `codex-routing-review.txt` (07:56Z) printed `verdict: BLOCKED … contents remain unverified` after the sandbox rejected `Get-Content`; it is not a review, but it is the reason every later cycle ran on inlined source. A sentence in the header would make the claim exact and preserve the provenance.

**NOTE-3 — The appendix heading still says "30 sites across 11 files".** `SPRINT-143.md:581`; the table beneath, as re-split by this commit, enumerates thirteen files. The Opus arm flagged this; the commit edited the adjacent rows and left it.

**NOTE-4 — Item 7 mislabels what is false.** `SPRINT-143.md:714` "a false comment about the date's origin". The false claim (Fable W3) is "never compared against an independently-computed server clock" — about *comparison*, not origin. An implementer told to fix the "origin" could rewrite the wrong clause. HEAD's "where the date comes from" has the same looseness.

**NOTE-5 (fixed at HEAD `e0eb34e`) — "the suffix is a client artefact" is inference stated as fact.** `WORKFLOW.md:418`. The evidence supports "the suffix appears on alias-`opus` spawns on both clients, so it cannot distinguish a fallthrough from normal resolution"; where it comes from is not established. HEAD says exactly that.

**NOTE-6 (fixed at HEAD `e0eb34e`) — A follow-ups row asserts the mechanism and, in the same cell, cites the review that refuted it.** `SPRINT-143.md:652` keeps "one Copenhagen day from the routing they drive" (Fable W3: "misattributes the mechanism") and then appends the correction without retracting the first clause.

**NOTE-7 — Three different seam-run counts.** Register `:70` "8 cases", commit message "7/7", `WORKFLOW.md:377-379` lists six. Each is a different run; say which one each refers to, or cite only the WORKFLOW list.

**Verified as stated, for the record:** Orchestrator model by phase in the register row — 09-25 transcript: last Opus 5.5 assistant message 07:58:26Z, first Fable 07:59:24Z, before the first absorption `8d35419` (08:04Z); 97 Opus / 432 Fable messages; client `2.1.281` on all 962 lines. Spawn counts: telemetry `:187-189, 256, 298` = 1 `backend-infrastructure`, 1 `trace`, 3 `reviewer` real spawns on 09-25 (the fourth reviewer, 09:11:05Z, is this cycle, after the commit). "Thirty seconds later", "12:25Z", "12:32Z", "ran on `claude-opus-5`", and the 12:15:50Z/12:16:21Z telemetry pair all check.

verdict: APPROVED-WITH-WARNINGS
reviewed-against-commit: 9c951d0

