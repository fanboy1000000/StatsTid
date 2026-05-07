---
name: refine-requirements
description: Refine and clarify requirements before planning or coding on this project. Trigger whenever the user asks to build, create, implement, fix, add, update, change, or develop anything (per CLAUDE.md Pre-Implementation Gate). Interprets intent rather than transcribing requests, proposes the best answer within the project's architecture, and surfaces Open Questions / Assumptions / Acceptance Criteria / Risks / Readiness before work begins. Skip only when the task is mechanical with an obvious fix.
---

# Requirements Refinement Skill

## Description
Refine and clarify requirements before planning or coding. The goal is not to ask the user what they want — it's to figure out what they actually need, propose the best answer, and guide them to think deeper before work begins.

## Trigger
Trigger whenever the user asks to build, create, implement, fix, add, update, change, or develop anything. Do not skip this step; always refine before planning or writing code.

## Core Principle
**Interpret intent, don't just transcribe requests.** Before responding, think: what does the user actually need? What problem are they solving? What would a domain expert recommend here? Then propose the best answer — don't ask the user to design the solution.

## Instructions

### Step 1: Understand the Real Need

Read the user's request and think beyond the literal words:
- What problem are they trying to solve? (not just what feature they asked for)
- Why do they need this now? What's the context?
- What would they wish they'd asked for in 3 months?
- What do they not know they need? (gaps between their request and the actual domain)

Cross-reference against the existing system:
- Read SYSTEM_TARGET.md — is this already specified? Does it conflict?
- Read docs/ARCHITECTURE.md — does this fit the current architecture?
- Read docs/knowledge-base/INDEX.md — are there ADRs or patterns that constrain this?
- Check docs/QUALITY.md — is this going into a weak area that needs extra care?

### Step 2: Separate What You Know from What You Don't

Sort every ambiguity into one of two buckets:

**Propose** — when there's a clear best answer within our architecture, domain patterns, or technical constraints. Don't ask the user to make technical decisions you can make.
- Example: "Validation will be server-side in the Rule Engine (per ADR-002). The frontend shows errors from the API."
- The user says "yes" or corrects — they don't need to design the solution.

**Ask** — when the answer depends on business intent, domain knowledge you lack, scope preference, or a genuine design fork with real tradeoffs. These are decisions only the user can make.
- Example: "Should child sick days be per-episode (reset each illness) or annual quota? Danish agreements vary — AC allows 1 day per episode, HK allows 2. Which model matches your users' contracts?"
- Present the options with tradeoffs, not just "X or Y?" — help the user choose.

**The test:** Could a domain expert at the user's organization disagree with your proposal? If yes → ask. If no → propose.

### Step 3: Output the Refinement

Present this structured block:

---

**What You Asked For**
One sentence — the literal request.

**What You Actually Need**
One to three sentences — the interpreted need, including things the user didn't explicitly say but that are necessary. This may be broader or narrower than what was asked for.

**Proposed Approach**
The recommended solution in 3-5 bullets. Be specific — name files, patterns, architectural layers. This is a hypothesis, not a question.

**Open Questions** (decisions only you can make)
Numbered list of genuine forks where user intent matters. For each:
- State the question
- Present the options with brief tradeoffs
- Recommend one if you have a lean, but make clear it's the user's call

Only include questions that would meaningfully change the implementation. If there are none: "None — all ambiguities resolved by architecture constraints."

**Assumptions** (correct me if wrong)
Numbered list of things being assumed. Phrased as statements, not questions. The user scans and flags anything wrong.
1. Assumption that seems obvious but might not be
2. Another assumption
3. ...

**Acceptance Criteria**
Verifiable checklist of "done":
- [ ] Criterion 1
- [ ] Criterion 2
- ...

**Risks & Conflicts**
Things that could go wrong, conflict with existing architecture, or have non-obvious consequences:
- Risk 1 (with mitigation if known)
- Risk 2
- ...

If none: "No conflicts with current architecture or KB entries."

**Readiness: READY | NEEDS CLARIFICATION**
Explicit assessment of whether this is clear enough to start planning/coding.
- READY: All ambiguities have proposed resolutions. Proceed unless user objects.
- NEEDS CLARIFICATION: List the 1-2 blocking unknowns that can't be reasonably assumed.

---

### Step 4: Review the Refinement (MANDATORY for substantive tasks)

Refinements that bake in wrong scope or assumptions waste effort downstream. WORKFLOW.md Step 0b plan review catches such flaws on the formal sprint plan, but by then the assumptions have already shaped the plan. Reviewing the refinement here is upstream and cheaper to correct.

**Procedure**

