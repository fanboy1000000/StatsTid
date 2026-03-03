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
| **Global Admin** | All organizations | Manage state agreements (OK version transitions, e.g. OK24 → OK26), create/manage organizations, manage all users |
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
