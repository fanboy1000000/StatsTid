# StatsTid System Target

> End-state product definition. Referenced by CLAUDE.md. See CLAUDE.md for governance.

## Product Goal

Build a legally deterministic, versioned, auditable, secure time registration and agreement engine for Danish state employees.
The system must:
Support AC, HK, PROSA and other state agreements
Be rule-driven (no hardcoded union logic)
Be event-sourced
Be replayable
Support historical recalculation
Support OK version transitions (e.g. OK24 → OK26)
Support multi-organization hierarchy (Ministry → Styrelse → Afdeling → Team)
Support 5-role access control scoped to organizations
Support local configuration within central agreement constraints
Support GlobalAdmin-managed agreement configuration with draft/active/archived lifecycle
Support period-based leader approval before payroll export
Support outbound API integrations
Support payroll export (SLS or equivalent)
Be production-ready from day one

## Functional Requirements (Mandatory)

### A. Basic Time Registration

System must support:
Daily registration (start/end OR hours)
Registration on:
Task / project
Activity type
Absence type
Flex/saldo visibility
Full history and audit trail
All changes must be event-based and immutable.

### B. Working Time Rules (Highly Complex)

System must handle variations in:
Norm Time
37 hours/week (standard)
Part-time (pro rata)
Irregular norm periods (e.g. 4-week norm)
Flex Arrangements
Maximum saldo
Carryover rules
Automatic conversion to time-off
Automatic payout rules
Merarbejde vs Overarbejde
System must:
Distinguish between merarbejde and overarbejde
Apply automatic calculation of supplements
Convert calculated amounts to payroll wage types

Examples:
AC:
Typically merarbejde
No traditional overtime logic
Possible on-call obligations

HK:
Overtime with 50% / 100% supplement
Time compensation vs payout

PROSA:
Often overtime supplements
Agreed system work outside normal hours

All behavior must be rule-configurable per employment category.

### C. Time Types & Supplements

System must support:
Evening/night supplements
Weekend/holiday supplements
On-call duty (rådighedsvagt)
Call-in work
Travel time (working vs non-working)
Special ministry domains (e.g. defense)

Rule engine must evaluate:
Timestamp
Calendar context (public holidays)
Employment profile
Agreement version
Calendar integration required.

### D. Absence Types (State-Specific Rules)

Must support:
Vacation (new Danish holiday act + transition rules)
Special holiday allowance
Care days
Child's 1st/2nd/3rd sick day
Parental leave (complex state rules)
Senior days
Leave with/without pay

Absence must impact:
Norm fulfillment
Flex balance
Pension basis
Payroll calculation
All absence effects must be rule-driven and version-aware.

### E. Organizational Structure

The Danish state operates a multi-org hierarchy with varying degrees of centralization.

System must support:
Hierarchical organization types: Ministry → Styrelse (agency) → Afdeling (department) → Team
Each organization has a parent reference and a materialized path for efficient subtree queries
Organizations are linked to agreements (agreement_code, ok_version)
Some ministries have centralized HR placed in a child organization (e.g. a styrelse) that covers the entire ministry subtree
Subtree resolution must be efficient (single query) for scope-based authorization

Examples:
Finansministeriet (Ministry)
├── Økonomistyrelsen (Styrelse) — may host centralized HR for all of Finansministeriet
├── Statens It (Styrelse)
│   ├── Drift (Afdeling)
│   └── Udvikling (Afdeling)
└── Digitaliseringsstyrelsen (Styrelse)

### F. Roles and Authorization

System must implement 5 roles with organization-scoped access control.

| Role | Scope | Key Capabilities |
|------|-------|------------------|
| **Global Admin** | All organizations | Manage state agreements (create, configure, publish, archive — see §I), manage OK version transitions (e.g. OK24 → OK26), create/manage organizations, manage all users |
| **Local Admin** | Assigned org(s) + descendants | Configure local settings within central agreement constraints, manage local users |
| **Local HR** | Explicitly assigned org subtree | View/edit all employees' time registrations within scope, organization statistics, employee management |
| **Local Leader** | Assigned team/org | Approve/reject time registration periods, employee oversight (sick days, vacation balances) |
| **Employee** | Own data only | Register time, view own registrations, submit periods for approval, view own balances |

