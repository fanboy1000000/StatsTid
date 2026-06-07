using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
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
            description             TEXT,
            -- S25 / TASK-2503 row-version (ADR-018 D7 optimistic concurrency).
            -- AgreementConfigRepository.ReadEntity (src:876) reads this column;
            -- absence raised "Field not found in row: version". Mirrors init.sql
            -- + ForcedRollbackHarness (which already carries it).
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
            description         TEXT,
            -- S25 / TASK-2504 row-version (ADR-018 D7). PositionOverrideRepository.ReadEntity
            -- (src:644) reads this column; absence raised "Field not found in row: version".
            -- Mirrors init.sql + ForcedRollbackHarness (which already carries it).
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
            timestamp       TIMESTAMPTZ NOT NULL DEFAULT NOW()
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

        -- S27 / TASK-2702 projection tables (mirrors docker/postgres/init.sql:1181-1221).
        -- Wired into the (conn, tx) tx-contract tests below per S27 / TASK-2710 Slot 7.
        -- Schema drift between this DDL and production must be mirrored here.
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
            outbox_id                   BIGINT          NOT NULL,
            feriedage                   NUMERIC(8,4)
        );

        ALTER TABLE absences_projection ADD COLUMN IF NOT EXISTS feriedage NUMERIC(8,4);
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

        // S25 / TASK-2503: v3 UpdateDraftAsync(conn, tx, configId, expectedVersion, ...)
        // requires the row's current version. Read it back via the same path the HTTP
        // endpoint uses (GET → If-Match → PUT).
        var preEntity = await repo.GetByIdAsync(configId);
        Assert.NotNull(preEntity);
        var expectedVersion = preEntity!.Version;

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var updated = NewAgreementConfig(weeklyNorm: 38m);
        var saveResult = await repo.UpdateDraftAsync(conn, tx, configId, expectedVersion, updated);
        Assert.False(saveResult.IsCreated);
        Assert.Equal(expectedVersion + 1, saveResult.Version);
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

        // S25 / TASK-2503: v3 PublishAsync expects expectedVersion. Concurrent
        // state-change (S24 Step 7a P1) under v3 manifests as
        // OptimisticConcurrencyException → 412, replacing the pre-S25 (Guid?, bool) tuple.
        var preDraft = await repo.GetByIdAsync(draftId);
        Assert.NotNull(preDraft);
        var expectedVersion = preDraft!.Version;

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var saveResult = await repo.PublishAsync(conn, tx, draftId, expectedVersion, "tester");
        Assert.Null(saveResult.ArchivedId); // no prior ACTIVE for this (code, version)
        Assert.False(saveResult.IsCreated);
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

        // S25 / TASK-2503: v3 ArchiveAsync expects expectedVersion.
        var preEntity = await repo.GetByIdAsync(configId);
        Assert.NotNull(preEntity);
        var expectedVersion = preEntity!.Version;

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var saveResult = await repo.ArchiveAsync(conn, tx, configId, expectedVersion, "tester");
        Assert.False(saveResult.IsCreated);
        Assert.Equal(expectedVersion + 1, saveResult.Version);
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

        // S25 / TASK-2504: v3 UpdateAsync(conn, tx, overrideId, expectedVersion, ...)
        // requires the row's current version. Read it back via the same path the HTTP
        // endpoint uses (GET → If-Match → PUT).
        var preEntity = await repo.GetByIdAsync(overrideId);
        Assert.NotNull(preEntity);
        var expectedVersion = preEntity!.Version;

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var updated = NewPositionOverride(maxFlex: 250m);
        var saveResult = await repo.UpdateAsync(conn, tx, overrideId, expectedVersion, updated);
        Assert.False(saveResult.IsCreated);
        Assert.Equal(expectedVersion + 1, saveResult.Version);
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

        // S25 / TASK-2504: v3 DeactivateAsync expects expectedVersion.
        var preEntity = await repo.GetByIdAsync(overrideId);
        Assert.NotNull(preEntity);
        var expectedVersion = preEntity!.Version;

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var saveResult = await repo.DeactivateAsync(conn, tx, overrideId, expectedVersion);
        Assert.False(saveResult.IsCreated);
        Assert.Equal("INACTIVE", saveResult.Status);
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

        // S25 / TASK-2504: v3 ActivateAsync expects expectedVersion.
        var preEntity = await repo.GetByIdAsync(overrideId);
        Assert.NotNull(preEntity);
        var expectedVersion = preEntity!.Version;

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var saveResult = await repo.ActivateAsync(conn, tx, overrideId, expectedVersion);
        Assert.False(saveResult.IsCreated);
        Assert.Equal("ACTIVE", saveResult.Status);
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

        // S25 / TASK-2505: v3 UpdateAsync(conn, tx, mapping, expectedVersion, ...) requires
        // the row's current version. Read it back via the same path the HTTP endpoint uses
        // (GET → If-Match → PUT).
        var preMapping = await repo.GetByKeyAsync(mapping.TimeType, mapping.OkVersion, mapping.AgreementCode, mapping.Position);
        Assert.NotNull(preMapping);
        var expectedVersion = preMapping!.Version;

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
        var saveResult = await repo.UpdateAsync(conn, tx, updated, expectedVersion);
        Assert.False(saveResult.IsCreated);
        Assert.Equal(expectedVersion + 1, saveResult.Version);
        await AssertTxStillUsable(conn, tx);
        await tx.RollbackAsync();
        var seenAfter = await ScalarFreshConn<string>(
            "SELECT wage_type FROM wage_type_mappings WHERE time_type = @id", mapping.TimeType);
        Assert.Equal("SLS_0110", seenAfter); // original
    }

    [Fact]
    public async Task WageTypeMappingRepo_SoftDeleteAsync_ParticipatesInCallerTx()
    {
        var repo = new WageTypeMappingRepository(_harness.Factory);
        var mapping = NewWageTypeMapping("TT_DELETE_TEST_1");
        await repo.CreateAsync(mapping);

        // S29 / TASK-2904: hard DeleteAsync(conn, tx, ..., expectedVersion) was replaced by
        // SoftDeleteAsync(conn, tx, ..., expectedVersion, closeDate) per ADR-020 D2 — the row
        // is preserved with effective_to set instead of removed. Tx semantics unchanged.
        var preMapping = await repo.GetByKeyAsync(mapping.TimeType, mapping.OkVersion, mapping.AgreementCode, mapping.Position);
        Assert.NotNull(preMapping);
        var expectedVersion = preMapping!.Version;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        var ok = await repo.SoftDeleteAsync(conn, tx, mapping.TimeType, mapping.OkVersion, mapping.AgreementCode, mapping.Position, expectedVersion, today);
        Assert.True(ok);
        await AssertTxStillUsable(conn, tx);
        // Inside tx: row is closed (effective_to = today, no longer "open").
        Assert.Equal(0L, await CountInsideTx(
            conn, tx,
            "SELECT COUNT(*) FROM wage_type_mappings WHERE time_type = @id AND effective_to IS NULL",
            mapping.TimeType));
        await tx.RollbackAsync();
        // After rollback the row is open again.
        Assert.Equal(1L, await ScalarFreshConn<long>(
            "SELECT COUNT(*) FROM wage_type_mappings WHERE time_type = @id AND effective_to IS NULL", mapping.TimeType));
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

    /// <summary>
    /// S26 / TASK-2603 (b): asserts <see cref="OvertimePreApprovalRepository.UpdateStatusAsync(NpgsqlConnection, NpgsqlTransaction, Guid, string, string?, string?, CancellationToken)"/>
    /// participates in the caller-supplied transaction. Seed a PENDING row outside the SUT
    /// tx; open conn+tx; call UpdateStatusAsync(conn, tx, ...) flipping to APPROVED; verify
    /// the change is visible INSIDE the tx but NOT visible to a fresh connection (RC isolation
    /// guarantees the uncommitted UPDATE stays inside the tx). Rollback; the row must remain
    /// PENDING — proving the SUT did not silently auto-commit a private tx.
    /// </summary>
    [Fact]
    public async Task OvertimePreApprovalRepo_UpdateStatusAsync_ParticipatesInCallerTx()
    {
        var repo = new OvertimePreApprovalRepository(_harness.Factory);
        var approval = new OvertimePreApproval
        {
            Id = Guid.NewGuid(),
            EmployeeId = "EMP_TX_PA_US",
            PeriodStart = new DateOnly(2026, 5, 1),
            PeriodEnd = new DateOnly(2026, 5, 31),
            MaxHours = 20m,
            Status = "PENDING",
            Reason = "Tx contract test",
        };
        await repo.CreateAsync(approval); // seed PENDING outside the SUT tx

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await repo.UpdateStatusAsync(conn, tx, approval.Id, "APPROVED", "manager", "OK", default);

        await AssertTxStillUsable(conn, tx);
        // Visible INSIDE tx — proves the SUT used the supplied tx.
        var statusInside = await ScalarInsideTx<string>(
            conn, tx, "SELECT status FROM overtime_pre_approvals WHERE id = @id", approval.Id);
        Assert.Equal("APPROVED", statusInside);
        // NOT visible to a fresh connection under RC — proves the SUT did not auto-commit.
        var statusFreshBeforeRollback = await ScalarFreshConn<string>(
            "SELECT status FROM overtime_pre_approvals WHERE id = @id", approval.Id);
        Assert.Equal("PENDING", statusFreshBeforeRollback);
        await tx.RollbackAsync();
        // After rollback the row remains at the seeded PENDING value.
        var statusAfter = await ScalarFreshConn<string>(
            "SELECT status FROM overtime_pre_approvals WHERE id = @id", approval.Id);
        Assert.Equal("PENDING", statusAfter);
    }

    // ── EntitlementBalanceRepository (S26 / TASK-2603 (a)) ────────────────────────────

    /// <summary>
    /// S26 / TASK-2603 (a): asserts <see cref="EntitlementBalanceRepository.CheckAndAdjustAsync(NpgsqlConnection, NpgsqlTransaction, string, string, int, decimal, decimal, CancellationToken)"/>
    /// participates in the caller-supplied transaction. Seed a balance row at used=0 outside
    /// the SUT tx; open conn+tx; call CheckAndAdjustAsync(conn, tx, ..., deltaDays=2, quota=25);
    /// verify the new used value is visible INSIDE the tx but NOT visible to a fresh
    /// connection. Rollback; the row must remain at used=0 — proving atomicity of the
    /// outer tx + the (conn, tx) overload.
    /// </summary>
    [Fact]
    public async Task EntitlementBalanceRepo_CheckAndAdjustAsync_ParticipatesInCallerTx()
    {
        var repo = new EntitlementBalanceRepository(_harness.Factory);
        const string employeeId = "EMP_TX_EB_CA";
        const string entitlementType = "VACATION";
        const int year = 2026;
        await repo.UpsertAsync(new EntitlementBalance
        {
            BalanceId = Guid.NewGuid(),
            EmployeeId = employeeId,
            EntitlementType = entitlementType,
            EntitlementYear = year,
            TotalQuota = 25m,
            Used = 0m,
            Planned = 0m,
            CarryoverIn = 0m,
        });

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        // S60: CheckAndAdjustAsync split (guardCap, seedQuota). For this IMMEDIATE-behavior
        // test the prior single effectiveQuota=25 is passed as BOTH args (preserves semantics).
        var (success, newUsed) = await repo.CheckAndAdjustAsync(
            conn, tx, employeeId, entitlementType, year, deltaDays: 2m, guardCap: 25m, seedQuota: 25m);
        Assert.True(success);
        Assert.Equal(2m, newUsed);

        await AssertTxStillUsable(conn, tx);
        var usedInside = await ScalarInsideTx<decimal>(
            conn, tx,
            "SELECT used FROM entitlement_balances WHERE employee_id = @id AND entitlement_type = 'VACATION' AND entitlement_year = 2026",
            employeeId);
        Assert.Equal(2m, usedInside);
        var usedFreshBeforeRollback = await ScalarFreshConn<decimal>(
            "SELECT used FROM entitlement_balances WHERE employee_id = @id AND entitlement_type = 'VACATION' AND entitlement_year = 2026",
            employeeId);
        Assert.Equal(0m, usedFreshBeforeRollback); // SUT must NOT auto-commit
        await tx.RollbackAsync();
        var usedAfter = await ScalarFreshConn<decimal>(
            "SELECT used FROM entitlement_balances WHERE employee_id = @id AND entitlement_type = 'VACATION' AND entitlement_year = 2026",
            employeeId);
        Assert.Equal(0m, usedAfter); // rollback reverted the in-tx UPDATE
    }

    /// <summary>
    /// S26 Step 7a B3 fix: when no balance row exists for the (employee, type, year) tuple,
    /// <see cref="EntitlementBalanceRepository.CheckAndAdjustAsync(NpgsqlConnection, NpgsqlTransaction, string, string, int, decimal, decimal, CancellationToken)"/>
    /// must auto-create the row at zero-state baseline (Statement 1: ensure-row INSERT) and
    /// then run the atomic quota-checked UPDATE (Statement 2). If deltaDays fits within quota,
    /// returns (true, deltaDays); the row is left at used = deltaDays. Pre-fix this returned
    /// (false, 0m) — indistinguishable from a real quota breach — and Skema first-absence-of-
    /// year was rejected with a false 422.
    /// </summary>
    [Fact]
    public async Task EntitlementBalanceRepo_CheckAndAdjustAsync_AutoCreatesRowOnMissing()
    {
        var repo = new EntitlementBalanceRepository(_harness.Factory);
        // Unique employee id so no pre-existing row from other tests collides.
        var employeeId = "EMP_TX_EB_MISSING_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        const string entitlementType = "VACATION";
        const int year = 2026;

        // No seed — row does NOT exist. Verify by fresh-conn read.
        var rowBefore = await ScalarFreshConn<long>(
            "SELECT COUNT(*) FROM entitlement_balances WHERE employee_id = @id AND entitlement_type = 'VACATION' AND entitlement_year = 2026",
            employeeId);
        Assert.Equal(0L, rowBefore);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        // S60: split signature; old effectiveQuota=25 passed as both guardCap and seedQuota.
        var (success, newUsed) = await repo.CheckAndAdjustAsync(
            conn, tx, employeeId, entitlementType, year, deltaDays: 2m, guardCap: 25m, seedQuota: 25m);

        // Pre-S26 Step 7a: would have returned (false, 0m). Post-fix: row materializes,
        // Statement 2 UPDATE applies, returns (true, 2m).
        Assert.True(success);
        Assert.Equal(2m, newUsed);

        await tx.CommitAsync();
        var usedAfter = await ScalarFreshConn<decimal>(
            "SELECT used FROM entitlement_balances WHERE employee_id = @id AND entitlement_type = 'VACATION' AND entitlement_year = 2026",
            employeeId);
        Assert.Equal(2m, usedAfter);
    }

    /// <summary>
    /// S26 Step 7a B3 fix: when no balance row exists AND deltaDays exceeds quota, returns
    /// (false, 0m). Statement 1 (ensure-row INSERT with used=0 baseline) materializes the row
    /// at zero-state; Statement 2 (UPDATE with quota guard) fails the WHERE clause because
    /// 0 + deltaDays > effectiveQuota + 0; the failure-path read returns the freshly-created
    /// row's used (0m). Net: caller sees (false, 0m) — same surface as the existing-row breach
    /// case — and a zero-state row is left behind. Caller treats this as a quota breach.
    /// </summary>
    [Fact]
    public async Task EntitlementBalanceRepo_CheckAndAdjustAsync_RejectsWhenMissingRowDeltaExceedsQuota()
    {
        var repo = new EntitlementBalanceRepository(_harness.Factory);
        var employeeId = "EMP_TX_EB_OVER_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        const string entitlementType = "VACATION";
        const int year = 2026;

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        // S60: split signature; old effectiveQuota=25 passed as both guardCap and seedQuota.
        var (success, newUsed) = await repo.CheckAndAdjustAsync(
            conn, tx, employeeId, entitlementType, year, deltaDays: 30m, guardCap: 25m, seedQuota: 25m);

        Assert.False(success);
        Assert.Equal(0m, newUsed);

        // Post-Statement-1, a zero-state row exists in this tx (ensure-row INSERT succeeded).
        // The Statement-2 UPDATE failed the quota WHERE clause; the row stays at used=0.
        var usedInside = await ScalarInsideTx<decimal>(
            conn, tx,
            "SELECT used FROM entitlement_balances WHERE employee_id = @id AND entitlement_type = 'VACATION' AND entitlement_year = 2026",
            employeeId);
        Assert.Equal(0m, usedInside);

        await tx.RollbackAsync();
        var rowAfterRollback = await ScalarFreshConn<long>(
            "SELECT COUNT(*) FROM entitlement_balances WHERE employee_id = @id AND entitlement_type = 'VACATION' AND entitlement_year = 2026",
            employeeId);
        Assert.Equal(0L, rowAfterRollback); // rollback removed the ensure-row INSERT
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

    // ── TimeEntryProjectionRepository (S27 / TASK-2710 Slot 7) ────────────────────────

    /// <summary>
    /// S27 / TASK-2710 Slot 7: asserts <see cref="TimeEntryProjectionRepository.InsertAsync(NpgsqlConnection, NpgsqlTransaction, TimeEntryRegistered, long, CancellationToken)"/>
    /// participates in the caller-supplied transaction. Open conn+tx; insert a projection
    /// row with a synthetic outbox_id; verify the row is visible INSIDE the tx but NOT
    /// visible to a fresh connection (RC isolation guarantees the uncommitted INSERT
    /// stays inside the tx). Rollback; assert ZERO rows in <c>time_entries_projection</c>
    /// — proving the repo participated in the caller's tx rather than auto-committing.
    /// </summary>
    [Fact]
    public async Task TimeEntryProjectionRepo_InsertAsync_ParticipatesInCallerTx()
    {
        var repo = new TimeEntryProjectionRepository(_harness.Factory);
        var employeeId = "EMP_TX_TEP_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var @event = new TimeEntryRegistered
        {
            EmployeeId = employeeId,
            Date = new DateOnly(2026, 5, 7),
            Hours = 7.4m,
            TaskId = "PROJ-TX-1",
            ActivityType = "NORMAL",
            AgreementCode = "HK",
            OkVersion = "OK24",
        };
        const long syntheticOutboxId = 12345L;

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await repo.InsertAsync(conn, tx, @event, syntheticOutboxId);

        await AssertTxStillUsable(conn, tx);
        // Visible INSIDE tx — proves the SUT used the supplied tx.
        Assert.Equal(1L, await CountInsideTx(
            conn, tx, "time_entries_projection", "event_id", @event.EventId));
        // NOT visible to a fresh connection under RC — proves the SUT did not auto-commit.
        Assert.Equal(0L, await CountFreshConn(
            "time_entries_projection", "event_id", @event.EventId));
        await tx.RollbackAsync();
        // After rollback the row must NOT persist.
        Assert.Equal(0L, await CountFreshConn(
            "time_entries_projection", "event_id", @event.EventId));
    }

    // ── AbsenceProjectionRepository (S27 / TASK-2710 Slot 7) ──────────────────────────

    /// <summary>
    /// S27 / TASK-2710 Slot 7: asserts <see cref="AbsenceProjectionRepository.InsertAsync(NpgsqlConnection, NpgsqlTransaction, AbsenceRegistered, long, CancellationToken)"/>
    /// participates in the caller-supplied transaction. Mirror of
    /// <see cref="TimeEntryProjectionRepo_InsertAsync_ParticipatesInCallerTx"/> for
    /// the absences projection table.
    /// </summary>
    [Fact]
    public async Task AbsenceProjectionRepo_InsertAsync_ParticipatesInCallerTx()
    {
        var repo = new AbsenceProjectionRepository(_harness.Factory);
        var employeeId = "EMP_TX_AEP_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var @event = new AbsenceRegistered
        {
            EmployeeId = employeeId,
            Date = new DateOnly(2026, 5, 7),
            AbsenceType = "VACATION",
            Hours = 7.4m,
            AgreementCode = "HK",
            OkVersion = "OK24",
        };
        const long syntheticOutboxId = 12346L;

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        await repo.InsertAsync(conn, tx, @event, syntheticOutboxId);

        await AssertTxStillUsable(conn, tx);
        // Visible INSIDE tx — proves the SUT used the supplied tx.
        Assert.Equal(1L, await CountInsideTx(
            conn, tx, "absences_projection", "event_id", @event.EventId));
        // NOT visible to a fresh connection under RC — proves the SUT did not auto-commit.
        Assert.Equal(0L, await CountFreshConn(
            "absences_projection", "event_id", @event.EventId));
        await tx.RollbackAsync();
        // After rollback the row must NOT persist.
        Assert.Equal(0L, await CountFreshConn(
            "absences_projection", "event_id", @event.EventId));
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
