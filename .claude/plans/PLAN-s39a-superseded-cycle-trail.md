# PLAN — Sprint 39a: ADR Amendment + Sprint Shift (audit-visibility slot reallocation)

> **STATUS — SUPERSEDED 2026-05-23 by governance commit (no sprint replacement needed)**
>
> Step 0b cycle 1 on this plan surfaced 6 same-area BLOCKERs across both Codex + Reviewer lenses. Both lenses converged on option (c) rollback. User pulled the cord with the diagnostic *"is all this back and forth because we can't number the sprints correctly?"* — surfacing the actual root cause.
>
> **Root cause**: ADRs were authored with sprint-number-shaped bindings (*"binding for S39 schema migration"*, *"cannot defer past S39"*). Sprint numbers are sequential execution indexes that shift on re-prioritisations; binding ADRs to them creates a cascade-rename surface across 3+ docs every time a sprint slot moves.
>
> **Fix landed instead**: `docs/WORKFLOW.md` § "Binding to Architectural Events, Not Sprint Numbers" governance rule + projection disclaimer at top of ADR-024 + ADR-025 + ADR-026. ~50 lines total. Future ADRs follow the rule; pre-rule ADRs have explicit disclaimer pointing to it. No sprint-number rename required.
>
> **Feedback memory**: `feedback_adrs_bind_to_events_not_sprints.md` captures the lesson.
>
> **This file kept for cycle-trail audit** alongside the (also-superseded) PLAN-s39.md. The 6-cycle trail (4 refinement + 2 plan-review) is itself the artifact — proves that wide-surface mechanical-rename work is the wrong granularity for planning, and that the governance fix is the right level of intervention.

---


| Field | Value |
|-------|-------|
| **Sprint** | 39a (sub-sprint splitting off the architectural amendment work; mirrors S38b precedent for design-only-style sub-sprints — preserves S40 main-sequence numbering for the deferred audit-visibility schema migration) |
| **Phase** | 4e (architectural amendment to settle S39 re-prioritisation per refinement → Step 0b smoke-alarm split) |
| **Sprint type** | **DESIGN-ONLY** — 3-ADR amendment + cross-doc sprint-shift + ROADMAP renumber. No src/ changes; no test count changes. |
| **Base commit** | `d5c6a87` (S38b post-close governance hook extension, 2026-05-23) |
| **Refinement** | `.claude/refinements/REFINEMENT-s39-tooling-debt.md` (READY after 4 cycles; this sub-sprint absorbs the architectural-amendment portion only) |
| **Predecessor plan** | `PLAN-s39.md` (SUPERSEDED 2026-05-23 by S39a/S39b split per Step 0b cycle 1 escalation) |
| **Sprint open date** | 2026-05-23 |
| **Projected end date** | 2026-05-23 (single-day, mirrors S38b pattern) |
| **Task count** | 5 (TASK-39A-00..04) |
| **Customer-go-live impact** | +1 sprint slip per ROADMAP L25; will become +2 sprint slip if S39b (tooling) runs before audit-visibility resumes at S40+, or +1 sprint slip if S39b is itself deferred — final answer determined at S39a close when S39b sequencing decision is made |

## Sprint Goal

Amend ADR-024 + ADR-025 + ADR-026 to slide audit-visibility binding implementation sprints by +1 (S39→S40, S40→S41, S41→S42), rename `PROGRAM-s36-s41-domain-correctness.md` → `PROGRAM-s36-s42-domain-correctness.md` + shift its internal sprint references, shift reference docs (role-dimension-audit, danish-agreements, agreement-source-register, phase-b-handoff-package, agreement-ruleset-audit), update ROADMAP.md with Impact Assessment block — all such that audit-visibility implementation can resume cleanly at S40 once S39a closes.

**Sub-sprint rationale**: Step 0b cycle 1 on the bundled PLAN-s39.md surfaced 3 same-area BLOCKERs (find-and-replace spec ambiguity + TASK-3914 scope omission + amendment-block format precedent divergence). That's the 5th consecutive review cycle in the same area (refinement cycles 1-4 + Step 0b cycle 1). Per `feedback_thrash_defer_real_world.md` smoke-alarm discipline + user adjudication 2026-05-23, the architectural amendment work is wider than a single bundled sprint can carry. S39a runs the amendment in isolation; S39b (tooling gates, scope-narrowed) follows.

