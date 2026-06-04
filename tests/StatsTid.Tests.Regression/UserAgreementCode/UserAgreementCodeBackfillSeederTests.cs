using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.UserAgreementCode;

/// <summary>
/// S34 / TASK-3414 — Idempotency D-test for
/// <see cref="UserAgreementCodeBackfillSeeder.SeedAsync"/>. The seeder bootstraps
/// per-user backfill on first boot by inserting a live
/// <c>user_agreement_codes</c> row at <c>effective_from='0001-01-01'</c> for
/// every active user without a covering row.
///
/// <para>
/// <b>Idempotency contract</b>: a second invocation of <c>SeedAsync</c> against
/// a database whose users already have a backfilled live row MUST be a no-op
/// (no duplicate rows, no duplicate audit rows, no duplicate
/// <c>UserAgreementCodeSeeded</c> outbox events). The NOT-EXISTS predicate in
/// the seeder's missing-users SQL drives this; the concurrent-startup race
/// inline catch (PostgresException SqlState=23505 on
/// <c>idx_user_agreement_codes_live</c>) is the belt-and-braces safety net for
/// the racing-startup edge case (also asserted via the
/// <c>skippedRace</c> counter in the seeder's log line).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class UserAgreementCodeBackfillSeederTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;
    private StatsTidWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new StatsTidWebApplicationFactory(_harness.ConnectionString);
        // First CreateClient triggers Program.cs host build → first SeedAsync run. The seeder
        // is awaited synchronously in Program.cs startup (BEFORE app.Run), so the backfilled
        // rows are committed by the time CreateClient returns — no drain-await needed.
        _ = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// First boot ran the seeder at WAF startup — every active init.sql-seeded
    /// user now has a live <c>user_agreement_codes</c> row at
    /// <c>effective_from='0001-01-01'</c>. A second explicit <c>SeedAsync</c>
    /// invocation must be a no-op: the row count, audit count, and outbox
    /// event count for <c>UserAgreementCodeSeeded</c> on each user's stream
    /// must NOT increase.
    /// </summary>
    [Fact]
    public async Task Backfill_SecondStartupSkipsAlreadyBackfilledUsers_Idempotent()
    {
        // Snapshot the post-first-boot counts so we can pin "unchanged after
        // second invocation" — not "exactly N rows" (init.sql seeds 7 users:
        // admin01/ladm01/hr01/mgr01/emp001-003 — but counts are coupled to
        // init.sql contents which may evolve, so snapshotting is more robust).
        long firstBootRows;
        long firstBootAuditRows;
        long firstBootSeededEvents;
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            // S64 F4-6 — the seeder writes effective_from = new DateOnly(1, 1, 1) (UNCHANGED
            // since S34 65d4889 — verified per the ruling). Npgsql's infinity-conversion maps
            // DateOnly.MinValue (0001-01-01) to Postgres DATE '-infinity', so the stored value
            // is '-infinity', NOT the literal '0001-01-01'. The original query used the finite
            // literal and matched 0 rows (defect — never the seeder). Match the stored anchor.
            firstBootRows = await CountAsync(conn,
                "SELECT COUNT(*) FROM user_agreement_codes WHERE effective_from = DATE '-infinity' AND effective_to IS NULL");
            firstBootAuditRows = await CountAsync(conn,
                "SELECT COUNT(*) FROM user_agreement_codes_audit WHERE action = 'CREATED' AND actor_id = 'SYSTEM_SEED'");
            firstBootSeededEvents = await CountAsync(conn,
                "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'UserAgreementCodeSeeded'");
        }

        Assert.True(firstBootRows > 0,
            "First boot's backfill seeder must have inserted at least one user_agreement_codes row.");
        Assert.Equal(firstBootRows, firstBootAuditRows);
        Assert.Equal(firstBootRows, firstBootSeededEvents);

        // Second explicit SeedAsync invocation — must be a no-op.
        await UserAgreementCodeBackfillSeeder.SeedAsync(
            _harness.Factory,
            _harness.EventStore,
            NullLogger.Instance);

        // Re-count: unchanged.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            // S64 F4-6 — stored anchor is DATE '-infinity' (Npgsql DateOnly.MinValue mapping);
            // see the firstBootRows query above.
            var secondBootRows = await CountAsync(conn,
                "SELECT COUNT(*) FROM user_agreement_codes WHERE effective_from = DATE '-infinity' AND effective_to IS NULL");
            var secondBootAuditRows = await CountAsync(conn,
                "SELECT COUNT(*) FROM user_agreement_codes_audit WHERE action = 'CREATED' AND actor_id = 'SYSTEM_SEED'");
            var secondBootSeededEvents = await CountAsync(conn,
                "SELECT COUNT(*) FROM outbox_events WHERE event_type = 'UserAgreementCodeSeeded'");

            Assert.Equal(firstBootRows, secondBootRows);
            Assert.Equal(firstBootAuditRows, secondBootAuditRows);
            Assert.Equal(firstBootSeededEvents, secondBootSeededEvents);
        }
    }

    private static async Task<long> CountAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}
