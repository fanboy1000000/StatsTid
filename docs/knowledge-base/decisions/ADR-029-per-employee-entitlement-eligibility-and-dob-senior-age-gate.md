# ADR-029 — Per-employee entitlement eligibility (child-sick) + DOB-derived senior-day age gate

| Field | Value |
|-------|-------|
| **Status** | accepted |
| **Landed** | S59 (projection annotation; binds to the architectural event "introduction of per-employee entitlement eligibility", not the sprint number — per WORKFLOW.md § Binding to Architectural Events) |
| **Domains** | Data Model, Infrastructure, Backend, Rule Engine, Frontend, Security |
| **Supersedes/Amends** | Amends ADR-025 D3 (`birth_date` placeholder → real column); establishes precedence over the dormant ADR-024 role layer |
| **Refinement** | `.claude/refinements/REFINEMENT-per-employee-entitlement-eligibility.md` (dual-lens reviewed) |

## Context

Child sick days (barns sygedag) and senior days (seniordage) are not available to every employee, but the system granted them implicitly to anyone on a covered agreement (eligibility was only the presence/absence of an `entitlement_balances` row — no explicit, audited, enforced switch). An OK analysis (`docs/references/danish-agreements.md:107-117`) showed the two entitlements have **different eligibility natures**:

- **Senior days**: eligibility is defined **purely by age (≥62)**, uniform across all five agreements (S37 TASK-3703). `entitlement_configs.min_age=62` already encoded this but was **never enforced** because no birthdate was stored. A manual per-employee toggle would mis-model the agreement and risk HR forgetting → an employee silently losing a statutory benefit.
- **Child sick days**: eligibility tracks whether the employee **has a qualifying child** — an individual family fact, not role- or age-based; the system cannot infer it.

Three existing mechanisms were evaluated and rejected as homes: `role_config_overrides` (per-role, no entitlement disabler, dormant — ADR-024 cutover suspended), `absence_type_visibility` (per-org, display-only, unenforced), and `entitlement_balances` (per-year, mutable, no version/audit/dating).

## Decision

**D1 — Two distinct mechanisms.** Senior eligibility is age-derived; child-sick eligibility is a per-employee opt-in flag. They are NOT unified.

**D2 — Child-sick: per-employee, event-sourced eligibility.** New table `employee_entitlement_eligibility` (employee_id, entitlement_type, eligible, effective_from/effective_to, version) + `_audit`, with a partial-unique "live" index enforcing no overlapping active rows per (employee_id, entitlement_type) (ADR-019/020 dated-read determinism). New event `EmployeeEntitlementEligibilitySet` (ADR-018 outbox + sync-in-tx projection) with an ADR-026 audit-projection mapper (TENANT_TARGETED, target-org = employee→users.primary_org_id). **Default = ineligible (opt-in); absent row ⇒ ineligible**, applied identically in GET and POST. Storage/event are generic, but **write/API/UI/enforcement are restricted to `CHILD_SICK`** (the endpoint rejects any other entitlement_type). HR-set (`HROrAbove` + `OrgScopeValidator` + If-Match).

**D3 — Senior: DOB-derived age gate in the rule engine.** New nullable `birth_date` on the `users` record (standing personal fact; NOT on the dated `employee_profiles`). `ValidateEntitlementRequest` gains nullable `MinAge` + `EmployeeAgeAsOfAbsenceDate`; `EntitlementValidationRule` gains an age-gate branch evaluated **before** per-episode/quota. The Backend passes the employee's **integer age computed as-of `absence.Date`** (DOB never crosses the rule-engine boundary → replay-deterministic, ADR-002). **Null/unknown DOB ⇒ fail-closed** (SENIOR_DAY rejected). Senior validation is **per absence row** (a 62nd birthday mid-month correctly allows later rows, rejects earlier ones in one save). No `SENIOR_DAY` row ever exists in `employee_entitlement_eligibility`.

**D4 — Enforcement placement.** Child-sick eligibility is a per-employee **fact gate, enforced inline in the Backend** Skema GET (`/month`, as-of month-end, display affordance) + POST (`/save`, as-of `absence.Date`, authoritative) — NOT the rule engine. Senior age is **agreement config (`min_age`)**, so its gate lives **in the rule engine**. This split respects the priority order (P2 deterministic rule engine = agreement math; per-employee facts stay Backend-local).

**D5 — GET/POST anchors differ by design.** GET uses month-end (what may be newly registered this month); POST uses per-`absence.Date` (authoritative). "Parity" means same projection + same absent-row default, NOT identical per-date verdicts. Forward-only (admin sets `effective_from` = today, ADR-023 D8); a mid-month toggle/birthday only diverges in the desired direction.

**D6 — Precedence over ADR-024.** Per-employee eligibility (D2) is authoritative at the endpoint. If the dormant role-config-override layer is ever wired into config resolution and gains entitlement disablers, role-level disablers gate availability one layer up and **per-employee eligibility can only further-restrict, never re-enable** a role-disabled type.

**D7 — DOB / GDPR (amends ADR-025 D3).** `birth_date` becomes a real column. It is access-controlled (`HROrAbove`+OrgScope on both read and write), never exposed in any Employee-facing DTO/JWT/export, and audited via `users_audit`. **DOB erasure is deferred WITH the rest of ADR-025 D3** (the `UserPiiErased` endpoint is still design-only) — a named, documented compliance gap, not a silent omission. `birth_date` is already listed among ADR-025 D3's erasable PII.

**D8 — No production migration.** Pre-production; test data is re-seeded. Opt-in default applies cleanly with no backfill of existing holders. (Audit-projection rebuild replays events only; it never seeds eligibility rows.)

## Consequences

- Senior days are now correctly age-gated automatically (wires up the dormant `min_age=62`); a `DefaultEntitlementConfigs.CreateSeniorDay` drift (stale 0 quota / age 60) was reconciled to 2/62 to match the DB seed.
- Child-sick registration is opt-in: until HR grants eligibility, child-sick rows are hidden (GET) and rejected (POST 422 `absence_type_not_eligible`).
- Determinism preserved: both enforcement reads are dated/as-of; the rule engine receives only a derived integer age.
- Open compliance item: DOB Article-17 erasure ships with ADR-025 D3 implementation.
