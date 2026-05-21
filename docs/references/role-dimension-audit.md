# Role Dimension Audit

> **Status**: DRAFT (S36 TASK-3606 produced; awaits Phase B + S38 ADR-024 D1 / D2 / D5 adjudication).
> **Scope**: within-OK role enumeration per agreement code (AC / HK / PROSA / AC_RESEARCH / AC_TEACHING) — strata, current encoding, compensation-entitlement gap analysis.
> **Created**: 2026-05-21.

## Purpose

This doc enumerates **within-agreement roles** for each of the 5 production agreement codes and audits how the current StatsTid encoding represents them. The headline finding from `PROGRAM-s36-s41-domain-correctness.md` L31–32 is reinforced: **AC chefkonsulent / specialkonsulent strata lose contractual merarbejde compensation right per the AC overenskomst, but the system treats all AC employees identically.** Production users in these roles would receive contractually-wrong compensation today.

Three downstream uses:

1. **S38 ADR-024 D1 input** — adjudicate role dimension placement: (a) extend `PositionOverrideConfigs` schema (lowest disruption); (b) activate `User.EmploymentCategory` as first-class rule input + introduce `RoleConfigOverride` parallel to position override (cleanest separation); (c) promote senior roles to separate agreement codes (matches AC_RESEARCH/AC_TEACHING precedent but conflates "agreement" with "stratum").
2. **S38 ADR-024 D2 input** — tri-state `MerarbejdeCompensationRight: CONTRACTUAL / DISCRETIONARY / NONE` model — necessary because current encoding's binary `DefaultCompensationModel` + `EmployeeCompensationChoice` cannot express chefkonsulent's "no contractual right" semantically.
3. **Phase B priority list** — domain-expert sign-off on (a) which AC strata lose merarbejde per cirkulær; (b) whether HK / PROSA have meaningful within-agreement role distinctions; (c) AC_RESEARCH + AC_TEACHING internal stratification.

## Source-Register Cross-References

All cells cited below trace to source-register rows at `docs/references/agreement-source-register.md`:

- **AC `HasMerarbejde = true`** → SR-AC-OK24-007 (cell value correct at agreement level; load-bearing for the chefkonsulent gap)
- **AC `DefaultCompensationModel = "AFSPADSERING"`** → SR-AC-OK24-005 (S35 TASK-3503 corrected bug)
- **AC `EmployeeCompensationChoice = false`** → SR-AC-OK24-006
- **AC position overrides**: SR-AC-OK24-038 (DEPARTMENT_HEAD) + SR-AC-OK24-039 (RESEARCHER)
- **HK position overrides explicit absence** → SR-HK-OK24-031
- **PROSA position overrides explicit absence** → SR-PROSA-OK24-009
- **AC_RESEARCH position overrides explicit absence** → SR-AC_RESEARCH-OK24-007
- **AC_TEACHING position overrides explicit absence** → SR-AC_TEACHING-OK24-007
- **Seeded position codes**: init.sql:944–958 — 4 AC positions (DEPARTMENT_HEAD / RESEARCHER / SPECIALIST / TEACHING_STAFF)

## Code-Side Evidence

Two model fields are relevant; only one is alive in the rule engine:

| Field | Status | Evidence |
|-------|--------|----------|
| `User.EmploymentCategory` | **vestigial** — set on model (`User.cs:13` default `"Standard"`), surfaced in `AdminEndpoints.cs:296+346` admin GET, but **NO rule in `src/RuleEngine/` reads it** | `grep -r EmploymentCategory src/RuleEngine/` returns 0 matches |
| `EmploymentProfile.EmploymentCategory` | **alive** (S31 introduction) — `EmploymentProfile.cs:22` `required`; consumed by `EmploymentProfileResolver` per S33 ADR-023 D1 cutover | Used by ComplianceEndpoints + EmployeeProfileRepository |
| `User.Position` / `EmploymentProfile.Position` | **alive** — read by `ConfigResolutionService` to apply position overrides from `position_override_configs` | S14 + S31 work |

The disconnect: `EmploymentProfile.EmploymentCategory` reaches the rule engine via `EmploymentProfileResolver`, **but no rule branches on it**. The category-aware compensation-entitlement decision the AC chefkonsulent gap requires is not implemented.

---

## AC Within-OK Roles

