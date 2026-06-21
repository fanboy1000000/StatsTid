using Npgsql;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S90 / TASK-9002 — DDL extending the baseline DockerHarness schema with the two tables the
/// atomic payroll-export path writes/reads beyond the outbox + audit tables: the new
/// <c>payroll_export_records</c> (the per-(employee, year, month) lock + manifest + idempotency
/// row) and <c>approval_periods</c> (for the B2 <c>FOR UPDATE</c> APPROVED re-check).
///
/// <para>
/// Mirrors <c>docker/postgres/init.sql</c> for the columns (payroll_export_records ~4140-4159,
/// approval_periods ~783-804). <c>approval_periods.org_id</c> FKs <c>organizations</c>, supplied
/// by <see cref="PhaseE.AuditProjectionTestSchema"/> (applied alongside in the test fixture).
/// Schema drift between this DDL and production must be mirrored here.
/// </para>
/// </summary>
internal static class PayrollExportRecordsTestSchema
{
    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS payroll_export_records (
            export_id                UUID          PRIMARY KEY,
            period_id                UUID          NULL,
            employee_id              TEXT          NOT NULL,
            year                     INT           NOT NULL,
            month                    INT           NOT NULL,
            exported_at              TIMESTAMPTZ   NOT NULL DEFAULT NOW(),
            original_lines           JSONB         NOT NULL,
            current_effective_lines  JSONB         NOT NULL,
            content_hash             TEXT          NOT NULL,
            source                   TEXT          NOT NULL DEFAULT 'CALCULATE_AND_EXPORT',
            CONSTRAINT uq_payroll_export_employee_month UNIQUE (employee_id, year, month)
        );

        CREATE INDEX IF NOT EXISTS idx_payroll_export_records_period
            ON payroll_export_records (period_id);

        CREATE TABLE IF NOT EXISTS approval_periods (
            period_id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            employee_id         TEXT        NOT NULL,
            org_id              TEXT        NOT NULL REFERENCES organizations(org_id),
            period_start        DATE        NOT NULL,
            period_end          DATE        NOT NULL,
            period_type         TEXT        NOT NULL CHECK (period_type IN ('WEEKLY', 'MONTHLY')),
            status              TEXT        NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'SUBMITTED', 'APPROVED', 'REJECTED')),
            submitted_at        TIMESTAMPTZ,
            submitted_by        TEXT,
            approved_by         TEXT,
            approved_at         TIMESTAMPTZ,
            rejection_reason    TEXT,
            agreement_code      TEXT        NOT NULL,
            ok_version          TEXT        NOT NULL,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (employee_id, period_start, period_end)
        );

        -- The post-commit best-effort delivery envelope (UNCHANGED delivery path; the lock does
        -- NOT depend on it). Mirrors init.sql:74-85.
        CREATE TABLE IF NOT EXISTS outbox_messages (
            message_id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            destination         TEXT        NOT NULL,
            payload             JSONB       NOT NULL,
            status              TEXT        NOT NULL DEFAULT 'pending',
            attempt_count       INT         NOT NULL DEFAULT 0,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            last_attempt_at     TIMESTAMPTZ,
            delivered_at        TIMESTAMPTZ,
            error_message       TEXT,
            idempotency_token   UUID        UNIQUE
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
