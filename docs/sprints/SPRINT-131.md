# SPRINT-131 — Code-quality audit sweep (WS7)

| Field | Value |
|-------|-------|
| **Type** | Audit (read-only on product code) — the "S129, but for quality" sweep |
| **Baseline** | `7e4bb1b` (S130 sprint close; all-CI-green; working tree clean at kickoff) |
| **Read-only contract** | At sprint close, `git diff 7e4bb1b..HEAD` touches ONLY `docs/**` + `.claude/**`. Any exception (S129 precedent: a mid-audit CI-health fix) requires an explicit owner ruling recorded here. Only the Orchestrator writes docs; dimension agents + refuters return findings. **Close-time status: PRODUCT SURFACE UNTOUCHED (verified — no `src/`, `frontend/`, `tests/`, `tools/`, `docker/` path in the baseline diff). Three root-level GOVERNANCE files sit outside the literal allowlist: `CLAUDE.md` (doc-map row, kickoff), `.gitignore` (sweeps-dir entry, kickoff), `ROADMAP.md` (TASK-E backlog/precondition updates). All Orchestrator-only governance docs consistent with the allowlist's intent (its rationale names product surface); recorded here as a deviation-of-letter for owner ratification rather than silently widened.** |
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

- [x] **TASK-A — method spec (the quality taxonomy).** *Status: DONE (committed `8cf593e`) — dual-lens
      reviewed (cycle 1: Codex 1 BLOCKER / 4 W; internal 0 BLOCKER / 7 W / 6 N) → all findings absorbed
      in one revision pass → Codex cycle-2 CLEARED.* **The recorded D3 sampling frame (final):** exhaustive tier of 9
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
- [x] **TASK-B — universe pin + calibration manifest.** *(DONE, committed `50b2bf1`.)* Commit-pinned per-dimension inventory at `7e4bb1b`.
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
- [x] **TASK-C — the sweep. COMPLETE 2026-08-19** (same day): 8 round-1 dimension agents + 6
      supplemental agents (D6-supp, D3-supp-A/B/C/D, D4-supp; one usage-limit interruption + resume),
      all returned with zero-silent-gaps coverage declarations. **Final candidate pool: ~170 Medium+
      rows (~28 proposed High, 0 Critical) + below-floor appendices**, archived verbatim/faithful in
      `.claude/sweeps/S131-agent-reports/` (14 reports). Owner-ruled D3 frame delivered: exhaustive
      tier 100% read (Unit 47 + Regression ~90 + Smoke whole), stratified sample 44/45 targets read in
      full, three pattern scans exhaustive. Two candidate PRODUCT defects escalated out of test
      analysis for TASK-D product-side verification: the daily-rest rule's midnight-crossing
      computation; the segment-manifests boundaryCause encoding split. Execution facts of record:
      - **View enforcement verified pre-launch:** two worktrees at `7e4bb1b` (`.claude/worktrees/s131-scored`
        + `s131-docdrift`), exclusion sets confirmed applied on disk (scored: sprints + 3 registers +
        s64-census + program doc + QUALITY.md + ROADMAP.md absent, dossiers present; docdrift: the strict
        superset — dossiers/reviews/caller-census also absent). `.claude/**` absent from both.
      - **Method injection:** CONVENTIONS.md (baseline-pinned copy) + the reviewed skill concatenated
        BIT-EXACT (mechanical file copy, no retyping) into a session-scratchpad artifact each agent must
        read in full before starting and confirm in its coverage declaration — chosen over 8× manual
        prompt transcription to guarantee fidelity to the reviewed text.
      - **Agent profile:** read-only tool profile (no Write/Edit) + worktree confinement + no-git-commands
        rule (commit messages are answer-bearing; the tree on disk is the universe).
      - **D6 artifact:** Orchestrator rebuilt the solution in the scored worktree
        (`dotnet build --no-incremental`): 0 errors, **137 warnings** — matches the sealed QC-1 count
        (artifact-integrity check PASSED). Full console log supplied to the D6 agent (count itself not
        disclosed in the prompt; the agent derives it).
      - **D2 fallback frame (Orchestrator-declared, per the TASK-B fallback ruling):** C# = line-count
        census of all `src/**/*.cs` → top 25 longest read in full + every Tier-1-path file (priority by
        length; unread Tier-1 files must be NAMED); frontend frame unchanged from the spec. All CCN
        figures labeled hand-counted estimates.
      - **ROUND 1 COMPLETE (2026-08-19, same day):** all 8 dimension agents returned. Candidate Medium+
        rows: D1=5 (1 High) · D2=15 · D3=8 (3 High) · D4=14 (2 High) · D5=15 (1 High) · D6=6 · D7=18
        (3 High) · D8=12 (4 High) ≈ **93 candidate rows** (pre-dedupe, pre-refute) + rich below-floor
        appendices. Verbatim/faithful archives: `.claude/sweeps/S131-agent-reports/D1..D8-*.md`
        (gitignored). Zero-silent-gaps held: every agent returned an explicit coverage declaration with
        named gaps and "could not verify" lists.
      - **Calibration (round 1): 1 of 3 — findings acceptance HALTED per the sealed rule.** One
        withheld item rediscovered blind with a code-derived chain (the D6 build-artifact integrity
        item, exact); two missed. Miss analysis (recorded in the gitignored manifest; loci stay out of
        tracked docs until sweep close): both misses share one root-cause class — round 1 verified the
        EXISTENCE of justifications/references but not their CURRENT TRUTH at the member/condition
        level. This is a method gap, not an effort gap, and it is fixable by construction.
      - **Supplemental round progress (2026-08-19):** D6-supp returned — **withheld item 2 RECOVERED**
        (independently surfaced, code-derived chain, + 7 further Medium rationale-truth findings; 34
        suppression sites verified, 0 skipped). D3-supp-A returned (partition COMPLETE: 66 files /
        25,384 lines; 11 Medium+ incl. 2 High — headline: both payroll replay marquees compare payloads
        provably independent of the mutated field). D3-supp-B returned (partition COMPLETE: 53 files /
        19,058 lines; 15 Medium+ incl. 3 High — headline: the atomic-outbox suite family largely
        performs assertion theater; 14 forced-rollback tests never attempt the guarded write).
        D3-supp-C returned (Unit tier COMPLETE — all 25 remaining files; 24 of 45 sample targets read;
        13 Medium+ incl. 5 High — headline: the daily-rest rule's midnight-crossing gap is a candidate
        PRODUCT correctness defect; the rule-classification registry is pinned by no test). Regression
        exhaustive tier verified COMPLETE across the union of round-1 + supp-A + supp-B. A sixth agent
        (**D3-supp-D**) launched for the 20 remaining largest-per-stratum sample files; the D4-supp
        census agent was interrupted mid-run by a session usage limit and RESUMED after reset.
      - **D4-supp returned (2026-08-19): withheld item 3 RECOVERED.** The corrected production-vs-test
        caller census (592 public members, 100% of its declared roots; 68 overload families
        disambiguated by call-site argument shape) independently surfaced the withheld family from
        code, plus 16 Medium rows covering **86 production-unused public members** — headline: across
        12 repository classes the self-connection write overload is production-unused WITHOUT
        EXCEPTION (production always routes the in-transaction sibling that the ADR-018 outbox+audit
        atomicity contract binds); both audit-log query methods are dead; the replay-determinism
        entry points have no production consumer.
      - **CALIBRATION FINAL: round-1 score 1/3 stands on the record; all 3 withheld items were
        independently rediscovered (QC-1 round 1; QC-2 + QC-3 by corrected-method supplemental
        passes, unseeded). Both misses shared one method-gap class (existence-verified vs
        currently-true); miss analyses CLOSED; findings-acceptance HALT LIFTED.** TASK-D convenes
        when D3-supp-D (the last coverage agent) returns.
      - **Supplemental round (originally 5 agents, launched 2026-08-19, UNSEEDED; grew to 6 + 1 resume):** D3×3 (close the declared
        exhaustive-tier shortfall — the round-1 D3 agent read 51 of 159 tier files and said so plainly —
        plus the unread stratified-sample targets), D4×1 (production-vs-test caller discrimination census
        at the individual-overload level — the census cut round 1 did not run), D6×1 (suppression-
        rationale trigger-condition verification over the complete suppression census). Withheld items
        count as covered only if independently surfaced from code. Halt lifts on supplemental return +
        closed miss analysis; then TASK-D.
      - **Orchestrator coverage rulings (recorded):** D2's census-first substitution (pattern-level
        branch census over 100% of files + region reads of filed hits) ACCEPTED as broader than the
        declared top-25 frame; its ~30 named census-identified-but-unread over-threshold regions ride
        to TASK-E as a coverage residual + S132 input (lizard-artifact re-run recommended). D7's
        declared shortfalls (KB prose-behavioural claims not systematically enumerated; ~230 src file
        headers unread) ACCEPTED as within the isolated slice's declared sample — recorded, not
        supplemented this sprint.