AC = Akademikere i Staten. The AC overenskomst defines several role strata with different compensation entitlements. The key distinction is **merarbejde entitlement**: junior AC staff receive merarbejde compensation; senior IC strata (chefkonsulent, possibly specialkonsulent) and managerial roles lose it per cirkulær framework.

### Seeded position codes (init.sql:953–958)

| `position_code` | Danish label | `agreement_code` | Has `position_override_configs` row? | Merarbejde entitlement per cirkulær (pending Phase B) |
|-----------------|--------------|------------------|--------------------------------------|--------------------------------------------------------|
| `DEPARTMENT_HEAD` | Kontorchef | AC | ✓ SR-AC-OK24-038 (max_flex_balance=200, norm_period_weeks=4) | **NONE** (managerial; loses merarbejde per cirkulær; current encoding ALSO INCORRECT — DEPARTMENT_HEAD employee today would still emit MERARBEJDE events because `HasMerarbejde=true` at agreement level) |
| `RESEARCHER` | Forsker | AC | ✓ SR-AC-OK24-039 (norm_period_weeks=4 only) | CONTRACTUAL (research-staff retain merarbejde; the multi-week norm reflects project work pattern, not entitlement reduction) |
| `SPECIALIST` | Specialkonsulent | AC | ✗ — seeded position but NO override row | **NONE or DISCRETIONARY** per cirkulær (pending Phase B; some cirkulær wordings put specialkonsulent in the senior-IC band that loses merarbejde, others retain it conditionally) |
| `TEACHING_STAFF` | Undervisningspersonale | AC | ✗ — seeded position but NO override row | Distinct path: typically routed through AC_TEACHING agreement code, not AC + TEACHING_STAFF position. **Position code may be vestigial** (Phase B clarifies use-case split between AC + TEACHING_STAFF position vs AC_TEACHING agreement) |

### Missing AC stratum (NOT seeded)

| Role | Danish label | Status | Phase B finding |
|------|--------------|--------|------------------|
| **CHEFKONSULENT** | Chefkonsulent | **NOT in `positions` table** | The PROGRAM L31 headline case. Chefkonsulent is the most senior AC individual-contributor stratum and most clearly loses merarbejde per cirkulær. Today, a chefkonsulent user would be assigned `agreement_code='AC'` with `position` either empty or one of the existing 4 codes (most likely SPECIALIST as next-most-senior). **No position override exists to express "no merarbejde entitlement"** even if the role were added, because `position_override_configs` schema lacks an entitlement-toggle column (see schema gap analysis below). |

### Per-role compensation-entitlement audit

| Role | Current StatsTid encoding | Cirkulær-stated entitlement (pending Phase B) | Production correctness |
|------|---------------------------|-----------------------------------------------|------------------------|
| Fuldmægtig (entry AC) | `HasMerarbejde=true`, `DefaultCompensationModel=AFSPADSERING`, `EmployeeCompensationChoice=false` | CONTRACTUAL merarbejde with employer-determined afspadsering feasibility | ✓ CORRECT |
| Specialkonsulent (SR position SPECIALIST) | Same as fuldmægtig — `HasMerarbejde=true` at agreement level; no position override | NONE or DISCRETIONARY (Phase B confirms direction) | **❌ POTENTIALLY INCORRECT** — would emit MERARBEJDE events for a senior IC who has no contractual right; the financial impact depends on how SLS-side handles unauthorised MERARBEJDE codes |
| Chefkonsulent (NOT seeded) | Same as fuldmægtig — would inherit agreement-level encoding regardless of how user is provisioned | **NONE** (clear cirkulær-stated loss of merarbejde) | **❌ DEFINITELY INCORRECT** — production chefkonsulent user receives merarbejde compensation today via agreement-level fallthrough. The headline PROGRAM L31 finding. |
| Kontorchef (SR position DEPARTMENT_HEAD) | `HasMerarbejde=true` at agreement level + position override changes flex cap + norm period, **not entitlement** | NONE (managerial loss of merarbejde) | **❌ INCORRECT** — managers receive merarbejde via agreement-level fallthrough despite the position override (which only adjusts quantitative cells). Less critical than chefkonsulent because admin / HR practice typically catches managerial overtime separately, but the system encoding is wrong. |
| Forsker (SR position RESEARCHER) | `HasMerarbejde=true` + position override (norm_period_weeks=4 only) | CONTRACTUAL (research-staff retain merarbejde with multi-week norm flexibility) | ✓ CORRECT — though the role-distinction with AC_RESEARCH agreement code needs Phase B clarification (when to use AC + RESEARCHER position vs AC_RESEARCH agreement) |

