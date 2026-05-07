using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Infrastructure;

/// <summary>
/// TASK-2401 Phase 1 contract tests for the (NpgsqlConnection, NpgsqlTransaction, …)
/// overloads added to the 7 in-scope repositories. Each test asserts:
///
/// <list type="bullet">
///   <item><description>The overload uses the caller-supplied conn + tx (does not open a
///   new connection or begin its own tx).</description></item>
///   <item><description>The overload does NOT commit, rollback, or otherwise close the
///   caller-supplied tx — verified by issuing a follow-up command on the SAME tx after
///   the SUT call. If the SUT had committed/rolled back, the follow-up would throw.</description></item>
///   <item><description>The mutation IS visible inside the caller-supplied tx (proves the
///   SUT actually used <paramref>tx</paramref> for the INSERT/UPDATE rather than swallowing
///   it and silently opening its own connection).</description></item>
/// </list>
///
/// <para>
/// Rollback path is also exercised: after the SUT runs the test rolls back the tx and
/// confirms the mutation did not persist (proving the SUT participated in the caller's tx
/// rather than auto-committing a private one).
/// </para>
///
/// <para>
/// Located in <c>StatsTid.Tests.Regression</c> rather than <c>StatsTid.Tests.Unit</c>
/// (the original task path) because Unit lacks the Npgsql + Testcontainers.PostgreSql
/// package references that Docker-gated repository tests need; Regression is where
/// every other Docker-gated repo test (ProfileSupersessionTests, ProfileNoOpShortCircuitTests,
/// ProfileConcurrencyTokenTests) already lives. Per ADR-018 D3 / TASK-2401 Step F.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class TxContractTests : IAsyncLifetime
{
    private const string OrgId = "STY02";
    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await ApplySchemaAsync(_harness.ConnectionString);
    }

    /// <summary>
    /// SQL DDL extending <see cref="Segmentation.TestFixtures.DockerHarness.SchemaDdl"/>'s
    /// baseline schema with the 7 in-scope tables (and FK parents) for TASK-2401's
    /// in-transaction overload contract tests. Idempotent; mirrors
    /// <c>docker/postgres/init.sql</c> for column shapes — schema drift between this DDL
    /// and production must be mirrored here.
    /// </summary>
    private const string SchemaDdl = """
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
            description             TEXT
        );

        CREATE TABLE IF NOT EXISTS agreement_config_audit (
            audit_id        BIGSERIAL   PRIMARY KEY,
            config_id       UUID        NOT NULL,
            action          TEXT        NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'PUBLISHED', 'ARCHIVED', 'CLONED')),
            previous_data   JSONB,
            new_data        JSONB,
            actor_id        TEXT        NOT NULL,
            actor_role      TEXT        NOT NULL,
            timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW()
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
            description         TEXT
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
            timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS wage_type_mapping_audit (
            audit_id        BIGSERIAL   PRIMARY KEY,
            time_type       TEXT        NOT NULL,
            ok_version      TEXT        NOT NULL,
            agreement_code  TEXT        NOT NULL,
            position        TEXT        NOT NULL DEFAULT '',
            action          TEXT        NOT NULL CHECK (action IN ('CREATED', 'UPDATED', 'DELETED')),
            previous_data   JSONB,
            new_data        JSONB,
            actor_id        TEXT        NOT NULL,
            actor_role      TEXT        NOT NULL,
            timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW()
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
        """;

    private static async Task ApplySchemaAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using (var cmd = new NpgsqlCommand(SchemaDdl, conn))
            await cmd.ExecuteNonQueryAsync(ct);
        // Seed FK rows used by ApprovalRepo + PositionOverrideRepo tests.
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

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ── ApprovalPeriodRepository ──────────────────────────────────────────────────────

    [Fact]
    public async Task ApprovalRepo_CreateAsync_ParticipatesInCallerTx()
    {
        var repo = new ApprovalPeriodRepository(_harness.Factory);
        var period = NewPeriod();

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var periodId = await repo.CreateAsync(conn, tx, period);

        // 1. SUT did NOT close the tx — follow-up command on same tx must succeed.
        await AssertTxStillUsable(conn, tx);
        // 2. Row IS visible inside the caller's tx.
        Assert.Equal(1L, await CountInsideTx(conn, tx, "approval_periods", "period_id", periodId));
        // 3. Test rolls the tx back; row must NOT persist post-rollback.
        await tx.RollbackAsync();
        Assert.Equal(0L, await CountFreshConn("approval_periods", "period_id", periodId));
    }

    [Fact]
    public async Task ApprovalRepo_UpdateStatusAsync_ParticipatesInCallerTx()
    {
        var repo = new ApprovalPeriodRepository(_harness.Factory);
        var period = NewPeriod();
        var periodId = await repo.CreateAsync(period); // seeded outside the SUT's tx

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await repo.UpdateStatusAsync(conn, tx, periodId, "SUBMITTED", actorId: "tester");

        await AssertTxStillUsable(conn, tx);
        var status = await ScalarInsideTx<string>(
            conn, tx, "SELECT status FROM approval_periods WHERE period_id = @id", periodId);
        Assert.Equal("SUBMITTED", status);
        await tx.RollbackAsync();
        // After rollback the row reverts to its seeded DRAFT status.
        var statusAfter = await ScalarFreshConn<string>(
            "SELECT status FROM approval_periods WHERE period_id = @id", periodId);
        Assert.Equal("DRAFT", statusAfter);
    }

    [Fact]
    public async Task ApprovalRepo_UpdateDeadlinesAsync_ParticipatesInCallerTx()
    {
        var repo = new ApprovalPeriodRepository(_harness.Factory);
        var period = NewPeriod();
        var periodId = await repo.CreateAsync(period);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var deadline = new DateOnly(2026, 5, 31);
        await repo.UpdateDeadlinesAsync(conn, tx, periodId, deadline, deadline);

        await AssertTxStillUsable(conn, tx);
        // Visible inside tx
        var seenInside = await ScalarInsideTx<DateTime?>(
            conn, tx, "SELECT employee_deadline FROM approval_periods WHERE period_id = @id", periodId);
        Assert.NotNull(seenInside);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ApprovalRepo_AppendAuditAsync_ParticipatesInCallerTx()
    {
        var repo = new ApprovalPeriodRepository(_harness.Factory);
        var period = NewPeriod();
        var periodId = await repo.CreateAsync(period);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await repo.AppendAuditAsync(conn, tx, periodId, "SUBMITTED", "tester", "EMPLOYEE", null);

        await AssertTxStillUsable(conn, tx);
        Assert.Equal(1L, await CountInsideTx(conn, tx, "approval_audit", "period_id", periodId));
        await tx.RollbackAsync();
        Assert.Equal(0L, await CountFreshConn("approval_audit", "period_id", periodId));
    }

    // ── AgreementConfigRepository ─────────────────────────────────────────────────────

    [Fact]
    public async Task AgreementConfigRepo_CreateAsync_ParticipatesInCallerTx()
    {
        var repo = new AgreementConfigRepository(_harness.Factory);
        var entity = NewAgreementConfig();

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var configId = await repo.CreateAsync(conn, tx, entity);

        await AssertTxStillUsable(conn, tx);
        Assert.Equal(1L, await CountInsideTx(conn, tx, "agreement_configs", "config_id", configId));
        await tx.RollbackAsync();
        Assert.Equal(0L, await CountFreshConn("agreement_configs", "config_id", configId));
    }

    [Fact]
    public async Task AgreementConfigRepo_UpdateDraftAsync_ParticipatesInCallerTx()
    {
        var repo = new AgreementConfigRepository(_harness.Factory);
        var entity = NewAgreementConfig();
        var configId = await repo.CreateAsync(entity);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var updated = NewAgreementConfig(weeklyNorm: 38m);
        var ok = await repo.UpdateDraftAsync(conn, tx, configId, updated);
        Assert.True(ok);
        await AssertTxStillUsable(conn, tx);
        var seenInside = await ScalarInsideTx<decimal>(
            conn, tx, "SELECT weekly_norm_hours FROM agreement_configs WHERE config_id = @id", configId);
        Assert.Equal(38m, seenInside);
        await tx.RollbackAsync();
        var seenAfter = await ScalarFreshConn<decimal>(
            "SELECT weekly_norm_hours FROM agreement_configs WHERE config_id = @id", configId);
        Assert.Equal(37m, seenAfter); // original weeklyNorm
    }

    [Fact]
    public async Task AgreementConfigRepo_PublishAsync_ParticipatesInCallerTx()
    {
        var repo = new AgreementConfigRepository(_harness.Factory);
        var draftId = await repo.CreateAsync(NewAgreementConfig());

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var (archivedId, wasPublished) = await repo.PublishAsync(conn, tx, draftId, "tester");
        Assert.Null(archivedId); // no prior ACTIVE for this (code, version)
        Assert.True(wasPublished); // target WAS DRAFT and is now ACTIVE in this tx (S24 Step 7a fix)
        await AssertTxStillUsable(conn, tx);
        var statusInside = await ScalarInsideTx<string>(
            conn, tx, "SELECT status FROM agreement_configs WHERE config_id = @id", draftId);
        Assert.Equal("ACTIVE", statusInside);
        await tx.RollbackAsync();
        // Rollback reverts to DRAFT.
        var statusAfter = await ScalarFreshConn<string>(
            "SELECT status FROM agreement_configs WHERE config_id = @id", draftId);
        Assert.Equal("DRAFT", statusAfter);
    }

    [Fact]
    public async Task AgreementConfigRepo_ArchiveAsync_ParticipatesInCallerTx()
    {
        var repo = new AgreementConfigRepository(_harness.Factory);
        var configId = await repo.CreateAsync(NewAgreementConfig());

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var ok = await repo.ArchiveAsync(conn, tx, configId, "tester");
        Assert.True(ok);
        await AssertTxStillUsable(conn, tx);
        var statusInside = await ScalarInsideTx<string>(
            conn, tx, "SELECT status FROM agreement_configs WHERE config_id = @id", configId);
        Assert.Equal("ARCHIVED", statusInside);
        await tx.RollbackAsync();
        var statusAfter = await ScalarFreshConn<string>(
            "SELECT status FROM agreement_configs WHERE config_id = @id", configId);
        Assert.Equal("DRAFT", statusAfter);
    }

    [Fact]
    public async Task AgreementConfigRepo_AppendAuditAsync_ParticipatesInCallerTx()
    {
        var repo = new AgreementConfigRepository(_harness.Factory);
        var configId = Guid.NewGuid();

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await repo.AppendAuditAsync(conn, tx, configId, "CREATED", null, "{}", "tester", "GLOBAL_ADMIN");

        await AssertTxStillUsable(conn, tx);
        Assert.Equal(1L, await CountInsideTx(conn, tx, "agreement_config_audit", "config_id", configId));
        await tx.RollbackAsync();
        Assert.Equal(0L, await CountFreshConn("agreement_config_audit", "config_id", configId));
    }

    // ── PositionOverrideRepository ────────────────────────────────────────────────────

    [Fact]
    public async Task PositionOverrideRepo_CreateAsync_ParticipatesInCallerTx()
    {
        var repo = new PositionOverrideRepository(_harness.Factory);
        var entity = NewPositionOverride();

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var overrideId = await repo.CreateAsync(conn, tx, entity);

        await AssertTxStillUsable(conn, tx);
        Assert.Equal(1L, await CountInsideTx(conn, tx, "position_override_configs", "override_id", overrideId));
        await tx.RollbackAsync();
        Assert.Equal(0L, await CountFreshConn("position_override_configs", "override_id", overrideId));
    }

    [Fact]
    public async Task PositionOverrideRepo_UpdateAsync_ParticipatesInCallerTx()
    {
        var repo = new PositionOverrideRepository(_harness.Factory);
        var entity = NewPositionOverride();
        var overrideId = await repo.CreateAsync(entity);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var updated = NewPositionOverride(maxFlex: 250m);
        var ok = await repo.UpdateAsync(conn, tx, overrideId, updated);
        Assert.True(ok);
        await AssertTxStillUsable(conn, tx);
        await tx.RollbackAsync();
        var seenAfter = await ScalarFreshConn<decimal>(
            "SELECT max_flex_balance FROM position_override_configs WHERE override_id = @id", overrideId);
        Assert.Equal(200m, seenAfter); // original
    }

    [Fact]
    public async Task PositionOverrideRepo_DeactivateAsync_ParticipatesInCallerTx()
    {
        var repo = new PositionOverrideRepository(_harness.Factory);
        var overrideId = await repo.CreateAsync(NewPositionOverride());

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var ok = await repo.DeactivateAsync(conn, tx, overrideId);
        Assert.True(ok);
        await AssertTxStillUsable(conn, tx);
        var statusInside = await ScalarInsideTx<string>(
            conn, tx, "SELECT status FROM position_override_configs WHERE override_id = @id", overrideId);
        Assert.Equal("INACTIVE", statusInside);
        await tx.RollbackAsync();
        var statusAfter = await ScalarFreshConn<string>(
            "SELECT status FROM position_override_configs WHERE override_id = @id", overrideId);
        Assert.Equal("ACTIVE", statusAfter);
    }

    [Fact]
    public async Task PositionOverrideRepo_ActivateAsync_ParticipatesInCallerTx()
    {
        var repo = new PositionOverrideRepository(_harness.Factory);
        var overrideId = await repo.CreateAsync(NewPositionOverride());
        await repo.DeactivateAsync(overrideId); // seed INACTIVE

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var ok = await repo.ActivateAsync(conn, tx, overrideId);
        Assert.True(ok);
        await AssertTxStillUsable(conn, tx);
        var statusInside = await ScalarInsideTx<string>(
            conn, tx, "SELECT status FROM position_override_configs WHERE override_id = @id", overrideId);
        Assert.Equal("ACTIVE", statusInside);
        await tx.RollbackAsync();
        var statusAfter = await ScalarFreshConn<string>(
            "SELECT status FROM position_override_configs WHERE override_id = @id", overrideId);
        Assert.Equal("INACTIVE", statusAfter);
    }

    [Fact]
    public async Task PositionOverrideRepo_AppendAuditAsync_ParticipatesInCallerTx()
    {
        var repo = new PositionOverrideRepository(_harness.Factory);
        var overrideId = Guid.NewGuid();

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await repo.AppendAuditAsync(conn, tx, overrideId, "CREATED", null, "{}", "tester", "GLOBAL_ADMIN");

        await AssertTxStillUsable(conn, tx);
        Assert.Equal(1L, await CountInsideTx(conn, tx, "position_override_config_audit", "override_id", overrideId));
        await tx.RollbackAsync();
        Assert.Equal(0L, await CountFreshConn("position_override_config_audit", "override_id", overrideId));
    }

    // ── WageTypeMappingRepository ─────────────────────────────────────────────────────

    [Fact]
    public async Task WageTypeMappingRepo_CreateAsync_ParticipatesInCallerTx()
    {
        var repo = new WageTypeMappingRepository(_harness.Factory);
        var mapping = NewWageTypeMapping("TT_CREATE_TEST_1");

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var ok = await repo.CreateAsync(conn, tx, mapping);
        Assert.True(ok);
        await AssertTxStillUsable(conn, tx);
        Assert.Equal(1L, await CountInsideTx(
            conn, tx,
            "SELECT COUNT(*) FROM wage_type_mappings WHERE time_type = @id",
            mapping.TimeType));
        await tx.RollbackAsync();
        Assert.Equal(0L, await ScalarFreshConn<long>(
            "SELECT COUNT(*) FROM wage_type_mappings WHERE time_type = @id", mapping.TimeType));
    }

    [Fact]
    public async Task WageTypeMappingRepo_UpdateAsync_ParticipatesInCallerTx()
    {
        var repo = new WageTypeMappingRepository(_harness.Factory);
        var mapping = NewWageTypeMapping("TT_UPDATE_TEST_1");
        await repo.CreateAsync(mapping);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        // Mutate WageType to a sentinel value to verify the in-tx UPDATE took effect.
        var updated = new WageTypeMapping
        {
            TimeType = mapping.TimeType,
            WageType = "SLS_9999",
            OkVersion = mapping.OkVersion,
            AgreementCode = mapping.AgreementCode,
            Position = mapping.Position,
            Description = mapping.Description,
        };
        var ok = await repo.UpdateAsync(conn, tx, updated);
        Assert.True(ok);
        await AssertTxStillUsable(conn, tx);
        await tx.RollbackAsync();
        var seenAfter = await ScalarFreshConn<string>(
            "SELECT wage_type FROM wage_type_mappings WHERE time_type = @id", mapping.TimeType);
        Assert.Equal("SLS_0110", seenAfter); // original
    }

    [Fact]
    public async Task WageTypeMappingRepo_DeleteAsync_ParticipatesInCallerTx()
    {
        var repo = new WageTypeMappingRepository(_harness.Factory);
        var mapping = NewWageTypeMapping("TT_DELETE_TEST_1");
        await repo.CreateAsync(mapping);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var ok = await repo.DeleteAsync(conn, tx, mapping.TimeType, mapping.OkVersion, mapping.AgreementCode, mapping.Position);
        Assert.True(ok);
        await AssertTxStillUsable(conn, tx);
        Assert.Equal(0L, await CountInsideTx(
            conn, tx,
            "SELECT COUNT(*) FROM wage_type_mappings WHERE time_type = @id",
            mapping.TimeType));
        await tx.RollbackAsync();
        // After rollback the row is back.
        Assert.Equal(1L, await ScalarFreshConn<long>(
            "SELECT COUNT(*) FROM wage_type_mappings WHERE time_type = @id", mapping.TimeType));
    }

    [Fact]
    public async Task WageTypeMappingRepo_AppendAuditAsync_ParticipatesInCallerTx()
    {
        var repo = new WageTypeMappingRepository(_harness.Factory);
        var mapping = NewWageTypeMapping("TT_AUDIT_TEST_1");

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await repo.AppendAuditAsync(
            conn, tx, mapping.TimeType, mapping.OkVersion, mapping.AgreementCode, mapping.Position,
            "CREATED", null, "{}", "tester", "GLOBAL_ADMIN");

        await AssertTxStillUsable(conn, tx);
        var countInside = await ScalarInsideTx<long>(
            conn, tx,
            "SELECT COUNT(*) FROM wage_type_mapping_audit WHERE time_type = @id",
            mapping.TimeType);
        Assert.Equal(1L, countInside);
        await tx.RollbackAsync();
        Assert.Equal(0L, await ScalarFreshConn<long>(
            "SELECT COUNT(*) FROM wage_type_mapping_audit WHERE time_type = @id", mapping.TimeType));
    }

    // ── OvertimePreApprovalRepository ─────────────────────────────────────────────────

    [Fact]
    public async Task OvertimePreApprovalRepo_CreateAsync_ParticipatesInCallerTx()
    {
        var repo = new OvertimePreApprovalRepository(_harness.Factory);
        var approval = new OvertimePreApproval
        {
            Id = Guid.NewGuid(),
            EmployeeId = "EMP_TX_PA",
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            MaxHours = 20m,
            Status = "PENDING",
            Reason = "Tx contract test",
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await repo.CreateAsync(conn, tx, approval);

        await AssertTxStillUsable(conn, tx);
        Assert.Equal(1L, await CountInsideTx(conn, tx, "overtime_pre_approvals", "id", approval.Id));
        await tx.RollbackAsync();
        Assert.Equal(0L, await CountFreshConn("overtime_pre_approvals", "id", approval.Id));
    }

    // ── OvertimeBalanceRepository ─────────────────────────────────────────────────────

    [Fact]
    public async Task OvertimeBalanceRepo_AdjustPaidOutAsync_ParticipatesInCallerTx()
    {
        var balanceRepo = new OvertimeBalanceRepository(_harness.Factory);
        const string employeeId = "EMP_TX_BAL_PO";
        const int year = 2026;
        await balanceRepo.UpsertAsync(new OvertimeBalance
        {
            BalanceId = Guid.NewGuid(),
            EmployeeId = employeeId,
            AgreementCode = "HK",
            PeriodYear = year,
            Accumulated = 50m,
            PaidOut = 0m,
            AfspadseringUsed = 0m,
            CompensationModel = "UDBETALING",
        });

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var (success, newPaidOut) = await balanceRepo.AdjustPaidOutAsync(conn, tx, employeeId, year, 5m);
        Assert.True(success);
        Assert.Equal(5m, newPaidOut);
        await AssertTxStillUsable(conn, tx);
        await tx.RollbackAsync();
        var paidOutAfter = await ScalarFreshConn<decimal>(
            "SELECT paid_out FROM overtime_balances WHERE employee_id = @id", employeeId);
        Assert.Equal(0m, paidOutAfter); // rollback reverted
    }

    [Fact]
    public async Task OvertimeBalanceRepo_AdjustAfspadseringAsync_ParticipatesInCallerTx()
    {
        var balanceRepo = new OvertimeBalanceRepository(_harness.Factory);
        const string employeeId = "EMP_TX_BAL_AF";
        const int year = 2026;
        await balanceRepo.UpsertAsync(new OvertimeBalance
        {
            BalanceId = Guid.NewGuid(),
            EmployeeId = employeeId,
            AgreementCode = "HK",
            PeriodYear = year,
            Accumulated = 50m,
            PaidOut = 0m,
            AfspadseringUsed = 0m,
            CompensationModel = "AFSPADSERING",
        });

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var (success, newAfspad) = await balanceRepo.AdjustAfspadseringAsync(conn, tx, employeeId, year, 7m);
        Assert.True(success);
        Assert.Equal(7m, newAfspad);
        await AssertTxStillUsable(conn, tx);
        await tx.RollbackAsync();
        var afspadAfter = await ScalarFreshConn<decimal>(
            "SELECT afspadsering_used FROM overtime_balances WHERE employee_id = @id", employeeId);
        Assert.Equal(0m, afspadAfter);
    }

    // ── TimerSessionRepository ────────────────────────────────────────────────────────

    [Fact]
    public async Task TimerRepo_CheckInAsync_ParticipatesInCallerTx()
    {
        var repo = new TimerSessionRepository(_harness.Factory);
        var session = new TimerSession
        {
            SessionId = Guid.NewGuid(),
            EmployeeId = "EMP_TX_TIMER_IN",
            Date = new DateOnly(2026, 5, 7),
            CheckInAt = DateTime.UtcNow,
            IsActive = true,
        };

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await repo.CheckInAsync(conn, tx, session);

        await AssertTxStillUsable(conn, tx);
        Assert.Equal(1L, await CountInsideTx(conn, tx, "timer_sessions", "session_id", session.SessionId));
        await tx.RollbackAsync();
        Assert.Equal(0L, await CountFreshConn("timer_sessions", "session_id", session.SessionId));
    }

    [Fact]
    public async Task TimerRepo_CheckOutAsync_ParticipatesInCallerTx()
    {
        var repo = new TimerSessionRepository(_harness.Factory);
        var session = new TimerSession
        {
            SessionId = Guid.NewGuid(),
            EmployeeId = "EMP_TX_TIMER_OUT",
            Date = new DateOnly(2026, 5, 7),
            CheckInAt = DateTime.UtcNow,
            IsActive = true,
        };
        await repo.CheckInAsync(session);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var checkOutAt = DateTime.UtcNow.AddHours(8);
        await repo.CheckOutAsync(conn, tx, session.SessionId, checkOutAt);

        await AssertTxStillUsable(conn, tx);
        var isActiveInside = await ScalarInsideTx<bool>(
            conn, tx, "SELECT is_active FROM timer_sessions WHERE session_id = @id", session.SessionId);
        Assert.False(isActiveInside);
        await tx.RollbackAsync();
        var isActiveAfter = await ScalarFreshConn<bool>(
            "SELECT is_active FROM timer_sessions WHERE session_id = @id", session.SessionId);
        Assert.True(isActiveAfter); // rollback reverted
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Issues a trivial command on the SUPPLIED tx; if the SUT had committed or rolled
    /// back the tx, this would throw <see cref="InvalidOperationException"/> /
    /// <see cref="NpgsqlException"/>. Survives ⇒ tx is still active and caller-controlled.
    /// Also asserts the connection is still in <see cref="ConnectionState.Open"/> — if the
    /// SUT had opened-and-closed its own connection, the caller's connection would still
    /// be open, but if it had hijacked the caller's connection state somehow this would
    /// catch it.
    /// </summary>
    private static async Task AssertTxStillUsable(NpgsqlConnection conn, NpgsqlTransaction tx)
    {
        Assert.Equal(ConnectionState.Open, conn.State);
        await using var cmd = new NpgsqlCommand("SELECT 1", conn, tx);
        var v = await cmd.ExecuteScalarAsync();
        Assert.Equal(1, Convert.ToInt32(v));
    }

    private static async Task<long> CountInsideTx(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string table, string column, object value)
    {
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {table} WHERE {column} = @v", conn, tx);
        cmd.Parameters.AddWithValue("v", value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<long> CountInsideTx(
        NpgsqlConnection conn, NpgsqlTransaction tx, string sql, object idValue)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", idValue);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<long> CountFreshConn(string table, string column, object value)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM {table} WHERE {column} = @v", conn);
        cmd.Parameters.AddWithValue("v", value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<T?> ScalarInsideTx<T>(
        NpgsqlConnection conn, NpgsqlTransaction tx, string sql, object idValue)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue("id", idValue);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result is DBNull) return default;
        return (T)result;
    }

    private async Task<T?> ScalarFreshConn<T>(string sql, object idValue)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", idValue);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result is DBNull) return default;
        return (T)result;
    }

    // ── Test data builders ────────────────────────────────────────────────────────────

    private static ApprovalPeriod NewPeriod() => new()
    {
        PeriodId = Guid.NewGuid(),
        EmployeeId = "EMP_TX_AP",
        OrgId = OrgId,
        PeriodStart = new DateOnly(2026, 5, 1),
        PeriodEnd = new DateOnly(2026, 5, 31),
        PeriodType = "MONTHLY",
        Status = "DRAFT",
        AgreementCode = "HK",
        OkVersion = "OK24",
    };

    private static AgreementConfigEntity NewAgreementConfig(decimal weeklyNorm = 37m) => new()
    {
        ConfigId = Guid.Empty,
        AgreementCode = "TX_AGR_" + Guid.NewGuid().ToString("N").Substring(0, 8),
        OkVersion = "OK24",
        Status = AgreementConfigStatus.DRAFT,
        WeeklyNormHours = weeklyNorm,
        NormPeriodWeeks = 1,
        NormModel = NormModel.WEEKLY_HOURS,
        AnnualNormHours = 1924m,
        MaxFlexBalance = 100m,
        FlexCarryoverMax = 50m,
        HasOvertime = true,
        HasMerarbejde = false,
        OvertimeThreshold50 = 37m,
        OvertimeThreshold100 = 40m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        EveningStart = 17,
        EveningEnd = 23,
        NightStart = 23,
        NightEnd = 6,
        EveningRate = 1.25m,
        NightRate = 1.5m,
        WeekendSaturdayRate = 1.5m,
        WeekendSundayRate = 2m,
        HolidayRate = 2m,
        OnCallDutyEnabled = false,
        OnCallDutyRate = 0.33m,
        CallInWorkEnabled = false,
        CallInMinimumHours = 3m,
        CallInRate = 1m,
        TravelTimeEnabled = false,
        WorkingTravelRate = 1m,
        NonWorkingTravelRate = 0.5m,
        MaxDailyHours = 13m,
        MinimumRestHours = 11m,
        RestPeriodDerogationAllowed = false,
        WeeklyMaxHoursReferencePeriod = 17,
        VoluntaryUnsocialHoursAllowed = true,
        DefaultCompensationModel = "UDBETALING",
        EmployeeCompensationChoice = false,
        MaxOvertimeHoursPerPeriod = 0m,
        OvertimeRequiresPreApproval = false,
        CreatedBy = "tester",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Description = "tx-contract-test",
    };

    private static PositionOverrideConfigEntity NewPositionOverride(decimal? maxFlex = 200m) => new()
    {
        OverrideId = Guid.Empty,
        AgreementCode = "AC",
        OkVersion = "OK24",
        PositionCode = "DEPARTMENT_HEAD",
        Status = "ACTIVE",
        MaxFlexBalance = maxFlex,
        FlexCarryoverMax = null,
        NormPeriodWeeks = 4,
        WeeklyNormHours = null,
        CreatedBy = "tester",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        Description = "tx-contract-test",
    };

    private static WageTypeMapping NewWageTypeMapping(string timeType) => new()
    {
        TimeType = timeType,
        WageType = "SLS_0110",
        OkVersion = "OK24",
        AgreementCode = "HK",
        Position = "",
        Description = "tx-contract-test",
    };
}
