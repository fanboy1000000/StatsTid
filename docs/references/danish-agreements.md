# Danish State Sector Agreement Reference

> Summary of AC, HK, and PROSA agreement rules for agent context. Source of truth: CentralAgreementConfigs (SharedKernel) and wage_type_mappings (init.sql).
>
> **Cross-references** (added S36 TASK-3608 for traceability; full prose rewrite deferred to S41 TASK-4106):
> - [`docs/references/agreement-source-register.md`](agreement-source-register.md) — per-cell source-of-truth with cirkulær cites + confidence levels + Phase B sign-off. Row IDs cited inline below as `[SR-XXX-OKXX-NNN]`.
> - [`docs/references/role-dimension-audit.md`](role-dimension-audit.md) — within-OK role distinctions; chefkonsulent merarbejde-entitlement gap.
> - [`docs/references/agreement-ruleset-audit.md`](agreement-ruleset-audit.md) — code-vs-seed-vs-source comparison with classification.

## Agreements Overview

| Code | Full Name | Description |
|------|-----------|-------------|
| AC | Akademikernes Centralorganisation | Academic employees (university-educated civil servants) |
| HK | Handels- og Kontorfunktionærerne | Office and administrative staff |
| PROSA | IT-professionelle | IT professionals |
| AC_RESEARCH | Akademikernes Centralorganisation (Researchers) | Academic researchers with annual activity norm (1924h) |
| AC_TEACHING | Akademikernes Centralorganisation (Teaching) | Teaching staff with reduced annual norm (1680h, research obligations) |

## OK Versions

- **OK24**: Effective 2024-04-01
- **OK26**: Effective 2026-04-01
- Version resolved by **entry date**, not current date (ADR-003)
- OK26 configs are currently placeholders identical to OK24 values

## Key Behavioral Differences (RES-001)

| Property | AC | HK | PROSA | AC_RESEARCH | AC_TEACHING | SR rows |
|----------|----|----|-------|-------------|-------------|---------|
| HasOvertime | false | true | true | false | false | SR-AC-OK24-021 / SR-HK-OK24-004 / SR-PROSA-OK24-001 (in mirrors-HK bundle) |
| HasMerarbejde | true | false | false | true | true | SR-AC-OK24-007 / SR-HK-OK24-005 / SR-PROSA-OK24-001 / SR-AC_RESEARCH-OK24-001 / SR-AC_TEACHING-OK24-001 |
| Supplements (Evening/Night/Weekend/Holiday) | all disabled | all enabled | all enabled | all disabled | all disabled | SR-AC-OK24-024 (compound, all-disabled) / SR-HK-OK24-008 (all-enabled quad) / SR-PROSA-OK24-001 / AC variants in 001 bundles |
| OnCallDutyEnabled | false | true | true | false | false | SR-AC-OK24-025 / SR-HK-OK24-015 / SR-PROSA-OK24-001 / AC variants in 001 bundles |
| OnCallDutyRate | -- | 0.33 | 0.33 | -- | -- | SR-AC-OK24-026 (inert) / SR-HK-OK24-016 (load-bearing) |
| CallInWorkEnabled | false | true | true | false | false | SR-AC-OK24-027 (compound) / SR-HK-OK24-017 / SR-PROSA-OK24-001 |
| CallInMinimumHours | -- | 3.0 | 3.0 | -- | -- | SR-AC-OK24-027 (inert) / SR-HK-OK24-017 (load-bearing) |
| TravelTimeEnabled | true | true | true | true | true | SR-AC-OK24-028 / SR-HK-OK24-018 (compound w/ rates) |
| WorkingTravelRate | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 | SR-AC-OK24-029 / SR-HK-OK24-018 |
| NonWorkingTravelRate | 0.5 | 0.5 | 0.5 | 0.5 | 0.5 | SR-AC-OK24-030 / SR-HK-OK24-018 |
| MaxFlexBalance | 150.0 | 100.0 | 120.0 | 150.0 | 150.0 | SR-AC-OK24-011 / SR-HK-OK24-002 / SR-PROSA-OK24-002 (divergent) |
| FlexCarryoverMax | 150.0 | 100.0 | 120.0 | 150.0 | 150.0 | SR-AC-OK24-012 / SR-HK-OK24-003 / SR-PROSA-OK24-003 (divergent) |
| NormModel | WEEKLY_HOURS | WEEKLY_HOURS | WEEKLY_HOURS | ANNUAL_ACTIVITY | ANNUAL_ACTIVITY | SR-AC-OK24-020 (compound) / SR-AC_RESEARCH-OK24-002 (divergent) / SR-AC_TEACHING-OK24-002 |
| AnnualNormHours | -- | -- | -- | 1924 | 1680 | SR-AC-OK24-020 (inert in AC) / SR-AC_RESEARCH-OK24-003 (load-bearing) / SR-AC_TEACHING-OK24-003 (divergent) |
| WeeklyNormHours | 37.0 | 37.0 | 37.0 | 37.0 | 37.0 | SR-AC-OK24-001 (universal state-sector norm) + cross-agreement HIGH confidence |
| NormPeriodWeeks | 1 | 1 | 1 | 1 | 1 | SR-AC-OK24-020 / SR-AC_RESEARCH-OK24-004 (inert under ANNUAL_ACTIVITY) |

