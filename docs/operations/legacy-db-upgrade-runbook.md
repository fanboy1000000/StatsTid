# Legacy Database Upgrade Runbook

> **Created**: S46 (2026-05-24)
> **Scope**: Upgrading a pre-existing PostgreSQL database to the current init.sql schema

## Background

`docker/postgres/init.sql` only runs on **fresh data directories** (first `docker compose up` with no existing volume). If the database was created by an earlier sprint's init.sql, new tables and columns added in later sprints are NOT automatically applied.

The init.sql uses `CREATE TABLE IF NOT EXISTS` + guarded `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` blocks for forward-compatibility on greenfield deployments. However, pre-existing Docker volumes with older schemas need explicit migration.

## Pre-Launch Posture

StatsTid is **pre-launch** — no production data exists. The recommended upgrade path is **volume wipe + fresh bootstrap**:

```bash
docker compose down -v          # removes volumes (DESTRUCTIVE — all data lost)
docker compose up -d            # fresh init.sql runs on empty data directory
```

This is the simplest and safest path. All seed data is in init.sql.

## Post-Launch Upgrade Path

Once production data exists, volume wipe is not an option. Two approaches:

### Option A: pg_dump / pg_restore (recommended for major version jumps)

```bash
docker compose exec postgres pg_dump -U statstid statstid > backup.sql
docker compose down -v
docker compose up -d postgres   # fresh schema from init.sql
docker compose exec -T postgres psql -U statstid statstid < backup.sql
```

**Caveat**: data-only restore (`--data-only`) requires schema compatibility. For large schema changes, a migration script is more reliable.

### Option B: Manual ALTER application (for incremental upgrades)

Apply the guarded ALTER blocks from init.sql in order. Each sprint's additions are documented below.

## Sprint-by-Sprint Schema Additions

| Sprint | Table(s) | Columns / Changes | init.sql location |
|--------|----------|-------------------|-------------------|
| S3 | events | actor_id, actor_role, correlation_id | ~L407-411 |
| S9 | approval_periods | employee_approved_at, employee_deadline, manager_deadline | ~L897-902 |
| S21 | local_agreement_profiles | version BIGINT | ~L1612-1613 |
| S22 | outbox_events + schema_migrations | New tables (CREATE IF NOT EXISTS) | ~L5-71 |
| S25 | agreement_configs, position_override_configs, wage_type_mappings, entitlement_configs | version BIGINT columns | ~L1652-1665 |
| S25 | *_audit tables | version_before, version_after BIGINT | ~L1670-1680 |
| S29 | wage_type_mappings | mapping_id UUID PK, effective_from/effective_to | ~L1709-1729 |
| S30 | entitlement_configs | effective_from/effective_to, entitlement_config_audit table | ~L1787-1800 |
| S31 | employee_profiles + employee_profile_audit | New tables (CREATE IF NOT EXISTS) | ~L480-520 |
| S34 | user_agreement_codes + user_agreement_code_audit | New tables (CREATE IF NOT EXISTS) | ~L1830-1842 |
| S35 | users | version BIGINT, users_audit table | ~L1843-1844 |
| S40 | role_config_overrides + role_config_override_audit, overtime_pre_approvals extension | New tables + columns | ~L1900-1982 |
| S43 | audit_projection | New table (CREATE IF NOT EXISTS) | ~L2035-2080 |

## Known Ordering Gap

**Entitlement_configs seed data** (init.sql ~L1358): The seed INSERT includes `effective_from` in the column list, but the base `CREATE TABLE` at ~L1289 does NOT include `effective_from` (it's added by the S30 guarded ALTER at ~L1787). On a greenfield deployment this works because the full init.sql runs top-to-bottom. On a pre-S30 legacy DB, the ALTER must be applied BEFORE the seed data can be re-inserted.

## Verification

After migration, verify table counts match greenfield expectations:

```sql
SELECT schemaname, tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename;
-- Expected: ~30 tables (events, event_streams, organizations, users, ...)
```