Authorization constraints:
Each role assignment is scoped to an organization with a scope type: GLOBAL, ORG_ONLY, or ORG_AND_DESCENDANTS
Scope resolution uses organizational hierarchy — a scope on a parent org covers all descendants
A user may hold multiple role assignments (e.g. Leader for Team A, HR for Styrelse B)
HR scope is explicitly assignable — HR in a child organization can cover the parent ministry's entire subtree
Role changes are infrequent (monthly at most) and take effect on next authentication
All role assignments must be auditable (who granted, when, expiration)

API enforcement:
All resource-keyed API endpoints must verify the actor's org scope covers the target resource's organization
Scope verification uses materialized path prefix matching (scopes are embedded in JWT — no DB lookup per request for the scope itself)
The target resource's org must be resolved at request time (e.g. employeeId → user's primaryOrgId → org's materializedPath)
Employees may only access their own data — ownership check only, no org scope check needed
Higher roles (Leader, HR, Admin) must pass org scope verification for cross-employee access
Failed scope checks must return 403 Forbidden with no data leakage
Scope enforcement must be auditable — failed access attempts should be logged

### G. Local Configuration

Local administrators can configure operational parameters within centrally defined constraints. Local configuration must never override centrally negotiated agreement rules.

Five controlled configuration areas:
1. **Working Time Planning** — Norm period length (1/4/8/12 weeks), planning start day, planning calendar
2. **Flex Rules** — Maximum flex balance (within central limit), warning thresholds, payout triggers
3. **Organizational Structure** — Departments, cost centers, projects, approval chains
4. **Local Agreements (Lokalaftaler)** — Parameterized policies within central framework agreement boundaries
5. **Operational Configuration** — Approval flows, cutoff dates, lock periods, exemptions

Constraint enforcement:
Local values must respect central min/max boundaries (e.g. local maxFlexBalance must be <= central MaxFlexBalance)
Centrally negotiated rates (overtime rates, supplement rates) cannot be overridden locally
Hierarchical resolution: Central Agreement → Employment Group (AC/HK/PROSA) → Local Institutional Configuration
The rule engine must remain pure — local configuration is merged at the service layer, not in the rule engine
Local config changes are effective after re-evaluation (not retroactive by default)

### H. Period Approval Workflow

Time registrations must be approved by a leader before payroll export.

System must support:
Period types: weekly or monthly
Period status lifecycle: DRAFT → SUBMITTED → APPROVED or REJECTED
Employees submit their own periods
Leaders approve/reject periods for employees within their organizational scope
Only APPROVED periods may be exported to payroll
Rejection must include a reason
All status transitions must be auditable (who, when, reason)

Approval constraints:
A leader may only approve periods for employees in their assigned organizational scope
Payroll export endpoint must enforce the approval guard — unapproved periods are blocked
Period approval does not affect the rule engine — it is a workflow gate before payroll export

### I. Agreement Configuration Management

Global Admins must be able to create, configure, and manage agreement rule configurations through the UI. Agreement configs are the central parameters that govern all rule evaluation — they must be database-backed and version-controlled.

#### Storage and Migration
Agreement configurations are stored in PostgreSQL as the single source of truth. On first deployment, the database is seeded from the initial static configs (AC, HK, PROSA, AC_RESEARCH, AC_TEACHING × OK24/OK26). After seeding, the database is authoritative — config changes do not require redeployment.

The rule engine remains pure: it receives `AgreementRuleConfig` as a parameter and never performs I/O. The service layer loads configs from the database and passes them to the rule engine.

#### Versioning Lifecycle
Each agreement config follows an immutable versioning lifecycle:

```
DRAFT → ACTIVE → ARCHIVED
```

- **DRAFT**: Being configured, not yet in use for rule evaluation. Editable.
- **ACTIVE**: In effect. Immutable — cannot be edited. At most one ACTIVE config per (AgreementCode, OkVersion) pair.
- **ARCHIVED**: Previous version, preserved for retroactive recalculation and audit. Read-only.

Publishing a DRAFT automatically archives the current ACTIVE config for the same (AgreementCode, OkVersion).

