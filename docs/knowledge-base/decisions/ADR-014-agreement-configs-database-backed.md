# [ADR-014] Agreement configs migrated from static code to database

| Field | Value |
|-------|-------|
| **ID** | ADR-014 |
| **Category** | decision |
| **Status** | approved |
| **Sprint** | Sprint 12 (planned) |
| **Date** | 2026-03-08 |
| **Domains** | Infrastructure, SharedKernel, Rule Engine, Payroll, Frontend |
| **Tags** | agreement-config, database, migration, configuration, lifecycle, versioning |

## Context
Agreement configs (AgreementRuleConfig) have been static compile-time data in `CentralAgreementConfigs.cs` since Sprint 2 (PAT-003). This requires code changes and redeployment to add or modify agreements. GlobalAdmins need to manage agreement configs through the UI — creating new agreements, cloning from existing ones, and managing OK version transitions — without developer involvement.

## Options Considered

### Option A — DB as source of truth, static as fallback
New user-created configs go to DB. Static configs remain for existing agreements. Resolution checks DB first, falls back to static.

**Rejected**: Two sources of truth creates ambiguity. Which one wins if both exist? Sync issues if static is updated in code but DB has a stale copy.

### Option B — DB for new configs only
Existing hardcoded configs remain static forever. Only new agreements use DB.

**Rejected**: Creates a permanent two-tier system. Existing agreements can never be managed through the UI. Adds complexity to resolution logic indefinitely.

### Option C — DB everywhere, static becomes migration seed (CHOSEN)
On first boot, seed the database from current static configs. After seeding, the database is the single source of truth. The static class becomes a seed/migration artifact only.

## Decision
Option C. Agreement configs are stored in a PostgreSQL `agreement_configs` table. On initial deployment, the table is seeded from the 10 existing static configs (AC, HK, PROSA, AC_RESEARCH, AC_TEACHING × OK24/OK26). After seeding, the database is authoritative.

### Versioning Lifecycle
Each config follows an immutable lifecycle: DRAFT → ACTIVE → ARCHIVED.
- ACTIVE configs are immutable — edits create a new DRAFT copy.
- Publishing a DRAFT auto-archives the current ACTIVE config for the same (AgreementCode, OkVersion).
- ARCHIVED configs are preserved for retroactive recalculation (P4) and audit (P3).

### Rule Engine Purity Preserved
The rule engine (P2) is unaffected. It continues to receive `AgreementRuleConfig` as a pure data parameter. The service layer (`ConfigResolutionService`) loads from DB instead of `CentralAgreementConfigs`. The rule engine never performs I/O.

### Resolution Chain (Updated)
```
DB (agreement_configs, status=ACTIVE) → Position Override → Local Override
```

Previously: `CentralAgreementConfigs (static) → Position Override → Local Override`

## Rationale
- Single source of truth eliminates sync hazards between code and DB
- Config changes don't require redeployment — GlobalAdmins self-serve
- Immutable versioning preserves retroactive recalculation correctness (P4)
- Event sourcing principles (P3) respected: ARCHIVED configs are never deleted
- Rule engine purity (P2) preserved: service layer mediates all I/O

## Consequences
- `CentralAgreementConfigs` static class is retained only as a seed source for initial DB population
- `ConfigResolutionService.ResolveAsync` must load from DB instead of static dictionary
- A new `AgreementConfigRepository` is needed for CRUD operations
- The `agreement_configs` table needs: all 31 AgreementRuleConfig fields + status + created/modified timestamps + created_by
- Config loading adds a DB query per resolution — consider caching for performance
- PAT-003 will be updated to reflect the new DB-backed pattern

## Supersedes
This decision updates PAT-003 (Agreement config as in-memory dictionary). The in-memory dictionary pattern remains valid for the rule engine's consumption of configs, but the source of truth shifts from static code to database.

## Agent Guidance
- **Infrastructure Agent**: `ConfigResolutionService` loads base config from DB (status=ACTIVE) instead of `CentralAgreementConfigs`. Position override and local override logic unchanged.
- **Rule Engine Agent**: No changes needed. Continue receiving `AgreementRuleConfig` as parameter. Never load configs directly.
- **Data Model Agent**: New `agreement_configs` table schema. Repository for CRUD.
- **Payroll Integration Agent**: No changes if configs are resolved before reaching payroll services.
- **UX Agent**: Agreement management page: overview (Active/Draft/Archived), editor with grouped fields, clone flow, publish/archive actions.
- **Test & QA Agent**: Verify seed migration produces identical configs to current static. Verify lifecycle transitions. Verify rule engine determinism unaffected.
