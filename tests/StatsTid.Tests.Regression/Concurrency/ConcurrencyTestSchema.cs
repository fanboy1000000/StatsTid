using Npgsql;

namespace StatsTid.Tests.Regression.Concurrency;

/// <summary>
/// SQL DDL extension that layers the S25 / TASK-2501 (<c>s25-d2-2-version</c>) schema
/// migration over the baseline tables created by
/// <see cref="Outbox.ForcedRollbackHarness.ForcedRollbackSchema"/>: adds the
/// <c>version BIGINT NOT NULL DEFAULT 1</c> column to the three S25-propagated state
/// tables and the <c>version_before</c> / <c>version_after</c> nullable columns to
/// their three audit tables. Idempotent — uses <c>ADD COLUMN IF NOT EXISTS</c>.
///
/// <para>
/// The S25 v3 repository overloads consumed by the Concurrency tests
/// (<see cref="StatsTid.Infrastructure.AgreementConfigRepository.UpdateDraftAsync(NpgsqlConnection, NpgsqlTransaction, System.Guid, long, StatsTid.SharedKernel.Models.AgreementConfigEntity, System.Threading.CancellationToken)"/>
/// etc.) read <c>version</c> columns via <c>RETURNING *</c>; without this DDL extension
/// they would throw at runtime under
/// <see cref="Segmentation.TestFixtures.DockerHarness"/> because the harness's baseline
/// schema predates the S25 migration. The production <c>init.sql</c> applies the same
/// columns inside the <c>s25-d2-2-version</c> guarded block — we reproduce the post-
/// migration shape here so direct repo orchestration sees the same column set.
/// </para>
///
/// <para>
/// Mirrors <c>docker/postgres/init.sql</c> lines 1317-1359 byte-for-byte for the
/// <c>ALTER TABLE</c> statements; schema drift between this DDL and the production
/// migration must be mirrored here.
/// </para>
/// </summary>
internal static class ConcurrencyTestSchema
{
    public const string Ddl = """
        ALTER TABLE agreement_configs
        ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

        ALTER TABLE position_override_configs
        ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

        ALTER TABLE wage_type_mappings
        ADD COLUMN IF NOT EXISTS version BIGINT NOT NULL DEFAULT 1;

        ALTER TABLE agreement_config_audit
        ADD COLUMN IF NOT EXISTS version_before BIGINT NULL,
        ADD COLUMN IF NOT EXISTS version_after BIGINT NULL;

        ALTER TABLE position_override_config_audit
        ADD COLUMN IF NOT EXISTS version_before BIGINT NULL,
        ADD COLUMN IF NOT EXISTS version_after BIGINT NULL;

        ALTER TABLE wage_type_mapping_audit
        ADD COLUMN IF NOT EXISTS version_before BIGINT NULL,
        ADD COLUMN IF NOT EXISTS version_after BIGINT NULL;
        """;

    /// <summary>
    /// Applies <see cref="Ddl"/> against the supplied connection string. Idempotent —
    /// safe to call multiple times across test classes on the same container.
    /// Caller MUST have already applied <see cref="Outbox.ForcedRollbackHarness.ForcedRollbackSchema"/>
    /// (which creates the parent tables this DDL extends).
    /// </summary>
    public static async Task ApplyAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
