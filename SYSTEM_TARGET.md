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
Norm period: one calendar month for HK, PROSA and AC — 7.4 h × the month's working days, pro rata for part-timers, minus public holidays (søgnehelligdage) that fall on a weekday the person would work; a local agreement may set another settlement period (§ G)

> *(S147, 2026-10-29: ADR-043 D1, D6; owner-approved spec update.)* In plain words: the hours a person owes are counted per calendar month, not per week, and a weekday public holiday lowers them. This is the state's own rule for HK and PROSA (§ 4 stk. 2; `docs/references/overtime-flex-model-research.md` V1); for AC it is a labelled default, because the AC agreement sets no period (`docs/references/agreement-cells-reverification-research.md` C5). Replaces "Irregular norm periods (e.g. 4-week norm)" and its S146 contradiction note. Decided; built in S148 stage 1. Until then payroll still judges each Monday–Sunday week, and no public holiday lowers the norm.
Flex Arrangements
Each institution's own flex caps, set locally; there is no central flex cap
Carryover rules
Automatic conversion to time-off
Forfeiture above a cap (recorded), a leaver's settlement, and the exceptions a local agreement allows, including 1:1 cash by agreement; no automatic payout (§ G)

> *(S147, 2026-10-30: ADR-043 D6; owner-approved spec update.)* In plain words: there is no central state flex agreement (PAV 19.5.1), so the central "maximum saldo" had no source, and flex above a cap is forfeited and shown rather than paid out automatically. Replaces "Maximum saldo" and "Automatic payout rules". Decided; built in S148's config stage (the settings) and stage 1 (their pay effect). Until then pay runs on today's central caps, and flex above them is paid out.
Merarbejde vs Overarbejde
System must:
Distinguish between merarbejde and overarbejde
Apply automatic calculation of supplements
Convert calculated amounts to payroll wage types

