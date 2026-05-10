using System.Data;
using Npgsql;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S24 TASK-2408 forced-rollback harness for the per-endpoint atomic-outbox proof
/// (ADR-018 D3). Provides three reusable pieces every <c>*AtomicTests</c> class
/// composes against:
///
/// <list type="bullet">
///   <item><description><see cref="ThrowingOutboxEnqueue"/> — drop-in replacement for the
///   production <see cref="IOutboxEnqueue"/> that throws on every <see cref="EnqueueAsync"/>
///   call so the surrounding state-change transaction must roll back if the endpoint
///   honors the atomic-outbox contract.</description></item>
///   <item><description>Schema DDL extension (<see cref="ForcedRollbackSchema"/>) that
///   layers onto <see cref="Segmentation.TestFixtures.DockerHarness.SchemaDdl"/> the
///   tables every Phase-2 endpoint touches: state tables (<c>approval_periods</c>,
///   <c>agreement_configs</c>, <c>position_override_configs</c>, <c>wage_type_mappings</c>,
///   <c>overtime_balances</c>, <c>overtime_pre_approvals</c>, <c>timer_sessions</c>) and
///   audit tables (<c>approval_audit</c>, <c>agreement_config_audit</c>,
///   <c>position_override_config_audit</c>, <c>wage_type_mapping_audit</c>) plus the
///   <c>organizations</c> + <c>positions</c> FK parents. Mirrors
///   <see cref="Infrastructure.TxContractTests"/>' Phase-1 schema byte-for-byte (the same
///   <c>init.sql</c> rows); schema drift between this DDL and production must be mirrored
///   here.</description></item>
///   <item><description>Four DB assertion helpers (<see cref="AssertNoStateMutationAsync"/>,
///   <see cref="AssertNoAuditRowAsync"/>, <see cref="AssertNoEventRowAsync"/>,
///   <see cref="AssertNoOutboxRowAsync"/>) that count rows matching a filter and assert
///   the count is zero. Used by every test post-rollback to prove no side-effect leaked
///   outside the rolled-back transaction.</description></item>
/// </list>
///
/// <para>
/// Test shape: each <c>*AtomicTests</c> test arranges fixture state, then mirrors the
/// converted endpoint's orchestration verbatim — open a connection, begin a tx, call the
/// repository's <c>(conn, tx)</c> overload, append the audit row (Pattern B only), call
/// <see cref="ThrowingOutboxEnqueue.EnqueueAsync"/>, and expect <see cref="InvalidOperationException"/>.
/// The unhandled throw before <c>tx.CommitAsync</c> means PostgreSQL rolls back the
/// transaction; the four post-action assertions then pin that no state row, no audit row,
/// no canonical event row, and no outbox row was visible to a fresh connection. This is
/// the same invariant the real endpoint upholds when its outbox-enqueue path fails — the
/// HTTP layer surfaces a 500 to the caller, but the ADR-018 D3 contract under test is
/// "everything-or-nothing transactional rollback," which is what these tests pin.
/// </para>
///
/// <para>
/// No <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// harness in the regression project — established convention since
/// <see cref="Config.ProfileAuditTests"/>: tests mirror the endpoint's orchestration
/// directly rather than booting the full <c>Backend.Api</c> stack against a Testcontainer
/// (which would require JWT seed + DB seeders + 29-table schema bring-up). The contract
/// under test (atomic rollback when outbox throws) is identical at the orchestration
/// surface and at the wire surface — proving it at the orchestration surface is the
/// minimum sufficient harness, consistent with <see cref="Infrastructure.TxContractTests"/>.
/// </para>
/// </summary>
internal static class ForcedRollbackHarness
{
    /// <summary>
    /// Forced-failure <see cref="IOutboxEnqueue"/> decorator. Every call to
    /// <see cref="EnqueueAsync"/> throws <see cref="InvalidOperationException"/> with a
    /// stable message so tests can assert against it. Used to drive the converted
    /// endpoint's atomic-outbox path through its rollback branch — the production path
    /// is "<c>repo.SomeAsync(conn, tx, ...)</c> + <c>repo.AppendAuditAsync(conn, tx, ...)</c>
    /// + <c>outbox.EnqueueAsync(conn, tx, ...)</c> + <c>tx.CommitAsync()</c>", and a throw
    /// at the third step before the commit forces PostgreSQL to roll back the
    /// transaction on dispose.
    /// </summary>
    public sealed class ThrowingOutboxEnqueue : IOutboxEnqueue
    {
        public const string ThrowMessage = "Forced rollback for testing";

