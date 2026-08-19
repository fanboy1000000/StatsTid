# REFINEMENT — S131: code-quality audit sweep (the "S129, but for quality") — REV 2

> **Rev 2 (post dual-lens cycle 1):** rev 1 drew BLOCKERs from BOTH lenses — chiefly that the calibration
> design didn't survive contact with the repo (S129's leak-prevention worked because its answer artifacts
> POSTDATED the baseline; quality-debt artifacts are years of tracked docs present at any SHA, and 5 of 6
> named candidates failed inspection). Rev 2 reworks calibration, adds a method-spec task, an 8th dimension,
> a 4th owner question (the severity floor), and precision fixes throughout. One lens disagreement was
> settled empirically: a rebuild at HEAD emits **exactly 137 warnings** (QUALITY.md's ~19-warning ledger is
> itself stale — an accidental proof of the audit's premise).

**What You Asked For**
An audit of code quality, analogous to the just-completed security audit (S129 sweep → S130 remediation).

**What You Actually Need**
A systematic, adversarially-verified quality sweep producing three durable things:
1. **A truthful `docs/QUALITY.md`.** It is the designated per-domain quality ledger (WORKFLOW.md:230-241),
   but it has **refrozen twice** despite an explicit re-establishment attempt: anchor sprint 111, matrix
   "Last updated: Sprint 64" with per-cell prose from ~S35, history table ending S35, narrative entries to
   S114 — and its governance header still points at a CLAUDE.md section that moved to WORKFLOW.md. The
   audit re-grounds it in evidence AND addresses the forcing function (see the pre-planned finding below).
2. **A QUAL-series findings register** (`docs/operations/quality-finding-register.md`) — modeled on the SEC
   register's shape (pointer-index rows, owner adjudication, revisit-not-shield). (Precision: the F/
   performance register is a sibling in spirit but lacks adjudication columns — QUAL copies SEC, not F.)
3. **A prioritized fix-next backlog** → an S132-candidate remediation sprint, as S129 fed S130.
Not needed: a new *governance* mechanism — but rev-1's "proven reuse, no new mechanism" was overstated:
quality lacks security's ready-made taxonomy (STRIDE/OWASP), so the sweep MUST first define its own method
spec (severity rubric, thresholds, sampling rules, dedupe) as a reviewed artifact — S129 did exactly this
with its vendored skill (its TASK-A).

**Proposed Approach**
- **TASK-A — method spec (the quality taxonomy), dual-lens-reviewed before any sweep.** A per-dimension
  method definition (fixed prompt templates or a vendored skill, as S129's TASK-A was): what each dimension
  measures, its severity rubric (proposal under OQ-4), evidence rules (e.g. dead-code claims require
  conservative evidence — reflection/DI/route-wiring defeats static inference), inter-agent dedupe keys, and
  a **per-dimension universe with an explicit exhaustive-vs-sampled declaration** (e.g. test-quality:
  exhaustive over the top-N load-bearing suites + a stratified sample elsewhere, the frame recorded in
  SPRINT-131.md). Partial coverage is acceptable; SILENT partial coverage is not (S129's zero-silent-gaps
  contract). Tool note: lizard's CI artifact covers only `src/` C# — frontend complexity is agent-read.
- **TASK-B — universe pin + calibration manifest (reworked; the rev-1 BLOCKER).**
  - Pin the sweep to post-S130 HEAD (`7e4bb1b`, all-green). Commit-pinned per-dimension inventory.
  - **Calibration items must be code/build-anchored ONLY** — discoverable from the worktree/build alone,
    exactly as S129's three were. Verified candidate #1: the **137 build warnings** (empirically exact at
    HEAD). TASK-B harvests more from sprint-log-recorded, still-live, code-visible debt — **each verified
    still-live at the baseline before the manifest is sealed**. (The verify rule is demonstrably
    load-bearing: cycle-2 checked rev-2's own two exemplar sources and BOTH are already dead — the S82
    `submitPeriod` twin was deleted at S127, the S118 WTM/child-entitlement dead-ends repaired at S121.
    Sprint-log-recalled debt has heavy attrition.) **Minimum manifest: ≥3 verified items (S129 parity) —
    keep harvesting until met** (next pool candidate: the Payroll CS0618 `[Obsolete]` opt-out, code-anchored,
    to be verified); if the pool genuinely runs dry below 3, that is an explicit owner-acknowledged fallback
    recorded in SPRINT-131.md, not a silent shrink.
  - **Scored-dimension view rule (doc-echo leak closure):** a code-anchored item harvested from a sprint log
    is still NAMED in that tracked log at the baseline — an agent with a full-tree view could "find" it by
    reading the ledger, a hit that measures doc-reading, not code analysis. Therefore every SCORED
    dimension's worktree view **excludes `docs/sprints/**` + the operations registers** (agents get code +
    current-truth docs, never the ledger), and all views are **baseline-pinned** (so the S131 planning
    artifacts, which postdate `7e4bb1b`, are auto-excluded S129-style). The refute panel additionally checks
    each calibration hit's evidence chain is code-derived.
  - Doc-anchored debt (frozen KB indexes, QUALITY.md staleness) is **excluded from calibration** — it is
    structurally either leaked (docs in view) or undiscoverable (docs excluded). The doc-drift dimension
    instead gets its own isolated slice: an explicitly-defined view (worktree minus `docs/sprints/`,
    registers, ROADMAP, QUALITY.md) and its own method — no withheld-item scoring for that dimension.
  - Items already registered elsewhere (SEC/F) are ineligible (rev-1 wrongly named RES-002, which is SEC-006).