Examples:
AC:
Typically merarbejde
No traditional overtime logic
Travel time is the only special time type; no on-call *(S147, 2026-10-30: ADR-043 D11, owner D-3 = (c); owner-approved spec update. Replaces "Possible on-call obligations". Decided; built with D11's later stage, S148 stage 2 or later)*

HK:
Overtime at a flat +50 %, only for hours a leader ordered or accepted (§ L) *(S147, 2026-10-30: ADR-043 D4, D3; owner-approved spec update. Replaces "50% / 100% supplement". Decided; built in S148 stage 1. Until then payroll pays the weekly 50 % / 100 % bands)*
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
Travel time on official trips: counts in full as worked time, capped at 13 h per working døgn (the 24 hours from the start of the working day)
Special ministry domains (e.g. defense)

> *(S147, 2026-10-29: ADR-043 D11; owner-approved spec update.)* In plain words: on-call, call-ins and travel are not ordinary hours, and each counts as worked time in its own sourced way. For each type and agreement the system holds three facts: how much of the time counts as worked (a fraction), what the supplement is paid on, and whether the time counts as rest. HK and PROSA (Statens arbejdstidsaftale § 7 nr. 3–5): on-call counts 1/3 at home and 3/4 at the workplace; a call-in at under 24 hours' notice counts at least 3 h; travel counts in full up to 13 h per working døgn, never halved. AC-agreement staff: travel only, at most 13 h per døgn unless agreed locally. Replaces the "working vs non-working" travel split, which no source supports. Decided; S148 stage 0 refuses these three types (they can be neither registered nor paid), and a later stage (stage 2 or later) builds the model.

Rule engine must evaluate:
Timestamp
Calendar context: one date-keyed holiday calendar with three kinds of day — (1) the ten public holidays (søgnehelligdage), which lower the norm when they fall on a weekday; (2) timed supplement windows (Grundlovsdag from 12:00, 24 December from 14:00); (3) each institution's own customary days off, dated and audited: a paid day off (whole or part; given to everyone, or on request) or a closure day the employee covers with leave, time off in lieu or flex
Employment profile
Agreement version
Calendar integration required.

> *(S147, 2026-10-29: ADR-043 D9; owner-approved spec update.)* In plain words: not every special day is a public holiday. Grundlovsdag, 24 and 31 December and 1 May are not (`docs/references/agreement-cells-reverification-research.md` C1); without a recorded local custom they are ordinary working days. Holidays are keyed by date, not by agreement version (Store Bededag ended by law on 1 January 2024). A paid customary day is credited like a day of paid absence; it does not lower the norm. Decided; the customary-days table is built in S148's config stage, and the calendar split and its pay effect in stage 1. Until then the code keeps three separate holiday lists, and all three count Grundlovsdag as a holiday.

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

Reporting-line hierarchy (ADR-027): Complementing the org-unit hierarchy, each employee may have a designated manager via a temporal reporting-line model. Relationships: PRIMARY (one per employee) and ACTING (temporary delegation / vikarierende leder). Approval authority is scoped to the employee's Organisation (the earlier independent-reporting-tree-root / same-tree cross-org model was narrowed to Organisation scope in S95: `ValidateSameTree` → `ValidateSameOrganisation`). The reporting line determines approval routing; org-scope authority determines approval authorization.

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
> **Current model — ADR-035 flat authority (S93):** authority follows an EXPLICIT set of one-or-more Organisations, never the org tree. The `ORG_AND_DESCENDANTS` / subtree language in this section and the "+ descendants" / "subtree" wording in the role table above describe the SUPERSEDED design — the bounding by an org-set is kept, only the subtree/ancestor inheritance is removed. Read "descendants"/"subtree" here as "the explicit Organisation set."
Each role assignment is scoped with a scope type: GLOBAL or ORG_ONLY (an explicit set of one-or-more Organisations)
Scope resolution matches the target org against that explicit org-set — subtree inheritance is retired (ADR-035)
A user may hold multiple role assignments (e.g. Leader for Team A, HR for Styrelse B)
HR scope is explicitly assignable — an HR actor is granted the specific Organisation(s) they cover (e.g. several Styrelser under a ministry, listed explicitly); no subtree inheritance (ADR-035)
Role changes are infrequent (monthly at most) and take effect on next authentication
All role assignments must be auditable (who granted, when, expiration)

API enforcement:
All resource-keyed API endpoints must verify the actor's org scope covers the target resource's organization
Scope verification matches the target org against the actor's explicit org-set (ADR-035 flat model); scopes are embedded in JWT (no DB lookup per request). Materialized-path prefix matching is dormant for authorization
The target resource's org must be resolved at request time (e.g. employeeId → user's primaryOrgId → org's materializedPath)
Employees may only access their own data — ownership check only, no org scope check needed
Higher roles (Leader, HR, Admin) must pass org scope verification for cross-employee access
Failed scope checks must return 403 Forbidden with no data leakage
Scope enforcement must be auditable — failed access attempts should be logged

### G. Local Configuration

Local administrators can configure operational parameters within centrally defined constraints. Local configuration must never override centrally negotiated agreement rules.

Five controlled configuration areas:
1. **Working Time Planning** — settlement period (the calendar month by default; another period only by local agreement), planning start day, planning calendar

> *(S147, 2026-10-29: ADR-043 D1, D6; owner-approved spec update.)* In plain words: the norm period is no longer a number of weeks an institution picks. The state's rule is the calendar month; a local agreement may set another settlement period (*"medmindre andet er aftalt"*). S148 builds the one value `month`; other values are recorded for the domain expert (ADR-043 provisional item 19). Replaces "Norm period length (1/4/8/12 weeks)" and its S146 contradiction note. Decided; built in S148's config stage (the setting) and stage 1 (its effect on pay).
2. **Flex Rules** — each institution's own, in dated local settings, with no central flex cap: the plus cap, the minus cap and the daily credit cap (each unset unless the institution sets it), the cap-test period, the action above the cap (forfeit and record by default; or a manager's exception, a transfer to the overtime account, 1:1 cash by agreement), the leaving action, the paid-absence credit basis, office hours (an optional clock window per weekday), customary days off (§ C), warning thresholds. Whether a person is on the flex scheme at all is a dated fact HR records per person. *(S147, 2026-10-29: ADR-043 D6, D2, D3, D9; owner-approved spec update. In plain words: there is no central state flex agreement (PAV 19.5.1), so the central caps had no source; "within central limit" and "payout triggers" are withdrawn, and flex above a cap is forfeited and shown, not paid. Decided; the settings are built in S148's config stage, their pay effect in stage 1. Until then pay still runs on today's central caps, and flex above them is paid out.)*
3. **Organizational Structure** — Departments, cost centers, projects, approval chains
4. **Local Agreements (Lokalaftaler)** — Parameterized policies within central framework agreement boundaries
5. **Operational Configuration** — Approval flows, cutoff dates, lock periods, exemptions

