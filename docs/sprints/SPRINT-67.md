# Sprint 67 — Vacation settlement architecture (ADR-033 design sprint)

| Field | Value |
|-------|-------|
| **Sprint** | 67 |
| **Status** | complete (ADR-033 ACCEPTED 2026-06-07; design-sprint close-out TASK-6702 done 2026-06-08; the Step-7a-equivalent dual-lens gate is the formal acceptance) |
| **Start Date** | 2026-06-07 |
| **End Date** | 2026-06-08 |
| **Orchestrator Approved** | yes — 2026-06-08 (design-only; ADR-033 D1–D13 accepted at the post-cycle-3 halt-and-prompt; close-out propagation + dual-lens Step-7a-equivalent on the final ADR+plan) |
| **Build Verified** | n/a — design sprint (no execution code; pure value-objects/contract-tests optional per refinement OQ-2a) |
| **Test Verified** | n/a — design sprint (`sprint-test-validation` SKIP per S38 precedent; the dual-lens ADR review is the formal gate) |

## Sprint Goal

**DESIGN sprint** producing **ADR-033 (vacation-settlement architecture) + a phased implementation roadmap** for the deferred ADR-030 D7 §-settlement work (transfer-by-agreement · automatic post-period payout · feriehindring auto-transfer · termination set-off · særlige-feriedage godtgørelse), spanning the Backend (balance/events) ↔ Payroll (export-line) boundary. The period-end *execution* layer behind the S66 disposition row. Owner-ratified (2026-06-07): design-first; **money stays out of StatsTid** (day/hour-count wage-type lines; SLS owns all kroner incl. the 2½%/2,02% rates — the early "%/rate line" framing was closed by the research → ADR-033 D1); the disputed Ferielov statutory numbering is settled by the Step-0 adversarial deep-research. Refinement: `.claude/refinements/REFINEMENT-s67-vacation-settlement.md` (READY; Step-4 2 cycles ×2 lenses, all design findings absorbed). NO launch-blocking item is created — each implementation slice is marked launch-neutral/blocking in the roadmap.

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

