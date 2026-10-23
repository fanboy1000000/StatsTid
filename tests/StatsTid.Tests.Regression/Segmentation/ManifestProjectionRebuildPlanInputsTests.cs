using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Segmentation;
using static StatsTid.Tests.Regression.Segmentation.ReplayTestKit;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// S145 / TASK-14515 (R47) — a projection rebuild keeps the frozen inputs.
///
/// <para>
/// <b>Plain language.</b> The manifest table is a read-model that can be thrown away and rebuilt
/// from the immutable events (ADR-016 D10). A calculation's frozen inputs live in the manifest's
/// <c>plan_inputs_jsonb</c> slot. If the rebuild forgot that slot, every manifest rebuilt after an
/// ops action would silently become unreplayable: the payslip could no longer be proven. This is a
/// NEW file (R50) because <c>ManifestProjectionRebuildTests</c> is edited by an open-wave paired
/// pass; that existing test reads only <c>segments_jsonb</c>
/// (<c>ManifestProjectionRebuildTests.cs:113-119</c>), so it could not see this loss.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ManifestProjectionRebuildPlanInputsTests : IAsyncLifetime
{
    private const string Emp = "EMP-RPL-REBUILD";

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await PrepareDatabaseAsync(_harness);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// <b>RB (R47).</b> September is calculated with non-empty frozen inputs (pieces and recorded
    /// hours ids). The manifest projection is then truncated and rebuilt from events through
    /// <see cref="SegmentManifestProjectionRebuilder"/>. The rebuilt <c>plan_inputs_jsonb</c> must
    /// equal the original <see cref="PlanInputs"/> member by member, and a replay by manifest id on
    /// the rebuilt manifest must succeed and be byte-identical to the original calculation.
    ///
    /// <para><b>Expected, derived from the spec.</b> Refinement A7 "Where it lives": the event is
    /// the record of truth "because the projection is rebuildable from events (ADR-016:139)"; "the
    /// projection insert, the load and <c>SegmentManifestProjectionRebuilder.cs</c> all carry it";
    /// TASK-14513 Do 4 / TASK-14509 Do 3 "carries <c>plan_inputs_jsonb</c> through a projection
    /// rebuild". Equality is compared member by member (the record's list members compare by
    /// reference); the live and the rebuilt row may differ in null encoding (the QUAL-002 residual),
    /// so both are deserialized with the production options before comparing. The two non-empty
    /// preconditions keep the equality from passing on two empty records.</para>
    ///
    /// <para><b>Red conditions.</b> MD-51 (the rebuilder omits <c>plan_inputs_jsonb</c>): the rebuilt
    /// slot is NULL, so the slot assertion fails, and a replay would refuse
    /// <c>replay-manifest-without-frozen-inputs</c>. Not MD-35: its null-slot fallback is a different
    /// path, and under it this fact is green because the slot is present unless MD-51.</para>
    /// </summary>
    [Fact]
    public async Task Rebuild_PreservesPlanInputs_ReplaySucceeds()
    {
        var approval = new FakeApprovalReader(); // S145 R26: M-1 treated as APPROVED
        var pcs = BuildPcs(_harness, new ReplayEchoRuleEngine(), approval, RealHoursReader(_harness));
        var replayPcs = BuildPcs(_harness, new ReplayEchoRuleEngine(), approval, RealHoursReader(_harness));

        var seed = new HoursSeeder(_harness);
        await seed.WeekdaysAsync(Emp, new DateOnly(2026, 8, 31), new DateOnly(2026, 9, 30));
        var forward = await ForwardAsync(pcs, Emp, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        var originalText = await ReadPlanInputsAsync(_harness, forward.ManifestId);
        Assert.NotNull(originalText);
        var original = Deserialize(originalText!);
        Assert.NotEmpty(original.Pieces);
        Assert.NotEmpty(original.EntryEventIds);

        // S146 R-24: the rebuild reads the manifest from events; drain Payroll's outbox partition first
        // (a no-op until the manifest rides the outbox).
        await TestSupport.PayrollOutboxDrain.DrainAsync(_harness.Factory);

        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var truncate = new NpgsqlCommand("TRUNCATE TABLE segment_manifests", conn);
            await truncate.ExecuteNonQueryAsync();
        }

        var rebuiltCount = await SegmentManifestProjectionRebuilder.RebuildAsync(_harness.Factory, NullLogger.Instance);
        Assert.True(rebuiltCount >= 1, $"Rebuild reported {rebuiltCount} rows; expected >= 1.");

        var rebuiltText = await ReadPlanInputsAsync(_harness, forward.ManifestId);
        Assert.NotNull(rebuiltText);
        AssertPlanInputsEqual(original, Deserialize(rebuiltText!));

        var replay = await replayPcs.ReplayAsync(forward.ManifestId);
        Assert.True(replay.Success);
        Assert.Equal(Canonical(forward.Result, withManifestIds: true), Canonical(replay, withManifestIds: true));
    }

    private static PlanInputs Deserialize(string json) =>
        JsonSerializer.Deserialize<PlanInputs>(json, TestFixtures.ManifestReadOptions)
        ?? throw new InvalidOperationException("plan_inputs_jsonb deserialized to null");

    private static void AssertPlanInputsEqual(PlanInputs expected, PlanInputs actual)
    {
        Assert.Equal(expected.Pieces, actual.Pieces);
        Assert.Equal(expected.SettlementCutoff, actual.SettlementCutoff);
        Assert.Equal(expected.OpeningFlexBalance, actual.OpeningFlexBalance);
        Assert.Equal(expected.EntryEventIds, actual.EntryEventIds);
        Assert.Equal(expected.AbsenceEventIds, actual.AbsenceEventIds);
        Assert.Equal(expected.AsOfOutboxId, actual.AsOfOutboxId);
        Assert.Equal(expected.EntriesDigest, actual.EntriesDigest);
        Assert.Equal(expected.AbsencesDigest, actual.AbsencesDigest);
        Assert.Equal(expected.ConfigFingerprints, actual.ConfigFingerprints);
        Assert.Equal(expected.EvaluationModel, actual.EvaluationModel);
    }
}
