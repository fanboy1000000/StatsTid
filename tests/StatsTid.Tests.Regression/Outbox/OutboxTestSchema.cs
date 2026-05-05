using Npgsql;

namespace StatsTid.Tests.Regression.Outbox;

/// <summary>
/// SQL DDL extending <see cref="Segmentation.TestFixtures.DockerHarness"/>'s baseline
/// schema with the S22 / ADR-018 transactional-outbox tables: <c>schema_migrations</c>
/// (ledger) and <c>outbox_events</c> (with the three partial indexes).
///
/// <para>
/// Mirrors <c>docker/postgres/init.sql</c> lines ~5-71 byte-for-byte for the columns;
/// schema drift between this DDL and production must be mirrored here.
/// </para>
/// </summary>
internal static class OutboxTestSchema
{
    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS schema_migrations (
            migration_id  TEXT         PRIMARY KEY,
            applied_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            notes         TEXT         NULL
        );

        CREATE TABLE IF NOT EXISTS outbox_events (
            outbox_id        BIGSERIAL    PRIMARY KEY,
            service_id       TEXT         NOT NULL,
            stream_id        TEXT         NOT NULL,
            event_id         UUID         NOT NULL UNIQUE,
            event_type       TEXT         NOT NULL,
            event_payload    JSONB        NOT NULL,
            correlation_id   TEXT         NULL,
            actor_id         TEXT         NULL,
            actor_role       TEXT         NULL,
            created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            published_at     TIMESTAMPTZ  NULL,
            stream_version   INT          NULL,
            attempts         INT          NOT NULL DEFAULT 0,
            last_error       TEXT         NULL,
            last_attempt_at  TIMESTAMPTZ  NULL
        );

        CREATE INDEX IF NOT EXISTS idx_outbox_unpublished
            ON outbox_events (service_id, outbox_id)
            WHERE published_at IS NULL;

        CREATE INDEX IF NOT EXISTS idx_outbox_attempts
            ON outbox_events (service_id, attempts, last_attempt_at)
            WHERE published_at IS NULL AND attempts > 0;

        CREATE INDEX IF NOT EXISTS idx_outbox_stream
            ON outbox_events (stream_id, outbox_id)
            WHERE published_at IS NULL;
        """;

    /// <summary>
    /// Applies <see cref="Ddl"/> against the supplied connection string. Idempotent —
    /// uses <c>CREATE TABLE IF NOT EXISTS</c> + <c>CREATE INDEX IF NOT EXISTS</c>.
    /// </summary>
    public static async Task ApplyAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
