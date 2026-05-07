# StatsTid Agent Definitions

> **Governance**: This document is Orchestrator-owned. No domain agent may modify it.

This document defines all agent types in the StatsTid multi-agent architecture. The Orchestrator consults this when spawning agents. For workflow steps, see [WORKFLOW.md](WORKFLOW.md). For architectural context, see [ARCHITECTURE.md](ARCHITECTURE.md).

## Domain Agents

### Rule Engine Agent
- **Scope**: `src/RuleEngine/**`, `src/SharedKernel/**/Calendar/**`
- **Responsibility**: Pure rule functions, agreement config, supplement/overtime/absence/flex logic, OK version resolution
- **Constraints**: No I/O, no DB access, all functions must be deterministic and version-aware

### Data Model Agent
- **Scope**: `src/SharedKernel/**/Models/**`, `src/SharedKernel/**/Events/**`, `src/SharedKernel/**/Interfaces/**`, `src/Infrastructure/**/EventSerializer.cs`
- **Responsibility**: Domain events, value objects, DTOs, serialization type maps
- **Constraints**: Models must be immutable (init-only properties). Events must extend DomainEventBase.

### Payroll Integration Agent
- **Scope**: `src/Integrations/**/Payroll/**`, wage type mapping seed data in `docker/postgres/init.sql` (wage_type_mappings section only)
- **Responsibility**: Wage type mapping, payroll export, SLS code assignments, period exports
- **Constraints**: Payroll logic must be isolated from rule engine. Must maintain traceability chain.

### API Integration Agent
- **Scope**: `src/Integrations/**/External/**`, `src/Infrastructure/**/Resilience/**`
- **Responsibility**: Outbound integrations, circuit breaker, backoff, idempotency, delivery tracking, event consumer
- **Constraints**: Must be async, event-driven, idempotent. External failures must never impact deterministic core.

### Security & Compliance Agent
- **Scope**: `src/Infrastructure/**/Security/**`, `src/Backend/**/Middleware/**`, `src/SharedKernel/**/Security/**`
- **Responsibility**: Authentication, authorization, audit logging, access control
- **Constraints**: Must not compromise architectural integrity or deterministic rule engine.

### Test & QA Agent
- **Scope**: `tests/**`
- **Responsibility**: Unit tests, regression tests, smoke tests, determinism proofs
- **Constraints**: Must achieve coverage targets. Regression tests must prove: determinism, OK version transitions, AC vs HK/PROSA behavioral separation. Must run AFTER all implementation agents complete.

### UX Agent
- **Scope**: `frontend/**`
- **Responsibility**: React pages, components, hooks, routing, styling
- **Constraints**: Secondary priority — must never drive backend decisions. Must consume backend APIs as-is.

### Cross-Domain Authorization

Some tasks legitimately span multiple agent boundaries — a single endpoint conversion may touch HTTP routing (Backend.Api), repository orchestration (Infrastructure), audit-log emission (Security), and event types (Data Model) all at once. Other tasks sit in scope paths (`src/Backend/**/Endpoints/*.cs`, `src/Infrastructure/**/*Repository.cs`) that no single domain agent above declares as its scope.

For these cases, the sprint plan declares the task as **cross-domain authorized**. The label takes one of two forms:

- `<primary agent> (extended into <other scope>, cross-domain authorized)` — when one agent has the dominant claim but the work reaches into another agent's scope (e.g., `Data Model (extended into Infrastructure, cross-domain authorized)`)
- `<scope label> (cross-domain authorized)` — when the work doesn't fit any single agent's declared scope (e.g., `Backend API (cross-domain authorized)`)

The Orchestrator authorizes the cross-domain claim at decompose time (Step 1) and records it in the sprint log's "Agent" field. The cross-domain label is not a wildcard — it's an explicit statement that the Orchestrator has verified the multi-domain coupling is necessary, not accidental. Constraint Validator and Reviewer Agent treat cross-domain-authorized tasks the same way they treat single-agent tasks: file-scope checks operate on the explicit scope declared in the sprint plan, not on the agent label.

