# SPRINT-131 — Code-quality audit sweep (WS7)

| Field | Value |
|-------|-------|
| **Type** | Audit (read-only on product code) — the "S129, but for quality" sweep |
| **Baseline** | `7e4bb1b` (S130 sprint close; all-CI-green; working tree clean at kickoff) |
| **Read-only contract** | At sprint close, `git diff 7e4bb1b..HEAD` touches ONLY `docs/**` + `.claude/**`. Any exception (S129 precedent: a mid-audit CI-health fix) requires an explicit owner ruling recorded here. Only the Orchestrator writes docs; dimension agents + refuters return findings. |
| **Outputs** | `docs/operations/quality-finding-register.md` (QUAL-###) · re-grounded `docs/QUALITY.md` · S132 remediation proposal |
| **Refinement** | `REFINEMENT-s131-quality-audit-sweep.md` rev 2.1 — dual-lens Step-4 cycles 1+2 BOTH lenses (cycle 1: 4 BLOCKERs total, absorbed; cycle 2: cleared; snapshot tracked as `SPRINT-131-refinement-rev2.md`) |

## Entropy Scan Findings (Step 0a — 2026-08-19)

- **CLEAN**: PAT-005 spot-check — no illegal RuleEngine project references from other services.
- **CLEAN**: working tree clean at `7e4bb1b`; KB INDEX completeness is CI-enforced and green.
- **DRIFT (fixed in kickoff)**: `docs-governance-program.md` status line still read "NEXT: Phase 3 = WS5"
  though WS5 completed 2026-08-14 → corrected; this sprint registered as **WS7**.
- **DRIFT (in-sprint, assigned TASK-E)**: `QUALITY.md` governance header points at a CLAUDE.md "Quality
  Grading" section that now lives in WORKFLOW.md; the doc itself is the audit's re-grounding target
  (anchor S111; matrix "Last updated S64"; history table ends S35).

## Plan Review (Step 0b) — decision

**Satisfied by reference, not re-run.** This plan is a 1:1 transcription of the refinement, which passed
TWO full dual-lens cycles (Codex + internal Reviewer; 4 BLOCKERs raised and absorbed — including the exact
defect class S129's own Step-0b famously caught, the calibration-control leak, here closed structurally in
cycle 1/2 before any plan existed). A third identical review of the same content would be review-theater.
The reviewed artifact is tracked (`SPRINT-131-refinement-rev2.md`). The owner may demand a fresh Step-0b at
any time.

## Owner rulings (2026-08-19) — all four as recommended

1. **Full sweep** — all domains, one evidence base.
2. **Test-suite quality IN scope, sampled** — exhaustive on the top load-bearing suites + a declared
   stratified sample (frame recorded here when TASK-A fixes it).
3. **Gate promotions are proposals-only** — each a QUAL finding; owner rules per gate; the audit changes no
   CI behavior.
4. **Registration floor = Medium+** ("would plausibly change behavior, block/mislead a future change, or
   misinform a reader"); below-floor → one inventory appendix (counts + pointers, not adjudicated).

## Task list

- [ ] **TASK-A — method spec (the quality taxonomy).** Vendor a `quality-audit` skill (sibling of S129's
      `threat-model-audit`): per-dimension method for the 8 dimensions (architecture conformance ·
      complexity hotspots · test-suite quality · duplication/dead code · error-contract consistency ·
      warning debt · doc/code drift [isolated slice, enumerated excluded-path set] · observability/logging),
      the Medium+ severity rubric, evidence rules (conservative dead-code standard), inter-agent dedupe
      keys, and a per-dimension universe with an EXPLICIT exhaustive-vs-sampled declaration. Must NOT seed
      agents with already-registered items (SEC/F dedupe). **Dual-lens reviewed BEFORE any sweep agent runs.**
- [ ] **TASK-B — universe pin + calibration manifest.** Commit-pinned per-dimension inventory at `7e4bb1b`.
      Calibration items code/build-anchored ONLY, each **verified still-live at the baseline before
      sealing**; manifest **≥3 items** (S129 parity) or an owner-acknowledged fallback recorded here.
      Verified candidate #1: the **137 build warnings** (rebuilt twice at HEAD, exact; leak-immune — no
      tracked doc records the true count). Next pool candidate to verify: the Payroll CS0618 `[Obsolete]`
      opt-out. **Scored-dimension view rule (doc-echo closure): every scored dimension's worktree view
      excludes `docs/sprints/**` + the operations registers, and views are baseline-pinned** (the S131
      planning artifacts postdate the baseline → auto-excluded). CI-artifact availability (lizard report,
      coverage) verified as a prerequisite.
- [ ] **TASK-C — the sweep.** One agent per dimension over its declared universe; findings with file:line
      evidence + rubric severity. Doc-drift runs as its own isolated slice (reduced view, own method, no
      withheld scoring).
- [ ] **TASK-D — refute panel.** Every finding at/above the Medium+ floor: real? severity honest per the
      rubric? already tracked (SEC/F/ROADMAP → cross-ref, not a row)? Calibration hits with doc-derived
      (not code-derived) evidence chains do not count.
- [ ] **TASK-E — grade + adjudicate.** QUAL-### rows; QUALITY.md re-graded over a declared canonical domain
      set, every grade citing evidence (no-findings domains keep/raise grades explicitly); fix the stale
      governance-header pointer; file the pre-planned finding "promote check_docs freshness warning to a
      hard failure for QUALITY.md" (FAIL-006 class; proposals-only); owner rules per finding; S132
      remediation proposal drafted.

Out-of-scope pointers (recorded, not silent): performance (F register owns it) · dependency CVEs
(SEC/Dependabot/CI) · dependency staleness/abandonment (unowned — flagged for a future pass).

## Environment constraints (recorded)
No Python and no Docker on this machine → lizard/coverage numbers come from CI artifacts (verified in
TASK-B); Docker-gated test execution stays CI-only; Codex on PATH for all dual-lens gates (verified).
