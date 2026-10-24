using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// S146 / TASK-14604-T (QUAL-205; rulings R-21, R-22; AC-A2) — the manifest travels through the ADR-018
/// transactional outbox, and the publisher delivers it to <c>events</c> with the SAME event id.
///
/// <para>
/// <b>Plain language.</b> A calculation's proof (the segment manifest) is no longer appended straight to the
/// canonical event log. It is written to an outbox row in the same database transaction as the manifest's
/// table row, and a background publisher copies the row into the event log a moment later (ADR-018 D3/D4). The
/// copy must be the SAME event: the outbox row's event id is the key that lets the publisher prove, after a
/// crash, that it has already delivered it. This fact runs a calculation, drains Payroll's outbox with the real
/// publisher (<see cref="PayrollOutboxDrain"/>, the <c>OutboxPublisherTests.WaitForOutboxDrainedAsync</c>
/// idiom), and checks that the manifest event is in <c>events</c> on its own stream with the outbox row's event
/// id, and that nothing reached <c>events</c> before the drain.
/// </para>
///
/// <para>
/// <b>Docker-gated: CI-pending, never locally green.</b> Kind <b>F</b>.
/// <b>Red conditions:</b> before TASK-14604 the manifest is appended straight to <c>events</c> and no
/// <c>outbox_events</c> row exists on <c>segment-manifest-{id}</c>, so the first assertion (exactly one
/// outbox row, and zero <c>events</c> rows before the drain) fails. Mutation after the fix: enqueue the manifest
/// event with a freshly generated event id instead of the event's own, so the drained row's id differs from the
/// outbox row's: the same-id assertion fails.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ManifestOutboxDrainTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TestFixtures.DockerHarness.StartAsync();
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ManifestOutboxRow_DrainsToEvents_SameEventId()
    {
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);

        const string employeeId = "EMP-S146-DRAIN";
        var profile = TestFixtures.Profile(employeeId);
        var start = new DateOnly(2026, 3, 25);
        var end = new DateOnly(2026, 4, 7);
        var pcs = TestFixtures.BuildPcs(
            _harness.Factory, _harness.EventStore,
            hoursReader: new InMemoryRecordedHoursReader(TestFixtures.WeekdayEntriesForPeriod(employeeId, start, end)));
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

        var manifestStream = $"segment-manifest-{plan.ManifestId}";

        // Before the drain: the manifest is on the outbox exactly once and NOT yet in `events`.
        var outboxEventId = await ReadSingleOutboxEventIdAsync(manifestStream);
        Assert.Equal(0, await CountEventsAsync(manifestStream));

        await PayrollOutboxDrain.DrainAsync(_harness.Factory);

        // After the drain: one `events` row on the manifest's own stream, with the outbox row's event id.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT event_id, event_type, data::text FROM events WHERE stream_id = @s", conn);
        cmd.Parameters.AddWithValue("s", manifestStream);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "the drained manifest event is in `events`");
        Assert.Equal(outboxEventId, reader.GetGuid(0));
        Assert.Equal("SegmentManifestCreated", reader.GetString(1));
        var drained = EventSerializer.Deserialize(reader.GetString(1), reader.GetString(2));
        Assert.Equal(plan.ManifestId, Assert.IsType<SegmentManifestCreated>(drained).ManifestId);
        Assert.False(await reader.ReadAsync(), "exactly one manifest event in `events`");
    }

    private async Task<Guid> ReadSingleOutboxEventIdAsync(string streamId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT event_id FROM outbox_events WHERE stream_id = @s AND service_id = 'payroll'", conn);
        cmd.Parameters.AddWithValue("s", streamId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "the calculation enqueued its manifest on the Payroll outbox");
        var id = reader.GetGuid(0);
        Assert.False(await reader.ReadAsync(), "exactly one outbox row on the manifest's stream");
        return id;
    }

    private async Task<long> CountEventsAsync(string streamId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM events WHERE stream_id = @s", conn);
        cmd.Parameters.AddWithValue("s", streamId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
