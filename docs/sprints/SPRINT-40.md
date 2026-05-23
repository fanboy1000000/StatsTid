# Sprint 40 — ADR-024 Sub-Sprint 1 (Schema + Repository + Events)

| Field | Value |
|-------|-------|
| **Sprint** | 40 |
| **Status** | **complete** |
| **Start Date** | 2026-05-23 |
| **End Date** | 2026-05-23 |
| **Orchestrator Approved** | yes — 2026-05-23 |
| **Build Verified** | yes — `dotnet build StatsTid.sln -c Release` returns 0 errors + 0 new warnings (Infrastructure + SharedKernel both gate strict per S39 Directory.Build.props) |
| **Test Verified** | yes — Plain regression 35 → 40 (+5 Phase E tests); Unit 526 unchanged; EventSerializer reflection coverage test passes (65 typeofs); Docker-gated suite untouched by S40 scope |
| **Sprint-start commit base** | `3a6f41a` (S39 close, 2026-05-23) |
| **Sprint-end HEAD** | _filled by sprint-close commit_ |
| **Sprint type** | Implementation (schema + plumbing only; no cutover code) |
| **Plan** | `.claude/plans/PLAN-s40.md` |
| **Phase** | 4e (Phase D Implementation Sub-Sprint 1 per ADR-024 L234) |

## Sprint Goal

Lay the architectural foundation for ADR-024 (role-within-agreement modeling + correction policy + overtime authorization) without any rule-engine / payroll / endpoint / frontend changes. Schema tables + repository + event registrations + corrected seed values + Phase E `bug_correction_history` schema validation test. Subsequent ADR-024 cutover sprint (S41) dispatches against stable plumbing surface.

**Out of scope**: ConfigResolutionService 4-layer extension, OvertimeGovernanceRule cutover, D6 ConfigBugCorrected endpoint pattern, D2 DISCRETIONARY workflow, admin endpoints, frontend, HK/PROSA seed flip (all S41); D-tests beyond bug_correction_history schema, full Phase E continuous-validation (S42).

## Cycle-Trail Context

Refinement Step 4 ran 3 cycles to converge:
- Cycle 1 (7 BLOCKERs): originally bundled 3 ADRs in S40; misread binding ADRs. User adjudication: split per-ADR.
- Cycle 2 (4 BLOCKERs): even ADR-024-full was too big; ADR-024's own Consequences section splits implementation across 3 sub-sprints. User adjudication: honor ADR-author's sub-sprint split.
- Cycle 3 (1 mechanical SQL syntax WARNING): clean otherwise; absorbed inline.

Step 0b plan review then surfaced 4 plan-vs-codebase BLOCKERs (audit column convention + FK target + event class shape + 6-vs-5 boolean count) all absorbed at cycle 1 of Step 0b; cycle 2 verified clean.

Both superseded refinements + the cycle-trail markdown live as `*-cycle-trail.md` artifacts under `.claude/refinements/` for future-sprint reference.

## Phase Decomposition

All tasks Orchestrator-direct sequential. No worktrees. Init.sql is single-file so schema tasks must be sequential.

| Phase | Tasks | Dispatch |
|-------|-------|----------|
| 0 | TASK-4000 | Sprint open plumbing |
| 1 | TASK-4001..4002 | Schema (role_config_overrides + audit; overtime_pre_approvals extension + overtime_authorization_audit) |
| 2 | TASK-4003 | RoleConfigOverrideRepository (5th versioned-config pattern) |
| 3 | TASK-4004 | EventSerializer wiring 7 new types (58 → 65) |
| 4 | TASK-4005 | Greenfield seed: 8 rows (4 AC strata × 2 OK versions) |
| 5 | TASK-4006 | Phase E `bug_correction_history` schema validation test |
| 6 | TASK-4007 | Sprint close |

## Step 7a Dual-Lens (TASK-4007 at close)

**MANDATORY** per `sprint-close-guard.ps1` hook. Codex + Reviewer Agent against S40 diff vs `3a6f41a`. Cycle-cap 2 per lens. Review focus:
- Schema column types + indices match ADR-024 D1 + D7
- Repository follows AgreementConfigRepository pattern verbatim (audit-bearing Pattern B)
- EventSerializer count 58 → 65; reflection coverage test passes
- 8 seed rows with correct tri-state values per ADR-024 L46-50
- bug_correction_history schema test passes

## Test Summary

Target: 869 baseline + ~5 new Phase E tests in plain regression = ~874 total. `sprint-test-validation` SKIP per design-light-implementation (mostly plumbing). Tooling sprint contract: no test count regression.

## Forward Pointers

