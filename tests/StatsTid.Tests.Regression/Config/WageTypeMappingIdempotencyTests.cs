using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Models;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;

namespace StatsTid.Tests.Regression.Config;

/// <summary>
/// S29 / TASK-2909 D-tests #8 + #10 — init.sql migration + seed idempotency for the
/// wage-type-mappings effective-dating rework (ADR-020 D1+D2+D3).
///
/// <list type="bullet">
///   <item><b>#8 (backfill idempotency)</b>: re-running init.sql against an already-applied
///   schema does not duplicate rows. The <c>schema_migrations</c> ledger short-circuits the
///   <c>s29-d1-wtm-effective-dating</c> DO block; the <c>ON CONFLICT (..., effective_from)
///   DO NOTHING</c> clauses on the 8 seed-INSERT blocks no-op against existing rows.</item>
///   <item><b>#10 (seed-INSERT idempotency under accumulated history)</b>: admin-edits a seed
///   row (cross-day supersession → predecessor stays at effective_from='2020-01-01' but is
///   closed). Re-running init.sql's seed INSERTs at effective_from='2020-01-01' no-ops via
///   the idx_wtm_natural_key_history conflict target — the SUPERSEDED predecessor stays
///   exactly 1 row; the current row stays untouched.</item>
/// </list>
///
/// Uses <see cref="StatsTidWebApplicationFactory.ApplyFullSchemaAsync"/> to execute the
/// canonical <c>docker/postgres/init.sql</c> in full. The script is idempotent end-to-end
/// (CREATE TABLE IF NOT EXISTS + ON CONFLICT DO NOTHING on every seed) — so re-applying
/// it on the same container is the canonical "second run" pattern.
/// </summary>
[Trait("Category", "Docker")]
public sealed class WageTypeMappingIdempotencyTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        // First run of init.sql — applies schema + seeds.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #8 — backfill / migration idempotency: re-running init.sql is a no-op.
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task BackfillIdempotency_ReRunningInitSql_ProducesZeroNewRows()
    {
        // Snapshot post-first-run row count.
        var rowsAfterFirst = await CountAllRowsAsync();
        Assert.True(rowsAfterFirst > 0, "First init.sql run must seed at least some rows.");

        // Snapshot post-first-run mapping_id set (every row has a unique surrogate PK).
        var mappingIdsAfterFirst = await ReadAllMappingIdsAsync();
        Assert.Equal(rowsAfterFirst, mappingIdsAfterFirst.Count);

        // Re-apply init.sql against the same container. The schema_migrations ledger
        // short-circuits all DO blocks; ON CONFLICT DO NOTHING on the seed INSERTs leaves
        // existing rows untouched. No new mapping_id values; no row count change.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        var rowsAfterSecond = await CountAllRowsAsync();
        Assert.Equal(rowsAfterFirst, rowsAfterSecond);

        var mappingIdsAfterSecond = await ReadAllMappingIdsAsync();
        Assert.Equal(mappingIdsAfterFirst.OrderBy(g => g).ToArray(),
                     mappingIdsAfterSecond.OrderBy(g => g).ToArray());

        // Also assert no duplicate (natural_key, effective_from) tuple — the
        // idx_wtm_natural_key_history full-unique constraint guarantees this; if the
        // second run had double-inserted, we would have seen a constraint violation
        // OR two rows for one tuple. Belt-and-braces verification.
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM (
                SELECT time_type, ok_version, agreement_code, position, effective_from, COUNT(*) c
                FROM wage_type_mappings
                GROUP BY time_type, ok_version, agreement_code, position, effective_from
                HAVING COUNT(*) > 1
            ) duplicates
            """, conn);
        var dupCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.Equal(0L, dupCount);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // D-test #10 — seed-INSERT idempotency under accumulated admin history (ADR-020 D3).
    // ═════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task SeedInsertIdempotency_UnderAccumulatedHistory_OnConflictNoOpsAgainstSupersededPredecessor()
    {
        // Pick a known seed row: NORMAL_HOURS / OK24 / AC / position=''. Its effective_from
        // is '2020-01-01' from the seed INSERTs (init.sql L200).
        const string TimeType = "NORMAL_HOURS";
        const string Ok = "OK24";
        const string Agreement = "AC";
        const string Position = "";

        var seedEffectiveFrom = new DateOnly(2020, 1, 1);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Verify the seed row exists at effective_from = 2020-01-01 with effective_to = NULL.
        var beforeEdit = await ReadAllForNaturalKeyAsync(TimeType, Ok, Agreement, Position);
        Assert.Single(beforeEdit);
        Assert.Equal(seedEffectiveFrom, beforeEdit[0].EffectiveFrom);
        Assert.Null(beforeEdit[0].EffectiveTo);

        // Admin-edit: cross-day supersession via SupersedeAndCreateAsync. Predecessor
        // becomes (2020-01-01, today); new row at (today, NULL, version=1).
        var repo = new WageTypeMappingRepository(_harness.Factory);
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await repo.SupersedeAndCreateAsync(conn, tx, new WageTypeMapping
            {
                TimeType = TimeType,
                WageType = "SLS_0110_EDITED",
                OkVersion = Ok,
                AgreementCode = Agreement,
                Position = Position,
                Description = "admin-edited via cross-day supersession",
                EffectiveFrom = today,
            }, expectedCurrentVersion: 1);
            await tx.CommitAsync();
        }

        // After edit: 2 rows for this natural key (closed predecessor + new open row).
        var afterEdit = await ReadAllForNaturalKeyAsync(TimeType, Ok, Agreement, Position);
        Assert.Equal(2, afterEdit.Count);
        Assert.Single(afterEdit.Where(r => r.EffectiveFrom == seedEffectiveFrom && r.EffectiveTo == today));
        Assert.Single(afterEdit.Where(r => r.EffectiveFrom == today && r.EffectiveTo is null));

        // Re-apply init.sql (seed INSERTs run again). For the edited natural key, the seed
        // INSERT at effective_from='2020-01-01' should no-op via the
        // idx_wtm_natural_key_history conflict target (which now matches the SUPERSEDED
        // predecessor row at effective_from='2020-01-01').
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);

        // The same two rows must still be present — no new seed-INSERT at '2020-01-01'
        // overwrote the SUPERSEDED predecessor; the current row (effective_from=today)
        // is unaffected (no seed targets today's date).
        var afterReseed = await ReadAllForNaturalKeyAsync(TimeType, Ok, Agreement, Position);
        Assert.Equal(2, afterReseed.Count);

        // Predecessor still at (2020-01-01, today) with the ORIGINAL pre-edit description
        // (seed re-INSERT did NOT clobber it — ON CONFLICT preserves the existing row).
        var predecessor = afterReseed.Single(r => r.EffectiveFrom == seedEffectiveFrom);
        Assert.Equal(today, predecessor.EffectiveTo);
        Assert.Equal("Normal working hours", predecessor.Description); // original seed text

        // Current row at (today, NULL) unaffected by reseed.
        var current = afterReseed.Single(r => r.EffectiveFrom == today);
        Assert.Null(current.EffectiveTo);
        Assert.Equal("admin-edited via cross-day supersession", current.Description);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task<long> CountAllRowsAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM wage_type_mappings", conn);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private async Task<List<Guid>> ReadAllMappingIdsAsync()
    {
        var ids = new List<Guid>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT mapping_id FROM wage_type_mappings ORDER BY mapping_id", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    private async Task<List<WageTypeMapping>> ReadAllForNaturalKeyAsync(
        string timeType, string okVersion, string agreementCode, string position)
    {
        var rows = new List<WageTypeMapping>();
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT mapping_id, time_type, wage_type, ok_version, agreement_code, position,
                   description, version, effective_from, effective_to
            FROM wage_type_mappings
            WHERE time_type = @tt AND ok_version = @ok AND agreement_code = @ac
              AND position = @pos
            ORDER BY effective_from
            """, conn);
        cmd.Parameters.AddWithValue("tt", timeType);
        cmd.Parameters.AddWithValue("ok", okVersion);
        cmd.Parameters.AddWithValue("ac", agreementCode);
        cmd.Parameters.AddWithValue("pos", position);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new WageTypeMapping
            {
                MappingId = reader.GetGuid(0),
                TimeType = reader.GetString(1),
                WageType = reader.GetString(2),
                OkVersion = reader.GetString(3),
                AgreementCode = reader.GetString(4),
                Position = reader.GetString(5),
                Description = reader.IsDBNull(6) ? null : reader.GetString(6),
                Version = reader.GetInt64(7),
                EffectiveFrom = reader.GetFieldValue<DateOnly>(8),
                EffectiveTo = reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
            });
        }
        return rows;
    }
}
