using Microsoft.AspNetCore.Http;
using Npgsql;
using StatsTid.Backend.Api.Endpoints.Helpers;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Outbox;

namespace StatsTid.Tests.Regression.Concurrency;

/// <summary>
/// S25 / TASK-2508 concurrency regression tests for the three v3 mutating endpoints on
/// <c>agreement_configs</c> (per ADR-019 D2/D6/D7/D8). Verifies the row-version optimistic
/// concurrency contract at the repository surface and the admin-strict If-Match parser
/// at the helper surface. Direct-orchestration shape mirroring <see cref="Config.ProfileAuditTests"/>
/// + <see cref="Outbox.AgreementConfigAtomicTests"/> precedent (no <c>WebApplicationFactory&lt;Program&gt;</c>
/// — HTTP-surface harness deferred to Phase 4d per S24 carry-forward).
///
/// <para>
/// Test slots (7 total):
///   <list type="bullet">
///     <item>3 stale-If-Match → <see cref="OptimisticConcurrencyException"/> tests
///       (PUT update DRAFT / publish / archive)</item>
///     <item>3 missing-If-Match → <see cref="EtagHeaderHelper.TryParseIfMatch"/> false-return tests
///       (PUT update DRAFT / publish / archive)</item>
///     <item>1 end-to-end ETag-cycle test (CREATE → version=1, UPDATE → version=2 read-back)</item>
///   </list>
/// Audit version-transition coverage lives in <see cref="AuditVersionTransitionTests"/>
/// (per ADR-019 D8 — cross-resource invariant).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AgreementConfigConcurrencyTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private AgreementConfigRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        await ForcedRollbackHarness.ApplySchemaAsync(_harness.ConnectionString);
        _repo = new AgreementConfigRepository(_harness.Factory);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ─── Stale-If-Match (412 contract) — repo surface ─────────────────────────

    [Fact]
    public async Task UpdateDraft_StaleIfMatch_ThrowsOptimisticConcurrency()
    {
        // Seed a DRAFT agreement config; bump it once via v3 to take version 1 → 2; then
        // attempt a SECOND v3 update with the STALE expectedVersion=1. Repo must throw
        // OptimisticConcurrencyException carrying (Expected=1, Actual=2). Endpoint maps to 412.
        var initial = NewConfig(weeklyNorm: 37m);
        var configId = await _repo.CreateAsync(initial);

        // First update — succeeds, version 1 → 2.
        var updateA = NewConfig(weeklyNorm: 38m);
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var result = await _repo.UpdateDraftAsync(conn, tx, configId, expectedVersion: 1, updateA);
            await tx.CommitAsync();
            Assert.Equal(2L, result.Version);
        }

        // Second update with STALE expectedVersion=1 — must throw.
        var updateB = NewConfig(weeklyNorm: 39m);
        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.UpdateDraftAsync(conn, tx, configId, expectedVersion: 1, updateB);
            await tx.CommitAsync();
        });
        Assert.Equal(1L, ex.ExpectedVersion);
        Assert.Equal(2L, ex.ActualVersion);
    }

    [Fact]
    public async Task Publish_StaleIfMatch_ThrowsOptimisticConcurrency()
    {
        // Seed a DRAFT agreement config; bump it once (e.g. via v3 update) to take version 1 → 2;
        // then attempt to publish with STALE expectedVersion=1. Repo must throw.
        var draft = NewConfig();
        var configId = await _repo.CreateAsync(draft, "DRAFT");

        // Bump version to 2 via an UpdateDraftAsync call.
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.UpdateDraftAsync(conn, tx, configId, expectedVersion: 1, NewConfig(weeklyNorm: 38m));
            await tx.CommitAsync();
        }

        // Stale publish with expectedVersion=1 — must throw.
        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.PublishAsync(conn, tx, configId, expectedVersion: 1, actorId: "tester");
            await tx.CommitAsync();
        });
        Assert.Equal(1L, ex.ExpectedVersion);
        Assert.Equal(2L, ex.ActualVersion);
    }

    [Fact]
    public async Task Archive_StaleIfMatch_ThrowsOptimisticConcurrency()
    {
        // Seed a DRAFT agreement config; bump it once via v3 update to take version 1 → 2;
        // attempt archive with STALE expectedVersion=1. Repo must throw.
        var draft = NewConfig();
        var configId = await _repo.CreateAsync(draft, "DRAFT");

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.UpdateDraftAsync(conn, tx, configId, expectedVersion: 1, NewConfig(weeklyNorm: 38m));
            await tx.CommitAsync();
        }

        var ex = await Assert.ThrowsAsync<OptimisticConcurrencyException>(async () =>
        {
            await using var conn = _harness.Factory.Create();
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await _repo.ArchiveAsync(conn, tx, configId, expectedVersion: 1, actorId: "tester");
            await tx.CommitAsync();
        });
        Assert.Equal(1L, ex.ExpectedVersion);
        Assert.Equal(2L, ex.ActualVersion);
    }

    // ─── Missing-If-Match (428 contract) — helper surface ─────────────────────

    [Fact]
    public void UpdateDraft_MissingIfMatch_HelperRejects()
    {
        // The 428 path is hit at the endpoint, not the repo. Verify the helper surface
        // rejects requests with no If-Match header in admin-strict mode (mirrors the
        // PUT /api/agreement-configs/{configId} endpoint's first-line precondition check).
        var request = NewRequestWithoutIfMatch();
        var parsed = EtagHeaderHelper.TryParseIfMatch(
            request, out _, out var error);
        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("Missing If-Match", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Publish_MissingIfMatch_HelperRejects()
    {
        // Mirrors POST /api/agreement-configs/{configId}/publish missing-precondition path.
        var request = NewRequestWithoutIfMatch();
        var parsed = EtagHeaderHelper.TryParseIfMatch(request, out _, out var error);
        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("Missing If-Match", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Archive_MissingIfMatch_HelperRejects()
    {
        // Mirrors POST /api/agreement-configs/{configId}/archive missing-precondition path.
        var request = NewRequestWithoutIfMatch();
        var parsed = EtagHeaderHelper.TryParseIfMatch(request, out _, out var error);
        Assert.False(parsed);
        Assert.NotNull(error);
        Assert.Contains("Missing If-Match", error, StringComparison.Ordinal);
    }

    // ─── End-to-end ETag-cycle ─────────────────────────────────────────────────

    [Fact]
    public async Task EtagCycle_CreateThenUpdate_VersionMonotonicallyIncreases()
    {
        // Wire shape: CREATE (version=1) → GET (version=1) → UPDATE with If-Match: "1"
        // (version=2) → GET (version=2). This is the contract the frontend banner-with-retry
        // hook depends on.
        var initial = NewConfig(weeklyNorm: 37m);
        var configId = await _repo.CreateAsync(initial);

        var afterCreate = await _repo.GetByIdAsync(configId);
        Assert.NotNull(afterCreate);
        Assert.Equal(1L, afterCreate!.Version);

        SaveAgreementConfigResult updateResult;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            updateResult = await _repo.UpdateDraftAsync(
                conn, tx, configId, expectedVersion: 1, NewConfig(weeklyNorm: 38m));
            await tx.CommitAsync();
        }
        Assert.Equal(2L, updateResult.Version);
        Assert.False(updateResult.IsCreated);
        Assert.Null(updateResult.ArchivedId);

        var afterUpdate = await _repo.GetByIdAsync(configId);
        Assert.NotNull(afterUpdate);
        Assert.Equal(2L, afterUpdate!.Version);
        Assert.Equal(38m, afterUpdate.WeeklyNormHours);
    }

    // ─── Publish-supersession atomic emission (ADR-019 D1, S25 Step 7a B1 fix) ────

    [Fact]
    public async Task Publish_OverPriorActive_EmitsTwoAuditRowsAndTwoOutboxEvents()
    {
        // ADR-019 D1: when a publish supersedes a prior ACTIVE config of the same
        // (agreement_code, ok_version), the publish handler must emit TWO audit rows +
        // TWO outbox events — PUBLISHED for the new ACTIVE + ARCHIVED for the prior ACTIVE.
        // Repo surface guarantees `saveResult.ArchivedId` + `saveResult.ArchivedVersion`
        // are populated so the endpoint can route the second emission through the same tx.
        // Pre-S25-Step-7a-cycle-1 the publish path silently dropped the second emission.
        var agreementCode = "CON_AGR_" + Guid.NewGuid().ToString("N").Substring(0, 8);
        const string okVersion = "OK24";

        var priorActiveId = await _repo.CreateAsync(
            NewConfig(weeklyNorm: 37m, agreementCode: agreementCode, okVersion: okVersion),
            "ACTIVE");
        var draftId = await _repo.CreateAsync(
            NewConfig(weeklyNorm: 38m, agreementCode: agreementCode, okVersion: okVersion),
            "DRAFT");

        // Atomic publish + 2x audit + 2x outbox in one tx (mirrors the endpoint shape).
        SaveAgreementConfigResult saveResult;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            saveResult = await _repo.PublishAsync(
                conn, tx, draftId, expectedVersion: 1, actorId: "tester");

            await _repo.AppendAuditAsync(
                conn, tx, draftId, "PUBLISHED",
                null, $"{{\"archivedConfigId\":\"{saveResult.ArchivedId}\"}}",
                "tester", "GLOBAL_ADMIN",
                versionBefore: 1, versionAfter: saveResult.Version);
            await InsertOutboxEventAsync(
                conn, tx, $"agreement-config-{draftId}", "AgreementConfigPublished");

            // ADR-019 D1: second emission for the archived prior-ACTIVE config.
            var archivedId = saveResult.ArchivedId!.Value;
            var archivedVersion = saveResult.ArchivedVersion!.Value;
            await _repo.AppendAuditAsync(
                conn, tx, archivedId, "ARCHIVED",
                "ACTIVE", "ARCHIVED", "tester", "GLOBAL_ADMIN",
                versionBefore: archivedVersion - 1, versionAfter: archivedVersion);
            await InsertOutboxEventAsync(
                conn, tx, $"agreement-config-{archivedId}", "AgreementConfigArchived");

            await tx.CommitAsync();
        }

        // SaveResult must surface both archived id and archived version (BlockEr-fix
        // record extension — pre-fix only ArchivedId existed, no way to compute the
        // archived audit row's version-transition pair).
        Assert.Equal(priorActiveId, saveResult.ArchivedId);
        Assert.Equal(2L, saveResult.ArchivedVersion);
        Assert.Equal(2L, saveResult.Version);

        // Audit table: TWO distinct rows on TWO distinct config_ids, each with the
        // correct version-transition pair populated.
        var publishAuditRows = await ReadAuditRowsAsync(draftId);
        Assert.Single(publishAuditRows);
        Assert.Equal(("PUBLISHED", (long?)1L, (long?)2L), publishAuditRows[0]);

        var archiveAuditRows = await ReadAuditRowsAsync(priorActiveId);
        Assert.Single(archiveAuditRows);
        Assert.Equal(("ARCHIVED", (long?)1L, (long?)2L), archiveAuditRows[0]);

        // Outbox table: TWO distinct events on TWO distinct stream_ids.
        var draftOutbox = await ReadOutboxEventTypesAsync($"agreement-config-{draftId}");
        Assert.Single(draftOutbox);
        Assert.Equal("AgreementConfigPublished", draftOutbox[0]);

        var archivedOutbox = await ReadOutboxEventTypesAsync($"agreement-config-{priorActiveId}");
        Assert.Single(archivedOutbox);
        Assert.Equal("AgreementConfigArchived", archivedOutbox[0]);
    }

    // ── Verification helpers (audit + outbox introspection) ─────────────────────

    private static async Task InsertOutboxEventAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string streamId, string eventType)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO outbox_events (service_id, stream_id, event_id, event_type, event_payload)
            VALUES (@service, @stream, @eventId, @type, '{}'::jsonb)
            """, conn, tx);
        cmd.Parameters.AddWithValue("service", "backend-api");
        cmd.Parameters.AddWithValue("stream", streamId);
        cmd.Parameters.AddWithValue("eventId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("type", eventType);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<List<(string Action, long? VersionBefore, long? VersionAfter)>>
        ReadAuditRowsAsync(Guid configId)
    {
        var rows = new List<(string, long?, long?)>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT action, version_before, version_after
            FROM agreement_config_audit
            WHERE config_id = @configId
            ORDER BY audit_id ASC
            """, conn);
        cmd.Parameters.AddWithValue("configId", configId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2)));
        }
        return rows;
    }

    private async Task<List<string>> ReadOutboxEventTypesAsync(string streamId)
    {
        var types = new List<string>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT event_type FROM outbox_events WHERE stream_id = @stream ORDER BY outbox_id ASC",
            conn);
        cmd.Parameters.AddWithValue("stream", streamId);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            types.Add(reader.GetString(0));
        return types;
    }

    // ── Test data builders ────────────────────────────────────────────────────

    /// <summary>
    /// Construct a synthetic <see cref="HttpRequest"/> with no If-Match / If-None-Match
    /// headers — drives <see cref="EtagHeaderHelper.TryParseIfMatch"/>'s missing-precondition
    /// branch.
    /// </summary>
    private static HttpRequest NewRequestWithoutIfMatch()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "PUT";
        return ctx.Request;
    }

    private static AgreementConfigEntity NewConfig(
        decimal weeklyNorm = 37m,
        string? agreementCode = null,
        string okVersion = "OK24") => new()
    {
        ConfigId = Guid.Empty,
        // Default to a fresh unique agreement_code per call so concurrent fixtures don't
        // collide; supersession tests pass an explicit shared code so the publish-archives-
        // prior-ACTIVE path can fire.
        AgreementCode = agreementCode
            ?? "CON_AGR_" + Guid.NewGuid().ToString("N").Substring(0, 8),
        OkVersion = okVersion,
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
        Description = "concurrency-test",
    };
}