1. Write the Step 3 refinement block to `.claude/refinements/REFINEMENT-<short-topic>.md` (gitignored — already excluded via `.gitignore`). Use a short, descriptive slug for `<short-topic>` (e.g., `s24-task-2206-redo`, `compliance-warning-color`).
2. Run BOTH review lenses (in parallel where possible):
   - **External lens (Codex)**: `codex exec "<review-prompt>"` referencing the refinement file path. Catches scope drift, missing exclusions, weak acceptance criteria, hidden assumptions, regression vectors.
   - **Internal lens (Reviewer Agent)**: spawn the Reviewer Agent (per [docs/AGENTS.md](../../../docs/AGENTS.md)) with the refinement file as REVIEW SCOPE. Catches architectural fit against KB / ADRs / priority order, simplicity, scope-vs-priority alignment.
3. Both lenses return BLOCKER / WARNING / NOTE findings in the standard severity format.
4. Append findings inline in the user-facing Step 3 output under a new section:

   ```
   **Review Findings (Step 4)**

   *External (Codex):*
   - BLOCKER: ...
   - WARNING: ...
   - NOTE: ...

   *Internal (Reviewer Agent):*
   - BLOCKER: ...
   - WARNING: ...
   - NOTE: ...
   ```
5. Address all BLOCKERs before proceeding to Step 5. For each fix, update the refinement file and re-invoke whichever lens flagged it. WARNINGs and NOTEs are surfaced to the user with a recommendation; the user decides.

**Codex review prompt template**

```
Review the requirements refinement at <path> against:
1. Scope tightness — explicit exclusions, anything in scope that should be deferred, anything deferred that's actually load-bearing?
2. Open Questions — genuine forks not surfaced? For each listed, is the recommendation defensible?
3. Assumptions — wrong against current code, ADRs (docs/knowledge-base/), ROADMAP, CLAUDE.md priority order?
4. Acceptance Criteria — checklist actually verifies "done"? Anything verifiable that's missing?
5. Risks — hidden coupling, in-flux interfaces, regression vectors, migration concerns missing?
6. Architectural fit — conflicts with KB / ADRs / ROADMAP / priority order?

Return BLOCKER / WARNING / NOTE findings with file:line citations where applicable. If clean, say "Clean — no findings."
Be terse.
```

**Cycle cap**

2 BLOCKER-fix cycles per lens. After the second cycle on the same lens, halt and prompt the user to choose: (a) continue iterating, (b) accept remaining findings and proceed, (c) defer findings as a follow-up task. Mirrors WORKFLOW.md Step 0b / Step 7a discipline (see `feedback_step7a_cycle_cap_discipline.md`).

**Skip conditions** (Step 4 only — Steps 1–3 still run)

- Calibration "simple bug fix or typo" tier where Step 3 was 2–3 lines.
- Mechanical task with an obvious fix.
- User explicitly says "skip review" or "proceed without review."
- `codex` CLI not on PATH OR Reviewer Agent unavailable → **halt and prompt user**. Do NOT silently skip.

When skipping, record a one-line rationale in the user-facing output (e.g., "Step 4 skipped: trivial mechanical fix").

### Step 5: Suggest the Next Question

Always end with a suggested follow-up question the user should consider. This guides them to think deeper about aspects they may not have considered.

The follow-up should:
- Target the weakest part of the requirement (the area most likely to cause rework)
- Be specific to the domain, not generic
- Open a dimension the user hasn't explored yet

Format:

> **To go deeper, consider:** [specific question about an unexplored dimension — e.g., "How should this behave when an employee switches agreements mid-period? The entitlement quotas would change, but they may have already used days under the old agreement."]

This is NOT "Does this look right?" — that's a yes/no gate. The follow-up question opens a new line of thinking.

### Step 6: Iterate or Proceed

- If the user answers the follow-up or makes corrections: update the refinement, re-run Step 4 review on the changed sections, then suggest the next deeper question. Repeat until READY.
- If the user says to proceed: start planning or coding.
- If the user gives a simple confirmation: proceed — don't over-refine simple tasks.

### Calibration

**Scale the refinement to the task size:**
- Simple bug fix or typo: 2-3 line refinement, skip edge cases, skip Step 4 review, go straight to READY
- Single-feature task: Standard refinement block + Step 4 review
- Multi-sprint feature or new domain: Full refinement with architecture review, Step 4 review, multiple follow-up rounds

**Don't over-engineer refinement for tasks where the right answer is obvious.** If the user says "fix the 500 error on the positions page" and you can see the error, just fix it. The skill is for ambiguous or complex requests, not mechanical tasks.