- **TASK-C — the sweep: one agent per dimension.** Dimensions (now 8):
  1. **Architecture conformance** — bounded-context/dependency rules verified tree-wide.
  2. **Complexity hotspots** — over-threshold functions (CI lizard artifact for C#; agent-read for
     frontend), ranked by load-bearing-ness (ranking criteria defined in the TASK-A spec, not ad-hoc).
  3. **Test-suite quality** (OQ-2) — assertion strength: vacuous tests, can't-fail tests, missing
     negative/boundary cases, Docker-gating that masks untested local paths. Sampled per the TASK-A frame.
  4. **Duplication & dead code** — conservative-evidence rules per TASK-A.
  5. **Error-handling & API-contract consistency** — status-code vocabulary, swallowed exceptions,
     fail-open patterns, error-body consistency.
  6. **Warning debt** — triage the 137 into fix / suppress-with-reason / ratchet candidates.
  7. **Doc/code drift** — the isolated slice per TASK-B (its own view + method).
  8. **Observability/logging consistency** *(added, both lenses)* — structured-logging consistency,
     correlation-id discipline, actionable failure messages, log-noise (e.g. the known tier-probe WARNING
     noise), sensitive-data-in-logs discipline.
  Out of scope, recorded as explicit pointers in SPRINT-131.md: performance (F register owns it),
  dependency CVEs (SEC/Dependabot/CI own them), dependency staleness/abandonment (unowned — flagged as a
  future pass, not silently dropped).
- **TASK-D — adversarial refute panel** for every finding at/above the registration floor (OQ-4): real?
  severity honest per the rubric? already tracked (SEC/F/ROADMAP — cross-ref, don't re-register)?
- **TASK-E — grade + adjudicate.** Confirmed findings → QUAL-### rows; QUALITY.md re-graded per domain,
  each grade citing register evidence (a no-findings domain keeps/raises its grade explicitly, never
  silently). TASK-E also: defines the canonical domain set BEFORE grading (the matrix has 12 rows but later
  prose added domains), fixes the stale governance-header pointer, and files a **pre-planned finding**:
  "promote `check_docs.py`'s freshness warning to a hard failure for QUALITY.md" — the doc has refrozen
  twice because freshness findings never reach the exit code (the FAIL-006 class). Proposals-only; you rule.
  Output: the S132 remediation proposal with your per-finding rulings.
- **Read-only audit** — restated as an allowlist (internal-lens WARNING): at sprint close,
  `git diff 7e4bb1b..HEAD` touches ONLY `docs/**` + `.claude/**`. `frontend/`, `tests/`, `tools/`,
  `docker/` are all product surface (frontend is a sibling of `src/`, so rev-1's "src/ diff empty" was too
  narrow). A mid-audit CI-health exception (S129 precedent: SEC-026) requires an explicit owner ruling in
  the sprint doc. Only the Orchestrator writes the register/QUALITY.md/sprint log — dimension agents and
  refuters return findings (CLAUDE.md: docs are Orchestrator-only).

**Open Questions** (decisions only you can make)
1. **Breadth: full codebase in one sweep (RECOMMENDED), or worst-domains-first slices?** Full sweep = all
   domains, one coherent evidence base, QUALITY.md fully re-grounded. Slice = cheaper but leaves the ledger
   partially stale, and cross-cutting dimensions don't slice. *Rev-2 correction that strengthens (a): rev-1
   justified slicing by "Frontend has been the C+/B laggard for 20+ sprints" — actually Frontend rose to A−
   at S82. My own grade intuition was stale, which is precisely why sweeping everything beats slicing by
   remembered grades.*
2. **Is test-suite quality in scope (RECOMMENDED yes, with the TASK-A sampling frame)?** Most expensive
   dimension; S130's evidence says it's the highest-value lens. The sampling frame (not exhaustive reads of
   all ~3,269 tests) keeps the cost honest and declared.
3. **Gate promotion: proposals-only (RECOMMENDED).** The audit files gate-flip findings (warning ratchet,
   complexity ceiling, coverage floor, the QUALITY.md freshness hard-fail) and you rule on each; the audit
   itself changes no CI behavior.
4. **Registration severity floor (NEW — the biggest noise/cost dial, both lenses).** Proposal: register a
   finding only at **Medium or above — "would plausibly change behavior, block or mislead a future change,
   or misinform a reader"**; below-floor items go to a single inventory appendix as counts + a pointer (not
   rows, not refuted, not adjudicated). Your call to ratify or move the bar.

