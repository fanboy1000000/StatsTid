# Requirements Refinement Skill

## Description
Refine and clarify loosely described requirements before starting any coding task or feature request. Use this skill at the start of ANY new coding task, feature request, bug fix, or technical implementation — even if the request seems clear.

## Trigger
Trigger whenever the user asks to build, create, implement, fix, add, update, change, or develop anything. Do not skip this step; always refine requirements first before writing any code or making any plan.

## Instructions

Before writing a single line of code or forming a plan, run through this refinement process. The goal is to transform a loosely described requirement into a clear, shared understanding — and get the user's confirmation before proceeding.

### Step 1: Analyse the Request

Read the user's request carefully and identify:
- What is being asked (the core goal)
- What is ambiguous or underspecified
- What assumptions would need to be made to proceed
- What edge cases might arise
- What success looks like

### Step 2: Output the Refined Requirements Block

Present the following structured block to the user. Be concise but thorough. Use plain language — avoid jargon unless the user is clearly technical.

---

**Refined Requirements**

**Goal**
One or two sentences clearly restating what will be built/done.

**Clarifying Questions**
List any questions that must be answered before proceeding. Number them so the user can reply easily (e.g. "1. yes, 2. no").
Only include questions that would meaningfully change the implementation. Skip anything you can reasonably infer. If there are no blockers, say "None — ready to proceed."

**Assumptions**
List what is being assumed in order to move forward. These are things not explicitly stated but that seem likely based on context.
- Assumption 1
- Assumption 2
- ...

If any assumption is wrong, the user should flag it before proceeding.

**Acceptance Criteria**
A clear checklist of what "done" looks like. These are verifiable outcomes.
- [ ] Criteria 1
- [ ] Criteria 2
- [ ] Criteria 3
- ...

**Edge Cases to Consider**
Things that could go wrong or situations that need to be handled:
- Edge case 1
- Edge case 2
- ...

---

### Step 3: Ask for Confirmation

After presenting the block, always end with:

> Does this look right? Any corrections or answers to the questions above before I proceed?

Do not start implementing until the user confirms. If they answer questions or make corrections, update the requirements block and confirm once more before starting.

### Tone and Style

- Be direct and structured — this is a working document, not a conversation
- Don't pad with phrases like "Great question!" or "Sure thing!"
- Keep each section tight — bullet points over paragraphs
- If the request is very simple, keep the block short — don't over-engineer it
- Match the user's technical level in language choices
