# Sprint 67 — Vacation settlement architecture (ADR-033 design sprint)

| Field | Value |
|-------|-------|
| **Sprint** | 67 |
| **Status** | in-progress (ADR-033 ACCEPTED 2026-06-07; remaining design-sprint close items teed up — see Task Log) |
| **Start Date** | 2026-06-07 |
| **End Date** | — |
| **Orchestrator Approved** | no |
| **Build Verified** | n/a — design sprint (no execution code; pure value-objects/contract-tests optional per refinement OQ-2a) |
| **Test Verified** | n/a — design sprint (`sprint-test-validation` SKIP per S38 precedent; the dual-lens ADR review is the formal gate) |

## Sprint Goal

**DESIGN sprint** producing **ADR-033 (vacation-settlement architecture) + a phased implementation roadmap** for the deferred ADR-030 D7 §-settlement work (transfer-by-agreement · automatic post-period payout · feriehindring auto-transfer · termination set-off · særlige-feriedage godtgørelse), spanning the Backend (balance/events) ↔ Payroll (export-line) boundary. The period-end *execution* layer behind the S66 disposition row. Owner-ratified (2026-06-07): design-first; **money stays out of StatsTid** (day/hour or percentage/rate wage-type lines; SLS owns kroner); the disputed Ferielov statutory numbering is settled by the Step-0 adversarial deep-research. Refinement: `.claude/refinements/REFINEMENT-s67-vacation-settlement.md` (READY; Step-4 2 cycles ×2 lenses, all design findings absorbed). NO launch-blocking item is created — each implementation slice is marked launch-neutral/blocking in the roadmap.

## Entropy Scan Findings

| Check | Result | Detail |
|-------|--------|--------|
| KB path validation | CLEAN | `tools/check_docs.py` all hard checks passed (db-schema in sync; KB INDEX 48 entries, 0 orphans, 0 dangling; sprint inventory through S66; freshness anchored S66) |
| Pattern compliance spot-check | CLEAN | No `FindFirst("scopes")` (FAIL-001) |
| Orphan detection | CLEAN | The S66 `expiring`/disposition surface fully wired (BalanceEndpoints + useYearOverview + ArsoversigtPage + tests + ADR-030 D9 + INDEX) |
| Documentation drift | CLEAN | MEMORY.md current (S66 closed+pushed; the S66 follow-ups + the D9-amendment recorded) |
| Quality grade review | CLEAN | S66 grades current per QUALITY.md anchor |

## Step-0 Inputs (pre-design)