- [x] **TASK-D — refute panel. COMPLETE 2026-08-19.** Every finding at/above the Medium+ floor: real? severity honest per the
      rubric? already tracked (SEC/F/ROADMAP → cross-ref, not a row)? Calibration hits with doc-derived
      (not code-derived) evidence chains do not count. **Status 2026-08-19: dual-lens verification
      COMPLETE except one late batch.** Seven internal refute batches (R1 architecture/dead-code, R2a/b/c
      test-quality, R3 error/observability, R4 warnings/doc-drift, R5 complexity [late — the Orchestrator
      caught that D2's rows were initially unassigned]) re-verified every Medium+ candidate fresh against
      the pinned worktree. Panel outcomes: 6 rows REFUTED with disproofs, ~20 merged, severities corrected
      BOTH directions (2 demotions incl. a High→Medium tier-rubric call; 2 escalations incl. a
      Medium→High where the panel found a 6th diverged family member the sweep missed, on the statutory
      §21 stk.2 deadline guard), 2 evidence components refuted inside surviving rows, and **one sweep
      test-observation escalated to a PRODUCT defect: the daily-rest rule's midnight-crossing model
      (dual-lens CRITICAL-recommended; owner ratifies)**. **Codex external lens over the surviving
      High/Critical set: 19 CONFIRM / 2 ADJUST (held High) / 0 REFUTE; boundary opinions delivered
      (BQ-1 Critical; BQ-2 systemic families promote where they prevent regression detection).**
      Calibration evidence chains re-verified code-derived (QC-1 by R4 with an honest printed-number
      qualification; QC-2 by R4; QC-3 by R1). External dedupe (Orchestrator, registers in view): 2
      candidate rows retired to SEC-015/ROADMAP cross-refs; register-update actions queued (SEC-022
      half-fixed split [Orchestrator-verified at the baseline Orchestrator Program.cs], SEC-037
      reachability, SEC-004 reconcile, F2 census correction). **Consolidated index:
      `.claude/sweeps/S131-consolidated-findings.md`.** Panel verdict archives: `.claude/sweeps/S131-refute/`.
      **R5 (D2 complexity) returned same day: 15/15 survive at Medium (0 refuted; 9 factual corrections;
      the three declared lower-bound rows' unread tails read — bounds rose 15-25% with no band change,
      vindicating the sweep's conservatism; one High trigger disproved [the scope-loop "divergence" is
      factored-differently-equivalent via RoleScope.CoversOrg]). Operational headline: the export-handler
      family's missing under-lock REVERSED probe is Medium ONLY because two verified gates keep the path
      dormant — and CRITICAL-class the moment the §15 stk.1 go-live gate is configured → registered as a
      NAMED GO-LIVE PRECONDITION (owner ratifies the conditional).**
      **TASK-D COMPLETE. FINAL: 140 register rows — 1 Critical + 21 High + 118 Medium — every row
      dual-verified (sweep agent → adversarial panel; the High/Critical set additionally Codex-verified:
      19 CONFIRM / 2 ADJUST / 0 REFUTE).**
- [x] **TASK-E — grade + adjudicate. EXECUTED 2026-08-19 (owner rulings PENDING — the only open item).**
      Delivered:
      - **Register LIVE**: `docs/operations/quality-finding-register.md` — QUAL-001…140 + the pre-planned
        gate row QUAL-141 (check_docs freshness → hard failure for QUALITY.md, FAIL-006 class), each row
        plain-language + pointer-index, statuses carrying the dual-lens verification chain. Below-floor
        appendix (per-dimension counts + pointers) and the gate-proposal packet included.
      - **Adjudication record**: `docs/sprints/SPRINT-131-adjudication.md` (tracked) — the full verdict
        provenance per row, refuted-rows section, calibration disclosure (withheld items now named),
        external-dedupe outcomes, cross-register actions, method-revision proposals, coverage residuals,
        and the ⚖ owner adjudication packet (7 reserved decisions).
      - **QUALITY.md re-grounded**: anchor 111→131; the stale CLAUDE.md governance pointer fixed
        (→ WORKFLOW.md); a declared 16-domain canonical set graded S56→S131 with every grade citing QUAL
        rows (headlines: Rule Engine A++→C+, Payroll A→C+, with the honest framing that measurement got
        real, not that code got worse); the stale Pre-S39 warning ledger corrected against the measured
        137 (retained as historical record).
      - **Cross-register updates applied**: SEC-022 split (half fixed-S130-incidental, verified; half
        open), SEC-037 reachability, SEC-015 ledger census-exact 94 + consolidation subnote, SEC-012
        re-observation, SEC-004 ⚖ reconciliation question; F1 guard-quality note + F2 census correction;
        ROADMAP — KB-index facts, the §15 stk.1 NAMED GO-LIVE PRECONDITION (QUAL-133), the period-label
        backlog item, the S132 pointer + coverage follow-ups.
      - **Owed quick checks closed**: SkemaEndpoints:1490-1491 anchors CONFIRMED stale (below-floor);
        GoLiveDate CONFIRMED unconfigured anywhere (corroborates QUAL-133's gate; test-comment drift
        below-floor).

## S132 remediation proposal (owner adjudicates; S129→S130 precedent)

**Proposed shape: S132 = the Critical + the High set + the gate rulings; the Mediums batch into themed
follow-ups (S133+ candidates) rather than one mega-sprint.** Draft order (bounded first, dependencies
respected):
1. **QUAL-001 + QUAL-021 + QUAL-114 — the rest-period correctness bundle** (fix the midnight model on
   absolute instants + one shared crossing interpretation; add crossing + boundary-exact fixtures).
   QUAL-123 (48h reference-period semantics) routes to the domain-truth track (Phase-B class) — ⚖.
2. **QUAL-133 — land the missing REVERSED probe now** (small, mirrors both siblings + a test) rather
   than carrying a conditional-Critical to the go-live gate — ⚖ (alternative: leave gated, precondition
   already registered).
3. **QUAL-002 — unify the manifest encoding** (string/audit-of-record) + re-encode + narrow the catch +
   re-tighten the rebuild test.
4. **QUAL-006 + QUAL-007 — the two swallow fixes** (log + surface the idempotency-mark failure; status
   checks + failure propagation in the weekly pipeline). Small and high-value.
5. **QUAL-004 + QUAL-005 — the two diverged families** (fail-loud the YEAR_END recovery copy; one shared
   business-timezone helper with an injectable clock).
6. **The test-integrity block — QUAL-013/015/016/017/018/019/020/022**: convert the atomic-outbox family
   to the real-route pattern (phased; retire the harness convention), repair the payroll/rule vacuous
   tests, stub-and-assert the compliance fail-closed test, add the revoke deny matrix, pin the registry
   tuples, delete the duplicate test class and write the governance-rule tests.
7. **QUAL-003 + QUAL-009 — the audit-trail pair** (wire the manifest stamp or amend ADR-016; denial
   logging + the audit-row decision) — depends on the ⚖ Backend-only-audit design ruling.
8. **QUAL-008 — correlation enrichment** (largest observability item; scope option: a logging-scope
   middleware rather than 184 call-site edits).
9. **The doc fix pass — QUAL-010/093 (the S80 pair), QUAL-011, QUAL-012 + the D7 Medium set + the
   anchor-family re-points** (mechanical, one task, big trust payoff).
10. **The dead-code batch — QUAL-032/033/035/040/041/042/043/044/046 deletions; QUAL-036
    internal+InternalsVisibleTo; QUAL-038/039 document-or-delete rulings ⚖.**
11. **The gate packet — QUAL-141 + 069/072/073/074/096/121, each per owner ruling ⚖.**
Everything else (the remaining Mediums) stays register-tracked with dispositions, picked up opportunistically
or in themed batches — never silently.

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