        public Task EnqueueAsync(
            NpgsqlConnection conn,
            NpgsqlTransaction tx,
            string streamId,
            IDomainEvent @event,
            CancellationToken ct = default)
        {
            throw new InvalidOperationException(ThrowMessage);
        }

        // S27 TASK-2703: same forced-rollback semantics on the new overload.
        // Throws the same InvalidOperationException(ThrowMessage) so any S27
        // atomic-projection test that wires this double through an
        // EnqueueAndReturnIdAsync call site sees identical rollback behavior
        // to the existing EnqueueAsync sites.
        public Task<long> EnqueueAndReturnIdAsync(
            NpgsqlConnection conn,
            NpgsqlTransaction tx,
            string streamId,
            IDomainEvent @event,
            CancellationToken ct = default)
        {
            throw new InvalidOperationException(ThrowMessage);
        }
    }

    /// <summary>
    /// SQL DDL covering every table the Phase-2 forced-rollback tests touch. Idempotent
    /// (uses <c>CREATE TABLE IF NOT EXISTS</c>) so it's safe to re-apply on the same
    /// container across test classes. Mirrors <c>docker/postgres/init.sql</c> for the
    /// covered rows; schema drift in those tables must be mirrored here.
    /// </summary>
    public const string ForcedRollbackSchema = """
        CREATE TABLE IF NOT EXISTS organizations (
            org_id              TEXT        PRIMARY KEY,
            org_name            TEXT        NOT NULL,
            org_type            TEXT        NOT NULL CHECK (org_type IN ('MINISTRY', 'STYRELSE', 'AFDELING', 'TEAM')),
            parent_org_id       TEXT        REFERENCES organizations(org_id),
            materialized_path   TEXT        NOT NULL,
            agreement_code      TEXT        NOT NULL DEFAULT 'AC',
            ok_version          TEXT        NOT NULL DEFAULT 'OK24',
            is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS positions (
            position_code   TEXT        PRIMARY KEY,
            display_label   TEXT        NOT NULL,
            agreement_code  TEXT        NOT NULL,
            is_active       BOOLEAN     NOT NULL DEFAULT true,
            created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS approval_periods (
            period_id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            employee_id         TEXT        NOT NULL,
            org_id              TEXT        NOT NULL REFERENCES organizations(org_id),
            period_start        DATE        NOT NULL,
            period_end          DATE        NOT NULL,
            period_type         TEXT        NOT NULL CHECK (period_type IN ('WEEKLY', 'MONTHLY')),
            status              TEXT        NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'EMPLOYEE_APPROVED', 'SUBMITTED', 'APPROVED', 'REJECTED')),
            submitted_at        TIMESTAMPTZ,
            submitted_by        TEXT,
            approved_by         TEXT,
            approved_at         TIMESTAMPTZ,
            rejection_reason    TEXT,
            employee_approved_at TIMESTAMPTZ,
            employee_approved_by TEXT,
            employee_deadline   DATE,
            manager_deadline    DATE,
            agreement_code      TEXT        NOT NULL,
            ok_version          TEXT        NOT NULL,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (employee_id, period_start, period_end)
        );

        CREATE TABLE IF NOT EXISTS approval_audit (
            audit_id            BIGSERIAL   PRIMARY KEY,
            period_id           UUID        NOT NULL,
            action              TEXT        NOT NULL CHECK (action IN ('CREATED', 'SUBMITTED', 'APPROVED', 'REJECTED', 'REOPENED')),
            actor_id            TEXT        NOT NULL,
            actor_role          TEXT        NOT NULL,
            comment             TEXT,
            timestamp           TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS agreement_configs (
            config_id               UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            agreement_code          TEXT        NOT NULL,
            ok_version              TEXT        NOT NULL,
            status                  TEXT        NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'ACTIVE', 'ARCHIVED')),
            weekly_norm_hours       DECIMAL     NOT NULL,
            norm_period_weeks       INT         NOT NULL DEFAULT 1,
            norm_model              TEXT        NOT NULL DEFAULT 'WEEKLY_HOURS',
            annual_norm_hours       DECIMAL     NOT NULL DEFAULT 1924,
            max_flex_balance        DECIMAL     NOT NULL,
            flex_carryover_max      DECIMAL     NOT NULL,
            has_overtime            BOOLEAN     NOT NULL,
            has_merarbejde          BOOLEAN     NOT NULL,
            overtime_threshold_50   DECIMAL     NOT NULL DEFAULT 37.0,
            overtime_threshold_100  DECIMAL     NOT NULL DEFAULT 40.0,
            evening_supplement_enabled  BOOLEAN NOT NULL DEFAULT FALSE,
            night_supplement_enabled    BOOLEAN NOT NULL DEFAULT FALSE,
            weekend_supplement_enabled  BOOLEAN NOT NULL DEFAULT FALSE,
            holiday_supplement_enabled  BOOLEAN NOT NULL DEFAULT FALSE,
            evening_start           INT         NOT NULL DEFAULT 17,
            evening_end             INT         NOT NULL DEFAULT 23,
            night_start             INT         NOT NULL DEFAULT 23,
            night_end               INT         NOT NULL DEFAULT 6,
            evening_rate            DECIMAL     NOT NULL DEFAULT 1.25,
            night_rate              DECIMAL     NOT NULL DEFAULT 1.50,
            weekend_saturday_rate   DECIMAL     NOT NULL DEFAULT 1.50,
            weekend_sunday_rate     DECIMAL     NOT NULL DEFAULT 2.0,
            holiday_rate            DECIMAL     NOT NULL DEFAULT 2.0,
            on_call_duty_enabled    BOOLEAN     NOT NULL DEFAULT FALSE,
            on_call_duty_rate       DECIMAL     NOT NULL DEFAULT 0.33,
            call_in_work_enabled    BOOLEAN     NOT NULL DEFAULT FALSE,
            call_in_minimum_hours   DECIMAL     NOT NULL DEFAULT 3.0,
            call_in_rate            DECIMAL     NOT NULL DEFAULT 1.0,
            travel_time_enabled     BOOLEAN     NOT NULL DEFAULT FALSE,
            working_travel_rate     DECIMAL     NOT NULL DEFAULT 1.0,
            non_working_travel_rate DECIMAL     NOT NULL DEFAULT 0.5,
            max_daily_hours         DECIMAL     NOT NULL DEFAULT 13.0,
            minimum_rest_hours      DECIMAL     NOT NULL DEFAULT 11.0,
            rest_period_derogation_allowed BOOLEAN NOT NULL DEFAULT FALSE,
            weekly_max_hours_reference_period INT NOT NULL DEFAULT 17,
            voluntary_unsocial_hours_allowed BOOLEAN NOT NULL DEFAULT TRUE,
            default_compensation_model TEXT NOT NULL DEFAULT 'UDBETALING',
            employee_compensation_choice BOOLEAN NOT NULL DEFAULT FALSE,
            max_overtime_hours_per_period DECIMAL NOT NULL DEFAULT 0,
            overtime_requires_pre_approval BOOLEAN NOT NULL DEFAULT FALSE,
            created_by              TEXT        NOT NULL DEFAULT 'SYSTEM_SEED',
            created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            published_at            TIMESTAMPTZ,
            archived_at             TIMESTAMPTZ,
            cloned_from_id          UUID        REFERENCES agreement_configs(config_id),
            description             TEXT,
            version                 BIGINT      NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS agreement_config_audit (
            audit_id        BIGSERIAL   PRIMARY KEY,
            config_id       UUID        NOT NULL,
            action          TEXT        NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'PUBLISHED', 'ARCHIVED', 'CLONED')),
            previous_data   JSONB,
            new_data        JSONB,
            actor_id        TEXT        NOT NULL,
            actor_role      TEXT        NOT NULL,
            timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            version_before  BIGINT,
            version_after   BIGINT
        );

        CREATE TABLE IF NOT EXISTS position_override_configs (
            override_id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            agreement_code      TEXT        NOT NULL,
            ok_version          TEXT        NOT NULL,
            position_code       TEXT        NOT NULL REFERENCES positions(position_code),
            status              TEXT        NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE', 'INACTIVE')),
            max_flex_balance    DECIMAL,
            flex_carryover_max  DECIMAL,
            norm_period_weeks   INT,
            weekly_norm_hours   DECIMAL,
            created_by          TEXT        NOT NULL DEFAULT 'SYSTEM_SEED',
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            description         TEXT,
            version             BIGINT      NOT NULL DEFAULT 1
        );

        CREATE UNIQUE INDEX IF NOT EXISTS idx_position_override_active_unique
            ON position_override_configs (agreement_code, ok_version, position_code)
            WHERE status = 'ACTIVE';

        CREATE TABLE IF NOT EXISTS position_override_config_audit (
            audit_id        BIGSERIAL   PRIMARY KEY,
            override_id     UUID        NOT NULL,
            action          TEXT        NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'ACTIVATED', 'DEACTIVATED')),
            previous_data   JSONB,
            new_data        JSONB,
            actor_id        TEXT        NOT NULL,
            actor_role      TEXT        NOT NULL,
            timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            version_before  BIGINT,
            version_after   BIGINT
        );

        CREATE TABLE IF NOT EXISTS wage_type_mapping_audit (
            audit_id        BIGSERIAL   PRIMARY KEY,
            time_type       TEXT        NOT NULL,
            ok_version      TEXT        NOT NULL,
            agreement_code  TEXT        NOT NULL,
            position        TEXT        NOT NULL DEFAULT '',
            action          TEXT        NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'DELETED', 'SUPERSEDED')),
            previous_data   JSONB,
            new_data        JSONB,
            actor_id        TEXT        NOT NULL,
            actor_role      TEXT        NOT NULL,
            timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            version_before  BIGINT,
            version_after   BIGINT
        );

        CREATE TABLE IF NOT EXISTS overtime_balances (
            balance_id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            employee_id         TEXT        NOT NULL,
            agreement_code      TEXT        NOT NULL,
            period_year         INT         NOT NULL,
            accumulated         DECIMAL     NOT NULL DEFAULT 0,
            paid_out            DECIMAL     NOT NULL DEFAULT 0,
            afspadsering_used   DECIMAL     NOT NULL DEFAULT 0,
            compensation_model  TEXT        NOT NULL DEFAULT 'UDBETALING',
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE (employee_id, period_year)
        );

        CREATE TABLE IF NOT EXISTS overtime_pre_approvals (
            id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            employee_id         TEXT        NOT NULL,
            period_start        DATE        NOT NULL,
            period_end          DATE        NOT NULL,
            max_hours           DECIMAL     NOT NULL,
            approved_by         TEXT,
            approved_at         TIMESTAMPTZ,
            status              TEXT        NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING', 'APPROVED', 'REJECTED')),
            reason              TEXT,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS timer_sessions (
            session_id      UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            employee_id     TEXT        NOT NULL,
            date            DATE        NOT NULL,
            check_in_at     TIMESTAMPTZ NOT NULL,
            check_out_at    TIMESTAMPTZ,
            is_active       BOOLEAN     NOT NULL DEFAULT TRUE,
            created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS wage_type_mappings (
            mapping_id      UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
            time_type       TEXT        NOT NULL,
            wage_type       TEXT        NOT NULL,
            ok_version      TEXT        NOT NULL,
            agreement_code  TEXT        NOT NULL,
            position        TEXT        NOT NULL DEFAULT '',
            description     TEXT,
            created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            version         BIGINT      NOT NULL DEFAULT 1,
            effective_from  DATE        NOT NULL DEFAULT '2020-01-01',
            effective_to    DATE
        );

        CREATE UNIQUE INDEX IF NOT EXISTS idx_wtm_natural_key_open
            ON wage_type_mappings (time_type, ok_version, agreement_code, position)
            WHERE effective_to IS NULL;

        CREATE UNIQUE INDEX IF NOT EXISTS idx_wtm_natural_key_history
            ON wage_type_mappings (time_type, ok_version, agreement_code, position, effective_from);
        """;

