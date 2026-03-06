# StatsTid — System Documentation

> A production-grade time registration and agreement engine for Danish state employees.

---

## Table of Contents

1. [What is StatsTid?](#1-what-is-statstid)
2. [Architecture Overview](#2-architecture-overview)
3. [The Services](#3-the-services)
4. [How Data Flows: Time Entry to Payroll](#4-how-data-flows-time-entry-to-payroll)
5. [Event Sourcing — How We Store Data](#5-event-sourcing--how-we-store-data)
6. [The Rule Engine](#6-the-rule-engine)
7. [Agreements and OK Versions](#7-agreements-and-ok-versions)
8. [Organizational Hierarchy](#8-organizational-hierarchy)
9. [Roles and Access Control](#9-roles-and-access-control)
10. [The Approval Workflow](#10-the-approval-workflow)
11. [The Frontend — Skema](#11-the-frontend--skema)
12. [Payroll Integration](#12-payroll-integration)
13. [Local Configuration](#13-local-configuration)
14. [External Integrations](#14-external-integrations)
15. [Technology Stack](#15-technology-stack)
16. [Glossary](#16-glossary)

---

## 1. What is StatsTid?

StatsTid is a time registration system built specifically for Danish state employees. It handles the full lifecycle of working time — from the moment an employee logs their hours, through complex rule evaluation (overtime, supplements, flex balance), all the way to payroll export.

**Why is this hard?** Danish state employment involves multiple collective agreements (AC for academics, HK for office staff, PROSA for IT workers), each with different rules for overtime, supplements, and flex. These rules change when new agreements are negotiated (OK24, OK26, etc.), and the system must correctly apply the right rules based on *when* the work happened — not when the calculation runs. Every calculation must be deterministic, auditable, and reproducible.

**Core capabilities:**
- Daily time registration on projects and absence types
- Automatic calculation of norm compliance, overtime, supplements, flex balance
- Two-step approval workflow (employee → manager)
- Payroll export with full traceability (every exported wage line traces back to a rule and a time entry)
- Multi-organization hierarchy with role-based access control
- Event-sourced data store — nothing is ever deleted, everything is auditable

---

## 2. Architecture Overview

StatsTid runs as **8 Docker services** communicating over HTTP, backed by a single PostgreSQL database.

```
┌─────────────────────────────────────────────────────────────────────┐
│                         FRONTEND (React)                            │
│                    Skema · Approvals · Admin                        │
└──────────────────────────────┬──────────────────────────────────────┘
                               │ HTTP (port 5100)
                               ▼
┌──────────────────────────────────────────────────────────────────────┐
│                        BACKEND API                                   │
│  Auth · Time · Skema · Timer · Approval · Admin · Config · Projects  │
│                                                                      │
│  Middleware: JWT Auth → RBAC → Audit Logging → Correlation IDs       │
└────────┬──────────────────────┬──────────────────────┬───────────────┘
         │                      │                      │
         ▼                      ▼                      ▼
┌─────────────────┐  ┌──────────────────┐  ┌────────────────────┐
│  ORCHESTRATOR   │  │   RULE ENGINE    │  │     PAYROLL        │
│                 │  │                  │  │                    │
│ Task dispatch,  │  │ Pure functions,  │  │ Wage type mapping, │
│ batch calc,     │  │ no I/O, no DB,  │  │ SLS export,        │
│ pipeline ctrl   │  │ deterministic    │  │ retroactive corr.  │
└────────┬────────┘  └──────────────────┘  └─────────┬──────────┘
         │                                           │
         ▼                                           ▼
┌─────────────────┐                       ┌────────────────────┐
│    EXTERNAL     │                       │   MOCK PAYROLL     │
│                 │                       │   (SLS stand-in)   │
│ Outbound APIs,  │                       └────────────────────┘
│ circuit breaker, │
│ idempotent      │
└────────┬────────┘
         ▼
┌─────────────────┐
│  MOCK EXTERNAL  │
│  (test target)  │
└─────────────────┘

         All services share:
┌──────────────────────────────────────────────────────────────────────┐
│                      POSTGRESQL 16                                   │
│  Event store · Audit log · Organizations · Users · Roles · Config    │
│  Wage type mappings · Projects · Timer sessions · Approval periods   │
└──────────────────────────────────────────────────────────────────────┘
```

**Key design principle:** The Rule Engine has *zero* access to the database. It receives data via HTTP, evaluates pure functions, and returns results. This guarantees that rule evaluation is deterministic — given the same input, it always produces the same output.

---

## 3. The Services

### Backend API (port 5100)
The main gateway for the frontend and the system's "front door." It handles authentication, stores events, enforces access control, and routes data to other services. Organized into 8 endpoint groups:

| Group | Purpose |
|-------|---------|
| Auth | Login (JWT token issuance) |
| Time | Register time entries and absences |
| Skema | Monthly spreadsheet composite data |
| Timer | Check-in / check-out clock |
| Approval | Period submission and approval workflow |
| Admin | CRUD for organizations, users, roles |
| Config | Local configuration management |
| Projects | Project setup per organization |

### Rule Engine (port 5200)
A pure calculation service. It receives time entries, an agreement configuration, and an OK version — then returns calculated results (norm compliance, overtime, supplements, etc.). It has no database connection and no side effects.

**4 endpoints:**
- Evaluate a time rule (norm, supplement, overtime, on-call)
- Evaluate an absence rule (vacation, sick day, etc.)
- Evaluate flex balance
- List available rules for an OK version

### Orchestrator (port 5300)
Manages background batch operations — for example, calculating an entire period's worth of time entries across multiple rule types. Not to be confused with the development Orchestrator Agent (which is the AI agent that builds the system).

### Payroll (port 5400)
The bridge between rule results and the payroll system. It calls the Rule Engine via HTTP, maps the results to wage types (SLS codes), and formats exports. Also handles retroactive corrections.

### External (port 5500)
Handles outbound integrations to other Danish state systems. Event-driven, idempotent, with circuit breaker and retry logic. Designed so that external system failures never affect the core time registration or rule evaluation.

### Mock Payroll / Mock External (ports 5600, 5700)
Test doubles that simulate external payroll (SLS) and external API systems during development.

---

## 4. How Data Flows: Time Entry to Payroll

This is the core journey of the system. Here's what happens when an employee registers time and it eventually reaches payroll:

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
     :    (manager reviews and approves)                │                │
     │                │                │                │                │
     │                │                │                │  5. Calculate  │
     │                │                │                │  & export      │
     │                │                │                │<───────────────│
     │                │                │                │                │
     │                │                │  6. Read time  │                │
     │                │                │  entries       │                │
     │                │                │<───────────────│                │
     │                │                │                │                │
     │                │                │                │  7. Evaluate   │
     │                │                │                │  rules (HTTP)  │
     │                │                │                │───────────────>│
     │                │                │                │                │
     │                │                │                │  8. Norm,      │
     │                │                │                │  overtime,     │
     │                │                │                │  supplements,  │
     │                │                │                │  flex results  │
     │                │                │                │<───────────────│
     │                │                │                │                │
     │                │                │                │  9. Map to     │
     │                │                │                │  wage types    │
     │                │                │                │  (SLS codes)   │
     │                │                │                │                │
     │                │                │ 10. Emit event │                │
     │                │                │<───────────────│                │
     │                │                │                │                │
     │                │                │                │  11. Export    │
     │                │                │                │  to SLS ──────>│ (Mock)
     │                │                │                │                │
```

**Step by step:**

1. **Employee fills in their monthly spreadsheet** (Skema) — hours per project per day, absence days
2. **Each cell save becomes an event** — `TimeEntryRegistered` or `AbsenceRegistered`, appended to the event store
3. **Employee approves their month** — status transitions to `EMPLOYEE_APPROVED`
4. **Manager reviews and approves** — status transitions to `APPROVED`
5. **Payroll export is triggered** — the Payroll service checks that the period is approved (unapproved periods are blocked)
6. **Payroll reads the employee's time entries** from the event store
7. **Payroll calls the Rule Engine via HTTP** — five rule types are evaluated in parallel (norm, supplement, overtime, on-call, absence), then flex balance is calculated sequentially (because it depends on absence results)
8. **Rule Engine returns calculation results** — line items with time types, hours, and rates
9. **Payroll maps results to wage types** — e.g., `OVERTIME_50` → `SLS_0210`, `VACATION` → `SLS_0410`
10. **A `PeriodCalculationCompleted` event is emitted** to the event store for audit
11. **The formatted export is sent to the payroll system** (currently a mock SLS endpoint)

**Traceability chain:** Every line in the payroll export includes `SourceRuleId` and `SourceTimeType`, so you can trace backwards from any payroll line → the rule that produced it → the time entry that triggered it.

---

## 5. Event Sourcing — How We Store Data

StatsTid uses **event sourcing** instead of traditional database updates. This means:

- **Nothing is ever updated or deleted.** Every change is stored as a new event.
- **The current state is computed** by replaying all events in order.
- **You get a complete audit trail for free** — who changed what, when, and why.

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
- Legal compliance: you can prove what data existed at any point in time
- Retroactive recalculation: if agreement rules change, you can replay events with new rules
- Debugging: you can see exactly what happened and in what order
- No data loss: mistakes are corrected by adding new events, not by overwriting

**Event structure:** Every event includes:
- A unique ID and timestamp
- The employee/stream it belongs to
- The event type (e.g., `TimeEntryRegistered`, `PeriodApproved`)
- The full event data as JSON
- Actor ID, actor role, and correlation ID (for audit trail)

The system currently tracks **22 event types** across time registration, approval workflow, organizational changes, and timer sessions.

---

## 6. The Rule Engine

The Rule Engine is the mathematical heart of StatsTid. It evaluates working time rules and produces calculation results. It is deliberately isolated — no database, no side effects, no state.

### Why isolation matters

If the Rule Engine could read from a database, its output might change depending on database state, timing, or network conditions. By keeping it pure, we guarantee: **same input → same output, always.** This is critical for:
- Retroactive recalculation (replaying old periods with corrected rules)
- Legal auditability (proving a calculation was correct)
- Testing (no mocking needed — just pass in data)

### The five rule types

```
┌─────────────────────────────────────────────────────────────────┐
│                    RULE ENGINE EVALUATION                        │
│                                                                 │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐            │
│  │ NORM CHECK  │  │ SUPPLEMENT  │  │  OVERTIME   │            │
│  │             │  │             │  │             │  Parallel   │
│  │ 37h/week    │  │ Evening,    │  │ 50% / 100% │  ────────>  │
│  │ compliance  │  │ night,      │  │ or          │            │
│  │             │  │ weekend,    │  │ merarbejde  │            │
│  └─────────────┘  │ holiday     │  └─────────────┘            │
│                    └─────────────┘                              │
│  ┌─────────────┐  ┌─────────────┐                              │
│  │  ON-CALL    │  │  ABSENCE    │  Parallel                    │
│  │  DUTY       │  │             │  ────────>                   │
│  │  1/3 rate   │  │ Vacation,   │                              │
│  │             │  │ sick day,   │                              │
│  └─────────────┘  │ care day... │                              │
│                    └─────────────┘                              │
│                           │                                     │
│                           ▼                                     │
│                    ┌─────────────┐                              │
│                    │    FLEX     │  Sequential                  │
│                    │  BALANCE    │  (depends on absence)        │
│                    │             │                              │
│                    │ Previous +  │                              │
│                    │ worked −    │                              │
│                    │ norm        │                              │
│                    └─────────────┘                              │
└─────────────────────────────────────────────────────────────────┘
```

| Rule | What it does | Output |
|------|-------------|--------|
| **Norm Check** | Verifies the employee worked their required hours (37h/week standard, pro-rated for part-time) | Deviation from norm, fulfillment status |
| **Supplement** | Calculates evening, night, weekend, and holiday supplements based on when work occurred | Supplement line items with rates |
| **Overtime** | Calculates overtime (HK/PROSA) or merarbejde (AC) when hours exceed norm | Overtime line items at 50% or 100% rate |
| **On-Call Duty** | Calculates on-call compensation at 1/3 rate (HK/PROSA only; disabled for AC) | On-call line items |
| **Absence** | Processes vacation, sick days, care days, etc. — determines norm credits | Absence line items by type |
| **Flex Balance** | Computes running flex balance: previous + worked + absence credits − norm | New balance, payout if over max |

### Supplement precedence (no double-dipping)

When work falls into multiple supplement categories, only the highest-priority one applies:

```
Holiday supplement  >  Weekend supplement  >  Evening/Night supplement
     (highest)              (middle)              (lowest)
```

An employee working on Christmas Day (which falls on a Sunday) gets the holiday rate, not the weekend rate *plus* the holiday rate.

---

## 7. Agreements and OK Versions

### Collective agreements

Danish state employees are covered by collective agreements negotiated between unions and the state employer. StatsTid supports three agreements:

| Agreement | Covers | Key differences |
|-----------|--------|----------------|
| **AC** | Academics (universitetsansatte, jurister, etc.) | Merarbejde instead of overtime, no traditional supplements, on-call disabled |
| **HK** | Office and administrative staff | Full overtime (50%/100% tiers), all supplements enabled, on-call at 1/3 rate |
| **PROSA** | IT professionals | Similar to HK — overtime supplements, on-call enabled |

The system does not hardcode union-specific logic. Instead, each agreement has a **configuration profile** that enables/disables features and sets rates:

```
AC config:                          HK config:
  HasMerarbejde: true                 HasMerarbejde: false
  HasOvertime: false                  HasOvertime: true
  EveningSupplementEnabled: false     EveningSupplementEnabled: true
  OnCallDutyEnabled: false            OnCallDutyEnabled: true
  OnCallDutyRate: 0.0                 OnCallDutyRate: 0.33
```

### OK versions

"OK" stands for *Overenskomst* (collective agreement). Agreements are renegotiated periodically, producing new versions like OK24 (2024) and OK26 (2026). When a new version takes effect:

- Rates, thresholds, and rules may change
- The system must apply the **correct version based on the date the work was performed** — not the date the calculation runs
- This means an employee's January 2025 hours are always calculated under OK24 rules, even if you recalculate in 2027

**Version resolution rule:** The OK version is determined by the *entry date* (when the work happened), not the current date. This is a foundational architectural decision that ensures retroactive recalculations produce stable, correct results.

---

## 8. Organizational Hierarchy

The Danish state has a layered organizational structure. StatsTid models this as a tree:

```
Finansministeriet (Ministry)                          Path: /MIN01/
├── Økonomistyrelsen (Styrelse/Agency)                Path: /MIN01/STY01/
├── Statens It (Styrelse)                             Path: /MIN01/STY02/
│   ├── Drift (Afdeling/Department)                   Path: /MIN01/STY02/AFD01/
│   └── Udvikling (Afdeling)                          Path: /MIN01/STY02/AFD02/
└── Digitaliseringsstyrelsen (Styrelse)               Path: /MIN01/STY03/
```

Each organization stores a **materialized path** — a string like `/MIN01/STY02/AFD01/` that encodes its full position in the tree. This makes it fast to answer questions like "give me all organizations under Statens It" — just find all paths that start with `/MIN01/STY02/`.

**Why this matters:** Access control is scoped to organizations. A manager of "Statens It" can approve time for employees in Drift and Udvikling (descendants). A local admin at "Finansministeriet" can configure settings for the entire ministry tree. The materialized path makes these checks a simple string prefix comparison — no recursive tree walking needed.

---

## 9. Roles and Access Control

StatsTid implements 5 roles with organization-scoped access. A user's permissions depend on *which role* they have and *at which organization* it's assigned.

```
┌─────────────────────────────────────────────────┐
│              ROLE HIERARCHY                       │
│                                                   │
│  Global Admin     ← Manages everything           │
│       │                                           │
│  Local Admin      ← Configures within org scope   │
│       │                                           │
│  Local HR         ← Views/edits employee data     │
│       │                                           │
│  Local Leader     ← Approves time periods         │
│       │                                           │
│  Employee         ← Registers own time            │
│                                                   │
└─────────────────────────────────────────────────┘
```

| Role | What they can do | Scope |
|------|-----------------|-------|
| **Global Admin** | Manage agreements, create organizations, manage all users | All organizations |
| **Local Admin** | Configure local settings, manage local users, set up projects | Assigned org + descendants |
| **Local HR** | View and edit employee time data, organization statistics | Assigned org subtree |
| **Local Leader** | Approve/reject time periods, view team balances | Assigned team/org |
| **Employee** | Register time, view own data, submit periods | Own data only |

### How scope works

Each role assignment includes a **scope type**:
- `GLOBAL` — access to all organizations
- `ORG_AND_DESCENDANTS` — access to the assigned org and everything below it
- `ORG_ONLY` — access to just that specific org, not its children

A user can have multiple role assignments. For example, someone might be a Leader for Team A and HR for an entire Styrelse.

### JWT tokens

When a user logs in, their role scopes are embedded directly in the JWT token:

```json
{
  "sub": "emp001",
  "role": "LocalLeader",
  "org_id": "STY02",
  "agreement_code": "HK",
  "scopes": [
    { "role": "LocalLeader", "orgId": "STY02", "scopeType": "ORG_AND_DESCENDANTS" }
  ]
}
```

This means most authorization checks don't require a database lookup — the scope information travels with every request. The server validates the token signature (HMAC-SHA256, shared key across all services) and checks the scope against the target resource's organization path.

---

## 10. The Approval Workflow

Time registrations must be approved before they can be exported to payroll. StatsTid uses a two-step approval flow:

```
                    Employee corrects
                    ┌──────────┐
                    │          │
                    ▼          │
┌─────────┐    ┌──────────────────┐    ┌───────────────┐
│  DRAFT  │───>│ EMPLOYEE_APPROVED │───>│   APPROVED    │
│         │    │                  │    │   (final)     │
└─────────┘    └────────┬─────────┘    └───────────────┘
     ▲                  │
     │          Manager reopens         ┌───────────────┐
     │          (with reason)      ┌───>│   REJECTED    │
     └──────────────────┘          │    │  (with reason)│
                                   │    └───────┬───────┘
                    ┌──────────────┘            │
                    │                           │
              EMPLOYEE_APPROVED ────────────────┘
              or SUBMITTED
```

**The journey of a monthly period:**

1. **DRAFT** — Employee fills in their Skema throughout the month. Cells are editable.
2. **EMPLOYEE_APPROVED** — Employee clicks "Godkend måned" (Approve month) by the deadline (last day of month + 2 days). The Skema becomes read-only.
3. **APPROVED** — Manager reviews and approves (deadline: last day of month + 5 days). Period can now be exported to payroll.

**Alternative paths:**
- Manager can **reject** (with a reason) — employee must correct and re-approve
- Manager can **reopen** an employee-approved period — returns to DRAFT for corrections
- Rejected periods return to DRAFT for the employee to fix

**Approval guard:** The payroll export endpoint checks that a period has `APPROVED` status before allowing export. This prevents incomplete or unreviewed time data from reaching the payroll system.

---

## 11. The Frontend — Skema

The primary employee-facing interface is the **Skema** (schedule) — a monthly spreadsheet where employees register their working hours.

```
┌──────────────────────────────────────────────────────────────────┐
│  Skema — Marts 2026                           ◀ Februar  April ▶ │
├──────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Timer:  ● 02:34:15    [Tjek ud]                                │
│                                                                  │
│  ┌──────────┬─────┬─────┬─────┬─────┬─────┬─────┬─────┬──────┐ │
│  │          │  1  │  2  │  3  │  4  │  5  │  6  │  7  │ Sum  │ │
│  │          │ So  │ Ma  │ Ti  │ On  │ To  │ Fr  │ Lø  │      │ │
│  ├──────────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┼──────┤ │
│  │DRIFT-01  │     │ 7.4 │ 7.4 │ 7.4 │ 7.4 │ 7.4 │     │ 37.0│ │
│  │PROJ-ALPHA│     │     │     │     │     │     │     │  0.0│ │
│  ├──────────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┼──────┤ │
│  │Ferie     │     │     │     │     │     │     │     │  0.0│ │
│  │Sygdom    │     │     │     │     │     │     │     │  0.0│ │
│  │Omsorgsdg │     │     │     │     │     │     │     │  0.0│ │
│  ├──────────┼─────┼─────┼─────┼─────┼─────┼─────┼─────┼──────┤ │
│  │ Total    │ 0.0 │ 7.4 │ 7.4 │ 7.4 │ 7.4 │ 7.4 │ 0.0 │ 37.0│ │
│  └──────────┴─────┴─────┴─────┴─────┴─────┴─────┴─────┴──────┘ │
│                                                                  │
│  Status: Kladde                          [Godkend måned]         │
│  Medarbejderfrist: 2026-04-02                                    │
│  Lederfrist: 2026-04-05                                          │
└──────────────────────────────────────────────────────────────────┘
```

**Structure:**
- **Columns:** Days of the month (1–28/30/31), with weekends highlighted
- **Project rows:** Configurable per organization by Local Admin (e.g., DRIFT-01, PROJ-ALPHA)
- **Absence rows:** Driven by the employee's agreement (AC/HK/PROSA), with some types hideable by Local Admin
- **Row and column sums:** Auto-calculated
- **Timer:** Check-in/check-out clock that tracks arrival and departure
- **Approval footer:** Shows current status, deadlines, and the approve button

**How saving works:** Each cell change is debounced (batched for 1 second) and then saved as a domain event. The grid reconstructs its state by reading events for the month — there's no separate "hours" table.

### Navigation (role-based)

The sidebar shows different items based on the user's role:

| Menu item | Visible to |
|-----------|-----------|
| Skema | All employees |
| Mine perioder (My periods) | All employees |
| Godkendelser (Approvals) | Leaders and above |
| Medarbejdere (Employees) | HR and above |
| Organisation | Local Admin and above |
| Roller (Roles) | Local Admin and above |
| Projekter (Projects) | Local Admin and above |
| Konfiguration (Configuration) | Local Admin and above |

---

## 12. Payroll Integration

The payroll integration maps rule engine output to **wage types** (SLS codes) used by the Danish state payroll system.

### The mapping

Every time type produced by the rule engine has a corresponding wage type:

| Time Type | Wage Type | Description |
|-----------|-----------|-------------|
| NORMAL_HOURS | SLS_0110 | Regular working hours |
| OVERTIME_50 | SLS_0210 | Overtime at 50% supplement (HK/PROSA) |
| OVERTIME_100 | SLS_0220 | Overtime at 100% supplement (HK/PROSA) |
| MERARBEJDE | SLS_0310 | Extra work, no supplement (AC) |
| VACATION | SLS_0410 | Vacation days |
| CARE_DAY | SLS_0420 | Care days |
| CHILD_SICK_DAY | SLS_0430 | Child's sick day |
| PARENTAL_LEAVE | SLS_0440 | Parental leave |
| SENIOR_DAY | SLS_0450 | Senior day |
| EVENING_SUPPLEMENT | SLS_0510 | Evening supplement |
| NIGHT_SUPPLEMENT | SLS_0520 | Night supplement |
| WEEKEND_SUPPLEMENT | SLS_0530 | Weekend supplement |
| HOLIDAY_SUPPLEMENT | SLS_0540 | Holiday supplement |
| FLEX_PAYOUT | SLS_0610 | Flex balance payout |
| ON_CALL_DUTY | SLS_0710 | On-call duty compensation |

These mappings are versioned per OK version and agreement code, so the same time type can map to different wage types as agreements evolve.

### Traceability

Every exported payroll line includes:
- `SourceRuleId` — which rule produced this line (e.g., `OVERTIME_CALC`)
- `SourceTimeType` — which time type it represents (e.g., `OVERTIME_50`)
- `EmployeeId`, `PeriodStart`, `PeriodEnd`, `OkVersion`

This creates an unbroken chain: **time entry → domain event → rule evaluation → wage type → payroll export line → SLS file**.

### Retroactive corrections

When rules change or errors are found, the system can recalculate past periods. The retroactive correction flow:
1. A recalculation is requested (with a reason)
2. The system re-evaluates the period using the correct rules
3. Differences are computed and a correction export is generated
4. A `RetroactiveCorrectionRequested` event is emitted for audit

### SLS export format

The export uses a pipe-delimited format with invariant culture formatting (no locale-dependent decimal separators):

```
emp001|SLS_0110|148.0|148.0|2026-03-01|2026-03-31|OK24|NORM_CHECK_37H|NORMAL_HOURS
emp001|SLS_0510|12.0|6.0|2026-03-01|2026-03-31|OK24|SUPPLEMENT_CALC|EVENING_SUPPLEMENT
```

---

## 13. Local Configuration

StatsTid supports a layered configuration model. Central agreement rules (negotiated nationally) set boundaries; local administrators can adjust operational parameters within those boundaries.

```
┌─────────────────────────────────────────────────┐
│  Central Agreement (AC/HK/PROSA)                │
│  ─ Overtime rates (cannot be overridden)        │
│  ─ Supplement rates (cannot be overridden)      │
│  ─ Max flex balance ceiling                     │
│                                                 │
│  ┌─────────────────────────────────────────┐    │
│  │  Local Configuration (per organization)  │    │
│  │  ─ Max flex balance (≤ central limit)   │    │
│  │  ─ Norm period length (1/4/8/12 weeks)  │    │
│  │  ─ Planning start day                   │    │
│  │  ─ Approval cutoff dates               │    │
│  │  ─ Warning thresholds                   │    │
│  └─────────────────────────────────────────┘    │
└─────────────────────────────────────────────────┘
```

**Key constraint:** Local values must respect central boundaries. For example, if the central agreement sets max flex balance at 100 hours, a local admin can set their organization's limit to 80 hours but not 120 hours.

**Where merging happens:** The `ConfigResolutionService` merges central and local config at the service layer — *not* inside the rule engine. This preserves the rule engine's purity (it receives a pre-merged config as input).

**Five configuration areas:**
1. Working time planning (norm period, planning calendar)
2. Flex rules (max balance, warning thresholds, payout triggers)
3. Organizational structure (departments, cost centers, approval chains)
4. Local agreements (parameterized policies within framework boundaries)
5. Operational configuration (cutoff dates, lock periods, exemptions)

---

## 14. External Integrations

The External integration service handles all outbound communication with other Danish state systems. It's designed with the assumption that external systems are unreliable:

- **Asynchronous:** Integrations don't block the main application
- **Event-driven:** External sends are triggered by domain events (via the outbox pattern)
- **Idempotent:** Re-sending the same message has no additional effect
- **Circuit breaker:** If an external system starts failing, the integration temporarily stops trying and fails fast
- **Retry with backoff:** Failed deliveries are retried with increasing delays
- **Delivery tracking:** Every outbound message is tracked for status

**Critical design rule:** External system failures must *never* affect the deterministic core (rule engine, event store, approval workflow). If a payroll system is down, employees can still register time and managers can still approve periods — the export simply queues.

---

## 15. Technology Stack

| Layer | Technology |
|-------|-----------|
| **Backend** | C# / .NET 8 Minimal APIs |
| **Frontend** | React 18 + TypeScript + Vite |
| **UI Components** | Scratch-built + Radix primitives, CSS Modules, IBM Plex Sans |
| **Design Language** | Inspired by Det Fælles Designsystem (designsystem.dk) |
| **Database** | PostgreSQL 16 via Npgsql (no ORM) |
| **Event Store** | Custom PostgreSQL tables (append-only events + outbox) |
| **Auth** | JWT (HMAC-SHA256), shared signing key across services |
| **Containerization** | Docker Compose (8 services) |
| **Testing** | xUnit (backend, 242 tests) + Vitest (frontend, 33 tests) |
| **Serialization** | System.Text.Json with polymorphic type handling |
| **Architecture** | Event sourcing, outbox pattern, CQRS-lite |

---

## 16. Glossary

| Term | Danish | Meaning |
|------|--------|---------|
| **OK** | Overenskomst | Collective agreement between unions and the state |
| **OK24 / OK26** | — | Specific agreement versions (2024, 2026) |
| **AC** | Akademikernes Centralorganisation | Academic employees' union |
| **HK** | Handels- og Kontorfunktionærernes forbund | Office workers' union |
| **PROSA** | — | IT professionals' union |
| **Merarbejde** | Merarbejde | Extra work beyond norm (AC — no supplement, just hours) |
| **Overarbejde** | Overarbejde | Overtime (HK/PROSA — with 50%/100% supplement) |
| **Norm** | Normtid | Required weekly hours (typically 37h) |
| **Flex** | Flekssaldo | Balance of hours worked above/below norm |
| **Skema** | Skema | Schedule / monthly time registration spreadsheet |
| **Ferie** | Ferie | Vacation |
| **Sygdom** | Sygdom | Sick leave |
| **Omsorgsdage** | Omsorgsdage | Care days |
| **Rådighedsvagt** | Rådighedsvagt | On-call duty |
| **SLS** | Statens Løn System | The Danish state payroll system |
| **Styrelse** | Styrelse | Government agency (child of ministry) |
| **Afdeling** | Afdeling | Department (child of agency) |
| **Godkend** | Godkend | Approve |
| **Kladde** | Kladde | Draft |
| **Leder** | Leder | Manager/Leader |
| **Medarbejder** | Medarbejder | Employee |

---

*Document generated from system state as of Sprint 9 (275 tests, ~93% functional coverage). See [ROADMAP.md](../ROADMAP.md) for planned features and [SYSTEM_TARGET.md](../SYSTEM_TARGET.md) for full requirements.*