#### Configurable Parameters (31 fields per agreement)
All parameters on `AgreementRuleConfig` are configurable:
- **Identity**: AgreementCode, OkVersion
- **Norm & Flex**: WeeklyNormHours, NormPeriodWeeks, NormModel (WEEKLY_HOURS/ANNUAL_ACTIVITY), AnnualNormHours, MaxFlexBalance, FlexCarryoverMax
- **Overtime & Merarbejde**: HasOvertime, HasMerarbejde, OvertimeThreshold50, OvertimeThreshold100
- **Supplements**: Evening/Night/Weekend/Holiday enabled flags, time windows (Start/End hours), and rates
- **On-call & Call-in**: OnCallDutyEnabled, OnCallDutyRate, CallInWorkEnabled, CallInMinimumHours, CallInRate
- **Travel**: TravelTimeEnabled, WorkingTravelRate, NonWorkingTravelRate

#### UI Requirements
The agreement management UI must support:
- **Overview page**: List all agreements grouped by status (Active / Draft / Archived), with filtering and search
- **Create new**: Empty form with sensible defaults from AgreementRuleConfig default values
- **Clone from existing**: Select a source agreement (any status) to pre-populate all fields, then edit as needed. Supports cross-version cloning (e.g., OK24 → OK26) and cross-agreement cloning.
- **Editor**: Form organized by parameter groups (Norm & Flex, Overtime, Supplements, On-call, Travel) with collapsible sections, toggle switches for boolean fields, conditional visibility (disabled supplement fields greyed out), and inline validation
- **Compare/Diff view**: Side-by-side comparison of two agreement versions highlighting changed parameters
- **Publish action**: Activates a draft config, auto-archiving the previous active config
- **Archive action**: Manually archive an active config without publishing a replacement

#### Validation Rules
- AgreementCode + OkVersion must be unique per ACTIVE config
- OvertimeThreshold100 must be ≥ OvertimeThreshold50
- HasOvertime and HasMerarbejde should not both be true (warn, not block — per RES-001)
- NormModel = ANNUAL_ACTIVITY requires AnnualNormHours > 0
- NormModel = WEEKLY_HOURS makes AnnualNormHours irrelevant (hidden in UI)
- NormPeriodWeeks must be one of: 1, 2, 4, 8, 12
- All rates and hours must be > 0 where applicable

#### Future Extensions (not in first iteration)
- **Position override management**: UI for managing PositionOverrideConfigs per agreement (currently static)
- **Wage type mapping management**: Separate admin page for configuring time type → SLS code mappings per agreement (currently seeded in init.sql)

### J. Working Time Compliance

The system must enforce working time limits mandated by EU directive 2003/88/EC and Danish implementation (Arbejdstidsloven). These constraints directly affect time registration validity and may generate compensatory payroll entries.

#### Rest Period Validation
- **Daily rest**: Minimum 11 consecutive hours rest per 24-hour period
- **Weekly rest day**: Minimum 1 uninterrupted rest period of 24 hours per 7-day period (in addition to 11-hour daily rest)
- The system must validate registered time entries against these limits
- Violations must generate warnings visible to the employee and their leader
- Some agreements allow temporary derogation from the 11-hour rule — this must be configurable per agreement (`RestPeriodDerogationAllowed`, `MinimumRestHours`)

#### Real-Time Rest Period Feedback (Skema UI)

When an employee registers time in Skema, the UI must perform **client-side rest period analysis** against existing registrations for adjacent days. If the new or edited entry would reduce the rest period below 11 hours (based on the previous day's end time or the next day's start time), the UI must:

1. **Show an inline warning** on the time entry explaining the situation: e.g. "Denne registrering giver kun 9 timers hvile mellem [dato] og [dato]. Det udløser kompenserende hvile, medmindre du selv har valgt at arbejde på dette tidspunkt."
2. **Prompt the employee to decide**: Present the `VoluntaryUnsocialHours` toggle directly in the warning context — not buried in a settings menu. The employee makes an active, informed choice at the moment of registration.
3. **Show the consequence**: If the employee does NOT mark it as voluntary, the warning should clarify that the registration will be flagged as a rest period breach, visible to their leader, and may trigger compensatory rest.