    /// <summary>
    /// Applies <see cref="ForcedRollbackSchema"/> against the supplied connection string
    /// and seeds the FK parents the Phase-2 endpoints reference (one organization, one
    /// position). Idempotent — safe to call multiple times across test classes on the
    /// same container.
    /// </summary>
    public static async Task ApplySchemaAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using (var cmd = new NpgsqlCommand(ForcedRollbackSchema, conn))
            await cmd.ExecuteNonQueryAsync(ct);

        // Seed FK parents reused across all Pattern-B tests.
        await using (var seedOrg = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, materialized_path, agreement_code, ok_version)
            VALUES ('STY02', 'STY02 Test Org', 'STYRELSE', '/STY02/', 'HK', 'OK24')
            ON CONFLICT (org_id) DO NOTHING
            """, conn))
            await seedOrg.ExecuteNonQueryAsync(ct);
        await using (var seedPos = new NpgsqlCommand(
            """
            INSERT INTO positions (position_code, display_label, agreement_code)
            VALUES ('DEPARTMENT_HEAD', 'Kontorchef', 'AC')
            ON CONFLICT (position_code) DO NOTHING
            """, conn))
            await seedPos.ExecuteNonQueryAsync(ct);
    }

    // ─── DB assertion helpers ────────────────────────────────────────────────

    /// <summary>
    /// Asserts no row in <paramref name="tableName"/> matches <paramref name="filterSql"/>
    /// (a parameter-free <c>WHERE</c>-clause body) — i.e. the converted endpoint's state
    /// table mutation must NOT have leaked outside the rolled-back tx. Reads on a fresh
    /// connection so MVCC isolation guarantees we see only committed data.
    /// </summary>
    public static async Task AssertNoStateMutationAsync(
        string connectionString, string tableName, string filterSql, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {tableName} WHERE {filterSql}", conn);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        Assert.Equal(0L, count);
    }

    /// <summary>
    /// Asserts no row in <paramref name="auditTableName"/> matches <paramref name="filterSql"/>
    /// — i.e. the audit row the converted endpoint emits in-tx (Pattern B endpoints) must
    /// NOT have leaked outside the rolled-back tx. Pattern C tests (Overtime, Timer) do not
    /// emit audit rows and skip this assertion.
    /// </summary>
    public static async Task AssertNoAuditRowAsync(
        string connectionString, string auditTableName, string filterSql, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {auditTableName} WHERE {filterSql}", conn);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        Assert.Equal(0L, count);
    }

    /// <summary>
    /// Asserts no row in the canonical <c>events</c> table has a matching
    /// <paramref name="streamId"/>. The forced-rollback throw happens BEFORE the outbox
    /// publisher would ever drain the row to canonical events, so this assertion is
    /// trivially true for the rollback path; it pins the contract that the publisher
    /// never published a non-existent outbox row.
    /// </summary>
    public static async Task AssertNoEventRowAsync(
        string connectionString, string streamId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM events WHERE stream_id = @streamId", conn);
        cmd.Parameters.AddWithValue("streamId", streamId);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        Assert.Equal(0L, count);
    }

    /// <summary>
    /// Asserts no row in <c>outbox_events</c> has a matching <paramref name="streamId"/>
    /// — the load-bearing assertion of the rolled-back atomic-outbox contract. If the
    /// throw ran AFTER <c>tx.CommitAsync</c> (or if <c>EnqueueAsync</c> opened its own
    /// auto-commit tx), the outbox row would survive on a fresh connection and this
    /// assertion would fail.
    /// </summary>
    public static async Task AssertNoOutboxRowAsync(
        string connectionString, string streamId, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE stream_id = @streamId", conn);
        cmd.Parameters.AddWithValue("streamId", streamId);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        Assert.Equal(0L, count);
    }
}
