# Phase B Domain-Expert Review — Handoff Package

> **Status**: READY FOR DISPATCH (S36 TASK-3609 produced; awaits expert engagement per PROGRAM L88–101).
> **Scope**: package the S36 Phase A inventory output (~111 cells across 5 agreements × 2 OK versions) for domain-expert sign-off + dispute adjudication.
> **Created**: 2026-05-21.
> **Target absorption sprint**: S37 (Phase A finalization).

## Purpose

This document is the **starting point for the domain-expert reviewer**. It points to the 3 S36-produced docs, indexes them by priority, provides a review-form template the expert fills in per cell, enumerates the 4 candidate bugs needing direction-confirmation, and documents the engagement protocol.

The expert's output drives S37 absorption (TASK-3700..3709 per PROGRAM L109–117).

---

## Package Index

| Doc | Purpose | What the expert reads first |
|-----|---------|-----------------------------|
| **[agreement-source-register.md](agreement-source-register.md)** | Per-cell source-of-truth with 15-column schema (row_id / agreement / OK / field / current_encoded_value / authoritative_source / interpretation / confidence_level / interpretation_authority / last_verified_by / decision_date / supersession_history / bug_correction_history / disputed? / notes). 111 cells total across AC + HK + PROSA + AC_RESEARCH + AC_TEACHING. | Sections "Source Citation Convention" + "Confidence Level Definitions" + the 5 per-agreement sections + Schema Validation summary. **Per-cell review happens in this doc.** |
| **[role-dimension-audit.md](role-dimension-audit.md)** | Within-OK role enumeration. Production-incorrectness call-out for AC chefkonsulent / kontorchef / specialkonsulent merarbejde entitlement loss. PositionOverrideConfigs 6-field schema gap analysis. Phase B sign-off table with 9 enumerated cells. | Per-agreement role sections + "Production-Incorrectness Call-Out". **Expert confirms role-level cirkulær framework.** |
| **[agreement-ruleset-audit.md](agreement-ruleset-audit.md)** | 3-column comparison (code | seed | source) with classification: MATCH / MATCH-PENDING-SOURCE / DRIFT-IN-CODE / DRIFT-IN-SEED / DRIFT-IN-SOURCE / DRIFT-UNCLEAR. Classification summary at top. Candidate Bug Routing Summary. | Classification Summary at top + 4 Candidate Bug sections + Routing Summary. **Expert confirms direction of each candidate bug.** |
| **[danish-agreements.md](danish-agreements.md)** | Human-readable summary (existing pre-S36 doc; S36 TASK-3608 added SR row references inline). | Optional context; the source register is authoritative going forward. Full rewrite deferred to S41 TASK-4106. |

---

## Priority Sequencing — Where Expert Attention Most Matters

The expert's time is the scarce resource. Cells are prioritised by impact:

### Tier 1 — HIGH PRIORITY (cells where direction determines bug status)

These cells **block S37 absorption** until adjudicated. Expert must confirm direction before S37 can write bug-correction commits.