- **S41 = ADR-024 Sub-Sprint 2 (cutover)** per ADR-024 L245-254: ConfigResolutionService 4-layer + OvertimeGovernanceRule + PayrollMappingService + admin endpoints + frontend + necessity-ack endpoint + HK/PROSA seed flip (Bug #4 final) + D6 ConfigBugCorrected endpoint pattern. ~15-18 tasks.
- **S42 = ADR-024 Sub-Sprint 3** (D-tests + Phase E completion). ~10-12 tasks.
- **S43+** = ADR-025 sub-sprints. **S46+** = ADR-026 sub-sprints.

---

## Sprint Close

### Outcome

**ADR-024 Phase D Implementation Sub-Sprint 1 closed cleanly.** Schema + repository + 7 events + greenfield seed + Phase E bug_correction_history schema test all landed without scope-creep. S41 cutover (admin endpoints + ConfigResolutionService 4-layer extension + OvertimeGovernanceRule + payroll + frontend + HK/PROSA seed flip) can dispatch against stable plumbing surface.

**Real findings closed during execution**:
1. Pre-existing ConcurrentSeedConflictException class extended to support composite-key resources (role_config_overrides has 3-part natural key); S35 UserId-keyed call sites preserved via legacy alias property
2. SoftDelete pattern formalized for role_config_overrides per ADR-023 D8: no version bump on the predecessor; partial-unique-index makes the row "disappear" from live reads
3. Phase E test brought up 12 existing bug_correction_history entries in source register — all 5 invariants pass; no source-register defects flagged

### Step 7a Dual-Lens Trail

Both lenses ran against `eea09d4..HEAD` (6 work commits) at sprint close 2026-05-23.

| Lens | Verdict | Cycles | Artifact |
|------|---------|--------|----------|
| Codex external | APPROVED-WITH-WARNINGS (1 WARNING + 1 NOTE; 0 BLOCKER) | 1 | `.claude/reviews/SPRINT-40-step7a-codex.md` |
| Reviewer Agent internal | APPROVED — 1 documentation NOTE; 0 BLOCKER | 1 | `.claude/reviews/SPRINT-40-step7a-reviewer.md` |

Both findings absorbed at close:
- Codex WARNING (AppendAuditAsync single-overload vs AgreementConfigRepository 3-overload trio) → code-comment added explaining the deliberate simplification: RoleConfigOverride's 4 actions all share the same audit-shape, so 3 overloads would be ceremony without value. S41 cutover endpoint emitters call the single method directly.
- Reviewer NOTE (RoleConfigOverrideCreated.cs xmldoc Case-B mislabel) → xmldoc rewritten to clarify "Covers Case A only" with cross-references to Updated (Case B) and Superseded (Case C).

Cycle-cap = 1 per lens (no iteration needed).

### Commit List

6 work commits + sprint-open + 1 close-absorption + sprint-close = 9 commits total:

```
eea09d4 S40 TASK-4000 sprint open
9a43950 S40 TASK-4001 role_config_overrides + audit schema
c5d8a93 S40 TASK-4002 overtime_pre_approvals D7 extension + audit
0d88ab5 S40 TASK-4003 RoleConfigOverrideRepository (878 LOC, 5th versioned-config pattern)
b4d8043 S40 TASK-4004 7 new event types (EventSerializer 58 → 65)
4ddbb7f S40 TASK-4005 greenfield seed (4 AC strata × 2 OK = 8 rows)
c4cbb55 S40 TASK-4006 Phase E bug_correction_history schema test (5 plain regression tests)
[this commit + 1 absorption] S40 TASK-4007 sprint close
```

### Quality Re-grade

- **SharedKernel (Events)**: 7 new typeof registrations; EventSerializer count moves 58 → 65. Mechanical scaling; grade held at **A-**.
- **Infrastructure**: 5th versioned-config repository (RoleConfigOverrideRepository) follows the established ADR-020 D2 + ADR-023 D8 pattern from S29/S30/S31/S33/S34. Grade held at **A**.
- **Domain Correctness** (new domain): partial-credit at **B** (Phase E bug_correction_history schema test landed; seed-parity + unknown-unknown + DRAFT-OK source-cite tests still defer to S42). Full **A** grade gated on S42 cutover-dependent Phase E completion.
- Other domains unchanged.

### Source-Register Annotations (DEFERRED to S41 cutover)

Per TASK-4005's commit message — 8 `bug_correction_history` annotations for the seeded role_config_overrides rows. Deferring to S41 because:
1. The source register's existing per-cell row structure (15 columns; SR-AC-OK24-NNN naming) doesn't yet have an established convention for role_config_overrides cells (different cardinality — per `(employment_category, agreement_code, ok_version)` rather than per-config-field)
2. S41 cutover naturally exercises these rows via rule-engine + payroll-mapping code, providing the implementation context for the SR row design
3. Phase E test currently passes against the 12 existing entries — adding 8 more isn't a correctness gate this sprint

S41 close will land the SR annotations + extend Phase E test coverage to verify them.

### Architectural Constraints Verified

- [x] P1 — No architecture changes; schema + repository + events match established patterns
- [x] P2 — No rule code touched (cutover is S41)
- [x] P3 — 7 new event types registered; reflection coverage test passes; EventSerializer 58 → 65
- [x] P4 — RoleConfigOverrideRepository follows ADR-020 D2 3-case + ADR-023 D8 SoftDelete patterns
- [x] P5 — No outbox/publisher/consumer changes; events register but emit only in S41
- [x] P6 — No payroll code touched
- [x] P7 — No new endpoints this sprint (S41 cutover)
- [x] P8 — All S39 CI gates (warn-as-error + .NET Analyzers security + gitleaks + vulnerable-package + smoke + vitest + lizard + coverage baseline) hold on master HEAD
- [x] P9 — No UX changes

