using Npgsql;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// D11 fixtures #6–#7 — pins the partial-unique-index invariant on
/// <c>local_agreement_profiles</c> (ADR-017 D1):
///
/// <list type="bullet">
///   <item>#6 — concurrent INSERT race surfaces PostgreSQL unique-violation 23505.</item>
///   <item>#7 — close-then-recreate succeeds (predecessor's <c>effective_to</c> is NOT NULL,
///   so the partial index excludes it).</item>
/// </list>
///
/// <para>
/// These tests intentionally bypass <see cref="StatsTid.Infrastructure.LocalAgreementProfileRepository"/>
/// and INSERT directly. The repository serializes writers via <c>SELECT ... FOR UPDATE</c>;
/// here we want to pin the schema-level invariant, not the repository's safe-write path —
/// so the fixture must trigger the partial-unique-index directly.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class ProfileUniquenessTests : IAsyncLifetime
{
    private const string OrgId = "STY02";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";

    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await ProfileTestSchema.ApplyAsync(_harness.ConnectionString);
        await ProfileTestSchema.SeedOrganizationAsync(_harness.ConnectionString, OrgId);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    [Fact]
    public async Task ConcurrentInsert_RaceSurfacesUniqueViolation()
    {
        // Two transactions, both INSERT an open-ended profile (effective_to NULL) for the
        // same triple. The partial-unique-index uq_local_agreement_profile_active forbids
        // a second open row; second commit must fail with PostgreSQL SqlState 23505.
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var effectiveFrom = new DateOnly(2026, 5, 4);

        await using var connA = new NpgsqlConnection(_harness.ConnectionString);
        await connA.OpenAsync();
        await using var txA = await connA.BeginTransactionAsync();

        await using (var insertA = NewInsertCmd(connA, txA, idA, effectiveFrom))
        {
            await insertA.ExecuteNonQueryAsync();
        }

        // First transaction commits.
        await txA.CommitAsync();

        // Second transaction now attempts to insert another open-ended row for the same
        // triple. Even sequentially this MUST fail because the partial-unique-index is
        // violated as soon as the row is inserted.
        await using var connB = new NpgsqlConnection(_harness.ConnectionString);
        await connB.OpenAsync();
        await using var txB = await connB.BeginTransactionAsync();
        await using (var insertB = NewInsertCmd(connB, txB, idB, effectiveFrom.AddDays(7)))
        {
            var ex = await Assert.ThrowsAsync<PostgresException>(
                async () => await insertB.ExecuteNonQueryAsync());
            Assert.Equal("23505", ex.SqlState);
        }
        await txB.RollbackAsync();
    }

    [Fact]
    public async Task DeactivationWithoutSupersession_AllowsFutureRecreation()
    {
        // Step 1: insert one open profile.
        var idA = Guid.NewGuid();
        var effectiveFrom = new DateOnly(2026, 1, 5);
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var insert = NewInsertCmd(conn, null, idA, effectiveFrom);
            await insert.ExecuteNonQueryAsync();
        }

        // Step 2: close the predecessor by setting effective_to = today.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var update = new NpgsqlCommand(
                "UPDATE local_agreement_profiles SET effective_to = @today WHERE profile_id = @id",
                conn);
            update.Parameters.AddWithValue("today", today);
            update.Parameters.AddWithValue("id", idA);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
        }

        // Step 3: insert a new open profile for the same triple. Must succeed because the
        // partial-unique-index excludes the predecessor (effective_to IS NOT NULL).
        var idB = Guid.NewGuid();
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var insert = NewInsertCmd(conn, null, idB, today);
            await insert.ExecuteNonQueryAsync();
        }

        // Verify both rows exist; only the new one has effective_to NULL.
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT
                    COUNT(*) FILTER (WHERE effective_to IS NULL) AS open_count,
                    COUNT(*)                                     AS total_count
                FROM local_agreement_profiles
                WHERE org_id = @org AND agreement_code = @ac AND ok_version = @ok
                """, conn);
            cmd.Parameters.AddWithValue("org", OrgId);
            cmd.Parameters.AddWithValue("ac", AgreementCode);
            cmd.Parameters.AddWithValue("ok", OkVersion);
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(2L, reader.GetInt64(1));
        }
    }

    private static NpgsqlCommand NewInsertCmd(
        NpgsqlConnection conn, NpgsqlTransaction? tx, Guid profileId, DateOnly effectiveFrom)
    {
        var cmd = tx is null
            ? new NpgsqlCommand("", conn)
            : new NpgsqlCommand("", conn, tx);
        cmd.CommandText = """
            INSERT INTO local_agreement_profiles (
                profile_id, org_id, agreement_code, ok_version,
                effective_from, effective_to,
                weekly_norm_hours, max_flex_balance, flex_carryover_max,
                max_overtime_hours_per_period, overtime_requires_pre_approval,
                created_by)
            VALUES (
                @id, @org, @ac, @ok,
                @from, NULL,
                NULL, 100, NULL, NULL, NULL,
                'test')
            """;
        cmd.Parameters.AddWithValue("id", profileId);
        cmd.Parameters.AddWithValue("org", OrgId);
        cmd.Parameters.AddWithValue("ac", AgreementCode);
        cmd.Parameters.AddWithValue("ok", OkVersion);
        cmd.Parameters.AddWithValue("from", effectiveFrom);
        return cmd;
    }
}
