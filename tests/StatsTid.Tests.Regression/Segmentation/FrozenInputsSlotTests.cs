using System.Text.Json;
using Npgsql;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// S145 / TASK-14513 (adjudicator R-17, R-18, R-22) — what a replay does when the stored frozen-inputs slot of a
/// manifest is damaged, missing a member, or absent altogether.
///
/// <para>
/// Plain language: every calculation stores the inputs it used (the "frozen inputs") in two places: on its
/// immutable event (the record of truth) and in a rebuildable table row (the projection). A replay reads the row
/// first. If the row is simply not there (or the table cannot be reached) the replay may use the event instead.
/// But if the row IS there and its frozen-inputs column is damaged, the replay must say so by name and refuse:
/// quietly swapping in the event would hide the damage, and a damaged row is repaired by a projection rebuild,
/// not papered over. A column that lost one required member (for example the opening flex balance) is damaged
/// too: reading the missing number as 0 would replay a different calculation than the one that was paid.
/// </para>
///
/// <para>
/// <b>Docker-gated: CI-verified, not locally green.</b> Each fact states its red conditions: the production
/// mutation (on <c>PeriodCalculationService.LoadManifestAsync</c>, S145 cycle-2 shape) and the assertion it trips.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FrozenInputsSlotTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TestFixtures.DockerHarness.StartAsync();
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// <b>R-17.</b> The row's <c>plan_inputs_jsonb</c> is the wrong shape (<c>[]</c>), while the event's own
    /// <c>PlanInputs</c> is intact. The replay must refuse <c>replay-manifest-without-frozen-inputs</c>; it must
    /// not fall back to the intact event and succeed.
    ///
    /// <para>
    /// <b>Red conditions.</b> Today (<c>b25bf24</c>) the projection read sits inside one
    /// <c>try { ... } catch (Exception)</c>; the <c>JsonException</c> from decoding <c>[]</c> is swallowed, the
    /// event fallback supplies the intact inputs, and the replay SUCCEEDS, so
    /// <c>Assert.ThrowsAsync&lt;ReplayRefusedException&gt;</c> fails with "No exception was thrown". Mutation
    /// after the fix-up: widen the catch again so it also wraps the decode of <c>plan_inputs_jsonb</c>; the same
    /// assertion trips.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Replay_PlanInputsSlotWrongShape_RefusesWithoutFrozenInputs_DespiteIntactEvent()
    {
        // S145 R-17:
        var (pcs, manifestId, _) = await ForwardCalcAsync("EMP-FIS-SHAPE");

        await ExecuteAsync(
            "UPDATE segment_manifests SET plan_inputs_jsonb = '[]'::jsonb WHERE manifest_id = @id", manifestId);

        // The "despite": the event still carries complete frozen inputs. S146 R-22: the manifest event rides the
        // outbox, so it is read from outbox_events; the drain puts the same event on `events` too, which is what
        // the replay's event fallback would read, so the refusal below is not vacuous (a replay that substituted
        // the event would find it).
        await PayrollOutboxDrain.DrainAsync(_harness.Factory);
        var created = await ReadManifestEventAsync(manifestId);
        Assert.NotNull(created.PlanInputs);
        Assert.Single((await _harness.EventStore.ReadStreamAsync($"segment-manifest-{manifestId}")).OfType<SegmentManifestCreated>());

        var refused = await Assert.ThrowsAsync<ReplayRefusedException>(() => pcs.ReplayAsync(manifestId));
        Assert.Equal("replay-manifest-without-frozen-inputs", refused.Reason);
        Assert.Equal(manifestId, refused.ManifestId);
    }

    /// <summary>
    /// <b>R-17 guard.</b> The row's <c>segments_jsonb</c> is damaged (<c>{}</c>, not an array). The segments are
    /// the plan, not frozen inputs, so this is not a named refusal: it fails as a damaged row
    /// (<see cref="InvalidOperationException"/>), and it must not succeed through the event.
    ///
    /// <para>
    /// <b>Red conditions.</b> It fails today (the swallow-and-fall-back succeeds, so "No exception was thrown"),
    /// and after the fix-up it fails under the mutation "leave the decode of <c>segments_jsonb</c> inside the
    /// catch-all, so a damaged row reads as the projection being unreachable".
    /// </para>
    /// </summary>
    [Fact]
    public async Task Replay_SegmentsSlotMalformed_FailsAsDamagedRow_NotEventFallback()
    {
        // S145 R-17:
        var (pcs, manifestId, _) = await ForwardCalcAsync("EMP-FIS-SEGMENTS");

        await ExecuteAsync(
            "UPDATE segment_manifests SET segments_jsonb = '{}'::jsonb WHERE manifest_id = @id", manifestId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => pcs.ReplayAsync(manifestId));
    }

    /// <summary>
    /// <b>R-18.</b> The stored frozen inputs lost <c>openingFlexBalance</c> (a required member). The replay must
    /// refuse by name; it must not read the missing number as 0 and replay a different calculation.
    ///
    /// <para>
    /// <b>S146 R-14 (the seeding changed; the fact's assertions did not).</b> The forward calculation no longer takes an opening from its caller (this
    /// fact used to pass 12.5): payroll derives it from the ledger. The helper seeds the previous month with a
    /// recorded closing of 12.5, so the month opens CHAINED at 12.5 (R-14 case 4a) and the frozen opening is again
    /// a distinct non-zero value: a replay that read the missing key as 0 would be a wrong, silent substitute.
    /// The fact's assertion is the refusal by name (R-37, reviewer c2 N3).
    /// </para>
    ///
    /// <para>
    /// <b>Red conditions.</b> Today <c>PlanInputs.OpeningFlexBalance</c> is a plain <c>decimal</c> with no
    /// required-member marker, so the missing key deserializes to 0 and the replay succeeds on balance 0:
    /// <c>ThrowsAsync</c> fails with "No exception was thrown". Mutation after the fix-up: drop
    /// <c>[property: JsonRequired]</c> from <c>PlanInputs.OpeningFlexBalance</c>; the same assertion trips.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Replay_PlanInputsSlotMissingOpeningBalance_RefusesWithoutFrozenInputs()
    {
        // S145 R-18:
        var (pcs, manifestId, _) = await ForwardCalcAsync("EMP-FIS-OPENING");

        await ExecuteAsync(
            "UPDATE segment_manifests SET plan_inputs_jsonb = plan_inputs_jsonb - 'openingFlexBalance' WHERE manifest_id = @id",
            manifestId);

        var refused = await Assert.ThrowsAsync<ReplayRefusedException>(() => pcs.ReplayAsync(manifestId));
        Assert.Equal("replay-manifest-without-frozen-inputs", refused.Reason);
        Assert.Equal(manifestId, refused.ManifestId);
    }

    /// <summary>
    /// <b>R-22 guard (green today).</b> When the projection row is simply ABSENT, the replay loads the frozen
    /// inputs from the immutable event and succeeds, with the same rule results as the forward calculation. This
    /// is the other half of R-17: "no row" falls back to the event, "a damaged row" does not.
    ///
    /// <para>
    /// <b>Mutation (it must fail under it).</b> Drop <c>PlanInputs: created.PlanInputs</c> from the event
    /// fallback's <c>SegmentManifest</c> in <c>LoadManifestAsync</c>: the manifest then carries no frozen inputs
    /// and the replay is refused <c>replay-manifest-without-frozen-inputs</c>, so <c>ReplayAsync</c> throws
    /// instead of returning.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Replay_ProjectionRowAbsent_LoadsFrozenInputsFromEvent_Succeeds()
    {
        // S145 R-22:
        var (pcs, manifestId, forward) = await ForwardCalcAsync("EMP-FIS-ABSENT");

        // S146 R-24: the replay falls back to the events copy once the projection row is gone; drain
        // Payroll's outbox partition first so that copy exists (a no-op until the manifest rides the outbox).
        await PayrollOutboxDrain.DrainAsync(_harness.Factory);

        await ExecuteAsync("DELETE FROM segment_manifests WHERE manifest_id = @id", manifestId);

        var replay = await pcs.ReplayAsync(manifestId);

        Assert.True(replay.Success, replay.ErrorMessage);
        Assert.NotEmpty(forward.RuleResults); // the equality below must compare something (Step 5a cycle 2, Fable NOTE 4)
        Assert.Equal(
            JsonSerializer.Serialize(forward.RuleResults),
            JsonSerializer.Serialize(replay.RuleResults));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<(PeriodCalculationService Pcs, Guid ManifestId, PeriodCalculationResult Forward)> ForwardCalcAsync(
        string employeeId)
    {
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);

        var profile = TestFixtures.Profile(employeeId);
        var start = new DateOnly(2026, 3, 25);
        var end = new DateOnly(2026, 4, 7);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, start, end);
        var pcs = TestFixtures.BuildPcs(
            _harness.Factory, _harness.EventStore,
            hoursReader: new InMemoryRecordedHoursReader(entries),
            // S146 R-14: an employed February (implicit, no resolver) closed at 12.5, so March opens CHAINED at 12.5.
            flexLedgerReader: InMemoryFlexLedgerReader.ClosedAt(start, 12.5m));

        var plan = PeriodPlanner.Plan(
            employeeId: employeeId,
            periodStart: start,
            periodEnd: end,
            calculationKind: "forward-calc",
            ruleSet: TestFixtures.StraddleSafeRuleSet,
            sources: TestFixtures.OkStraddleSources(),
            options: PlannerOptions.Default,
            enrollment: TestFixtures.StraddleEnrollment(),
            profile: profile);

        var forward = await pcs.CalculateAsync(plan, profile);
        Assert.True(forward.Success, forward.ErrorMessage);
        return (pcs, plan.ManifestId, forward);
    }

    private async Task ExecuteAsync(string sql, Guid manifestId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        // CA2100-justified (QUAL-073 ratchet): every caller passes a compile-time test constant;
        // the manifest id travels as a parameter, never as text.
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        cmd.Parameters.AddWithValue("id", manifestId);
        var rows = await cmd.ExecuteNonQueryAsync();
        Assert.Equal(1, rows);
    }

    /// <summary>S146 R-22: the manifest event as the calculation enqueued it, decoded from its `outbox_events` row
    /// through <see cref="StatsTid.Infrastructure.EventSerializer"/> (a calculation no longer appends to `events`).</summary>
    private async Task<SegmentManifestCreated> ReadManifestEventAsync(Guid manifestId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT event_type, event_payload::text FROM outbox_events WHERE stream_id = @s", conn);
        cmd.Parameters.AddWithValue("s", $"segment-manifest-{manifestId}");
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "non-vacuity: the manifest event is on the outbox");
        var decoded = StatsTid.Infrastructure.EventSerializer.Deserialize(reader.GetString(0), reader.GetString(1));
        Assert.False(await reader.ReadAsync(), "exactly one manifest event on the outbox");
        return Assert.IsType<SegmentManifestCreated>(decoded);
    }
}