- **Refinement** — Step 4 complete (2 cycles × 2 lenses; Codex 3B/4W/2N + Reviewer 0B/2W/7N cycle-1, Reviewer 0B/0W/3N + Codex 3B/4W cycle-2; all design findings absorbed; the statutory-numbering BLOCKERs escalated to the Step-0 research per owner ratification). Owner-ratified leans: design-first · money-free (lines only) · numbering-via-research.
- **Step-0 deep-research (IN FLIGHT, HIGH priority — the gating task):** settle the exact Ferielov/cirkulære statutory numbering against primary text (the §21/§26/§12-stk.2 [project's verified S65 research] vs §22/§24/§26/§15-stk.2/§17 [Codex cycle-2] dispute) + re-audit the shipped docs (ADR-030 D9, `ferie-transfer-timing-research.md`, `danish-agreements.md`, the S66 labels) for a latent numbering error; the §-settlement execution mechanics (transfer deadline; auto-payout timing/recipient; feriehindring; termination set-off + cap; the 2½% godtgørelse basis/§/timing); state-sector divergence; and (best-effort) the SLS wage-type input contract per mechanism (the money-boundary gate). Adversarial — refute BOTH numbering analyses. → a reference doc `docs/references/vacation-settlement-law-research.md`.
- **Fact-finding census (done at refinement, code-verified):** no salary/base-rate in-system (`Amount = Hours × multiplier`); `carryover_in` has no non-zero production writer; no `employment_end_date`; no period-close trigger; the S66 disposition row is display-only; `ApplyRevaluationAsync`/`EntitlementBalanceRevalued` is the evented-mutation template; `DelegationExpiryService` the period-close BackgroundService shape; `approval_periods` a weak §21-host; the ADR-026 audit-mapper family; the `PayrollExportLine`/SLS-formatter line shape (new code + new period-close emitter).

## Plan Review (Step 0b)

| Field | Value |
|-------|-------|
| **Trigger** | MANDATORY (new architectural domain P1 + payroll P6 + events P3) — run on ADR-033 (the design artifact) |
| **External Codex** | invoked 2026-06-07 — 3 cycles: 5B/5W/1N → 3B/1W → 4B |
| **Internal Reviewer** | invoked 2026-06-07 — 3 cycles: 0B/3W/6N → 0B/2W/1N → 0B/1W/1N ("smoke-alarm CLEAR") |
| **BLOCKERs resolved before accept** | yes — ALL absorbed across 3 cycles; owner-accepted at the post-cycle-3 halt-and-prompt |

Cycle-1 caught real design errors (the §24-as-carryover legal error; the §21/§22 carryover race; undesigned Payroll exactly-once; the over-generalized SLS day-count; the §34-auto-forfeiture legal violation) → the one-atomic-partition + fail-closed restructure. Cycle-2 → the settlement state machine (PENDING_REVIEW/SETTLED/REVERSED + sequence + trigger). Cycle-3 → the state-machine boundary conditions (composite key, reversal's own sequence, termination-into-settled-year symmetry, the manual-completion CAS guard); lenses converged on the findings, split on severity (Reviewer convergent-complete / Codex 4B-but-one-line); owner accepted. Full trail in ADR-033 § Review Findings.

## Architectural Constraints Verified

_(design sprint — verified at ADR-033 acceptance + at each implementation slice)_

- [ ] P1 — Architectural integrity (the Backend↔Payroll settlement boundary; ADR-033 the vehicle)
- [ ] P2 — Deterministic settlement quantities (pure of boundary date + recorded feriedage + dated config; inherits ADR-032 D2)
- [ ] P3 — Event sourcing/auditability (settlement event vocabulary + ADR-026 mappers)
- [ ] P4 — OK-version correctness at the boundary (entry-date-stamped recorded feriedage; boundary-date timezone is the new surface)
- [ ] P5 — Integration isolation (SLS owns kroner; StatsTid emits lines)
- [ ] P6 — Payroll correctness (the settlement wage-type lines + the SLS input contract)
- [ ] P7 — Security/access control
- [ ] P8 — CI/CD
- [ ] P9 — UX (the §21-agreement surface, if any)

## Task Log

### TASK-6700 — Step-0 deep-research (statutory numbering + settlement mechanics)

| Field | Value |
|-------|-------|
| **ID** | TASK-6700 |
| **Status** | COMPLETE 2026-06-07 — deep-research + refute-lens (**9/9 VERIFIED, 0 refuted**) → `docs/references/vacation-settlement-law-research.md`; the shipped-doc §-citation corrections APPLIED (build 0E, doc gate green) |
| **Agent** | Orchestrator-dispatched research (opus, adversarial) + 1 refute-lens |
| **Components** | docs/references/vacation-settlement-law-research.md (new) |
| **KB Refs** | ADR-030 D7/D9, the ferie-transfer-timing + vacation-consumption research docs |

**Description**: Settle the Ferielov/cirkulære numbering dispute against primary sources + re-audit the shipped docs. **VERDICT (deep-research, primary-source verbatim from the extracted cirkulære PDF; Ferielov mirror-sourced + cross-validated):** transfer-by-agreement = **Ferielov §21 stk.2** (the project's S65 research + ADR-030 D9 boundaryMonth=12 **STAND**); auto post-period payout >4wk = **§24**; feriehindring = **§22**/**§25**; termination payout = **§26**; forskud modregning = **§7 stk.1** (capped at final pay); first-4-wk forfeiture = **§34** (Feriefonden); state særlige-feriedage 2½% godtgørelse = **Cirkulære 021-24 §15 stk.2 (payout) + §17 (calc)** — **NOT §12 stk.2** (that's the taking window). Current law = **LBK 152/2024** + cirk. 021-24 (defers per §3). **SLS input = day-count quantity (løndele 5017/5027/5037); SLS owns the 2½% rate → confirms OQ-1a (money stays out of StatsTid).** **Shipped-doc fix found:** the "§12 stk.2 = godtgørelse" citation (propagated S65→S66→today's D9 amendment + danish-agreements + INDEX) is WRONG → §15 stk.2/§17 (applied post-verification). Codex cycle-2's numbering challenge was substantially right; the project's §21/boundaryMonth ruling was right.

---

### TASK-6701 — ADR-033 authoring (the settlement architecture)

| Field | Value |
|-------|-------|
| **ID** | TASK-6701 |
| **Status** | COMPLETE — ADR-033 ACCEPTED 2026-06-07 (Step-0b 3 cycles, all findings absorbed) |
| **Agent** | Orchestrator |
| **Components** | docs/knowledge-base/decisions/ADR-033 (new); KB INDEX row |
| **KB Refs** | ADR-030 D7/D9, ADR-032 D2, ADR-018 D3/D6/D8/D13, ADR-026, ADR-013, ADR-021 D4, ADR-025 D3/D6 |

**Description**: D1–D13 settling the money-free boundary, the verified §-spine, the deterministic period-close, the Backend↔Payroll exactly-once ownership, the settlement state machine, the provenance-keyed carryover writer, the wage-type-line scheme, the §21-agreement record, termination awareness, the fail-closed forfeiture/feriehindring, the särlige model-correction timing, the GLOBAL fence, and the phased roadmap. Owner-accepted at the post-cycle-3 halt-and-prompt.

---

### TASK-6702 — Design-sprint close-out (REMAINING — next session)

| Field | Value |
|-------|-------|
| **ID** | TASK-6702 |
| **Status** | planned (teed up — owner stopped for the day after accepting ADR-033) |
| **Agent** | Orchestrator |

**Description (the remaining S67 design-sprint items):** (a) a **SYSTEM_TARGET settlement-execution section** (the target extension — §K/§M scope the rules, this adds the execution layer); (b) `danish-agreements.md` settlement wage-type rows (the day-count SLS løndele); (c) the **ROADMAP phased roadmap** entry (slices 1–4 with launch-relevance marks; §21-carryover-write as the launch-neutral first infrastructure unit); (d) cross-annotate ADR-030 D7 → ADR-033; (e) ADR-031 D6-style "no launch-blocker created" note; (f) the **design-sprint close** (`sprint-test-validation` SKIP per S38; the Step-7a-equivalent dual-lens on the final ADR+plan IS the gate + the close-guard artifacts; commit + push). NOTE: ADR-033 + today's edits are committed locally; not yet pushed (owner stopped for the day).

## External Review (Step 7a)

_pending — design-sprint Step-7a-equivalent dual-lens on ADR-033 (the formal gate; S38 precedent)._

## Test Summary

_n/a (design sprint) — baseline carried from S66: 631 unit + 466 regression + 5 smoke + 176 FE = 1278._

## Sprint Retrospective

_pending_
