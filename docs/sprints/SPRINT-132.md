# SPRINT-132 — Fix-next remediation, increment 1: the correctness + safety core

| Field | Value |
|-------|-------|
| **Type** | Remediation (product code — S131 read-only contract is LIFTED) |
| **Baseline** | `455e34d` (S131 close; all-CI-green; working tree clean at kickoff) |
| **Program** | Increment 1 of the S131 fix-next PROGRAM (S132–S134), owner re-ruled 2026-08-20 (OQ-1). Impact Assessment in `ROADMAP.md` (§ Quality). S133 = test integrity; S134 = audit-scope + observability + docs. |
| **Discipline** | Every product fix carries a **RED-on-old** test (fails on baseline, passes after). `dotnet build` clean + relevant suite green per task. Step-0b plan review (this doc) + Step-5a high-risk external override + Step-7a dual-lens close. Docs Orchestrator-only; all product/test code delegated to domain agents. |
| **Sources of truth** | **Owner rulings (authoritative): `ROADMAP.md` §Quality → "Impact Assessment — S131 fix-next re-ruled into a program" (OQ-1/2b/4b).** `docs/operations/quality-finding-register.md` (the allowlist — nothing not a ratified row is in) · `docs/sprints/SPRINT-131-adjudication.md` (§Owner rulings) · `docs/sprints/SPRINT-131-consolidated-findings.md` (per-row provenance) · working refinement `REFINEMENT-s132-remediation.md` rev 3 (gitignored; secondary) |

## Goal

Remediate the invariant-adjacent **product** defects from the S131 quality sweep: close the Critical
daily-rest miscalculation and its day-attribution sibling, restore the missing payroll reversal guard,
unify the segment-manifest encoding, make the two swallowed-failure paths and the two diverged code
families fail loud, land the three ruling-5 SEC remediations + the SEC-004 verify test, and land the two
S132-eligible gates — each behind a clean payroll warning count where it edits payroll.

## Entropy Scan Findings (Step 0a — 2026-08-20)

- **CLEAN — product surface unchanged since `7e4bb1b`.** S131 was read-only on product code (`git diff`
  touched only `docs/**` + `.claude/**`), so the S131 Step-0a scan results still hold for `src/`.
- **CLEAN — FAIL-001 (`FindFirst("scopes")`)**: zero occurrences in `src/`.
- **CLEAN — PAT-005 (illegal RuleEngine calls from other services)**: the sole `RuleEngine.Api.Rules`
  match in `src/` is a doc comment in `AgreementRuleConfig.cs:7` recording a SEC-033 constant relocation,
  not a live cross-service call.
- **CLEAN — working tree** clean at `455e34d`; KB INDEX completeness is CI-enforced and green at S131 close.
- **DEBT (tracked, non-blocking)** — the `check_docs.py` link-presence-only gap and the duplicate
  "SharedKernel" domain-index rows (ROADMAP § Governance/docs). Not an S132 concern.

## Plan Review (Step 0b)

Dual-lens, MANDATORY (triggers: legal rule logic · payroll export · audit · multi-domain). Run BEFORE
decomposition (Step 1). BLOCKERs absorbed by plan edit; cycle-2 re-verification gates Step 1.

### Cycle 1 (2026-08-20) — both lenses NEEDS-REVISION; absorbed into rev 2

*External (Codex):*
- **BLOCKER** — gate ordering: QUAL-141 landed before TASK-5's final QUALITY.md revisit. **ABSORBED** —
  restructured to Phase 4 (validate + FINALISE QUALITY.md + row flips) → Phase 5 (gates) → Phase 6 (close);
  QUAL-141 now lands after TASK-132-4.
- **BLOCKER** — QUAL-069 froze the payroll warning count after Phase 2, but SEC-039 (Phase 3c) edits payroll
  PCS. **ABSORBED** — QUAL-069 condition now "clean after EVERY payroll edit incl. SEC-039"; lands in Phase 5.
- **WARNING** — agent assignments not per AGENTS.md scopes. **ABSORBED** — scope table rewritten with
  AGENTS.md paths + `(cross-domain authorized)` labels; TASK-3c split per agent.
- **WARNING** — SEC-039/040/041 lacked per-fix RED-on-old ACs. **ABSORBED** — each SEC task now carries one.
- **WARNING** — QUAL-002 rebuild AC didn't pin both encodings. **ABSORBED** — AC now requires a legacy
  numeric projection row AND a string rebuild-from-events, each asserted via `LoadManifestAsync`.
- **WARNING** — "summed-hours unchanged" contradicts TASK-1b's deliberate attribution change. **ABSORBED**
  (see the shared Reviewer finding below).
- **NOTE** — scope, TASK-0→1b gating, TASK-1a independence, QUAL-019 deferral, OQ-1/2b/4b, QUAL-003 absence
  all match the owner rulings. (Confirmation.)

*Internal (Reviewer Agent):*
- **BLOCKER** — TASK-1b day-attribution has an unaddressed **OK-version × segmentation interaction**
  (ADR-003 + ADR-016): attributing post-midnight hours to D+1 can move them across a date boundary that
  coincides with an OK segment boundary (2026-04-01), violating ADR-016's "day atomic at date-aligned
  boundaries" segment-safety; TASK-0 (legal-text only) didn't own it. **ABSORBED** — TASK-0 extended with a
  `(0-segmentation)` part; TASK-1b gains an OK-boundary RED-on-old AC asserting no hours cross the boundary.
  *(This is the invariant-level catch Codex missed — the clearest value of the dual lens this cycle.)*
- **WARNING** — TASK-1a "summed-hours unchanged" AC contradicts TASK-1b (carried over from OQ-2**a** framing,
  not reconciled to the ruled OQ-2**b**). **ABSORBED** — 1a confinement now scoped to non-crossing inputs /
  the 1a-only diff; 1b updates those expectations; Legal & Payroll row fixed.
- **WARNING** — four agent assignments wrong/under-specified (QUAL-007 in `src/Orchestrator/**`; QUAL-004 in
  Infrastructure not Payroll; QUAL-002 & QUAL-005 cross-scope; TASK-3c bundles three scopes). **ABSORBED** —
  scope table + TASK-3c split corrected accordingly.
- **CONFIRMED** — owner-ruling fidelity (OQ-1/2b/4b), QUAL-003 absence from S132, and scope tightness
  (6/12/7/2 High reconciliation) all clean.
- **NOTEs ABSORBED** — sources-of-truth now cites `ROADMAP.md` §Impact Assessment (authoritative); QUAL-049
  corrected to `ReportingLineEndpoints.cs` (reporting-line bulk import, not settlement); SEC-004 exempted
  from RED-on-old; TASK-1b RED-on-old flagged conditional on TASK-0; QUAL-133 RED-on-old must force the §15
  GoLiveDate condition in-test.