**Out-of-scope for S39a** (moves to S39b or later):
- Quality gate lifts (gitleaks, vulnerable-package check, Dependabot, global.json, .config/dotnet-tools.json, Directory.Build.props, .NET Analyzers, coverlet baseline, lizard CCN, smoke + vitest CI wiring)
- QUALITY.md re-grade
- CI workflow changes

## BLOCKER Absorption from PLAN-s39 Step 0b cycle 1

This plan incorporates fixes for ALL 6 BLOCKERs surfaced at Step 0b cycle 1 that fall within S39a scope. Each absorption is recorded inline at the relevant task:

| Step 0b BLOCKER | Source | Absorption |
|---|---|---|
| TASK-3912 find-and-replace ambiguous (chained vs ordered) | Codex BLOCKER #1 | TASK-39A-01 rewrites as 3 explicit ordered passes with `sed` (or PowerShell equivalent) one-shot commands per source→target pair |
| TASK-3914 scope omits PROGRAM filename refs in 3 ADRs | Reviewer BLOCKER #1 | TASK-39A-01 extended to include PROGRAM filename refs (`PROGRAM-s36-s41` → `PROGRAM-s36-s42`) inside the 3 ADRs; TASK-39A-03 audits historical refs in sprint docs (those stay per WORKFLOW.md L161) |
| TASK-3912 amendment-block format diverges from S38 TASK-3803 precedent | Reviewer BLOCKER #3 | TASK-39A-01 switches to `## Amendment — sprint shift (S39a, 2026-05-23)` SECTION at bottom of each ADR per ADR-013:46 precedent; verification grep filter adjusted to `grep -vE '^## Amendment'` (or section-marker-aware filter) |
| P2 missing from architectural constraints checklist | Codex BLOCKER #2 (carry-forward to S39b) | This sprint adds P2 to the checklist below (ADRs touched have P2 binding via ADR-024 D2 tri-state on rule path); S39b inherits the corrected checklist shape |
| Agent assignments outside declared scopes | Codex BLOCKER #3 (carry-forward to S39b) | This sprint uses **Orchestrator-direct on all tasks** (KB writes + ROADMAP edits are Orchestrator-only per CLAUDE.md §Agent Architecture). S39b will fix the agent-scope assignments for CI/repo-root tasks. |
| TASK-3905 smoke wiring requires docker harness in CI | Reviewer BLOCKER #2 (carry-forward to S39b) | Out-of-scope for S39a. S39b plan will explicitly split TASK-3905 into (a) docker-compose CI harness step (new) + (b) smoke test wiring (consumes the harness). |

## Phase Decomposition

Orchestrator-direct sequential per S38b / S32 / S28 design-only precedent. No worktrees.

| Phase | Tasks | Dispatch |
|-------|-------|----------|
| 0 | TASK-39A-00 | Sprint open plumbing |
| 1 | TASK-39A-01 | 3-ADR amendment (programmatic find-and-replace + section-at-bottom amendment block + 3rd customer-go-live surface) |
| 2 | TASK-39A-02 | ROADMAP renumber + Impact Assessment block |
| 3 | TASK-39A-03 | PROGRAM rename + reference-doc sprint-shift + sprint-doc historical-context audit |
| 4 | TASK-39A-04 | Step 7a dual-lens review + sprint close |

## Step 0a — Entropy Scan

| Check | Result |
|-------|--------|
| KB path validation | CLEAN — ADR-024 + ADR-025 + ADR-026 all exist and are ACCEPTED; PROGRAM-s36-s41-domain-correctness.md exists at expected path |
| ADR amendment precedent | CONFIRMED — `ADR-013-retroactive-corrections-single-period-no-cascade.md:46` shows `## Amendment — ADR-024 cross-reference (S38 TASK-3803, 2026-05-21)` section-at-bottom format. TASK-39A-01 follows this precedent. |
| Worktree count | 77 (per Step 0b cycle 1 NOTE — corrected from stale "80+" in superseded plan) |
| Mechanical gate | ACTIVE per `297fdee` + extended by `d5c6a87`; sprint-open does not trigger staleness check; sprint-close may trip if ADR amendments are reviewed against pre-amendment commits — see Risk #1 below |
| Refinement disposition | READY (post 4-cycle absorption); S39a scope = subset (architectural amendment only) |

## Step 0b — Plan Review

**MANDATORY** for this sub-sprint per `feedback_thrash_defer_real_world.md` smoke-alarm follow-through:

- Step 0b cycle 1 on PLAN-s39 fired the escalation criterion → split executed
- Step 0b cycle 1 on PLAN-s39a (this file) is a fresh start with the BLOCKERs absorbed
- **Strict cycle-cap = 2 per lens**, NO post-S35 governance extension. If Step 0b cycle 1 on S39a surfaces ANY same-area BLOCKER, escalate to user immediately (cycle 6 of same-area thrash would confirm S39a itself is wider than this sub-sprint can carry — fallback to option (c) "roll back to S39 = audit per ADR-026 verbatim").

Dispatch dual-lens (Codex external + Reviewer Agent internal) on this PLAN file before TASK-39A-01 dispatches.

## Architectural Constraints

_Checked at sprint close._

- [ ] **P1 — Architectural integrity** → 3-ADR amendment lands cleanly with no broken cross-references; PROGRAM rename consistent across all consumers; amendment-block section-at-bottom format matches ADR-013 precedent
- [ ] **P2 — Deterministic rule engine** → ADR-024 D2 `MerarbejdeCompensationRight` tri-state binding shifts from S40→S41 cutover; **rule code untouched this sprint**; binding integrity verified by TASK-39A-01 amendment block at bottom of ADR-024
- [ ] **P3 — Event sourcing / auditability** → No event schema changes; ADR-026 D13 (sync-in-tx projection) commitment preserved by amendment (sprint slot shifts only)
- [ ] **P4 — Version correctness** → No version handling changes
- [ ] **P5 — Integration isolation** → No integration changes
- [ ] **P6 — Payroll integration correctness** → No payroll changes; ADR-024 D6 `ConfigBugCorrected` binding shifts from S40→S41 cutover; payroll mapping untouched
- [ ] **P7 — Security / access control** → No security changes
- [ ] **P8 — CI/CD enforcement** → No CI changes (deferred to S39b)
- [ ] **P9 — Usability / UX** → No UX changes

---

## Task Log

### Phase 0 — Sprint Open

#### TASK-39A-00 — Sprint-open plumbing

