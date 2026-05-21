# Agreement Ruleset Audit (Code vs Seed vs Source)

> **Status**: DRAFT (S36 TASK-3607 produced; awaits Phase B sign-off on `pending` source citations + S37 absorption of DRIFT-IN-SEED candidates).
> **Scope**: per-cell 3-column comparison across all 5 agreement codes (AC / HK / PROSA / AC_RESEARCH / AC_TEACHING) × both OK versions.
> **Created**: 2026-05-21.

## Purpose

Audit the consistency of every active rule-engine cell across **three sources of truth**:

- **Column A** — `src/SharedKernel/StatsTid.SharedKernel/Config/CentralAgreementConfigs.cs` (the in-code default applied when DB seed is unavailable)
- **Column B** — `docker/postgres/init.sql` seed (the DB-backed value loaded at startup)
- **Column C** — authoritative cirkulær source per `docs/references/agreement-source-register.md` (the legally-binding value)

Per-cell classification per ROADMAP rule correction policy (committed 2026-05-18) + PROGRAM L51–84:

| Class | Meaning | Resolution path |
|-------|---------|-----------------|
| **MATCH** | A = B = C; source HIGH-confidence | None needed — cell verified consistent |
| **MATCH-PENDING-SOURCE** | A = B; C = pending Phase B verification (MEDIUM / LOW confidence) | Phase B sign-off; expected to flip MATCH on confirmation |
| **DRIFT-IN-CODE** | A ≠ B; B = C (or C = pending); rule-engine default disagrees with seed | Code-side correction (bug-with-no-past-impact pre-launch); update `CentralAgreementConfigs.cs` to match seed |
| **DRIFT-IN-SEED** | A = B; C confirmed different (B ≠ C); seed disagrees with cirkulær | Seed correction (bug-with-no-past-impact pre-launch per ROADMAP rule correction policy); S37 or S39 fix |
| **DRIFT-IN-SOURCE** | A = B; C ambiguous, contested, or structurally inconsistent | Phase B HIGH priority — adjudicate direction OR escalate to ADR-024 D5 interpretation authority |
| **DRIFT-UNCLEAR** | LOW-confidence cell; insufficient information to classify | Phase B verifies + reclassifies |

## Classification Summary (provisional, pending Phase B)

