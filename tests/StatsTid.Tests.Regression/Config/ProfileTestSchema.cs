using Npgsql;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// SQL DDL extending <see cref="Segmentation.TestFixtures.DockerHarness"/>'s baseline schema
/// with the S21 profile-suite tables (ADR-017): <c>organizations</c>, <c>local_configurations</c>,
/// <c>local_configuration_audit</c>, <c>local_agreement_profiles</c>, and
/// <c>local_agreement_profile_audit</c>.
///
/// <para>
/// Mirrors the post-S20 cleanup pattern (commit 537706b) of keeping a single source of truth
/// for schema-DDL inside the test layer; production <c>init.sql</c> remains the authoritative
/// schema, this is a hand-synthesised compatible subset for the testcontainer harness.
/// </para>
///
/// <para>
/// Schema drift between this DDL and <c>docker/postgres/init.sql</c> will NOT auto-surface —
/// when columns or constraints change in production, mirror them here as well.
/// </para>
/// </summary>
internal static class ProfileTestSchema
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

        CREATE TABLE IF NOT EXISTS local_configurations (
            config_id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            org_id              TEXT        NOT NULL REFERENCES organizations(org_id),
            config_area         TEXT        NOT NULL CHECK (config_area IN (
                'WORKING_TIME', 'FLEX_RULES', 'ORG_STRUCTURE', 'LOCAL_AGREEMENT', 'OPERATIONAL'
            )),
            config_key          TEXT        NOT NULL,
            config_value        JSONB       NOT NULL,
            effective_from      DATE        NOT NULL,
            effective_to        DATE,
            version             INT         NOT NULL DEFAULT 1,
            agreement_code      TEXT        NOT NULL,
            ok_version          TEXT        NOT NULL,
            created_by          TEXT        NOT NULL,
            approved_by         TEXT,
            approved_at         TIMESTAMPTZ,
            is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (org_id, config_area, config_key, effective_from, agreement_code, ok_version)
        );

        CREATE TABLE IF NOT EXISTS local_configuration_audit (
            audit_id            BIGSERIAL   PRIMARY KEY,
            config_id           UUID        NOT NULL,
            action              TEXT        NOT NULL CHECK (action IN (
                'CREATED', 'MODIFIED', 'DEACTIVATED', 'APPROVED',
                'DROPPED_DUPLICATE_AT_MIGRATION', 'DROPPED_INFORMATIONAL',
                'DROPPED_UNKNOWN_KEY', 'MIGRATED_FROM_LEGACY'
            )),
            previous_value      JSONB,
            new_value           JSONB,
            actor_id            TEXT        NOT NULL,
            actor_role          TEXT        NOT NULL,
            timestamp           TIMESTAMPTZ NOT NULL DEFAULT NOW()
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
            -- S148 C-4, C-8, C-9: the eighteen flex-settlement and office-hours columns, all nullable
            -- with no DEFAULT, with init.sql's CHECKs.
            flex_settlement_period              TEXT            CHECK (flex_settlement_period IN ('MONTH')),
            flex_cap_test_period                TEXT            CHECK (flex_cap_test_period IN ('MONTH')),
            flex_plus_cap_hours                 NUMERIC(6,2)    CHECK (flex_plus_cap_hours >= 0),
            flex_minus_cap_hours                NUMERIC(6,2)    CHECK (flex_minus_cap_hours >= 0),
            flex_daily_credit_cap_hours         NUMERIC(5,2)    CHECK (flex_daily_credit_cap_hours >= 0),
            flex_above_cap_action               TEXT            CHECK (flex_above_cap_action IN ('FORFEIT')),
            flex_leaving_action                 TEXT            CHECK (flex_leaving_action IN ('FORFEIT_POSITIVE_RECORD_NEGATIVE')),
            office_hours_mon_start              TIME,
            office_hours_mon_end                TIME,
            office_hours_tue_start              TIME,
            office_hours_tue_end                TIME,
            office_hours_wed_start              TIME,
            office_hours_wed_end                TIME,
            office_hours_thu_start              TIME,
            office_hours_thu_end                TIME,
            office_hours_fri_start              TIME,
            office_hours_fri_end                TIME,
            paid_absence_credit_basis           TEXT            CHECK (paid_absence_credit_basis IN ('PARAGRAPH_7_NR_2', 'OFFICE_HOURS')),
            created_by                          TEXT            NOT NULL,
            created_at                          TIMESTAMPTZ     NOT NULL DEFAULT NOW(),
            -- S22 / ADR-018 D7: row-version optimistic-concurrency token. First-insert is 1;
            -- each in-place UPDATE bumps it by one. Mirrors production init.sql DO $$ block.
            version                             BIGINT          NOT NULL DEFAULT 1,
            CONSTRAINT local_agreement_profiles_office_hours_mon_check CHECK ((office_hours_mon_start IS NULL AND office_hours_mon_end IS NULL) OR (office_hours_mon_start IS NOT NULL AND office_hours_mon_end IS NOT NULL AND office_hours_mon_start < office_hours_mon_end)),
            CONSTRAINT local_agreement_profiles_office_hours_tue_check CHECK ((office_hours_tue_start IS NULL AND office_hours_tue_end IS NULL) OR (office_hours_tue_start IS NOT NULL AND office_hours_tue_end IS NOT NULL AND office_hours_tue_start < office_hours_tue_end)),
            CONSTRAINT local_agreement_profiles_office_hours_wed_check CHECK ((office_hours_wed_start IS NULL AND office_hours_wed_end IS NULL) OR (office_hours_wed_start IS NOT NULL AND office_hours_wed_end IS NOT NULL AND office_hours_wed_start < office_hours_wed_end)),
            CONSTRAINT local_agreement_profiles_office_hours_thu_check CHECK ((office_hours_thu_start IS NULL AND office_hours_thu_end IS NULL) OR (office_hours_thu_start IS NOT NULL AND office_hours_thu_end IS NOT NULL AND office_hours_thu_start < office_hours_thu_end)),
            CONSTRAINT local_agreement_profiles_office_hours_fri_check CHECK ((office_hours_fri_start IS NULL AND office_hours_fri_end IS NULL) OR (office_hours_fri_start IS NOT NULL AND office_hours_fri_end IS NOT NULL AND office_hours_fri_start < office_hours_fri_end))
        );
        CREATE UNIQUE INDEX IF NOT EXISTS uq_local_agreement_profile_active
            ON local_agreement_profiles (org_id, agreement_code, ok_version)
            WHERE effective_to IS NULL;

        CREATE TABLE IF NOT EXISTS local_agreement_profile_audit (
            audit_id        BIGSERIAL       PRIMARY KEY,
            profile_id      UUID            NOT NULL,
            -- S22 / ADR-018 D9: 'MODIFIED' added for same-day in-place edits.
            action          TEXT            NOT NULL CHECK (action IN (
                'CREATED', 'MODIFIED', 'SUPERSEDED', 'DEACTIVATED', 'MIGRATED_FROM_LEGACY'
            )),
            delta_jsonb     JSONB           NOT NULL,
            actor_id        TEXT            NOT NULL,
            actor_role      TEXT            NOT NULL,
            timestamp       TIMESTAMPTZ     NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_local_profile_audit_profile
            ON local_agreement_profile_audit(profile_id);
        """;

    /// <summary>
    /// Applies <see cref="Ddl"/> against the supplied connection string. Idempotent —
    /// uses <c>CREATE TABLE IF NOT EXISTS</c> + <c>CREATE INDEX IF NOT EXISTS</c>.
    /// </summary>
    public static async Task ApplyAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Inserts a default test organization that the profile/configuration tables can FK to.
    /// </summary>
    public static async Task SeedOrganizationAsync(
        string connectionString, string orgId = "STY02", CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, materialized_path, agreement_code, ok_version)
            VALUES (@orgId, @orgId || ' Test Org', 'ORGANISATION', '/' || @orgId || '/', 'HK', 'OK24')
            ON CONFLICT (org_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