| Field | Value |
|-------|-------|
| **ID** | TASK-39A-00 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PLAN-s39a.md` (this file), `docs/sprints/SPRINT-39a.md`, `docs/sprints/INDEX.md` provisional entry |

**Validation Criteria**:
- [ ] PLAN-s39a.md filed with full task log + Step 0a + Step 0b sections
- [ ] SPRINT-39a.md initial sprint-doc filed
- [ ] INDEX.md provisional Sprint 39a entry added
- [ ] Sprint-open commit through hook

---

### Phase 1 — 3-ADR Amendment (TASK-39A-01)

| Field | Value |
|-------|-------|
| **ID** | TASK-39A-01 |
| **Status** | pending |
| **Agent** | Orchestrator-direct (KB writes are Orchestrator-only per CLAUDE.md §Agent Architecture) |
| **Components** | `docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md`, `docs/knowledge-base/decisions/ADR-025-multi-tenant-operational-concerns.md`, `docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md` |

**Operation** (3 explicit ordered passes, NOT chained transforms):

1. **Sprint-number shift, pass 1: S41 → S42** (do this FIRST so S40 → S41 in pass 2 doesn't double-apply)
   ```
   # Across the 3 ADRs only
   sed -i -E 's/\bS41\b/S42/g; s/\bSprint 41\b/Sprint 42/g; s/\bTASK-41([0-9]{2})\b/TASK-42\1/g' \
     docs/knowledge-base/decisions/ADR-024-role-within-agreement-modeling.md \
     docs/knowledge-base/decisions/ADR-025-multi-tenant-operational-concerns.md \
     docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md
   ```
2. **Sprint-number shift, pass 2: S40 → S41**
   ```
   sed -i -E 's/\bS40\b/S41/g; s/\bSprint 40\b/Sprint 41/g; s/\bTASK-40([0-9]{2})\b/TASK-41\1/g' \
     <same 3 files>
   ```
3. **Sprint-number shift, pass 3: S39 → S40**
   ```
   sed -i -E 's/\bS39\b/S40/g; s/\bSprint 39\b/Sprint 40/g; s/\bTASK-39([0-9]{2})\b/TASK-40\1/g' \
     <same 3 files>
   ```
4. **PROGRAM filename ref shift** (across the 3 ADRs only — sprint docs/PLANs stay per WORKFLOW.md L161 "completed sprints never renumbered"):
   ```
   sed -i 's/PROGRAM-s36-s41-domain-correctness/PROGRAM-s36-s42-domain-correctness/g' <same 3 files>
   ```
5. **Customer-go-live commitment text** (manual edit, 3 surfaces per Reviewer WARNING #5):
   - ADR-025 L204 (current: "ADR-026 cannot defer past S39") — pattern-replace pass 3 above already changed this to "cannot defer past S40" which is wrong intent → manual revert to **"ADR-026 cannot defer past customer-go-live"**
   - ADR-025 L268 ("cannot defer past S39 per launch commitment") — same manual edit to "cannot defer past customer-go-live per launch commitment"
   - ADR-026 L367 ("ADR-026 cannot defer past S39") — same manual edit to "ADR-026 cannot defer past customer-go-live"
6. **Append amendment section at bottom of each of the 3 ADRs** per S38 TASK-3803 / ADR-013:46 precedent:
   ```markdown
   ## Amendment — sprint shift (S39a, 2026-05-23)
   
   Per `REFINEMENT-s39-tooling-debt.md` cycle-1 user decision + Step 0b cycle 1 escalation 2026-05-23: S39 sprint slot re-allocated to tooling-debt work. This ADR's binding implementation sprints slide by +1: S39 → S40 schema migration, S40 → S41 cutover, S41 → S42 D-tests. Customer-go-live unblock-architecturally commitment per ROADMAP L25 preserved (audit-visibility ships before customer-go-live; only the implementation sprint slot shifts).
   
   See `PLAN-s39a.md` + `PLAN-s39.md` (superseded) for amendment trail. See `SPRINT-39a.md` for sprint-close record.
   ```

**Verification** (executed before commit):

- `grep -nE '\bS39\b|TASK-39[0-9]{2}|cannot defer past S39|PROGRAM-s36-s41-domain-correctness' docs/knowledge-base/decisions/ADR-024*.md docs/knowledge-base/decisions/ADR-025*.md docs/knowledge-base/decisions/ADR-026*.md` returns ZERO matches outside the "## Amendment" sections at the bottom (filter: `grep -vE '^## Amendment|^Per .REFINEMENT-s39|^See .PLAN-s39a'` or equivalent post-`## Amendment`-header line filtering)
- `grep -nE 'cannot defer past customer-go-live' docs/knowledge-base/decisions/ADR-025*.md docs/knowledge-base/decisions/ADR-026*.md` returns at least 3 matches (ADR-025 L204 + L268 + ADR-026 L367)
- Each of the 3 ADRs has a "## Amendment — sprint shift (S39a, 2026-05-23)" section at the bottom
- `grep -nE 'PROGRAM-s36-s41-domain-correctness' docs/knowledge-base/decisions/ADR-024*.md docs/knowledge-base/decisions/ADR-025*.md docs/knowledge-base/decisions/ADR-026*.md` returns ZERO matches
- **Reviewer Agent invocation on the commit diff before merge** — check (a) cross-reference invariants (every `[[ADR-XXX]]` style link still resolves), (b) section-numbering integrity (no orphan section refs), (c) intent preservation, (d) no spurious matches in code samples / source-register row IDs

**Validation Criteria**:
- [ ] All 6 operation steps executed in order
- [ ] All 4 verification greps pass
- [ ] Interim Reviewer Agent dispatch on TASK-39A-01 commit diff completes without BLOCKER
- [ ] Build clean (no .NET impact from doc-only changes)

---

### Phase 2 — ROADMAP Renumber + Impact Assessment (TASK-39A-02)

| Field | Value |
|-------|-------|
| **ID** | TASK-39A-02 |
| **Status** | pending |
| **Agent** | Orchestrator-direct (ROADMAP.md is Orchestrator-only per CLAUDE.md) |
| **Components** | `ROADMAP.md` |

**Operation**:
1. Add new completed-sprint row "Sprint 39a — ADR Amendment + Sprint Shift" to the completed-sprints table (with `_filled at close_` test-count placeholder = 869 unchanged per design-only)
2. Update Phase Roadmap section: shift S39/S40/S41 references in Phase 4e bullets to S40/S41/S42 (audit-visibility), insert S39a + S39b descriptions
3. Add Impact Assessment block per existing convention (S18/S19/S21 precedents in the same file), citing `docs/WORKFLOW.md:127 §Sprint Numbering & Re-prioritization` as the governing convention. Document:
   - Trigger: REFINEMENT-s39-tooling-debt.md user decision + Step 0b cycle 1 smoke-alarm split
   - Effect: customer-go-live commitment slips +1 sprint (or +2 if S39b runs before audit-visibility resumes)
   - Rationale: pre-launch tooling debt cheaper before more audit-visibility code lands; ADR amendment isolated to S39a to avoid wide-surface bundling

