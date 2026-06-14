# ADR-027 — Reporting-Line Hierarchy (Complementing ADR-008 Org Hierarchy)

| Field | Value |
|-------|-------|
| **Status** | ACCEPTED |
| **Sprint** | S48 (Phase 1 — schema + repository + admin API + admin UI). |
| **Domains** | Infrastructure, Backend, Frontend, Data Model, Security. |
| **Tags** | reporting-line, manager, approval-routing, temporal, hierarchy, vikarierende-leder, acting-manager, phase-migration. |
| **Supersedes** | none |
| **Amends** | SYSTEM_TARGET.md §E (org structure) + §H (approval workflow); ADR-012 (two-step approval — adds routing layer). |

## Context

The existing org-unit hierarchy (ADR-008) handles agreement inheritance, scope validation, and config resolution. It models **organizational structure** (Ministry → Styrelse → Afdeling → Team) but does not model **who manages whom**. Danish state sector requires:

- Designated-manager time approval routing (not just any leader in the org)
- Fallback chain traversal when a manager is inactive
- Acting-manager delegation (vikarierende leder) during vacation/illness
- HR system import of reporting structures (some institutions)
- Admin visibility into the people hierarchy

These are **complementary hierarchies**: the org-unit hierarchy determines agreement rules and scope; the reporting-line hierarchy determines approval routing and management visibility.

## Decisions

### D1 — Single temporal `reporting_lines` table

**No `manager_id` on the `users` table.** The reporting-line relationship is stored in a dedicated `reporting_lines` table following the ADR-017 D1 temporal pattern:

- `effective_from` / `effective_to` (NULL = currently active)
- Partial unique index `WHERE effective_to IS NULL AND relationship = 'PRIMARY'` enforces at-most-one active PRIMARY per employee
- Row-version column (ADR-018 D7) for optimistic concurrency
- `created_by` + `created_at` for audit

The "current manager" is derived from the temporal table, not duplicated. This avoids the dual-source-of-truth consistency risk that a `manager_id` column on `users` would create.

### D2 — Tree boundary per MINISTRY/STYRELSE

Each org of type MINISTRY or STYRELSE is an independent reporting-tree root. `tree_root_org_id` is derived from the employee's `primary_org_id` by walking up `organizations.parent_org_id` until reaching a MINISTRY or STYRELSE.

A single institution (ministry) can have multiple independent trees — one per styrelse plus one for the ministry itself. This matches the real-world Danish state sector where each styrelse operates with independent management chains.

### D3 — PRIMARY + ACTING relationship types

- **PRIMARY**: One per employee (enforced by partial unique index). The employee's designated manager.
- **ACTING**: One per employee (enforced by separate partial unique index). Temporary delegation during manager's vacation/illness. Takes precedence over PRIMARY in approval routing.

Both types share the same table and temporal lifecycle. An ACTING line is created when a manager goes on leave and closed when they return.

### D4 — Manager-preferred routing (not authorization)

The reporting line determines **routing** (who sees the pending approval), not **authorization** (who can approve). Authorization remains role + org scope (LocalLeader+ with matching scope per ADR-012). This preserves backward compatibility — any LocalLeader in the employee's org scope can still approve.

The designated manager is the *default approver* shown on the approval dashboard. The fallback traversal (D5) resolves who that is.

### D5 — Fallback traversal

```
resolve_designated_approver(employee_id):
  1. active ACTING line → if manager is active → return manager
  2. active PRIMARY line → if manager is active → return manager
  3. if manager is inactive → recurse on manager's manager
  4. if depth > 10 OR no line exists → return NULL (fall back to org-scope)
```

Evaluated at **approval-request time** (not submission time). Depth > 3 emits an auditable `FallbackTraversalWarning` event.

### D6 — Acting manager (vikarierende leder)

Hard requirement for Danish state sector. The `ACTING` relationship type is a first-class concept from day one, not a future addition.

Phase 1: manual ACTING assignment via Admin API. Phase 2+: self-service delegation UI for managers.

### D7 — HR system integration (Phase 2)

REST import endpoint: `POST /api/admin/reporting-lines/import`. GLOBAL_ADMIN only. JSON format. Format-agnostic — adapter layer for institution-specific HR system formats (CSV, SOAP/HR-Løn) is Phase 2+.

