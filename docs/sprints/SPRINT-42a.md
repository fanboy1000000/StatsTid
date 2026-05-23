# Sprint 42a — ADR-024 2nd Amendment (Cross-Process Boundary + Tx Envelope)

| Field | Value |
|-------|-------|
| **Sprint** | 42a (mirrors S41a precedent — 2nd ADR-024 amendment) |
| **Status** | **closed-with-discipline-rollback** |
| **Start Date** | 2026-05-23 |
| **End Date** | 2026-05-23 |
| **Sprint-start commit base** | `6dc9008` (S41a close) |
| **Sprint type** | **DESIGN-ONLY** — 2nd amendment to ADR-024 authored; Step 7a triggered discipline-rollback per amendment's own threshold |
| **Build Verified** | N/A (design-only) |
| **Test Verified** | 874 unchanged (design-only contract) |
| **Orchestrator Approved** | yes — 2026-05-23 (with rollback verdict) |
| **Plan** | `.claude/plans/PLAN-s42a.md` |
| **Phase** | 4e |

## Sprint Goal

Append `## Amendment 2026-05-23 — Cross-Process Boundary + Tx Envelope (S42a)` section to ADR-024 settling Seam E (rule-engine HTTP boundary doesn't carry merged config), Seam F (tx envelope vs EmitManifestAsync degraded-audit), Seam G (audit-line entry contract location).

## Cycle-trail context

6th sprint slot on ADR-024 work. If S43 refinement cycle 1 finds ANOTHER seam, smoke-alarm discipline calls for rollback of D1+D2 cutover entirely.

## Forward Pointers

- S43 = ADR-024 D1+D2 cutover (re-drafted against doubly-amended ADR)
- S44 = D7+D6+Bug #4
- S45 = D-test matrix + Phase E

---

## Sprint Close — Discipline Rollback

### Outcome

S42a authored the 2nd ADR-024 amendment at TASK-42A-01 (commit `21436aa`) settling 3 seams (E/F/G). Step 7a dual-lens immediately surfaced **a 4th cross-process caller** (`Orchestrator.WeeklyCalculationPipeline`) that the amendment missed — exactly the same-area thrash pattern from prior cycles, but at a NEW depth (a second cross-process caller after the 1st one was settled).

The amendment itself stated: *"if S43 refinement cycle 1 surfaces ANOTHER architectural seam, that's cycle 7 of same-area thrash — the discipline calls for ROLL BACK of ADR-024 D1+D2 cutover entirely."* The trigger fired **at S42a Step 7a, before S43 even started** — Step 7a (the amendment's own validation gate) surfaced the 7th-cycle seam.

User adjudication 2026-05-23: **rollback applied per discipline**. A 3rd amendment section appended to ADR-024 documents the suspension.

### Step 7a Dual-Lens Trail

| Lens | Verdict | Findings |
|------|---------|----------|
| Codex external | BLOCKED → discipline-rollback | 1 BLOCKER (Seam F recovery-claim contradiction) + 2 WARNINGs + 4 NOTEs |
| Reviewer Agent internal | BLOCKED → discipline-rollback | 2 BLOCKERs (Seam H Orchestrator caller + Seam F+G atomicity contradiction + sprint-sequencing) + WARNINGs + NOTEs |

Step 7a artifacts at `.claude/reviews/SPRINT-42a-step7a-{codex,reviewer}.md` with `reviewed-against-commit: 21436aa`.

### What was suspended

ADR-024 D1+D2 implementation cutover. Sprint slots S43 (planned cutover) + S44 (planned D7+D6) + S45 (planned D-test matrix) re-allocated.

### What stays in place

- S40 schema + repository + events + seed: DORMANT plumbing in the codebase
- S35 AC=AFSPADSERING bug-fix baseline preserved
- ADR-024 D1-D7 design decisions stay ACCEPTED (rollback is implementation-level, not design-level)
- S41a 1st amendment + S42a 2nd amendment stay in ADR text for historical record

### Commit list

```
5d27f67 S42a TASK-42A-00: sprint open
21436aa S42a TASK-42A-01: ADR-024 2nd amendment authored
[this commit] S42a TASK-42A-03: sprint close with discipline-rollback (3rd amendment appended + feedback memory + INDEX + ROADMAP updates)
```

(TASK-42A-02 Step 7a dual-lens ran but produced the rollback verdict rather than absorption — no separate absorption commit.)

### Forward pointers

- **ADR-024 D1+D2 cutover** — suspended until post-launch architectural sprint. Resumption criteria documented in the 3rd amendment.
- **Customer-go-live posture interim mitigation**: NO chefkonsulent / kontorchef AC employees on the platform at launch (operator screens during onboarding).
- **Bug #4 HK/PROSA seed flip** — stays in decision-recorded-fix-deferred state per S37 absorption.
- **Next sprint (S43)** — re-allocated. Options for the user at sprint-open: (a) ADR-025 multi-tenant operational concerns Sub-Sprint 1; (b) ADR-026 audit visibility Sub-Sprint 1; (c) Phase 4e legacy-DB-upgrade runbook (recurring deferred since S30/S31/S35); (d) Phase 4e employment_category dating (the Seam D Phase 4e candidate from S41a — actually now MORE valuable post-rollback since it addresses a sibling cross-process gap); (e) something else.

### Lesson recorded

New feedback memory `feedback_cross_process_caller_census_required.md` codifies the structural authoring discipline: cross-process ADR work requires enumerated caller census BEFORE seam decisions. Future ADRs touching HTTP/IPC boundaries apply this discipline at authoring time, not at review time.

The 7-cycle trail across the session (S38b + S40 cycles 1+2 + S41 cycle 1 → S41a amendment + S42 cycle 1 → S42a amendment + S42a Step 7a → rollback) is itself the artifact validating the smoke-alarm discipline from `feedback_thrash_defer_real_world.md`. The rollback fired at the right moment, with clear justification rooted in the amendment's own threshold language.
