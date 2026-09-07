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

> **QUAL-012 (S134): the `init.sql location` column now uses DURABLE search references, not absolute
> `~L###` line numbers.** The prior line numbers had drifted (init.sql grows every sprint; ~11 of 14 rows
> pointed at unrelated lines, and one named tables that no longer exist) — the same rot class as QUAL-090.
> Each row names the CREATE / ALTER / index block to `grep` for in `docker/postgres/init.sql`, which does
> not drift. (Mirrors the S122 row's long-standing pattern.)

| Sprint | Table(s) | Columns / Changes | init.sql location (grep for) |
|--------|----------|-------------------|------------------------------|
| S3 | events | actor_id, actor_role, correlation_id | the `CREATE TABLE ... events` body + these columns |
| S9 | approval_periods | employee_approved_at, employee_deadline, manager_deadline | the `approval_periods` block + these deadline columns |
| S21 | local_agreement_profiles | version BIGINT | `local_agreement_profiles` + its `version` column |
| S22 | outbox_events + schema_migrations | New tables (CREATE IF NOT EXISTS) | `CREATE TABLE IF NOT EXISTS outbox_events` + `schema_migrations` |
| S25 | agreement_configs, position_override_configs, wage_type_mappings, entitlement_configs | version BIGINT columns | the `version` column on each of the four named tables |
| S25 | *_audit tables | version_before, version_after BIGINT | `version_before` / `version_after` on the `*_audit` tables |
| S29 | wage_type_mappings | mapping_id UUID PK, effective_from/effective_to | `wage_type_mappings` + `mapping_id` / `effective_from` / `idx_wtm_natural_key_*` |
| S30 | entitlement_configs | effective_from/effective_to, entitlement_config_audit table | `entitlement_configs` effective-dating + `CREATE TABLE ... entitlement_config_audit` |
| S31 | employee_profiles + employee_profile_audit | New tables (CREATE IF NOT EXISTS) | `CREATE TABLE IF NOT EXISTS employee_profiles` + `employee_profile_audit` |
| S34 | user_agreement_codes + user_agreement_code_audit | New tables (CREATE IF NOT EXISTS) | `CREATE TABLE IF NOT EXISTS user_agreement_codes` + `user_agreement_code_audit` |
| S35 | users | version BIGINT, users_audit table | `users` `version` column + `CREATE TABLE ... users_audit` |
| S40 | role_config_overrides + role_config_override_audit, overtime_pre_approvals extension | New tables + columns | `role_config_overrides` + `role_config_override_audit` + the `overtime_pre_approvals` D7 columns |
| S43 | audit_projection | New table (CREATE IF NOT EXISTS) | `CREATE TABLE IF NOT EXISTS audit_projection` (+ the `s43-d1-audit-projection-table` guarded block) |
| S97 | ~~enheder + user_enheder~~ **SUPERSEDED** | ~~New tables + `idx_enheder_active_name`~~ | **OBSOLETE — `enheder`/`user_enheder` were REPLACED by `units`/`unit_leaders`/`users.unit_id` (ADR-038, S103) as a GREENFIELD reseed, NOT a migration (D9). A pre-ADR-038 legacy DB is reseeded, not upgraded through this row; a current init.sql has no `enheder` tables. For the current model grep `CREATE TABLE ... units` + `unit_leaders`.** |
| S122 | agreement_configs, overtime_balances | DEFAULT flip `'UDBETALING'`→`'AFSPADSERING'` + named CHECK on the compensation-model column (each in BOTH the CREATE-body inline form AND a guarded post-table `ALTER COLUMN SET DEFAULT` + `DROP/ADD CONSTRAINT` block) | search `agreement_configs_default_compensation_model_check` / `overtime_balances_compensation_model_check` in init.sql (CREATE-body inline + guarded ALTER, ~2 sites each) |
| S136 | users | Named CHECK `users_employment_window_check` (`end >= start`, NULL = unbounded per ADR-040 D2 — lands with NO backfill). **The guarded segment's census FAILS LOUD on any existing `end < start` row** (raises with the violating user_ids; the ledger insert rolls back too, so the rerun-after-fix is a plain re-run) — choosing which employment boundary is wrong is business history, an OPERATOR decision, never auto-repaired. Cannot be CREATE-body inline (both employment-date columns land via later ALTERs), so both forms sit at EOF: the `schema_migrations`-guarded segment + an unguarded DROP/ADD. NOTE: `generate_db_schema.py` parses only CREATE-body constraints + ALTER-added COLUMNS, so this constraint is deliberately absent from `docs/generated/db-schema.md` (known generator limitation, recorded here). | search `s136-employment-window-check` / `users_employment_window_check` in init.sql (marker-delimited segment `S136-EMPLOYMENT-WINDOW-CHECK-SEGMENT-BEGIN/END` + the unguarded re-land after it) |
| S137 | employee_profiles | `employment_category TEXT` added NULLABLE (ADR-040 D4 — "what category was this employee in March?" becomes answerable) + a HISTORY-covering backfill from `users.employment_category` onto EVERY existing row, live and closed. Copying the live value onto history rows is CORRECT, not an approximation: the users column was write-once until S138, so the live value IS the value that held over every historical row's window. NULLABLE by ruled design with NO census — reads used `COALESCE(ep.employment_category, u.employment_category)`, so a missed write degraded to the definitionally-correct live value rather than crashing. (That posture is REVERSED by S138 below — apply S137 before S138.) | search `s137-profile-category` in init.sql (marker-delimited segment `S137-PROFILE-CATEGORY-SEGMENT-BEGIN/END` + the file-scope `ADD COLUMN IF NOT EXISTS employment_category` after the `employee_profiles` CREATE) |
| S138 | employee_profiles, hr_backdate_worklist | **(a)** `employment_category` tightened to **NOT NULL** — S138 makes the category editable per date (a change writes a NEW dated row; `users.employment_category` becomes merely the cache of today's row), which destroys the premise that made S137's COALESCE fallback safe: it would answer a March question with today's value. **The guarded segment's census FAILS LOUD** on any `employment_category IS NULL` row (raises naming the `profile_id`s; the ledger insert rolls back, so fix-then-plain-rerun) — the S136 precedent, deliberately unlike S137. **ORDERING CONSTRAINT (loud, never silent): the file-scope `ADD COLUMN` now says NOT NULL, and PostgreSQL refuses that on a NON-EMPTY table — so a PRE-S137 database that still holds `employee_profiles` rows cannot be upgraded by applying the current init.sql directly; it must pass through an S137-era release first (or be reseeded per the Pre-Launch Posture above).** Owner-accepted at the S138 close: the alternative (branch on emptiness) would make the doc generator's first-match parse order load-bearing. **(b)** NEW table `hr_backdate_worklist` — the HR diagnostic list of already-exported months / already-settled holiday years that a backdated correction made stale (ADR-013: a list, never an auto-cascade). Two row KINDS under CHECK-tied keys, `triggers JSONB` (per-trigger baseline captured at append), two partial UNIQUEs on open rows. `export_id` is a plain REFERENCE column with NO foreign key — `payroll_export_records` is Payroll-context-owned (ADR-034). | search `s138-profile-category-not-null` and `s138-backdate-worklist` in init.sql (segments `S138-PROFILE-CATEGORY-NOTNULL-SEGMENT-BEGIN/END` and `S138-BACKDATE-WORKLIST-SEGMENT-BEGIN/END`; plus the file-scope `employment_category TEXT NOT NULL` after the `employee_profiles` CREATE) |

## S122 — Compensation-model DB CHECK + default correction (TASK-12200)

S122 gives the compensation-model vocabulary a DB authority (the S120-flagged P6 gap) and eradicates the S17 `'UDBETALING'` default-inversion trap. On a **greenfield** DB the CREATE-TABLE-inline `CONSTRAINT` + flipped DEFAULT apply automatically. On a **legacy/incremental** DB, the inline CREATE is skipped (table exists), so the guarded ALTER blocks are the effective path — run them:

### Census (before adding the constraint — pre-launch, expected empty)

```sql
-- Any stored value outside the allowed set would block the ADD CONSTRAINT.
SELECT 'agreement_configs' AS tbl, config_id::text AS id, default_compensation_model AS val
FROM agreement_configs
WHERE default_compensation_model NOT IN ('AFSPADSERING', 'UDBETALING')
UNION ALL
SELECT 'overtime_balances', employee_id || ':' || period_year::text, compensation_model
FROM overtime_balances
WHERE compensation_model NOT IN ('AFSPADSERING', 'UDBETALING');
```

Expected: **zero rows** (the only writers are the inline-validated endpoints + the seeds, all in-set). If a row surfaces, correct it to the agreement-appropriate value (per `docs/references/danish-agreements.md`) before the ADD CONSTRAINT. **Note the pre-launch inversion class:** admin-CLONED configs created before S122 may carry `'UDBETALING'` from the field-loss bug (in-set, so not blocked, but semantically wrong for an AFSPADSERING agreement) — a value review, not a constraint blocker; forward-only per the S35 lineage.

### Apply the guarded constraints + default correction

```sql
ALTER TABLE agreement_configs ALTER COLUMN default_compensation_model SET DEFAULT 'AFSPADSERING';
ALTER TABLE agreement_configs DROP CONSTRAINT IF EXISTS agreement_configs_default_compensation_model_check;
ALTER TABLE agreement_configs ADD CONSTRAINT agreement_configs_default_compensation_model_check
    CHECK (default_compensation_model IN ('AFSPADSERING', 'UDBETALING'));

ALTER TABLE overtime_balances ALTER COLUMN compensation_model SET DEFAULT 'AFSPADSERING';
ALTER TABLE overtime_balances DROP CONSTRAINT IF EXISTS overtime_balances_compensation_model_check;
ALTER TABLE overtime_balances ADD CONSTRAINT overtime_balances_compensation_model_check
    CHECK (compensation_model IN ('AFSPADSERING', 'UDBETALING'));
```

The DEFAULT flip changes no existing row (every writer stamps the column explicitly and every seed is `'AFSPADSERING'`); it is belt-and-suspenders for the code-side default-trap fix.

## S97 — Enhed structured-metadata backfill (TASK-9704)

> **LEGACY / RETIRED (S103 + S110, ADR-038).** This whole section describes the now-superseded `enheder`/`user_enheder`/`enhed_label` model. The Enhedsspor re-architecture (ADR-038) replaced it with the `units`/`unit_leaders`/`users.unit_id` model in S103 (greenfield reseed, D9 — the `enhed_label` COLUMN was removed then), and S110 removed the last vestigial `enhedLabel` display field from the read responses. Retained below for historical context only; it does NOT apply to the current schema.

S97 replaces the free-text `employee_profiles.enhed_label` with a structured `enheder`
entity table + a `user_enheder` multi-tag membership link (ADR-035; pure display metadata,
zero authority/scope/approval meaning). `enhed_label` is **kept read-only** as a display
fallback — it is NOT dropped this sprint.

### Schema (apply the guarded blocks for a legacy DB)

**[SUPERSEDED (QUAL-012) — the `enheder`/`user_enheder` tables described in this S97 section were
REPLACED by `units`/`unit_leaders`/`users.unit_id` via ADR-038 (S103) as a GREENFIELD RESEED, not a
migration (D9). A current `init.sql` has no `enheder` tables + no `idx_enheder_active_name`, so these
blocks no longer exist to apply; a pre-ADR-038 legacy DB is reseeded, not upgraded through this section.
For the current unit model grep `CREATE TABLE ... units` + `unit_leaders` in init.sql.]**

(Historical, for provenance:) the two enheder tables were `CREATE TABLE IF NOT EXISTS` (so they applied
on both greenfield and legacy at S97), incl. the partial unique index `idx_enheder_active_name ON enheder
(organisation_id, lower(name)) WHERE deleted_at IS NULL` and `idx_user_enheder_enhed`.

### Data backfill (`EnhedBackfillSeeder`, runs at app startup)

The backfill runs automatically in `Program.cs` AFTER the employee-profile seed. It is the
**legacy-db-upgrade mechanism**, not a greenfield seeder:

- **Source = the projection column, not an event replay.** It reads
  `SELECT DISTINCT u.primary_org_id, ep.enhed_label FROM employee_profiles ep JOIN users u
  ON u.user_id = ep.employee_id WHERE ep.effective_to IS NULL AND ep.enhed_label IS NOT NULL
  AND TRIM(ep.enhed_label) <> ''`. The demo seed wrote `enhed_label` by a raw projection
  INSERT and never emitted `EmployeeProfileCreated.EnhedLabel`, so a replay would migrate
  nothing — the column is the only source of truth.
- **Event-sourced (no raw projection INSERT — the S92 lesson).** For each distinct
  `(Organisation, label)` it emits `EnhedCreated` (stream `enhed-{id}`); then for each
  labeled user it emits `UserEnhederChanged(userId, [enhedId])` (stream `user-{userId}`).
  Projection writes + outbox events commit in one tx each (ADR-018 D3).
- **Greenfield = NO-OP by design.** The init.sql baseline (what CI reseeds) has
  `enhed_label` universally NULL, so the source query returns zero rows and the seeder
  exits early. There is nothing to migrate on a fresh bootstrap — labels only exist on a
  database that carried demo/legacy `enhed_label` values.
- **Idempotent.** Re-running does not duplicate enheder (it reuses any existing active
  enhed matching `(organisation_id, lower(name))` — the partial-unique key) or tags (it
  skips a user whose current `user_enheder` set already equals the desired single tag). A
  concurrent-startup `23505` on the active-name index is caught and re-resolved to the
  winner's id.

### Verification (legacy DB with labels)

```sql
-- One active enhed per distinct (org, label) that appears on a live profile:
SELECT COUNT(*) FROM enheder WHERE deleted_at IS NULL;
-- Every live, labeled user carries exactly one tag (no user loses metadata):
SELECT COUNT(*) FROM user_enheder;
```

## S139 — Database session time zone is assumed UTC (QUAL-153 clock seam)

**What changed and why it matters for an upgrade.** S139 moved the clock SOURCE on the profile path, the
agreement-code path and the approval-period status projection behind the app's injected `TimeProvider`, so that a
fixed test clock reaches every read on those paths. Two SQL statements that used to take the DATE from the database
clock now take it from the application as a bound UTC `DateOnly` parameter: the profile soft-delete
(`EmployeeProfileRepository.SoftDeleteAsync`, formerly `SET effective_to = NOW()::date`, now `= @today`) and the
period-status projection (`ApprovalPeriodRepository.GetPeriodStatusProjectionForTreeAsync`, formerly
`ap.period_end < CURRENT_DATE`, now `< @today`). This is behaviour-preserving **only because the Postgres session
time zone is UTC** — the image default; no `TZ` / `PGTZ` / `timezone =` / `SET TIME ZONE` override exists in
`docker-compose*.yml`, `docker/postgres/init.sql` or the Testcontainers harness (verified S139). Under UTC,
`NOW()::date` was already the UTC day the app computes.

**If a non-greenfield server is configured with another time zone** (e.g. `Europe/Copenhagen`), the two converted
statements now write/compare the UTC day where they previously used the server's local day — a 1–2 hour window
each night, and in the correct direction (the validator and the stamp finally agree). The remaining DATE reads still
taken from the database clock follow the SERVER's zone and would disagree with the app for that window:
`ReportingLineRepository.cs` (`SET effective_to = CURRENT_DATE` when closing an approver line),
`DelegationExpiryService.cs` (`until_date < CURRENT_DATE`), `LocalAgreementProfileMigrator.cs` (startup compare),
`init.sql` (the SELF_DELEGATION backfill block), plus two dead sites (`RoleConfigOverrideRepository`,
`LocalConfigurationRepository.GetActiveByOrgAsync`). They are registered in the QUAL register (S139 rows: "SQL clock
sites not parameterised") with their reach.

**Runbook step:** before upgrading a pre-existing database, verify the server/session zone — `SHOW timezone;` must
return `UTC` (or set `ALTER DATABASE statstid SET timezone = 'UTC';`). Do not "fix" the remaining sites by changing
the zone to Copenhagen: the app's UTC-day rule on the profile/agreement paths is deliberate (owner ruling OQ-3 (a),
S139 — it matches the frontend's `toISOString().slice(0,10)`), and the UTC-vs-Copenhagen split is its own QUAL row
awaiting a domain ruling.

## Known Ordering Gap

**Entitlement_configs seed data** (grep `INSERT INTO entitlement_configs`): The seed INSERT includes `effective_from` in the column list, but the base `CREATE TABLE ... entitlement_configs` does NOT include `effective_from` (it is added by the S30 guarded ALTER — grep `s30-d2-ec-effective-dating`). On a greenfield deployment this works because the full init.sql runs top-to-bottom. On a pre-S30 legacy DB, the ALTER must be applied BEFORE the seed data can be re-inserted. (QUAL-012: line pointers replaced with durable greps.)

## Verification

After migration, verify table counts match greenfield expectations:

```sql
SELECT schemaname, tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename;
-- Expected: ~67 tables (events, event_streams, organizations, users, ...)
```