**Net AC finding**: of 5 enumerated role strata, the system encoding is **correct for 2 (fuldmægtig + forsker)** and **incorrect for 3 (specialkonsulent + chefkonsulent + kontorchef)**. The bug class affects all employees in those 3 roles. Pre-launch posture means no past periods to recompute; correction shipped before launch ships as a free seed + schema-extension correction per ROADMAP rule correction policy.

---

## HK Within-OK Roles

HK = Handels- og Kontorfunktionærer i Staten. **No HK position codes seeded** (init.sql:953–958 enumerates only AC positions). No `position_override_configs` rows for HK (SR-HK-OK24-031 explicit-absence).

### Within-HK stratification — preliminary assessment

| Stratum (hypothesised) | Danish label | Likely cirkulær framework | Phase B confirmation needed |
|------------------------|--------------|---------------------------|------------------------------|
| Kontorfunktionær (entry HK) | Kontorfunktionær | Standard HK overtime regime + supplements + on-call | Default assumed for all HK employees today |
| Overassistent | Overassistent | Same HK regime, higher salary band | Likely no encoding-level distinction beyond salary (handled outside StatsTid) |
| HK-leder (HK manager) | Funktionær med ledelsesansvar | May lose overtime entitlement under HK cirkulær if classified as functionally managerial | **Phase B verification needed** — if HK managers lose overtime entitlement, same encoding gap as AC chefkonsulent applies to HK |

**Net HK finding**: HK appears to have **less stratification than AC** — the overtime regime applies uniformly across most HK roles. The potential exception is HK-leder if classified as functionally managerial. Phase B confirms.

---

## PROSA Within-OK Roles

PROSA = IT-faglig organisation. **No PROSA position codes seeded**. No `position_override_configs` rows (SR-PROSA-OK24-009 explicit-absence).

### Within-PROSA stratification — preliminary assessment

| Stratum (hypothesised) | Danish label | Likely cirkulær framework | Phase B confirmation needed |
|------------------------|--------------|---------------------------|------------------------------|
| IT-medarbejder (entry) | IT-medarbejder | Standard PROSA overtime + supplements + on-call | Default assumed for all PROSA employees |
| Senior-konsulent (PROSA senior) | IT-konsulent / specialist | Standard PROSA regime; some senior PROSA contracts route through individual contracts outside the standard cirkulær | **Phase B verification** — within-PROSA stratification more common via individual contract terms than cirkulær-mandated strata |
| PROSA-leder | IT-leder med ledelsesansvar | Same managerial-classification question as HK-leder | **Phase B verification** |

**Net PROSA finding**: similar to HK — the regime applies uniformly across most roles; managerial-classification is the potential gap. Less critical because the PROSA employee base in state agencies is small (IT staff in ministerial-level IT teams).

---

## AC_RESEARCH Within-OK Roles

AC_RESEARCH = researchers under AC overenskomst, distinct agreement code (not a position). **Internal stratification IS expected** at university research-staff scale: PhD students vs postdocs vs adjunkt vs lektor vs professor.

### Within-AC_RESEARCH stratification — preliminary assessment

| Stratum | Danish label | Annual norm | Phase B confirmation needed |
|---------|--------------|-------------|------------------------------|
| PhD student | PhD-studerende | Possibly reduced or different model (PhD is hybrid student/employee) | **Phase B HIGH priority** — PhD-specific norm encoding may need its own agreement code or position override |
| Postdoc | Postdoc | 1924h annual norm (matches AC_RESEARCH base) | Default assumed |
| Adjunkt | Adjunkt | 1924h annual norm | Same as postdoc |
| Lektor | Lektor | 1924h annual norm — but typical practice mixes research (1924h frame) + teaching (1680h frame) for the same individual | **Phase B HIGH priority** — Lektor may route through AC_TEACHING-equivalent agreement OR have hybrid encoding |
| Professor | Professor | 1924h annual norm — typically loses merarbejde under managerial / leadership classification | **Phase B verification** — same chefkonsulent-class question for senior research staff |

**Net AC_RESEARCH finding**: **richest within-OK stratification of the 5 agreements**. The university research-staff career ladder has 5+ identifiable strata, each with potentially different compensation regimes. Currently encoded as monolithic AC_RESEARCH (no stratification). Likely needs ADR-024 D1 placement decision before any seed correction.