### D8 — Four-phase migration

| Phase | Scope | Enforcement |
|-------|-------|-------------|
| Phase 1 (S48) | Schema + repository + admin API + admin UI | Reporting lines optional — employees without one use org-scope as today |
| Phase 2+3 (S49) | HR import endpoint + bulk import UI + designated-approver resolution + "My Reports" tab + `designated_approver_id`/`approval_method` on approval_periods | Trees populated via import; approval dashboard routes to designated manager; org-scope fallback still active |
| Phase 4 (S50) | Enforcement toggle via `reporting_line_tree_settings` table (NOT ADR-025 D6 — workflow enforcement is a first-class config, not a UI feature flag) | Opt-in REQUIRED mode: non-designated approvers get 428 + must confirm fallback. Population gate: cannot enable unless all employees have PRIMARY lines. `explicit_fallback_confirmation` on approval_periods for audit. |

### D9 — Root invariant

An employee with no active PRIMARY reporting line in a given tree is a **tree root**. At most one root per `tree_root_org_id`. Phase 1 allows zero roots (unpopulated trees). Phase 4 enforcement makes population mandatory.

The tree root need not hold any system admin role — the root of the people hierarchy is a domain concept, not an RBAC concept.

### D10 — Event types

Five domain events on `reporting-line-{employeeId}` stream (ADR-018 D6):

| Event | When |
|-------|------|
| `ReportingLineAssigned` | New PRIMARY or ACTING line created |
| `ReportingLineSuperseded` | Existing line closed (effective_to set) |
| `ReportingLineBulkImported` | HR import batch processed |
| `ReportingLineManagerDeactivated` | Manager's is_active set to false (Phase 4) |
| `FallbackTraversalWarning` | Designated-approver resolution depth > 3 (S49 amendment) |

All registered in EventSerializer (DEP-003). Atomic outbox emission per ADR-018 D3.

### D11 — Enforcement model (S50 amendment)

Enforcement is a **first-class per-tree config** via `reporting_line_tree_settings` table, NOT an ADR-025 D6 feature flag. ADR-025 D6 prohibits workflow-affecting flags ("must NEVER affect workflow enablement"). Enforcement changes approval authorization behavior, so it needs its own config surface.

**Modes**: `PREFERRED` (default — routing only, current behavior) or `REQUIRED` (soft enforcement — 428 + confirmation dialog for non-designated approvers).

**Population gate**: Cannot set `REQUIRED` unless every non-root employee in the tree has an active PRIMARY reporting line. Validated at write time.

**Soft enforcement**: 428 response with `confirmFallback=true` query param override. Never hard 403. `explicit_fallback_confirmation` boolean on `approval_periods` tracks confirmed fallbacks for audit.

### D12 — Self-service delegation (S51 amendment)

Managers (LocalLeader+) can delegate their approval queue via `POST /api/reporting-lines/delegate`. Creates ACTING lines with `source = 'SELF_DELEGATION'` and `scheduled_expiry` for auto-closure. The `DelegationExpiryService` (BackgroundService) polls every 5 minutes and atomically closes expired lines with outbox event emission (ADR-018 D3).

**Temporal model**: `scheduled_expiry DATE` column on `reporting_lines` (nullable). Read queries remain `effective_to IS NULL` — no regression. Lines are "active" until the expiry service closes them by setting `effective_to = scheduled_expiry`.

**Org-scope validation**: Acting manager's role-assignment scopes must cover ALL direct reports' organizations. Validated at delegation time via `RoleScope.CoversOrg` pattern.

**Invariant**: One active delegation per manager. Second POST returns 409 — cancel first, then re-delegate.

### D13 — Designated-edge approve-AUTHORITY within the styrelse (S74 / Phase 5 amendment — AMENDS D4)

D4 originally made the reporting edge **routing, not authorization**. S74 (owner ruling OQ-3a) deliberately expands it: **holding the effective designated-approver edge GRANTS the right to approve/reject/reopen that employee's periods — anywhere within the same `tree_root_org_id`, even where the actor's RBAC org-scope does not reach.** This makes cross-afdeling (matrix/secondment) approval actually work.

