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

## S147 note (2026-10-29) — ADR-043 D12 puts this decision on the pay path

**In plain words.** This ADR decided that the database, not the code, holds the agreement configs, and that the rule engine receives resolved configs as data. That was built for the screens and for booking, never for pay: the Rule Engine still reads the configs compiled into the code, and refuses an agreement it holds none for (`RuleRegistry.cs:446-449`), while payroll registers the database resolver (`src/Integrations/StatsTid.Integrations.Payroll/Program.cs:147`) and never calls it (QUAL-233). ADR-043 (ACCEPTED 2026-10-29; owner ruling D-6 = (a), "Editors reach pay") confirms this ADR and implements it on the pay path. For each day of a month payroll resolves the published agreement config, the position override and the institution's local profile in force that day (central → position → local) at the service layer, and passes them to the pure engine in the request; the engine never reads a database (ADR-002).

**Implementation notes** (decided; built in S148's config stage):
- Agreement configs and position overrides gain an "applies from" date (QUAL-182). The lifecycle above records when a config was published, not from which day it applies.
- Every value a calculation used is frozen into its manifest, replacing ADR-016 D10's fingerprint of configs as code.
- A publish never changes an exported month. A past "applies from" date reaches an exported month only through an explicit correction (ADR-013, ADR-034).
- `CentralAgreementConfigs` stays a seed and a defence-in-depth fallback at most.
- Until the stage lands, the agreement editor and the local-profile editor say "affects screens and booking; does not yet affect pay" (S148 stage 0).
- The Agent Guidance line "Payroll Integration Agent: No changes if configs are resolved before reaching payroll services" did not hold: payroll resolves the configs itself, dated per day.