---

## AC_TEACHING Within-OK Roles

AC_TEACHING = teaching staff under AC overenskomst, distinct agreement code. **Internal stratification expected** at undervisningspersonale scale.

### Within-AC_TEACHING stratification — preliminary assessment

| Stratum | Danish label | Annual norm | Phase B confirmation needed |
|---------|--------------|-------------|------------------------------|
| Underviser (entry teaching) | Underviser | 1680h annual norm (matches AC_TEACHING base) | Default assumed |
| Adjunkt (teaching-track) | Adjunkt | 1680h annual norm | Same as underviser |
| Lektor (teaching-track) | Lektor | 1680h annual norm + senior salary band | **Phase B verification** — distinction from AC_RESEARCH Lektor |
| Studielektor | Studielektor | 1680h annual norm — coursework-focused | **Phase B verification** |

**Net AC_TEACHING finding**: similar to AC_RESEARCH but smaller stratum count. The 1680h vs 1924h norm distinction between AC_TEACHING and AC_RESEARCH already encodes the research-vs-teaching first-order distinction; within-AC_TEACHING strata mostly affect salary bands (outside StatsTid scope).

---

## `User.EmploymentCategory` Vestigial Analysis

| Aspect | Finding |
|--------|---------|
| Model field | `User.EmploymentCategory` (User.cs:13) initialized to `"Standard"` default |
| Where set | `AdminEndpoints` user creation + update paths set it from request payload |
| Where surfaced | `AdminEndpoints` admin GET responses (`AdminEndpoints.cs:296` + `:346`) |
| Where consumed by rules | **NOWHERE in `src/RuleEngine/`** — `grep -r EmploymentCategory src/RuleEngine/` returns 0 matches |
| Where consumed elsewhere | `EmploymentProfileResolver` (S31 ADR-022 + S33 ADR-023 D1 cutover) reads it from `EmploymentProfile.EmploymentCategory` (now required, mirrors via repository) |
| Production impact | Zero — the field rides through the admin API + appears in audit trails but never branches a rule decision |

**Resolution scope** (ADR-024 D1):
- If ADR-024 D1 picks option (b) — first-class rule input — `EmploymentCategory` becomes load-bearing in rules consuming it. `OvertimeGovernanceRule` would gain a category-branching check; `PayrollMappingService.BuildLine` would consult category before emitting MERARBEJDE.
- If ADR-024 D1 picks (a) — `PositionOverrideConfigs` schema extension — the category field stays vestigial; the role distinction routes through position overrides.
- If (c) — promote senior roles to separate agreement codes — the category field stays vestigial; new agreement codes (e.g., `AC_CHEFKONSULENT`) carry the distinction.

---

## `PositionOverrideConfigs` Schema Gap Analysis

Current schema (init.sql:1209–1223):

```sql
CREATE TABLE position_override_configs (
    override_id         UUID PRIMARY KEY,
    agreement_code      TEXT NOT NULL,
    ok_version          TEXT NOT NULL,
    position_code       TEXT NOT NULL,
    status              TEXT NOT NULL DEFAULT 'ACTIVE',
    max_flex_balance    DECIMAL,
    flex_carryover_max  DECIMAL,
    norm_period_weeks   INT,
    weekly_norm_hours   DECIMAL,
    -- + metadata columns
);
```

**4 quantitative override columns**: `max_flex_balance`, `flex_carryover_max`, `norm_period_weeks`, `weekly_norm_hours`. All `DECIMAL` or `INT`, all nullable (NULL = inherit from base agreement config).

**Schema gap**: cannot express:

1. **"No merarbejde entitlement"** (boolean disablement) — the entitlement flag lives on `agreement_configs.has_merarbejde` at agreement level; no per-position override toggle exists.
2. **"No overtime entitlement"** (boolean disablement) — same gap for `has_overtime`.
3. **"Different compensation model"** (categorical override) — `agreement_configs.default_compensation_model` has no override path.
4. **"No supplement entitlement"** (4 boolean disablement toggles) — supplements live at agreement level; no per-position override.
5. **"No on-call entitlement"** + **"No call-in entitlement"** (boolean disablement) — same gap.
6. **"Different SLS wage code"** for an existing time type when emitted by a specific role — current `wage_type_mappings` does support `position` column (it's part of the composite key) but the seed has all `position=''`; no role-specific mappings exist.

The 4-field schema is **necessary but not sufficient** for role-level compensation modeling. ADR-024 D1 picks the structural fix path; whichever option lands, the schema needs extension.

---

## Production-Incorrectness Call-Out

**Headline finding (PROGRAM L31 reinforced)**:

> An AC chefkonsulent user provisioned with `agreement_code='AC'` (any position, including no position) receives merarbejde compensation today because `agreement_configs.AC.has_merarbejde = true` at agreement level + no role-level override mechanism exists. The same applies to managerial AC roles (DEPARTMENT_HEAD / Kontorchef) and potentially specialkonsulent.

**Specific MERARBEJDE-event-emission risk**: when an AC chefkonsulent's time entry triggers the merarbejde regime (excess hours beyond weekly norm), the rule engine emits a MERARBEJDE event mapped to SLS_0310. SLS-side processes this as if the chefkonsulent has a contractual merarbejde right — payroll downstream emits compensation that the cirkulær says the employee should not receive.

**Pre-launch posture**: per ROADMAP rule correction policy (committed 2026-05-18), this falls under **bug-with-no-past-impact** classification — pre-launch means no past periods exist, so no retroactive recompute is needed. The fix ships as part of the S38 ADR-024 → S39 schema migration → S40 cutover → S41 D-test pipeline.

**Phase B confirmation priority**:

1. **HIGH** — verify cirkulær wording on AC chefkonsulent merarbejde entitlement loss (likely well-established cirkulær §X but URL pending Phase B)
2. **HIGH** — verify cirkulær wording on AC kontorchef (managerial) merarbejde entitlement loss
3. **MEDIUM-HIGH** — verify cirkulær wording on AC specialkonsulent merarbejde entitlement direction (cirkulær may explicitly retain, explicitly remove, or be ambiguous)
4. **MEDIUM** — verify HK / PROSA manager classifications + their overtime entitlement implications
5. **MEDIUM** — verify AC_RESEARCH / AC_TEACHING within-stratum compensation distinctions

---

## Forward Pointers

This audit feeds:

- **S38 ADR-024 D1** — role dimension placement decision (3 options enumerated)
- **S38 ADR-024 D2** — tri-state `MerarbejdeCompensationRight: CONTRACTUAL / DISCRETIONARY / NONE` model
- **S38 ADR-024 D4** — classification governance for the discoveries here (which are bugs vs interpretation changes vs disputed cells)
- **S38 ADR-024 D5** — interpretation authority (Personalestyrelsen default + per-cell deviation documentation in source register)
- **S39 TASK-3901** — schema migration for `role_within_agreement_configs` table (per PROGRAM L173)
- **S39 TASK-3903** — seed `role_within_agreement_configs` rows from this audit + S37-finalized source register
- **S40 TASK-4001** — activate `EmploymentCategory` (or alternative) as first-class field per ADR-024 D1
- **S40 TASK-4003** — rule engine cutover (`OvertimeGovernanceRule` reads tri-state `MerarbejdeCompensationRight`)
- **S40 TASK-4006** — marquee D-test: chefkonsulent past-period replay determinism + correct no-entitlement behavior
- **S41 TASK-4101** — D-test matrix per agreement: AC (fuldmægtig + specialkonsulent + chefkonsulent + kontorchef + forsker)

## Phase B Sign-Off Tracking

Each row below to be filled by Phase B domain expert:

| Finding | Confirmed bug? | Cirkulær cite | Resolution | Expert | Date |
|---------|----------------|---------------|------------|--------|------|
| AC chefkonsulent no merarbejde | pending | pending | pending | pending | pending |
| AC kontorchef no merarbejde | pending | pending | pending | pending | pending |
| AC specialkonsulent merarbejde direction | pending | pending | pending | pending | pending |
| HK-leder overtime classification | pending | pending | pending | pending | pending |
| PROSA-leder overtime classification | pending | pending | pending | pending | pending |
| AC_RESEARCH within-stratum distinctions | pending | pending | pending | pending | pending |
| AC_TEACHING within-stratum distinctions | pending | pending | pending | pending | pending |
| TEACHING_STAFF position code use-case (vs AC_TEACHING agreement) | pending | pending | pending | pending | pending |
| Missing CHEFKONSULENT position code seed | pending | pending | pending | pending | pending |
