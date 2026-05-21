# ADR-025 — Multi-Tenant Operational Concerns (Phase C Design)

| Field | Value |
|-------|-------|
| **Status** | ACCEPTED-WITH-D7-DEFERRED (S38 TASK-3802; cycle-3 halt-and-prompt 2026-05-21 resolved by user adjudication: D7 deferred to dedicated ADR-026 per `feedback_thrash_defer_real_world.md` thrash-defer pattern. Cycles 1-3 trail: cycle 1 (Codex P1.1+P1.2+P1.3; Reviewer W2 convergent); cycle 2 (Codex P1.NEW-1+P1.NEW-2 same-areas; Reviewer B1+B2 same-area); cycle 3 (P1.NEW-1+B1+B2+W1-cycle2 CLOSED via cycle-2 absorption; Codex cycle-3 P1.C3-1 = NEW BLOCKER in same D7 area — internal contradiction between row-shape acknowledgment + event-type completeness D-test). Three cycles disclosing D7 defects in same area = canonical thrash signal → user authorized defer-D7. Decisions D1-D6 + D8 ship as ACCEPTED. D7 placeholder records the open problem + the two known paths (A scope-by-actor / B schema-extension) + an event-sourcing-aligned third option for ADR-026 to adjudicate.) |
| **Sprint** | S38 (companion to ADR-024 + ADR-013 amendment). |
| **Domains** | Infrastructure, Backend, Frontend, Security, Payroll Integration. |
| **Tags** | multi-tenant, saas-operations, per-tenant-sls, customer-onboarding, gdpr, noisy-neighbor-fairness, cross-tenant-reporting, feature-flags, audit-visibility, institution-type, design-binding, phase-4e. |
| **Supersedes** | none |
| **Amends** | ROADMAP "Deployment Model" L16-27 (no rewrite; this ADR fills in operational details the deployment model implies). |

## Context

ROADMAP "Deployment Model" (committed 2026-05-18) settled the high-level shape: single logical deployment, single PostgreSQL database, single application instance serving ~150 institutions at launch. Tenants map to top-level organizations in the materialized-path org hierarchy (ADR-008). Agreements are GLOBAL across tenants; per-institution variations live in `local_configurations` per ADR-017.

The deployment model is sound architecturally for domain-correctness (agreements stay global; scope binding works via OrgScopeValidator + JWT scope-embedded). But **eight operational gaps** need explicit decision before launch — listed at ROADMAP L389 / PROGRAM-s36-s41 L142-151. This ADR settles each.

**Orthogonal to ADR-024** (role-within-agreement modeling). The two ADRs can land in parallel and do not share architectural decision points.

**Each decision is binding for S39 schema migration + S40 cutover + S41 D-tests + customer-onboarding runbook authorship** — precise enough that operational rollout can proceed without revisiting the architecture.

## Decisions

### D1 — Per-tenant SLS payroll endpoint

**Today**: SLS export configuration is global (single SLS endpoint URL + credentials in `appsettings.json`). All institutions submit to the same destination.

**Problem**: each Danish state institution typically submits payroll to its own SLS file destination (institution-specific batch number, possibly different credentials per ministry).

