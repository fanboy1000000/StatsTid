using Npgsql;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// Pre-S22 (S21-era) DDL for <c>local_agreement_profiles</c> and
/// <c>local_agreement_profile_audit</c>: NO <c>version</c> column, NO <c>'MODIFIED'</c>
/// in the audit-action CHECK, end-inclusive <c>effective_to</c> semantics.
///
/// <para>
/// Used exclusively by <see cref="EndExclusiveMigrationTests"/> (D12 #11–#13) to seed
/// pre-migration row shapes and then apply <see cref="S22MigrationDdl"/> on top to
/// prove the migration converts correctly. Other tests should use the post-S22
/// <see cref="ProfileTestSchema"/>.
/// </para>
/// </summary>
internal static class LegacyProfileSchema
{
    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS organizations (
            org_id              TEXT        PRIMARY KEY,
            org_name            TEXT        NOT NULL,
            org_type            TEXT        NOT NULL CHECK (org_type IN ('MAO', 'ORGANISATION')),
            parent_org_id       TEXT        REFERENCES organizations(org_id),
            materialized_path   TEXT        NOT NULL,
            agreement_code      TEXT        NOT NULL DEFAULT 'AC',
            ok_version          TEXT        NOT NULL DEFAULT 'OK24',
            is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS local_agreement_profiles (
            profile_id                          UUID            PRIMARY KEY DEFAULT gen_random_uuid(),
            org_id                              TEXT            NOT NULL REFERENCES organizations(org_id),
            agreement_code                      TEXT            NOT NULL,
            ok_version                          TEXT            NOT NULL,
            effective_from                      DATE            NOT NULL,
            effective_to                        DATE,
            weekly_norm_hours                   NUMERIC(5,2),
            max_flex_balance                    NUMERIC(6,2),
            flex_carryover_max                  NUMERIC(6,2),
            max_overtime_hours_per_period       NUMERIC(6,2),
            overtime_requires_pre_approval      BOOLEAN,
            created_by                          TEXT            NOT NULL,
            created_at                          TIMESTAMPTZ     NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS uq_local_agreement_profile_active
            ON local_agreement_profiles (org_id, agreement_code, ok_version)
            WHERE effective_to IS NULL;

        CREATE TABLE IF NOT EXISTS local_agreement_profile_audit (
            audit_id        BIGSERIAL       PRIMARY KEY,
            profile_id      UUID            NOT NULL,
            action          TEXT            NOT NULL CHECK (action IN (
                'CREATED', 'SUPERSEDED', 'DEACTIVATED', 'MIGRATED_FROM_LEGACY'
            )),
            delta_jsonb     JSONB           NOT NULL,
            actor_id        TEXT            NOT NULL,
            actor_role      TEXT            NOT NULL,
            timestamp       TIMESTAMPTZ     NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS schema_migrations (
            migration_id  TEXT         PRIMARY KEY,
            applied_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            notes         TEXT         NULL
        );
        """;

    /// <summary>
    /// The S22 migration DO $$ block from <c>docker/postgres/init.sql</c> lines ~1278-1305
    /// — the single canonical source of the migration logic. Idempotent via
    /// <c>schema_migrations</c> ledger guard.
    /// </summary>
    public const string S22MigrationDdl = """
        DO $$
        BEGIN
            INSERT INTO schema_migrations (migration_id, notes)
            VALUES ('s22-d7-d8-d9', 'ADR-018: row-version + end-exclusive + MODIFIED audit action')
            ON CONFLICT (migration_id) DO NOTHING;

            IF NOT FOUND THEN
                RETURN;
            END IF;

            ALTER TABLE local_agreement_profiles
            ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

            UPDATE local_agreement_profiles
            SET effective_to = effective_to + INTERVAL '1 day'
            WHERE effective_to IS NOT NULL;

            ALTER TABLE local_agreement_profile_audit
            DROP CONSTRAINT IF EXISTS local_agreement_profile_audit_action_check;

            ALTER TABLE local_agreement_profile_audit
            ADD CONSTRAINT local_agreement_profile_audit_action_check
            CHECK (action IN ('CREATED', 'MODIFIED', 'SUPERSEDED', 'DEACTIVATED', 'MIGRATED_FROM_LEGACY'));
        END
        $$;
        """;

    public static async Task ApplyAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task RunS22MigrationAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(S22MigrationDdl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
