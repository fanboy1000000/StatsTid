# Systematic Debugging Skill

## Description
Structured diagnostic process for hard, stuck, or recurring bugs — stops thrashing and replaces random fix attempts with disciplined, evidence-driven investigation.

## Trigger
Activate when ANY of these signals appear:
1. The user expresses confusion or frustration ("I can't figure out why", "this makes no sense", "I've tried everything", "I'm going in circles")
2. Claude is about to make a second or third fix attempt on the same bug
3. A fix was applied but the bug returned or moved
4. The bug has no obvious root cause from the description alone
5. The user pastes an error with no clear stack trace
6. "It worked yesterday and now it doesn't"

Do NOT trigger for simple, obvious bugs with a clear cause.

## Instructions

**Rule #1: No code changes until the root cause is confirmed.**

### Step 0: Recognise You're Stuck

Check for thrashing signals:
- You're about to try a fix you're not confident will work
- You've already made one or more attempts and the bug persists
- The user has expressed frustration or confusion
- The same bug has come back after a previous "fix"
- There's no clear causal chain from code to symptom

If any are true, stop and announce:

> Switching to systematic debugging mode — diagnosing before fixing.

### Step 1: Bug Capture

Nail down the precise shape of the bug before doing anything else.

Ask the user (or extract from context):

1. **Observed behaviour** — What is actually happening? Exact error message, wrong output, crash, hang, silent failure. Re-read the literal error output without interpretation — describe what it *says*, not what you think it means.
2. **Expected behaviour** — What should happen instead?
3. **Reproduction steps** — Exact steps to trigger. Note if intermittent.
4. **What's already been tried** — Every fix or investigation attempted so far. Prevents re-treading.
5. **Recent changes** — What changed just before this appeared? Deploy, dependency update, config change, refactor?
6. **Environment** — Does it reproduce everywhere? Or only in specific environments/machines/configs?

Present a concise summary back to the user and confirm it's accurate before proceeding.

### Step 1b: Minimal Reproduction

Before generating hypotheses, try to reproduce in the smallest possible context.

- Can you strip away 90% of the system and still trigger the bug?
- Can you write a minimal test case that exhibits the failure?
- If yes, you've already localized significantly — use this to constrain hypotheses.
- If "it worked yesterday": consider git bisect through recent commits before theorizing. The offending commit often reveals the cause faster than any hypothesis.

### Step 2: Scope Narrowing

Binary-search the blast radius before generating hypotheses. Ask:

- Frontend or backend?
- This function or its caller?
- This request or all requests?
- This data or all data?
- This environment or all environments?

Each answer cuts the hypothesis space in half. Keep narrowing until you've localized to a specific layer or component.

### Step 3: Hypothesis Generation

Before touching any code, generate all plausible root causes.

- List every hypothesis, even unlikely ones
- Group by layer if helpful (data, logic, network, environment, config, race condition, etc.)
- Rank by likelihood based on available evidence
- For each: note what evidence would confirm it AND what would rule it out
- **Confirmation bias guard**: For your top hypothesis, explicitly ask "what evidence would *disprove* this?" If you can't answer, the hypothesis is too vague.

Format:

| # | Hypothesis | Likelihood | Confirms if... | Rules out if... |
|---|-----------|-----------|----------------|-----------------|
| 1 | ... | High | ... | ... |
| 2 | ... | Medium | ... | ... |
| 3 | ... | Low | ... | ... |

Do not skip low-likelihood hypotheses — surprising bugs often live there.

### Step 4: Systematic Elimination

Work through hypotheses one at a time, starting with most likely.

For each hypothesis:
- Design the smallest possible probe — a log, an assertion, an isolated test, a print statement
- **Timebox**: If a probe takes more than a few minutes without yielding clear signal, mark inconclusive and move on to the next hypothesis. Come back later with a better probe.
- Run it and record what it reveals
- Mark the hypothesis as CONFIRMED, RULED OUT, or INCONCLUSIVE
- Move to the next — do not change two things at once

Maintain a running evidence log:

```
[H1] Hypothesis: X was null due to missing initialisation
     Probe: Added console.log before line 42
     Result: Value was present — RULED OUT

[H2] Hypothesis: Race condition in async handler
     Probe: Added mutex / serialised calls
     Result: Bug disappeared — CANDIDATE
```

Never discard the log. If a fix later stops working, the log tells you where to resume.

### Step 5: Root Cause Confirmation

Before writing the actual fix, confirm the root cause by answering ALL three:

1. Can you explain the full causal chain? (X happened -> Y was affected -> Z broke)
2. Can you reproduce the bug on demand, and make it disappear by reversing the cause?
3. Does the fix address the cause, not just mask the symptom?

If you can't answer yes to all three, keep investigating. A passing test is not enough — you need to understand *why* it passes.

State the confirmed root cause explicitly:

> Root cause confirmed: [clear one or two sentence explanation of the full causal chain]

### Step 6: Fix and Verify

Now write the fix, with the root cause in mind.

- Make the minimal change needed to address the root cause
- Re-run the reproduction steps to confirm the bug is gone
- Check that no adjacent behaviour broke (run existing tests, spot-check related paths)

### Step 7: Regression Note

Before closing, document:

- What broke and why (one sentence)
- Whether a test should be added to catch this in future
- Any related areas that could have the same issue

Format:

> Regression note: [What broke] because [root cause]. Consider adding [test/guard] to prevent recurrence.

### Tone and Style

- Be clinical and methodical — this is investigation mode, not exploration mode
- State each step clearly so the user can follow along
- Never skip straight to a fix, even if one seems obvious — confirm the hypothesis first
- If the user pushes to "just try this fix", acknowledge it but note the risk of masking the symptom
- Keep the evidence log visible — it builds trust and prevents loops
