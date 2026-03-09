# StatsTid Orchestrator Workflow

> **Governance note**: This document is Orchestrator-owned. Only the Orchestrator may modify it. All workflow steps are mandatory unless explicitly marked otherwise.

This document defines the Orchestrator's mandatory workflow, governance processes, and sprint management rules. For agent definitions, see [AGENTS.md](AGENTS.md). For the priority order, see [../CLAUDE.md](../CLAUDE.md).

## Orchestrator Workflow (MANDATORY)
When given an implementation task (sprint plan, feature request, bug fix spanning multiple domains):

0. **Consult Knowledge Base**: Read `docs/knowledge-base/INDEX.md` and identify relevant entries for the task. Use the Domain Index to select entries to include in each agent's prompt.
0a. **Entropy Scan** (sprint start only): Before the first task of a new sprint, run the Entropy Scanner to detect drift and accumulation. See "Entropy Management" section for details. Record findings in the sprint log header. Fix critical findings before proceeding; note non-critical findings for future sprints.
1. **Decompose**: Break the task into domain-scoped subtasks. Each subtask must map to exactly one domain agent. (See [AGENTS.md](AGENTS.md) for agent definitions and file scopes.)
2. **Delegate**: Spawn one Agent per subtask using the Claude Code `Agent` tool with `subagent_type: "general-purpose"`. Each agent receives:
   - Its domain role (e.g. "You are the Rule Engine Agent")
   - The specific files it may create or modify (see [AGENTS.md](AGENTS.md) for File Scope)
   - The priority order from this CLAUDE.md
   - Exact acceptance criteria for its subtask
   - All necessary context (existing file contents, interfaces it must conform to, models it depends on)
3. **Parallelize**: Run independent agents concurrently. Use `isolation: "worktree"` when multiple agents write to the repo simultaneously. Use `run_in_background: true` for agents that can work while others proceed.
4. **Phase dependencies**: Organize agents into phases when outputs depend on each other:
   - **Phase 1** (parallel): Data Model Agent + Rule Engine Agent + UX Agent (independent domains)
   - **Phase 2** (parallel): Payroll Integration Agent + API Integration Agent (depend on models/events from Phase 1)
   - **Phase 3** (sequential): Test & QA Agent (depends on all implementation code)
   - **Phase 4** (sequential): Orchestrator validates — `dotnet build && dotnet test`
5. **Validate**: After all agents complete, the Orchestrator runs build and test. If validation fails, identify the responsible agent and re-dispatch with the error context.
5α. **Constraint Validation**: Run the Constraint Validator Agent (see [AGENTS.md](AGENTS.md)) on every agent output (except documentation-only changes). If violations are found, re-dispatch the responsible agent with the violation list before proceeding. For Small Tasks Exception, the Orchestrator self-checks against the Constraint Validator checklist. This step is fast and mechanical — it should catch rule violations that agents missed in their pre-submission checklist.
5a. **Reviewer Audit**: If the task meets trigger criteria (see [AGENTS.md](AGENTS.md) Reviewer Agent section), spawn the Reviewer Agent with the agent's output as context. Read the Reviewer's findings before proceeding to step 5b. The Orchestrator decides how to act on findings — a BLOCKER finding is a strong signal to withhold approval and re-dispatch, but authority remains with the Orchestrator. Do NOT invoke the Reviewer for tasks covered by the Small Tasks Exception.
5b. **Review Knowledge Proposals**: Review agent outputs for `PROPOSED KNOWLEDGE ENTRY` sections. Approve valid proposals by creating entries in `docs/knowledge-base/` and updating `INDEX.md`.
5c. **Record in Sprint Log**: After validating each task, record it in the current sprint's `docs/sprints/SPRINT-N.md` with validation criteria, files changed, and KB references.
6. **Merge**: If agents ran in worktrees, the Orchestrator merges their branches and resolves any conflicts.
7. **Commit & Push**: After a sprint is completed successfully (build passes, tests pass, sprint log updated), the Orchestrator commits all changes and pushes to the remote repository. This is mandatory — every completed sprint must be committed and pushed before moving on.