| Priority | Cell(s) | Question for expert | Resolution path |
|----------|---------|---------------------|-----------------|
| **P1** | **AC chefkonsulent / kontorchef merarbejde entitlement loss** (role-dimension-audit.md headline finding) | Does AC overenskomst §X explicitly remove merarbejde entitlement from chefkonsulent? From kontorchef? From specialkonsulent? Cite paragraph. | S38 ADR-024 D2 tri-state `MerarbejdeCompensationRight: CONTRACTUAL / DISCRETIONARY / NONE` model design |
| **P2** | **AC_RESEARCH + AC_TEACHING missing entitlement_configs rows** (SR-AC_RESEARCH-OK24-005 + SR-AC_TEACHING-OK24-005; candidate bug #1) | Should AC_RESEARCH + AC_TEACHING employees have the same 5 entitlements (VACATION / SPECIAL_HOLIDAY / CARE_DAY / CHILD_SICK / SENIOR_DAY) as AC base? OR are there variant-specific quotas? | S37 seed correction: 20 new rows mirroring AC base, OR variant-specific rows |
| **P3** | **AC variants wage_type_mappings SLS code divergence** (SR-AC_RESEARCH-OK24-006 + SR-AC_TEACHING-OK24-006; candidate bug #2) | Are research/teaching staff payroll SLS codes intentionally distinct from AC base? If yes, the MERARBEJDE→SLS_0210 collision with HK/PROSA OVERTIME_50 is still a bug — how does SLS-side disambiguate? | S37 (if S11 seed bug — mirror AC base) OR S38 ADR-024 D6 SLS reconciliation |
| **P4** | **SENIOR_DAY paired-bug** (SR-AC-OK24-015 + SR-AC-OK24-035 + SR-HK-OK24-029 + SR-PROSA-OK24-006; candidate bug #3) | Is `annual_quota = 0` + `min_age = 60` intended as "rule-engine computes age-banded quota at runtime" OR "cells should have non-zero quotas seeded"? Cite cirkulær on senior-day grant structure. | Resolution determines whether fix is code-side (rule logic) or seed-side (quota values) |

### Tier 2 — MEDIUM-HIGH (cells where confirmation closes MATCH classification)

| Priority | Cell | Question for expert | Resolution path |
|----------|------|---------------------|-----------------|
| **P5** | **HK + PROSA `OvertimeRequiresPreApproval = false`** (SR-HK-OK24-022 + SR-PROSA-OK24-007; candidate bug #4) | Do HK / PROSA cirkulærer require manager pre-approval for non-emergency overtime? | S37 if confirmed cirkulær-mandated (flip seed + code to TRUE); else MATCH stays |
| **P6** | **AC `MaxFlexBalance = 150h` baseline** (SR-AC-OK24-011) | Cite AC cirkulær paragraph on flex ceiling. Confirm 150h is the central baseline (institutional override permitted per ADR-017 glocal principle). | MATCH-PENDING-SOURCE → MATCH on confirmation |
| **P6** | **HK `MaxFlexBalance = 100h` baseline** (SR-HK-OK24-002) | Cite HK cirkulær paragraph on flex ceiling. | Same |
| **P6** | **PROSA `MaxFlexBalance = 120h` baseline** (SR-PROSA-OK24-002) | Cite PROSA cirkulær paragraph on flex ceiling. | Same |
| **P7** | **AC_TEACHING `AnnualNormHours = 1680h`** (SR-AC_TEACHING-OK24-003) | Cite cirkulær paragraph on teaching-staff norm reduction (1680h vs 1924h for research). | MATCH-PENDING-SOURCE → MATCH on confirmation |
| **P8** | **Position override DEPARTMENT_HEAD (200h flex + 4-week norm)** (SR-AC-OK24-038) | Cite AC cirkulær on kontorchef working-time arrangement. Confirm 200h flex cap + 4-week norm is central baseline (per ADR-017 glocal) vs institutional-local. | MATCH-PENDING-SOURCE → MATCH on confirmation |

### Tier 3 — STANDARD (Phase B verification rotates through these — lower urgency)

Approximately 80 cells classified MATCH-PENDING-SOURCE. Most carry MEDIUM confidence. The expert sign-off pattern is:

1. Read the cell's `current_encoded_value` + `interpretation` + `notes` in source register
2. Verify against cirkulær wording
3. Fill `last_verified_by` + `decision_date` + (if updating) `authoritative_source` + `confidence_level`
4. Mark `disputed? = true` if cirkulær wording is ambiguous or parties disagree

These cells flip MATCH-PENDING-SOURCE → MATCH on confirmation. No bug-correction work expected — encoded values are believed correct, just lack explicit cirkulær-paragraph cite today.

### Tier 4 — DRIFT-UNCLEAR (~5 cells)

Cells where source confidence is too LOW for the inventory to classify without expert input. Expert determines classification + resolution path.

| Cell | Question for expert |
|------|---------------------|
| AC_TEACHING within-stratum compensation distinctions | Do underviser / adjunkt / lektor have different compensation rules within AC_TEACHING? |
| TEACHING_STAFF position code purpose (vs AC_TEACHING agreement) | When does AC + TEACHING_STAFF position apply vs AC_TEACHING agreement? Is one path obsolete? |
| AC + RESEARCHER position vs AC_RESEARCH agreement | Same kind of question for research-side |
| HK / PROSA missing position codes | Should HK-leder / PROSA-leder positions exist? |
| `min_age` field generalization | Should other entitlements (e.g., maternity-related) use age-band logic? |

---

## Candidate Domain-Expert Profile

Per PROGRAM L95–96 (engagement options identified during S35 close week):

| Profile | Strengths | Likely fit for | Engagement cost |
|---------|-----------|------------------|------------------|
| **Internal customer HR/payroll lead** | Deep agreement familiarity from operational practice; knows where cirkulærer get interpreted in day-to-day work | Tier 1 + Tier 2 sign-off (operational reality) | Low if customer is launch partner; medium otherwise |
| **Akademikerne / DM consultant** | Authoritative on AC + AC_RESEARCH + AC_TEACHING cirkulærer; well-positioned for AC chefkonsulent finding | AC-family cells across all 4 tiers; especially P1 + P2 + P3 | Medium-high — consultants charge per hour |
| **HK union consultant** | Authoritative on HK cirkulær | HK-family cells (Tier 2 P6 + P7; Tier 1 P5 for HK direction) | Medium |
| **PROSA union consultant** | Authoritative on PROSA cirkulær | PROSA cells | Medium |
| **Retired Personalestyrelsen / Medst consultant** | Employer-side authoritative; ROADMAP defaults to employer-side interpretation (rule correction policy) | Cross-agreement sign-off + dispute adjudication where cirkulær ambiguous | Medium-high |
| **External legal counsel (state-sector employment law)** | Independent third-party; lowest dispute risk | Last-resort adjudication if union + employer consultants disagree | High |

**Recommended engagement model** (per PROGRAM L101 mitigation):

1. **Primary reviewer**: customer HR/payroll lead (operational view; sign-off rate ~80% of cells with HIGH confidence)
2. **Secondary reviewer (parallel)**: union consultant per agreement (cirkulær authoritative cite for the ~20% cells customer can't confidently close)
3. **Dispute resolution**: Personalestyrelsen-side default per ROADMAP commitment (rule correction policy + ADR-024 D5); documented in source register `disputed?` + `notes` field when invoked

Engagement starts **week 2 of S36** (per PROGRAM L101) — when source register has draft tables for AC OK24 (TASK-3601 close). By TASK-3609 close (this commit), all 5 agreements × both OK versions are populated and ready for expert iteration.

---

## Review Form Template (per cell)

For each cell the expert reviews, the per-cell return form:

```
Cell row_id: SR-XXX-OKXX-NNN

Section A — Verification
  [ ] Agree: encoded value matches cirkulær — sign off
  [ ] Disagree: value should be different (provide corrected value below)
  [ ] Disputed: cirkulær wording ambiguous OR parties disagree

Section B — Source citation
  Cirkulær URL: ___________________________
  Paragraph: §___
  Confidence: HIGH / MEDIUM / LOW
  Authority: Personalestyrelsen / Akademikerne / HK union / PROSA union / EU / negotiated / contested

Section C — If disagreeing or correcting
  Corrected value: ___________________________
  Rationale: ___________________________
  Classification (rule correction policy):
    [ ] Bug — encoding never matched the agreed interpretation
        (sub-class):
          [ ] Bug-with-no-past-impact (pre-launch posture)
          [ ] Bug-with-past-impact (requires retroactive recompute; ADR-027 post-launch)
    [ ] Supersession — parties agree on new reading (NOT applicable pre-launch since no past periods)
    [ ] Interpretation change required (cirkulær itself needs amendment; out-of-scope for StatsTid)

Section D — Identification
  Reviewed by: ___________________________
  Date: YYYY-MM-DD
  Affiliation: ___________________________
```

**Bulk-review shortcut for MATCH cells**: per-section blanket confirmation acceptable when the expert is signing off a contiguous block of cells with the same source citation (e.g., all 5 EU-derived rest/max-daily cells share the same EU WTD Article 3 cite). Source register `last_verified_by` + `decision_date` apply per cell but rationale can be `"see blanket confirmation under cells SR-XXX-OKXX-NNN..MMM"`.

---

## Candidate Bug Enumeration (Tier 1 expanded)

These are the 4 candidate bugs from `agreement-ruleset-audit.md` § Candidate Bug Routing Summary. Each requires explicit Phase B direction.

### Candidate Bug 1 — AC variants missing entitlement_configs rows

**Affected SR rows**: SR-AC_RESEARCH-OK24-005 + SR-AC_TEACHING-OK24-005 (+ OK26 inheritance)

**Symptom**: init.sql:1343–1378 seeds entitlement rows for AC + HK + PROSA only. AC_RESEARCH + AC_TEACHING employees have **no** VACATION / SPECIAL_HOLIDAY / CARE_DAY / CHILD_SICK / SENIOR_DAY rows. Quota lookups return zero rows.

**Expert decision needed**: confirm AC variants should inherit AC base values verbatim, OR have variant-specific quotas.

**S37 absorption path** (if verbatim inheritance confirmed): 20 new rows in init.sql mirroring AC base values (5 entitlements × 2 OK × 2 variants).

**Severity**: HIGH — Ferieloven applies universally so VACATION absence is most immediately concerning.

### Candidate Bug 2 — AC variants wage_type_mappings SLS code divergence

**Affected SR rows**: SR-AC_RESEARCH-OK24-006 + SR-AC_TEACHING-OK24-006

**Symptom**: 6 of 11 wage type mappings for AC variants use different SLS codes from AC base:
- `MERARBEJDE → SLS_0210` (vs AC's `SLS_0310`) — **collides with HK/PROSA `OVERTIME_50 → SLS_0210`**
- `CARE_DAY → SLS_0550` (vs AC's `SLS_0520`)
- `SENIOR_DAY → SLS_0570` (vs AC's `SLS_0550`)
- `LEAVE_WITH_PAY → SLS_0580` (vs AC's `SLS_0565`)
- `LEAVE_WITHOUT_PAY → SLS_0590` (vs AC's `SLS_0560`)
- `CHILD_SICK_1 → SLS_0560` (time_type renamed from AC's `CHILD_SICK_DAY` + single mapping vs AC's 3-day chain)

**Expert decision needed**:
1. Are research/teaching staff payroll codes intentionally distinct from AC base?
2. If yes, how does SLS-side disambiguate AC_RESEARCH MERARBEJDE (SLS_0210) from HK OVERTIME_50 (SLS_0210)?
3. The time_type rename `CHILD_SICK_DAY` → `CHILD_SICK_1` — is this an intentional research-staff distinction (single sick day per episode) OR an S11 authoring inconsistency?

**S37 absorption path** (if S11 seed bug — mirror AC base): mechanical seed correction.
**S38 absorption path** (if intentional separate code-block + needs SLS reconciliation): ADR-024 D6 design.

**Severity**: HIGH — AC variant payroll may not flow at all if rule-emitted time_types don't match seed mappings.

### Candidate Bug 3 — SENIOR_DAY paired-bug

**Affected SR rows**: SR-AC-OK24-015 + SR-AC-OK24-035 + SR-HK-OK24-029 + SR-PROSA-OK24-006 (+ AC variant inheritance via SR-AC_RESEARCH-OK24-008 paired with bug 1 resolution)

**Symptom**: `entitlement_configs.SENIOR_DAY.annual_quota = 0` + `min_age = 60`. Structurally inconsistent: the age-gate field is populated but never grants any days because quota is zero. Same encoding across all 3 base agreements (AC + HK + PROSA per init.sql:1373–1378).

**Expert decision needed**: confirm intended encoding semantic.
- Path A: rule-engine code should override with age-banded quota lookup (e.g., 1 day at 60–61, 2 days at 62–63, ...) — bug is code-side missing logic. Cite cirkulær on age-band structure.
- Path B: seed `quota` should be non-zero default (e.g., quota=2 at age 60+); rule reads quota directly. Bug is seed-side.
- Path C: cirkulær itself is ambiguous on age-band structure; defer to interpretation authority (Personalestyrelsen default per ROADMAP).

**S37 + S39 + potentially S40 absorption** depending on path selected.

**Severity**: HIGH — senior-employee compensation correctness; affects all base agreements + 2 variants (5 of 5 total).

### Candidate Bug 4 — HK / PROSA OvertimeRequiresPreApproval

**Affected SR rows**: SR-HK-OK24-022 + SR-PROSA-OK24-007

**Symptom**: `OvertimeRequiresPreApproval = false` for HK + PROSA at agreement level. For their real overtime regimes (HK + PROSA have `HasOvertime = true`), pre-approval IS a governance question. The seed default (column DEFAULT FALSE) carried through S17 without per-agreement consideration.

**Expert decision needed**: do HK + PROSA cirkulærer require manager pre-approval for non-emergency overtime?
- If YES: bug-with-no-past-impact correction in S37 (flip seed + code to TRUE for both).
- If NO: current `false` is correct; flips MATCH-PENDING-SOURCE → MATCH on confirmation.

**Severity**: MEDIUM-HIGH — workflow gate affecting all HK + PROSA overtime registration going forward.

---

## Engagement Protocol

Per PROGRAM L94–99:

1. **Identification** — candidate expert(s) identified during S35 close week (already done; see Candidate Domain-Expert Profile above).
2. **Package handoff** — this doc + 3 reference docs delivered to expert (PDF or web-viewable form acceptable; markdown source on github.com works too).
3. **Review cycle** — expert iterates through cells using review-form template; flags disputed + corrects misinterpretations + signs off uncontested cells. Tier 1 cells (4 candidate bugs) first; Tier 2 next; Tier 3 + Tier 4 rotation.
4. **Findings absorption** — S37 sprint open (TASK-3700..3709) absorbs expert feedback into source register + bug correction commits per ROADMAP rule correction policy classification.
5. **Sign-off marking** — each cell that closes gets `last_verified_by` (expert name) + `decision_date` (YYYY-MM-DD) populated in source register row.

**Cadence**:
- Week 2 of S36: dispatch (TASK-3609 close)
- Weeks 2–6 of S36–S37: expert review (~4 weeks of expert iteration; cadence depends on expert availability)
- S37 sprint open: absorption begins on the cells expert has signed off; remaining cells continue in parallel
- S37 close: source register status → APPROVED (per PROGRAM L114)

**Dispute resolution**:
- If union + employer reviewers disagree on a cell, default to Personalestyrelsen / Medst employer-side interpretation per ROADMAP commitment (rule correction policy + ADR-024 D5 — interpretation authority).
- Disputed cells get `disputed? = true` in source register + the dispute summary in `notes`.
- ROADMAP commits to "shipping cadence on disputed cells couples to negotiation cadence" — sprint dispatch blocks on disputed cells affecting Tier 1 candidate bugs.

---

## Tracking — Phase B Status Dashboard

To be updated per S37 sprint progress:

| Field | Value |
|-------|-------|
| Expert(s) selected | _pending — fill in S35 close / S36 week 2_ |
| Engagement start date | _pending_ |
| Tier 1 candidate bugs adjudicated | 0 of 4 |
| Tier 2 cells confirmed (MATCH-PENDING-SOURCE → MATCH) | 0 of ~80 |
| Tier 3 cells confirmed | 0 of ~5 DRIFT-UNCLEAR |
| Disputed cells recorded | 0 |
| Source register status | DRAFT (S36 close) — target APPROVED at S37 close (per PROGRAM L114) |

---

## What Happens Next (S37)

Per PROGRAM L109–117 + ROADMAP rule correction policy:

1. **TASK-3700** — S37 sprint open with PLAN-s37.md.
2. **TASK-3701..3705** — per-agreement absorption of expert feedback (AC + HK + PROSA + AC_RESEARCH + AC_TEACHING). Each task absorbs the expert sign-offs for one agreement; updates source register cells with `last_verified_by` + `decision_date` + (if updating) `authoritative_source` + `confidence_level`; flips classification (MATCH-PENDING-SOURCE → MATCH; etc.).
3. **TASK-3706** — Resolve disputed cells per default-employer-side policy (ADR-024 D5 provisional application).
4. **TASK-3707** — Document newly-discovered bugs (provisional classification per ROADMAP rule correction policy; ADR-024 D4 will formalize governance).
5. **TASK-3708** — Source register status → APPROVED.
6. **TASK-3709** — S37 sprint close + Phase C (S38 ADR authorship) handoff.

**Bug correction commits during S37**: if expert confirms candidate bug direction, the bug-with-no-past-impact correction ships as a seed-edit commit during S37. May result in N supplementary commits beyond the headline source-register work.

**Spillover handling**: if S37 absorbs more bugs than expected, spillover-bugs file for S38 ADR-024 D4 classification deferral (per PROGRAM L274 risk mitigation).
