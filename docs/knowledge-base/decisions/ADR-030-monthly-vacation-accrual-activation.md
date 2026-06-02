# ADR-030 — Activate monthly vacation accrual (MONTHLY_ACCRUAL)

| Field | Value |
|-------|-------|
| **Status** | accepted |
| **Landed** | S60 (projection annotation; binds to the architectural event "activation of monthly accrual", not the sprint number — per WORKFLOW.md § Binding to Architectural Events) |
| **Domains** | Rule Engine, Data Model, Infrastructure, Backend, Frontend, Security |
| **Supersedes/Amends** | Supersedes ADR-021 **D6** (MONTHLY_ACCRUAL no longer dead code); annotates ADR-021 **D5** (records the activation event — D5 was accurate as of S30, NOT "corrected") |
| **Refinement** | `.claude/refinements/REFINEMENT-monthly-vacation-accrual.md` (forskud policy from cited deep-research: Ferieloven §§5/7, STAR, Medst. ferieaftale §3/§8/§13) |

## Context

The system granted the full annual vacation quota IMMEDIATE-ly at ferieår start (ADR-021 D6 deferred real accrual as "Phase 5+ work"). Danish Ferieloven (samtidighedsferie) instead **earns** vacation monthly — 2,08 d/md (25/12) for ordinary vacation, 0,42 d/md (5/12) for the state *særlige feriedage* — over the ferieår (1 Sep–31 Aug). The IMMEDIATE model diverges from law wherever earned-to-date matters (over-booking, mid-year balance, termination). ADR-021 D6 reserved the `MONTHLY_ACCRUAL` enum for exactly this activation.

## Decision

**D1 — Compute earned-to-date on read (pure rule-engine function), NOT stored grant events.** `AccrualCalculator.EarnedToDate(annualQuota, partTimeFraction, ferieårStart, employmentStart?, asOfDate)` is a pure function (ADR-002) invoked at the existing ADR-021 D4 consumption boundary. No scheduler, no grant events, no new table, no balance migration. `entitlement_balances.total_quota` keeps meaning "annual entitlement"; earned-to-date is derived. Accrual is **exact-fractional**; rounding is display-only.

**D2 — Scope: VACATION + SPECIAL_HOLIDAY** use MONTHLY_ACCRUAL (shown as separate entitlements); CARE_DAY/CHILD_SICK/SENIOR_DAY stay IMMEDIATE (calendar-year). Activated via **sentinel-row reseed** (the `0001-01-01` rows, all 5 agreement codes incl. AC_RESEARCH/AC_TEACHING) — NOT supersession — preserving the ADR-021 D5 `(natural_key, effective_from)` invariant.

**D3 — Three distinct quantities (no conflation).** `earnedToDate` (gross optjent) · `available` = earned + carryoverIn − used − planned (rest) · `bookableLimit` (business validation cap, carryover-INCLUSIVE). The repo `CheckAndAdjustAsync` takes a **`guardCap`** (carryover-EXCLUDED — the method adds carryover_in itself) and a separate **`seedQuota`** (= annual, first-INSERT total_quota), so carryover is never double-counted and total_quota is never corrupted.

**D4 — Forskudsferie (advance vacation), per cited research:**
- **VACATION** — forskud allowed **by agreement** (Ferieloven §7) up to the **dynamic ferieår cap** = `earnedToDate + still-accruable-in-current-ferieår (+ carryover)`. The system treats the **manager period-approval flow as the §7 employer agreement** (no separate consent UI; the agreement is satisfied lazily at approval, after the employee's save).
- **SPECIAL_HOLIDAY** — **no forskud** (state ferieaftale §13 stk.4): hard cap at `earned (+ carryover)`.
- Enforced at BOTH the pre-transaction quota check AND the atomic `CheckAndAdjustAsync` guard. The 20%-remaining WARNING threshold and the per-episode branch keep keying off **annual** quota.

**D5 — Determinism (P4):** earnedToDate is a pure function of the passed `asOfDate` (never wall-clock). Skema validates at the absence batch's `firstAbsenceDate`; Balance summary at the requested month-end. Replay-stable, consistent with ADR-021 D4.

**D6 — `employment_start_date`** (new nullable column on `users`, HR-managed: GET/PUT `…/employment-start-date`, HROrAbove + OrgScope + If-Match — the S59 `birth_date` pattern) feeds mid-ferieår-hire pro-ration: still-accruable computes from `max(ferieårStart, employmentStart)`. It is an **explicit pure-rule input, a non-dated fact** (a correction re-derives uniformly; not bitemporal). **Null ⇒ full-ferieår fallback, NOT fail-closed** — the opposite polarity from S59's DOB (which fail-closes SENIOR_DAY), because a missing hire date must not wrongly deny *already-earned* vacation, whereas DOB gates an eligibility.

**D7 — Out of scope (payroll follow-up):** the state §8 wage-deduction for unearned advance days (*ferie uden løn*, 7,4/1924 of annual salary/day) and the §7 termination *modregning* (set-off of negative balance against final pay, capped at final pay) are payroll/settlement concerns — this sprint changes accrual/availability only.

## Consequences

- Vacation now earns over the ferieår; an employee on 1 Sep no longer "has" 25 days — they accrue progressively, with forskud permitted (VACATION) up to the dynamic cap on manager approval.
- `DefaultEntitlementConfigs` VACATION/SPECIAL_HOLIDAY reconciled to MONTHLY_ACCRUAL alongside the init.sql sentinel reseed (AC/HK/PROSA in the factory; AC_RESEARCH/AC_TEACHING init.sql-only).
- The PAT-005 boundary forced a Backend-local duplicate of `EarnedToDate` (Backend cannot reference the RuleEngine assembly); a reconciliation unit test pins the two in sync. **Resolved in S61:** the pure calc was consolidated into `src/SharedKernel/StatsTid.SharedKernel/Calendar/AccrualMath.cs` (a dependency-free leaf both RuleEngine + Backend already reference — NOT the PAT-005 HTTP boundary); `AccrualCalculator` + the two former Backend mirrors all delegate to the single source, enforced by a grep/architecture test. This closes follow-up #1.
- **Behavior change to flag:** the Skema POST quota path now requires a dated employee profile (422 `employment_profile_missing`) where it previously used a hardcoded part-time fraction of 1.0; the Balance summary stays graceful (`?? 1.0m`). See the sprint log / Step-7a review.
- Open compliance item: §8/§7 payroll handling ships separately.