### Cycle 2 (2026-08-20) — Reviewer READY; Codex found one fix-induced BLOCKER; absorbed into rev 3

*Internal (Reviewer Agent):* **READY-TO-DECOMPOSE** — its cycle-1 BLOCKER (OK-version × segmentation) and
all three WARNINGs verified resolved; Codex's two gate-ordering BLOCKERs independently verified; nothing new
at BLOCKER/WARNING level; the QUAL-002 both-encodings AC rated a genuine improvement. Two NOTEs, both
**ABSORBED into rev 3**: (a) AC (ii)'s re-validation list now includes `REST_PERIOD_48H_CEILING`
(`CheckWeeklyMaxHours`) + a QUAL-123 non-entanglement note; (b) the exact-11.0h boundary ownership split
between TASK-1a (11h rest) and TASK-1c (`MaxDaily` + 48h) is stated.

*External (Codex):* cycle-1 findings 1–6 resolved; Reviewer-BLOCKER absorption confirmed. **One NEW BLOCKER
+ one WARNING (both consequences of the Phase reorder), ABSORBED into rev 3:**
- **BLOCKER** — Phase 4 validated BEFORE the Phase-5 gate edits, so the gate changes (`check_docs.py`,
  `Directory.Build.props`) were never re-validated and QUAL-141/069's rows never flipped. **ABSORBED** —
  new **TASK-132-6a** re-runs build + exercises the new gates + flips their rows before the Step-7a close.
- **WARNING** — SEC-004 (Test & QA) had no dependency to run after its implementation (AGENTS.md:37).
  **ABSORBED** — sequencing bullet added: Test & QA follows implementation; RED-on-old authored-first /
  verified-green-after; the confirming SEC-004 runs after its code lands.

### Cycle 3 (2026-08-20) — both lenses cleared; Step-0b COMPLETE

*Internal (Reviewer Agent):* **READY-TO-DECOMPOSE** — both cycle-2 NOTEs resolved; Codex's post-gate
BLOCKER fix verified correct; new Phase 6a/6b introduces no BLOCKER/WARNING; owner-ruling fidelity,
QUAL-003 absence, scope tightness (6/12/7/2), and agent assignments all remain clean. Two decompose-time
NOTEs (no plan edit warranted): (1) bind QUAL-021 (the RED-on-old for QUAL-001) to the TASK-1a/1b Rule
Engine fixes, not an end-of-sprint Test & QA pass; (2) keep the 6a register-row flips and any close-time
QUALITY.md edit on the FRESH side of the now-live QUAL-141 gate.

*External (Codex):* cycle-2 BLOCKER + WARNING resolved; **one new WARNING** (TASK-6a flipped QUAL-069
unconditionally though 5b may defer it) — **ABSORBED**: the QUAL-069 flip is now conditional on 5b actually
landing the gate. **No BLOCKER at cycle 3** → the cycle-cap halt-and-prompt does not fire.

**STEP-0b COMPLETE — 3 dual-lens cycles, both lenses cleared, plan LOCKED.** Decompose-time NOTEs above are
carried into Step 1. Proceed to decomposition.

## Scope

**IN (S132 — correctness + safety core):**

> Agent labels follow `AGENTS.md` file scopes (Rule Engine `src/RuleEngine/**`+`SharedKernel/**/Calendar`;
> Data Model `SharedKernel/**/Models,Events,Interfaces`; Payroll `src/Integrations/**/Payroll/**`; Security
> `src/Infrastructure/**/Security`, `src/Backend/**/Middleware`, `SharedKernel/**/Security`; Test & QA
> `tests/**`; UX `frontend/**`). Work outside any single scope uses the `(cross-domain authorized)`
> convention (AGENTS.md:44-57). Exact loci are pinned from the register at decomposition.