### Supplement Time Windows (HK/PROSA only)

| Supplement | Start | End | Rate | SR rows |
|------------|-------|-----|------|---------|
| Evening | 17:00 | 23:00 | 1.25 | SR-HK-OK24-009 (windows compound) + SR-HK-OK24-010 (rate) |
| Night | 23:00 | 06:00 | 1.50 | SR-HK-OK24-009 + SR-HK-OK24-011 |
| Weekend (Saturday) | -- | -- | 1.50 | SR-HK-OK24-012 |
| Weekend (Sunday) | -- | -- | 2.00 | SR-HK-OK24-013 |
| Holiday | -- | -- | 2.00 | SR-HK-OK24-014 |

PROSA mirrors HK on all 5 cells per SR-PROSA-OK24-001 "mirrors HK" bundle.

### Overtime Thresholds (HK/PROSA only)

| Threshold | Hours | SR rows |
|-----------|-------|---------|
| OvertimeThreshold50 | 37.0 | SR-HK-OK24-006 (load-bearing for HK; inert for AC per SR-AC-OK24-022) |
| OvertimeThreshold100 | 40.0 | SR-HK-OK24-007 (load-bearing for HK; inert for AC per SR-AC-OK24-023) |

## Compensation Model

> Added 2026-05-18 (S35 / TASK-3504). The `DefaultCompensationModel` + `EmployeeCompensationChoice` fields were introduced in S17 (Overtime Governance & Compensation Model) but were never back-filled into this reference doc. Source of truth: the overenskomst cirkulærer cited per agreement below. Encoded values live in `init.sql` agreement_configs seed rows + `CentralAgreementConfigs.cs`.

The two fields govern how overtime/merarbejde compensation is delivered per agreement:

- **`DefaultCompensationModel`** — `"AFSPADSERING"` (time-off-in-lieu) or `"UDBETALING"` (payment).
- **`EmployeeCompensationChoice`** — `true` if the employee may choose, `false` if the employer determines feasibility per the cirkulære.

