using Npgsql;

namespace StatsTid.Tests.Regression.PhaseE;

/// <summary>
/// S43 / TASK-4306. Minimal DDL extending
/// <see cref="Segmentation.TestFixtures.DockerHarness"/>'s baseline schema
/// with the rows needed to exercise <c>audit_projection</c> tests:
/// <list type="bullet">
///   <item><description><c>organizations</c> stub — FK target for
///   <c>audit_projection.target_org_id</c>; mirrors
///   <see cref="StatsTid.Tests.Regression.Config.LegacyProfileSchema"/>
///   col-by-col.</description></item>
///   <item><description><c>event_streams</c> + <c>events</c> stubs — required
///   by Test #2 to seed a synthetic source event that the backfill consumes.</description></item>
///   <item><description><c>audit_projection</c> table + 5 indexes + 2 CHECK
///   constraints + FK — mirrors init.sql lines 2008-2068 byte-for-byte; schema
///   drift between this DDL and production must be mirrored here.</description></item>
/// </list>
/// </summary>
internal static class AuditProjectionTestSchema
{
    public const string Ddl = """
        CREATE TABLE IF NOT EXISTS organizations (
            org_id              TEXT        PRIMARY KEY,
            org_name            TEXT        NOT NULL,
            org_type            TEXT        NOT NULL CHECK (org_type IN ('MINISTRY', 'STYRELSE', 'AFDELING', 'TEAM')),
            parent_org_id       TEXT        REFERENCES organizations(org_id),
            materialized_path   TEXT        NOT NULL,
            agreement_code      TEXT        NOT NULL DEFAULT 'AC',
            ok_version          TEXT        NOT NULL DEFAULT 'OK24',
            is_active           BOOLEAN     NOT NULL DEFAULT TRUE,
            created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS event_streams (
            stream_id        TEXT         PRIMARY KEY,
            created_at       TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            current_version  INT          NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS events (
            event_id         UUID         PRIMARY KEY,
            stream_id        TEXT         NOT NULL REFERENCES event_streams(stream_id),
            stream_version   INT          NOT NULL,
            event_type       TEXT         NOT NULL,
            data             JSONB        NOT NULL,
            occurred_at      TIMESTAMPTZ  NOT NULL,
            stored_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            actor_id         TEXT         NULL,
            actor_role       TEXT         NULL,
            correlation_id   UUID         NULL,
            UNIQUE (stream_id, stream_version)
        );

        CREATE TABLE IF NOT EXISTS audit_projection (
            projection_id            UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
            event_id                 UUID         NOT NULL UNIQUE,
            outbox_id                BIGINT       NOT NULL,
            event_type               TEXT         NOT NULL,
            visibility_scope         TEXT         NOT NULL CHECK (visibility_scope IN ('TENANT_TARGETED', 'GLOBAL_TENANT_VISIBLE', 'GLOBAL_ADMIN_ONLY')),
            target_org_id            TEXT         NULL REFERENCES organizations(org_id),
            target_resource_id       TEXT         NULL,
            actor_id                 TEXT         NULL,
            actor_primary_org_id     TEXT         NULL,
            occurred_at              TIMESTAMPTZ  NOT NULL,
            correlation_id           UUID         NULL,
            details                  JSONB        NOT NULL,
            projected_at             TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
            CONSTRAINT chk_target_org_required_when_tenant
                CHECK (
                    (visibility_scope = 'TENANT_TARGETED'      AND target_org_id IS NOT NULL) OR
                    (visibility_scope IN ('GLOBAL_TENANT_VISIBLE', 'GLOBAL_ADMIN_ONLY'))
                )
        );

        CREATE INDEX IF NOT EXISTS idx_audit_projection_target_org_time
            ON audit_projection (target_org_id, occurred_at DESC)
            WHERE target_org_id IS NOT NULL;

        CREATE INDEX IF NOT EXISTS idx_audit_projection_global_visible
            ON audit_projection (occurred_at DESC)
            WHERE visibility_scope = 'GLOBAL_TENANT_VISIBLE';

        CREATE INDEX IF NOT EXISTS idx_audit_projection_actor_org_time
            ON audit_projection (actor_primary_org_id, occurred_at DESC)
            WHERE actor_primary_org_id IS NOT NULL;

        CREATE INDEX IF NOT EXISTS idx_audit_projection_event_type_time
            ON audit_projection (event_type, occurred_at DESC);

        CREATE INDEX IF NOT EXISTS idx_audit_projection_outbox_id
            ON audit_projection (outbox_id);
        """;

    public static async Task ApplyAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
