# Docs & Governance Cleanup Program

**Status**: PHASE 1 IN PROGRESS (captured 2026-08-12; owner gave go for Phase 1) · **Owner**: Orchestrator + PM
**Why this exists**: a mid-session replan (per WORKFLOW.md Replanning Protocol). Several governance
and documentation threads opened while closing S128 and scoping the security sweep; this doc is the
**single source of truth** for all of them so nothing is lost across sessions. It supersedes the
scattered state (S128 Open follow-ups, the S129 security refinement, and in-conversation decisions).

**Plain-language goal**: get the docs clean and internally consistent *before* we lean harder on
AI-only development — because for this project the docs ARE the shared memory the agents run on, so
a stale doc is an agent acting on wrong information.

---

## Decisions already made (record, so they are not re-litigated)

- **D1 — Project framing.** StatsTid is a learning project in active development, not deployed; the
  production-grade Danish state SaaS is the design TARGET. (Landed in CLAUDE.md + `docs/CONVENTIONS.md`.)
- **D2 — Explanation standard.** Decisions/information must be explained so a product manager can
  understand AND learn from them. (In `docs/CONVENTIONS.md`, injected into every agent prompt.)
- **D3 — Priority model → INVARIANTS, not a ranking.** The old 1–9 "lower never compromises higher"
  order is replaced by: a co-equal **set of inviolable invariants** (a path that compromises any is
  invalid → find another path; genuine unresolvable conflict escalates to the owner), plus a short
  **ranked trade-off tier** for the things actually balanced (usability/UX, then shipping cadence),
  with **CI/CD named as the enforcement layer**, not a priority. Rationale: 7 of the 9 old items were things we
  would never trade — ranking non-negotiables against each other was meaningless, which is why the
  order never had a written rationale. **Full rename** chosen (named invariants, drop ordinals) so
  the docs read clean for AI-only development — accepting the doc-wide `P#` migration cost.
- **D4 — Sequence.** Governance model → thorough docs review (with the `P#` migration folded in) →
  security sweep. The security sweep reads the docs as input, so it runs against clean docs; it
  renumbers to the sprint after the docs sprint.
- **D5 — Docs-review scope.** "Live-truth" docs only (everything agents/Orchestrator treat as
  current truth). The 128 historical sprint logs get an index/freshness check only, not a
  line-by-line re-read.

---

## Workstreams

### WS1 — Governance model finalization (CLAUDE.md + CONVENTIONS.md) — PHASE 1
- [x] SYSTEM ROLE reframed to "target"; blocks moved to `docs/CONVENTIONS.md`; step-5 injection
      mandate; doc-map row. *(done, uncommitted)*
- [x] **Invariant-model rewrite** (D3): canonical model in `docs/CONVENTIONS.md` (invariant set +
      find-another-path + escalation + ranked trade-offs + CI/CD as enforcement); CLAUDE.md carries
      a compact named summary + pointer. *(done, uncommitted — the model lives in CONVENTIONS.md so
      it reaches agents; Step-7a Codex catch)*
- [x] Doc-map cleanup: Operations table split (durable vs historical/research); `FAIL-001` pin
      removed; "Maintaining this file" rule added; Agent-Architecture / How-to-Use overlap de-duped.
      *(done, uncommitted)*
- [x] Dual-lens review (architectural → mandatory): cycle 1 internal BLOCKED / Codex
      APPROVED-WITH-WARNINGS (the "priority order" term in CONVENTIONS.md + the model not reaching
      agents + stale checkboxes) → absorbed → cycle 2 BOTH APPROVED-WITH-WARNINGS, 0 blockers
      (residual: enumeration lists + a "delivery"/"shipping cadence" term slip) → absorbed. Converged.
- [x] Commit + push the governance change. *(this commit)*

### WS2 — `P#` rename migration (doc-wide) — FOLDED INTO WS3
Depends on WS1's rename landing. Every `P7` / `priority #7` / `P2` reference across WORKFLOW.md,
AGENTS.md, QUALITY.md, the KB, sprint logs, and review-prompt templates → named invariants.
**Executed as part of the WS3 per-doc pass** (one touch per file), not as a separate sweep.

### WS3 — Thorough docs review — PHASE 2 (the big one)
Method (makes "thorough" verifiable):
- [ ] **Inventory**: enumerate every live-truth doc; classify (canon / KB / references / operations
      / generated). One row per doc.
- [ ] **Rubric**: define "clean" = accurate vs current code; internally consistent; no stale
      citations (the recurring line-drift problem); freshness anchors current; jargon glossed per
      the explanation standard (D2); uses the invariant vocabulary (D3), not `P#`.
- [ ] **Per-doc audit** (fan-out by cluster): each doc → reviewed, findings logged, `P#` migrated.
- [ ] **Findings register + fixes**; every inventory row ends marked clean or fixed.
- Exit: every row in the inventory checked. Sprint logs: index/freshness check only (D5).

### WS4 — S128 follow-ups (recorded in SPRINT-128.md; not at risk of loss)
- [ ] FU-A — tier-probe spurious "Access denied" log noise (a non-logging classification path).
- [ ] FU-B — RES-002 9-read remainder (7 lack month params; also feeds WS5).
- [ ] FU-C — TASK-12802 loader-evidence rerun (needs a docker-capable machine OR the native
      rule-engine; see WS6).
- [ ] FU-D — SkemaPage 7203-pin vitest flake watch (graduates to a finding only on recurrence).
- [ ] FU-E — environment facts (recorded; actionable bit = native rule-engine for full UI testing).

### WS5 — Security threat-model sweep — PHASE 3 (was "S129"; renumbers after the docs sprint)
- [ ] Finalize refinement rev 2 + cycle-2 dual-lens verification (`.claude/refinements/REFINEMENT-s129-security-sweep.md`).
- [ ] Vendor the skill (`security.md` + `security-checklist.md` only; no hooks; no `--fix`; invoke-by-name).
- [ ] Build the SEC register with the corrected 12-read census + revisit rows ("known — should be
      revisited": prior rulings are re-attacked, not shielded).
- [ ] Run the sweep (static-analysis only; no live probing of the local stack) → adversarial
      verification → owner adjudication → remediation-sprint proposal.

### WS6 — Environment / infra (parked / on demand)
- [ ] Native stack back up (backend-api + Vite) when UI testing is wanted (Postgres already up).
- [ ] Native rule-engine on :5200 + finish the demo load (also produces FU-C's evidence).
- [ ] Docker on the VDI = external IT ticket (nested virtualization) — owner action, may be declined.

### Cross-cutting — git hygiene
Governance edits are currently uncommitted on top of the S128 close (`8c182e9`). Commit boundary:
WS1 lands as one reviewed governance commit; WS3 as its own; keep `origin/master` current.

---

## Phase gate

The owner confirmed this capture and gave the go for Phase 1 (2026-08-12); WS1 is in progress.
Each substantive workstream passes through the `refine-requirements` gate when picked up (WS1's
refinement was the in-conversation invariant-model design). This doc is updated as items close and
reviewed at each entropy scan.