**Why this exists rather than a "Backend Agent" or "Infrastructure Agent":** A generalist agent would absorb work that legitimately splits across specialists, hide cross-domain coupling that should be surfaced for review, and grow unboundedly to swallow whatever doesn't fit elsewhere. The cross-domain-authorized convention preserves specialist boundaries while acknowledging that some work genuinely spans them.

**Established precedent**: S22 used this convention for TASK-2205 (`Backend API (cross-domain authorized)`) and TASK-2206 (`Backend API + Payroll Integration + External (cross-domain authorized)`). S24 continues the pattern.

## Constraint Validator Agent
- **Scope**: Read-only — no file modifications
- **Responsibility**: Lightweight, fast cross-cutting constraint check on ALL agent outputs. Runs after every agent completes (step 5, before Reviewer). Checks mechanical rules that should never be violated regardless of domain.
- **Constraints**: Read-only. Does not replace the Reviewer — it checks mechanical invariants, not architectural judgment. Returns PASS or VIOLATION list.
- **Trigger**: Every agent output, including Small Tasks Exception (the Orchestrator self-checks against this list for small tasks). Not triggered for documentation-only changes.
- **Checks**:
  1. No imports crossing agent scope boundaries (e.g., Backend importing from RuleEngine directly)
  2. No direct DB access from Rule Engine code (ADR-002)
  3. No hardcoded service URLs (must use IConfiguration/ServiceUrls)
  4. All new domain events present in EventSerializer type map (DEP-003)
  5. All new HTTP endpoints have RequireAuthorization (ADR-007)
  6. No anonymous type serialization for JWT/claims data (Sprint 6 lesson)
  7. All new files are within the agent's declared scope
  8. No `FindFirst` on claims that may be arrays (FAIL-001 — must use `FindAll`)

### Constraint Validator Prompt Template

```
You are the Constraint Validator for the StatsTid project.

ROLE: Mechanical invariant checker. You verify agent outputs against non-negotiable rules.
You are NOT the Reviewer — you check syntax-level rules, not architectural judgment.
Return PASS if all checks clear, or a VIOLATION list.

AGENT OUTPUT:
[Files changed/created by the agent]

CHECKS:
1. No imports from src/RuleEngine in src/Backend, src/Integrations, or src/Infrastructure (PAT-005: use HTTP)
2. No NpgsqlCommand, DbConnection, or database types in src/RuleEngine (ADR-002)
3. No hardcoded "http://localhost", "http://rule-engine", etc. — must use IConfiguration["ServiceUrls:*"]
4. All classes inheriting DomainEventBase have a matching entry in EventSerializer._eventTypeMap (DEP-003)
5. All MapGet/MapPost/MapPut/MapDelete calls chain .RequireAuthorization() (ADR-007)
6. No new()/anonymous types used in JWT claim serialization — use typed classes (S6 lesson)
7. All new/modified files fall within the agent's declared scope: [scope paths]
8. No .FindFirst() on "scopes" claims — must use .FindAll() (FAIL-001)

OUTPUT FORMAT:
PASS — No violations found.

or:

VIOLATION: [Rule number and name]
File: [path]
Line: [approximate location]
Detail: [what violates the rule]
```