This feedback loop ensures employees are aware of the compliance impact before submitting, and makes the voluntary/involuntary distinction a natural part of the registration flow rather than an afterthought.

The analysis must be client-side (based on already-loaded period data) for immediate feedback — no server round-trip required. The definitive compliance check remains server-side in the rule engine.

#### Voluntary Unsocial Hours (Frivilligt arbejde uden for normal tid)

Employees may voluntarily choose to work during hours that would otherwise constitute a rest period violation. This is common for researchers working late, employees attending voluntary evening events, or flexible scheduling where the employee initiates the non-standard hours.

The system must distinguish between:
1. **Employer-directed rest period breach** — full compliance applies: warning, compensatory rest obligation, potential supplement
2. **Employee-initiated voluntary work** — the rest period rule does not apply; no violation is recorded, no compensatory rest is required

Implementation requirements:
- `VoluntaryUnsocialHours`: boolean flag on TimeEntry, default false
- When set to true, the rest period validation rule **skips** this entry when checking 11-hour and weekly rest compliance
- The flag must be set by the employee themselves at registration time (not retroactively by a leader)
- The flag is **informational for the leader** — visible in the approval view so the leader is aware of the employee's choice
- Flagged entries must still count toward norm fulfillment, flex balance, and payroll — only rest period validation is suppressed
- Flagged entries must still respect the 48h/week EU ceiling (voluntary choice does not exempt from maximum working time)
- The flag must be auditable — stored in the event and visible in the audit trail
- Agreement-level configuration: `VoluntaryUnsocialHoursAllowed` (boolean) — some agreements may not permit this opt-out

#### Daily Working Time Limits
- `MaxDailyHours` per agreement config (default: no explicit limit, but implied by rest rules ≈ 13 hours)
- Registrations exceeding the daily maximum must be flagged
- The NormCheckRule must validate daily limits in addition to period-based norm

#### Weekly Working Time Limit
- Maximum average 48 hours/week over reference period (typically 4 months)
- Reference period length must be configurable per agreement (`WeeklyMaxHoursReferencePeriod`)
- This is a compliance ceiling, not a norm — it applies on top of overtime calculations
- Voluntary unsocial hours still count toward the 48h ceiling — the EU directive maximum is absolute

#### Compensatory Rest
- When rest period is reduced (with derogation) due to **employer-directed work**, compensatory rest must be granted within a defined window
- Compensatory rest events must be tracked and exported to payroll if they affect pay
- Voluntary unsocial hours do **not** trigger compensatory rest obligations

### K. Entitlement & Balance Management

The system must track annual entitlements (budgets) for absence types that have limited quotas. Without entitlement tracking, the system cannot validate absence registrations or provide accurate balance information to employees and leaders.

#### Vacation Entitlement
- Annual vacation entitlement: 25 days (5 weeks) per the Danish Holiday Act (Ferieloven)
- Entitlement year: 1 September – 31 August (simultaneous earning/accrual model since 2020)
- Part-time employees: pro-rated by PartTimeFraction
- Carryover: maximum 5 days transferred to next entitlement year (configurable per agreement: `VacationCarryoverMaxDays`)
- Remaining vacation must be planned before carryover deadline or is forfeited (with exceptions)
- Vacation reduces norm fulfillment for the period (hours = days × daily norm)
- The balance summary endpoint must show: total entitlement, used, planned, remaining, carryover from previous year

#### Special Holiday Allowance (Særlig feriegodtgørelse)
- Typically 1.5% of annual salary — handled by payroll system
- The system must track the associated special holiday days (feriefridage): typically 5 days/year
- Entitlement and balance tracking required (same model as vacation)

#### Care Days (Omsorgsdage)
- Annual quota: 2 days per child under 7 (some agreements extend to age 14)
- Quota resets annually (calendar year or agreement-specific)
- Not transferable between years
- Must validate against quota before accepting registration

#### Child Sick Days (Barns sygedag)
- 1st sick day: statutory right (all employees)
- 2nd and 3rd sick day: agreement-specific (configurable: `ChildSickDaysEntitlement` = 1, 2, or 3)
- Per-event quota (not annual) — resets per sickness episode
- System must track episodes, not just individual days