| Row | What | Known locus | Domain agent (AGENTS.md scope) | Step-5a high-risk? |
|-----|------|-------------|--------------------------------|--------------------|
| QUAL-001 (1a) | Daily-rest computed on absolute instants (fixes the ~29h wall-clock miscalc + the invisible day-shift+night-callout) | `RestPeriodRule.cs:211-278` (`CheckDailyRest`) | Rule Engine | **Yes** — legal rule logic |
| QUAL-001 (1b, day-attr, OQ-2b) — **NOW a representation change (owner ruled "full fix" 2026-08-20)** | Normalize midnight-crossing shifts into per-day entries so hours land on the correct day AND OK-version by construction; rule keeps summing by `e.Date` (now clock-correct) | ingest/representation layer (`TimeEntry`/event/projection) + `RestPeriodRule.cs:151-159,178-203,285-338,345-375` | **Data Model + Backend Endpoints + Rule Engine** (spans scopes) — **gated on the event-sourcing-safe design ADR (dual-lens, in progress) AND TASK-0 already ratified** | **Yes** — legal rule logic + architectural (event representation) |
| QUAL-021 | RED-on-old test for QUAL-001, driving the real `RuleRegistry` classification path | `tests/**` (RuleEngine suite) | Test & QA | (test) |
| QUAL-114 (boundary leg only) | Pin the three rest-rule thresholds at their exact limits | `RestPeriodRuleTests.cs:27-48,233-250` | Test & QA | (test) |
| QUAL-133 | SPECIAL_HOLIDAY (§15 stk.2/§17) export handler gains the under-lock REVERSED-row probe the §24 sibling carries. **Corrected from reading the code:** the reconciled-check branch is legitimately N/A here (no §15/§17 operator-reconciliation surface — the handler's own parity note, `:678-686`), so the fix is the REVERSED probe ONLY, not "both guards" | `SettlementExportEmitter.cs:643-780 (parity note :678-686)`; mirror the §24 path's step-0b SKIPPED_VOIDED probe | Payroll Integration | **Yes** — payroll export |
| QUAL-002 | Unify `boundaryCause` encoding across PCS writer + reader; tolerant reader accepts both encodings | `PeriodCalculationService.cs` JsonOptions (PCS = Payroll) `(+ SegmentManifestProjectionRebuilder.cs` only if its C# is edited — likely PCS-only, the rebuilder projection is pure SQL already emitting strings) | Payroll Integration (extended into Infrastructure, cross-domain authorized — confirm at decompose) | **Yes** — payroll |
| QUAL-006 | Payroll idempotency swallow now logs + surfaces | `src/Integrations/**/Payroll/**` (pin at decompose) | Payroll Integration | **Yes** — payroll |
| QUAL-007 | Weekly-pipeline swallow (failed HTTP fetches) now logs + surfaces | `WeeklyCalculationPipeline.cs` + `OrchestratorControlLoop.cs` in `src/Orchestrator/**` | Orchestrator scope (cross-domain authorized) — **not** Rule Engine (no-I/O + wrong path) | (surface at decompose) |
| QUAL-004 | YEAR_END recovery fails loud like its three siblings | `VacationSettlementService.cs` in `src/Infrastructure/**` | Infrastructure (cross-domain authorized) — **not** Payroll | **Yes** — settlement / retroactive |
| QUAL-005 | One shared Copenhagen business-date helper with an injectable clock (kills the +01:00 CEST fallback bug) | six copies across `src/Backend/**/Endpoints`, `src/Infrastructure/**`, `src/SharedKernel/**/Models` | cross-domain authorized — **declare scope set + helper home at decompose** (SharedKernel/Calendar is Rule Engine scope) | (date-correctness) |
| SEC-039 ← QUAL-061 | Remediation (ruling 5a) | `PeriodCalculationService.cs` (Payroll) | Payroll Integration | payroll-adjacent |
| SEC-040 ← QUAL-063 | Remediation (ruling 5a) | `AuthEndpoints.cs` (`src/Backend/**/Endpoints`) | Backend Endpoints (cross-domain authorized) | **Yes** — auth |
| SEC-041 ← QUAL-049 | Remediation (ruling 5a) — reporting-line **bulk import** (NOT settlement) | `ReportingLineEndpoints.cs` (`src/Backend/**/Endpoints`) | Backend Endpoints (cross-domain authorized) | where auth-adjacent |
| SEC-004 | Belt-and-braces **confirming** test (ruling 5b; premise already retired) — **exempt from the blanket RED-on-old DoD** | `tests/**` | Test & QA | (test) |
| QUAL-141 | Promote `check_docs.py` freshness warning to a hard failure for `docs/QUALITY.md` | `tools/check_docs.py` (no agent scopes `tools/**`) | Orchestrator (tooling) | (gate) |
| QUAL-069 | Payroll warn-gate — **lands only if the payroll warning count is clean after ALL payroll edits, incl. SEC-039's PCS edit** (OQ-5) | `Directory.Build.props:48-58` | Orchestrator (build config; owner-approval per CLAUDE.md) | (gate) |

**OUT (explicitly deferred — the register is the allowlist):**
- **S133**: QUAL-013/014/015/016/017/018/019/020/022 (test integrity) + QUAL-095/110/111 (test-family) +
  QUAL-027/036 (D4 dead-code batch) + gates QUAL-096/121 + QUAL-072/073/074.
- **S134**: QUAL-003 + QUAL-009 IN FULL (OQ-4b — build the calc-host audit middleware so the ADR-016 D10
  join works as written; ADR-016 **not** amended) + QUAL-008 observability + the doc-fix pass
  (QUAL-010/011/012/093 + enumerated D7 rows + QUAL-090) + SEC-038/042.
- **Domain-semantics track**: QUAL-123 (48h reference-period averaging) + the day-attribution domain-truth
  question (OQ-2b) — the analysis is a track item; its *code* lands here in S132 (TASK-0 → TASK-1b).
- **The 112 Medium rows** stay register-tracked (owner ruling 9), except the ruling-5 SEC mirrors above.

## TASK-0 outcome + owner ruling (2026-08-20)

TASK-0 (day-attribution domain-truth research) is **DONE** → `docs/references/day-attribution-midnight-crossing-research.md`.
It surfaced a genuine architectural fork: a fully-correct day-attribution fix cannot live inside the rule
(the entry is bound to one OK-segment by `e.Date` before any rule runs), so the correct fix is to
**normalize midnight-crossing shifts into per-day entries** — which appeared to conflict with the
events-record-facts invariant (ADR-001/018) that an earlier lens cited against split-on-ingest.

**Owner ruling: "full fix now (split at entry)."** Consequences, recorded honestly:
1. Day-attribution grows from a Rule Engine edit into an **ingest/representation change** spanning Data
   Model + Backend Endpoints + Rule Engine — **S132 scope materially widened.**
2. This touches an inviolable invariant (auditability / events-record-facts), so before any code the
   Orchestrator runs a **dual-lens design adjudication** (in progress) to settle the invariant-safe
   normalization LAYER — most likely: keep the raw crossing shift in the immutable event (records the fact
   as entered) and normalize in a **rebuildable projection / read-model** the rule consumes (ADR-018
   pattern), rather than reshaping the write-time event. Outcome recorded as a new **ADR** (Orchestrator KB).
3. **TASK-1b restructures** into (b1) the ingest/projection normalization per that ADR + (b2) the rule-side
   consumption + the OK-boundary RED-on-old test. The exact task breakdown is finalised once the ADR lands.
4. The material fact the ADR must weigh: the WorkTime ingest path ALREADY splits crossings at midnight
   (`SkemaEndpoints.cs:722`; `SkemaGrid.tsx:147`), so normalization may be a *lossless unification* of an
   existing convention rather than a new interpretation — the `/time` + compliance-projection paths, which
   skip the `end>start` check, are the reachability gap.

## Task list

> Exact per-row file loci are pinned from the register at decomposition (Step 1); the loci above are the
> known anchors. Each product task's Definition of Done = RED-on-old test written (fails at `455e34d`) +
> fix + relevant suite green + `dotnet build` clean; high-risk tasks add the Step-5a external Codex lens.

### PRE-TASK (gates the day-attribution code)

- [ ] **TASK-132-0 — Day-attribution domain-truth analysis (TWO parts).** *(Orchestrator + research;
      dual-lens; owner confirms before TASK-1b code.)*
      - **(0-legal)** Establish, with citations (Arbejdstidsloven / EU Working Time Directive + AC/HK/PROSA
        agreement text), which calendar day a shift's post-midnight hours count toward for the weekly-rest
        (`CheckWeeklyRest`) and daily-hours (`CheckMaxDailyHours`, `CheckWeeklyMaxHours`) checks.
      - **(0-segmentation)** — *added by Step-0b BLOCKER* — rule on the **boundary-coincident case**: the
        rule runs per-segment over entries filtered by `e.Date` (`RestPeriodRule.cs:151-159`), and ADR-016's
        classification is segment-safe only because "day is atomic at date-aligned boundaries"
        (`REST_PERIOD_MAX_DAILY`) / aligned-window + `RejectIfMultipleSegments` (`DAILY_REST`/`WEEKLY_REST`).
        A midnight crossing is a date boundary that can coincide with an OK-version segment boundary
        (2026-04-01 OK24→OK26). Determine whether attributing post-midnight hours to D+1 can push them
        outside the current segment window / into the next OK segment, and rule on the correct behaviour so
        that **no hours silently move across an OK-version boundary** (ADR-003 entry-date resolution;
        domain-correctness + architectural-integrity invariants).
      Output: a research note under `docs/references/` (sibling of the ferie-timing and vacation-consumption
      dossiers) stating BOTH the attribution rule AND its segmentation/OK-boundary disposition. **Owner
      ratifies before any TASK-1b code.** Distinct from QUAL-123 (48h averaging), same track. *Blocks
      TASK-1b only; TASK-1a and Phase-2/3 proceed in parallel.*

### Phase 1 — Rule Engine (domain correctness)

- [ ] **TASK-132-1a — QUAL-001 core (the confirmed 29h fix).** `CheckDailyRest` computes rest on absolute
      instants `(Date+Start, Date+End (+1d if End≤Start))`. RED-on-old (QUAL-021) pins: `23:00→02:00`
      then `07:00` → VIOLATION (not 29h); the day-shift+night-callout case no longer invisible; **exact
      11.0h boundary** pass/fail; overlapping/same-day; period-edge crossing; derogation/voluntary arm.
      Confinement regression: `CheckMaxDailyHours` + `CheckWeeklyMaxHours` results are **unchanged by the
      1a edit** — asserted against the **1a-only diff using non-crossing inputs** (proving 1a is confined to
      the rest-gap logic). *These expectations are then UPDATED by TASK-1b (day-attribution deliberately
      changes daily-hours attribution — see 1b); the "unchanged" claim is scoped to 1a-in-isolation, not to
      completed QUAL-001.* Test drives the real `RuleRegistry`. **Step-5a high-risk (legal rule logic).**
      *Not blocked by TASK-0; but 1a and 1b both edit `RestPeriodRule.cs` → they are SEQUENTIAL (1a→1b),
      not parallel-in-file.*
- **TASK-132-1b — QUAL-001 day-attribution (OQ-2b, "full fix").** Decomposed per the dual-lens design
  adjudication (2026-08-20) into **projection-time normalization** (ADR-039). **BLOCKED until owner ratifies
  ADR-039.** Sub-tasks:
  - [ ] **1b-1 — projection normalization (the mechanism).** Add the deterministic, versioned midnight-split
        to the `time_entries_projection` derivation — per-`(day, OkVersion)` rows with a shared source-stint
        continuity link (ADR-039 D1-D4). **ONE implementation shared by the in-tx writer AND the
        rebuild-from-events path** (D6 — else recreate the QUAL-002 split-encoding bug). Conservation
        invariant (no dropped/double-counted hours). **Data Model + Infrastructure (cross-domain authorized).**
  - [ ] **1b-2 — ADR-039 + ADR-016 row-6 amendment.** Orchestrator/docs (this ADR + the row-6 edit + KB INDEX).
  - [ ] **1b-3 — rule-side consumption (D5).** Hours-summing checks (`CheckMaxDailyHours`,
        `CheckWeeklyMaxHours`) consume the normalized per-day rows; **rest checks (`CheckDailyRest`,
        `CheckWeeklyRest`) read the CONTINUOUS stint via instants (TASK-1a)** — NOT the blunt split (else a
        false 0-hour midnight gap / false 7th worked-day). Must NOT entangle with QUAL-123's deferred 48h
        averaging (same `CheckWeeklyMaxHours` locus — register cross-references both). **Rule Engine.**
  - [ ] **1b-4 — defense-in-depth + RED-on-old.** Tighten `RequestValidator.cs:17` so `/time` rejects an
        un-split `end≤start` crossing (D8, defense-in-depth, not the mechanism) — **Backend Endpoints
        (cross-domain authorized)**. RED-on-old: crossing 2026-03-31 23:00 → 2026-04-01 02:00 with a **divergent
        OK26 `MaxDailyHours`** in-fixture, asserting per-half OK-version, conservation (no dropped/double hours),
        no false midnight rest gap, no false 7th worked-day. **Step-5a high-risk (legal rule logic +
        event-representation).** Updates TASK-1a's "unchanged" daily-hours expectations to the correct new
        attribution.
- [x] **TASK-132-1c — QUAL-114 boundary-threshold leg.** *DONE 2026-08-21 — merged; internal Reviewer clean
      (no findings); build green, 18/18 RestPeriod tests pass.* Removed the dead `WeeklyMaxHoursReferencePeriod`
      fixture config (never read; behavior-neutral) + 4 exact-limit boundary tests pinning `CheckMaxDailyHours`
      (13.0/13.01h) and `CheckWeeklyMaxHours` (48.0/48.01h avg) — the previously-untested `>`-vs-`>=` edges
      (green-both regression-locks; the hours-checks were unchanged by QUAL-001, so scoped to them, not the
      rewritten rest checks). Pin the thresholds at their exact limits;
      remove the never-read reference-period fixture config (its reference-period concern is QUAL-123, out).
      **Ownership split (Step-0b cycle-2 NOTE): TASK-1a owns the exact-11.0h daily-rest boundary fixture;
      TASK-1c owns the `MaxDailyHours` + 48h-ceiling exact-limit fixtures** — no duplicated/conflicting
      fixture across 1a/1c.

### QUAL-001 cluster — Step-5a (1b-1) findings & re-plan (2026-08-20)

1b-1 (the `MidnightCrossingNormalizer`) is BUILT and dual-lens reviewed. Both lenses **affirm the normalizer
itself is correct and invariant-safe** (pure, conservation bit-for-bit, per-half OkVersion, continuity link,
replay-covered, event+display untouched). But both returned **NEEDS-REVISION** on the increment — it must NOT
merge alone. Three gaps:

- **GAP-A (BLOCKER, both lenses) — co-land the rest consumer.** 1b-1 feeds split halves to the CURRENT rest
  checks (compliance path), which read a false 0-hour midnight gap → wrong daily/weekly-rest result on the
  LIVE monthly compliance surface *today* (not gated on OK26). Fix: **1a (daily-rest instants) + 1b-3
  (stint-aware rest consumption via `SourceStintId`) MUST land WITH 1b-1 as one reviewed/merged unit** before
  any validation/commit. Architectural fact (Reviewer): rest checks run ONLY on the compliance path; PCS runs
  the calc-family rules.
- **GAP-B (period-edge hour-drop) — COMPLIANCE fixed; PAYROLL is a caller-contract residual.** A crossing on
  the LAST day of a period had its D+1 half dropped and never re-fetched → hours in NEITHER period (a payroll
  underpayment + OK26 hours unjudged at the 2026-04-01 boundary; the 1b-1 test masked it with a 2-day span).
  - **Compliance path: FIXED** — `ComplianceEndpoints` now reads `[monthStart-1 .. monthEnd]`, normalizes,
    then the rule's period filter keeps the correct halves. Unit test reworked to a real monthly period.
  - **Payroll path: caller-contract residual.** The PCS calc receives entries via the request contract
    (`CalculateAndExportRequest.Entries` / `RecalculateRequest.Entries`), not a server-side read — so the
    widen is a **caller obligation** (assembler must read `periodStart-1`), documented in ADR-039 D5b. No
    in-repo request-assembler exists today. **Disposition pending the combined Step-5a Reviewer ruling:**
    document-as-contract + tracked go-live precondition, vs. an in-repo PCS guard that refuses/flags an
    entry set not covering `periodStart-1`.
- **GAP-C (WARNING, Reviewer) — RESOLVED by the boundary sweep.** Of the 5 time-entry→rule boundaries, only
  TWO are results-of-record — `PeriodCalculationService` (pay-of-record) and `ComplianceEndpoints`
  (live compliance) — and **1b-1 already normalizes both**. The other three passing time entries are
  SECONDARY (`WeeklyCalculationPipeline` ×2, `TaskDispatcher` — no manifest/export/verdict, raw `JsonElement`
  passthrough) → **documented ADR-039 exclusions (D6a)** + a tracked normalize-or-retire follow-up.
  (OvertimeEndpoints/ApprovalEndpoints/OrchestratorScopeHelpers confirmed NON-boundaries — they pass no time
  entries to rules.) So no additional boundary normalization is needed; the remaining QUAL-001 work is GAP-A
  + GAP-B, confined to PCS + Compliance + the RuleEngine rest checks.
- **NOTEs:** `PolicyVersion` is not persisted in the manifest → replay determinism holds only WITHIN a policy
  version (document; or persist the tag). The new `SharedKernel/Normalization/` folder = Data Model scope
  (cross-domain authorized), recorded. Fix `.gitattributes`/line-endings on the new files.

**Contention:** QUAL-007 (TASK-3a) is currently editing `WeeklyCalculationPipeline.cs` (fetch-success) — the
GAP-C normalization/fetch-widen on that file layers AFTER QUAL-007 merges.

**Expanded QUAL-001 build order (supersedes the earlier 1b-1→1a+1b-3→1b-4):** 1b-1 normalizer [built] →
boundary set [Explore] → **one co-reviewed/co-merged unit = {per-boundary fetch-widen + normalize (GAP-B/C) +
RuleEngine 1a + 1b-3 rest consumer (GAP-A)}** → 1b-4 guard + tests, incl. a period-edge test over a **real
monthly period** (not a 2-day span) proving no dropped hours and OK26 attribution at the month boundary.

**Cycle-2 combined Step-5a → cycle-3 revision (2026-08-21).** The combined review raised 3 edge BLOCKERs,
all fixed: (B1) the normalizer now emits REJOINABLE halves even when `SourceStintId` is null (a deterministic
SHA-256-derived shared id); (B2) the rest checks reconstruct stints from RAW entries BEFORE the period filter
(hours-checks stay on period-filtered rows) so a lower-edge crossing's true start day survives (no false
7-day weekly-rest); (B3) a PCS `AssertNoDroppedBoundaryCrossing` fail-closed guard turns the payroll
period-edge underpayment LOUD (throws on an un-split boundary-last-day crossing; doesn't trip on normal
input). NOTEs absorbed (segmentation rationale corrected in-code; negative-rest display clamped). **953/953
unit tests green.**
- **D8 REVERTED (Orchestrator correction):** the `/time` crossing-rejection was wrong — ADR-039's normalizer
  is built for the ONE-registration model (register a crossing as one entry; the normalizer splits it into
  rejoinable halves). D8 forced a TWO-registration model whose halves lack a shared continuity id → a false
  0-hour midnight gap. `/time` returns to accepting un-split crossings; the normalizer + the PCS guard handle
  them.