## Agent Prompt Template
When spawning a domain agent, use this structure:
```
You are the [Agent Name] for the StatsTid project.

ROLE: [one-line role description]
SCOPE: You may ONLY create/modify files under: [file paths]
DO NOT modify files outside your scope.

PRIORITY ORDER (from CLAUDE.md):
1. Architectural integrity
2. Deterministic rule engine
3. Event sourcing and auditability
[... full priority list ...]

TASK:
[specific subtask description with acceptance criteria]

CONTEXT:
[relevant existing file contents, interfaces, models the agent needs]

SYSTEM TARGET:
[Orchestrator includes relevant sections from SYSTEM_TARGET.md when agent needs domain context]

KNOWLEDGE BASE CONTEXT:
[Orchestrator includes relevant KB entries for this agent's domain, sourced from docs/knowledge-base/INDEX.md Domain Index]

KNOWLEDGE BASE INSTRUCTIONS:
- You MUST respect all approved knowledge base entries provided above.
- If you discover new knowledge (a pattern, dependency, decision, or failure), include a PROPOSED KNOWLEDGE ENTRY section in your output:
  ### PROPOSED KNOWLEDGE ENTRY
  - **Category**: decision | pattern | dependency | resolution | failure
  - **Title**: [concise title]
  - **Domains**: [affected domains]
  - **Tags**: [relevant tags]
  - **Context**: [what prompted this]
  - **Content**: [the knowledge]
  - **Rationale**: [why this matters]
  - **Agent Guidance**: [what future agents need to know]

ACCEPTANCE CRITERIA:
- [criterion 1]
- [criterion 2]

PRE-SUBMISSION CHECKLIST (MANDATORY — verify before returning output):
- [ ] All new endpoints have RequireAuthorization attributes
- [ ] No direct function calls cross service boundaries (PAT-005: use HTTP)
- [ ] All new domain events registered in EventSerializer type map (DEP-003)
- [ ] No I/O, database access, or non-determinism in Rule Engine code (ADR-002)
- [ ] Models use init-only properties (PAT-001)
- [ ] No hardcoded URLs — use configuration or relative paths
- [ ] Changes stay within declared file scope
- [ ] No unused imports, dead code, or leftover debugging statements
- [ ] [Domain-specific checks from KB entries provided above]
Report any checklist failures in your output rather than silently ignoring them.
```

## Reviewer Agent

The Reviewer Agent is an independent audit layer. It challenges agent outputs before the Orchestrator grants approval. It does not replace the Orchestrator, does not approve or reject outputs directly, and does not modify any files.

**The Reviewer advises. The Orchestrator decides. Authority remains centralized.**

### Role and Boundaries

The Reviewer:
- Challenges agent outputs for priority violations, architectural breaches, quality regressions, and simplicity failures
- Acts as internal auditor, red team, compliance validator, architecture stress tester, and code quality reviewer
- Produces advisory findings only — it never issues approvals or rejections
- Has no file scope — it reads agent outputs and existing code context provided by the Orchestrator, but writes nothing
- Is spawned by the Orchestrator at workflow step 5a, only when trigger criteria are met
- Must not review its own prior advice — if a domain agent was re-dispatched based on Reviewer findings, the second pass is NOT sent back to the Reviewer for the same concern

### Trigger Criteria

| Tier | Condition | Review Required |
|------|-----------|----------------|
| MANDATORY | Task touches P1 (Architectural integrity) | Always |
| MANDATORY | Task touches P2 (Deterministic rule engine) | Always |
| MANDATORY | Task touches P3 (Event sourcing / auditability) | Always |
| MANDATORY | Task touches P4 (Version correctness) | Always |
| MANDATORY | Task involves cross-domain changes (multiple agents) | Always |
| MANDATORY | Task introduces a new pattern or abstraction | Always |
| OPTIONAL | Task touches P5–P7 (integrations, payroll, security) | Orchestrator discretion |
| SKIP | Small Tasks Exception applies (< 10 lines, single domain) | Never |
| SKIP | Pure UI fix with no backend change | Never |
| SKIP | Documentation-only change | Never |
| SKIP | Trivial seed data update (no schema or logic change) | Never |

### Finding Severity Levels

| Severity | Meaning | Expected Orchestrator Response |
|----------|---------|-------------------------------|
| BLOCKER | Priority violation or architectural breach | Strongly consider withholding approval and re-dispatching the responsible agent with the finding |
| WARNING | Quality or simplicity concern, potential future issue | Note in sprint log; address at Orchestrator discretion |
| NOTE | Suggestion for improvement; not blocking | Record if useful; no required action |

The Reviewer NEVER uses the words "approved", "rejected", "pass", or "fail" in its output.

### Scope per Invocation