#### Senior Days (Seniordage)
- Entitlement depends on age and agreement (typically 1–5 days/year for employees aged 60+)
- Configurable per agreement: `SeniorDayEntitlementAge`, `SeniorDaysPerYear`
- Annual quota with reset

#### Entitlement Configuration
Each entitlement type requires:
- `EntitlementType`: vacation, special_holiday, care_day, child_sick, senior_day
- `AnnualQuota`: number of days (or hours)
- `AccrualModel`: immediate (full quota at year start) or monthly accrual
- `ResetDate`: when quota resets (calendar year, ferieår, or custom)
- `CarryoverMax`: maximum days transferable
- `ProRateByPartTime`: boolean
- `AgreementCode` + `OkVersion`: scoped to agreement

Entitlements are read by the rule engine (as parameters) but managed by the service layer. The rule engine remains pure.

### L. Overtime Governance

The system currently calculates overtime correctly but lacks governance controls. For a production system, overtime must be bounded and controllable.

#### Maximum Overtime
- `MaxOvertimeHoursPerPeriod`: configurable ceiling per agreement (e.g., 10 hours/week, 40 hours/month)
- When exceeded, the system must flag but still record (overtime may be legitimate with approval)
- Exceeded overtime must be visible in leader dashboard and approval workflow

#### Overtime Pre-Approval
- Some organizations require pre-approval before overtime is worked
- `OvertimeRequiresPreApproval`: boolean per agreement or local config
- When enabled, overtime registrations without prior approval generate a warning
- Pre-approval is a workflow concept (not a rule engine concern) — modeled as a flag on TimeEntry or a separate approval record
- Unapproved overtime must still be calculable (for retroactive approval) but flagged in export

### M. Compensation Model (Afspadsering vs. Udbetaling)

The system must explicitly model the choice between time-off compensation (afspadsering) and monetary payout for overtime and merarbejde. These map to different SLS wage types.

#### Compensation Types
- **Afspadsering**: Overtime/merarbejde hours converted to time-off at the applicable rate (e.g., 1 hour overtime at 50% = 1.5 hours afspadsering)
- **Udbetaling**: Overtime/merarbejde hours paid out as salary supplement
- **Default model**: Configurable per agreement (`DefaultCompensationModel`: AFSPADSERING or UDBETALING)
- **Employee choice**: Some agreements allow employee to choose per period — configurable (`EmployeeCompensationChoice`: boolean)

#### Overtime Balance
- Separate from flex balance — overtime balance tracks accumulated overtime hours eligible for compensation
- `OvertimeBalance`: running total of uncompensated overtime hours
- Afspadsering reduces overtime balance and creates an absence entry
- Payout reduces overtime balance and creates a payroll export line
- Conversion rates must be agreement-aware (50% overtime = 1.5x for afspadsering, different SLS code for payout)

#### Wage Type Mapping
- OVERTIME_50_PAYOUT → SLS code (monetary)
- OVERTIME_50_AFSPADSERING → SLS code (time-off)
- OVERTIME_100_PAYOUT → SLS code (monetary)
- OVERTIME_100_AFSPADSERING → SLS code (monetary)
- MERARBEJDE_PAYOUT → SLS code
- MERARBEJDE_AFSPADSERING → SLS code
- These must be added to the wage_type_mappings table

## AC-Specific Requirements

AC employees differ fundamentally.
System must support:
Disabling overtime calculation for specific groups
Norm-based tracking instead of hour-based logic
Position-based rule overrides
On-call obligations
Academic/research norm systems (e.g. universities)
Overtime logic must be configurable per job category.

## Payroll Integration (Critical)

System must:
Map time types → wage types
Generate export to SLS or equivalent state payroll system
Handle retroactive corrections
Support recalculation across OK versions
Version payroll mappings
Maintain traceability: Time Event → Rule Evaluation → Wage Type → Export File
Payroll logic must be isolated from rule engine but driven by rule outputs.
Without payroll integration, system is considered incomplete.

## External Integrations

System must support outbound API integrations.
Integrations must:
Be asynchronous
Be event-driven
Be isolated in a dedicated bounded context
Never influence rule evaluation
Be idempotent
Support retries and circuit breaker patterns
Track delivery status
Use versioned outbound contracts
External failure must not impact deterministic core.