- **↪ S132-DISCOVERED FOLLOW-UP (to register at close):** the compliance rest checks rejoin a crossing's
  halves ONLY when they share a `SourceStintId` (normalizer-produced from ONE entry). Two SEPARATELY-
  registered adjacent per-day intervals (a user manually filing `D 23:00→00:00` + `D+1 00:00→02:00`) are
  read as two stints → a false 0-hour midnight gap. Candidate fix: rejoin clock-adjacent zero-gap
  same-employee stints, or carry a shared id across a manual per-day split. Track as a new QUAL row.

**Cycle-3 verification (2026-08-21) → MERGE-READY → MERGED.** Both lenses cleared: Codex "clean, no new
regression"; internal Reviewer "MERGE-READY, no new BLOCKER at cycle 3". The guard-trip risk against the
existing regression suite is statically de-risked — the only regression file with timed entries
(`RegressionTests.cs`) uses exclusively non-crossing daytime shifts, so neither the normalizer nor the guard
alters/trips any existing fixture. **QUAL-001 unit MERGED to main (uncommitted): 7 files** (normalizer +
`TimeEntry.SourceStintId` + `RestPeriodRule` + `PeriodCalculationService` + `ComplianceEndpoints` + 2 unit
tests). The 29h→5h daily-rest fix, day-attribution, and the compliance period-edge are all fixed; 953/953
unit tests green. **QUAL-001 (Critical) + QUAL-021 (RED-on-old) DONE; QUAL-114 boundary-leg still pending
(TASK-1c).**

