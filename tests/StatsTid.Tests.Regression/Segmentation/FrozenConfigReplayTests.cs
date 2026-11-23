using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.TestSupport;
using static StatsTid.Tests.Regression.Segmentation.ReplayTestKit;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// S148 / TASK-14830-T (AC-C3a, AC-C3b; ADR-043 D12, ADR-016 D10, S148 ruling R-5) — a month's stored proof holds the
/// agreement rules the month was paid on, by value; a replay sends exactly those and never asks the configuration
/// database; a proof written before that is refused by name.
///
/// <para>
/// <b>In plain language.</b> June 2026 is calculated. An administrator then publishes a different HK rule set (a
/// 32-hour week) dated Monday 1 June, the first day of the month just paid. Replaying June (re-running it from its
/// stored proof) must give back exactly the original answer: a replay that quietly used the newly published rules
/// would show a different month than the one that was paid. The other four facts craft an older proof (one that holds
/// no stored rules) and check that a replay refuses it by name, whichever of the two places the proof is read from
/// (the table row, or the event itself once the row is gone), before it asks the Rule Engine anything and while writing
/// nothing.
/// </para>
///
/// <para>
/// <b>Docker-gated: CI-pending, never locally green.</b> The fixture is the replay kit (<see cref="ReplayTestKit"/>)
/// over the full <c>init.sql</c> schema, so the seeded configurations, the real <see cref="ConfigResolutionService"/>
/// and <c>audit_log</c> exist. June 2026 opens on a Monday, so it has no context days and needs no previous month.
/// Product code runs between the readings: the forward calculation, the dated publish
/// (<see cref="AgreementConfigRepository.PublishAsync"/>, the repository the publish route calls, over the same
/// container) and the replay.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FrozenConfigReplayTests : IAsyncLifetime
{
    private const string Emp = "EMP-S148-FROZEN";
    private const string OrgId = "STY01";

    // 2026: 1 June is a Monday, 30 June a Tuesday.
    private static readonly DateOnly Jun1 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);

    private TestFixtures.DockerHarness _harness = null!;
    private HoursSeeder _seed = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
        _seed = new HoursSeeder(_harness);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ─────────────────────────────────────────────────────────────────────────
    // AC-C3a
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Kind N</b> (names <c>PlanInputs.ConfigRuns</c>, which TASK-14830 creates; CI, mutation <b>M-13</b> at CI-M5).
    /// An HK full-timer, 7.4 h on every weekday of June 2026 (OK26; the seeded HK/OK26 version, weekly norm 37 h, is
    /// published from <c>0001-01-01</c>). June is calculated forward on a service with the real configuration lookup.
    /// An HK/OK26 version with a weekly norm of <b>32</b> h is then published, dated Monday 1 June 2026. A replay of June
    /// by its manifest id, on a second service with the same real lookup, must give back the forward result byte for
    /// byte, and the timeline request it sends must carry <b>37</b> as June's piece's <c>config.weeklyNormHours</c>
    /// (the frozen value), never 32. Beyond that one setting, each piece's raw <c>config</c> object in the replay's
    /// request equals the forward request's as JSON text (every member, the decimal scale and the enum encoding,
    /// through PostgreSQL).
    ///
    /// <para>
    /// <b>NR-P22's condition.</b> The changed publish is dated later than the version it replaces (the seeded one, from
    /// <c>0001-01-01</c>) and on or before the replayed month's first day (1 June), so M-15's inverted same-date
    /// tie-break, run in the same CI-M5 batch, cannot hand the replay back the original and mask M-13. Non-vacuity: the
    /// lookup itself answers 32 for 1 June after the publish, and the forward request carried 37.
    /// </para>
    ///
    /// <para>
    /// <b>Red conditions.</b> <b>M-13</b> (in <c>FrozenEvaluationInputs</c>, each piece's config resolved through
    /// <c>IAgreementConfigLookup.ResolveAsync</c> as of the piece's first day with its frozen keys, as TASK-14827's
    /// interim code did) sends 32: the echo engine's <c>ECHO_CONFIG|2026-06-01|32</c> line differs from the original's
    /// <c>…|37</c>, so the byte comparison fails, and the request assertion reads 32. On the code before TASK-14830 the
    /// same replay is refused by the retired fingerprint check, so the fact cannot pass there either. Other facts expected
    /// red under M-13: none expected (the replay facts publish nothing between forward and replay).
    /// </para>
    /// </summary>
    [Fact]
    public async Task PublishChangedConfig_ReplayReproducesOriginal()
    {
        await _seed.WeekdaysAsync(Emp, Jun1, Jun30);
        var lookup = RealLookup();
        var approval = new FakeApprovalReader();
        var forwardEngine = new ReplayEchoRuleEngine();
        var replayEngine = new ReplayEchoRuleEngine();
        var pcs = BuildPcs(_harness, forwardEngine, approval, RealHoursReader(_harness), configLookup: lookup);
        var replayPcs = BuildPcs(_harness, replayEngine, approval, RealHoursReader(_harness), configLookup: lookup);

        var forward = await ForwardAsync(pcs, Emp, Jun1, Jun30);
        Assert.Equal(new[] { 37m }, PieceWeeklyNorms(forwardEngine)); // non-vacuity: the original was paid on 37

        await PublishHkOk26Async(weeklyNormHours: 32m, appliesFrom: Jun1);
        var nowInForce = await lookup.ResolveAsync(OrgId, "HK", Ok, null, Jun1);
        Assert.Equal(32m, nowInForce.WeeklyNormHours); // non-vacuity: anything that asks now hears 32

        var replay = await replayPcs.ReplayAsync(forward.ManifestId);

        Assert.True(replay.Success);
        Assert.Equal(Canonical(forward.Result, withManifestIds: true), Canonical(replay, withManifestIds: true));
        Assert.Equal(new[] { 37m }, PieceWeeklyNorms(replayEngine));

        // Every member of every piece's config, the decimal scale and the enum encoding included, through PostgreSQL:
        // the replay's raw `config` objects equal the forward's as JSON text, piece by piece (the weekly norm above is
        // one of 41 members).
        var forwardConfigs = PieceConfigs(forwardEngine);
        var replayConfigs = PieceConfigs(replayEngine);
        Assert.NotEmpty(forwardConfigs); // non-vacuity
        Assert.Equal(forwardConfigs.Count, replayConfigs.Count);
        for (var i = 0; i < forwardConfigs.Count; i++)
            Assert.Equal(forwardConfigs[i], replayConfigs[i]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-C3b — pieces carrying orgId (the shape the T14b stack wrote): the F facts, the contract names
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Kind F</b> (CI). AC-C3b, projection present. June is calculated; the row's <c>plan_inputs_jsonb</c> is then
    /// rewritten to the shape written between the T14b stack and TASK-14830: its pieces carry <c>orgId</c>, it holds
    /// the retired fingerprint list and no <c>configRuns</c>. The replay is refused <c>replay-manifest-without-frozen-inputs</c>,
    /// no request reaches the engine and nothing is written for this manifest or employee.
    ///
    /// <para>
    /// <b>Red conditions.</b> On the code before TASK-14830 <c>IsComplete</c> accepts a record with fingerprints and no
    /// configurations, and a replay re-resolves each piece's configuration (TASK-14827's interim code) and reaches the
    /// engine: the replay answers, or is refused by the retired fingerprint check, never the frozen-inputs reason, so the
    /// refusal assertion fails, and the "no engine request" assertion fails too.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PreStageManifest_ProjectionPresent_RefusedByName()
    {
        var (manifestId, _) = await CalculateJuneAsync();
        await RewriteProjectionAsync(manifestId, withOrgId: true);

        await AssertRefusedBeforeTheEngineWritingNothingAsync(manifestId);
    }

    /// <summary>
    /// <b>Kind F</b> (CI). AC-C3b, event only. As above, but the projection row is deleted and the stream's
    /// <c>SegmentManifestCreated</c> carries the pre-stage <c>planInputs</c> (pieces with <c>orgId</c>, fingerprints, no
    /// configurations), so the replay reads the event. Refused by the same name, through the same check: the new member
    /// is not <c>[JsonRequired]</c>, so the event's decode does not throw (which the store would read as "not found").
    ///
    /// <para>
    /// <b>Red conditions.</b> As the projection-present fact: before TASK-14830 the replay re-resolves and reaches the
    /// engine.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PreStageManifest_EventOnly_RefusedByName()
    {
        var (manifestId, _) = await CalculateJuneAsync();
        await RewriteEventOnlyAsync(manifestId, withOrgId: true);

        await AssertRefusedBeforeTheEngineWritingNothingAsync(manifestId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AC-C3b — pieces without orgId (a manifest written before S148): the R facts (ruling 19)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Kind R</b> (CI; a regression pin: refused by name since TASK-14827). As the F fact, but the pieces carry no
    /// <c>orgId</c> either: a manifest written before S148.
    ///
    /// <para>
    /// <b>Red conditions.</b> The fact needs <b>both</b> rules of <c>IsComplete</c> gone to go red: the pieces carry no
    /// organisation (the organisation rule) and the record holds no configurations (the configuration rule), and either
    /// rule alone still refuses it by name. On the base, TASK-14827's organisation rule refuses it, so the fact is green
    /// there (kind R); dropping the organisation rule alone leaves the configuration rule refusing it, so that mutation
    /// is not seen here but by the Unit fact <c>FrozenInputsRefusalTests.OrganisationRule_PieceAndEntryWithoutOrgId_RefusedByName</c>. Only a replay
    /// that reaches the engine, or is not refused by this name, fails the assertions (the reason, no engine request,
    /// nothing written).
    /// </para>
    /// </summary>
    [Fact]
    public async Task PreS148Manifest_ProjectionPresent_RefusedByName()
    {
        var (manifestId, _) = await CalculateJuneAsync();
        await RewriteProjectionAsync(manifestId, withOrgId: false);

        await AssertRefusedBeforeTheEngineWritingNothingAsync(manifestId);
    }

    /// <summary>
    /// <b>Kind R</b> (CI). As <see cref="PreS148Manifest_ProjectionPresent_RefusedByName"/>, with the projection row
    /// deleted and the pre-S148 <c>planInputs</c> on the event. Red conditions as there.
    /// </summary>
    [Fact]
    public async Task PreS148Manifest_EventOnly_RefusedByName()
    {
        var (manifestId, _) = await CalculateJuneAsync();
        await RewriteEventOnlyAsync(manifestId, withOrgId: false);

        await AssertRefusedBeforeTheEngineWritingNothingAsync(manifestId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────

    private ConfigResolutionService RealLookup() => new(
        new AgreementConfigRepository(_harness.Factory),
        new LocalConfigurationRepository(_harness.Factory),
        new PositionOverrideRepository(_harness.Factory),
        new LocalAgreementProfileRepository(_harness.Factory),
        NullLogger<ConfigResolutionService>.Instance);

    /// <summary>The weekly norm each piece of the engine's last timeline request carried, in piece order.</summary>
    private static IReadOnlyList<decimal> PieceWeeklyNorms(ReplayEchoRuleEngine engine)
    {
        var body = engine.Requests.Last(r => r.Path.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal)).Body;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("pieces").EnumerateArray()
            .Select(p => p.GetProperty("config").GetProperty("weeklyNormHours").GetDecimal())
            .ToList();
    }

    /// <summary>The raw <c>config</c> object of each piece of the engine's last timeline request, as JSON text, in piece order.</summary>
    private static IReadOnlyList<string> PieceConfigs(ReplayEchoRuleEngine engine)
    {
        var body = engine.Requests.Last(r => r.Path.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal)).Body;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("pieces").EnumerateArray()
            .Select(p => p.GetProperty("config").GetRawText())
            .ToList();
    }

    /// <summary>June 2026 calculated forward on the echo engine; returns the manifest id and the forward result.</summary>
    private async Task<(Guid ManifestId, PeriodCalculationResult Forward)> CalculateJuneAsync()
    {
        await _seed.WeekdaysAsync(Emp, Jun1, Jun30);
        var pcs = BuildPcs(_harness, new ReplayEchoRuleEngine(), new FakeApprovalReader(), RealHoursReader(_harness));
        var forward = await ForwardAsync(pcs, Emp, Jun1, Jun30);
        return (forward.ManifestId, forward.Result);
    }

    /// <summary>
    /// The pre-stage shape of a <c>planInputs</c> object: <c>configRuns</c> removed, the retired fingerprint list present
    /// (a legacy fixture property), and, for a manifest written before S148, no <c>orgId</c> on any piece.
    /// </summary>
    private static string PreStagePlanInputs(JsonNode planInputs, bool withOrgId)
    {
        var node = planInputs.DeepClone().AsObject();
        node.Remove("configRuns"); // present after S148, never written before it
        Assert.Null(node["configRuns"]); // the fixture shape holds none, whichever code wrote the forward manifest
        node["configFingerprints"] = new JsonArray("stub");
        foreach (var piece in node["pieces"]!.AsArray())
        {
            Assert.NotNull(piece!["orgId"]); // non-vacuity: the forward pieces carried the organisation
            if (!withOrgId) Assert.True(piece.AsObject().Remove("orgId"));
        }
        return node.ToJsonString();
    }

    private async Task RewriteProjectionAsync(Guid manifestId, bool withOrgId)
    {
        var text = await ReadPlanInputsAsync(_harness, manifestId);
        var rewritten = PreStagePlanInputs(JsonNode.Parse(text!)!, withOrgId);
        await ExecAsync(_harness,
            "UPDATE segment_manifests SET plan_inputs_jsonb = @p::jsonb WHERE manifest_id = @id",
            ("p", rewritten), ("id", manifestId));
    }

    private async Task RewriteEventOnlyAsync(Guid manifestId, bool withOrgId)
    {
        // The manifest event rides the outbox; the drain puts it on `events`, which is what the replay reads once the row is gone.
        await PayrollOutboxDrain.DrainAsync(_harness.Factory);
        var stream = $"segment-manifest-{manifestId}";
        var text = await ScalarAsync<string>(
            "SELECT data::text FROM events WHERE stream_id = @s AND event_type = 'SegmentManifestCreated'", ("s", stream));
        var evt = JsonNode.Parse(text)!.AsObject();
        evt["planInputs"] = JsonNode.Parse(PreStagePlanInputs(evt["planInputs"]!, withOrgId));
        await ExecAsync(_harness,
            "UPDATE events SET data = @d::jsonb WHERE stream_id = @s AND event_type = 'SegmentManifestCreated'",
            ("d", evt.ToJsonString()), ("s", stream));
        await ExecAsync(_harness, "DELETE FROM segment_manifests WHERE manifest_id = @id", ("id", manifestId));
    }

    private async Task AssertRefusedBeforeTheEngineWritingNothingAsync(Guid manifestId)
    {
        var replayEngine = new ReplayEchoRuleEngine();
        var replayPcs = BuildPcs(_harness, replayEngine, new FakeApprovalReader(), RealHoursReader(_harness));
        var before = await CountsAsync(manifestId);

        await AssertRefusedAsync("replay-manifest-without-frozen-inputs", () => replayPcs.ReplayAsync(manifestId));

        Assert.Empty(replayEngine.Requests);
        Assert.Equal(before, await CountsAsync(manifestId));
    }

    /// <summary>Rows for this manifest and employee in the tables a replay could write, never an unfiltered count.</summary>
    private async Task<(long Manifests, long Outbox, long Events, long Audit)> CountsAsync(Guid manifestId) => (
        await ScalarAsync<long>("SELECT count(*) FROM segment_manifests WHERE manifest_id = @id OR employee_id = @e",
            ("id", manifestId), ("e", Emp)),
        await ScalarAsync<long>("SELECT count(*) FROM outbox_events WHERE stream_id = @s OR event_payload->>'employeeId' = @e",
            ("s", $"segment-manifest-{manifestId}"), ("e", Emp)),
        await ScalarAsync<long>("SELECT count(*) FROM events WHERE stream_id = @s OR data->>'employeeId' = @e",
            ("s", $"segment-manifest-{manifestId}"), ("e", Emp)),
        await ScalarAsync<long>("SELECT count(*) FROM audit_log WHERE resource_id = @m OR details::text LIKE '%' || @e || '%'",
            ("m", manifestId.ToString()), ("e", Emp)));

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] args)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): every caller passes a compile-time test constant;
        // values travel as parameters, never as text.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        var scalar = await cmd.ExecuteScalarAsync();
        Assert.NotNull(scalar);
        return (T)scalar!;
    }

    /// <summary>
    /// Publishes an HK/OK26 version dated <paramref name="appliesFrom"/> through the repository's dated publish, with the
    /// seeded version's values copied and the weekly norm changed (a Monday, because the weekly norm is a Monday-only
    /// field, ruling 6).
    /// </summary>
    private async Task PublishHkOk26Async(decimal weeklyNormHours, DateOnly appliesFrom)
    {
        var repo = new AgreementConfigRepository(_harness.Factory);
        var seed = await repo.GetActiveAsync("HK", Ok)
            ?? throw new InvalidOperationException("HK/OK26 has no seeded ACTIVE row to copy the values from");
        var draftId = await repo.CreateAsync(new AgreementConfigEntity
        {
            ConfigId = Guid.Empty,
            AgreementCode = "HK",
            OkVersion = Ok,
            Status = AgreementConfigStatus.DRAFT,
            WeeklyNormHours = weeklyNormHours,
            NormPeriodWeeks = seed.NormPeriodWeeks,
            NormModel = seed.NormModel,
            AnnualNormHours = seed.AnnualNormHours,
            MaxFlexBalance = seed.MaxFlexBalance,
            HasOvertime = seed.HasOvertime,
            HasMerarbejde = seed.HasMerarbejde,
            OvertimeThreshold50 = seed.OvertimeThreshold50,
            OvertimeThreshold100 = seed.OvertimeThreshold100,
            EveningSupplementEnabled = seed.EveningSupplementEnabled,
            NightSupplementEnabled = seed.NightSupplementEnabled,
            WeekendSupplementEnabled = seed.WeekendSupplementEnabled,
            HolidaySupplementEnabled = seed.HolidaySupplementEnabled,
            EveningStart = seed.EveningStart,
            EveningEnd = seed.EveningEnd,
            NightStart = seed.NightStart,
            NightEnd = seed.NightEnd,
            EveningRate = seed.EveningRate,
            NightRate = seed.NightRate,
            WeekendSaturdayRate = seed.WeekendSaturdayRate,
            WeekendSundayRate = seed.WeekendSundayRate,
            HolidayRate = seed.HolidayRate,
            OnCallDutyEnabled = seed.OnCallDutyEnabled,
            OnCallDutyRate = seed.OnCallDutyRate,
            CallInWorkEnabled = seed.CallInWorkEnabled,
            CallInMinimumHours = seed.CallInMinimumHours,
            CallInRate = seed.CallInRate,
            TravelTimeEnabled = seed.TravelTimeEnabled,
            WorkingTravelRate = seed.WorkingTravelRate,
            NonWorkingTravelRate = seed.NonWorkingTravelRate,
            MaxDailyHours = seed.MaxDailyHours,
            MinimumRestHours = seed.MinimumRestHours,
            RestPeriodDerogationAllowed = seed.RestPeriodDerogationAllowed,
            WeeklyMaxHoursReferencePeriod = seed.WeeklyMaxHoursReferencePeriod,
            VoluntaryUnsocialHoursAllowed = seed.VoluntaryUnsocialHoursAllowed,
            DefaultCompensationModel = seed.DefaultCompensationModel,
            EmployeeCompensationChoice = seed.EmployeeCompensationChoice,
            MaxOvertimeHoursPerPeriod = seed.MaxOvertimeHoursPerPeriod,
            OvertimeRequiresPreApproval = seed.OvertimeRequiresPreApproval,
            CreatedBy = "tester",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Description = "S148 TASK-14830-T: a changed publish dated into the replayed month",
        });
        var draft = await repo.GetByIdAsync(draftId) ?? throw new InvalidOperationException("draft vanished");
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await repo.PublishAsync(conn, tx, draftId, draft.Version, appliesFrom, "tester");
        await tx.CommitAsync();
    }
}
