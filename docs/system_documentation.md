# StatsTid — System Documentation

> A comprehensive guide to the StatsTid platform: what it does, how it's built, and how we develop it.
> Written for product managers, technical leads, and new team members.

---

## Table of Contents

**Part I — The Product**
1. [What is StatsTid?](#1-what-is-statstid)
2. [Domain Context: Danish State Employment](#2-domain-context-danish-state-employment)
3. [How Data Flows: Time Entry to Payroll](#3-how-data-flows-time-entry-to-payroll)
4. [The Rule Engine](#4-the-rule-engine)
5. [Agreements and OK Versions](#5-agreements-and-ok-versions)
6. [Organizational Hierarchy](#6-organizational-hierarchy)
7. [Roles and Access Control](#7-roles-and-access-control)
8. [The Approval Workflow](#8-the-approval-workflow)
9. [The Frontend — Skema](#9-the-frontend--skema)
10. [Payroll Integration](#10-payroll-integration)
11. [Local Configuration](#11-local-configuration)
12. [Entitlements and Balances](#12-entitlements-and-balances)

**Part II — The Architecture**
13. [System Architecture](#13-system-architecture)
14. [Event Sourcing — How We Store Data](#14-event-sourcing--how-we-store-data)
15. [Technology Stack](#15-technology-stack)

**Part III — How We Develop**
16. [AI-Driven Multi-Agent Engineering](#16-ai-driven-multi-agent-engineering)
17. [The Agent Roster](#17-the-agent-roster)
18. [Governance Documents](#18-governance-documents)
19. [The Knowledge Base](#19-the-knowledge-base)
20. [Sprint Execution Workflow](#20-sprint-execution-workflow)
21. [Validation Pipeline](#21-validation-pipeline)
22. [Quality Assurance](#22-quality-assurance)
23. [Entropy Management and Harness Evolution](#23-entropy-management-and-harness-evolution)

**Reference**
24. [Current Status and Roadmap](#24-current-status-and-roadmap)
25. [Glossary](#25-glossary)

---

# Part I — The Product

## 1. What is StatsTid?

StatsTid is a time registration and agreement engine for Danish state-sector employees. It handles the full lifecycle of working time — from the moment an employee logs their hours, through complex rule evaluation (overtime, supplements, flex balance, entitlements), all the way to payroll export in the SLS format used by Danish government payroll systems.

**Why is this hard?** Danish state employment involves multiple collective agreements (AC for academics, HK for office staff, PROSA for IT workers), each with fundamentally different rules for overtime, supplements, and flex. These rules change when new agreements are negotiated (OK24 → OK26), and the system must correctly apply the right rules based on *when* the work happened — not when the calculation runs. Every calculation must be deterministic, auditable, and reproducible.

**Core capabilities:**
- Daily time registration on projects and absence types via a monthly spreadsheet (Skema)
- Automatic calculation of norm compliance, overtime, supplements, flex balance
- Entitlement tracking (vacation, care days, senior days, child sick days) with quota validation
- Two-step approval workflow (employee → manager)
- Payroll export with full traceability (every exported wage line traces back to a rule and a time entry)
- Multi-organization hierarchy with role-based access control (5 roles, org-scoped)
- Agreement configuration management with lifecycle (Draft → Active → Archived)
- Position-based rule overrides (researchers, senior lecturers, etc.)
- Retroactive recalculation across OK version transitions
- Event-sourced data store — nothing is ever deleted, everything is auditable

**Key user roles:**

| Role | What they do in StatsTid |
|------|--------------------------|
| Employee | Registers time daily via Skema, tracks flex/vacation/entitlement balances, submits periods for approval |
| Local Leader | Approves/rejects employee time periods, monitors compliance warnings, oversees team balances |
| Local HR | Views/edits all employees' time registrations within organizational scope |
| Local Admin | Configures local working-time rules within central agreement constraints, manages projects and org structure |
| Global Admin | Manages agreement configurations, position overrides, wage type mappings, OK version transitions |

---

## 2. Domain Context: Danish State Employment

Understanding StatsTid requires context on Danish state employment rules. These rules drive most of the system's complexity.

### Collective Agreements (Overenskomster)

Danish state employees are covered by collective agreements negotiated between unions and the employer (Medarbejder- og Kompetencestyrelsen). The main agreements supported:

- **AC** (Akademikernes Centralorganisation) — academics, researchers, senior lecturers, PhD fellows. Typically uses *merarbejde* (extra work without overtime supplements), position-based rule overrides, and annual norm models for research/teaching positions.
- **HK** (Handels- og Kontorfunktionærernes Forbund) — office and administrative staff. Traditional overtime with 50% and 100% supplement tiers, on-call duty at ⅓ rate.
- **PROSA** (Forbundet af It-professionelle) — IT professionals. Similar to HK for overtime but with system-work-outside-hours provisions.

Each agreement defines parameters for: norm hours, flex rules, overtime thresholds, supplement rates (evening, night, weekend, holiday), on-call duty, travel time, and absence entitlements.

### OK Versions

Agreements are renegotiated periodically. "OK24" means the agreement version effective from the 2024 negotiation round; "OK26" is the next. When a new version takes effect:

- Rates, thresholds, and rules may change
- The system applies the **correct version based on the date the work was performed** — not when the calculation runs
- An employee's January 2025 hours are always calculated under OK24 rules, even if recalculated in 2027

### Norm Time and Flex

The standard working week is 37 hours. Employees working more accumulate positive flex; working less creates negative flex. Some agreements use multi-week norm periods (4, 8, or 12 weeks) where the 37-hour target is averaged. Academic positions may use annual activity norms (e.g., 1924 hours/year for research) instead of weekly hours.

### Merarbejde vs. Overarbejde

A critical Danish distinction:
- **Merarbejde** (extra work): Hours beyond norm without overtime supplement — typical for AC employees
- **Overarbejde** (overtime): Hours beyond norm with mandatory 50% or 100% supplement — typical for HK/PROSA

The system must never apply overtime logic to an AC employee who should only get merarbejde, and vice versa. This is enforced by agreement configuration, not by hardcoded rules.

---

## 3. How Data Flows: Time Entry to Payroll

This is the core journey of the system:

```
┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐     ┌──────────┐
│ EMPLOYEE │     │ BACKEND  │     │  EVENT   │     │  RULE    │     │ PAYROLL  │
│ (Skema)  │     │   API    │     │  STORE   │     │  ENGINE  │     │ SERVICE  │
└────┬─────┘     └────┬─────┘     └────┬─────┘     └────┬─────┘     └────┬─────┘
     │                │                │                │                │
     │  1. Save hours │                │                │                │
     │───────────────>│                │                │                │
     │                │  2. Append     │                │                │
     │                │  event         │                │                │
     │                │───────────────>│                │                │
     │                │                │                │                │
     │  3. Employee   │                │                │                │
     │  approves month│                │                │                │
     │───────────────>│  4. Status →   │                │                │
     │                │  EMPLOYEE_     │                │                │
     │                │  APPROVED      │                │                │
     │                │───────────────>│                │                │
     │                │                │                │                │
     :    (manager reviews and approves — status → APPROVED)            │
     │                │                │                │                │
     │                │                │                │  5. Calculate  │
     │                │                │                │  & export      │
     │                │                │                │<───────────────│
     │                │                │                │                │
     │                │                │                │  6. Evaluate   │
     │                │                │                │  rules (HTTP)  │
     │                │                │                │───────────────>│
     │                │                │                │                │
     │                │                │                │  7. Results    │
     │                │                │                │<───────────────│
     │                │                │                │                │
     │                │                │                │  8. Map to     │
     │                │                │                │  SLS wage types│
     │                │                │                │                │
     │                │                │                │  9. Export ───>│ (SLS)
```

**Step by step:**

1. **Employee fills in Skema** — hours per project per day, absence days
2. **Each save becomes an immutable event** in the event store
3. **Employee approves their month** — status becomes `EMPLOYEE_APPROVED`
4. **Manager reviews and approves** — status becomes `APPROVED`
5. **Payroll export is triggered** — checks that the period is APPROVED (unapproved periods are blocked)
6. **Payroll calls the Rule Engine via HTTP** — norm, supplement, overtime, on-call, absence evaluated in parallel; flex balance calculated sequentially (depends on absence results)
7. **Rule Engine returns calculation results** — line items with time types, hours, and rates
8. **Payroll maps results to wage types** — e.g., `OVERTIME_50` → `SLS_0210`
9. **Formatted export is sent to the payroll system**

**Traceability chain:** Every line in the payroll export includes `SourceRuleId` and `SourceTimeType`, creating an unbroken chain: **time entry → domain event → rule evaluation → wage type → SLS export line**.

---

## 4. The Rule Engine

The Rule Engine is the mathematical heart of StatsTid. It is a pure calculation service: given inputs (time entries, agreement config, calendar), it returns deterministic outputs. It has:

- **No database access** — configs are passed in as parameters
- **No HTTP calls** — it is called by other services, never the reverse
- **No file I/O** — everything is in-memory
- **No randomness or time-dependence** — results are fully reproducible

### Why isolation matters

If the Rule Engine could read from a database, its output might change depending on database state, timing, or network conditions. By keeping it pure: **same input → same output, always.** This is critical for:
- Retroactive recalculation (replaying old periods with corrected rules)
- Legal auditability (proving a calculation was correct)
- Testing (no mocking needed — just pass in data)

### Rules implemented

| Rule | What it calculates |
|------|-------------------|
| **NormCheckRule** | Whether hours meet the weekly/multi-week/annual norm. Supports 1, 2, 4, 8, and 12-week periods plus annual activity norms. |
| **SupplementRule** | Evening, night, weekend, and holiday supplements based on time-of-day and calendar context. Precedence prevents double-dipping (holiday > weekend > evening/night). |
| **OvertimeRule** | 50% and 100% overtime tiers (HK/PROSA) or merarbejde tracking (AC). |
| **AbsenceRule** | Effects of vacation, sick days, care days, etc. on norm, flex, and payroll. |
| **FlexBalanceRule** | Running flex saldo: previous balance + worked + absence credits − norm. |
| **OnCallDutyRule** | On-call duty hours at configurable rate (e.g., ⅓ for HK/PROSA; disabled for AC). |
| **CallInWorkRule** | Call-in work with minimum-hours guarantee. |
| **TravelTimeRule** | Working travel (full rate) vs. non-working travel (half rate). |

### Configuration resolution chain

Agreement configs follow a three-layer merge:

```
Central config (database, with static fallback)
  └── Position override (e.g., RESEARCHER gets annual norm)
       └── Local override (per org unit, set by LocalAdmin)
            = Effective config passed to rule engine
```

This merging happens at the service layer (`ConfigResolutionService`), not inside the rule engine. The rule engine always receives a single, already-merged config — it doesn't know or care about the merge chain.

---

## 5. Agreements and OK Versions

### Agreement configuration

The system does not hardcode union-specific logic. Each agreement has a **configuration profile** (31 fields) that controls all behavior:

```
AC config:                          HK config:
  HasMerarbejde: true                 HasMerarbejde: false
  HasOvertime: false                  HasOvertime: true
  EveningSupplementEnabled: false     EveningSupplementEnabled: true
  OnCallDutyEnabled: false            OnCallDutyEnabled: true
  OnCallDutyRate: 0.0                 OnCallDutyRate: 0.33
  NormModel: WEEKLY_HOURS             NormModel: WEEKLY_HOURS
```

Academic sub-configs (AC_RESEARCH, AC_TEACHING) use `NormModel: ANNUAL_ACTIVITY` with position-specific annual hours.

### Configuration lifecycle

Agreement configs are database-backed with an immutable versioning lifecycle:

```
DRAFT → ACTIVE → ARCHIVED
```

- **DRAFT**: Being configured. Editable. Not used for rule evaluation.
- **ACTIVE**: In effect. Immutable. At most one ACTIVE per (AgreementCode, OkVersion).
- **ARCHIVED**: Previous version, preserved for retroactive recalculation and audit.

Publishing a DRAFT automatically archives the current ACTIVE config. GlobalAdmins manage this through the UI, including create, clone (even cross-agreement), edit, publish, archive, and side-by-side comparison.

### OK version resolution

The OK version is determined by the *entry date* (when the work happened), not the current date. This ensures retroactive recalculations produce stable results. When an OK transition date falls mid-period, the `RetroactiveCorrectionService` splits the period at the transition date and recalculates each segment under its applicable version.

---

## 6. Organizational Hierarchy

The Danish state has a layered organizational structure modeled as a tree:

```
Finansministeriet (Ministry)                          Path: /MIN01/
├── Økonomistyrelsen (Styrelse/Agency)                Path: /MIN01/STY01/
├── Statens It (Styrelse)                             Path: /MIN01/STY02/
│   ├── Drift (Afdeling/Department)                   Path: /MIN01/STY02/AFD01/
│   └── Udvikling (Afdeling)                          Path: /MIN01/STY02/AFD02/
└── Digitaliseringsstyrelsen (Styrelse)               Path: /MIN01/STY03/
```

Each organization stores a **materialized path** — a string like `/MIN01/STY02/AFD01/` encoding its full position. This makes subtree queries fast: "all organizations under Statens It" = all paths starting with `/MIN01/STY02/`.

**Why this matters:** Access control is scoped to organizations. A manager of "Statens It" can approve time for employees in Drift and Udvikling (descendants). The materialized path makes these checks a simple string prefix comparison — no recursive tree walking.

---

## 7. Roles and Access Control

### Five roles with org scope

| Role | What they can do | Scope |
|------|-----------------|-------|
| **Global Admin** | Manage agreements, organizations, all users, OK version transitions | All organizations |
| **Local Admin** | Configure local settings, manage local users, projects | Assigned org + descendants |
| **Local HR** | View and edit employee time data, organization statistics | Assigned org subtree |
| **Local Leader** | Approve/reject time periods, view team balances | Assigned team/org |
| **Employee** | Register time, view own data, submit periods | Own data only |

Each role assignment includes a scope type: `GLOBAL`, `ORG_AND_DESCENDANTS`, or `ORG_ONLY`. A user can hold multiple assignments (e.g., Leader for Team A and HR for a Styrelse).

### JWT-based stateless auth

Role scopes are embedded directly in the JWT token:

```json
{
  "sub": "emp001",
  "scopes": [
    { "role": "LocalLeader", "orgId": "STY02", "scopeType": "ORG_AND_DESCENDANTS" }
  ]
}
```

Most authorization checks don't need a database lookup — the scope travels with every request. The server validates the token signature (HMAC-SHA256, shared across all services) and checks the scope against the target resource's organization path. Failed scope checks return 403 with no data leakage and are logged for audit.

---

## 8. The Approval Workflow

Time registrations must be approved before payroll export. StatsTid uses a two-step flow:

```
┌─────────┐    ┌──────────────────┐    ┌───────────────┐
│  DRAFT  │───>│ EMPLOYEE_APPROVED │───>│   APPROVED    │──> payroll export allowed
└─────────┘    └──────────────────┘    └───────────────┘
     ▲                  │
     │                  └───> REJECTED (with reason) ──> back to DRAFT
     │
     └── REOPEN (manager can reopen for corrections)
```

**The journey of a monthly period:**

1. **DRAFT** — Employee fills in Skema throughout the month
2. **EMPLOYEE_APPROVED** — Employee clicks "Godkend måned" by deadline (last day of month + 2 days). Skema becomes read-only.
3. **APPROVED** — Manager reviews and approves (deadline: last day of month + 5 days). Period is now exportable.

The payroll export endpoint enforces an approval guard — unapproved periods cannot be exported.

---

## 9. The Frontend — Skema

The primary employee-facing interface is the **Skema** (schedule) — a monthly spreadsheet:

```
┌──────────────────────────────────────────────────────────────────┐
│  Min Tid — Marts 2026                        ◀ Februar  April ▶  │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌─── Balance Summary ──────────────────────────────────────┐   │
│  │ Flex: +12.5t  │ Ferie: 18/25 dage │ Norm: 148/148t │ ... │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  Timer:  ● 02:34:15    [Tjek ud]                                │
│                                                                  │
│  ┌──────────┬─────┬─────┬─────┬─────┬─────┬─────┬─────┬──────┐ │
│  │          │  1  │  2  │  3  │  4  │  5  │  6  │  7  │ Sum  │ │
│  │          │ Lø  │ Sø  │ Ma  │ Ti  │ On  │ To  │ Fr  │      │ │
│  ├──────────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┼──────┤ │
│  │DRIFT-01  │     │     │ 7.4 │ 7.4 │ 7.4 │ 7.4 │ 7.4 │ 37.0│ │
│  │PROJ-ALPHA│     │     │     │     │     │     │     │  0.0│ │
│  ├──────────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┼──────┤ │
│  │Ferie     │     │     │     │     │     │     │     │  0.0│ │
│  │Sygdom    │     │     │     │     │     │     │     │  0.0│ │
│  │Omsorgsdg │     │     │     │     │     │     │     │  0.0│ │
│  ├──────────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┼──────┤ │
│  │ Total    │ 0.0 │ 0.0 │ 7.4 │ 7.4 │ 7.4 │ 7.4 │ 7.4 │ 37.0│ │
│  └──────────┴─────┴─────┴─────┴─────┴─────┴─────┴─────┴──────┘ │
│                                                                  │
│  Status: Kladde                          [Godkend måned]         │
└──────────────────────────────────────────────────────────────────┘
```

**Structure:**
- **Columns:** Days of the month (1–28/30/31), with weekends highlighted
- **Project rows:** Configurable per organization by Local Admin
- **Absence rows:** Driven by the employee's agreement, with some types hideable by Local Admin
- **Balance summary:** Four cards showing flex saldo, vacation days, norm hours, and overtime/merarbejde
- **Timer:** Check-in/check-out clock that tracks arrival and departure
- **Approval footer:** Current status, deadlines, and approve button

**How saving works:** Each cell change is saved as a domain event. The grid reconstructs state from events — there's no separate "hours" table.

### Admin pages

| Page | Role | Purpose |
|------|------|---------|
| Approval Dashboard | Leader+ | Review and approve/reject employee periods |
| Org Management | LocalAdmin+ | Create and manage organizational units |
| User Management | LocalAdmin+ | Employee CRUD within org scope |
| Role Management | LocalAdmin+ | Assign/revoke roles with org scope |
| Config Management | LocalAdmin+ | Local working-time configuration |
| Overenskomster | GlobalAdmin | Agreement config lifecycle management |
| Positionstilpasninger | GlobalAdmin | Position-based rule override management |
| Lønartstilknytninger | GlobalAdmin | Wage type mapping administration |

---

## 10. Payroll Integration

The payroll pipeline maps rule engine outputs to SLS wage type codes:

### Wage type mapping

| Time Type | SLS Code | Description |
|-----------|----------|-------------|
| NORMAL_HOURS | SLS_0110 | Regular working hours |
| OVERTIME_50 | SLS_0210 | Overtime at 50% (HK/PROSA) |
| OVERTIME_100 | SLS_0220 | Overtime at 100% (HK/PROSA) |
| MERARBEJDE | SLS_0310 | Extra work, no supplement (AC) |
| VACATION | SLS_0410 | Vacation days |
| CARE_DAY | SLS_0420 | Care days |
| CHILD_SICK_DAY | SLS_0430 | Child's sick day |
| EVENING_SUPPLEMENT | SLS_0510 | Evening supplement |
| NIGHT_SUPPLEMENT | SLS_0520 | Night supplement |
| WEEKEND_SUPPLEMENT | SLS_0530 | Weekend supplement |
| ON_CALL_DUTY | SLS_0710 | On-call compensation |
| CALL_IN_WORK | SLS_0810 | Call-in work |
| TRAVEL_WORK | SLS_0820 | Working travel |
| NORM_DEVIATION | SLS_0150 | Academic norm deviation |

Mappings are versioned per OK version and agreement code, and position-aware (a researcher's entries may map to different codes than an administrator's).

### SLS export format

Pipe-delimited with invariant culture formatting:

```
emp001|SLS_0110|148.0|148.0|2026-03-01|2026-03-31|OK24|NORM_CHECK_37H|NORMAL_HOURS
emp001|SLS_0510|12.0|6.0|2026-03-01|2026-03-31|OK24|SUPPLEMENT_CALC|EVENING_SUPPLEMENT
```

### Retroactive corrections

When past periods need recalculation (e.g., agreement parameter was wrong, OK version transition mid-period):

1. The `RetroactiveCorrectionService` splits the period at the transition date
2. Each segment is recalculated under its applicable OK version
3. Delta lines are exported with correction prefixes (`HC|`, `C|`, `TC|`)
4. Corrections are single-period — they don't cascade across periods

---

## 11. Local Configuration

StatsTid supports layered configuration. Central agreement rules set boundaries; local administrators adjust within them.

```
Central Agreement (nationally negotiated)
  ── Overtime rates (cannot be overridden)
  ── Supplement rates (cannot be overridden)
  ── Max flex balance ceiling

  └── Local Configuration (per organization)
       ── Max flex balance (≤ central limit)
       ── Norm period length (1/4/8/12 weeks)
       ── Planning start day
       ── Approval cutoff dates
       ── Warning thresholds
```

**Key constraint:** Local values must respect central boundaries. If the central agreement sets max flex at 100 hours, a local admin can set 80 but not 120.

**Where merging happens:** `ConfigResolutionService` merges central + position override + local config at the service layer — *not* inside the rule engine. This preserves the rule engine's purity.

---

## 12. Entitlements and Balances

The system tracks annual entitlements for absence types with limited quotas:

| Entitlement | Annual Quota | Reset | Notes |
|-------------|-------------|-------|-------|
| **Vacation** (Ferie) | 25 days | Sep 1 (ferieår) | Part-time pro-rated. Max 5 days carryover. |
| **Special holiday days** (Feriefridage) | 5 days | Annual | Tracked separately from vacation. |
| **Care days** (Omsorgsdage) | 2 per child under 7 | Calendar year | Not transferable between years. |
| **Child sick days** (Barns sygedag) | 1–3 days | Per episode | Agreement-specific (1 statutory, 2–3 negotiated). |
| **Senior days** (Seniordage) | 1–5 days/year | Annual | Age-based (typically 60+), agreement-specific. |

Each entitlement has configurable parameters per agreement: quota, accrual model (immediate or monthly), reset date, carryover maximum, and part-time pro-rating. Absence registrations are validated against quotas — the system warns or rejects when a quota is exceeded.

The employee's "Min Tid" page shows balance summary cards for all tracked balances (flex, vacation, norm, overtime/merarbejde) with used/remaining/planned breakdowns.

---

# Part II — The Architecture

## 13. System Architecture

StatsTid runs as **8 Docker services** communicating over HTTP, backed by a single PostgreSQL database:

```
┌─────────────────────────────────────────────────────────────────────┐
│                         FRONTEND (React + Vite)                      │
│                    Min Tid · Approvals · Admin pages                  │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ HTTP (port 5100)
                               ▼
┌──────────────────────────────────────────────────────────────────────┐
│                        BACKEND API (.NET 8)                           │
│  12 endpoint groups: Auth, Time, Skema, Timer, Approval, Admin,      │
│  Config, Projects, Balance, AgreementConfig, PositionOverride,       │
│  WageTypeMapping                                                     │
│  Middleware: JWT Auth → RBAC → Org-Scope Validation → Audit Log      │
└────────┬──────────────────────┬──────────────────────┬───────────────┘
         │                      │                      │
         ▼                      ▼                      ▼
┌─────────────────┐  ┌──────────────────┐  ┌────────────────────┐
│  ORCHESTRATOR   │  │   RULE ENGINE    │  │     PAYROLL        │
│  :8082          │  │   :8081          │  │     :8083          │
│                 │  │                  │  │                    │
│ Batch calc      │  │ Pure functions   │  │ Wage type mapping  │
│ pipeline        │  │ zero I/O         │  │ SLS export         │
│ task dispatch   │  │ deterministic    │  │ retroactive corr.  │
└────────┬────────┘  └──────────────────┘  └─────────┬──────────┘
         │                                           │
         ▼                                           ▼
┌─────────────────┐                       ┌────────────────────┐
│    EXTERNAL     │                       │   MOCK PAYROLL     │
│    :8084        │                       │   :8085            │
│                 │                       └────────────────────┘
│ Circuit breaker │
│ idempotent      │
└────────┬────────┘
         ▼
┌─────────────────┐
│  MOCK EXTERNAL  │
│  :8086          │
└─────────────────┘

         All services ──────> PostgreSQL :5432
```

### Bounded contexts and dependency rules

```
SharedKernel (models, events, interfaces, config, calendar)
  └── RuleEngine (depends ONLY on SharedKernel — no DB, no HTTP, no I/O)
  └── Infrastructure (repositories, security, config resolution, event serializer)
       └── Backend API (HTTP gateway, endpoint groups)
       └── Payroll Integration (calls Rule Engine via HTTP, maps to SLS codes)
       └── External Integration (outbound APIs, circuit breaker, outbox delivery)
  └── Frontend (React SPA, communicates only with Backend API)
```

**Hard dependency rules:**
1. Rule Engine depends only on SharedKernel — zero I/O of any kind
2. Backend and Payroll call Rule Engine via HTTP only — never direct function calls
3. External integration failures never affect the deterministic core
4. Frontend communicates only with Backend API via relative `/api/` paths

---

## 14. Event Sourcing — How We Store Data

StatsTid uses **event sourcing** instead of traditional database updates:

- **Nothing is ever updated or deleted.** Every change is stored as a new immutable event.
- **The current state is computed** by replaying all events in order.
- **Complete audit trail is built-in** — who changed what, when, and why.

```
Traditional database:          Event-sourced database:
┌──────────────────────┐      ┌──────────────────────────────────────┐
│ employee_hours       │      │ events                               │
│                      │      │                                      │
│ emp001 | 2026-03-01  │      │ #1  TimeEntryRegistered              │
│ hours: 7.5           │      │     emp001 | 2026-03-01 | 7.4h       │
│ (overwritten)        │      │     actor: emp001 | 09:15 UTC        │
│                      │      │                                      │
│                      │      │ #2  TimeEntryRegistered              │
│                      │      │     emp001 | 2026-03-01 | 7.5h       │
│                      │      │     actor: emp001 | 14:30 UTC        │
│                      │      │     (correction — old value preserved)│
└──────────────────────┘      └──────────────────────────────────────┘
```

**Why this matters for a state system:**
- Legal compliance: prove what data existed at any point in time
- Retroactive recalculation: replay events with new rules
- Debugging: see exactly what happened and in what order
- No data loss: mistakes are corrected by adding new events, not overwriting

**Event structure:** Every event includes a unique ID, timestamp, stream/employee ID, event type, full data as JSON, actor ID, actor role, and correlation ID. The system has **29+ registered event types** spanning time registration, approval workflow, organizational changes, timer sessions, agreement config lifecycle, entitlements, and more.

**Outbox pattern:** When a service needs to notify others of an event, it writes to an outbox table in the same transaction. A separate process delivers events, guaranteeing at-least-once delivery even if a service is temporarily down.

---

## 15. Technology Stack

| Layer | Technology | Notes |
|-------|-----------|-------|
| Backend services | C# / .NET 8 Minimal APIs | No controllers, no MVC |
| Database | PostgreSQL 16, Npgsql | No ORM — direct SQL for full control |
| Event sourcing | Append-only event store + outbox | Custom tables, not a framework |
| Serialization | System.Text.Json | Explicit type map for event polymorphism |
| Authentication | JWT HMAC-SHA256 | Scope-embedded, shared key across services |
| Org hierarchy | Materialized path in PostgreSQL | `text_pattern_ops` index for prefix matching |
| Frontend | React 18 + TypeScript + Vite | SPA with role-based routing |
| UI components | 14 scratch-built + 6 Radix-wrapped | CSS Modules + custom properties |
| Design language | designsystem.dk-inspired | IBM Plex Sans, `#0059B3`, 0px radius, 1px borders |
| Containerization | Docker Compose | 8 services |
| Backend testing | xUnit | 407 unit + 15 regression tests |
| Frontend testing | vitest + @testing-library/react | 41 component/hook tests |

---

# Part III — How We Develop

## 16. AI-Driven Multi-Agent Engineering

StatsTid is developed using an AI-driven multi-agent architecture powered by Claude Code. This is not a single AI writing code — it is a structured engineering organization where specialized AI agents handle different domains under centralized governance.

### Why multi-agent?

Building a legally deterministic payroll system requires strict architectural discipline. A single agent working across the entire codebase tends to introduce cross-domain entanglement — the rule engine starts importing from the database layer, services bypass HTTP boundaries, and architectural constraints erode. The multi-agent approach prevents this by giving each agent a narrow scope with hard boundaries.

### How it works

```
┌──────────────────────────────────────────────────────────┐
│                    ORCHESTRATOR                            │
│                                                          │
│  Reads: CLAUDE.md, AGENTS.md, WORKFLOW.md, Knowledge Base │
│  Does: Decompose → Delegate → Validate → Merge → Commit  │
│  Does NOT: Write domain code (except trivial < 10 lines) │
└────────┬───────────────┬────────────────┬────────────────┘
         │               │                │
    ┌────▼────┐    ┌─────▼─────┐    ┌─────▼─────┐
    │  Rule   │    │  Payroll  │    │    UX     │    ... and more
    │  Engine │    │  Agent    │    │   Agent   │
    │  Agent  │    │           │    │           │
    │         │    │ Scope:    │    │ Scope:    │
    │ Scope:  │    │ Payroll/  │    │ frontend/ │
    │ Rule    │    │ only      │    │ only      │
    │ Engine/ │    └───────────┘    └───────────┘
    │ only    │
    └─────────┘
```

The **Orchestrator** (the top-level Claude Code instance) acts as a technical lead:

1. **Decomposes** sprint tasks into domain-scoped subtasks
2. **Delegates** each to a specialized agent with full context (role, scope, acceptance criteria, knowledge base entries)
3. **Parallelizes** independent agents using git worktrees for isolation
4. **Validates** all outputs through a multi-stage pipeline (constraint checks → reviewer audit → build/test)
5. **Merges** agent branches, resolves conflicts
6. **Records** decisions, findings, and sprint progress in governance artifacts

The Orchestrator never writes domain code directly (except for trivial changes under 10 lines in a single domain — the "Small Tasks Exception").

---

## 17. The Agent Roster

Seven specialized domain agents, plus two validation agents:

### Domain Agents

| Agent | File Scope | Responsibility |
|-------|-----------|----------------|
| **Rule Engine Agent** | `src/RuleEngine/**` | Pure rule functions, agreement config, supplement/overtime/absence/flex logic, OK version resolution. Must produce code with zero I/O. |
| **Data Model Agent** | `src/SharedKernel/**/Models/**`, `Events/**`, `Interfaces/**` | Domain events, value objects, DTOs, serialization type maps. Models must be immutable (init-only properties). |
| **Payroll Integration Agent** | `src/Integrations/**/Payroll/**` | Wage type mapping, SLS export, period calculation, retroactive corrections. Must maintain the traceability chain. |
| **API Integration Agent** | `src/Integrations/**/External/**` | Outbound integrations, circuit breaker, backoff, idempotency. External failures must never impact the core. |
| **Security Agent** | `src/Infrastructure/**/Security/**`, `src/Backend/**/Middleware/**` | Authentication, authorization, audit logging. Must not compromise architectural integrity. |
| **Test & QA Agent** | `tests/**` | Unit tests, regression tests, determinism proofs. Runs after all implementation agents complete. |
| **UX Agent** | `frontend/**` | React pages, components, hooks, routing, styling. Must consume backend APIs as-is — never drives backend decisions. |

### Validation Agents

| Agent | Role | Output |
|-------|------|--------|
| **Constraint Validator** | Fast mechanical rule checker. Runs on every agent output. | PASS or VIOLATION list |
| **Reviewer Agent** | Independent architectural audit for significant changes. Advisory only — never approves/rejects. | Findings: BLOCKER / WARNING / NOTE |

**Strict scope enforcement:** No agent may modify files outside its declared scope. If an agent encounters a cross-domain dependency (e.g., the Payroll Agent needs a new event type), it declares the dependency in its output — the Orchestrator then dispatches the Data Model Agent to create it.

---

## 18. Governance Documents

The project is governed by a hierarchy of documents that encode architectural decisions, patterns, and constraints. These are what make the multi-agent approach work — agents produce consistent output because they receive the same rules and context.

### Document map

| Document | Purpose | Who uses it |
|----------|---------|-------------|
| **CLAUDE.md** | Hub. Defines the priority order (P1–P9), links all other docs. | Orchestrator only |
| **SYSTEM_TARGET.md** | End-state product definition. 13 functional requirement areas (A–M). | Orchestrator extracts relevant sections for agent prompts |
| **ROADMAP.md** | Phased milestones, sprint planning (rolling detail), coverage tracker. | Orchestrator for planning |
| **docs/AGENTS.md** | All 7+2 agent definitions: file scopes, constraint validator checks, reviewer trigger criteria, prompt templates. | Orchestrator when spawning agents |
| **docs/WORKFLOW.md** | Mandatory workflow (steps 0–7), sprint management, entropy scans, quality grading, effectiveness metrics, harness evolution. | Orchestrator during execution |
| **docs/ARCHITECTURE.md** | Service topology, bounded contexts, dependency rules, ADR/PAT/DEP index. | All agents via context injection |
| **docs/SECURITY.md** | JWT patterns, RBAC, scope validation, known gotchas (FAIL-001). | Security Agent |
| **docs/FRONTEND.md** | Design system tokens, component library, routing, hooks, CSS conventions. | UX Agent |
| **docs/references/danish-agreements.md** | AC/HK/PROSA rules, entitlement quotas, wage type mappings. | Rule Engine Agent, Payroll Agent |
| **docs/generated/db-schema.md** | All 29+ tables with columns, keys, indexes (generated from init.sql). | Data Model Agent |
| **docs/QUALITY.md** | Per-domain quality grades (A–F), updated each sprint. | Orchestrator for sprint planning |
| **docs/knowledge-base/INDEX.md** | Master index of 26 knowledge base entries. | Orchestrator selects entries per agent |
| **docs/sprints/INDEX.md** | Sprint log index with task counts, test progression, constraint coverage. | Orchestrator for tracking |

### The priority order

Every decision respects this strict order. When concerns conflict, the higher priority wins:

| Priority | Principle | What it means |
|----------|-----------|--------------|
| **P1** | Architectural integrity | Bounded contexts, dependency rules, service isolation |
| **P2** | Deterministic rule engine | No I/O, no side effects, reproducible results |
| **P3** | Event sourcing and auditability | Append-only events, full audit trail |
| **P4** | Version correctness | OK version resolved by entry date |
| **P5** | Integration isolation | External failures can't affect the core |
| **P6** | Payroll integration correctness | Traceability from event to export |
| **P7** | Security and access control | JWT, RBAC, org-scope enforcement |
| **P8** | CI/CD enforcement | Builds pass, tests pass |
| **P9** | Usability and UX | Secondary to all of the above |

This isn't theoretical — it's enforced mechanically. The Constraint Validator checks P1, P2, and P7 on every output. The Reviewer audits P1–P4 on significant changes.

---

## 19. The Knowledge Base

The project maintains a structured, version-controlled knowledge base at `docs/knowledge-base/` — a formal repository of decisions, patterns, dependencies, conflict resolutions, and documented failures.

### Categories

| Category | Prefix | Count | Purpose | Examples |
|----------|--------|-------|---------|---------|
| Architectural Decision Records | ADR | 14 | Why we chose X over Y | ADR-002: Pure function rule engine; ADR-014: DB-backed agreement configs |
| Validated Patterns | PAT | 6 | How we do X | PAT-005: HTTP-only rule evaluation; PAT-006: Unified response format |
| Cross-Domain Dependencies | DEP | 4 | X depends on Y | DEP-003: EventSerializer must register all event types |
| Priority Conflict Resolutions | RES | 1 | When P2 conflicts with P9 | RES-001: AC has no overtime (agreement fidelity > feature parity) |
| Failure/Pivot Log | FAIL | 1 | What we tried that didn't work | FAIL-001: .NET 8 JWT claim remapping silently breaks custom claims |

### How it's used in practice

1. **Before each sprint**, the Orchestrator reads the index and identifies entries relevant to the upcoming tasks
2. **When spawning agents**, the Orchestrator includes specific KB entries in each agent's prompt (e.g., the Rule Engine Agent gets PAT-005 and ADR-002; the Security Agent gets FAIL-001)
3. **During implementation**, agents must respect all provided KB entries
4. **If an agent discovers new knowledge** (a pattern, dependency, decision, or failure), it includes a `PROPOSED KNOWLEDGE ENTRY` section in its output
5. **The Orchestrator reviews proposals** and creates entries — agents cannot write to the knowledge base directly

This creates institutional memory across sprints. A lesson learned in Sprint 3 (e.g., FAIL-001: JWT claim remapping) is automatically provided to agents working on auth-related tasks in Sprint 15.

---

## 20. Sprint Execution Workflow

Each sprint follows a mandatory 8-step workflow:

### Step 0 — Consult Knowledge Base
Read `docs/knowledge-base/INDEX.md` and identify entries relevant to the sprint's tasks.

### Step 0a — Pre-Sprint Entropy Scan
Run a lightweight scan to detect drift and accumulation before starting work:
- Validate all KB file path references still exist
- Grep for known anti-patterns (direct rule engine calls, `FindFirst` on array claims, hardcoded URLs, missing auth)
- Check for orphaned files from recent sprints
- Verify deferred items lists are current
- Update quality grades if the previous sprint changed domain quality

### Step 1 — Decompose
Break the sprint goal into domain-scoped subtasks. Each subtask maps to exactly one agent.

### Step 2 — Delegate
Spawn agents with their role, file scope, priority order, acceptance criteria, existing code context, and relevant KB entries.

### Step 3 — Parallelize
Run independent agents concurrently. Use git worktrees when multiple agents write to the repo simultaneously.

### Step 4 — Respect Phase Dependencies
```
Phase 1 (parallel): Data Model + Rule Engine + UX (independent domains)
Phase 2 (parallel): Payroll + API Integration (depend on Phase 1 outputs)
Phase 3 (sequential): Test & QA (depends on all implementation)
Phase 4 (sequential): Orchestrator validates — dotnet build && dotnet test
```

### Step 5 — Validate
Run the Constraint Validator, then the Reviewer (if trigger criteria are met), then build and test. Re-dispatch agents if violations are found.

### Step 5b/5c — Record
Review agent knowledge proposals. Record validated tasks in the sprint log with validation evidence.

### Steps 6–7 — Merge and Commit
Merge worktree branches, resolve conflicts, commit, push.

### Sprint log

Every sprint produces a formal log at `docs/sprints/SPRINT-N.md` containing:
- Task IDs, agents, components, KB references
- Acceptance criteria with pass/fail
- Files changed
- Orchestrator approval with date
- Legal and payroll verification table
- Reviewer findings (if applicable)
- Retrospective and knowledge base entries produced

---

## 21. Validation Pipeline

Every agent's output goes through multi-stage validation:

### Stage 1: Constraint Validator (mechanical, every output)

Fast automated checks that catch rule violations agents missed:

| Check | What it catches |
|-------|----------------|
| Cross-boundary imports | Backend importing from RuleEngine directly (must use HTTP) |
| DB access in rule engine | NpgsqlCommand or similar in RuleEngine code |
| Hardcoded URLs | `http://localhost` or service names instead of configuration |
| Unregistered events | New domain events missing from EventSerializer type map |
| Missing authorization | Endpoints without `RequireAuthorization` |
| Scope claim bug | `FindFirst("scopes")` instead of `FindAll` (FAIL-001 regression) |
| Scope violation | Files outside the agent's declared scope |

### Stage 2: Reviewer Agent (architectural, for significant changes)

An independent audit triggered for tasks touching:
- P1 (Architectural integrity) — always
- P2 (Deterministic rule engine) — always
- P3 (Event sourcing) — always
- P4 (Version correctness) — always
- Cross-domain changes — always
- New patterns or abstractions — always
- P5–P7 (integrations, payroll, security) — Orchestrator discretion

Skipped for: trivial changes, pure UI fixes, documentation-only changes.

The Reviewer produces findings at three severity levels:
- **BLOCKER**: Priority violation or architectural breach — strong signal to re-dispatch
- **WARNING**: Quality concern — address at Orchestrator discretion
- **NOTE**: Suggestion for improvement — no required action

The Reviewer advises. The Orchestrator decides. Authority is centralized.

### Stage 3: Build and Test

All outputs must pass `dotnet build` and `dotnet test`. Failures are traced to the responsible agent and re-dispatched with error context.

---

## 22. Quality Assurance

### Test counts (Sprint 15)

| Category | Count |
|----------|-------|
| Backend unit tests | 407 |
| Backend regression tests | 15 |
| Frontend tests (vitest) | 41 |
| **Total** | **422** |

### Quality grades by domain

| Domain | Grade | Key factors |
|--------|-------|-------------|
| Rule Engine | **A** | High coverage, full pattern compliance, pure functions |
| SharedKernel (Models) | **A** | Immutability tests, config tests |
| SharedKernel (Events) | **B+** | All registered in serializer, no dedicated event tests |
| Infrastructure | **B** | Repositories tested at integration level |
| Payroll Integration | **B** | Mapping, SLS format, and correction tests |
| Security | **B-** | Coverage via integration paths only — needs dedicated tests |
| Backend API | **B-** | Tested indirectly via smoke tests |
| Frontend | **C+** | 41 tests, no E2E or visual regression — priority improvement |

### What's proven by tests

- **Determinism**: Same inputs + same config → same outputs, verified across runs
- **OK version transitions**: Rules produce correct results for both OK24 and OK26
- **AC vs HK/PROSA behavioral separation**: Same time entries, different agreements → different results
- **Supplement precedence**: No double-dipping across tiers
- **Payroll traceability**: Every export line traces back to a rule and time entry
- **Regression guards**: 15 tests preventing previously discovered bugs from recurring

### Agent effectiveness metrics

The sprint index tracks agent performance to improve prompts and governance:

| Metric | Definition |
|--------|-----------|
| Tasks | Total tasks per sprint |
| Constraint Violations | Violations caught by Constraint Validator |
| Reviewer Findings | Count by severity (BLOCKER / WARNING / NOTE) |
| Re-dispatches | Agents sent back for corrections |
| First-Pass Rate | Tasks accepted without re-dispatch |

If first-pass rate drops below 80%, prompts are investigated. If the same violation recurs, it's escalated to a knowledge base entry.

---

## 23. Entropy Management and Harness Evolution

### Entropy scans

To prevent documentation drift and pattern decay, each sprint begins with a scan:
1. **KB path validation** — all file references still exist?
2. **Anti-pattern grep** — known violations present?
3. **Orphan detection** — unused files from recent sprints?
4. **Documentation drift** — deferred items list current?
5. **Quality grade review** — grades need updating?

Findings are classified as DRIFT (fix now), DEBT (note for later), or CLEAN.

### Harness evolution ("rippable harness" principle)

The governance structure is a harness — it constrains and corrects agent behavior. As the system matures and model capabilities improve, parts may become unnecessary overhead.

**Evaluation rules:**
- Every 5 sprints: review effectiveness metrics. Constraints with zero violations across 5+ sprints are candidates for relaxation.
- On model upgrade: reassess whether existing constraints are still necessary.
- On workflow friction: if a step consistently slows delivery without catching issues, mark it for evaluation.

The goal is **minimum effective governance**, not maximum governance.

---

# Reference

## 24. Current Status and Roadmap

### Completed (Sprints 1–15)

15 sprints completed, delivering **142 tasks** across all domains:

| Phase | Sprints | Key Deliverables |
|-------|---------|-----------------|
| Foundation | S1–S3 | 8-service Docker skeleton, event sourcing, first rules, JWT auth, RBAC, audit, CI/CD |
| Rule Engine + Payroll | S4–S5 | Absence completion, flex payout, PeriodCalculationService, SLS export, on-call, retroactive corrections |
| RBAC + Frontend | S6–S8 | 5-role RBAC, org hierarchy, scope-embedded JWT, design system, 20 UI components, 6 admin pages |
| Skema | S9 | Monthly spreadsheet, timer, two-step approval, project management |
| Advanced Rules | S10–S11 | Call-in, travel, multi-week norm, tech debt, retroactive OK split, position overrides, academic norms |
| Config Management | S12 | DB-backed agreement configs with lifecycle |
| Employee Experience | S13 | Unified "Min Tid" page, balance summary |
| Admin Management | S14 | Position override + wage type mapping UI |
| Entitlements | S15 | 5 entitlement types, quota validation, balance tracking |

### Remaining phases

| Phase | Sprint(s) | Focus |
|-------|-----------|-------|
| **3f — Compliance & Overtime Governance** | S16–S17 | EU working time compliance (11h rest, 48h/week ceiling), overtime pre-approval, compensation model (afspadsering vs. udbetaling) |
| **3g — UI/UX Refinements** | S18 | Visual consistency, responsive layout, accessibility, form validation, error states, navigation review |
| **4 — Production Hardening** | S19+ | Performance, monitoring, real SLS integration, load testing, security audit, operational runbooks |

### Coverage

Overall SYSTEM_TARGET.md functional coverage: **~86%** after Sprint 15, projected **~96%** after Phase 3g, **100%** after Phase 4.

---

## 25. Glossary

| Term | Danish | Meaning |
|------|--------|---------|
| **OK** | Overenskomst | Collective agreement between unions and the state |
| **OK24 / OK26** | — | Specific agreement versions (2024, 2026 negotiation rounds) |
| **AC** | Akademikernes Centralorganisation | Academic employees' union |
| **HK** | Handels- og Kontorfunktionærernes forbund | Office workers' union |
| **PROSA** | Forbundet af It-professionelle | IT professionals' union |
| **Merarbejde** | Merarbejde | Extra work beyond norm (AC — no supplement) |
| **Overarbejde** | Overarbejde | Overtime (HK/PROSA — with 50%/100% supplement) |
| **Afspadsering** | Afspadsering | Time-off compensation for overtime (instead of payout) |
| **Norm** | Normtid | Required working hours (typically 37h/week) |
| **Flex** | Flekssaldo | Balance of hours worked above/below norm |
| **Skema** | Skema | Schedule / monthly time registration spreadsheet |
| **Ferie** | Ferie | Vacation (25 days/year, ferieår Sep–Aug) |
| **Feriefridage** | Feriefridage | Special holiday days (typically 5/year) |
| **Omsorgsdage** | Omsorgsdage | Care days (2 per child under 7) |
| **Seniordage** | Seniordage | Senior days (age-based, 60+) |
| **Barns sygedag** | Barns sygedag | Child's sick day |
| **Rådighedsvagt** | Rådighedsvagt | On-call duty |
| **SLS** | Statens Løn System | The Danish state payroll system |
| **Styrelse** | Styrelse | Government agency (child of ministry) |
| **Afdeling** | Afdeling | Department (child of agency) |
| **Godkend** | Godkend | Approve |
| **Kladde** | Kladde | Draft |
| **Leder** | Leder | Manager/Leader |
| **Medarbejder** | Medarbejder | Employee |
| **ADR** | — | Architectural Decision Record (knowledge base entry) |
| **PAT** | — | Validated Pattern (knowledge base entry) |
| **DEP** | — | Cross-Domain Dependency (knowledge base entry) |

---

*This document is maintained by the Orchestrator. Last updated: Sprint 15 (2026-03-10). 422 tests, ~86% functional coverage.*
*See [ROADMAP.md](../ROADMAP.md) for detailed phase planning and [SYSTEM_TARGET.md](../SYSTEM_TARGET.md) for full requirements.*
