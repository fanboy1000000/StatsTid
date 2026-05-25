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
| Phase 4 | Enforcement toggle (per-tree feature flag per ADR-025 D6) | Opt-in: approval requires designated manager or fallback |

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
