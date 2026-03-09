# Danish State Sector Agreement Reference

> Summary of AC, HK, and PROSA agreement rules for agent context. Source of truth: CentralAgreementConfigs (SharedKernel) and wage_type_mappings (init.sql).

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

| Property | AC | HK | PROSA | AC_RESEARCH | AC_TEACHING |
|----------|----|----|-------|-------------|-------------|
| HasOvertime | false | true | true | false | false |
| HasMerarbejde | true | false | false | true | true |
| Supplements (Evening/Night/Weekend/Holiday) | all disabled | all enabled | all enabled | all disabled | all disabled |
| OnCallDutyEnabled | false | true | true | false | false |
| OnCallDutyRate | -- | 0.33 | 0.33 | -- | -- |
| CallInWorkEnabled | false | true | true | false | false |
| CallInMinimumHours | -- | 3.0 | 3.0 | -- | -- |
| TravelTimeEnabled | true | true | true | true | true |
| WorkingTravelRate | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 |
| NonWorkingTravelRate | 0.5 | 0.5 | 0.5 | 0.5 | 0.5 |
| MaxFlexBalance | 150.0 | 100.0 | 120.0 | 150.0 | 150.0 |
| FlexCarryoverMax | 150.0 | 100.0 | 120.0 | 150.0 | 150.0 |
| NormModel | WEEKLY_HOURS | WEEKLY_HOURS | WEEKLY_HOURS | ANNUAL_ACTIVITY | ANNUAL_ACTIVITY |
| AnnualNormHours | -- | -- | -- | 1924 | 1680 |
| WeeklyNormHours | 37.0 | 37.0 | 37.0 | 37.0 | 37.0 |
| NormPeriodWeeks | 1 | 1 | 1 | 1 | 1 |

### Supplement Time Windows (HK/PROSA only)

| Supplement | Start | End | Rate |
|------------|-------|-----|------|
| Evening | 17:00 | 23:00 | 1.25 |
| Night | 23:00 | 06:00 | 1.50 |
| Weekend (Saturday) | -- | -- | 1.50 |
| Weekend (Sunday) | -- | -- | 2.00 |
| Holiday | -- | -- | 2.00 |

### Overtime Thresholds (HK/PROSA only)

| Threshold | Hours |
|-----------|-------|
| OvertimeThreshold50 | 37.0 |
| OvertimeThreshold100 | 40.0 |

## Entitlement Quotas

Source: `DefaultEntitlementConfigs.cs` and `entitlement_configs` seed data.

| Entitlement Type | Annual Quota | Reset Month | Per-Episode | Pro-Rate Part-Time | Carryover Max | Min Age | Notes |
|-----------------|-------------|-------------|-------------|-------------------|---------------|---------|-------|
| VACATION | 25 days | September (9) | No | Yes | 5 days | -- | Ferie (ferieaar) |
| SPECIAL_HOLIDAY | 5 days | September (9) | No | Yes | 0 | -- | Saerlige feriedage |
| CARE_DAY | 2 days | January (1) | No | No | 0 | -- | Omsorgsdage |
| CHILD_SICK (AC) | 1 day | January (1) | Yes | No | 0 | -- | Barns sygedag |
| CHILD_SICK (HK) | 2 days | January (1) | Yes | No | 0 | -- | Barns sygedag |
| CHILD_SICK (PROSA) | 3 days | January (1) | Yes | No | 0 | -- | Barns sygedag |
| SENIOR_DAY | 0 (age-dependent) | January (1) | No | No | 0 | 60 | Seniordage, resolved at runtime |

All entitlements use IMMEDIATE accrual model. VACATION and SPECIAL_HOLIDAY are pro-rated by part-time fraction. CHILD_SICK is the only per-episode entitlement and varies by agreement.

## Wage Type Mappings (SLS Codes)

Source: `wage_type_mappings` seed data in `init.sql`. All mappings are identical for OK24 and OK26.

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

These use a partially different SLS code set:

| Time Type | SLS Code | Description |
|-----------|----------|-------------|
| NORMAL_HOURS | SLS_0110 | Normal hours |
| NORM_DEVIATION | SLS_0150 | Norm deviation |
| MERARBEJDE | SLS_0210 | Merarbejde (note: SLS_0210, not SLS_0310) |
| VACATION | SLS_0510 | Vacation |
| SICK_DAY | SLS_0540 | Sick day |
| CARE_DAY | SLS_0550 | Care day (note: SLS_0550, not SLS_0520) |
| CHILD_SICK_1 | SLS_0560 | Child sick day |
| SENIOR_DAY | SLS_0570 | Senior day |
| LEAVE_WITH_PAY | SLS_0580 | Leave with pay (note: SLS_0580, not SLS_0565) |
| LEAVE_WITHOUT_PAY | SLS_0590 | Leave without pay (note: SLS_0590, not SLS_0560) |
| TRAVEL_WORK | SLS_0820 | Travel time (working) |
| TRAVEL_NON_WORK | SLS_0830 | Travel time (non-working) |

Note the SLS code divergence between base AC and academic variants for MERARBEJDE, CARE_DAY, LEAVE_WITH_PAY, and LEAVE_WITHOUT_PAY.

## Position Overrides

Source: `PositionOverrideConfigs.cs` and `position_override_configs` seed data.

Positions are AC-only. Override fields are nullable; null means "use base config value".

| Position Code | Display Label | Agreement | Override: MaxFlexBalance | Override: NormPeriodWeeks | Override: FlexCarryoverMax | Override: WeeklyNormHours |
|--------------|---------------|-----------|--------------------------|---------------------------|----------------------------|---------------------------|
| DEPARTMENT_HEAD | Kontorchef | AC | 200.0 | 4 | -- | -- |
| RESEARCHER | Forsker | AC | -- | 4 | -- | -- |
| SPECIALIST | Specialkonsulent | AC | -- (no override) | -- (no override) | -- | -- |
| TEACHING_STAFF | Undervisningspersonale | AC | -- (no override) | -- (no override) | -- | -- |

SPECIALIST and TEACHING_STAFF exist in the positions registry but have no config overrides defined in PositionOverrideConfigs.

### Config Resolution Chain

When resolving a config for a specific employee:

1. **Central config** (CentralAgreementConfigs) -- base values per agreement/OK version
2. **Position override** (PositionOverrideConfigs) -- partial overrides per position, null fields fall through
3. **Local override** (local_configurations table) -- org-specific overrides within central constraints

This chain is implemented in `ConfigResolutionService` (Infrastructure layer).