The Orchestrator defines the Reviewer's scope each time. The Reviewer does not self-scope. Each invocation receives:
- The specific agent output(s) to review (diff, new files, changed files)
- The specific concerns to evaluate (e.g. "check P2 compliance", "assess cross-domain boundary violations")
- Relevant existing code context (file contents, interfaces, KB entries)

### Reviewer Prompt Template

When spawning the Reviewer Agent, use this structure:

```
You are the Reviewer Agent for the StatsTid project.

ROLE: Independent audit layer. You challenge agent outputs before Orchestrator approval.
You produce advisory findings only — you never approve, reject, or modify files.

REVIEW SCOPE:
[Orchestrator specifies which agent output(s) to review, which files changed, which concerns to evaluate.]

PRIORITY ORDER (from CLAUDE.md):
1. Architectural integrity
2. Deterministic rule engine
3. Event sourcing and auditability
4. Version correctness (including OK transitions)
5. Integration isolation and delivery guarantees
6. Payroll integration correctness
7. Security and access control
8. CI/CD enforcement
9. Usability and UX

AGENT OUTPUT:
[Diff or new file contents produced by the domain agent(s) being reviewed.]

EXISTING CONTEXT:
[Relevant existing file contents, interfaces, KB entries.]

REVIEW CHECKLIST:
[Orchestrator selects which checks apply:]
- [ ] P1 — Architectural integrity preserved? Bounded contexts respected?
- [ ] P2 — Rule engine code free of I/O, non-determinism, state?
- [ ] P3 — Events remain append-only? Auditability maintained?
- [ ] P4 — OK version resolution correct (entry-date, not current-date)?
- [ ] P5 — Integrations properly isolated? Delivery guaranteed?
- [ ] P6 — Payroll traceability chain maintained end-to-end?
- [ ] P7 — Security intact? New endpoints authorized?
- [ ] P8 — CI/CD will pass? Untested paths?
- [ ] Cross-domain — Output require changes outside agent's declared scope?
- [ ] Simplicity — Unnecessary complexity, over-abstraction, or duplication?
- [ ] Completeness — Gaps relative to acceptance criteria?

OUTPUT FORMAT:
Structured findings report using severity labels: BLOCKER, WARNING, NOTE.
Do not use "approved", "rejected", "pass", or "fail".
If no issues found: "No findings for the reviewed scope."

Format:
BLOCKER: [Title]
Priority: P[N]
Location: [File and line or section]
Finding: [What the problem is]
Recommendation: [What the agent should do]

SELF-REVIEW CONSTRAINT:
If this is a second pass on a previously flagged concern, do NOT re-raise it.
```

### Constraints

- The Reviewer has no file scope — it may not create, modify, or delete any file
- The Reviewer may not approve or reject agent outputs — it advises only
- The Reviewer may not self-assign review tasks — the Orchestrator controls invocation and scope
- The Reviewer must not re-raise findings it previously raised on the same concern in a re-dispatch cycle
- The Orchestrator is not required to act on every finding — WARNING and NOTE are at Orchestrator discretion
- The Orchestrator should document reasoning in the sprint log when choosing not to act on a BLOCKER
- The Reviewer does not appear in sprint log "Agent" fields — findings are recorded as validation evidence

## Plan Review (Step 0b)

The Plan Review is the earliest review checkpoint in the sprint workflow — invoked at sprint start, before any code is written. It complements the implementation reviews (Step 5α/5a/7a) by catching plan-level defects (missing validation criteria, ambiguous scope, wrong agent assignments, stale KB refs, gate/downstream-consumer mismatches encoded as features) while the cost of fixing them is still cheap (markdown edit, not code rewrite).

**Plan Review advises. The Orchestrator decides. Authority remains centralized.**

### Two Lenses

Plan Review runs both an external Codex pass and an internal Reviewer pass — the same complementary structure used at Step 5a. They have different priors and tend to catch different classes of plan defect:

| Lens | Catches |
|------|---------|
| External Codex (`codex exec`) | Scope tightness, validation-criteria coverage gaps, gate / downstream-consumer mismatches, ambiguous task boundaries, plan-encoded bugs (a "feature" that's really a security hole) |
| Internal Reviewer Agent | Architectural fit, simplicity vs. over-engineering, agent assignment correctness, KB reference freshness, alignment with priority order |

### Trigger Criteria

| Tier | Condition | Plan Review Required |
|------|-----------|----------------------|
| MANDATORY | Sprint touches P1 (Architectural integrity) | Always |
| MANDATORY | Sprint touches P3 (Event sourcing / auditability) | Always |
| MANDATORY | Sprint touches P4 (Version correctness) | Always |
| MANDATORY | Sprint touches P7 (Security / access control) | Always |
| MANDATORY | Sprint touches schema migrations or payroll export | Always |
| OPTIONAL | Sprint touches P5–P6 (integrations, payroll non-export) | Orchestrator discretion |
| SKIP | Sprint is documentation-only or pure tech-debt cleanup | Never |
| SKIP | Sprint replays a previously-validated plan with no scope change | Never |

Orchestrator discretion applies beyond this list. The cost-benefit calculus: 5–10 min at sprint start vs. at least one Codex implementation cycle on a plan-level bug (S19 cycle 1's BLOCKER is the canonical example).

### Invocation

**External Codex (plan mode)**: Unlike `codex review` (which targets a diff), `codex exec` runs a free-form prompt. The prompt references the plan path on disk so Codex reads the file via its tools — keeps the prompt size bounded and ensures Codex sees the latest version.

```
codex exec "<plan-review-prompt>"
```

**Internal Reviewer (plan mode)**: Spawn the Reviewer Agent via the `Agent` tool. Pass the plan path as REVIEW SCOPE and use the Plan Review prompt template below — it overrides the default Reviewer prompt's "agent output" framing for the plan-review case. The Reviewer reads the plan and returns the same BLOCKER/WARNING/NOTE format used elsewhere.

Both lenses can be run in parallel (they don't share state).

### Plan Review Prompt Template

When invoking either lens, pass this structure (substituting `[bracketed]` placeholders):

```
You are reviewing a sprint plan for the StatsTid project — a Danish public-sector workforce management SaaS — BEFORE implementation begins. The plan is at docs/sprints/SPRINT-N.md.

This is a PLAN review, not a CODE review. There is no diff yet. You are checking whether the plan itself is sound.

SPRINT GOAL: [one-line objective from the plan's "## Sprint Goal" section]

PRIORITY ORDER (what matters most, in order):
1. Architectural integrity
2. Deterministic rule engine (no I/O, no state, version-aware)
3. Event sourcing and auditability
4. OK version correctness (entry-date resolution)
5. Integration isolation and delivery guarantees
6. Payroll integration correctness (SLS codes, traceability)
7. Security and access control
8. CI/CD enforcement
9. Usability and UX

REVIEW FOCUS:
- **Scope tightness**: Are task boundaries clear? Any task that should be split or merged? Any work that belongs in a different sprint?
- **Validation criteria coverage**: For each task, do the validation criteria pin the actual invariant the task is supposed to enforce? Watch for criteria that test the wrong layer (e.g., the resolver but not the service-level branch choice).
- **Gate / downstream-consumer alignment**: For any task that adds a security gate or scope check, does the plan validate that the gate reads the SAME field the downstream consumer reads? Plan-encoded mismatches are common (S19 TASK-1901 was the canonical case).
- **Agent assignment correctness**: Does each task list the right Agent? Cross-domain tasks should be split or assigned to multiple agents.
- **KB reference freshness**: Are the listed ADR/PAT/DEP/RES/FAIL refs still relevant and pointing at current files?
- **Dependency closure**: Does the plan depend on prior sprint work that isn't yet committed?
- **Implicit features**: Watch for plan language that encodes a security hole as a feature (e.g., "top-level wins over nested" when the downstream reads nested).

OUTPUT:
Label findings as BLOCKER, WARNING, or NOTE. For each finding:
- The plan section / task ID being flagged
- What is wrong or concerning
- Recommended fix to the plan

If no issues found: "No findings for the reviewed plan."
```

### Output Handling

Plan Review findings are recorded in the sprint log under "## Plan Review (Step 0b)" — placed between the Entropy Scan section and Architectural Constraints Verified. Findings are mapped to BLOCKER/WARNING/NOTE. BLOCKERs MUST be addressed (plan edit) before Step 1 (decompose) begins.

### Cycle Cap

Same as External Review (Step 7a): 2 cycles per lens per sprint. After cycle 2 on either lens, halt and prompt the user — choices: (a) continue iterating, (b) accept findings and proceed to Step 1, (c) defer findings as a new sprint task.

### Constraints

- Plan Review findings are advisory; the Orchestrator decides whether to act
- Plan Review does NOT modify the plan — only the Orchestrator does, based on findings
- If `codex` CLI is unavailable at invocation time, run only the internal Reviewer pass and document the Codex skip in the sprint log
- A sprint that SKIPs Plan Review entirely (per the trigger table) records a one-line SKIP rationale in the sprint log under the same heading
- Plan Review does not appear in sprint log "Agent" fields — findings are recorded as validation evidence

## External Review (Codex)

The External Review is an independent, out-of-harness audit performed by the `codex` CLI. It brings an external perspective with no project-specific priors, complementing the internal Reviewer Agent. The Orchestrator invokes Codex via the `codex review` subcommand through the Bash tool.

**Codex advises. The Orchestrator decides. Authority remains centralized.**

### Role and Boundaries

External Review:
- Brings outside perspective — no KB priors, no harness assumptions
- Complements (does not replace) the internal Reviewer Agent
- Produces advisory findings only — never issues approvals or rejections
- Operates on git diffs, not individual agent outputs
- Is invoked via the `codex` CLI (`codex review`) through the Bash tool
- Is invoked only by the Orchestrator
- Has no write access to project files

### Invocation Modes

> **CLI constraint (verified on `codex-cli` 0.120.x)**: `codex review` does NOT accept a custom prompt and a diff-target flag (`--uncommitted` / `--base` / `--branch` / `--commit`) in the same invocation — the two forms are mutually exclusive. When a prompt is passed alone, Codex auto-detects the working copy's current uncommitted diff. When a diff-target flag is passed alone, Codex runs its default review prompt against that diff. See PR openai/codex#6538 for the documented contract.

| Mode | When | Command Pattern |
|------|------|-----------------|
| Per-task high-risk review (Step 5a override) | Orchestrator-triggered on high-risk tasks, runs alongside internal Reviewer. At Step 5a all sprint work is still uncommitted by workflow design, so the prompt-alone form targets the right diff. | `codex review "<prompt>"` |
| Sprint-end review (Step 7a) — no intermediate commits (preferred) | After tests green, internal Reviewer resolved, worktree merges complete but kept uncommitted on master. This is the default path — step 7 (commit) only runs AFTER step 7a. | `codex review "<prompt>"` |
| Sprint-end review (Step 7a) — intermediate commits exist | If worktree merges produced commits on master during the sprint, the base-anchored form must be used. Steering prompt is LOST in this form. | `codex review --base <sprint-start-commit>` |

`<sprint-start-commit>` is the HEAD commit of the previous sprint on master (i.e., the commit just before the current sprint's first task commit).

**Preference**: Keep sprint work uncommitted until after Step 7a so the prompt-steered form is available. When merging worktree branches in Step 6, prefer `git merge --squash` / file-copy into the working tree over `git merge --no-ff` to avoid creating intermediate commits on master.

### High-Risk Trigger Categories (Per-Task)

The Orchestrator triggers per-task Codex review at Step 5a when a task touches any of:

- **Schema migrations** — changes to table definitions in `docker/postgres/init.sql`
- **JWT/auth code** — `src/Infrastructure/**/Security/**`, `src/SharedKernel/**/Security/**`, JWT token issuance or validation
- **Payroll export** — SLS code assignments, `src/Integrations/**/Payroll/**`
- **Legal rule logic** — Danish agreement rules, OK version transitions, entitlement quotas, compliance checks
- **Retroactive correction paths** — correction event flows, period recalculation, OK version re-resolution

Orchestrator discretion applies beyond this list. Per-task Codex runs **in addition to**, not instead of, the internal Reviewer Agent.

### Per-Task Scope (Step 5a)

Per-task Codex review has a narrower scope than sprint-end review:
- **Focus**: Cross-check domain correctness with external eyes — logic bugs, edge cases, spec misalignment
- **Not the focus**: Architectural invariants (that is the internal Reviewer's job) or mechanical rules (that is the Constraint Validator's job)

This division prevents duplication with the internal Reviewer Agent on high-risk tasks.

### Finding Severity Levels

Same semantics as the internal Reviewer Agent:

| Severity | Meaning | Expected Orchestrator Response |
|----------|---------|-------------------------------|
| BLOCKER | Correctness, security, or architectural breach | Strongly consider withholding approval and re-dispatching the responsible agent |
| WARNING | Quality or simplicity concern | Note in sprint log; address at Orchestrator discretion |
| NOTE | Suggestion; not blocking | Record if useful; no required action |

Codex's native output may not use these exact labels. The Orchestrator maps Codex's findings to BLOCKER/WARNING/NOTE when recording them in the sprint log.

### Scope-Creep Governance (Review Cycle Cap)

To prevent sprint-commit delays, the Orchestrator tracks Codex review cycles within a single sprint. A **cycle** = one `codex review` invocation. Re-invocation after fixing Codex's own BLOCKERs counts as an additional cycle.

- **Cycle 1**: Normal — run Codex, act on findings.
- **Cycle 2**: Normal — re-run Codex after BLOCKER fixes.
- **Cycle 3 and beyond**: **Halt and prompt the human operator.** The Orchestrator must not invoke Codex a 3rd time without explicit user direction. The user chooses:
  - (a) continue iterating (Codex runs again);
  - (b) accept remaining findings and proceed to commit;
  - (c) split remaining findings off as a new sprint task.

The cap applies **separately** to sprint-end review (Step 7a) and per-task review (Step 5a). Record cycle counts in the sprint log.

### Codex Review Prompt Template

When invoking `codex review` in the prompt-alone form (`codex review "<prompt>"`), pass this structure as the single positional argument. For the base-anchored form (`codex review --base <sha>`), no prompt can be supplied — Codex's default review prompt runs instead.

```
Review this diff for the StatsTid project — a Danish public-sector workforce management SaaS.

SCOPE: [sprint-end | per-task high-risk — {category}]

SPRINT GOAL / TASK GOAL: [one-line sprint objective, or task acceptance criteria]

PRIORITY ORDER (what matters most, in order):
1. Architectural integrity
2. Deterministic rule engine (no I/O, no state, version-aware)
3. Event sourcing and auditability
4. OK version correctness (entry-date resolution)
5. Integration isolation and delivery guarantees
6. Payroll integration correctness (SLS codes, traceability)
7. Security and access control
8. CI/CD enforcement
9. Usability and UX

REVIEW FOCUS:
[sprint-end: "Cross-task consistency, architectural drift, spec-vs-implementation alignment, integration seams, gaps between sprint goal and delivered work."]
[per-task: "Domain correctness of the high-risk change. Logic bugs, edge cases, spec misalignment. Do NOT re-audit architectural invariants — that is covered by the internal Reviewer Agent."]

OUTPUT:
Label findings as BLOCKER, WARNING, or NOTE. For each finding, include:
- File and approximate location
- What is wrong or concerning
- Recommended action

If no issues found: "No findings."
```

### Constraints

- Codex findings are advisory only — the Orchestrator decides whether to act
- Codex is NOT invoked by domain agents — only by the Orchestrator
- Codex findings go to the sprint log, not to the knowledge base (KB entries remain Orchestrator-curated — an Orchestrator may author a KB entry based on a Codex finding, but Codex output is not a KB entry)
- If the `codex` CLI is unavailable at invocation time, the Orchestrator halts and prompts the user rather than silently skipping review
- Codex does not appear in sprint log "Agent" fields — findings are recorded as validation evidence