- **The bound (never edge-alone):** `IsEffectiveDesignatedApprover(actor, employee, asOf) ⟺ actor is active+LeaderOrAbove AND the SINGLE resolved effective approver of employee at asOf (per D5/D14 precedence) == actor AND actor & employee share the same tree_root_org_id`. The same-tree clause is **enforced structurally by the predicate itself** (the Defense-in-depth bullet below) — NOT merely assumed from the assign-time `ValidateSameTreeAsync` invariant: that invariant holds for PRIMARY/ACTING edges but the vikar-creation path needed its own same-tree fix (D14), so the predicate cannot trust "resolved == actor implies same tree" and re-checks it explicitly. **Cross-styrelse approval is therefore structurally impossible (D2 preserved).** It is NOT a UNION of the transitive-report set (that would over-grant a grand-manager).
- **One canonical predicate** (`DesignatedApproverAuthorizer`) is used IDENTICALLY for (a) the my-reports dashboard reads (`GetPendingForDesignatedReportsAsync` + `GetByMonthForDesignatedReportsAsync`, rewritten from the transitive CTE to single-immediate-effective-approver semantics, recursion only for inactive-manager escalation) and (b) the approve/reject/reopen authorization — so **see == act** at every level. `asOf = today` for action authority. The existing org-scope authorization path is preserved as an additive OR-branch; the default `/pending` org-scope query is unchanged. `employee-approve` + `submit` do NOT inherit edge authority (not manager transitions).
- **Defense-in-depth:** the predicate ALSO structurally re-checks that the actor and employee share a tree root (so even a mis-created cross-tree vikar cannot grant cross-styrelse authority). The vikar-creation path is same-tree-validated (D14).
- **Deferred (in-lock hardening follow-up):** authority is checked at request entry, OUTSIDE the action tx (the pre-existing check-then-act pattern shared with the org-scope path) — a revocation in the sub-second window before commit is not caught. Owner-deferred to a dedicated in-lock re-evaluation pass covering both the org-scope and edge paths.

### D14 — Approver-owned vikar + resolver vikar-consult (S74 / Phase 5 amendment — AMENDS D5, D12)

S74 (owner ruling OQ-5a) adds an **approver-owned vikar** that covers the absent approver's CURRENT *and FUTURE* reports (resolved dynamically), replacing the D12 per-report `SELF_DELEGATION` ACTING fan-out as the self-delegation STORAGE:

- New `manager_vikar` table (one active vikar per approver, partial-unique): `absent_approver_id`, `vikar_user_id`, `until_date` (INCLUSIVE), `reason` CHECK-enum, `tree_root_org_id`, version, `effective_to` close marker. CHECK `absent_approver_id <> vikar_user_id`. **Same-tree-validated at creation** (the vikar MUST be in the same styrelse as the absent approver — closes the cross-styrelse-authority hole that D13's expansion would otherwise open).
- **The 3 `/api/reporting-lines/delegate` endpoint CONTRACTS stay byte-stable** (the S51 self-service UI survives until the FE phases retire it); only the storage moves to `manager_vikar`. GET re-derives `delegatedEmployees[]` dynamically (current reports the vikar is effective for, excluding admin-ACTING-superseded).
- **Resolver precedence (D5 refined, now dated `asOf` default-today so existing callers compile unchanged):** admin-assigned ACTING → the PRIMARY manager M's active approver-owned vikar V (V's user active) → M (if active) → inactive-manager escalation. Edge case: if M is INACTIVE but holds an active vikar V, V wins over escalation (fired in the same loop iteration). An INACTIVE vikar user is skipped. A vikar resolves with `ApprovalMethod = ACTING_MANAGER`.
- **Inclusive "til og med" fix:** `DelegationExpiryService` closes a vikar the day AFTER `until_date` (predicate `until_date < CURRENT_DATE`), so the named day is the LAST covered day.

### D15 — Write-time integrity: cycle guard, atomic create+assign, no-orphan delete (S74 / Phase 5 amendment — EXTENDS D1, D9)