**⚠ TRACKED — go-live-coupled (Reviewer cycle-3 WARNING):** the PCS `AssertNoDroppedBoundaryCrossing` guard is
a LOUD interim backstop. When the GAP-B payroll caller-widen lands (ADR-039 D5b, the "real fix"), every
period's calc will include its last-day crossing and the guard would then throw on VALID input — so **the
guard and the caller-widen must be removed/reworked IN LOCKSTEP.** Recorded on the go-live precondition so
the widen never lands alone. (Reviewer NOTE: consider migrating the guard from `PlannerInvariantViolation` to
the graceful `HandleDeterministicFailure` contract for a cleaner client error — minor, at rework time.)

**Docker-gated verification owed at close:** the RuleEngine regression + payroll-calc regressions
(MixedVersionExport / marquee / replay) under Docker (no Docker in this env) — the static de-risk above
covers the guard-trip concern; the full RED→GREEN still runs at close.

### Phase 2 — Payroll / Settlement (parallel where independent)

- [x] **TASK-132-2a — QUAL-133 reversal probe.** *DONE 2026-08-20 — merged to main (uncommitted); Step-5a BOTH lenses CLEAN (Codex "clean, no findings" + internal Reviewer "no findings"); `dotnet build` green. The under-lock probe mirrors the §24 sibling exactly. **Docker-gated regression RED→GREEN to be confirmed under Docker at close** (this env has no Docker daemon).* Add the under-lock REVERSED-row probe (the SKIPPED_VOIDED
      branch) to `ProcessSaerligeFeriedagePaidOutAsync`, mirroring the §24 sibling's step-0b probe. **The
      reconciled-check branch is correctly OMITTED** (no §15/§17 reconciliation surface — the handler's
      parity note `:678-686`; do NOT add it). **RED-on-old: the export path is gate-DORMANT at baseline
      (`Settlement:GoLiveDate` unconfigured, no `SaerligeFeriedagePaidOut` emitted), so the test MUST force
      the §15 go-live condition in-fixture and stage a `SaerligeFeriedagePaidOut` after a `SettlementReversed`
      — asserting the line is skipped/compensated, not orphaned** (else the red test is vacuous). **Step-5a
      high-risk (payroll export). Named go-live precondition (§15 stk.1).**
