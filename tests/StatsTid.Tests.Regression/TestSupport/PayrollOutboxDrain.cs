using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;

namespace StatsTid.Tests.Regression.TestSupport;

/// <summary>
/// S146 / TASK-14609 (R-24) — drains Payroll's partition of <c>outbox_events</c> into <c>events</c> for a
/// test that builds a <c>PeriodCalculationService</c> directly.
///
/// <para><b>Plain language.</b> In S146 the calculation's proof (the segment manifest) is written to the
/// transactional outbox in one transaction, and a background publisher copies it into the canonical
/// <c>events</c> table a moment later. A test host runs no publisher, so a fact that rebuilds or replays
/// the manifest "from <c>events</c>" straight after a calculation would find nothing there. This helper
/// is the publisher, run on demand: it starts the real <see cref="OutboxPublisher"/> for the
/// <c>"payroll"</c> partition, waits until no unpublished Payroll row is left, and stops it. Until the
/// manifest is on the outbox there is nothing to drain and the call returns at once.</para>
///
/// <para>Idiom: the same start / poll / stop shape as <c>OutboxPublisherTests.WaitForOutboxDrainedAsync</c>
/// and <c>SettlementEmitterFixture.ProcessOnceAsync</c>. The publisher drains only its own
/// <c>service_id</c> partition, so rows of other services are neither touched nor waited for.</para>
/// </summary>
public static class PayrollOutboxDrain
{
    /// <summary>Payroll's service id (<c>Program.cs</c> registers <c>OutboxServiceContext("payroll")</c>).</summary>
    public const string ServiceId = "payroll";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Starts a Payroll <see cref="OutboxPublisher"/>, waits until no <c>outbox_events</c> row with
    /// <c>service_id = 'payroll'</c> is unpublished, then stops it. Throws <see cref="TimeoutException"/>
    /// when rows are still unpublished after <paramref name="timeout"/> (loud, never a silent partial drain).
    /// </summary>
    public static async Task DrainAsync(DbConnectionFactory factory, TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(15);
        using var publisher = new OutboxPublisher(
            factory, new OutboxServiceContext(ServiceId), NullLogger<OutboxPublisher>.Instance);

        await publisher.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow + limit;
            while (true)
            {
                var remaining = await CountUnpublishedAsync(factory);
                if (remaining == 0)
                    return;

                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException(
                        $"PayrollOutboxDrain: {remaining} unpublished '{ServiceId}' outbox row(s) remained after {limit}.");

                await Task.Delay(PollInterval);
            }
        }
        finally
        {
            await publisher.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<long> CountUnpublishedAsync(DbConnectionFactory factory)
    {
        await using var conn = factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE service_id = @serviceId AND published_at IS NULL", conn);
        cmd.Parameters.AddWithValue("serviceId", ServiceId);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
