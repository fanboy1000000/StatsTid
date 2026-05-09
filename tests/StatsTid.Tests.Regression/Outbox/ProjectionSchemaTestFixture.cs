using Npgsql;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// SQL DDL extension for the S27 / TASK-2702 projection tables
/// (<c>time_entries_projection</c> + <c>absences_projection</c>). Layered on top of
/// <see cref="Segmentation.TestFixtures.DockerHarness.SchemaDdl"/> +
/// <see cref="OutboxTestSchema"/> for the S27 / TASK-2710 atomic-projection,
/// publisher-stall RYW, parity, and backfill tests.
///
/// <para>
/// Mirrors <c>docker/postgres/init.sql</c> lines 1181-1221 byte-for-byte for the
/// covered columns + indexes. Schema drift between this DDL and production must be
/// mirrored here — the same convention as <see cref="OutboxTestSchema"/>,
/// <see cref="ForcedRollbackHarness"/>, and
/// <see cref="Infrastructure.TxContractTests"/> (which inlines a sibling copy on its
/// own schema DDL per the established no-shared-schema convention to keep each test
/// class' DDL self-contained).
/// </para>
/// </summary>
internal static class ProjectionSchemaTestFixture
{
    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS time_entries_projection (
            event_id                    UUID            PRIMARY KEY,
            employee_id                 TEXT            NOT NULL,
            date                        DATE            NOT NULL,
            hours                       NUMERIC(6,2)    NOT NULL,
            start_time                  TIME,
            end_time                    TIME,
            task_id                     TEXT,
            activity_type               TEXT,
            agreement_code              TEXT            NOT NULL,
            ok_version                  TEXT            NOT NULL,
            voluntary_unsocial_hours    BOOLEAN         NOT NULL DEFAULT false,
            occurred_at                 TIMESTAMPTZ     NOT NULL,
            actor_id                    TEXT,
            actor_role                  TEXT,
            correlation_id              UUID,
            outbox_id                   BIGINT          NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_time_entries_proj_emp_date_outbox
            ON time_entries_projection(employee_id, date, outbox_id);
        CREATE INDEX IF NOT EXISTS idx_time_entries_proj_emp_outbox
            ON time_entries_projection(employee_id, outbox_id);

        CREATE TABLE IF NOT EXISTS absences_projection (
            event_id                    UUID            PRIMARY KEY,
            employee_id                 TEXT            NOT NULL,
            date                        DATE            NOT NULL,
            absence_type                TEXT            NOT NULL,
            hours                       NUMERIC(6,2)    NOT NULL,
            agreement_code              TEXT            NOT NULL,
            ok_version                  TEXT            NOT NULL,
            occurred_at                 TIMESTAMPTZ     NOT NULL,
            actor_id                    TEXT,
            actor_role                  TEXT,
            correlation_id              UUID,
            outbox_id                   BIGINT          NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_absences_proj_emp_date_outbox
            ON absences_projection(employee_id, date, outbox_id);
        CREATE INDEX IF NOT EXISTS idx_absences_proj_emp_outbox
            ON absences_projection(employee_id, outbox_id);

        -- entitlement_balances reused by Skema bundle-rollback + multi-absence quota
        -- breach tests (Slot 2 + Slot 3). Mirrors init.sql:1108-1119. Idempotent.
        CREATE TABLE IF NOT EXISTS entitlement_balances (
            balance_id              UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            employee_id             TEXT        NOT NULL,
            entitlement_type        TEXT        NOT NULL,
            entitlement_year        INT         NOT NULL,
            total_quota             DECIMAL     NOT NULL,
            used                    DECIMAL     NOT NULL DEFAULT 0,
            planned                 DECIMAL     NOT NULL DEFAULT 0,
            carryover_in            DECIMAL     NOT NULL DEFAULT 0,
            updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (employee_id, entitlement_type, entitlement_year)
        );
        """;

    public static async Task ApplyAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