| Agreement | DefaultCompensationModel | EmployeeCompensationChoice | Source citation | SR rows |
|-----------|--------------------------|----------------------------|-----------------|---------|
| AC | AFSPADSERING | false | AC overenskomst cirkulære ([oes.dk 043-19](https://oes.dk/media/ik0hm2lr/043-19.pdf)) §4 — afspadsering as far as possible; payment as fallback when afspadsering infeasible; employer determines feasibility | SR-AC-OK24-005 (model, HIGH-confidence post-S35) + SR-AC-OK24-006 (choice) |
| HK | AFSPADSERING | true | HK Stat overenskomst — default afspadsering within 3 months; employee has right to payment if not arranged in time | SR-HK-OK24-021 (model) + SR-HK-OK24-020 (choice) |
| PROSA | AFSPADSERING | true | PROSA stat overenskomst — default afspadsering or payment 1:1; employee right per agreement | SR-PROSA-OK24-001 (mirrors HK bundle) |
| AC_RESEARCH | AFSPADSERING | false | Inherits from AC base agreement | SR-AC_RESEARCH-OK24-001 (mirrors AC bundle) |
| AC_TEACHING | AFSPADSERING | false | Inherits from AC base agreement | SR-AC_TEACHING-OK24-001 (mirrors AC_RESEARCH bundle) |

### Historical correction (2026-05-18, S35 / TASK-3503)

The AC family (AC + AC_RESEARCH + AC_TEACHING) seeds originally carried `'UDBETALING'` due to an S17 inheritance trap: the model default in `AgreementRuleConfig.cs:67` **was** `"UDBETALING"` (flipped to `"AFSPADSERING"` at S122 — see the S122 subsection below), and the AC entries in `CentralAgreementConfigs.cs` did not override it. This inverted the cirkulære rule. Classified under the [ROADMAP rule correction policy](../../ROADMAP.md) (committed 2026-05-18) as **bug-with-no-past-impact** — pre-launch posture, no past periods exist, forward-only correction. See commit message of `S35 TASK-3503` for full source URLs (Personalestyrelsen + Akademikerne + Djøf + Folketinget + DM).

### S122 completion of the S35 lineage (2026-07-24, TASK-12200)

S35 fixed only the **seed values**; the S17 trap's **default heads** survived and kept inverting the rule wherever a config was built without an explicit model. S122 eradicated all of them and installed the authority the value set never had:
- **Every `"UDBETALING"` default flipped to `"AFSPADSERING"`** — 2 DB DEFAULTs (`agreement_configs.default_compensation_model`, `overtime_balances.compensation_model`), 3 CLR property defaults (`AgreementRuleConfig.cs:67`, `AgreementConfigEntity.cs:67`, `OvertimeBalance.cs:13`), and 4 hand-rolled test-fixture DDL heads.
- **Two genuinely-live field-loss inversions fixed** (they wrote the CLR default UDBETALING into real configs): `PositionOverrideConfigs.ApplyOverride` and the admin config-clone endpoint now copy all four overtime-governance fields.
- **The vocabulary authority** is now the DB CHECK `agreement_configs_default_compensation_model_check` / `overtime_balances_compensation_model_check` (`IN ('AFSPADSERING','UDBETALING')`) — the model column values can no longer drift from this table, and the spec-enum on the response members is declared citing it (PAT-012). Same forward-only, bug-with-no-past-impact classification as S35.

### Forward reference (S36+ / ADR-024 — pending)

**Within-OK role distinction is NOT currently modeled.** The state-sector overenskomst distinguishes between *fuldmægtig*, *specialkonsulent*, and *chefkonsulent* under AC; specialkonsulent + chefkonsulent **LOSE the contractual right to merarbejde compensation** per the cirkulære, but the system today treats all AC employees identically. Production chefkonsulent users would receive contractually-wrong compensation.

Modeling gap scheduled for the S36–S41 program (`.claude/plans/PROGRAM-s36-s41-domain-correctness.md`):
- **S36–S37 Phase A inventory** — source register + role-dimension audit + ruleset audit (with domain-expert validation running parallel as Phase B).
- **S38 Phase C design** — ADR-024 settles role-within-agreement placement + tri-state `MerarbejdeCompensationRight: CONTRACTUAL / DISCRETIONARY / NONE` replacing the current binary.
- **S39–S41 implementation** — `role_within_agreement_configs` table + `ConfigResolutionService` extension + payroll mapping cutover + exhaustive D-test matrix.

See ROADMAP Phase 4e "S35 domain-correctness discovery (LAUNCH-BLOCKING)" for the systemic framing.

### Forward reference (S36 source register)

Per-cell full citation + confidence level (HIGH/MEDIUM/LOW) + interpretation authority (Personalestyrelsen / Akademikerne / negotiated / contested) + verification date now lives in [`docs/references/agreement-source-register.md`](agreement-source-register.md) (created S36 TASK-3601 + populated through TASK-3605). The SR row references inline above point to specific entries. Phase B sign-off cycles will fill `last_verified_by` + `decision_date` per cell.

## Entitlement Quotas

Source: `DefaultEntitlementConfigs.cs` and `entitlement_configs` seed data.

| Entitlement Type | Annual Quota | Reset Month | Per-Episode | Pro-Rate Part-Time | Carryover Max | Min Age | Notes | SR rows |
|-----------------|-------------|-------------|-------------|-------------------|---------------|---------|-------|---------|
| VACATION | 25 days | September (9) | No | No (day-count flat per Ferieloven §5; ADR-031) | 5 days | -- | Ferie (ferieaar) | SR-AC-OK24-013 (quota, HIGH per Ferieloven) + SR-AC-OK24-032 (sub-fields) |
| SPECIAL_HOLIDAY | 5 days | September (9) | No | No (day-count flat per Ferieloven §5; ADR-031) | 0 | -- | Saerlige feriedage | SR-AC-OK24-031 (quota) + SR-AC-OK24-036 (sub-fields) |
| CARE_DAY | 2 days | January (1) | No | No | 0 | -- | Omsorgsdage | SR-AC-OK24-014 (quota) + SR-AC-OK24-033 (sub-fields) |
| CHILD_SICK (AC) | 1 day | January (1) | Yes | No | 0 | -- | Barns sygedag | SR-AC-OK24-016 + SR-AC-OK24-034 |
| CHILD_SICK (HK) | 2 days | January (1) | Yes | No | 0 | -- | Barns sygedag | SR-HK-OK24-028 |
| CHILD_SICK (PROSA) | 3 days | January (1) | Yes | No | 0 | -- | Barns sygedag | SR-PROSA-OK24-005 (divergent quota) |
| SENIOR_DAY | 2 days | January (1) | No | No | 0 | 62 | Seniordage — RESOLVED S37 TASK-3703 (was 0 + 60, paired-bug; user-corrected to 2 + 62 uniform across all 5 agreements). | SR-AC-OK24-015 + SR-AC-OK24-035 + SR-HK-OK24-029 + SR-PROSA-OK24-006 |

VACATION and SPECIAL_HOLIDAY use the **MONTHLY_ACCRUAL** model (Ferieloven *samtidighedsferie* — earned-to-date, ~2,08 / ~0,42 days per month respectively; activated S60, **ADR-030**, superseding ADR-021 D6). Their **day-count is NOT pro-rated by part-time fraction** (flat 25/5 for every employee per Ferieloven §5 stk.1 — "uanset om du arbejder på fuld tid, nedsat tid, eller arbejder få dage om ugen"; state circular Medst. 021-24 §3 follows; **ADR-031**, superseding ADR-030 D8's piecewise-fraction premise). **Consumption (afholdelse) — corrected S66, ADR-032 (verified against primary sources, `vacation-consumption-mechanism-research.md`):** one day off consumes `hours ÷ that day's scheduled norm` feriedage — the state Ferievejledning's own mechanism (Example 3.5: 3 timers frihed = ½ dag on a 6h day, ⅓ on a 9h day) — so a whole scheduled workday off = exactly **1** dag for every employee at any fraction. **There is NO `5 ÷ N per day off` multiplier in the state-sector authority** (LBK 230/2021 §6 stk.2 is a *week-mirroring* rule: vacation is held 5 days/week mirroring the work pattern, arbejdsfrie dage "indgår i ferien med et forholdsmæssigt antal" — self-enforcing under the system's 5-weekday norm model; the 5÷N framing the earlier deferral anticipated was a private-sector practitioner shorthand, refuted by adversarial verification). §8.1/§8.4: NO special rules for <5-day/week employees for either ordinary or særlige feriedage. SPECIAL_HOLIDAY consumes by the same basis (and may be taken "samlet, enkeltvis og som brøkdele af dage"). The monetary feriebetaling value (also part-time-scaled) is not computed by this system. CARE_DAY, CHILD_SICK, and SENIOR_DAY use the IMMEDIATE accrual model and consume by the same `hours ÷ day-norm` basis (no conversion). CHILD_SICK is the only per-episode entitlement and varies by agreement.

> **AC_RESEARCH + AC_TEACHING gap (RESOLVED S37 TASK-3701)**: previously NO entitlement rows seeded for the 2 AC variants. Per interim-expert decision 2026-05-21, AC variants now have 20 rows mirroring AC base values (5 entitlements × 2 OK × 2 variants). See SR-AC_RESEARCH-OK24-005 + SR-AC_TEACHING-OK24-005 (canonical candidate bug **#1** in `agreement-ruleset-audit.md`). Bug-with-no-past-impact per ROADMAP rule correction policy.

## Wage Type Mappings (SLS Codes)

Source: `wage_type_mappings` seed data in `init.sql`. All mappings are identical for OK24 and OK26.

Per-agreement bundle SR row references: SR-AC-OK24-037 (AC bundle, 18 mappings) / SR-HK-OK24-030 (HK bundle, ~22 mappings) / SR-PROSA-OK24-008 (mirrors HK) / SR-AC_RESEARCH-OK24-006 + SR-AC_TEACHING-OK24-006 (variants — SLS code divergence resolved S37 TASK-3702 via mirror-AC-base per interim-expert decision; canonical candidate bug **#2** in `agreement-ruleset-audit.md`).

### Core Time Types (all agreements)

| Time Type | SLS Code | Description | AC | HK | PROSA |
|-----------|----------|-------------|----|----|-------|
| NORMAL_HOURS | SLS_0110 | Normal working hours | Y | Y | Y |
| NORM_DEVIATION | SLS_0150 | Norm deviation (merarbejde from norm surplus) | Y | -- | -- |

### Overtime / Merarbejde

| Time Type | SLS Code | Description | AC | HK | PROSA |
|-----------|----------|-------------|----|----|-------|
| OVERTIME_50 | SLS_0210 | Overtime at 50% supplement | -- | Y | Y |
| OVERTIME_100 | SLS_0220 | Overtime at 100% supplement | -- | Y | Y |
| MERARBEJDE | SLS_0310 | Extra work (merarbejde) | Y | -- | -- |

### Supplements (HK/PROSA only)

| Time Type | SLS Code | Description | AC | HK | PROSA |
|-----------|----------|-------------|----|----|-------|
| EVENING_SUPPLEMENT | SLS_0410 | Evening supplement 17-23 | -- | Y | Y |
| NIGHT_SUPPLEMENT | SLS_0420 | Night supplement 23-06 | -- | Y | Y |
| WEEKEND_SUPPLEMENT | SLS_0430 | Weekend supplement | -- | Y | Y |
| HOLIDAY_SUPPLEMENT | SLS_0440 | Public holiday supplement | -- | Y | Y |

### Absence Types (all agreements)

| Time Type | SLS Code | Description | AC | HK | PROSA |
|-----------|----------|-------------|----|----|-------|
| VACATION | SLS_0510 | Vacation | Y | Y | Y |
| CARE_DAY | SLS_0520 | Care day | Y | Y | Y |
| CHILD_SICK_DAY | SLS_0530 | Child's 1st sick day | Y | Y | Y |
| CHILD_SICK_DAY_2 | SLS_0531 | Child's 2nd sick day | Y | Y | Y |
| CHILD_SICK_DAY_3 | SLS_0532 | Child's 3rd sick day | Y | Y | Y |
| PARENTAL_LEAVE | SLS_0540 | Parental leave | Y | Y | Y |
| SICK_DAY | SLS_0540 | Sick day | Y | Y | Y |
| SENIOR_DAY | SLS_0550 | Senior day | Y | Y | Y |
| LEAVE_WITHOUT_PAY | SLS_0560 | Leave without pay | Y | Y | Y |
| LEAVE_WITH_PAY | SLS_0565 | Leave with pay | Y | Y | Y |
| SPECIAL_HOLIDAY_ALLOWANCE | SLS_0570 | Special holiday allowance | Y | Y | Y |

### Flex & Duty

| Time Type | SLS Code | Description | AC | HK | PROSA |
|-----------|----------|-------------|----|----|-------|
| FLEX_PAYOUT | SLS_0610 | Flex balance auto-payout | Y | Y | Y |
| ON_CALL_DUTY | SLS_0710 | On-call duty compensation | Y* | Y | Y |
| CALL_IN_WORK | SLS_0810 | Call-in work compensation | Y* | Y | Y |
| TRAVEL_WORK | SLS_0820 | Working travel time | Y | Y | Y |
| TRAVEL_NON_WORK | SLS_0830 | Non-working travel time | Y | Y | Y |

*AC has wage type mappings for completeness but the rules are disabled (OnCallDutyEnabled=false, CallInWorkEnabled=false).

### Academic Agreement Mappings (AC_RESEARCH / AC_TEACHING)

**Resolved post-S37 TASK-3702 (canonical candidate bug #2)**: previously 6 of 11 mappings DIVERGED from AC base. Per interim-expert decision 2026-05-21 (Reading A: S11 seed authoring bug), now mirror AC base. The reference table below reflects the previous (pre-S37) state and is preserved as historical context for the bug correction — the actual current seed values are AC-base-identical post-S37 TASK-3702 commit `ce1bf68`.

**Current seed values (post-S37 TASK-3702, mirroring AC base)**:

| Time Type | SLS Code | Description |
|-----------|----------|-------------|
| NORMAL_HOURS | SLS_0110 | Normal hours |
| NORM_DEVIATION | SLS_0150 | Norm deviation |
| MERARBEJDE | SLS_0310 | Merarbejde (corrected from SLS_0210 in S37; resolves the HK/PROSA OVERTIME_50 collision) |
| VACATION | SLS_0510 | Vacation |
| SICK_DAY | SLS_0540 | Sick day |
| CARE_DAY | SLS_0520 | Care day (corrected from SLS_0550 in S37) |
| CHILD_SICK_DAY | SLS_0530 | Child's 1st sick day (renamed from `CHILD_SICK_1` + chain restored in S37) |
| CHILD_SICK_DAY_2 | SLS_0531 | Child's 2nd sick day (added in S37) |
| CHILD_SICK_DAY_3 | SLS_0532 | Child's 3rd sick day (added in S37) |
| SENIOR_DAY | SLS_0550 | Senior day (corrected from SLS_0570 in S37) |
| LEAVE_WITH_PAY | SLS_0565 | Leave with pay (corrected from SLS_0580 in S37) |
| LEAVE_WITHOUT_PAY | SLS_0560 | Leave without pay (corrected from SLS_0590 in S37) |
| TRAVEL_WORK | SLS_0820 | Travel time (working) |
| TRAVEL_NON_WORK | SLS_0830 | Travel time (non-working) |

All AC variant mappings now equal AC base. The `CHILD_SICK_DAY` rename also fixes a pre-existing pre-launch production-broken state where rule engine emitted `CHILD_SICK_DAY` per `AbsenceRule.cs:112-114` but AC variants seed only had `CHILD_SICK_1` (phantom row never consulted). See SR-AC_RESEARCH-OK24-006 + SR-AC_TEACHING-OK24-006 bug_correction_history.

### Vacation Settlement Wage Types (period-end disposition — ADR-033)

The vacation-**settlement** layer (period-end transfer / payout / forfeiture / termination — ADR-033, design S67, implementation S68+ slices) emits **day/hour-count** lines via a *new period-close emitter* (NOT the rule-engine path, NOT the `FLEX_PAYOUT` seam). StatsTid emits quantities only; **SLS owns all kroner** (`Amount = Hours × multiplier` is the only in-app amount). New settlement rows inherit the ADR-020 versioned natural key `(time_type, ok_version, agreement_code, position)` + `effective_from/effective_to`. Source: `docs/references/vacation-settlement-law-research.md` (9/9 adversarially verified; Ferielov LBK 152/2024, Cirkulære 021-24) + Statens Administration SLS brugervejledninger.

**Verified today** — the særlige-feriedage godtgørelse is the *only* SLS day-count input the S67 research confirmed (ADR-033 D1 — the finding does **not** generalize to the other mechanisms):

| Mechanism | SLS løndel | Input | Notes |
|-----------|-----------|-------|-------|
| Særlige feriedage — cash godtgørelse (2½%) | 5017 / 5027 / 5037 | antal dage (felt 3 to-pay / felt 4 accrued) | SLS auto-applies *"2½ % af den feriegivende løn"* (Cirkulære 021-24 §15 stk.2 default-payout + §17 calc). The 2½% rate lives in SLS, not StatsTid. |
| Ferielønregulering | 5062 | hours | Hour-count input. |
| (Kroner manual correction) | 5007 | kroner | Manual-correction only — **StatsTid never emits this** (no in-app kroner). |

**Pending per-slice Step-0 verification** (ADR-033 D1 — each mechanism's SLS contract is verified at its implementation slice, not assumed):

| Mechanism | § | Line type | SLS contract |
|-----------|---|-----------|--------------|
| Transfer by written agreement | Ferieloven §21 | **none** (balance-only carryover) | n/a — no payroll line |
| Automatic post-period payout (>4-wk) | §24 | day-count payout | **unverified — slice-1 Step-0** |
| Termination payout (earned-untaken) | §26 | day-count payout | **unverified — slice-3 Step-0** |
| Termination *modregning* (over-taken/forskud, capped at final pay) | §7 stk.1 | **deduction** | **unverified — slice-3 Step-0**; a bare day-count cannot prove the monetary cap, which likely lives SLS-side (highest-risk contract) |
| First-4-week forfeiture to Arbejdsmarkedets Feriefond | §34 | (no employee line — lapse) | fail-closed to manual review pre-modeling (ADR-033 D10) |

(§17's 2½% ≠ §10's 2,02% general særlig feriegodtgørelse — distinct mechanisms; do not conflate.)

## Position Overrides

Source: `PositionOverrideConfigs.cs` and `position_override_configs` seed data.

Positions are AC-only. Override fields are nullable; null means "use base config value".

| Position Code | Display Label | Agreement | Override: MaxFlexBalance | Override: NormPeriodWeeks | Override: FlexCarryoverMax | Override: WeeklyNormHours | SR rows |
|--------------|---------------|-----------|--------------------------|---------------------------|----------------------------|---------------------------|---------|
| DEPARTMENT_HEAD | Kontorchef | AC | 200.0 | 4 | -- | -- | SR-AC-OK24-038 |
| RESEARCHER | Forsker | AC | -- | 4 | -- | -- | SR-AC-OK24-039 |
| SPECIALIST | Specialkonsulent | AC | -- (no override) | -- (no override) | -- | -- | covered as gap in `role-dimension-audit.md` (no override row exists) |
| TEACHING_STAFF | Undervisningspersonale | AC | -- (no override) | -- (no override) | -- | -- | covered as gap in `role-dimension-audit.md` (no override row exists) |

SPECIALIST and TEACHING_STAFF exist in the positions registry but have no config overrides defined in PositionOverrideConfigs.

> **Within-OK role distinction gap**: the 4 quantitative override fields cannot express "no merarbejde entitlement" — the AC chefkonsulent / kontorchef / potentially specialkonsulent compensation-entitlement loss per AC overenskomst is unmodeled. See [`role-dimension-audit.md`](role-dimension-audit.md) for the production-incorrectness call-out + 6-field schema gap analysis. Resolution: S38 ADR-024 D1 (placement) + D2 (tri-state `MerarbejdeCompensationRight`).

HK / PROSA / AC_RESEARCH / AC_TEACHING have no position overrides at all — see explicit-absence rows SR-HK-OK24-031 / SR-PROSA-OK24-009 / SR-AC_RESEARCH-OK24-007 / SR-AC_TEACHING-OK24-007.

### Config Resolution Chain

When resolving a config for a specific employee:

1. **Central config** (CentralAgreementConfigs) -- base values per agreement/OK version
2. **Position override** (PositionOverrideConfigs) -- partial overrides per position, null fields fall through
3. **Local override** (local_configurations table) -- org-specific overrides within central constraints

This chain is implemented in `ConfigResolutionService` (Infrastructure layer).