Constraint enforcement:
Local values must respect central min/max boundaries where a central agreement sets one; there is no central flex cap, so an institution's flex caps have no central bound *(S147, 2026-10-29: ADR-043 D6; owner-approved spec update)*
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
Leaders approve/reject periods for employees. The system routes pending approvals to the employee's designated manager (ADR-027). Approval **authority** (ADR-035 flat model): the designated/effective approver — via the ADR-027 edge, including a unit leader — OR an HR/Admin actor by org-scope over the employee's Organisation. The unfloored leader-by-org-scope approval branch and the per-tree REQUIRED-mode `428` org-scope-fallback confirmation (ADR-027 D11) were retired in S94.
Only APPROVED periods may be exported to payroll
**Deadlines (owner-ratified 2026-09-08, provisional institutional defaults).** Each period carries two stored deadlines derived from its month end: the employee should submit by **month-end + 2 days**, the leader should approve by **month-end + 5 days**. The **payroll export cutoff is the leader's deadline** (month-end + 5). These were developer defaults nobody had ratified until S140; they are now the institutional defaults and are to become configurable per institution (see § G item 5, "cutoff dates"). They are derived in exactly one place — `InstitutionalDeadlines` in SharedKernel/Calendar — and are read by the HR follow-up surface, which is what makes a month's overdue state a fact rather than a caption (S140; the organisation page's "efter frist" tile previously counted every pending month regardless of any deadline)
A leader may reopen an APPROVED month **until it is sent to payroll**; once exported the month is locked — reopen is refused for all roles and changes go through retroactive corrections (ADR-034, S89/S90). Monthly payroll export is idempotent per (employee, month)
Rejection must include a reason
All status transitions must be auditable (who, when, reason)

Approval constraints:
A leader may only approve periods for employees in their assigned organizational scope
**Segregation of duties (four-eyes; owner OQ-1 = (a), S147).** A month carries two signatures from two different people. Whoever sent the month (the employee, or HR sending on the employee's behalf) can neither approve nor reject it. There is no exception path. Nobody approves or rejects their own month. Reopening is not a signature and stays allowed. The rule is enforced at the endpoint, inside the transaction that records the decision, and again at the payroll export (SEC-009, SEC-053; `docs/SECURITY.md` § "Segregation of duties")
Payroll export endpoint must enforce the approval guard — unapproved periods are blocked
Period approval does not affect the rule engine — it is a workflow gate before payroll export

### I. Agreement Configuration Management

Global Admins must be able to create, configure, and manage agreement rule configurations through the UI. Agreement configs are the central parameters that govern all rule evaluation — they must be database-backed and version-controlled.

#### Storage and Migration
Agreement configurations are stored in PostgreSQL as the single source of truth. On first deployment, the database is seeded from the initial static configs (AC, HK, PROSA, AC_RESEARCH, AC_TEACHING × OK24/OK26). After seeding, the database is authoritative — config changes do not require redeployment.

The rule engine remains pure: it receives `AgreementRuleConfig` as a parameter and never performs I/O. The service layer loads configs from the database and passes them to the rule engine.

> *(S147, 2026-10-29: ADR-043 D12; owner-approved spec update.)* In plain words: what a GlobalAdmin publishes here, and what an institution sets in its local profile, is what payroll calculates with. For each day of a month, payroll resolves the published agreement config, the position override and the institution's local profile in force that day (central → position → local) at the service layer, and passes them to the pure rule engine in the request; every value a calculation used is frozen into its proof. Each published agreement config and position override carries an "applies from" date. A publish never changes an exported month; an exported month changes only by an explicit correction. Decided; built in S148's config stage. Until then the rule engine reads configs compiled into the code, so these editors change screens and booking but not pay, and from S148 stage 0 they say so ("affects screens and booking; does not yet affect pay").

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
- **Norm & Flex**: WeeklyNormHours, NormPeriodWeeks, NormModel (WEEKLY_HOURS/ANNUAL_ACTIVITY), AnnualNormHours, MaxFlexBalance *(retires, ADR-043 D6; S148 stage 1)*, FlexCarryoverMax *(retires, ADR-043 D6; S148 stage 1)*
- **Overtime & Merarbejde**: HasOvertime, HasMerarbejde, OvertimeThreshold50 *(retires, ADR-043 D4; S148 stage 1)*, OvertimeThreshold100 *(retires, ADR-043 D4; S148 stage 1)*
- **Supplements**: Evening/Night/Weekend/Holiday enabled flags, time windows (Start/End hours), and rates
- **On-call & Call-in**: OnCallDutyEnabled, OnCallDutyRate *(retires, ADR-043 D11; S148 stage 2 or later)*, CallInWorkEnabled, CallInMinimumHours, CallInRate

> *(S147, 2026-10-30: ADR-043 D4, D6, D11; owner-approved spec update.)* The parameters marked "retires" stay in the code until the named stage builds their replacement: the institution's own flex caps (§ G), the flat +50 % for decided hours (§ L), and the three facts per time type above.
- **Travel, on-call and call-in**: TravelTimeEnabled; for each time type and agreement three facts, a worked-time fraction, a supplement basis and a rest flag, in place of the working and non-working travel rates *(S147, 2026-10-29: ADR-043 D11; owner-approved spec update. Decided; built in a later stage, S148 stage 2 or later)*

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
- OvertimeThreshold100 must be ≥ OvertimeThreshold50 *(retires with the thresholds, ADR-043 D4; S148 stage 1)*
- HasOvertime and HasMerarbejde should not both be true (warn, not block — per RES-001)
- NormModel = ANNUAL_ACTIVITY requires AnnualNormHours > 0
- NormModel = WEEKLY_HOURS makes AnnualNormHours irrelevant (hidden in UI)
- The settlement period is the calendar month; S148 builds the one value `month`, and other values are recorded for the domain expert

> *(S147, 2026-10-29: ADR-043 D1, D6; owner-approved spec update.)* Replaces "NormPeriodWeeks must be one of: 1, 2, 4, 8, 12", a week set that cannot express a month (QUAL-216). Decided; built in S148's config stage and stage 1. Until then `NormPeriodWeeks` and the week set stay in code.
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
- Part-time employees: day-count is FLAT (25/5 regardless of fraction — Ferieloven §5 stk.1, ADR-031); consumption is norm-based (`hours ÷ that day's scheduled norm`, the state Ferievejledning mechanism per ADR-032 — one scheduled workday off = 1 day at any fraction; no 5÷N conversion exists in the state-sector authority)
- Carryover: maximum 5 days transferred to next entitlement year (configurable per agreement: `VacationCarryoverMaxDays`)
- Remaining vacation must be planned before carryover deadline or is forfeited (with exceptions)
- Vacation reduces norm fulfillment for the period (hours = days × daily norm)
- The balance summary endpoint must show: total entitlement, used, planned, remaining, carryover from previous year

#### Special Holiday Allowance (Særlig feriegodtgørelse)
- 2,02% of annual salary (Cirkulære 021-24 §10, OK-2024 — superseded the older 1,5% rate) — handled by the payroll system. **Distinct** from the §17 **2½%** cash *godtgørelse* paid for unused *særlige feriedage* (the settlement mechanism — see *Vacation Settlement* above); do not conflate the two figures.
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

#### Vacation Settlement (Period-End Disposition & Execution)

Entitlement tracking (above) records what is *earned, used, and remaining*. **Settlement** is the period-end *execution* that disposes of the remaining balance at each entitlement boundary — the execution layer behind the year-overview disposition row (ADR-030 D9). Designed in **ADR-033** (S67); implemented in phased slices (S68+).

- **Money stays out of StatsTid.** The system holds no salary or base rate (`Amount = Hours × multiplier`); SLS owns all kroner. Settlement detects boundaries, executes the balance/state mutations, and emits **day/hour-count wage-type lines** for SLS to monetize — it never computes money.
- At each boundary the remaining balance partitions into legal buckets (verified §-spine, Ferieloven **LBK 152/2024** + state **Cirkulære 021-24**; `docs/references/vacation-settlement-law-research.md`):
  - **Transfer by written agreement** (Ferieloven §21, deadline 31 Dec) — the >4-week tranche carries to next year's balance. **Balance-only, no payroll line.**
  - **Automatic post-period payout** (§24) — an untaken, un-transferred 5th week is auto-paid after the ferieafholdelsesperiode. **Day-count payout line.**
  - **Feriehindring** (§22 auto-transfer ≤4 weeks; §25 payout if the impediment persists) — sickness/barsel spanning the period.
  - **Termination** (§26 payout of earned-untaken + §7 *modregning* of over-taken/forskud, capped at final pay — no clawback). **Day-count payout + deduction lines.**
  - **First-4-week forfeiture** (§34) — untaken, not-transferred, not-paid days lapse to Arbejdsmarkedets Feriefond.
  - **Særlige feriedage godtgørelse** (state-specific: Cirkulære 021-24 §15 stk.2 + §17, **2½%** cash on untaken days; **NOT** §12 stk.2, which is only the 1 May–30 Apr taking window). **Day-count line; SLS owns the 2½% rate.** (§17's 2½% ≠ §10's 2,02% general ferietillæg — distinct.)
- **Launch posture:** pre-launch, any un-automated settlement mechanism falls back to a **manual operator-recorded process** — nothing is missing at launch; the slices *replace manual with automated* per boundary (ADR-033 D13). No launch-blocking item is created.
- Settlement is **GLOBAL** (one national cirkulære — no per-institution override). Forfeiture (§34) and feriehindring (§22) are **never auto-executed pre-modeling**: the close fails closed to manual review rather than risk a wrongful forfeiture.

### L. Overtime Governance

In plain words: an hour worked above the month's norm is a flex hour unless a leader ordered it beforehand or accepted afterwards that it was necessary. Only those decided hours become overtime (HK, PROSA pay groups 1–2, +50 %) or count toward an AC merarbejde claim, and they leave the flex account, so one hour is never both. Governance is therefore a record of decisions about hours, not a flag on automatic overtime.

#### Decided Hours
- A leader may record an order, or a roster span, beforehand, naming the date and the clock span; the ordered hours are the overlap of the span with the hours actually worked
- Work nobody ordered in advance is marked by the employee in Skema as ordered or necessary; the leader accepts or rejects the mark in month approval (Teamoversigt). A mark is decided before its month is approved, by whoever holds that month's approval authority; the month cannot be approved while a mark is undecided
- Every record holds a date and a clock span; its kind (ordered beforehand, or accepted afterwards); a placement fact ("placement ordered or rostered", which the +25 % supplement and § 14's compensation read), kept apart from the surplus fact ("ordered or necessary"); an activity type; a creator, a decider and a status
- Four-eyes: a leader's order is decided at creation by its creator; no one decides their own hours (403); a record awaiting a decision is decided by someone other than its creator; the authority to order or decide is the authority to approve that employee's month, not bare organisation scope
- A decision made after the month is approved or exported lands as a dated movement in the month it is made; no exported month is reopened
- The records reach the pure rule engine as frozen inputs, frozen by value into each calculation's proof
- Where an institution sets office hours (§ G), a decided hour leaves flex only if it fell outside office hours and after the person's per-day norm amount (7 h 24 min for a full-timer); ordered weekend and public-holiday work stays under § 14 whatever the office hours

#### Maximum Overtime
- `MaxOvertimeHoursPerPeriod`: an optional warning over decided hours (per agreement or local setting); it never refuses or reclassifies an hour
- An exceeded warning is visible to the leader in the approval workflow

> *(S147, 2026-10-29: ADR-043 D3, D6; owner-approved spec update.)* Replaces "The system currently calculates overtime correctly but lacks governance controls", the overtime ceiling as a flag on automatic overtime, and the `OvertimeRequiresPreApproval` switch. Every overtime hour now needs a decision, so an optional pre-approval switch has no meaning and retires, with today's budget record, which names no hours. Decided; built in S148 stage 2. Until then: through stage 0 and the config stage, the code still pays overtime on every hour above a weekly norm; from stage 1 every hour above the month's norm goes to flex, ordered or not, with no +50 % until stage 2.

### M. Compensation Model (Afspadsering vs. Udbetaling)

The system must explicitly model the choice between time-off compensation (afspadsering) and monetary payout for overtime and merarbejde. These map to different SLS wage types.

#### Compensation Types
- **Afspadsering**: Overtime/merarbejde hours converted to time-off at the applicable rate (e.g., 1 hour overtime at 50% = 1.5 hours afspadsering)
- **Udbetaling**: Overtime/merarbejde hours paid out as salary supplement
- **Default model**: time off (afspadsering); pay only by the employer's recorded decision (see below) *(S147, 2026-10-30: ADR-043 D8; owner-approved spec update. Replaces "Configurable per agreement (`DefaultCompensationModel`: AFSPADSERING or UDBETALING)". Decided; built in S148 stage 2. Until then the per-agreement default is in code)*
- **The employer chooses, time off first**: the default is time off at +50 % (6 decided hours = 9 hours off); the leader who decides may choose pay instead when time off is judged impossible or inappropriate, recorded with a reason. The employee has no general choice, and `EmployeeCompensationChoice` retires
- **The employee's two elections**, each the employee's own act: agreeing to postpone an earning's time off by up to a year (a record both the decider and the employee sign), and choosing saved days (*opsparingsdage*) instead of pay at the deadline

> *(S147, 2026-10-29: ADR-043 D8; owner-approved spec update.)* In plain words: for HK, PROSA and AC the employer, not the employee, chooses time off or pay (PAV 19.2.7.1; `docs/references/overtime-flex-model-research.md` V10; `docs/references/agreement-cells-reverification-research.md` C7; QUAL-221), and the employee keeps two narrower choices of their own. PROSA's printed "1:1" becomes +50 %. Decided; built in S148 stage 2. Until then the seeds still let HK and PROSA employees choose.

#### Overtime Account
- Separate from flex balance — the overtime account holds decided hours only: § 8 overtime, § 14 weekend compensation, and AC merarbejde from the AC claims
- A ledger of dated movements, not a running total: *earned* (the period worked, the hours, the rate, the kind), *taken* as time off, *paid* (by a decision, or by the deadline's default), *postponed*, *converted* to saved days. Each movement is audited and frozen into the proof of the month it lands in; none edits an exported month
- The balance and each earning's deadline are derived by the pure rule engine from the movements, never a stored total anyone adjusts
- Every compensation draws on decided earnings, never on a bare balance; a decision about another person's account is made by whoever holds that person's month-approval authority, never on the decider's own hours
- Each earning has a clock: § 8 overtime is due as time off within 3 months of the end of the month worked, otherwise it is paid (a 30 September deadline pays in the October pay run); § 14 weekend compensation by the end of the following norm period. A leaders' "time off due" list and the employee's account screen show each earning with its deadline
- Afspadsering reduces the account and creates an absence entry: a Skema time-off-in-lieu day, credited as a planned day, drawing on the earning with the earliest deadline first
- Payout reduces the account and creates a payroll export line (hours and a wage type, never kroner)
- Conversion rates are agreement-aware (+50 % = 1.5 h off per decided hour; a different SLS code for payout)
- A leaver's owed time off is paid, never forfeited (in the leaving month: a labelled default)

#### Saved Days (Opsparingsdage)
- StatsTid holds a saved-days balance: the employee's election screen, a Skema "saved day" absence type, and the cap as a local parameter with the dated central floor (74 h, then 111 h from 1 April 2026, OK26 § 11 stk. 2)
- Designed; built after research on § 11 (ADR-043 R-A), in a sprint S148's refinement chooses. Until then an election is recorded as a movement, and a saved day taken is registered as paid leave

> *(S147, 2026-10-29: ADR-043 D8; owner-approved spec update.)* Replaces the yearly running total (`OvertimeBalance`). Decided; the account is built in S148 stage 2. Until then the yearly balance exists in code, and nothing in production feeds it.

#### Wage Type Mapping
- OVERTIME_50_PAYOUT → SLS code (monetary)
- OVERTIME_50_AFSPADSERING → SLS code (time-off)
- ~~OVERTIME_100_PAYOUT~~, ~~OVERTIME_100_AFSPADSERING~~: retire *(S147, 2026-10-30: ADR-043 D4, D8; owner-approved spec update. Overtime is a flat +50 %, so there is no 100 % rate. Decided; the line retires in S148 stage 1, the wage types in stage 2)*
- MERARBEJDE_PAYOUT → SLS code
- MERARBEJDE_AFSPADSERING → SLS code
- These must be added to the wage_type_mappings table
- **Vacation-settlement wage types** (§24/§26 payouts, §7 deduction, §15 stk.2/§17 særlige-feriedage godtgørelse) are day-count lines emitted by a *separate period-close emitter*, not the rule-engine path — specified in ADR-033 D7 + `docs/references/danish-agreements.md`. Only the særlige-feriedage godtgørelse løndele are SLS-verified today; the §24/§26/§7 contracts are per-slice Step-0 gates (ADR-033 D1).

### N. Employment Lifecycle & Time-Control (ADR-040)

Every employee-level fact is bounded in time, and the whole system obeys those bounds — the
governing model is **ADR-040 "Employee timeline & as-of resolution"** (owner-ratified 2026-08-25):

- **Employment window**: an employee starts on a date and leaves on a date (end date inclusive =
  last day employed). Conceptually employment is a list of non-overlapping **spells** (re-hire = a
  later spell; deferred increment — a completed spell is never silently overwritten). NULL dates
  mean unbounded on that side.
- **Enforcement**: registration (time/absence/skema) outside the window is refused with date-free
  errors; window edits that would strand existing registered data are refused; approval keeps
  whole-month geometry with content window-clean by construction; access for departed employees is
  role-gated (HR corrections via the terminated-inclusive paths).
- **A leaver's last days**: the leaver's own access ends the day after their last working day,
  whatever their role: login is refused and nothing of their own can be changed. Their manager keeps
  5 calendar days after that day to approve, reject or reopen the final month; from day 6 only HR may
  act, and payroll pays the month. The month's deadline and payroll cutoff do not move. A departed
  manager's authority over others ends the day after their own last day; a stand-in appointed before
  they left keeps signing until its end date or until HR revokes it. *(S147, `2026-11-03`: ADR-040
  amendment 2026-11-03; owner rulings OQ-2 and Q-B = (c); owner-approved spec update)*
- **As-of resolution**: what was true on date X — position, part-time fraction, agreement code,
  OK-version (a pure function of the date, ADR-003), employment category, and (model decided,
  implementation a named follow-up) org/unit membership — resolves from the dated history, never
  from "current" values, for every past-dated read and calculation.
- **Calculation**: employment and profile-change boundaries split payroll segments (typed
  EMPLOYED/NOT_EMPLOYED segments; non-employed spans evaluate no rules and export no lines but stay
  explicit in the manifest); monthly accrual pro-rates at start and caps at the end date —
  IMMEDIATE-grant entitlements (care/child-sick/senior days) keep full quota on hire pending
  Phase B domain sourcing (ADR-040 D9 ratified default).
- **Temporal editing**: changes may be future-dated (apply on their date) or backdated (recorded
  truthfully; exported months affected by a backdate land on an HR diagnostic worklist for the
  audited manual recalculation path — never auto-cascaded, per ADR-013).
- Employment dates are HR-scoped: never serialized into employee-facing DTOs, JWTs, exports, or
  error bodies.

## AC-Specific Requirements

AC employees differ fundamentally.
System must support:
Disabling overtime calculation for specific groups
Norm-based tracking instead of hour-based logic
Position-based rule overrides
Travel time as the only special time type; no on-call *(S147, 2026-10-30: ADR-043 D11; owner-approved spec update. Replaces "On-call obligations")*
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
