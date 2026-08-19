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

- [ ] **TASK-A — method spec (the quality taxonomy).** *Status: drafted + dual-lens reviewed (Codex 1
      BLOCKER / 4 W; internal 0 BLOCKER / 7 W / 6 N) → all findings absorbed in one revision pass →
      Codex cycle-2 re-check pending.* **The recorded D3 sampling frame (final):** exhaustive tier of 9
      tree-computable rules (payroll/settlement · authz/scope · outbox/events · OK-version/migrations ·
      audit/projection · the rule-engine virtual stratum `*RuleTests.cs` ∪ `/Rules/` ∪ `*Accrual*` (~13
      files) · architecture-constraints · SEC-id-citing tests · the smoke suite whole) + a
      largest-and-smallest-per-stratum sample over FIVE suites (DemoSeed declared IN as its own stratum;
      co-located `*.test.ts(x)` join the nearest `__tests__` stratum) + three pattern-level full scans
      (Docker/env gating, assertion-free methods, swallowed catches). **Named residual (the declared cost
      of sampling, per OQ-2):** assertion-present-but-weak tests in mid-size unsampled files are
      systematically unexamined. Notable review catches: the drafting agent itself refused to write the
      warning count into the skill (leak-immunity); the review moved the sole-guard-test example from
      Critical to High under the new ruled boundary. Vendor a `quality-audit` skill (sibling of S129's
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
      excludes `docs/sprints/**`, the three operations registers, AND (TASK-A review absorption) the three
      ledger-class debt docs — `docs/operations/s64-regression-debt-census.md`, `docs/QUALITY.md`,
      `docs/operations/docs-governance-program.md`; views are baseline-pinned** (the S131 planning
      artifacts postdate the baseline → auto-excluded). **Sealing checklist (TASK-A review, both lenses):
      (i) per item — "no in-view doc records this item"; (ii) cross-check the manifest against every exact
      fact the skill itself states** (the skill's stated counts — 16 csproj, 26 endpoint files, 157 routes,
      35 ILogger files — are thereby foreclosed as withheld items, generalizing the drafting agent's own
      137 catch). CI-artifact availability (lizard report, coverage) verified as a prerequisite.
      **Status: SEALED (2026-08-19).** Manifest = **3 items, spread D6 ×2 + D4 ×1**, each verified
      still-live at `7e4bb1b` by direct git-pinned greps; full loci in the gitignored
      `.claude/sweeps/S131-calibration-manifest.md` (NOT recorded here until the sweep completes —
      belt-and-braces on top of the baseline-pinned views). Sealing checklist PASSED: per-item
      no-in-view-doc-records-it (grep-verified); no overlap with any skill-stated exact fact; no
      SEC/F/ROADMAP dedupe collision. Notes: one candidate was dropped because its only finder is the
      UNSCORED D7 slice (structurally cannot host a scored item — recorded as a design fact); a second
      candidate sharpened during verification (the item is not merely unused code — it bypasses a
      correctness contract). **CI-artifact prerequisite outcome:** artifact download requires
      authentication this machine lacks (no gh CLI/token) → **D2 falls back to agent-read complexity
      estimation for BOTH C# and frontend** (allowed by the refinement, hereby declared). ROADMAP.md
      added to the scored-view exclusion set (ledger-class backlog — same rationale as the three debt
      docs; D7 already excluded it).
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

## Pre-known findings (recorded at the TASK-A review, 2026-08-19 — so view-exclusions cost nothing)

The TASK-A dual-lens review surfaced two genuine doc-map gaps in files the D7 drift-checker will never see
(both are excluded from its view), so they are recorded HERE as pre-known findings and will be filed as
QUAL rows at TASK-E without sweep credit:
1. **`docs/operations/s64-regression-debt-census.md` is unrouted** — CLAUDE.md's doc map does not mention
   it at all (the TASK-A draft even mis-cited the doc map as designating it historical — a citation to a
   routing that does not exist).
2. **`docs/references/vacation-settlement-law-research.md` is unrouted** — a point-in-time S67 research
   verdict absent from CLAUDE.md's doc map (its siblings are all routed under "Historical & research
   dossiers").

## Environment constraints (recorded)
No Python and no Docker on this machine → lizard/coverage numbers come from CI artifacts (verified in
TASK-B); Docker-gated test execution stays CI-only; Codex on PATH for all dual-lens gates (verified).