- [x] P1 — Architectural integrity **(designed)** — the Backend↔Payroll settlement boundary settled in ADR-033 D4 (Backend closes+events / Payroll consumes via a new emitter); D3 deterministic period-close
- [x] P2 — Deterministic settlement quantities **(designed)** — D3 immutable input snapshot (pure of the boundary date + recorded feriedage + dated config; inherits ADR-032 D2); the boundary-date timezone is the only new P2/P4 surface → folded into follow-up (v)
- [x] P3 — Event sourcing/auditability **(designed)** — D5 settlement event vocabulary (§21/§22/§25/§24/§34/godtgørelse/termination/reversal/manual-review) + ADR-026 mappers + catalog rows
- [x] P4 — OK-version correctness at the boundary **(designed)** — entry-date-stamped recorded feriedage (ADR-032 D2); the boundary-date timezone surface named (follow-up (v))
- [x] P5 — Integration isolation **(designed)** — D1 money stays out of StatsTid; SLS owns all kroner; StatsTid emits day/hour-count lines only
- [x] P6 — Payroll correctness **(designed)** — D7 settlement wage-type lines via a new period-close emitter; D1 per-slice SLS-contract Step-0 gates (only the særlige-godtgørelse day-count is SLS-verified today)
- [x] P7 — Security/access control **(designed)** — D9 termination `employment_end_date` audited event + GDPR + auth; D10 manual-review CAS winner-guard; D12 GLOBAL multitenant fence
- [x] P8 — CI/CD — n/a (design-only sprint; no code/CI surface). Doc-consistency gate (`tools/check_docs.py`) green; master CI green on the last pushed commit
- [x] P9 — UX **(designed)** — D8 §21 transfer-agreement is HR-recorded pre-launch (no employee+manager signing UI; the law's default is auto-payout §24, so the agreement is the captured exception)

## Task Log

### TASK-6700 — Step-0 deep-research (statutory numbering + settlement mechanics)

| Field | Value |
|-------|-------|
| **ID** | TASK-6700 |
| **Status** | COMPLETE 2026-06-07 — deep-research + refute-lens (**9/9 VERIFIED, 0 refuted**) → `docs/references/vacation-settlement-law-research.md`; the shipped-doc §-citation corrections APPLIED (build 0E, doc gate green) |
| **Agent** | Orchestrator-dispatched research (opus, adversarial) + 1 refute-lens |
| **Components** | docs/references/vacation-settlement-law-research.md (new) |
| **KB Refs** | ADR-030 D7/D9, the ferie-transfer-timing + vacation-consumption research docs |

**Description**: Settle the Ferielov/cirkulære numbering dispute against primary sources + re-audit the shipped docs. **VERDICT (deep-research, primary-source verbatim from the extracted cirkulære PDF; Ferielov mirror-sourced + cross-validated):** transfer-by-agreement = **Ferielov §21 stk.2** (the project's S65 research + ADR-030 D9 boundaryMonth=12 **STAND**); auto post-period payout >4wk = **§24**; feriehindring = **§22**/**§25**; termination payout = **§26**; forskud modregning = **§7 stk.1** (capped at final pay); first-4-wk forfeiture = **§34** (Feriefonden); state særlige-feriedage 2½% godtgørelse = **Cirkulære 021-24 §15 stk.2 (payout) + §17 (calc)** — **NOT §12 stk.2** (that's the taking window). Current law = **LBK 152/2024** + cirk. 021-24 (defers per §3). **SLS input = day-count quantity (løndele 5017/5027/5037); SLS owns the 2½% rate → confirms OQ-1a (money stays out of StatsTid).** **Shipped-doc fix found:** the "§12 stk.2 = godtgørelse" citation (propagated S65→S66 into ADR-030 D9 + `ferie-transfer-timing-research.md` + ROADMAP follow-up iii + `BalanceEndpoints.cs:859`) is WRONG → §15 stk.2/§17 (applied post-verification). The KB INDEX D9 row + `danish-agreements.md` were **checked and needed no change** (their §12 stk.2 references were the correct *taking-window* citation, or §-free) — per the research re-audit, not propagation sites. Codex cycle-2's numbering challenge was substantially right; the project's §21/boundaryMonth ruling was right.

---

### TASK-6701 — ADR-033 authoring (the settlement architecture)

| Field | Value |
|-------|-------|
| **ID** | TASK-6701 |
| **Status** | COMPLETE — ADR-033 ACCEPTED 2026-06-07 (Step-0b 3 cycles, all findings absorbed) |
| **Agent** | Orchestrator |
| **Components** | docs/knowledge-base/decisions/ADR-033 (new); KB INDEX row |
| **KB Refs** | ADR-030 D7/D9, ADR-032 D2, ADR-018 D3/D6/D8/D13, ADR-026, ADR-013, ADR-021 D4, ADR-025 D3/D6 |

**Description**: D1–D13 settling the money-free boundary, the verified §-spine, the deterministic period-close, the Backend↔Payroll exactly-once ownership, the settlement state machine, the provenance-keyed carryover writer, the wage-type-line scheme, the §21-agreement record, termination awareness, the fail-closed forfeiture/feriehindring, the særlige model-correction timing, the GLOBAL fence, and the phased roadmap. Owner-accepted at the post-cycle-3 halt-and-prompt.

---

### TASK-6702 — Design-sprint close-out (DONE 2026-06-08)

| Field | Value |
|-------|-------|
| **ID** | TASK-6702 |
| **Status** | COMPLETE 2026-06-08 — close-out propagation applied (a–e); dual-lens Step-7a-equivalent gate run (Reviewer + Codex; verdicts + absorption recorded in § External Review); this commit performs the close (push + CI-verify immediately follow) |
| **Agent** | Orchestrator |
| **Components** | SYSTEM_TARGET.md §K+§M; docs/references/danish-agreements.md; ADR-030 (D7 + consequence); ROADMAP.md; docs/knowledge-base/INDEX.md; docs/QUALITY.md; this log |

**Description (the remaining S67 design-sprint items):** (a) a **SYSTEM_TARGET settlement-execution section** (the target extension — §K/§M scope the rules, this adds the execution layer); (b) `danish-agreements.md` settlement wage-type rows (the day-count SLS løndele); (c) the **ROADMAP phased roadmap** entry (slices 1–4 with launch-relevance marks; §21-carryover-write as the launch-neutral first infrastructure unit); (d) cross-annotate ADR-030 D7 → ADR-033; (e) ADR-031 D6-style "no launch-blocker created" note; (f) the **design-sprint close** (`sprint-test-validation` SKIP per S38; the Step-7a-equivalent dual-lens on the final ADR+plan IS the gate + the close-guard artifacts; commit + push).

**Done (2026-06-08):**
- **(a) SYSTEM_TARGET** — new §K subsection *"Vacation Settlement (Period-End Disposition & Execution)"* (money-free principle; the six legal buckets §21 / §24 / §22+§25 / §26+§7 / §34 / §15 stk.2+§17; manual-fallback launch posture; GLOBAL fence; fail-closed §34/§22) + a §M wage-type pointer to ADR-033/danish-agreements.
- **(b) danish-agreements.md** — new *"Vacation Settlement Wage Types"* subsection: a **verified** table (særlige-godtgørelse løndele 5017/5027/5037 days; ferielønregulering 5062 hours; 5007 kroner-manual = StatsTid-never-emits) + a **pending per-slice-Step-0** table (§21 no-line, §24/§26 payout, §7 capped-deduction, §34 forfeiture) — honoring ADR-033 D1's scoping (the day-count finding does NOT generalize).
- **(c) ROADMAP** — Completed-Sprints S67 row + the rolling-detail S67 bullet with the **phased S68+ slice roadmap** (1 ferie-5th-week / 2 særlige / 3 termination / 4 feriehindring; §21-carryover-write = the launch-neutral first infra unit; slice 3 the only launch-relevant one) + next-sprint-candidates refresh + anchor 66→67.
- **(d) ADR-030** — D7 cross-annotated → ADR-033 (the "§8 wage-deduction" label reframed onto the verified §7 *udligning*, no separate §8; LBK 152/2024) + the "Open compliance item" consequence repointed.
- **(e) "no launch-blocker"** — recorded in the ROADMAP S67 bullet + SYSTEM_TARGET + QUALITY (pre-launch settlement is a manual operator-recorded fallback; slices automate per boundary).
- **KB INDEX** entropy fix (a stray blank line orphaned the ADR-033 row from the ADR table — removed) + **QUALITY** S67 note + anchor.
- **Entropy finding (surfaced at close, NOT S67-attributable):** the `docs/sprints/INDEX.md` Sprint Index table is missing rows for the entire **S58–S66** band (its highest row is S57; S67 likewise un-added) — pre-existing maintenance lapse, within CI freshness slack so the gate stayed green. Left untouched (cherry-picking only an S67 row would falsely imply the table is maintained); recorded as a **docs-debt backfill candidate** (ROADMAP/QUALITY) for the owner. A sibling stale figure surfaced adjacent to the new settlement section and **was corrected this close** (Codex Step-7a BLOCKER): `SYSTEM_TARGET.md` §K said the særlig feriegodtgørelse is "typically 1.5%" → corrected to the verified **2,02% (Cirkulære 021-24 §10, OK-2024)**, explicitly distinguished from the §17 2½% særlige-feriedage godtgørelse.

## External Review (Step 7a)

Design-sprint **Step-7a-equivalent dual-lens** on the final ADR-033 + the close-out propagation (the formal acceptance gate; `sprint-test-validation` SKIP per S38 precedent). Reviewed against parent `8cce1cd`. Artifacts: `.claude/reviews/SPRINT-67-step7a-codex.md` + `.claude/reviews/SPRINT-67-step7a-reviewer.md`.

| Lens | Cycle 1 | After absorption |
|------|---------|------------------|
| **Internal Reviewer** | APPROVED-WITH-WARNINGS — **0B** / 2W / 5N | all absorbed |
| **External Codex** | **BLOCKED** — 5B / 3W / 3N | all 8 cycle-1 findings absorbed → **cycle-2: 7/7 substantive RESOLVED** |

**Findings & absorption (all in fix-forward edits on `8cce1cd`):**
- **Codex B1 (design) — ADR-033 D4/D5 reversal-sequencing was internally contradictory** ("transition the active row in place" vs "reversal records at its OWN new sequence"; and the reversal line + superseding line could not both be `sequence+1`). Absorbed → an explicit **export-line-uniqueness invariant** on `(identity, sequence, bucket)`; state-transitions allocate no sequence; the literal `sequence+1` marked illustrative; exact allocation deferred to slice-Step-0b. The design (buckets / state machine / exactly-once) is unchanged — only the sequence description was disambiguated. (Recorded in ADR-033 § Review Findings → S67 Step-7a.)
- **Codex B2–B4 (doc-consistency, missed propagations)** — Sprint Goal "%/rate lines" vs D1 (→ day/hour-count); `SYSTEM_TARGET` stale "1.5%" (→ verified **2,02% §10**, distinguished from §17 2½%); KB INDEX ADR-030 row still "D7 §8 / §21-§26 deferred" (→ annotated completed-by-ADR-033, §8→§7). All absorbed.
- **Reviewer W1 / Codex (§10)** — the 2,02% general-ferietillæg was mis-cited §11 / §10-§11 across three docs; the verified authority is **§10**. Normalized everywhere. **Reviewer W2** — the sprint-INDEX entropy gap was understated (corrected to the full **S58–S66** band).
- **Codex W1–W2 / Reviewer NOTEs** — TASK-6700 over-claimed danish-agreements/INDEX as fix sites (→ recorded as checked/no-change); ROADMAP S60 `§8/§7` duplicate (→ deduped + superseded-note); "särlige"→"særlige" grep-hazard; range-dash→"+". All absorbed. The `s66-vite-dev.log` HMR churn is **excluded** from the close commit.

**Adjudicated disposition: APPROVED-WITH-WARNINGS.** Codex cycle-2's residual `verdict: BLOCKED` was solely the **review-records-itself paradox** — this *External Review* section and the *Retrospective* were unfilled at review time while the header read complete; all 7 substantive findings were RESOLVED. Completing these two sections (and syncing the entropy-finding text to the applied 1.5%→2,02% fix) in this close commit resolves it; the committed state is internally consistent. Cycle cap respected (2 Codex cycles; no cycle-3 — the residual was mechanical bookkeeping, not a substantive finding). Lens complementarity held: Codex caught four missed-propagations + the ADR reversal-sequencing contradiction that the internal lens did not; the internal lens caught the §10 citation, the entropy enumeration, and the spelling. A pre-existing trailing-whitespace NOTE (ADR-033 D13 slice-3 line) is not in the S67 diff (`git diff --check` clean on added lines) — left to the docs-debt pass.

## Test Summary

_n/a (design sprint) — baseline carried from S66: 631 unit + 466 regression + 5 smoke + 176 FE = 1278._

## Sprint Retrospective

**What shipped.** A DESIGN sprint: **ADR-033 (vacation-settlement architecture)** — the period-end *execution* layer completing the long-deferred ADR-030 D7 — plus a phased S68+ implementation roadmap. The headline positions: **money stays out of StatsTid** (settlement emits day/hour-count wage-type lines; SLS owns all kroner, including the 2½%/2,02% rates — code-verified: there is no salary/rate in-system); the **verified §-spine** (transfer §21 / auto-payout §24 / feriehindring §22+§25 / termination §26+§7 stk.1-capped / forfeiture §34 / særlige godtgørelse Cirk. §15 stk.2+§17; LBK 152/2024); a deterministic idempotent period-close BackgroundService; Backend-closes-&-events / Payroll-consumes-via-a-new-emitter with an exactly-once consumer checkpoint; a settlement **state machine** (PENDING_REVIEW/SETTLED/REVERSED + sequence + trigger); the first non-zero `carryover_in` writer; and **fail-closed §34/§22** (no wrongful forfeiture). **No launch-blocking item was created** — pre-launch settlement is a manual operator-recorded fallback; each slice automates a boundary.

**Process.** TASK-6700 deep-research (9/9 adversarially verified, 0 refuted) settled the disputed Ferielov/cirkulære numbering against primary sources and corrected a propagated shipped-doc citation (§12 stk.2→§15 stk.2/§17). ADR-033 ran Step-0b 3 cycles (Codex 5B→3B→4B / Reviewer 0B×3), all absorbed, owner-accepted. TASK-6702 propagated the decisions across SYSTEM_TARGET, danish-agreements, ADR-030, ROADMAP, KB INDEX, QUALITY, and ran the design-sprint Step-7a-equivalent dual-lens.

**What went well.** The money-free boundary held under scrutiny — the SLS day-count scoping (særlige-godtgørelse-only verified; §24/§26/§7 as per-slice Step-0 gates) propagated faithfully, with no over-generalization. The §-spine reproduced correctly in every doc.

**What the close caught (lens complementarity).** The external Codex lens BLOCKED cycle-1 with findings the internal Reviewer missed: **four missed-propagations** (the Sprint Goal still said "%/rate lines"; the KB INDEX ADR-030 row still framed D7 as "§8 / §21-§26 deferred"; TASK-6700 over-claimed two clean docs as fix-sites; a ROADMAP duplicate) **plus a genuine internal contradiction in ADR-033's reversal-sequencing** (state-transition-in-place vs own-new-sequence; reversal+superseding both at `sequence+1`). The internal lens independently caught the §10-vs-§11 citation, the understated entropy enumeration, and a "særlige" grep-hazard. Both were needed.

**Lessons.**
- **Close-out propagation is itself review-worthy.** The ADR was clean (one reversal-sequencing edge aside); the BLOCKERs were in the *propagated docs* — a design sprint's risk concentrates in faithfully threading the decision across canonical docs, not just in the ADR. Worth a dedicated dual-lens even when "only docs changed."
- **A design ADR can still ship an internal contradiction.** The cycle-2/cycle-3 Step-0b absorptions that built the reversal state machine introduced the `sequence+1` collision; a fresh close-lens caught it. Naming the *invariant* (export-line uniqueness) and deferring the *arithmetic* to slice-Step-0b is the right altitude for a design ADR.
- **Entropy discovered, honestly recorded not masked:** the `docs/sprints/INDEX.md` Sprint Index table is missing the entire **S58–S66** band (within CI freshness slack, so the gate never flagged it). Left for a docs-debt backfill rather than cherry-picking an S67 row; a sibling stale figure (SYSTEM_TARGET "1.5%"→2,02% §10) was corrected in-place.

**Open after S67.** The four implementation slices (S68+): slice 1 ferie-5th-week (the §21-carryover-write launch-neutral infra unit) is the lean first; **slice 3 termination is the only launch-relevant one** (ships-or-manual-fallback). Plus the boundary-timezone ADR (follow-up (v), intersects D3); the per-mechanism SLS-contract gates (§7-deduction cap the highest-risk); and the docs-debt pass (sprint-INDEX backfill + the residual whitespace NOTE).
