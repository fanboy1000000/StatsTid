using Npgsql;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// SQL DDL extension for the S27 / TASK-2702 projection tables
/// (<c>time_entries_projection</c> + <c>absences_projection</c>) plus the S56 /
/// TASK-5603 <c>work_time_projection</c> table. Layered on top of
/// <see cref="Segmentation.TestFixtures.DockerHarness.SchemaDdl"/> +
/// <see cref="OutboxTestSchema"/> for the S27 / TASK-2710 atomic-projection,
/// publisher-stall RYW, parity, and backfill tests.
///
/// <para>
/// <b>Plain language.</b> This is a hand-kept copy of two tables from the production schema
/// (<c>docker/postgres/init.sql</c>), used so the projection tests can run without applying the
/// whole schema. A copy that falls behind breaks every test that writes a column the copy lacks,
/// and it does so in setup, far from the cause (S144's close went red on exactly that shape).
/// </para>
///
/// <para>
/// <b>What is true about the copy (S145 / TASK-14501 corrected an earlier "byte-for-byte" claim).</b>
/// The copy is <c>init.sql</c>-<i>compatible</i>, not a byte copy: it keeps the same columns (name,
/// type, nullability, default), the same indexes and the same CHECK constraint for the two hours
/// tables, but it leaves out the production comments and writes the column list in its own order.
/// <c>work_time_projection</c> and <c>entitlement_balances</c> below are NOT checked against
/// <c>init.sql</c> by any test. For the two hours tables the equality is enforced by
/// <c>Migrations.HarnessDdlMatchesInitSqlTests</c>, which compares this copy with a database built
/// from <c>init.sql</c> alone, so drift fails CI with a named column instead of an unrelated setup
/// failure. Schema drift between this DDL and production must still be mirrored here — the same
/// convention as <see cref="OutboxTestSchema"/>, <see cref="ForcedRollbackHarness"/>, and
/// <see cref="Infrastructure.TxContractTests"/> (which inlines a sibling copy on its own schema DDL
/// per the established no-shared-schema convention to keep each test class' DDL self-contained).
/// </para>
/// </summary>
internal static class ProjectionSchemaTestFixture
{
    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS time_entries_projection (
            event_id                    UUID            PRIMARY KEY,
            employee_id                 TEXT            NOT NULL,
            date                        DATE            NOT NULL,
            hours                       NUMERIC(8,4)    NOT NULL,
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
            outbox_id                   BIGINT          NOT NULL,
            -- S145 / TASK-14500 (A0): who wrote the row. 'SKEMA' = a Skema save (the cell holds ONE
            -- current value; a re-save supersedes the old row), 'TIME_POST' = POST /api/time-entries
            -- (additive: every registration counts). A pre-S145 row is TIME_POST, hence the default.
            source                      TEXT            NOT NULL DEFAULT 'TIME_POST',
            -- The outbox_id of the row that replaced this one; NULL = this row is current.
            superseded_by_outbox_id     BIGINT          NULL,
            CONSTRAINT time_entries_projection_source_check CHECK (source IN ('SKEMA','TIME_POST'))
        );
        CREATE INDEX IF NOT EXISTS idx_time_entries_proj_emp_date_outbox
            ON time_entries_projection(employee_id, date, outbox_id);
        CREATE INDEX IF NOT EXISTS idx_time_entries_proj_emp_outbox
            ON time_entries_projection(employee_id, outbox_id);
        -- One CURRENT Skema row per (employee, date, task) cell; TIME_POST rows are never in it.
        CREATE UNIQUE INDEX IF NOT EXISTS ux_time_entries_proj_current_skema_cell
            ON time_entries_projection (employee_id, date, task_id)
            WHERE source = 'SKEMA' AND superseded_by_outbox_id IS NULL;

        CREATE TABLE IF NOT EXISTS absences_projection (
            event_id                    UUID            PRIMARY KEY,
            employee_id                 TEXT            NOT NULL,
            date                        DATE            NOT NULL,
            absence_type                TEXT            NOT NULL,
            hours                       NUMERIC(8,4)    NOT NULL,
            -- S66 / TASK-6602 / ADR-032 D2 — the authoritative per-absence consumption
            -- record (feriedage). NULLABLE: pre-S66 rows carry NULL until backfilled to
            -- hours/7.4. Mirrors docker/postgres/init.sql:1543. This shared test-fixture
            -- DDL is a cross-process contract (S64 lesson) — AbsenceProjectionRepository
            -- and ProjectionBackfillService both read/write this column, so the column
            -- MUST be present here or every AbsenceRegistered projection path 42703-errors.
            feriedage                   NUMERIC(8,4)    NULL,
            agreement_code              TEXT            NOT NULL,
            ok_version                  TEXT            NOT NULL,
            occurred_at                 TIMESTAMPTZ     NOT NULL,
            actor_id                    TEXT,
            actor_role                  TEXT,
            correlation_id              UUID,
            outbox_id                   BIGINT          NOT NULL,
            -- S145 / TASK-14500 (A0): same two columns as time_entries_projection above.
            source                      TEXT            NOT NULL DEFAULT 'TIME_POST',
            superseded_by_outbox_id     BIGINT          NULL,
            CONSTRAINT absences_projection_source_check CHECK (source IN ('SKEMA','TIME_POST'))
        );
        CREATE INDEX IF NOT EXISTS idx_absences_proj_emp_date_outbox
            ON absences_projection(employee_id, date, outbox_id);
        CREATE INDEX IF NOT EXISTS idx_absences_proj_emp_outbox
            ON absences_projection(employee_id, outbox_id);
        -- One CURRENT Skema row per (employee, date, absence type) cell.
        CREATE UNIQUE INDEX IF NOT EXISTS ux_absences_proj_current_skema_cell
            ON absences_projection (employee_id, date, absence_type)
            WHERE source = 'SKEMA' AND superseded_by_outbox_id IS NULL;

        -- S56 TASK-5603 work_time_projection: LATEST-WINS read-model keyed
        -- (employee_id, date). init.sql-compatible (same columns; the init.sql
        -- table is the one at "work_time_projection"; NOT covered by the drift
        -- guard). Backs the Skema workTime round-trip, approval-survival,
        -- backfill-replay, and allocation-gate tests.
        CREATE TABLE IF NOT EXISTS work_time_projection (
            employee_id     TEXT            NOT NULL,
            date            DATE            NOT NULL,
            intervals       JSONB           NOT NULL DEFAULT '[]'::jsonb,
            manual_hours    NUMERIC(8,4)    NOT NULL DEFAULT 0,
            occurred_at     TIMESTAMPTZ     NOT NULL,
            actor_id        TEXT,
            actor_role      TEXT,
            correlation_id  UUID,
            outbox_id       BIGINT          NOT NULL,
            PRIMARY KEY (employee_id, date)
        );

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
