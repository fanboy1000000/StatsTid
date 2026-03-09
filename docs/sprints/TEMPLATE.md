# Sprint N — [Title]

| Field | Value |
|-------|-------|
| **Sprint** | N |
| **Status** | planned \| in-progress \| complete |
| **Start Date** | YYYY-MM-DD |
| **End Date** | YYYY-MM-DD |
| **Orchestrator Approved** | no \| yes — YYYY-MM-DD |
| **Build Verified** | no \| yes — `dotnet build` 0 warnings 0 errors |
| **Test Verified** | no \| yes — N unit + N regression = N total passing |

## Sprint Goal
_One or two sentences describing the sprint objective._

## Entropy Scan Findings
_Results of pre-sprint entropy scan (step 0a). Omit if this is not the first sprint in a session._

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN \| DRIFT \| DEBT | _Stale paths found and fixed, or none_ |
| Pattern compliance spot-check | CLEAN \| DRIFT \| DEBT | _Anti-patterns found, or none_ |
| Orphan detection | CLEAN \| DEBT | _Unused files found, or none_ |
| Documentation drift | CLEAN \| DRIFT | _Stale MEMORY.md items, or none_ |
| Quality grade review | CLEAN \| updated | _Grades changed, or stable_ |

## Architectural Constraints Verified

_Check each constraint that was explicitly validated during this sprint._

- [ ] P1 — Architectural integrity preserved
- [ ] P2 — Rule engine determinism maintained (no I/O, no side effects)
- [ ] P3 — Event sourcing append-only semantics respected
- [ ] P4 — OK version correctness (entry-date resolution)
- [ ] P5 — Integration isolation and delivery guarantees
- [ ] P6 — Payroll integration correctness (traceability chain)
- [ ] P7 — Security and access control
- [ ] P8 — CI/CD enforcement
- [ ] P9 — Usability and UX

## Task Log

### TASK-N01 — [Task Title]

| Field | Value |
|-------|-------|
| **ID** | TASK-N01 |
| **Status** | complete \| partial \| blocked |
| **Agent** | [Rule Engine \| Data Model \| Payroll \| API Integration \| Security \| Test & QA \| UX \| Orchestrator] |
| **Components** | [list of affected bounded contexts / modules] |
| **KB Refs** | [ADR-xxx, PAT-xxx, DEP-xxx, RES-xxx — knowledge base entries relevant to this task] |
| **Constraint Validator** | pass \| violations — [count and summary] |
| **Reviewer Audit** | skipped \| performed — [summary or "no findings"] |
| **Orchestrator Approved** | yes — YYYY-MM-DD |

**Description**: _What was done and why._

**Validation Criteria**:
- [ ] Criterion 1
- [ ] Criterion 2

**Files Changed**:
- `path/to/file.cs` — description of change

---

_Repeat TASK block for each task in the sprint._

## Legal & Payroll Verification

| Check | Status | Notes |
|-------|--------|-------|
| Agreement rules match legal requirements | pending \| verified | _Which agreements were tested_ |
| Wage type mappings produce correct SLS codes | pending \| verified \| N/A | _Which mappings were validated_ |
| Overtime/supplement calculations are deterministic | pending \| verified | _Test evidence_ |
| Absence effects on norm/flex/pension are correct | pending \| verified \| N/A | _Which absence types were tested_ |
| Retroactive recalculation produces stable results | pending \| verified \| N/A | _Test evidence_ |

## Test Summary

| Suite | Count | Status |
|-------|-------|--------|
| Unit tests | N | all passing |
| Regression tests | N | all passing |
| Smoke tests | N | all passing \| N/A (requires Docker) |
| **Total** | N | — |

## Agent Effectiveness

| Metric | Value |
|--------|-------|
| Tasks | N |
| Constraint Violations | N |
| Reviewer Findings | NB, NW, NN |
| Re-dispatches | N |
| First-Pass Rate | N% |

## Sprint Retrospective

**What went well**: _Brief notes._

**What to improve**: _Brief notes._

**Knowledge produced**: _List any new KB entries (ADR/PAT/DEP/RES/FAIL) created during this sprint._
