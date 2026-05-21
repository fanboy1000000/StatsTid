# ADR-026 — Audit Visibility Surface (Placeholder; Dedicated Design Sprint Required)

| Field | Value |
|-------|-------|
| **Status** | PLANNED (placeholder — filed at S38 TASK-3804 cycle-3 halt-and-prompt 2026-05-21 per user adjudication; ADR-025 D7 deferred here for dedicated design pass). |
| **Sprint** | TBD (cannot defer past S39; launch commitment per PROGRAM L279 + ADR-025 §Customer-go-live commitment). |
| **Domains** | Backend, Infrastructure, Security, Data Model. |
| **Tags** | audit-visibility, tenant-scoping, scope-by-actor, scope-by-target, projection, schema-extension, design-sprint-required. |
| **Supersedes** | none |
| **Amends** | ADR-025 D7 (the deferral itself) once authored. |

## Context

Institution-internal auditors need to query the audit log for their institution's events without seeing other institutions'. This is launch-required for first-customer go-live commitment.

The decision was originally scoped as ADR-025 D7 (one of 8 multi-tenant operational concerns). S38 Step 7a cycle-1 / cycle-2 / cycle-3 dual-lens reviews each surfaced new defects in the audit-visibility area:

- **Cycle 1**: ADR-025 D7 asserted existing tenant-scoped audit behavior that did not exist in code (no `/api/admin/audit/` endpoint; method names misstated; helper invented).
- **Cycle 2**: cycle-1 absorption introduced new fictive code references (`OrgScopeValidator.GetAccessibleOrgsAsync` invented; `AuditLogRepository.GetByActorAsync` mis-named) AND surfaced a deeper substantive concern: the `audit_log` row shape doesn't carry `target_org_id` / `target_resource_id` / `event_type` columns, so JOIN-by-actor-primary-org misses operator/system actions affecting a tenant from an outside-actor.
- **Cycle 3**: cycle-2 absorption (D7 reframed as "minimal scope-by-actor + forward-pointer for schema extension") introduced an internal contradiction: D7 honestly acknowledged the row-shape gap then claimed audit-trail completeness + commissioned an S41 D-test for event-type completeness that can't be implemented on the actual row shape.

Three cycles disclosing new defects in the same architectural area = canonical signal per `feedback_thrash_defer_real_world.md` that the seam is wider than one decision in a multi-decision ADR can carry. User authorized defer-D7 at cycle-3 halt-and-prompt; this ADR is the dedicated design venue.

## Open Problem (carried in from ADR-025 D7 deferral)

Institution-internal auditors need to query the audit log for their institution's events without seeing other institutions'. The query must:

1. Restrict to events affecting the requesting admin's institution subtree
2. Cover operator/system actions affecting the tenant (not just actor-in-subtree actions)
3. Support pagination + filtering by time range + actor + resource type
4. Preserve event-sourcing immutability per ADR-001
5. Land by S39 (no later than the schema-migration sprint, given launch commitment)

## Known Architectural Paths

**(A) Minimal scope-by-actor** — JOIN `audit_log.actor_id → users.primary_org_id` + materialized-path subtree check. Implementable on current row shape. **Known limitation**: operator/system actions from outside-actor invisible to tenant. May be insufficient for the launch commitment depending on customer audit-completeness expectations.

**(B) Schema extension for scope-by-target** — ALTER `audit_log` ADD `target_org_id NULL` + `target_resource_id NULL` + `event_type NULL` columns. Retrofit `AuditLoggingMiddleware` to populate these from per-endpoint context (every state-changing endpoint must declare its target_org_id mapping). Enables event-type-completeness + tenant-targeted queries. **Cost**: middleware retrofit touches every state-changing endpoint + each endpoint must publish its target_org_id mapping.

**(C) Event-sourcing-aligned audit projection** — leverage ADR-018 D13 sync-in-tx projection pattern. Project audit-relevant events into a dedicated `audit_projection` table with explicit `target_org_id` + `target_resource_id` + `event_type` columns at projection time, derived from the event payload itself (which carries the target context). Event log stays immutable per ADR-001; the projection is the authoritative read surface for audit queries. **Advantage**: aligns with the architectural posture established S27 (read-path projection tables); avoids retrofitting request-middleware audit row; per-event explicit declarations cleaner than per-endpoint middleware. **Cost**: requires inventory of which events are audit-relevant + projection definitions per event type + S27-pattern (conn, tx) atomic-sync repository overloads.

**(D) Hybrid** — minimal scope-by-actor for the request-audit-middleware row (path A) + event projection (path C) for state-changing event types. Tenant query unions both sources scoped to subtree. **Cost**: union semantics + reconciliation discipline; more moving parts.

## Open Questions for the Design Sprint

1. Is operator/system-action visibility actually required for launch, or can launch ship with scope-by-actor + a documented limitation + plan to migrate to a fuller solution post-launch?
2. If full visibility is required: which path (B, C, D) is cleanest given existing S27 projection-table infrastructure?
3. How does this interact with ADR-024 D6's new `ConfigBugCorrected` event + ADR-024 D7 `OvertimeNecessityAcknowledged` + the 4 new ADR-025 events? Are they audit-relevant by default, or does each event type opt in?
4. ADR-025 D5 cross-tenant report bypass produces `CrossTenantReportAccessed` audit events. Under each path: who can see these events? (GlobalAdmin-only? Or also visible to LocalAdmin of the institutions whose data was queried?)
5. Phase E continuous-validation tests (S39 TASK-3905) — what audit-completeness invariants should be asserted?

## Decisions

(NONE YET — placeholder filed; design sprint produces D1-DN at TBD sprint.)

## Consequences (anticipated, refined at design sprint)

- New schema (path B or C) + S39 schema-migration ledger entry
- New endpoint + repository method + admin UI page
- Possibly: middleware retrofit (path B) or event-projection definitions (path C)
- New event types: TBD per chosen path
- S41 D-test scope expanded with audit-visibility tests

## References

- **ADR-025 D7** (DEFERRED) — original framing + cycle-1/2/3 trail captured there
- ADR-001 (event sourcing immutability) — informs path C
- ADR-018 D13 (sync-in-tx projection canonical pattern) — informs path C
- ADR-024 D6 + D7 (new event types this ADR audit-completeness questions touch)
- ADR-025 D5 (cross-tenant report bypass audit-visibility question)
- `feedback_thrash_defer_real_world.md` — the discipline that produced this deferral
- `AuditLoggingMiddleware.cs:37` — current row-shape (actor_id / http_path / http_status / details)
- `AuditLogRepository.cs:42` + `:62` — current query methods (`QueryByActorAsync`, `QueryByCorrelationAsync`)
- `OrgScopeValidator.cs:32` + `:85` — current scope methods (`ValidateEmployeeAccessAsync`, `ValidateOrgAccessAsync`)