**Assumptions** (correct me if wrong)
1. The S129 harness *structure* transfers (pinned universe → calibration → refute → register → rulings →
   remediation proposal), PROVIDED TASK-A supplies the quality taxonomy the security lens got for free.
2. QUAL register modeled on the SEC register; QUALITY.md remains the graded summary citing register rows.
3. Environment: no Python/no Docker locally → lizard + coverage numbers come from CI artifacts (their
   availability is an explicit TASK-B prerequisite, verified before the sweep), Docker-gated execution
   stays CI-only; Codex is on PATH for all dual-lens gates (verified).
4. This is S131, pinned to `7e4bb1b`; registered as a new workstream in
   `docs/operations/docs-governance-program.md` (whose stale "NEXT: WS5" status line gets fixed in passing).
5. Dedupe: findings already in SEC/F/ROADMAP get cross-refs, not QUAL rows.

**Acceptance Criteria**
- [ ] TASK-A method spec exists and passed its own dual-lens review BEFORE any sweep agent ran (severity
      rubric, per-dimension universe + exhaustive-vs-sampled declaration, evidence + dedupe rules).
- [ ] Calibration: every manifest item is code/build-anchored and verified still-live at `7e4bb1b` before
      sealing; **manifest ≥3 verified items or an owner-acknowledged smaller-manifest fallback recorded**;
      scored-dimension views exclude `docs/sprints/**` + the operations registers and are baseline-pinned
      (doc-echo closure); the withheld list + leak-prevention design documented in SPRINT-131.md; a miss
      halts findings acceptance until analyzed, and a hit whose evidence chain is doc-derived (not
      code-derived) does not count (refute-panel checked).
- [ ] Every confirmed finding **at/above the OQ-4 floor** is a QUAL-### row with file:line evidence,
      severity per the rubric, refute outcome, and an adjudication cell; below-floor inventory is an
      appendix with counts (rev-1's "every confirmed finding" contradicted its own floor policy — fixed).
- [ ] QUALITY.md re-graded over a declared canonical domain set, every grade citing evidence; the stale
      header pointer fixed; the freshness-gate promotion filed as a pre-planned finding (FAIL-006 cited).
- [ ] Read-only allowlist holds: `git diff 7e4bb1b..HEAD` at sprint close touches only `docs/**` +
      `.claude/**`; any exception carries an owner ruling in the sprint doc.
- [ ] Dedupe holds (cross-refs, no duplicate rows); the out-of-scope pointers (performance, dependency
      staleness) are recorded, not silent.
- [ ] Only the Orchestrator wrote docs; agents returned findings.

**Risks & Conflicts**
- **Finding flood** — mitigated by the OQ-4 floor + rubric + refute panel + dedupe. The floor is the dial.
- **Judgment-call severity drift between agents** — mitigated by the TASK-A rubric + refute panel checking
  severity honesty against it (this replaces what OWASP/STRIDE gave S129 for free).
- **Calibration failure modes** — dead candidates (mitigated: verify-at-baseline before sealing), leaked
  answers (mitigated: code/build-anchored only + the doc-drift slice isolation).
- **Coverage overclaim** — mitigated by declared exhaustive-vs-sampled frames per dimension.
- **Scope creep into remediation** — read-only allowlist; every fix impulse becomes a row.
- **Cost** — a full sprint, comparable to S129; the OQ-4 floor and OQ-2 sampling bound it tighter than
  breadth alone.
- No invariant trade-off: this exercises the enforcement layer; nothing is traded.

**Readiness: READY (rev 2.1 — dual-lens cycle-2 CLEARED)** — Codex cycle-2: "Cleared — cycle-1 findings
resolved, no new blockers" (accepted the 137-warnings candidate, conceding its cycle-1 dispute after the
empirical rebuild). Internal cycle-2: 0 BLOCKER / 2 WARNING / 3 NOTE — cycle-1 fully resolved; the two new
WARNINGs (minimum-manifest feasibility after both exemplar sources proved dead; the doc-echo leak path for
scored dimensions) are ABSORBED above (≥3-item manifest rule + owner fallback; scored-view exclusion of
`docs/sprints/**` + registers, baseline-pinned views, code-derived-evidence check). NOTEs absorbed as TASK-A/B
drafting hygiene: dimension-7's excluded path set enumerated in TASK-A; method specs must not seed agents
with already-registered items (the tier-probe example is SEC-012 — illustration only, dedupe applies).
**Pending only: owner rulings on OQ-1..4.**