- **Write-time cycle prevention:** the PRIMARY assign, ACTING assign, atomic create+assign, R10 reassign, and the bulk import all REJECT (4xx) an approver that is the employee or any descendant — enforced via a **tree-wide `pg_advisory_xact_lock(tree_root)`** (the ADR-032 D4 precedent) taken before a bounded descendant walk (path-array visited-set termination; no depth cap). This closes a real prior gap (no cycle guard existed) and the concurrent-first-assign phantom gap (a slot FOR UPDATE alone doesn't serialize first-assigns).
- **Atomic create-person-with-approver:** `POST /api/admin/users` optionally creates the PRIMARY edge in the SAME tx (in-tx `ValidateSameTree`/`ResolveTreeRoot` overloads see the just-inserted user; emits `ReportingLineAssigned` + audit). No new orphan from the UI create path.
- **No-orphan delete-with-reassignment (D9 refined):** "Fjern medarbejder" is preflight-409 + MANDATORY reassignment — it NEVER creates an orphan. One atomic tx: reassign incoming PRIMARY edges to supplied replacements, close incoming ACTING + the removed person's own edges + the `manager_vikar` rows on both sides, soft-deactivate. A report transferred cross-styrelse (current tree ≠ removed person's tree) is REJECTED for manual handling rather than reassigned under the wrong lock.
- **Concurrency model:** uniform lock order on every path — tree advisory lock → id-ordered two-row `FOR UPDATE` (in same-tree validation) → cycle guard → slot `FOR UPDATE`; READ COMMITTED isolation so post-lock reads see committed state. **Deferred (in-lock hardening follow-up):** perfect serialization of assign/delete under a SIMULTANEOUS cross-styrelse org-transfer (the advisory key derives from mutable org data; the same-tree validation rejects the cross-tree-drift case, but two assigns transferring to the same new styrelse simultaneously is a non-serialized — non-corrupting — residual needing a stable tree id or an org-transfer lock).

### D16 — `enhed_label` + new event types (S74 / Phase 5 amendment — AMENDS D10)

- **`employee_profiles.enhed_label`** (owner ruling OQ-1a): an additive, display-only free-text "Enhed" label that rides the existing ADR-022 temporal profile versioning (a label change supersedes the live profile row like position/fraction — NOT a new event family). `primary_org_id` stays the structured RBAC/tree/config anchor.
- **New events** `ManagerVikarCreated` / `ManagerVikarEnded` (ADR-026 audit-projection mappers, sync-in-tx trio). `ReportingLineSelfDelegated` is **RETIRED FROM EMISSION** but its class + EventSerializer registration are **RETAINED for historical replay**.

### D17 — Admin-on-behalf vikar authority + the `/delegate` in-lock completion (S76 / Phase 3 amendment — EXTENDS D14, D15; partially RESOLVES the D13/D15 in-lock-hardening deferral)

S76 (owner ruling OQ-2a) adds an **admin-managed** path to the approver-owned vikar of D14, so a LocalAdmin can set/clear a vikar on **any** manager in scope from the Medarbejder-administration surface (not only the manager self-delegating). Because an active vikar GRANTS approve-authority (D13), this is an authorization surface held to the full create-authority contract.

