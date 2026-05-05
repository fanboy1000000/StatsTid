using System.Data;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.SharedKernel.Events;
using StatsTid.Tests.Regression.Config;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// D12 fixtures #14–#16 — direct contract tests on
/// <see cref="PostgresEventStore.EnqueueAsync"/> + the row-version backfill on the
/// post-migration <c>local_agreement_profiles</c> table.
///
/// <para>
/// #14 + #15 verify the in-tx visibility / rollback contract: an outbox row written
/// inside the caller's transaction is visible WITHIN that tx (via the same conn) but
/// invisible from a separate connection until commit; rollback must drop it.
/// </para>
///
/// <para>
/// #16 is parked in this file per the D12 spec table even though it logically tests the
/// migration. It seeds a pre-S22 row (no version column), runs the S22 migration, then
/// asserts the repo's <see cref="LocalAgreementProfileRepository.GetCurrentOpenAsync"/>
/// projection reads <c>Version = 1</c>. The wire ETag header is the endpoint's concern;
/// at the repository surface, the post-migration projection IS the load-bearing assertion.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EventStoreInTxTests : IAsyncLifetime
{
    private const string OrgId = "STY02";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";
    private const string ServiceId = "backend-api";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task EnqueueAsync_WritesOutboxRowInCallerTx()
    {
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        var enqueueStore = new PostgresEventStore(_harness.Factory, new OutboxServiceContext(ServiceId));

        var streamId = "test-stream-intx-visible";
        var evt = NewProfileEvent();

        // Open a tx, EnqueueAsync, and SELECT inside the same conn+tx → visible (count=1).
        // Then SELECT from a SEPARATE connection → invisible until commit (count=0).
        long countInsideTx;
        long countOutsideTx;
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            await enqueueStore.EnqueueAsync(conn, tx, streamId, evt);

            await using (var insideCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM outbox_events WHERE event_id = @id", conn, tx))
            {
                insideCmd.Parameters.AddWithValue("id", evt.EventId);
                countInsideTx = Convert.ToInt64(await insideCmd.ExecuteScalarAsync());
            }

            // Separate connection — outside the tx, MVCC under default ReadCommitted
            // can't see the uncommitted row.
            await using (var outsideConn = new NpgsqlConnection(_harness.ConnectionString))
            {
                await outsideConn.OpenAsync();
                await using var outsideCmd = new NpgsqlCommand(
                    "SELECT COUNT(*) FROM outbox_events WHERE event_id = @id", outsideConn);
                outsideCmd.Parameters.AddWithValue("id", evt.EventId);
                countOutsideTx = Convert.ToInt64(await outsideCmd.ExecuteScalarAsync());
            }

            await tx.CommitAsync();
        }

        Assert.Equal(1L, countInsideTx);
        Assert.Equal(0L, countOutsideTx);

        // Post-commit visibility: row is now visible from a fresh connection.
        await using (var afterConn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await afterConn.OpenAsync();
            await using var afterCmd = new NpgsqlCommand(
                "SELECT COUNT(*) FROM outbox_events WHERE event_id = @id", afterConn);
            afterCmd.Parameters.AddWithValue("id", evt.EventId);
            Assert.Equal(1L, Convert.ToInt64(await afterCmd.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public async Task EnqueueAsync_RollsBackWithCallerTx()
    {
        await OutboxTestSchema.ApplyAsync(_harness.ConnectionString);
        var enqueueStore = new PostgresEventStore(_harness.Factory, new OutboxServiceContext(ServiceId));

        var streamId = "test-stream-intx-rollback";
        var evt = NewProfileEvent();

        // Open a tx, EnqueueAsync, ROLLBACK. Outbox row count delta = 0.
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            await enqueueStore.EnqueueAsync(conn, tx, streamId, evt);
            await tx.RollbackAsync();
        }

        await using var verifyConn = new NpgsqlConnection(_harness.ConnectionString);
        await verifyConn.OpenAsync();
        await using var verifyCmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM outbox_events WHERE event_id = @id", verifyConn);
        verifyCmd.Parameters.AddWithValue("id", evt.EventId);
        Assert.Equal(0L, Convert.ToInt64(await verifyCmd.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task VersionBackfill_ExistingProfileReturns1()
    {
        // Seed a pre-S22 profile row (no version column) using LegacyProfileSchema, then
        // apply the S22 migration DO $$ block — the column is added with DEFAULT 1, so
        // existing rows backfill to version=1. The repo's read-side projection MUST
        // surface Version=1.
        //
        // Note: this scenario sits in EventStoreInTxTests per the D12 spec table even
        // though it primarily exercises the schema migration. The wire ETag header
        // shape ("1" RFC-7232 quoted) is the endpoint's concern (TASK-2205); at the
        // repository level the load-bearing assertion is the projection value.
        await LegacyProfileSchema.ApplyAsync(_harness.ConnectionString);
        await SeedOrganizationAsync(_harness.ConnectionString, OrgId);

        var profileId = Guid.NewGuid();
        var effectiveFrom = new DateOnly(2025, 1, 6);
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var insertCmd = new NpgsqlCommand(
                """
                INSERT INTO local_agreement_profiles (
                    profile_id, org_id, agreement_code, ok_version, effective_from,
                    weekly_norm_hours, created_by)
                VALUES (@id, @org, @ac, @ok, @from, 37, 'admin1')
                """, conn);
            insertCmd.Parameters.AddWithValue("id", profileId);
            insertCmd.Parameters.AddWithValue("org", OrgId);
            insertCmd.Parameters.AddWithValue("ac", AgreementCode);
            insertCmd.Parameters.AddWithValue("ok", OkVersion);
            insertCmd.Parameters.AddWithValue("from", effectiveFrom);
            await insertCmd.ExecuteNonQueryAsync();
        }

        // Apply the S22 migration: adds version column DEFAULT 1, shifts effective_to
        // (no closed rows here so a no-op for that branch), extends audit-action CHECK.
        await LegacyProfileSchema.RunS22MigrationAsync(_harness.ConnectionString);

        // Repo-side projection: Version reads as 1 (the column DEFAULT backfilled).
        var repo = new LocalAgreementProfileRepository(_harness.Factory);
        var current = await repo.GetCurrentOpenAsync(OrgId, AgreementCode, OkVersion);
        Assert.NotNull(current);
        Assert.Equal(profileId, current!.ProfileId);
        Assert.Equal(1L, current.Version);
    }

    private static LocalAgreementProfileChanged NewProfileEvent() => new()
    {
        ProfileId = Guid.NewGuid(),
        OrgId = OrgId,
        AgreementCode = AgreementCode,
        OkVersion = OkVersion,
        EffectiveFrom = new DateOnly(2026, 5, 4),
        ActorId = "admin1",
        ActorRole = "LocalAdmin",
    };

    private static async Task SeedOrganizationAsync(string connectionString, string orgId)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO organizations (org_id, org_name, org_type, materialized_path, agreement_code, ok_version)
            VALUES (@orgId, @orgId || ' Test Org', 'STYRELSE', '/' || @orgId || '/', 'HK', 'OK24')
            ON CONFLICT (org_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("orgId", orgId);
        await cmd.ExecuteNonQueryAsync();
    }
}