| Class | Cell count (provisional) | Notes |
|-------|--------------------------|-------|
| MATCH | ~25 | EU-derived cells (rest, max-daily, ref-period) + Ferieloven VACATION + universal state-sector norm (37h) where source HIGH-confidence |
| MATCH-PENDING-SOURCE | ~80 | Dominant case — code = seed, but cirkulær-paragraph cite pending Phase B; expected MATCH on confirmation |
| DRIFT-IN-CODE | **0** | No A≠B cases detected in current code/seed comparison — they are maintained in parallel and audit found byte-equivalence on all overlap |
| DRIFT-IN-SEED | **2 candidate** (1 RESOLVED + 1 active) | RESOLVED: S35 AC=AFSPADSERING. Active: AC variants missing entitlement_configs rows (#1). |
| DRIFT-IN-SOURCE | **2 candidate** | HK / PROSA OvertimeRequiresPreApproval=false (#4 — Phase B direction); SENIOR_DAY paired-bug (#3 — could land DRIFT-IN-CODE / DRIFT-IN-SEED / DRIFT-IN-SOURCE per Phase B path selection). |
| DRIFT-UNCLEAR | ~6 | Cells where source confidence is too LOW to classify without Phase B, plus candidate-bug #2 (AC variants SLS divergence — direction-dependent: DRIFT-IN-SEED if S11 authoring bug, DRIFT-IN-SOURCE if intentional separate research-code-block). |
| **Total cells audited** | **~111** | Matches source register cell count post-TASK-3605 |

**Net pre-launch fix candidates from this audit**: **4 candidate bugs requiring DRIFT-IN-SEED or DRIFT-IN-SOURCE correction** (see § Candidate Bug Routing below). Pre-launch posture means all four ship as free seed/code corrections per ROADMAP rule correction policy.

---

## Section 1: agreement_configs Cell Comparison

For each agreement × OK × cell:

### MATCH cells (HIGH source confidence — no Phase B needed)

These cells share `A = B = C` with HIGH source confidence per the source register. EU-derived + Ferieloven cells:

| Cell | Code value | Seed value | Source | SR row |
|------|-----------|-----------|--------|--------|
| `MinimumRestHours` (all 5 agreements × both OK) | `11.0m` | `11.0` | EU WTD Art 3 + Lov om arbejdstid | SR-AC-OK24-003 + cross-refs |
| `MaxDailyHours` (all 5 × both OK) | `13.0m` | `13.0` | EU WTD Art 3 derived | SR-AC-OK24-018 + cross-refs |
| `WeeklyMaxHoursReferencePeriod` (all 5 × both OK) | `17` | `17` | EU WTD Art 16 | SR-AC-OK24-004 + cross-refs |
| `WeeklyNormHours` (all 5 × both OK) | `37.0m` | `37.0` | Personalestyrelsen 37h universal state-sector | SR-AC-OK24-001 + cross-refs |
| `entitlement_configs.VACATION.annual_quota` (AC + HK + PROSA) | n/a (DB-only) | `25` | Ferieloven LBK 230 §8 | SR-AC-OK24-013 + cross-refs |
| `DefaultCompensationModel` (AC family POST-S35) | `"AFSPADSERING"` | `'AFSPADSERING'` | AC cirkulær 043-19 §4 (S35-verified) | SR-AC-OK24-005 + variants |

**Subtotal**: ~25 cells across all agreements ✓ MATCH

### MATCH-PENDING-SOURCE cells (A = B; source pending Phase B)

These cells have code = seed byte-equivalence + MEDIUM confidence on intended value. Expected to flip MATCH on Phase B confirmation. Dominant case.

| Cell area | Representative cells | Agreements affected | Pending Phase B |
|-----------|---------------------|---------------------|------------------|
| Flex caps | `MaxFlexBalance`, `FlexCarryoverMax` | All 5 (AC=150, HK=100, PROSA=120, AC variants=150) | Confirm AC=150h cirkulær cite + HK=100h + PROSA=120h |
| Overtime tiers | `OvertimeThreshold50/100` | HK + PROSA load-bearing | Confirm 37h/40h tier paragraphs |
| Supplement rates | `EveningRate=1.25`, `NightRate=1.50`, `WeekendSaturdayRate=1.50`, `WeekendSundayRate=2.0`, `HolidayRate=2.0` | HK + PROSA load-bearing; AC family inert | Confirm rate paragraphs for HK + PROSA |
| Supplement windows | `EveningStart/End=17/23`, `NightStart/End=23/6` | HK + PROSA load-bearing; AC family inert | Confirm hour-boundary paragraphs |
| On-call | `OnCallDutyRate=0.33` | HK + PROSA load-bearing; AC inert (unless local override) | Confirm 33% rådighedsvagt rate |
| Call-in | `CallInMinimumHours=3.0`, `CallInRate=1.0` | HK + PROSA load-bearing | Confirm 3-hour minimum guarantee |
| Travel | `WorkingTravelRate=1.0`, `NonWorkingTravelRate=0.5` | All 3 active agreements (AC, HK, PROSA) | Confirm in-hours full + out-of-hours half rate paragraphs |
| AC variants norm | `NormModel=ANNUAL_ACTIVITY`, `AnnualNormHours=1924/1680` | AC_RESEARCH + AC_TEACHING load-bearing | Confirm AC research provisions + 1680h teaching reduction paragraphs |
| Entitlement quotas | `SPECIAL_HOLIDAY.annual_quota=5`, `CARE_DAY.annual_quota=2`, `CHILD_SICK.annual_quota=1/2/3` | AC + HK + PROSA (per agreement variation) | Confirm cirkulær-paragraph for each |

**Subtotal**: ~80 cells across all agreements ⏳ MATCH-PENDING-SOURCE

### DRIFT-IN-SEED — RESOLVED

| Cell | Original code value | Original seed value | Source | Correction commit |
|------|---------------------|---------------------|--------|-------------------|
| `DefaultCompensationModel` (AC family pre-S35) | `"UDBETALING"` (inherited from `AgreementRuleConfig.cs:67` default) | `'UDBETALING'` for AC + AC_RESEARCH + AC_TEACHING (6 rows total) | AC cirkulær 043-19 §4: "AFSPADSERING" | `cbaea7d` (S35 TASK-3503; classifier Orchestrator/Claude; date 2026-05-18) |

**S35 TASK-3503 RESOLVED DRIFT-IN-SEED**: pre-launch `bug-with-no-past-impact` per ROADMAP rule correction policy first concrete application. Code (`CentralAgreementConfigs.cs` AC + AC_RESEARCH + AC_TEACHING entries) + seed (init.sql 6 rows) updated together. DB column DEFAULT kept as `'UDBETALING'` per Step 0b Reviewer NOTE — documentary legacy fallback never fires. Post-S35: AC family now MATCH (with HIGH source confidence).

### DRIFT-IN-SEED — CANDIDATE 1: AC variants missing entitlement_configs rows

| Cell | Code value | Seed value | Source (expected) | Resolution |
|------|-----------|-----------|-------------------|------------|
| `entitlement_configs.VACATION` (AC_RESEARCH + AC_TEACHING × both OK) | n/a (DB-only) | **NO ROW** (init.sql:1343–1378 seeds only AC + HK + PROSA) | Ferieloven applies universally → expected rows mirroring AC base values (25 / IMMEDIATE / 9 / 5 / true / false) | S37 absorbs seed correction (add 4 rows: AC_RESEARCH OK24+OK26 + AC_TEACHING OK24+OK26 for VACATION); or S39 schema migration if Phase B prefers structural change |
| `entitlement_configs.SPECIAL_HOLIDAY` (AC variants × both OK) | n/a | NO ROW | Expected mirror of AC base | Same as VACATION — 4 more rows |
| `entitlement_configs.CARE_DAY` (AC variants × both OK) | n/a | NO ROW | Expected mirror of AC base | Same — 4 more rows |
| `entitlement_configs.CHILD_SICK` (AC variants × both OK) | n/a | NO ROW | Expected mirror of AC base (1 day per episode) | Same — 4 more rows |
| `entitlement_configs.SENIOR_DAY` (AC variants × both OK) | n/a | NO ROW | Expected mirror of AC base (`quota=0`, `min_age=60` — same paired-bug) | Same — 4 more rows; bug correction (if classified per #4) cascades |

**Cumulative**: 20 missing entitlement rows for AC variants × OK24+OK26. **DRIFT-IN-SEED — Phase B HIGH priority**. Resolution path: S37 commit adds 20 entitlement rows to init.sql mirroring AC base values. Pre-launch posture means no past periods to recompute; classifier = Orchestrator pending Phase B confirmation.

**Phase B clarification needed**: confirm AC variants should inherit AC base entitlements verbatim, OR have variant-specific values (e.g., AC_RESEARCH PhD students may have different VACATION accrual than salaried AC staff). If verbatim inheritance is correct, the seed correction is mechanical.

### DRIFT-UNCLEAR — CANDIDATE 2: AC variants wage_type_mappings SLS code divergence

> **Classification note (revised post-Step-7a)**: this candidate is classified **DRIFT-UNCLEAR pending Phase B direction**, not DRIFT-IN-SEED. Per ROADMAP rule correction policy binary framework (was-agreed × materially-wrong), a finding cannot be classified as "bug" until direction adjudication. The S37 routing path depends on which way Phase B confirms (S11 seed authoring bug → DRIFT-IN-SEED with mechanical seed correction; OR intentional separate research-payroll-code-block → DRIFT-IN-SOURCE with SLS reconciliation pattern). The SLS_0210 collision sub-issue (below) is bug-grade regardless of direction.

| Cell (AC variant) | Seed SLS code | AC base SLS code | Divergence type |
|-------------------|---------------|------------------|-----------------|
| `MERARBEJDE` | `SLS_0210` | `SLS_0310` | **Code collision** with HK/PROSA OVERTIME_50 |
| `CARE_DAY` | `SLS_0550` | `SLS_0520` | AC variants use AC base's SENIOR_DAY code |
| `SENIOR_DAY` | `SLS_0570` | `SLS_0550` | AC variants use AC base's SPECIAL_HOLIDAY_ALLOWANCE code |
| `LEAVE_WITH_PAY` | `SLS_0580` | `SLS_0565` | New code not in AC base mapping table |
| `LEAVE_WITHOUT_PAY` | `SLS_0590` | `SLS_0560` | New code not in AC base mapping table |
| `CHILD_SICK_1` (time_type renamed from `CHILD_SICK_DAY`) | `SLS_0560` | `SLS_0530/0531/0532` (3-day chain) | Time_type rename + single mapping vs AC base's 3-day chain |

**Cumulative**: 6 of 11 wage_type_mappings rows diverge from AC base for AC_RESEARCH + AC_TEACHING × OK24+OK26. **DRIFT-UNCLEAR pending Phase B direction — HIGH priority**.

**Resolution paths** (mutually exclusive):

1. **Intentional separate payroll-code-block for research staff** — if Phase B confirms research/teaching payroll uses a distinct SLS code-block (which would need to be sourced from SLS technical documentation), THE SLS_0210 / 0310 collision IS still a bug because the SLS-side cannot distinguish AC_RESEARCH MERARBEJDE from HK OVERTIME_50.
2. **S11 seed authoring bug** — if Phase B confirms research/teaching SLS codes should mirror AC base, all 6 mismatches are S37 seed corrections (rename time_types + remap codes).

**Additional concern**: rule-engine code emits time_type values that map to wage codes via this table. If the rule engine emits `MERARBEJDE` for an AC_RESEARCH employee, the seed lookup finds `SLS_0210` (not `SLS_0310`) — meaning AC_RESEARCH merarbejde ships under the HK/PROSA OVERTIME_50 SLS code in payroll. SLS-side payroll downstream may flag the mismatch OR silently process the wrong rate. **Investigate before launch**.

### DRIFT-IN-SOURCE — CANDIDATE 3: SENIOR_DAY paired-bug

| Cell | Code value | Seed value | Source (current) | Structural concern |
|------|-----------|-----------|-------------------|---------------------|
| `entitlement_configs.SENIOR_DAY.annual_quota` (AC + HK + PROSA × both OK) | n/a | `0` | pending — likely cirkulær establishes age-banded grants (e.g., 1 day at 60-61, 2 days at 62+) | `quota=0` paired with `min_age=60` is structurally inconsistent: the age-gate field is populated but never grants any days because quota is zero. Either rule-engine override (read min_age, compute banded quota at runtime) OR vestigial min_age field. |

**Cumulative**: 6 rows (AC + HK + PROSA × both OK) all with same structural inconsistency. If AC variants get entitlement rows added per CANDIDATE 1, the count grows to 10 rows.

**Resolution paths**:

1. **DRIFT-IN-CODE**: rule-engine code is missing age-banded logic; fix is code-side (add `EntitlementBalanceRule` logic that consults `min_age` + employee birth date to compute effective quota). Seed `quota=0` is correct as a "no fixed quota; consult age band" sentinel.
2. **DRIFT-IN-SEED**: seed `quota` should be non-zero with a default value (e.g., `quota=2` at age 60+); rule-engine reads quota directly.
3. **DRIFT-IN-SOURCE**: cirkulær itself is ambiguous on the age-band structure; Phase B should adjudicate the intended encoding.

**Phase B HIGH priority** — adjudicate which resolution path applies.

### DRIFT-IN-SOURCE — CANDIDATE 4: HK / PROSA OvertimeRequiresPreApproval

| Cell | Code value | Seed value | Source (current) | Concern |
|------|-----------|-----------|-------------------|---------|
| `OvertimeRequiresPreApproval` (HK + PROSA × both OK) | not set in code; inherits `AgreementRuleConfig.cs:70` default `false` | `FALSE` (init.sql column DEFAULT carried through) | pending — Phase B should verify HK / PROSA cirkulær on pre-approval requirement for non-emergency overtime | Seed default carried through S17 without per-agreement consideration. For HK / PROSA's load-bearing overtime regime, pre-approval IS a governance question; current `false` may invert cirkulær intent. |

**Cumulative**: 4 cells (HK OK24 + OK26 + PROSA OK24 + OK26).

**Resolution paths**:

1. **DRIFT-IN-SEED**: if HK / PROSA cirkulær requires pre-approval, seed should be `TRUE`; correction is code + seed update.
2. **DRIFT-IN-SOURCE**: cirkulær wording may be silent on pre-approval (modern workflow-process concept may post-date cirkulær publication); Phase B adjudicates via interpretation authority (ROADMAP commitment: default Personalestyrelsen).
3. **MATCH-PENDING-SOURCE → MATCH**: if Phase B confirms no cirkulær-mandated pre-approval requirement, current `false` is correct.

**Phase B MEDIUM-HIGH priority** — affects all HK + PROSA overtime registrations going forward.

### DRIFT-UNCLEAR cells (Phase B verification + reclassification)

| Cell area | Examples | Phase B clarification needed |
|-----------|----------|-------------------------------|
| `AC_TEACHING` within-stratum compensation | Lektor (teaching-track) vs Lektor (research-track) hybrid encoding | Phase B clarifies whether 1680h reduced norm is the only encoding-level distinction or if internal strata need separate cells |
| `TEACHING_STAFF` position code purpose | init.sql:957 seeds the position but no override row exists | Phase B clarifies use-case split between AC + TEACHING_STAFF position vs AC_TEACHING agreement code |
| AC `Position = RESEARCHER` vs `agreement_code = AC_RESEARCH` | Distinct paths to "researcher flexibility"; SR-AC-OK24-039 + SR-AC_RESEARCH bundles | Phase B clarifies intended use-case split |
| HK / PROSA missing position codes | No HK or PROSA positions seeded at all | Phase B clarifies whether HK / PROSA have within-agreement strata needing encoding (manager-classification gap from role-dimension-audit.md) |
| `min_age` field in entitlement_configs | Currently populated only for SENIOR_DAY (=60) | Phase B clarifies if other entitlements (e.g., maternity-related) should leverage age-band logic |

**Subtotal**: ~5 cells flagged DRIFT-UNCLEAR. All resolve via Phase B + S37 absorption.

---

## Section 2: entitlement_configs Cell Comparison

Cross-table comparison — code values N/A (entitlements are DB-only since S15; no `CentralEntitlementConfigs.cs` equivalent). Comparison is **seed vs source** only.

| Cell | Seed | Source | Class | SR row |
|------|------|--------|-------|--------|
| `VACATION.annual_quota` (AC, HK, PROSA × both OK) | `25` | Ferieloven §8 | MATCH | SR-AC-OK24-013 + cross |
| `VACATION.accrual_model` (same) | `'IMMEDIATE'` | Ferieloven (state-sector convention) | MATCH | SR-AC-OK24-032 + cross |
| `VACATION.reset_month` (same) | `9` | Ferieloven §15 (September 1 ferieår start) | MATCH | SR-AC-OK24-032 + cross |
| `VACATION.carryover_max` (same) | `5` | Ferieloven §15 (max 5 days carryover) | MATCH | SR-AC-OK24-032 + cross |
| `VACATION.pro_rate_by_part_time` (same) | `true` | State-sector convention | MATCH-PENDING-SOURCE | SR-AC-OK24-032 + cross |
| `SPECIAL_HOLIDAY.*` (AC, HK, PROSA × both OK) | `5 / IMMEDIATE / 9 / 0 / true / false` | pending | MATCH-PENDING-SOURCE | SR-AC-OK24-031 + 036 + cross |
| `CARE_DAY.*` (AC, HK, PROSA × both OK) | `2 / IMMEDIATE / 1 / 0 / false / false` | pending | MATCH-PENDING-SOURCE | SR-AC-OK24-014 + 033 + cross |
| `CHILD_SICK.annual_quota` (AC=1, HK=2, PROSA=3 × both OK) | varies | pending | MATCH-PENDING-SOURCE | SR-AC-OK24-016 + cross |
| `CHILD_SICK.is_per_episode` (same) | `true` | State-sector convention | MATCH-PENDING-SOURCE | SR-AC-OK24-034 + cross |
| `SENIOR_DAY.annual_quota` + `min_age` (AC, HK, PROSA × both OK) | `0` + `60` | pending — encoding semantics unclear | **DRIFT-IN-SOURCE** | SR-AC-OK24-015 + 035 + cross + Candidate Bug #3 |
| AC_RESEARCH + AC_TEACHING entitlement_configs rows (5 types × 2 variants × 2 OK = 20 missing) | **NO ROW** | Expected mirror of AC base per ROADMAP no-opt-in glocal principle | **DRIFT-IN-SEED** | SR-AC_RESEARCH-OK24-005 + SR-AC_TEACHING-OK24-005 + Candidate Bug #1 |

---

## Section 3: wage_type_mappings Cell Comparison

Comparison is **seed vs source** (code values N/A — mappings are DB-only). Per-agreement bundle comparison:

| Agreement bundle | Seed cells (count) | Source | Class | SR row |
|------------------|---------------------|--------|-------|--------|
| AC OK24 + OK26 | 17 mappings + 1 NORM_DEVIATION × 2 OK = 36 cells | SLS technical doc pending | MATCH-PENDING-SOURCE for AC-pinned cells; HIGH-confidence for ID-equivalent cells with HK/PROSA | SR-AC-OK24-037 + cross |
| HK OK24 + OK26 | ~22 mappings × 2 OK = ~44 cells | SLS technical doc pending | MATCH-PENDING-SOURCE | SR-HK-OK24-030 + cross |
| PROSA OK24 + OK26 | ~22 mappings × 2 OK = ~44 cells (matches HK) | SLS technical doc pending | MATCH-PENDING-SOURCE | SR-PROSA-OK24-008 + cross |
| AC_RESEARCH OK24 + OK26 | 12 mappings × 2 OK = 24 cells; 6 of 11 SLS codes diverge from AC base | SLS technical doc pending; divergence direction unclear | **DRIFT-UNCLEAR pending Phase B** (resolves to DRIFT-IN-SEED if S11 authoring bug, or DRIFT-IN-SOURCE if intentional research-specific code block) | SR-AC_RESEARCH-OK24-006 + Candidate Bug #2 |
| AC_TEACHING OK24 + OK26 | Same 12 × 2 = 24 cells; same divergence pattern as AC_RESEARCH | Same as AC_RESEARCH | **DRIFT-UNCLEAR** (inherits AC_RESEARCH finding) | SR-AC_TEACHING-OK24-006 + cross |

**Critical sub-issue (within Candidate Bug #2)**: `MERARBEJDE → SLS_0210` for AC_RESEARCH + AC_TEACHING **collides with** HK / PROSA's `OVERTIME_50 → SLS_0210` mapping. SLS-side payroll cannot distinguish between "research-staff extra work" and "HK overtime tier 1". Resolution must address this collision regardless of whether intent is to mirror AC base or use a separate code block.

---

## Section 4: position_override_configs Cell Comparison

| Agreement | Override count | Seed | Source | Class | SR row |
|-----------|----------------|------|--------|-------|--------|
| AC OK24 | 2 (DEPARTMENT_HEAD + RESEARCHER) | At init.sql:1258 + 1260 | pending — central baseline + institutional override per ADR-017 | MATCH-PENDING-SOURCE | SR-AC-OK24-038 + 039 |
| AC OK26 | 2 (mirror OK24) | At init.sql:1259 + 1261 | pending | MATCH-PENDING-SOURCE (placeholder) | SR-AC-OK26-004 |
| HK / PROSA / AC_RESEARCH / AC_TEACHING × both OK | 0 (explicit absence) | No rows | pending — Phase B confirms intentional vs missing | DRIFT-UNCLEAR | SR-HK-OK24-031 + SR-PROSA-OK24-009 + SR-AC_RESEARCH-OK24-007 + SR-AC_TEACHING-OK24-007 + OK26 mirrors |

**Phase B clarifications needed**:
- Confirm DEPARTMENT_HEAD's 200h flex cap + 4-week norm is centrally-mandated or institutionally-negotiated baseline
- Confirm RESEARCHER's 4-week norm (without flex cap change) is correct
- Confirm HK / PROSA / AC_RESEARCH / AC_TEACHING genuinely have no role-based overrides at the central level
- Cross-ref with `role-dimension-audit.md` Phase B sign-off table (which 9 cells require Phase B verification)

---

## Candidate Bug Routing Summary

All 4 candidate bugs surfaced during S36 inventory route to **either S37 (seed correction commits) or S39 (schema migration) per the bug-correction-when-classified path** in ROADMAP rule correction policy:

| # | Candidate | Class | Pre-launch action | Sprint |
|---|-----------|-------|---------------------|--------|
| 1 | AC variants entitlement_configs absence (20 missing rows) | DRIFT-IN-SEED | Mechanical seed correction (mirror AC base values); pre-launch `bug-with-no-past-impact` | **S37** (absorb after Phase B confirms inheritance is correct) |
| 2 | AC variants wage_type_mappings SLS code divergence | **DRIFT-UNCLEAR pending Phase B** (resolves to DRIFT-IN-SEED if S11 authoring bug; DRIFT-IN-SOURCE if intentional separate research-code-block) | Either (a) S37 mechanical seed correction (mirror AC base) OR (b) S38 ADR-024 D6 SLS reconciliation pattern if Phase B confirms intentional separate research-code-block + needs SLS-side coordination. Cannot pre-classify as "bug" until Phase B direction adjudication per ROADMAP rule correction policy + PROGRAM L134 ADR-024 D4 governance. | **S37 or S38** depending on Phase B finding |
| 3 | SENIOR_DAY paired-bug (`quota=0` + `min_age=60`) | DRIFT-IN-SOURCE pending Phase B; could land DRIFT-IN-CODE OR DRIFT-IN-SEED | Resolution depends on Phase B path selection: rule-engine code update (if DRIFT-IN-CODE) OR seed quota update (if DRIFT-IN-SEED) OR ADR-024 D2 tri-state-model integration | **S37 + S39 + potentially S40 cutover** depending on resolution path |
| 4 | HK / PROSA `OvertimeRequiresPreApproval=false` | DRIFT-IN-SOURCE; could land MATCH-PENDING-SOURCE → MATCH if confirmed correct | If Phase B confirms cirkulær-mandated pre-approval, S37 mechanical seed + code correction (flip to `TRUE`) | **S37** (or no action if MATCH) |

**Note on SR rows updated when candidates classify**: when Phase B closes a candidate, the source register's `bug_correction_history` field on the affected SR rows gets a new entry per ROADMAP rule correction policy ("supersession-by-default; bug-correction-when-classified"). The current S35 AC=AFSPADSERING correction is the template.

---

## Cross-References

- **`docs/references/agreement-source-register.md`** — per-cell source-of-truth + SR row IDs cited above
- **`docs/references/role-dimension-audit.md`** — companion audit for within-OK role distinction; chefkonsulent gap not covered here (production-incorrectness is at the role level, not the cell level)
- **`docs/references/danish-agreements.md`** — to be updated in TASK-3608 with SR row ID cross-references
- **PROGRAM-s36-s41-domain-correctness.md** — Phase E continuous-validation tests (S39 TASK-3905) consume this audit; seed-parity test asserts seed values match expected values per source register
- **ROADMAP "Deployment Model" L25** — rule correction policy committed 2026-05-18; supersession-by-default + bug-correction-when-classified
- **S35 TASK-3503 commit `cbaea7d`** — first concrete bug-correction-when-classified application (AC=AFSPADSERING)
- **S38 ADR-024 D2** — tri-state `MerarbejdeCompensationRight` model may absorb Candidate #3 (SENIOR_DAY) resolution path if Phase B routes via entitlement-rule-engine logic
- **S39 TASK-3905** — Phase E seed-parity tests + "unknown unknown" tests + DRAFT-OK rule enforcement

## How to Update This Doc After Phase B

When Phase B closes a candidate:

1. Update the SR row's `last_verified_by`, `decision_date`, `confidence_level` (likely HIGH).
2. Add the SR row's `bug_correction_history` or `supersession_history` entry if a value changed.
3. Reclassify the cell in this audit: typically MATCH-PENDING-SOURCE → MATCH, or DRIFT-IN-SEED → resolved.
4. Update the Classification Summary counts at the top.
5. Mark the Candidate Bug Routing row as resolved with commit hash.