## Constraints
- No agent may modify files outside its declared scope
- No agent may modify system architecture (CLAUDE.md, SYSTEM_TARGET.md, ROADMAP.md, .sln, docker-compose.yml, init.sql schema structure) without Orchestrator approval
- Agents are specialists — they do not self-assign tasks
- The Orchestrator is the only entity that decomposes goals, assigns work, and validates outputs
- If an agent encounters a cross-domain dependency, it must declare it in its output rather than modifying the other domain's files
- All agent outputs must pass `dotnet build` before the Orchestrator accepts them
- No agent may create, modify, or delete files under `docs/knowledge-base/` — this is an Orchestrator-only directory
- No agent may create, modify, or delete files under `docs/sprints/` — this is an Orchestrator-only directory
- The Reviewer Agent may not create, modify, or delete any file — it has no file scope
- No domain agent may invoke the Reviewer Agent — only the Orchestrator may spawn it

## Small Tasks Exception
For trivial changes (single-file fix, typo, < 10 lines changed in one domain), the Orchestrator may implement directly without spawning an agent. This exception must not be used to bypass the multi-agent workflow for substantive work.

# Knowledge Base
The project maintains a structured, version-controlled knowledge base at `docs/knowledge-base/`.

## Governance
- **Orchestrator-only writes**: Only the Orchestrator may create, modify, or delete files under `docs/knowledge-base/`. Agents may propose new entries but cannot write them directly.
- **Version-controlled**: The knowledge base lives in the git repository and persists across sessions and machines.
- **Outside agent scopes**: `docs/` is not within any agent's declared file scope — this is by design.

## Entry Categories
| Prefix | Directory | Purpose |
|--------|-----------|---------|
| ADR | `decisions/` | Architectural Decision Records — why we chose X over Y |
| PAT | `patterns/` | Validated pattern conventions — how we do X |
| DEP | `dependencies/` | Cross-domain dependency registry — X depends on Y |
| RES | `resolutions/` | Priority conflict resolutions — when P2 conflicts with P9 |
| FAIL | `failures/` | Failure/pivot log — what we tried that didn't work and why |

## Agent Responsibilities
- **Before implementation**: The Orchestrator consults `docs/knowledge-base/INDEX.md` and includes relevant entries in agent prompts.
- **During implementation**: Agents MUST respect all approved knowledge base entries provided in their context.
- **Proposing new knowledge**: If an agent discovers a new pattern, dependency, or decision during implementation, it includes a `PROPOSED KNOWLEDGE ENTRY` section in its output for Orchestrator review.
- **Never modify directly**: Agents must never create, edit, or delete files under `docs/knowledge-base/`.

# Sprint Log
The project maintains a structured sprint log at `docs/sprints/` — a formal governance artifact that documents completed work with validation evidence and traceability.

## Governance
- **Orchestrator-only writes**: Only the Orchestrator may create, modify, or approve sprint log entries. Agents report task completion; the Orchestrator records it.
- **Version-controlled**: Lives in the git repository alongside the knowledge base.
- **Separate from git history**: Git commits record *what* changed; the sprint log records *why*, *who approved it*, *what was validated*, and *which constraints were verified*.

## Sprint Log Structure
- `docs/sprints/INDEX.md` — Master index with sprint table, cumulative metrics, constraint coverage matrix
- `docs/sprints/TEMPLATE.md` — Template for new sprints
- `docs/sprints/SPRINT-N.md` — One file per sprint

## Task Entry Fields
Each task in a sprint log includes:
- **ID**: TASK-NNN (sprint-prefixed, e.g., TASK-301)
- **Status**: complete | partial | blocked
- **Agent**: Which domain agent performed the work (see [AGENTS.md](AGENTS.md) for agent definitions)
- **Components**: Affected bounded contexts / modules
- **KB Refs**: Related knowledge base entries
- **Orchestrator Approved**: yes/no with date
- **Validation Criteria**: Checklist of acceptance criteria with pass/fail
- **Files Changed**: List of affected files

## Orchestrator Workflow Integration
- **Sprint start**: Copy TEMPLATE.md → SPRINT-N.md, fill metadata and goal
- **During sprint**: Record each validated task as agents complete work
- **Sprint end**: Verify all architectural constraints, run build/test, approve sprint, update INDEX.md, commit and push to remote
- **Post-sprint**: Record retrospective and knowledge base entries produced