**Decision**: per-tenant SLS configuration via new `tenant_sls_configurations` table keyed by `org_id` (institution's top-level org node):

```sql
CREATE TABLE tenant_sls_configurations (
    config_id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id              TEXT        NOT NULL REFERENCES organizations(org_id),
    sls_endpoint_url    TEXT        NOT NULL,
    sls_credential_ref  TEXT        NOT NULL,  -- secret store ref, not the credential itself
    sls_batch_prefix    TEXT        NOT NULL,  -- per-institution batch identifier
    effective_from      DATE        NOT NULL DEFAULT CURRENT_DATE,
    effective_to        DATE,
    version             BIGINT      NOT NULL DEFAULT 1,
    -- + standard audit columns
    UNIQUE (org_id) WHERE effective_to IS NULL  -- one active per institution
);
```

`PayrollExportService` reads `tenant_sls_configurations` by `org_id` (resolved from the employee's institution top-level org via materialized-path lookup) before each batch submission. Falls back to global `appsettings.json` SLS config when no per-tenant row exists (smooth migration path).

**Credential handling**: `sls_credential_ref` is a reference (key name in secrets store: Azure Key Vault / AWS Secrets Manager / `appsettings.local.json` per env), NOT the credential value. The credential itself never sits in the database.

**Admin UI**: new `SlsConfigurationEditor.tsx` page (GlobalAdminOnly) for SaaS operator to provision per-institution SLS config during customer onboarding.

**Versioning**: follows the established versioned-config pattern (ADR-018 D7 row-version + If-Match per ADR-019 D2 admin-strict) so credential rotations are atomic + auditable.

### D2 — Customer-onboarding workflow

**Problem**: provisioning a new institution requires creating: (a) top-level org node in `organizations`, (b) seed `local_configurations` if institution overrides any defaults, (c) first LocalAdmin user, (d) per-tenant SLS configuration (per D1), (e) if billing: subscription record. Currently no runbook documents this; provisioning happens ad-hoc.

**Decision**: codify the workflow as a new `customer-onboarding-runbook.md` doc + a GlobalAdminOnly endpoint `POST /api/admin/institutions/provision` that atomically performs:

1. INSERT into `organizations` with `materialized_path = '/{new_org_id}/'`, `agreement_code` = provisioned default
2. INSERT into `tenant_sls_configurations` with provided SLS endpoint + credential ref + batch prefix (per D1)
3. INSERT initial LocalAdmin user via existing AdminEndpoints POST logic (per S35 admin-strict If-Match)
4. INSERT default `local_configurations` profile (if any institution-specific overrides at provision time; else NULL — falls through to central agreement config)
5. Emit new `InstitutionProvisioned` event (one of 4 new events from this ADR; total post-S40 EventSerializer count: 58 + 7 from ADR-024 + 4 from ADR-025 = **69**) for downstream audit + billing trigger
6. All 5 operations atomic per ADR-018 D3 single-tx pattern

**Runbook doc** (`docs/operations/customer-onboarding-runbook.md`) covers the human steps: gather institution metadata + SLS endpoint + initial LocalAdmin contact + agreement default; invoke the endpoint; verify provisioning; deliver LocalAdmin credentials.

**Idempotency**: the endpoint is idempotent via the `org_id` natural key — repeated calls with the same `org_id` return the existing row, not a duplicate. Matches the S30/S31 seeder idempotency pattern.

### D3 — GDPR per-tenant export + Article 17 right-to-erasure

**Problem**: event-sourced architecture makes Article 17 (right to be forgotten) non-trivial. Events are append-only by ADR-001 / ADR-004 design. A naive "delete the user" violates the immutability invariant.

**Decision**: two-part approach.

**Part A — Per-tenant data export** (Article 15 right of access + Article 20 data portability):

- New endpoint `GET /api/admin/institutions/{org_id}/export` (LocalAdminOrAbove + OrgScopeValidator restricting to the requesting admin's institution).
- Returns a streaming JSON archive containing: all users at `org_id` subtree + all time_entries + absences + approval_periods + audit_log scoped to those users + entitlement_balances + overtime_balances + employee_profiles + user_agreement_codes history. Does NOT include events (events are infrastructure; the projection tables carry the authoritative state per ADR-018 D13).
- Standard streaming format: NDJSON one record per line per table, prefixed with `{"table": "users"}` headers.
- Audit trail: each export emits `InstitutionDataExported` event (typeof 61 → 62) with `requesting_actor_id` + `org_id` + timestamp.

**Part B — Article 17 right-to-erasure**:

- Erasure operates on the **projection tables** (post-S27 sync-in-tx projection pattern). The event log remains immutable per ADR-001.
- New endpoint `POST /api/admin/users/{user_id}/erase` (LocalAdminOrAbove + OrgScopeValidator + erasure-confirmation token to prevent accidental triggering).
- Atomic transaction: NULL out PII columns on `users` + `employee_profiles` (`username`, `display_name`, `email`, `birth_date`, etc.); leave non-PII operational columns (org_id, agreement_code) intact; INSERT into new `erased_users_audit` table; emit `UserPiiErased` event (typeof 62 → 63).
- Events in `events` table retain the user_id reference but lookups against PII columns return NULL.
- Past payroll exports remain replayable byte-identically per ADR-016 D10 (the erasure happens at the projection level; replay uses event log + projections; replay-after-erasure produces compliance-compliant output where PII fields are NULL).

**Soft-delete vs hard-delete**: erasure is **NULL-out**, not row deletion. Row deletion would break event-log foreign-key references. NULL-out preserves the row skeleton + breaks the PII linkage.

**Per-tenant scoping**: both Part A export + Part B erasure scope to the requesting admin's institution via OrgScopeValidator. Cross-tenant data leakage prevented.

### D4 — Noisy-neighbor / per-tenant fairness

**Problem**: `OutboxPublisher` (ADR-018) currently has 4-way cross-stream parallelism but no per-tenant fairness. One institution's burst load could degrade outbox-delivery latency for others.

**Decision**: **defer to post-launch** with explicit ROADMAP commitment + telemetry instrumentation now.

**Pre-launch**: ~150 institutions at launch, average tenant load below per-stream throughput ceiling. Noisy-neighbor unlikely to trigger.

**Post-launch instrumentation** (lands S40 alongside other cutover work):
- New telemetry counters on OutboxPublisher: `outbox_publish_duration_p99_per_tenant`, `outbox_backlog_size_per_tenant`, `outbox_publish_rate_per_tenant`
- Dashboard with per-tenant view; alert threshold when one tenant's backlog exceeds 10× the median
- Document the deferral in `docs/operations/scaling-runbook.md` (new) with the eventual fairness mechanism (weighted-fair-queueing per `org_id` derived from outbox event stream prefix) outlined but not implemented

**When to implement**: when telemetry shows the threshold breach OR when launching the 151st institution (whichever first).

**Rationale**: building fairness infrastructure pre-launch with no data to drive the design is YAGNI. The instrumentation lets the design decision be data-driven post-launch.

### D5 — SaaS-operator cross-tenant reporting

**Problem**: SaaS operator needs usage/billing dashboards that aggregate across tenants. This deliberately breaks the scope binding (which restricts admin views to institution subtree).

**Decision**: new `GlobalAdminOnly` reporting endpoints under `/api/admin/reports/cross-tenant/` that:

- Bypass OrgScopeValidator (explicit policy exemption documented in code at each endpoint)
- Return aggregated data only (no individual user PII); aggregation level is `org_id` (top-level institution) for usage metrics, `agreement_code` for revenue allocation
- Audit every cross-tenant query: new event `CrossTenantReportAccessed` (typeof 63 → 64) with requesting actor + report type + parameter hash; admin GET shows the audit trail per institution-internal auditor request

**Initial reports** (S40 scope):
1. **Usage**: per-institution active-user counts (last 30/60/90 days)
2. **Activity**: per-institution time-entry counts + absence counts (last calendar month)
3. **SLS exports**: per-institution payroll batch counts + last-export timestamp
4. **Billing inputs**: per-institution feature-flag enablement (per D6) + tier classification

**Frontend**: new `CrossTenantReports.tsx` GlobalAdmin-only page; absent from LocalAdmin navigation entirely.

**Security boundary**: `OrgScopeValidator` bypass is the most permissive RBAC choice in the system. Documented in `docs/SECURITY.md` post-S40 as the single canonical bypass site; any future bypass must update the canonical list.

### D6 — Per-tenant feature flags

**Problem**: institutions may differ on OPTIONAL UI / surface presentation (e.g., whether the Skema page shows a particular widget, whether a non-mandatory dashboard panel renders). Where does this configuration live?

**Decision**: extend `local_configurations` per ADR-017 with a `feature_flags` JSONB column. Schema:

```sql
ALTER TABLE local_configurations ADD COLUMN feature_flags JSONB NOT NULL DEFAULT '{}';
```

**Schema for flag values** (TypeScript-style; non-controversial examples — actual flag catalog evolves with each feature):

```typescript
type FeatureFlags = {
  flex_carryover_grace_days?: number;          // per-institution UI grace-period display only; rule calc unchanged
  optional_skema_widget_enabled?: boolean;     // UI widget toggle; no rule-engine impact
  custom_branding_logo_url?: string;           // tenant branding only
  // ... future per-tenant UI / surface toggles
};
```

**Resolution chain**: feature flags live on `local_configurations`; consumed at the UI / endpoint surface layer via `ConfigResolutionService.GetEffectiveFeatureFlags(org_id)`. Rule engine code does NOT read feature_flags. Falls back to global defaults from `appsettings.json` when not set per-tenant.

**Why JSONB not separate columns**: feature flags are inherently a growing set; JSONB lets new flags ship without ALTER TABLE migrations. Schema validation lives at the consumer (any unknown flag is ignored; documented flag set is enumerated in `docs/operations/feature-flags-catalog.md`).

**Hard constraint per glocal principle (S38 Step 7a P1.1 absorption)**: feature flags are **UI / surface presentation toggles ONLY**. They MUST NOT affect:

- Rule-engine outputs (any cell in `agreement_configs` / `entitlement_configs` / `wage_type_mappings` / `position_override_configs` / new `role_config_overrides`)
- Compensability semantics (whether a time entry produces a compensable event)
- Workflow enablement that changes which payroll lines emit (e.g., the ADR-024 D7 overtime authorization workflow is GLOBAL per D7 §6; feature flags cannot opt institutions out of it)
- Any cell whose value would differ in the rule-engine evaluation path

Per ROADMAP L24 + ADR-024 D6 + ADR-024 D7 §6: "rule interpretation is GLOBAL — no per-institution interpretation override is permitted". Feature flags toggle UI surface presentation; they NEVER cross the rule-engine boundary. Phase E continuous-validation test in S39 asserts: every flag in `docs/operations/feature-flags-catalog.md` MUST declare `surface_scope: "ui-only"` and any new flag attempting `surface_scope: "rule-engine"` rejects at catalog-validation time.

**Catalog doc** enforces this in the per-flag description with explicit `surface_scope` declaration + 1-sentence rationale showing the flag doesn't cross the rule-engine boundary. Example catalog entry:

```yaml
- name: flex_carryover_grace_days
  surface_scope: ui-only
  type: number
  default: 0
  rationale: |
    Display-only grace-period hint for admins viewing flex balance near year-end.
    Rule engine flex_carryover_max calculation unchanged; this number only
    influences a UI tooltip "X days until carryover deadline".
```

**Admin UI**: extension of existing `LocalConfigurationsEditor.tsx` (S12 / S14 origin); add feature-flags pane with per-flag checkbox / value input + documentation tooltip rendering the catalog `rationale`.

**Cross-reference**: ADR-024 D7 §6 "No per-institution opt-in/out" applies to the overtime authorization workflow. The pre-approval-vs-post-hoc-ack choice is per-ENTRY at the workflow level, not per-TENANT. Feature flags cannot expose this choice as a tenant toggle.

### D7 — DEFERRED to ADR-026 (Audit Visibility Surface — dedicated design sprint)

**Status (cycle-3 halt-and-prompt resolution, 2026-05-21)**: this decision is DEFERRED from ADR-025 to a dedicated future ADR — **ADR-026 (Audit Visibility Surface)** — per user adjudication at S38 Step 7a cycle-3 halt-and-prompt per `feedback_thrash_defer_real_world.md`.

**Why deferred**: cycle-1 / cycle-2 / cycle-3 Step 7a reviews each surfaced new defects in this same architectural area — first the missing endpoint surface (cycle 1), then the row-shape gap (cycle 2), then the internal contradiction between honest row-shape acknowledgment and event-type-completeness D-test claims (cycle 3). Three cycles disclosing new defects in the same area is the canonical signal that the architectural seam is wider than one decision in a multi-decision ADR can carry. The audit-visibility surface needs its own dedicated design pass.

**Open problem (carried into ADR-026)**: institution-internal auditors need to query the audit log for their institution's events without seeing other institutions'. Two known architectural paths:

- **(A) Minimal scope-by-actor** — JOIN `audit_log.actor_id → users.primary_org_id` + materialized-path subtree check. Limitation: operator/system actions affecting a tenant from an outside-actor are NOT visible. Implementable on current `audit_log` row shape (`actor_id, http_path, http_status, details`).
- **(B) Schema extension for scope-by-target** — ALTER `audit_log` ADD `target_org_id NULL` + `target_resource_id NULL` + `event_type NULL`. Retrofit `AuditLoggingMiddleware` to populate these from per-endpoint context. Touches every state-changing endpoint. Enables event-type-completeness + tenant-targeted query semantics.

ADR-026 design sprint adjudicates between (A) + (B) + hybrid + an event-sourcing-aligned alternative (project audit-relevant events into a dedicated `audit_projection` table with explicit `target_org_id` per ADR-018 D13 sync-in-tx projection pattern — preserving event-log immutability + enabling tenant-scoped queries without retrofitting the request-middleware audit row).

**Verified existing-code state** (cycle-2 verification preserved for ADR-026 design input):
- No `/api/admin/audit/` endpoint exists today
- `AuditLogRepository.cs` exposes only `QueryByActorAsync` (L42) + `QueryByCorrelationAsync` (L62)
- `OrgScopeValidator.cs` exposes `ValidateEmployeeAccessAsync` (L32) + `ValidateOrgAccessAsync` (L85). No subtree-enumeration helper.
- `AuditLoggingMiddleware.cs:37` records `(actor_id, http_path, http_status, details)` per request; no `target_org_id` / `target_resource_id` / `event_type` columns.

**Forward pointer**: ADR-026 placeholder filed at `docs/knowledge-base/decisions/ADR-026-audit-visibility-surface.md` with Status: PLANNED. Sprint slot: TBD (likely between S39 and S40, or folded into S40 prep if the schema-extension path lands as scope-creep there). Pre-launch posture: audit-visibility surface is launch-required for first-customer go-live commitment per PROGRAM L279 + ADR-025 final §Customer-go-live commitment — so ADR-026 cannot defer past S39.

**No Phase B dependency** — audit-visibility is system-design + security-correctness; not cirkulær-dependent.

### D8 — Explicit `Institution` type vs generic top-level org

**Problem**: today an institution = top-level org node by convention. The convention is implicit: any `organization` row with `materialized_path` matching `/{org_id}/$` is an institution. Customer-onboarding, billing, SLS configuration all rely on this convention without explicit type-level encoding.

**Three options considered**:

- **(a)** Add `is_institution BOOLEAN NOT NULL DEFAULT FALSE` column to `organizations`; set TRUE for top-level rows. Simplest; encodes the convention. Minor schema change.
- **(b)** Promote institution to a separate `institutions` table with `org_id REFERENCES organizations(org_id)` + institution-specific columns (legal_name, cvr_number, billing_contact, etc.). Cleanest separation; institution-specific metadata gets a proper home. More schema.
- **(c)** Keep current convention; document it in `docs/ARCHITECTURE.md`. Cheapest; relies on `materialized_path` regex check at every boundary.

**Decision: option (b)** — separate `institutions` table.

**Why**: customer-onboarding (D2) + per-tenant SLS configuration (D1) + billing + GDPR scope (D3) all need institution-specific metadata that doesn't fit on the generic `organizations` row. Bolt-on columns to `organizations` would pollute the generic-org schema. Separate `institutions` table preserves the org-hierarchy generic shape and gives institution metadata a proper home.

**Schema**:

```sql
CREATE TABLE institutions (
    institution_id      UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    org_id              TEXT        NOT NULL UNIQUE REFERENCES organizations(org_id),
    legal_name          TEXT        NOT NULL,
    cvr_number          TEXT,                    -- Danish business registration number (optional; some state institutions don't have one)
    billing_contact_name TEXT       NOT NULL,
    billing_contact_email TEXT      NOT NULL,
    onboarded_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    onboarded_by_actor_id TEXT,
    subscription_tier   TEXT        NOT NULL DEFAULT 'STANDARD' CHECK (subscription_tier IN ('STANDARD', 'PREMIUM', 'TRIAL')),
    is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
    deactivated_at      TIMESTAMPTZ
);
```

**Migration path**: greenfield-baked at S39; for upgrades from pre-S39 production (n/a pre-launch but documented in the S39 ALTER ledger entry), a backfill seeder populates `institutions` from existing top-level `organizations` rows + a default LegalName = `org_name`. Customer-onboarding workflow per D2 atomically creates both `organizations` + `institutions` rows.

**Consumer cutover**: D1 (`tenant_sls_configurations`) keys by `org_id` but resolves through institutions table for legal_name display. D2 (provisioning endpoint) creates institutions row. D3 (per-tenant export) reads institutions metadata for the export header. D5 (cross-tenant reports) aggregates by institutions for billing-tier slicing.

**Convention preserved**: "institution = top-level org" still holds at the materialized-path level; institutions row is a 1:1 extension with extra columns. Existing code that does the path regex check stays correct; new code that needs institution-specific metadata reads through the institutions table.

## Consequences

### S39 schema migration (alongside ADR-024's tables)

New tables introduced by ADR-025:
- `tenant_sls_configurations` (D1) — versioned-config pattern per ADR-018 D7
- `institutions` (D8) — straight schema; no versioning needed (institution metadata changes infrequently; events handle audit)
- `erased_users_audit` (D3 Part B) — append-only audit
- ALTER `local_configurations` add `feature_flags` JSONB column (D6)

Ledger entries: `s39-d1-tenant-sls-configs` + `s39-d3-erased-users-audit` + `s39-d6-local-configurations-feature-flags` + `s39-d8-institutions-table`.

### S40 cutover (alongside ADR-024's cutover)

- `TenantSlsConfigurationRepository` + `PayrollExportService` cutover (D1) — per-tenant SLS endpoint lookup before each batch submission
- `POST /api/admin/institutions/provision` endpoint + `customer-onboarding-runbook.md` (D2)
- `GET /api/admin/institutions/{org_id}/export` + `POST /api/admin/users/{user_id}/erase` endpoints (D3)
- OutboxPublisher per-tenant telemetry counters (D4 instrumentation; fairness mechanism deferred post-launch)
- `/api/admin/reports/cross-tenant/` endpoint suite + `CrossTenantReports.tsx` GlobalAdmin page (D5)
- `local_configurations.feature_flags` consumer in `ConfigResolutionService.GetEffectiveFeatureFlags(org_id)` (D6) + admin UI extension + `feature-flags-catalog.md`
- `InstitutionsRepository` + 1:1 enforcement on `organizations` top-level rows (D8) + backfill seeder

**D7 audit-visibility surface DEFERRED to ADR-026** (filed as placeholder; sprint slot TBD, cannot defer past S39 per launch commitment).

**New event types** (alongside ADR-024's 7): `InstitutionProvisioned` + `InstitutionDataExported` + `UserPiiErased` + `CrossTenantReportAccessed` (4 events; combined with ADR-024's 7 new events, **EventSerializer 58 → 69** after S40).

### S41 D-tests + governance bake-in

- D1 D-test: per-tenant SLS endpoint resolution + fallback to global config + credential ref resolves through secret store mock
- D2 D-test: customer-onboarding endpoint atomicity (all 5 ops in one tx; rollback verified)
- D3 D-test: per-tenant export scoping (LocalAdmin sees only own institution) + Article 17 erasure NULL-out semantics + past-payroll replay-after-erasure produces NULL PII
- D5 D-test: cross-tenant report endpoint authorisation (LocalAdmin gets 403; GlobalAdmin succeeds) + audit emission on each query
- D6 D-test: feature_flags JSONB resolution + unknown-flag-ignored + per-flag opt-in/out semantics
- (D7 D-tests deferred to ADR-026 — audit-visibility surface design pass)
- D8 D-test: institutions row 1:1 with top-level org; cascade on org soft-delete; backfill seeder idempotency

`docs/SECURITY.md` updated to document the D5 cross-tenant report bypass as the single canonical scope-binding exception. `docs/ARCHITECTURE.md` updated for D8 institutions-table.

### Customer-go-live commitment

**Per ROADMAP Risk + Dependencies (PROGRAM L279)**: "First customer go-live should NOT happen before S38 ADR-025 lands; defer customer-go-live commitment". This ADR landing (post-Step 7a ACCEPTED) unblocks first-customer go-live planning. S39+S40 implementation lands the actual code before any customer is provisioned.

### Post-launch deferred

- **D4 fairness implementation** — when telemetry shows threshold breach OR 151st institution provisioned
- **ADR-027** — bug-with-past-impact workflow + SLS reconciliation pattern (placeholder per ADR-024 D6)
- **Per-tenant SLS credential rotation runbook** — covered at high level in D1's versioning; operational detail deferred to first credential rotation event

## References

- ROADMAP "Deployment Model" L16-27 — single logical deployment, 150 institutions, glocal rule encoding
- ROADMAP L389 — multi-tenant operational concerns (S35 discovery; this ADR settles each)
- PROGRAM-s36-s41-domain-correctness.md L142-151 — 8 decisions enumerated
- ADR-001 (event sourcing) — events immutable; D3 erasure operates on projections not events
- ADR-008 (materialized-path org hierarchy) — institution = top-level org node convention; D8 extends with explicit table
- ADR-009 (scope-embedded JWT) — D5 cross-tenant report bypass documented as single exception
- ADR-014 (DB-backed agreement configs) — D6 feature flags follow versioned-config pattern
- ADR-016 D10 (replay determinism) — D3 erasure preserves past-payroll byte-identical replay (with NULL PII)
- ADR-017 (local agreement profile) — D6 feature_flags column extends local_configurations
- ADR-018 D3 (atomic outbox) + D7 (row-version) + D13 (sync-in-tx projection) — D1 + D2 + D3 endpoints follow these patterns
- ADR-019 D2 (admin-strict If-Match) — D1 versioned-config endpoint inherits
- ADR-024 (companion; S38 TASK-3801) — role-within-agreement modeling; orthogonal
- ADR-013 amendment (companion; S38 TASK-3803) — cross-reference D3 + D6 bug correction
- **ADR-026** (placeholder filed S38 TASK-3804 cycle-3 halt-and-prompt resolution) — audit visibility surface design sprint; absorbs the deferred D7 design