- [ ] **TASK-132-2b — QUAL-002 encoding unification.** `JsonStringEnumConverter` on PCS write AND read
      options; reader tolerant of both encodings. **AC (sharpened by Step-0b): the rebuild/replay test seeds
      inputs in BOTH encodings and asserts each reconstructed manifest's `boundaryCause` reads back
      correctly via `LoadManifestAsync` — (a) a LEGACY numeric projection row (as written by the old
      converter-less direct writer) remains readable via the tolerant reader, AND (b) a rebuild-from-events
      (events are string-encoded) produces a string projection row that also reads back.** *DONE 2026-08-21 —
      merged (via `git apply --reject` + a 1-line manual import merge, since QUAL-001 + QUAL-002 both add a
      PCS `using`); all 9 fixes build together 0 errors. Step-5a: both lenses cleared the substance;
      cycle-1/2 WARNINGs absorbed — the "byte-identical" claim reframed to DESERIALIZED EQUIVALENCE (the real
      ADR-018 contract; null-snapshot byte-residual is benign + recorded as follow-up #8), and the fabricated
      null-snapshot assertion replaced with real event→rebuild + `ReplayAsync` checks. Fix = `JsonStringEnumConverter`
      on PCS's shared JsonOptions ONLY (no `WhenWritingNull`/wire-format change); rebuilder confirmed pure-SQL,
      unedited.* Confirm at implementation whether the
      rebuilder C# is edited at all (its projection may be pure SQL already emitting strings → PCS-only fix).
      RED-on-old. **Step-5a high-risk (payroll).**
- [x] **TASK-132-2c — QUAL-004 YEAR_END recovery fails loud.** *DONE 2026-08-21 — merged (uncommitted);
      Step-5a BOTH lenses clean (Codex + internal Reviewer no-BLOCKERs); build green.* The `SettleActiveYearEndAsync`
      winner-null fallback now `throw`s (was a fabricated `AlreadySettled(row)`), faithfully mirroring its 3
      siblings. Per owner ruling **"seam + test all four"**: landed a minimal behavior-preserving repo
      test-seam (unseal + 2 `virtual`s, no DI change) + a four-block fail-closed suite (1 genuine RED-on-old
      for YEAR_END + 3 regression-locks). **Docker-gated RED→GREEN confirmed at close** (no Docker in env).
      Match its three siblings. RED-on-old. **Step-5a (settlement / retroactive).**
- [x] **TASK-132-2d — QUAL-006 payroll idempotency swallow.** *DONE 2026-08-21 — merged; Step-5a BOTH lenses
      clean (Codex + internal Reviewer no-BLOCKERs); build green.* **Architectural finding:** the marker was
      written in a SEPARATE post-commit tx in the endpoint (swallowed on failure) → the correct fix folds it
      INTO `RetroactiveCorrectionService`'s correction tx (atomic with the event+audit+baseline; loud
      log+rethrow → rollback; `UNIQUE(idempotency_token)` fails concurrent same-token CLOSED). Gate↔consumer
      confirmed consistent. *Intended behaviour (documented): the rare concurrent-race loser gets a
      fail-closed, self-healing 5xx (next retry → 200) — trades a rare accurate 5xx for zero phantoms.*
      RED-on-old (Docker-gated → verified at close). Log + surface the swallowed failure. **Step-5a (payroll).**

### Phase 3 — Backend / Shared

- [x] **TASK-132-3a — QUAL-007 weekly-pipeline swallow.** *DONE 2026-08-20 — merged to main (uncommitted); internal Reviewer clean (no BLOCKERs) + Constraint Validator self-check pass; build + 55/55 orchestrator tests green.* Guards both Backend fetches with `IsSuccessStatusCode` and THROWS `HttpRequestException` on failure (verified the sole caller `OrchestratorControlLoop` only treats exceptions — not `Success=false` — as failure); secrets-safe bounded body snippet. Log + surface. RED-on-old on the silent path.
  - **↪ S132-DISCOVERED FOLLOW-UP (to register at close):** the same file's rule-eval helpers (`CallRuleEvaluateAsync`/`CallAbsenceEvaluateAsync`/`CallFlexEvaluateAsync`) swallow a non-success RULE response to `null` while the pipeline still returns `Success=true` — the same silent-failure class as QUAL-007 but for rule-eval calls. Correctly scoped OUT of QUAL-007 (data-fetch); track as a new QUAL row.