- **New `POST/DELETE /api/admin/reporting-lines/{managerId}/vikar`** (LocalAdminOrAbove; reuses `manager_vikar` + `ManagerVikarCreated/Ended` — NO new event/table). **CREATE** enforces, ALL in ONE tx under the tree advisory lock, in order *root → lock → authorize → census → same-tree → cycle → insert*: (i) the actor's **admin-role-floored** scope covers the manager's CURRENT org (read in-tx under the held advisory, narrowing — though not fully eliminating — the window vs the simultaneous-cross-styrelse-transfer residual deferred below, since the org `SELECT` is not itself a `FOR UPDATE` row pin); (ii) the vikar is active + LeaderOrAbove; (iii) the vikar's scope covers ALL of the manager's reports (the report list read **in-lock**, so a concurrent assignment can't slip an uncovered report past the check); (iv) same-tree (the D13/D14 cross-styrelse defense — same-tree-validated, so a cross-tree vikar is rejected even when its scope would cover the reports); (v) `uq_manager_vikar_active` 409; (vi) `ManagerVikarCreated` + ADR-026 audit, **attributed to the ADMIN actor's org** (not the manager's tree — the S71 actor-org lesson).
- **REVOKE (DELETE) = split, revoke-safe authorization:** acquire the tree lock → read the active row **FOR UPDATE** → authorize against **THAT row's persisted `tree_root_org_id`** (the sole anchor) → close — atomically, so a concurrent close/recreate cannot swap the row between authorize and close. Revoke does NOT re-check vikar-still-active / still-covers-reports: a now-inactive vikar/manager MUST remain revocable.
- **`/delegate` (self) in-lock completion:** the self-delegation path is restructured to the D15 lock discipline — the same-tree validation, the cycle guard, AND the direct-report coverage census all run **in-lock, in-tx** (READ COMMITTED, the advisory lock as the first in-tx action after root resolution), closing the phantom gap where a pre-lock coverage snapshot could auto-expose a newly-assigned uncovered report. **The `/delegate` request/response/error contract stays byte-stable** (the live S51 self-service UI). This **resolves the D13/D15 in-lock-hardening deferral *for the vikar-creation paths*** (both self and on-behalf); the org-scope approval-auth TOCTOU + the simultaneous-cross-styrelse-transfer serialization residuals remain owner-deferred (Phase-4/hardening).
- **Cross-cutting security (B1, S76):** the mixed-role scope-leak class — `OrgScopeValidator` admitting via ANY covering scope regardless of role — was closed by adding per-scope **admin-role-floored** variants of every admin scope-admission path (`ValidateOrgAccessAsync`, `ValidateEmployeeAccessAsync`, `GetAccessibleOrgsAsync`, `ValidateEmployeeAccessIncludingTerminatedAsync`) + role-gating `HasGlobalScope`, routed at each endpoint's policy floor (LocalAdmin / LocalHR / GlobalAdmin), leaving the leader-permissive year-overview reads null-floored. See [docs/SECURITY.md](../../SECURITY.md) (the mixed-role scope-floor invariant). The external review lens drove this to completeness across 3 cycles — a scoped/structural trace cannot enumerate every scope-admission caller ([[review-lens-complementarity]]).

## Consequences

### S48 schema (Phase 1)

- `reporting_lines` table with 5 indexes (2 partial-unique, 3 lookup)
- `reporting_line_audit` table with 1 index
- 14 seed rows (13 PRIMARY + 1 ACTING across 7 trees)
- `ReportingLineRepository` following `LocalAgreementProfileRepository` temporal pattern
- `ReportingLineEndpoints.cs` with 7 admin CRUD endpoints
- Frontend: `ReportingLineTree.tsx` admin page + manager picker on UserManagement
- 4 new event types registered in EventSerializer

### Future sprints

- Phase 2: HR import endpoint + bulk import UI
- Phase 3: Approval routing changes (`ApprovalEndpoints.cs` + `ApprovalDashboard.tsx`)
- Phase 4: Per-tree enforcement toggle + `designated_approver_id` on `approval_periods`

### Interaction with other ADRs

- **ADR-008**: Complementary — org hierarchy for scope/config, reporting-line hierarchy for approval routing
- **ADR-012**: Amended — adds routing layer to the two-step approval flow
- **ADR-017 D1**: Pattern inherited — temporal table with `effective_to IS NULL` partial unique index
- **ADR-018 D3**: Pattern inherited — atomic outbox (state + audit + event in single tx)
- **ADR-019 D2**: Pattern inherited — ETag/If-Match on admin-strict write endpoints
- **ADR-024**: Orthogonal — `employment_category` drives rule config, reporting lines drive approval routing
- **ADR-025 D8**: Related but distinct — `institutions` table provides institution boundary (ministry level); `tree_root_org_id` provides finer-grained tree boundary (styrelse level)

## References

- Analysis: `.claude/refinements/ANALYSIS-reporting-line-hierarchy.md` (v3, Codex cycle 2 clean)
- ADR-008 (materialized-path org hierarchy)
- ADR-012 (two-step approval flow)
- ADR-017 D1 (temporal profile pattern)
- ADR-018 D3 (atomic outbox) + D6 (stream naming) + D7 (row-version)
- ADR-019 D2 (admin-strict ETag/If-Match)
- ADR-025 D8 (institution concept)