**OWNER-RULED (2026-08-19): all four as recommended.**
- **OQ-1 = full sweep** (all domains, one evidence base; QUALITY.md fully re-grounded).
- **OQ-2 = test-suite quality IN scope, sampled** (exhaustive on the top load-bearing suites + a declared
  stratified sample; frame recorded in SPRINT-131.md).
- **OQ-3 = gate promotions are PROPOSALS-ONLY** (each a QUAL finding; owner rules per gate at adjudication;
  the audit changes no CI behavior).
- **OQ-4 = registration floor at Medium+** ("would plausibly change behavior, block/mislead a future change,
  or misinform a reader"); below-floor → one inventory appendix (counts + pointers, not adjudicated).

## Review Findings (Step 4)

*Cycle 1 — External (Codex): 3 BLOCKER / 5 WARNING / 2 NOTE. Internal (Reviewer): 1 BLOCKER / 4 WARNING /
4 NOTE. Convergent on the core defect; one factual disagreement settled empirically.*

- **BLOCKER (both, absorbed) — calibration design unsound.** S129's leak-prevention derived from its answer
  artifacts postdating the baseline; quality-debt artifacts are tracked at every SHA. 5 of 6 rev-1
  candidates failed: frontend TS errors fixed at S47 (dead), RES-002 is SEC-006 (lens-mismatch + dedupe
  violation), SkemaPage flake is CI-history-only (undiscoverable from the tree), QUALITY.md staleness +
  frozen KB indexes are doc-anchored (leaked-or-undiscoverable). → code/build-anchored calibration only,
  verified live at baseline; doc-drift gets an isolated slice with its own view/method.
- **BLOCKER (Codex, absorbed) — AC/floor contradiction.** "Every confirmed finding is a row" vs the
  severity-floor appendix. → AC now floor-qualified.
- **BLOCKER (Codex, partially incorrect — resolved empirically):** "137 warnings is wrong, the ledger says
  ~19." The internal lens rebuilt at HEAD: **exactly 137**. Codex was citing QUALITY.md's stale warning
  ledger — itself drift. The valid kernel (verify candidates freshly, don't assert from memory) is absorbed
  as the verify-at-baseline rule; the 137 item stands as calibration candidate #1.
- **WARNING (both, absorbed) — quality lacks a ready taxonomy.** → TASK-A method spec (rubric, thresholds,
  sampling, dedupe), dual-lens-reviewed like S129's vendored skill; per-dimension universes with declared
  exhaustive-vs-sampled coverage; lizard-covers-only-src/-C# noted.
- **WARNING (both, absorbed) — add observability/logging dimension** (dimension 8); dependency CVEs stay
  with SEC, staleness recorded as an explicit unowned out-of-scope pointer; dead-code conservative-evidence
  rule into TASK-A.
- **WARNING (internal, absorbed) — read-only AC too narrow** (frontend/tests/tools are product surface
  outside `src/`). → allowlist AC over the whole tree + owner-ruled exception path.
- **WARNING (internal, absorbed) — the refreeze forcing function.** QUALITY.md refroze twice because
  check_docs freshness warnings never reach the exit code (FAIL-006 class). → pre-planned gate-promotion
  finding in TASK-E (proposals-only).
- **WARNING (internal→OQ, absorbed) — severity floor is an owner decision.** → OQ-4 with a concrete
  Medium-floor proposal.
- **NOTEs (absorbed):** QUALITY.md staleness described precisely (narratives to S114; matrix S64/S35;
  header pointer drift → fixed in TASK-E); canonical domain set defined before grading; OQ-1(b)'s "Frontend
  laggard" rationale was stale (rose to A− at S82) — strengthening the full-sweep lean; workstream
  registered in the docs-governance program (+ its stale status line fixed); Orchestrator-only docs writes
  made an explicit AC; CI-artifact availability an explicit prerequisite; F-register shape precision.
