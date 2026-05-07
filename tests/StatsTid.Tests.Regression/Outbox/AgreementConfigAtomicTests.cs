using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S24 TASK-2408 forced-rollback tests for Phase 2 / TASK-2403's 5 converted
/// agreement-config endpoints (Pattern B — endpoint emits an audit row in the same tx).
/// Each test mirrors the converted endpoint's orchestration with
/// <see cref="ForcedRollbackHarness.ThrowingOutboxEnqueue"/> wired in for
/// <see cref="StatsTid.Infrastructure.Outbox.IOutboxEnqueue"/>; the throw before commit
/// rolls the tx back, and the four post-action assertions pin no leakage.
///
/// <para>
/// Phase 2 endpoints under test:
/// <list type="bullet">
///   <item><c>POST /api/agreement-configs</c> (<see cref="Create_OutboxFails_RollsBack"/>)</item>
///   <item><c>POST /api/agreement-configs/{configId}/clone</c> (<see cref="Clone_OutboxFails_RollsBack"/>)</item>
///   <item><c>PUT /api/agreement-configs/{configId}</c> (<see cref="UpdateDraft_OutboxFails_RollsBack"/>)</item>
///   <item><c>POST /api/agreement-configs/{configId}/publish</c>
///         (<see cref="Publish_OutboxFails_RollsBack"/>) — most invasive endpoint:
///         atomically archives prior ACTIVE + activates DRAFT in a single tx; the test
///         pins that BOTH state transitions roll back together.</item>
///   <item><c>POST /api/agreement-configs/{configId}/archive</c> (<see cref="Archive_OutboxFails_RollsBack"/>)</item>
/// </list>
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AgreementConfigAtomicTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private AgreementConfigRepository _repo = null!;
    private ForcedRollbackHarness.ThrowingOutboxEnqueue _outbox = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _repo = new AgreementConfigRepository(_harness.Factory);
        _outbox = new ForcedRollbackHarness.ThrowingOutboxEnqueue();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task Create_OutboxFails_RollsBack()
    {
        var entity = NewConfig();
        var newConfigIdPlaceholder = Guid.Empty;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var configId = await _repo.CreateAsync(conn, tx, entity);
            newConfigIdPlaceholder = configId;
            await _repo.AppendAuditAsync(
                conn, tx, configId, "CREATED", null, "{}", "tester", "GLOBAL_ADMIN");

            var @event = new AgreementConfigCreated
            {
                ConfigId = configId,
                AgreementCode = entity.AgreementCode,
                OkVersion = entity.OkVersion,
            };
            await _outbox.EnqueueAsync(conn, tx, $"agreement-config-{configId}", @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Filter on the unique agreement_code we generated — that row must not exist.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"agreement_code = '{entity.AgreementCode}'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"config_id = '{newConfigIdPlaceholder}'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(
            _harness.ConnectionString, $"agreement-config-{newConfigIdPlaceholder}");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(
            _harness.ConnectionString, $"agreement-config-{newConfigIdPlaceholder}");
    }

    [Fact]
    public async Task Clone_OutboxFails_RollsBack()
    {
        // Arrange: a source DRAFT config to clone from (created via no-tx path).
        var source = NewConfig();
        var sourceId = await _repo.CreateAsync(source);

        var clone = NewConfig(); // unique agreement_code as our witness column
        var newCloneIdPlaceholder = Guid.Empty;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var newConfigId = await _repo.CreateAsync(conn, tx, clone);
            newCloneIdPlaceholder = newConfigId;
            await _repo.AppendAuditAsync(
                conn, tx, newConfigId, "CLONED", null, $"{{\"sourceConfigId\":\"{sourceId}\"}}",
                "tester", "GLOBAL_ADMIN");

            var @event = new AgreementConfigCloned
            {
                ConfigId = newConfigId,
                SourceConfigId = sourceId,
                AgreementCode = clone.AgreementCode,
                OkVersion = clone.OkVersion,
            };
            await _outbox.EnqueueAsync(conn, tx, $"agreement-config-{newConfigId}", @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // Source config remains; clone-row must not exist (filter on the clone's unique code).
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"agreement_code = '{clone.AgreementCode}'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"config_id = '{newCloneIdPlaceholder}'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(
            _harness.ConnectionString, $"agreement-config-{newCloneIdPlaceholder}");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(
            _harness.ConnectionString, $"agreement-config-{newCloneIdPlaceholder}");
    }

    [Fact]
    public async Task UpdateDraft_OutboxFails_RollsBack()
    {
        var initial = NewConfig(weeklyNorm: 37m);
        var configId = await _repo.CreateAsync(initial);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            // Mutate a sentinel field (WeeklyNormHours 37 -> 38) to verify the rollback.
            var updated = NewConfig(weeklyNorm: 38m);
            var ok = await _repo.UpdateDraftAsync(conn, tx, configId, updated);
            Assert.True(ok);
            await _repo.AppendAuditAsync(
                conn, tx, configId, "UPDATED", "{}", "{}", "tester", "GLOBAL_ADMIN");

            var @event = new AgreementConfigUpdated
            {
                ConfigId = configId,
                AgreementCode = initial.AgreementCode,
                OkVersion = initial.OkVersion,
            };
            await _outbox.EnqueueAsync(conn, tx, $"agreement-config-{configId}", @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // weekly_norm_hours stays at 37 (the seed); a row matching 38 is the absence-witness.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"config_id = '{configId}' AND weekly_norm_hours = 38");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"config_id = '{configId}' AND action = 'UPDATED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(
            _harness.ConnectionString, $"agreement-config-{configId}");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(
            _harness.ConnectionString, $"agreement-config-{configId}");
    }

    [Fact]
    public async Task Publish_OutboxFails_RollsBack()
    {
        // Arrange: prior ACTIVE + new DRAFT for the same (agreement_code, ok_version).
        // Publish atomically archives the prior ACTIVE and activates the DRAFT — both
        // state transitions must roll back together when the outbox throws.
        var (priorActiveId, draftId, sharedAgreementCode) = await SeedPriorActivePlusDraftAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var archivedId = await _repo.PublishAsync(conn, tx, draftId, "tester");
            // archivedId == priorActiveId — endpoint stamps that into the event payload.
            await _repo.AppendAuditAsync(
                conn, tx, draftId, "PUBLISHED", null, $"{{\"archivedConfigId\":\"{archivedId}\"}}",
                "tester", "GLOBAL_ADMIN");

            var @event = new AgreementConfigPublished
            {
                ConfigId = draftId,
                AgreementCode = sharedAgreementCode,
                OkVersion = "OK24",
                ArchivedConfigId = archivedId,
            };
            await _outbox.EnqueueAsync(conn, tx, $"agreement-config-{draftId}", @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        // BOTH state transitions must have rolled back: prior is still ACTIVE, draft is
        // still DRAFT. Either being mid-flight (prior=ARCHIVED OR draft=ACTIVE) would mean
        // a partial rollback — the assertion shape pins both.
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"config_id = '{priorActiveId}' AND status = 'ARCHIVED'");
        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"config_id = '{draftId}' AND status = 'ACTIVE'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"config_id = '{draftId}' AND action = 'PUBLISHED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(
            _harness.ConnectionString, $"agreement-config-{draftId}");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(
            _harness.ConnectionString, $"agreement-config-{draftId}");
    }

    [Fact]
    public async Task Archive_OutboxFails_RollsBack()
    {
        var initial = NewConfig();
        var configId = await _repo.CreateAsync(initial);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var archived = await _repo.ArchiveAsync(conn, tx, configId, "tester");
            Assert.True(archived);
            await _repo.AppendAuditAsync(
                conn, tx, configId, "ARCHIVED", "DRAFT", "ARCHIVED", "tester", "GLOBAL_ADMIN");

            var @event = new AgreementConfigArchived
            {
                ConfigId = configId,
                AgreementCode = initial.AgreementCode,
                OkVersion = initial.OkVersion,
            };
            await _outbox.EnqueueAsync(conn, tx, $"agreement-config-{configId}", @event);
            await tx.CommitAsync();
        });
        Assert.Equal(ForcedRollbackHarness.ThrowingOutboxEnqueue.ThrowMessage, ex.Message);

        await ForcedRollbackHarness.AssertNoStateMutationAsync(
            _harness.ConnectionString, "agreement_configs",
            $"config_id = '{configId}' AND status = 'ARCHIVED'");
        await ForcedRollbackHarness.AssertNoAuditRowAsync(
            _harness.ConnectionString, "agreement_config_audit",
            $"config_id = '{configId}' AND action = 'ARCHIVED'");
        await ForcedRollbackHarness.AssertNoEventRowAsync(
            _harness.ConnectionString, $"agreement-config-{configId}");
        await ForcedRollbackHarness.AssertNoOutboxRowAsync(
            _harness.ConnectionString, $"agreement-config-{configId}");
    }

    // ── Test data builders ────────────────────────────────────────────────────────────

    private async Task<(Guid PriorActiveId, Guid DraftId, string SharedAgreementCode)> SeedPriorActivePlusDraftAsync()
    {
        var sharedCode = "FR_PUB_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var prior = NewConfig();
        prior = WithCode(prior, sharedCode);
        var priorActiveId = await _repo.CreateAsync(prior, "ACTIVE");

        var draft = NewConfig();
        draft = WithCode(draft, sharedCode);
        var draftId = await _repo.CreateAsync(draft, "DRAFT");

        return (priorActiveId, draftId, sharedCode);
    }

    private static AgreementConfigEntity WithCode(AgreementConfigEntity src, string code)
    {
        // Records would be cleaner here, but AgreementConfigEntity is a POCO with init-only
        // properties — copy-construct.
        return new AgreementConfigEntity
        {
            ConfigId = src.ConfigId,
            AgreementCode = code,
            OkVersion = src.OkVersion,
            Status = src.Status,
            WeeklyNormHours = src.WeeklyNormHours,
            NormPeriodWeeks = src.NormPeriodWeeks,
            NormModel = src.NormModel,
            AnnualNormHours = src.AnnualNormHours,
            MaxFlexBalance = src.MaxFlexBalance,
            FlexCarryoverMax = src.FlexCarryoverMax,
            HasOvertime = src.HasOvertime,
            HasMerarbejde = src.HasMerarbejde,
            OvertimeThreshold50 = src.OvertimeThreshold50,
            OvertimeThreshold100 = src.OvertimeThreshold100,
            EveningSupplementEnabled = src.EveningSupplementEnabled,
            NightSupplementEnabled = src.NightSupplementEnabled,
            WeekendSupplementEnabled = src.WeekendSupplementEnabled,
            HolidaySupplementEnabled = src.HolidaySupplementEnabled,
            EveningStart = src.EveningStart,
            EveningEnd = src.EveningEnd,
            NightStart = src.NightStart,
            NightEnd = src.NightEnd,
            EveningRate = src.EveningRate,
            NightRate = src.NightRate,
            WeekendSaturdayRate = src.WeekendSaturdayRate,
            WeekendSundayRate = src.WeekendSundayRate,
            HolidayRate = src.HolidayRate,
            OnCallDutyEnabled = src.OnCallDutyEnabled,
            OnCallDutyRate = src.OnCallDutyRate,
            CallInWorkEnabled = src.CallInWorkEnabled,
            CallInMinimumHours = src.CallInMinimumHours,
            CallInRate = src.CallInRate,
            TravelTimeEnabled = src.TravelTimeEnabled,
            WorkingTravelRate = src.WorkingTravelRate,
            NonWorkingTravelRate = src.NonWorkingTravelRate,
            MaxDailyHours = src.MaxDailyHours,
            MinimumRestHours = src.MinimumRestHours,
            RestPeriodDerogationAllowed = src.RestPeriodDerogationAllowed,
            WeeklyMaxHoursReferencePeriod = src.WeeklyMaxHoursReferencePeriod,
            VoluntaryUnsocialHoursAllowed = src.VoluntaryUnsocialHoursAllowed,
            DefaultCompensationModel = src.DefaultCompensationModel,
            EmployeeCompensationChoice = src.EmployeeCompensationChoice,
            MaxOvertimeHoursPerPeriod = src.MaxOvertimeHoursPerPeriod,
            OvertimeRequiresPreApproval = src.OvertimeRequiresPreApproval,
            CreatedBy = src.CreatedBy,
            CreatedAt = src.CreatedAt,
            UpdatedAt = src.UpdatedAt,
            ClonedFromId = src.ClonedFromId,
            Description = src.Description,
        };
    }

    private static AgreementConfigEntity NewConfig(decimal weeklyNorm = 37m) => new()
    {
        ConfigId = Guid.Empty,
        // Unique agreement_code per test so we can filter by it as the absence-witness.
        AgreementCode = "FR_AGR_" + Guid.NewGuid().ToString("N").Substring(0, 8),
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
        Description = "forced-rollback-test",
    };
}
