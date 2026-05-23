using Npgsql;
using NpgsqlTypes;
using StatsTid.SharedKernel.Audit;

namespace StatsTid.Infrastructure;

/// <summary>
/// S43 / ADR-026 D1+D2. DB-facing surface for the <c>audit_projection</c>
/// read-model. 3rd projection repository after <c>TimeEntryProjectionRepository</c>
/// + <c>AbsenceProjectionRepository</c> (S27 sync-in-tx canonical pattern per
/// ADR-018 D13).
///
/// <para>
/// <b>Atomic-outbox contract (ADR-018 D3).</b> <see cref="InsertAsync"/> is the
/// sole write path — endpoint code calls it inside the same transaction that
/// appends the source event and enqueues the outbox row, so the projection
/// row, the event, and the outbox row commit or roll back atomically. The
/// caller passes <paramref name="outboxId"/> returned by
/// <c>IOutboxEnqueue.EnqueueAndReturnIdAsync</c> earlier in the same tx, so
/// <c>audit_projection.outbox_id</c> aligns with the global outbox sequence
/// (per ADR-018 D13 ordering).
/// </para>
///
/// <para>
/// <b>Append-only.</b> The <c>audit_projection</c> table has no UPDATE or
/// DELETE surface — once a row lands, it's immutable. Backfill replay-safety
/// rides <c>ON CONFLICT (event_id) DO NOTHING</c> (the <c>event_id</c> UNIQUE
/// constraint enforces 1:1 mapping from source event to projection row).
/// </para>
///
/// <para>
/// Sub-Sprint 1 (S43) ships <see cref="InsertAsync"/> + <see cref="CountAsync"/>
/// + <see cref="CountByEventIdAsync"/> only. The read-path
/// <c>QueryByOrgScopeAsync</c> is deferred to Sub-Sprint 2 (S44) so the
/// auth/scope semantics ride alongside the GET endpoint that consumes them
/// (per Step 4 cycle 1 WARNING absorption).
/// </para>
/// </summary>
public sealed class AuditProjectionRepository
{
    private readonly DbConnectionFactory _connectionFactory;

    public AuditProjectionRepository(DbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    /// <summary>
    /// In-transaction insert for the path C event-projection contract. The
    /// caller passes the closed-generic context + row data + the outbox ID
    /// returned earlier in the same tx; this method does NOT commit or roll
    /// back. <c>ON CONFLICT (event_id) DO NOTHING</c> makes backfill replay safe
    /// and absorbs any double-emit at the endpoint layer without surfacing
    /// PostgresException 23505 to the caller.
    /// </summary>
    public async Task InsertAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid eventId,
        long outboxId,
        string eventType,
        AuditProjectionRowData rowData,
        AuditProjectionContext ctx,
        CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            @"INSERT INTO audit_projection (
                  event_id, outbox_id, event_type, visibility_scope,
                  target_org_id, target_resource_id, actor_id, actor_primary_org_id,
                  occurred_at, correlation_id, details
              ) VALUES (
                  @eventId, @outboxId, @eventType, @visibilityScope,
                  @targetOrgId, @targetResourceId, @actorId, @actorPrimaryOrgId,
                  @occurredAt, @correlationId, @details::jsonb
              )
              ON CONFLICT (event_id) DO NOTHING",
            conn, tx);
        cmd.Parameters.AddWithValue("eventId", eventId);
        cmd.Parameters.AddWithValue("outboxId", outboxId);
        cmd.Parameters.AddWithValue("eventType", eventType);
        cmd.Parameters.AddWithValue("visibilityScope", rowData.VisibilityScope.ToWireValue());
        cmd.Parameters.AddWithValue("targetOrgId", (object?)rowData.TargetOrgId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("targetResourceId", (object?)rowData.TargetResourceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorId", (object?)ctx.ActorId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("actorPrimaryOrgId", (object?)ctx.ActorPrimaryOrgId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("occurredAt", ctx.OccurredAt);
        cmd.Parameters.AddWithValue("correlationId", (object?)ctx.CorrelationId ?? DBNull.Value);
        cmd.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Jsonb) { Value = rowData.DetailsJson });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Total row count. Used by the Phase E backfill idempotency test
    /// (TASK-4306 Test #2) to verify that a second backfill pass conflicts
    /// no-op rather than duplicating rows.
    /// </summary>
    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM audit_projection", conn);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    /// <summary>
    /// Per-event row count (0 or 1; the <c>event_id UNIQUE</c> constraint
    /// caps this). Used by Phase E tests + Sub-Sprint 2 endpoint-side
    /// idempotency assertions.
    /// </summary>
    public async Task<long> CountByEventIdAsync(Guid eventId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM audit_projection WHERE event_id = @eventId", conn);
        cmd.Parameters.AddWithValue("eventId", eventId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }
}