- [x] **TASK-132-3b — QUAL-005 Copenhagen business-date helper.** *DONE 2026-08-21 — merged; Step-5a BOTH
      lenses clean (Codex + internal Reviewer APPROVE); build green, 945 unit tests pass.* One shared
      `SharedKernel/Calendar/CopenhagenBusinessDate.Today(TimeProvider)` (real Europe/Copenhagen zone, DST-
      correct, IANA→Windows→UTC fallback, never a hardcoded offset); all 6 copies consolidated (5 were
      already correct → delegating wrappers; the §21 copy fixed). RED-on-old = summer-near-midnight +
      characterization of the old `+01:00` arithmetic. **Severity clarified (Reviewer NOTE, for register
      accuracy):** the §21 copy's MAIN path was already DST-correct; the hardcoded `+01:00` was only the
      TERMINAL fallback (stripped host, no tz DB) — so the real win is making it clock-INJECTABLE + closing
      the fallback bug, NOT "wrong every summer in production." Reflect this when flipping the QUAL-005
      register row (don't overclaim). One shared helper with an injectable clock; a CEST-date test the old
      +01:00 fallback fails. RED-on-old.
- [x] **TASK-132-3c-1 — SEC-039 (← QUAL-061).** *DONE 2026-08-21 — merged (direct apply, no conflict); Step-5a
      BOTH lenses clean (Codex + internal Reviewer no-BLOCKERs); build green.* Removed the full rule-engine
      RESPONSE-BODY log (which echoed employee id + per-day hours/rates/balances) from the 3 rule-call failure
      paths (`CallTime/Absence/Flex`); now logs status + rule id + employee id only. Other log sites audited —
      no other body/payload leak. RED-on-old (reflection-driven, employment-data sentinel). *NOTE: only the
      time-rule path has a dedicated test; absence/flex got the identical mechanical edit (representative
      sampling — both lenses accepted).* **Payroll Integration.** *(payroll edit → counts toward the QUAL-069
      warning-count freeze — see gate ordering.)*
- [x] **TASK-132-3c-2 — SEC-040 (← QUAL-063).** *DONE 2026-08-21 — merged; Step-5a: internal Reviewer clean,
      Codex 1×P2 (CR/LF log-forging) ABSORBED (a `SanitizeForLog` control-char strip on the new + adjacent
      username log lines + a newline-username regression test); build green.* Failed logins now emit a
      structured WARNING (username + reason class + IP + correlationId; password never logged; generic 401
      preserved — no response-side enumeration oracle). RED-on-old (Docker-gated → verified at close).
      **Backend Endpoints (cross-domain authorized). Step-5a high-risk (auth).**
- [x] **TASK-132-3c-3 — SEC-041 (← QUAL-049).** *DONE 2026-08-21 — merged; both lenses clean (Codex + internal
      Reviewer no findings); build green.* The bulk-import catch-all now LOGS the exception server-side (Error,
      safe context) + returns a generic `{error:"Import failed"}` 500 with NO raw `ex.Message`. Specific
      handlers preserved. RED-on-old via IOutboxEnqueue fault-injection (Docker-gated → verified at close).
      **Backend Endpoints (cross-domain authorized).**
- [x] **TASK-132-3c-4 — SEC-004 confirming test.** *DONE 2026-08-21 — merged; internal Reviewer clean (no
      findings); build green; Docker-gated.* A confirming test that the flat-authority reform (exact-Organisation
      equality, ADR-035 D3 `ValidateSameOrganisationAsync`) rejects a secondary-principal (vikar) binding across
      SIBLING Organisations under the same MAO (STY01 vs STY02 / MIN01) — the exact pair the retired nested
      model would have allowed; the sibling vikar is given covering scope so the same-Organisation guard is the
      SOLE rejection cause + a same-Org positive control. **EXEMPT from RED-on-old** (green-both confirming
      guard). *Reviewer NOTE (optional, non-blocking): assert the more specific "same styrelse" substring for
      airtightness — the fallback exception is structurally unreachable, so no false-pass risk today.* **Test & QA.**

### Phase 4 — Validate + finalize (BEFORE the gates — Step-0b BLOCKER: gates land after ALL their debt)

- [x] **TASK-132-4 — Validate + finalize.** *DONE 2026-08-21.* `dotnet build StatsTid.sln` → **0 errors**
      across all 12 fixes together (139 warnings, all pre-existing incl. the payroll CS0618; +2 are CA2100 in
      new test files, not the payroll project). `docs/QUALITY.md` grades **revisited + FINALISED** (S132
      remediation re-grade added: Rule Engine C+→B, Payroll C+→B, Infrastructure B−→B, Domain-Correctness
      C+→B−, others held-with-fixes-noted) and **re-anchored to S132**. `dotnet test` = Docker-gated →
      confirmed in CI. Register-row flips recorded below (§S132 remediation).

### Phase 5 — Gates (land LAST — each only AFTER the debt it gates is fully cleared, OQ-5)

- [x] **TASK-132-5a — QUAL-141.** *DONE 2026-08-21 — LANDED.* `tools/check_docs.py` `check_freshness()` now
      routes a stale `docs/QUALITY.md` anchor to the HARD `failures` list (exit 1), not the soft `warnings`
      list (all other anchored docs stay soft). Closes the FAIL-006 refreeze class. QUALITY.md is fresh
      (re-anchored S132) so the gate is green now. **Runtime verification (gate fires on stale / passes on
      fresh) deferred to CI** — no Python on this VDI (known toolchain gap); logic verified by inspection.
- [~] **TASK-132-5b — QUAL-069 — DEFERRED (per OQ-5 condition).** The payroll warn-gate would re-enable
      `TreatWarningsAsErrors` for `StatsTid.Integrations.Payroll` (remove its `Directory.Build.props:48-58`
      opt-out), which requires the project's warning count to be CLEAN. It is NOT: the baseline **CS0618**
      (the legacy `PeriodCalculationService.CalculateAsync(EmploymentProfile,…)` overload the
      `/calculate-and-export` endpoint still calls) remains — retiring that overload is out of S132's
      fix-next scope. Per OQ-5, QUAL-069 **defers to the increment that clears the CS0618** (the
      legacy-overload retirement). Recorded as an S132 follow-up. No `Directory.Build.props` change made.

### Phase 6 — Post-gate validation + close

- [x] **TASK-132-6a — Re-validate the gates + close their rows.** *DONE 2026-08-21.* `dotnet build` re-run
      after the gate edits → **0 errors**. QUAL-141 landed (its runtime fire-on-stale check runs in CI — no
      Python here). QUAL-069 **deferred** (payroll CS0618) — its row NOT flipped, deferral recorded (Phase
      5b). Register rows flipped via the **§S132 remediation status** blocks in the QUAL register + the SEC
      register (efficient durable record of every fixed/deferred/routed row). QUALITY.md finalized + anchored
      S132.
- [~] **TASK-132-6b — Step-7a + close.** *Step-7a DONE 2026-08-21 — BOTH lenses CLEAN CLOSE (no code BLOCKER,
      no cycle-cap halt); artifacts at `.claude/reviews/SPRINT-132-step7a-{codex,reviewer}.md`.* Codex: co-edits
      compose coherently, 1×P2 ADR-039-staleness WARNING **ABSORBED** (D6/D8/Consequences reconciled to the
      input-boundary design + the D8 reversal). Reviewer: invariant integrity + co-edit composition + ADR
      fidelity + honest deferrals + evidence-cited re-grade all hold; WARNING (15 untracked files → **`git
      add -A` at commit**) + NOTEs (INDEX/note ratification, ADR-016 row-6 wording) **ABSORBED**.
      **⏳ COMMIT + PUSH: AWAITING OWNER GO-AHEAD** (commit/push is owner-gated). At commit: `git add -A` (the
      15 untracked incl. the 2 load-bearing new sources), then the close commit (the sprint-close-guard
      checks the Step-7a artifacts [written], CI-health, and the FAIL-003 untracked-source gate [cleared by
      `git add -A`]).

## Sequencing & dependencies

- **TASK-0 is DONE** (research ratified) and **ADR-039 is ACCEPTED** (2026-08-20), so TASK-1b is UNBLOCKED.
- **QUAL-001 cluster is a PIPELINE, not a flat list** (corrected after reading `RestPeriodRule.cs` — 1a's
  daily-rest instants and 1b-3's normalized consumption both edit the same rest-check logic and depend on
  the normalized representation):
  1. **1b-1 — projection normalization** (Data Model + Infra): the per-day split + continuity link in
     `time_entries_projection`, one shared writer+rebuild impl. Foundation — everything else consumes it.
  2. **1a + 1b-3 — Rule Engine** (SEQUENTIAL after 1b-1; both edit `RestPeriodRule.cs`): daily-rest on
     absolute instants over the CONTINUOUS stint (via the continuity link, so the split reads no false
     midnight gap); hours-checks consume the per-day rows; weekly-rest continuous. Do 1a+1b-3 as one
     coordinated Rule Engine change to avoid rework.
  3. **1b-4 — guard + tests** (Backend + Test & QA): the `/time` validator tightening + the on-boundary
     RED-on-old (divergent OK26 config) + QUAL-021/114 fixtures.
- **Phases 2/3 run in parallel** with the QUAL-001 pipeline (separate projects).
- **PCS is a contended file**: QUAL-002 (2b) and SEC-039 (3c-1) both edit `PeriodCalculationService.cs`
  (different regions — JsonOptions vs the QUAL-061 locus) → sequence within the Payroll agent or isolate.
  The S134 audit-linkage also touches PCS but is deferred (OQ-4), noted.
- **Gates land LAST (Phase 5), after Phase 4 finalises** the touched QUALITY.md grades (for QUAL-141) and
  after EVERY payroll edit incl. SEC-039 (for QUAL-069's clean-count condition); **Phase 6 then re-validates
  the gate changes themselves and closes their rows** (Step-0b cycle-2 BLOCKER).
- **Test & QA follows implementation (AGENTS.md:37).** RED-on-old tests (QUAL-021, QUAL-114) are authored
  against the baseline to fail first, then verified green once their fix lands (co-developed with the fix);
  the SEC-004 **confirming** test (TASK-3c-4) runs AFTER its implementation is in place — it is not
  RED-on-old and must not be sequenced before the code it confirms.
- **Worktree isolation** for agents writing the same project concurrently. Rule Engine, Payroll, Backend
  Endpoints, and Orchestrator are separate projects → cross-project parallelism is safe; contention is
  within-project (RestPeriodRule 1a/1b; PCS 2b/3c-1) → sequence or isolate those.

## Review posture

- **Step-0b (this plan)**: dual-lens, MANDATORY (triggers met). Cycle-1 findings absorbed into rev 2; see
  the Plan Review section. Cycle-2 re-verification is the gate before Step 1 (decompose).
- **Step-5a high-risk external override** fires per-task on: QUAL-001 (1a+1b, legal rule logic),
  QUAL-133/002/004 + SEC-039 (payroll/settlement export), QUAL-006 (payroll), and **SEC-040 (auth)**.
  Internal Reviewer on all substantive tasks; Constraint Validator on every output. (SEC-004 is a confirming
  test, exempt from the RED-on-old DoD.)
- **Step-7a**: dual-lens close on the full S132 diff. Cycle cap per WORKFLOW.md:38.
- **Codex on PATH** — verified this session (Step-0b/5a/7a external lens available).

## S132-discovered follow-ups (register as new QUAL/SEC rows at close)

Findings surfaced DURING S132 remediation (not in the original fix-next set) — to be registered + routed at
close, not silently dropped:
1. **Rule-eval swallow (QUAL-007 sibling)** — `WeeklyCalculationPipeline`'s `CallRuleEvaluateAsync` /
   `CallAbsenceEvaluateAsync` / `CallFlexEvaluateAsync` swallow a non-success RULE response to `null` while
   the pipeline still returns `Success=true` (same silent-failure class as QUAL-007, for rule-eval calls).
2. **Separately-registered adjacent intervals (QUAL-001 edge)** — two separately-registered adjacent per-day
   intervals (distinct/absent `SourceStintId`) are read as two stints → a false 0-hour midnight gap in the
   rest checks. Candidate: rejoin clock-adjacent zero-gap same-employee stints, or a one-crossing-entry input
   contract.
3. **Guard ⇄ caller-widen coupling (QUAL-001, go-live-coupled)** — the PCS `AssertNoDroppedBoundaryCrossing`
   loud guard must be reworked IN LOCKSTEP with the GAP-B payroll caller-widen (ADR-039 D5b), else the widen
   would make the guard throw on valid input. On the §15/go-live precondition.
4. **Un-normalized secondary rule-input boundaries (ADR-039 D6a exclusions)** — `WeeklyCalculationPipeline`
   + `TaskDispatcher` ship un-normalized entries to OK-version-sensitive calc rules; normalize-or-retire.
5. **Login timing side-channel (SEC-040 sibling)** — the DB-auth path returns for an unknown user BEFORE
   running BCrypt, but runs BCrypt on a wrong password → a response-timing user-enumeration oracle. (SEC-040
   closed the observability half; the timing half is separate.)
6. **`InvalidOperationException` detail echo (SEC-041 residual)** — the specific `InvalidOperationException`
   handler in the bulk import still returns `detail = ex.Message`; less controlled than the pure-domain
   exceptions. Ruling wanted on whether every raw-`ex.Message` echo is closed.
7. **App-wide username/identifier log-sanitization audit (SEC-040 sibling)** — Codex Step-5a flagged CR/LF
   log-injection via an unsanitized username VALUE at a plain-text sink; SEC-040 sanitizes it within
   `AuthEndpoints.cs`, but other files that log usernames/identifiers (per the internal Reviewer, the
   pattern pre-exists app-wide) should be audited for the same CR/LF sanitization. Add a shared sanitizer
   / structured-sink convention.
8. **Universal segment-manifest byte-identity (QUAL-002 residual)** — QUAL-002 restores DESERIALIZED
   equivalence (the real ADR-018 contract) between a live-written and a rebuilt manifest, but a null-snapshot
   segment still byte-DIFFERS (`PeriodCalculationService` writes `"snapshot":null`; `EventSerializer` omits
   it via `WhenWritingNull`). Benign (no consumer keys on segment bytes; the D10 join is `manifest_id`).
   Universal byte-identity would require adding `DefaultIgnoreCondition = WhenWritingNull` to PCS's SHARED
   `JsonOptions` — a WIRE-FORMAT change to the rule-engine HTTP payloads needing its own RED test; deferred.

## Legal & Payroll Verification (to complete at close)

| Dimension | S132 impact | Status |
|-----------|-------------|--------|
| Agreement rule compliance | QUAL-001 changes daily-rest (1a) + day-attribution (1b) outcomes | pending |
| Wage-type mapping correctness | untouched | n/a |
| Overtime/supplement determinism | 1a is confined (summed-hours unchanged, non-crossing inputs); **1b deliberately re-attributes daily-hours per the TASK-0 rule** — expectations updated, must not cross an OK-version boundary | pending (1a confinement AC + 1b attribution + OK-boundary AC) |
| Absence effect accuracy | untouched | n/a |
| Retroactive recalculation stability | QUAL-002 replay (both encodings) + QUAL-004 YEAR_END recovery | pending |