## Legal & Payroll Traceability
Each sprint log includes a Legal & Payroll Verification table tracking:
- Agreement rule compliance
- Wage type mapping correctness
- Overtime/supplement determinism
- Absence effect accuracy
- Retroactive recalculation stability

# Roadmap
The project maintains a phased roadmap at `ROADMAP.md` using a rolling detail pattern.

## Governance
- **Rolling detail**: Only the next sprint has task-level planning. Future phases have milestone-level descriptions.
- **Sprint promotion**: After completing a sprint, the Orchestrator promotes the next sprint to detailed planning and updates ROADMAP.md.
- **Coverage tracker**: ROADMAP.md includes a SYSTEM_TARGET.md coverage tracker showing projected completion per phase.
- **Orchestrator-only writes**: Only the Orchestrator may modify ROADMAP.md. Already enforced by the Constraints section (ROADMAP.md in protected files list).
- **Consistency**: Sprint plans in ROADMAP.md must be consistent with sprint logs in `docs/sprints/`.

# Sprint Numbering & Re-prioritization

## Sequential Sprint Numbers
- Sprint numbers are strictly sequential: the next sprint is always N+1 where N is the last completed sprint.
- Never skip sprint numbers. Sprint numbers track execution order, not thematic grouping.
- Task IDs follow the sprint: Sprint 6 tasks are TASK-601, TASK-602, etc.

## Phases vs Sprints
- Phases are thematic groupings (e.g. "RBAC + Frontend"). Sprints are chronological execution units.
- ROADMAP.md assigns projected sprint ranges to phases (e.g. "Phase 2: Sprints 6-8"). These are projections, not fixed assignments.
- When execution order changes, update the phase-to-sprint mapping in ROADMAP.md — never adjust sprint numbers to match the old projection.

## Re-prioritization Procedure
When new requirements arrive that change the planned execution order, the Orchestrator must replan before executing. The depth of replanning is tiered by scale of impact.

### Tier 1 — Small addition (1 planned sprint affected or fewer)
1. Assign the next sequential sprint number (N+1) to the new work
2. Update ROADMAP.md: adjust phase-sprint ranges, shift deferred phases forward
3. Update the coverage tracker to reflect new ordering
4. Proceed to execution

### Tier 2 — Cross-cutting change (2+ planned sprints affected)
1. Assign the next sequential sprint number (N+1) to the new work
2. Write an **Impact Assessment** section in ROADMAP.md before execution, containing:
   - Which existing planned sprints are affected and how (scope change, dependency shift, new prerequisites)
   - Whether any planned sprint must be split, merged, or have tasks added/removed
   - Updated phase-sprint ranges for all affected phases
   - Updated coverage tracker projections
3. Rewrite the detailed plan for the next sprint (N+1) incorporating the impact
4. Clear or rewrite any stale detailed plans for future sprints that were invalidated
5. Proceed to execution only after the impact assessment is complete

### Common rules (both tiers)
- Deferred phases shift forward — their projected sprint numbers increase accordingly
- Completed sprints are never renumbered — they are historical records
- SYSTEM_TARGET.md must be updated if the new requirements introduce functional areas not yet documented

# Entropy Management

The project uses scheduled entropy scans to prevent drift and accumulation between sprints. This is inspired by the "garbage collection" pattern from harness engineering.

## Pre-Sprint Entropy Scan (Step 0a)

At the start of each new sprint, the Orchestrator runs a lightweight entropy scan. This is NOT a full agent — the Orchestrator performs it directly using Glob/Grep/Read tools.

### Scan Checklist

1. **KB Path Validation**: Verify all file paths referenced in `docs/knowledge-base/` entries still exist. Flag stale references.
2. **Pattern Compliance Spot-Check**: Grep for known anti-patterns:
   - Direct Rule Engine function calls from non-RuleEngine services (PAT-005 violation)
   - `FindFirst("scopes")` usage (FAIL-001 regression)
   - Hardcoded `http://localhost` URLs in non-test code
   - Missing `RequireAuthorization` on endpoint definitions
3. **Orphan Detection**: Check for files that exist but are not imported/referenced anywhere (unused components, dead models). Focus on files created in the previous 2 sprints.
4. **Documentation Drift**: Verify that MEMORY.md deferred items list is current (no items already completed, no missing new items).
5. **Quality Grade Review**: Update `docs/QUALITY.md` grades if the previous sprint changed domain quality.

