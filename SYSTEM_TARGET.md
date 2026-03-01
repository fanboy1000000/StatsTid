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