**Validation Criteria**:
- [ ] Sprint 39a row added to completed-sprints table
- [ ] Phase Roadmap section sprint-number shifts applied (only the Phase 4e forward-pointer bullets, NOT the historical completed-sprints table)
- [ ] Impact Assessment block added with full rationale
- [ ] Cross-references to PLAN-s39a.md + PLAN-s39.md (superseded) documented

---

### Phase 3 — PROGRAM Rename + Reference-Doc Sprint Shift (TASK-39A-03)

| Field | Value |
|-------|-------|
| **ID** | TASK-39A-03 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/plans/PROGRAM-s36-s41-domain-correctness.md` (rename target); `docs/references/role-dimension-audit.md`, `docs/references/danish-agreements.md`, `docs/references/agreement-source-register.md`, `docs/references/phase-b-handoff-package.md`, `docs/references/agreement-ruleset-audit.md` |

**Operation**:
1. `git mv .claude/plans/PROGRAM-s36-s41-domain-correctness.md .claude/plans/PROGRAM-s36-s42-domain-correctness.md`
2. Apply the SAME 3 ordered sprint-number-shift passes from TASK-39A-01 (S41→S42, then S40→S41, then S39→S40) to:
   - The renamed PROGRAM doc
   - The 5 reference docs listed in Components
3. Add a "## Amendment — sprint shift (S39a, 2026-05-23)" section at the bottom of the PROGRAM doc per the same precedent
4. **Historical-context preservation** per Reviewer BLOCKER #1 + WORKFLOW.md L161: do NOT touch sprint docs (SPRINT-35.md … SPRINT-38b.md) or completed-PLAN docs (PLAN-s35.md … PLAN-s38b.md). Their S39/S40/S41 references are historical and correct as-of-when-written. The dangling `PROGRAM-s36-s41-domain-correctness.md` filename references in those historical docs become legitimate snapshot references; future readers see them as "the file as it existed at sprint N" and can locate the renamed file via the git history.

**Verification**:
- `grep -nrE '\bS39\b|TASK-39[0-9]{2}|Sprint 39|PROGRAM-s36-s41' .claude/plans/PROGRAM-s36-s42-domain-correctness.md docs/references/` returns ZERO matches outside the new "## Amendment" section in the PROGRAM doc and any legitimate "S39a tooling" / "S39b tooling" forward-pointer references
- `git mv` recorded in git log
- ROADMAP.md cross-references to the PROGRAM file still resolve (PROGRAM-s36-s42-domain-correctness.md exists)

**Reviewer Agent invocation** on the TASK-39A-03 commit diff with same check criteria as TASK-39A-01.

**Validation Criteria**:
- [ ] PROGRAM file renamed
- [ ] 3 sprint-shift passes applied to PROGRAM + 5 ref docs
- [ ] Amendment section added to PROGRAM
- [ ] Historical sprint docs + PLAN-s35..s38b untouched
- [ ] Verification grep clean
- [ ] Reviewer Agent on commit diff returns no BLOCKER

---

### Phase 4 — Step 7a Dual-Lens + Sprint Close (TASK-39A-04)

| Field | Value |
|-------|-------|
| **ID** | TASK-39A-04 |
| **Status** | pending |
| **Agent** | Orchestrator-direct |
| **Components** | `.claude/reviews/SPRINT-39a-step7a-{codex,reviewer}.md`, `docs/sprints/SPRINT-39a.md` (close sections), `docs/sprints/INDEX.md`, `MEMORY.md` entry |

**Step 7a dispatch**:
- Codex external review on full S39a diff vs `d5c6a87` — focus: does the find-and-replace mechanic produce semantically-identical post-state? does the amendment section at the bottom of each ADR adequately record the change rationale? any cross-reference invariants broken?
- Reviewer Agent on same diff — focus: architectural fit (3-ADR amendment is novel for the project), cycle-trail discipline (was the smoke-alarm split the right call vs the pragmatic-patch alternative), governance hook compliance

Cycle-cap = 2 per lens. If cycle 2 surfaces a NEW BLOCKER in same area as refinement cycles 1-4 + Step 0b cycle 1 — that would be cycle 7 of same-area thrash, **immediate halt-and-prompt for option (c) rollback** (keep S39 as audit per ADR-026 verbatim).

**Sprint close**:
- All 4 prior tasks marked complete
- Sprint-end HEAD commit hash backfilled
- ROADMAP S39a entry status flipped to "complete"
- INDEX.md Sprint 39a entry filled
- MEMORY.md entry with cycle-trail recorded (refinement cycles 1-4 + Step 0b cycle 1 split + S39a clean close)
- `.claude/hooks/sprint-close-guard.ps1` passes; **override rationale recorded in commit message if staleness check fires** on T-39A-01 ADR amendments (ADR-026 was ACCEPTED 2 days ago at S38b close — the staleness check could trigger)

**Validation Criteria**:
- [ ] Step 7a Codex artifact at `.claude/reviews/SPRINT-39a-step7a-codex.md` with verdict line
- [ ] Step 7a Reviewer artifact at `.claude/reviews/SPRINT-39a-step7a-reviewer.md` with verdict line
- [ ] Both verdicts CLEAN or APPROVED-WITH-NOTES (no BLOCKER)
- [ ] Sprint-close-guard hook passes
- [ ] All architectural constraints P1-P9 checked off
- [ ] S39a closed; S39b sequencing decision pending user input post-close

---

## Forward Pointers

- **PLAN-s39b.md** (to be drafted after S39a close): tooling gates per superseded PLAN-s39's Phase 1 + Phase 2 + QUALITY re-grade, with the following BLOCKER absorptions inherited from PLAN-s39 Step 0b cycle 1:
  - P2 included in architectural constraints checklist
  - Agent assignments corrected (Orchestrator-direct for CI/repo-root tasks; Builder/Test agents only for src/ + tests/ scopes)
  - TASK-3905 explicitly split: (a) CI docker-compose harness step + (b) smoke test wiring as 2 separate tasks
  - gitleaks task body includes appsettings + .claude/ + tests/Fixtures + init.sql bcrypt explicitly
  - Phase 1 reorder: gitleaks first (per Reviewer NOTE #2) — secret-baseline visibility before other config changes ride atop
  - Sprint sizing: re-examine — without the 5 ADR-amendment tasks, S39b is ~13 tasks. Compare to S35 (11) + S25 (~12). Realistic.
- **S40 = audit_projection schema migration** (was S39 pre-amendment, was S40 in superseded PLAN-s39): resumes audit-visibility per amended ADR-024 + ADR-025 + ADR-026
- **S41 = ADR-026 cutover** (cascade-shifted)
- **S42 = audit-visibility D-tests** (cascade-shifted)
- **Customer-go-live**: now slides by +1 sprint if S39b runs after S40-S42 (option (c) sequencing), or +2 sprints if S39b runs before S40 (the original re-prioritisation intent). Decision deferred to S39a close.

---

## Lessons from Cycle Trail (refinement cycles 1-4 + Step 0b cycle 1)

1. **Refine the operation, not the enumeration**. Pre-enumerating mechanical work at refinement-time creates over-specification traps when the surface is wide.
2. **Reverse-order pattern-replace** is the canonical mechanism for cascading sprint-number / version shifts. Apply highest-number-first to prevent double-application.
3. **Whole-word match boundaries** (`\bS39\b`) are critical when sprint numbers can substring-match other identifiers.
4. **Amendment blocks intentionally contain stale-numbered references** — verification greps must exclude amendment-block lines (filter by section header marker).
5. **Smoke-alarm vs pragmatic-fix is a user-adjudication, not a rule of thumb**. Cycle-4 refinement absorption used pragmatic-fix because cycle-4 substance was one-filter-expression sized. Step 0b cycle 1 had 3 same-area BLOCKERs simultaneously — split was right because the substance was larger.
6. **WORKFLOW.md L161 "completed sprints never renumbered"** is the right invariant for historical sprint docs / PLANs: rename target = current planning docs only; historical docs preserve sprint-number context as-of-when-written.
7. **Wide-surface architectural amendments deserve their own sprint** (S38b precedent for ADR-026 authorship; S39a precedent for 3-ADR cross-amendment). Don't bundle with implementation work.
8. **Sub-sprint numbering convention** (S38b, S39a) allows architectural amendments without burning main-sequence sprint slots — customer-go-live slip is +1 main-sequence sprint regardless of sub-sprint count.