### Recording

Entropy scan findings are recorded in the sprint log header under "## Entropy Scan Findings":
- **DRIFT**: Stale reference or outdated documentation — fix before proceeding
- **DEBT**: Non-critical accumulation — note in sprint log, fix when convenient
- **CLEAN**: No issues found for this check

If the scan finds DRIFT items, the Orchestrator fixes them before starting sprint tasks. DEBT items are tracked but don't block sprint execution.

# Quality Grading

The project maintains `docs/QUALITY.md` — a persistent quality assessment per domain and architectural layer, updated after each sprint.

## Purpose
- Visibility into which areas are fragile, undertested, or accumulating tech debt
- Data-driven sprint planning — prioritize work in low-grade domains
- Historical tracking of quality trends across sprints

## Governance
- **Orchestrator-only writes**: Updated by the Orchestrator at sprint end (step 5c) or during entropy scan (step 0a)
- **Not a judgment tool**: Grades reflect objective measures (test coverage, pattern compliance, documentation completeness), not subjective quality

## Grade Scale
- **A**: High test coverage, full pattern compliance, well-documented, low tech debt
- **B**: Adequate coverage, mostly compliant, some documentation gaps, manageable debt
- **C**: Gaps in coverage or compliance, noticeable debt, needs attention in upcoming sprints
- **D**: Significant gaps, active tech debt, should be prioritized for improvement
- **F**: Broken or fundamentally non-compliant — immediate action required

# Agent Effectiveness Metrics

The sprint INDEX.md tracks agent effectiveness metrics to enable data-driven improvement of agent prompts and governance.

## Metrics Tracked

| Metric | Definition |
|--------|-----------|
| **Tasks** | Total tasks in the sprint |
| **Constraint Violations** | Count of violations caught by the Constraint Validator (step 5α) |
| **Reviewer Findings** | Count by severity: B=BLOCKER, W=WARNING, N=NOTE |
| **Re-dispatches** | Number of agents re-dispatched after validation failures |
| **First-Pass Rate** | Percentage of tasks accepted without re-dispatch: (Tasks - Re-dispatches) / Tasks |

## Usage
- If First-Pass Rate drops below 80%, investigate whether agent prompts need more KB context or clearer constraints
- If a specific agent type consistently produces violations, add domain-specific items to its pre-submission checklist
- If the same violation type recurs across sprints, escalate it to a new PAT or FAIL entry in the knowledge base

# Harness Evolution

The governance structure in this file is a "harness" — it constrains, informs, and corrects agent behavior. As model capabilities improve, parts of this harness may become unnecessary overhead.

## Rippable Harness Principle
Every governance mechanism should be periodically evaluated for cost vs. value. If a constraint consistently catches zero violations across 5+ sprints, consider relaxing or removing it. The goal is minimum effective governance, not maximum governance.

## Evaluation Cadence
- **Every 5 sprints**: Review agent effectiveness metrics. Identify constraints with zero violations and consider relaxation.
- **On model upgrade**: When the underlying model changes significantly, reassess whether existing constraints are still necessary. Capabilities that required pipelines in earlier sprints may only need prompting with newer models.
- **On workflow friction**: If a governance step consistently slows delivery without catching real issues, mark it for evaluation.

## Observability Integration (Future — Phase 4+)
When Docker services run in production-like environments, agent prompts may include runtime context:
- Recent error logs from the target service
- Performance metrics (latency, error rates)
- Database query patterns and slow queries

This enables agents to debug production issues autonomously. Implementation requires:
- MCP server or API access to logging infrastructure
- Scoped permissions (read-only access to logs, no write access to production)
- Prompt template extensions for runtime context injection

## Documentation Drift Prevention
To prevent documentation from diverging from code:
- **Entropy scan step 1**: KB path validation catches stale file references
- **Entropy scan step 4**: MEMORY.md deferred items review catches completed-but-not-removed items
- **Sprint log review**: Each sprint log lists files changed — cross-reference against docs that reference those files
- **Future enhancement**: CI-step that validates all file paths in `docs/knowledge-base/*.md` resolve to existing files
