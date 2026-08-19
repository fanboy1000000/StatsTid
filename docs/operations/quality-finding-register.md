<!-- anchor-sprint: 131 -->
# QUAL — Code-Quality Finding Register

**Status**: SCAFFOLD — the S131 sweep has not yet run; rows land as TASK-C/D confirm findings.
**Owner**: Orchestrator + PM. **Sweep baseline SHA**: `7e4bb1b` (S130 close). **Registration floor
(owner-ruled, 2026-08-19)**: **Medium+** — a finding that would plausibly change behavior, block or mislead
a future change, or misinform a reader. Below-floor items live ONLY in the inventory appendix (counts +
pointers; not refuted, not adjudicated).

**Semantics** (mirrors the SEC register): rows are a pointer-index — the durable "what + where + status,"
with deep evidence in `SPRINT-131.md`. **Revisit, not shield**: a prior ruling ("accepted", "deferred") is
re-attackable at any later audit, never auto-excluded as settled. **Dedupe rule**: anything already tracked
in the SEC register, the F (performance) register, or the ROADMAP backlog gets a cross-reference here at
most — never a duplicate QUAL row.

**Method** (S131): commit-pinned universe → code/build-anchored withheld calibration (manifest sealed only
after verify-still-live-at-baseline; scored-dimension views exclude `docs/sprints/**` + the operations
registers, baseline-pinned) → one agent per dimension (8 dimensions, per the vendored `quality-audit`
method spec) → adversarial refute panel → owner adjudication → remediation proposal (S132 candidate).

## Findings

| QUAL | What it means (plain language) | Title | Dimension | Sev | Status | Source of truth | Adjudication |
|------|--------------------------------|-------|-----------|-----|--------|-----------------|--------------|
| *(rows land as the S131 sweep confirms findings)* | | | | | | | |

## Below-floor inventory (appendix)

*(counts + pointers per dimension; populated at TASK-E. Not adjudicated — visible so "below the floor"
never means "invisible".)*

## Gate-promotion proposals (owner rules each; the audit changes no CI behavior)

*(filed as QUAL rows during TASK-E — pre-planned: promote `check_docs.py`'s freshness warning to a hard
failure for QUALITY.md, citing the FAIL-006 warnings-never-reach-the-exit-code class.)*
