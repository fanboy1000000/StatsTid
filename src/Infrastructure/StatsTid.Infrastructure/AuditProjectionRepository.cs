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
    /// <returns>1 when the row was inserted; 0 when ON CONFLICT short-circuited
    /// (duplicate event_id). Callers use this to distinguish first-write from
    /// replay/conflict cases.</returns>
    public async Task<int> InsertAsync(
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
        return await cmd.ExecuteNonQueryAsync(ct);
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

    /// <summary>
    /// S44 / TASK-4406 — ADR-026 D5 audit-visibility-surface read path.
    /// Returns paginated audit_projection rows visible to the actor
    /// scope expressed by <paramref name="accessibleOrgIds"/>.
    ///
    /// <para><b>Visibility tier resolution</b> per ADR-026 D4:</para>
    /// <list type="bullet">
    ///   <item><description><c>accessibleOrgIds = null</c> (GlobalAdmin
    ///   sentinel per <see cref="Security.OrgScopeValidator.GetAccessibleOrgsAsync"/>) →
    ///   includes ALL rows regardless of visibility_scope, including
    ///   GLOBAL_ADMIN_ONLY.</description></item>
    ///   <item><description><c>accessibleOrgIds = []</c> (empty list;
    ///   Employee / unscoped) → returns empty page (no-op fast path).</description></item>
    ///   <item><description><c>accessibleOrgIds = ['X', 'Y', ...]</c>
    ///   (LocalAdmin scope-by-target subtree) → includes
    ///   <c>visibility_scope = 'TENANT_TARGETED' AND target_org_id = ANY(@orgIds)</c>
    ///   OR <c>visibility_scope = 'GLOBAL_TENANT_VISIBLE'</c>. Excludes
    ///   GLOBAL_ADMIN_ONLY.</description></item>
    /// </list>
    ///
    /// <para>Additional filter dimensions from <see cref="AuditQueryFilter"/>
    /// (event_types / target_org_id / actor_id / occurred_at range /
    /// visibility_scopes) are AND'd onto the visibility clause.</para>
    ///
    /// <para>Page-based pagination (page is 1-indexed); returns
    /// <c>(rows, totalCount)</c>. Total count excludes pagination so the
    /// frontend can render "showing N of M" affordances.</para>
    /// </summary>
    public async Task<(IReadOnlyList<AuditProjectionRow> Rows, long TotalCount)> QueryByOrgScopeAsync(
        IReadOnlyList<string>? accessibleOrgIds,
        AuditQueryFilter filter,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        // Fast path: empty scope → empty page (no DB hit).
        if (accessibleOrgIds is { Count: 0 })
            return (Array.Empty<AuditProjectionRow>(), 0L);

        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 500) pageSize = 500;

        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        // Build the visibility predicate based on the sentinel.
        // null = GlobalAdmin (no scope filter); non-null = scope-by-target.
        var (visibilitySql, visibilityParams) = BuildVisibilityClause(accessibleOrgIds);
        var (filterSql, filterParams) = BuildFilterClause(filter);

        var whereSql = string.IsNullOrEmpty(filterSql)
            ? visibilitySql
            : $"({visibilitySql}) AND ({filterSql})";

        // Total count (excludes pagination).
        // CA2100 suppression: whereSql is constructed entirely from BuildVisibilityClause
        // + BuildFilterClause; no user input is ever string-concatenated into the SQL
        // text. All values flow through parameterized NpgsqlParameter instances via
        // ApplyParameters. The dynamic clause assembly is the only way to support
        // optional filter dimensions without 32+ permuted query strings.
        long totalCount;
#pragma warning disable CA2100
        await using (var countCmd = new NpgsqlCommand(
            $"SELECT COUNT(*) FROM audit_projection WHERE {whereSql}", conn))
#pragma warning restore CA2100
        {
            ApplyParameters(countCmd, visibilityParams);
            ApplyParameters(countCmd, filterParams);
            totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));
        }

        if (totalCount == 0)
            return (Array.Empty<AuditProjectionRow>(), 0L);

        // Paged results (offset + limit; ordered by occurred_at DESC for newest-first)
        var offset = (page - 1) * pageSize;
        var rows = new List<AuditProjectionRow>(pageSize);
#pragma warning disable CA2100
        await using (var pageCmd = new NpgsqlCommand(
            $@"SELECT projection_id, event_id, outbox_id, event_type, visibility_scope,
                      target_org_id, target_resource_id, actor_id, actor_primary_org_id,
                      occurred_at, correlation_id, details::text, projected_at
               FROM audit_projection
               WHERE {whereSql}
               ORDER BY occurred_at DESC, projection_id DESC
               LIMIT @limit OFFSET @offset", conn))
#pragma warning restore CA2100
        {
            ApplyParameters(pageCmd, visibilityParams);
            ApplyParameters(pageCmd, filterParams);
            pageCmd.Parameters.AddWithValue("limit", pageSize);
            pageCmd.Parameters.AddWithValue("offset", offset);

            await using var reader = await pageCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                rows.Add(ReadRow(reader));
        }

        return (rows, totalCount);
    }

    // -------------------------------------------------------------------
    // Internal helpers — clause builders + row reader for QueryByOrgScopeAsync
    // -------------------------------------------------------------------

    private static (string Sql, List<(string Name, object Value, NpgsqlDbType? Type)> Params) BuildVisibilityClause(
        IReadOnlyList<string>? accessibleOrgIds)
    {
        var p = new List<(string, object, NpgsqlDbType?)>();
        if (accessibleOrgIds is null)
        {
            // GlobalAdmin: no filter — include all 3 visibility tiers
            return ("1=1", p);
        }
        // LocalAdmin scope-by-target subtree + GLOBAL_TENANT_VISIBLE always
        p.Add(("accessibleOrgIds", accessibleOrgIds.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Text));
        return (
            "(visibility_scope = 'TENANT_TARGETED' AND target_org_id = ANY(@accessibleOrgIds)) " +
            "OR visibility_scope = 'GLOBAL_TENANT_VISIBLE'",
            p);
    }

    private static (string Sql, List<(string Name, object Value, NpgsqlDbType? Type)> Params) BuildFilterClause(
        AuditQueryFilter filter)
    {
        var clauses = new List<string>(6);
        var p = new List<(string, object, NpgsqlDbType?)>();

        if (filter.EventTypes is { Count: > 0 })
        {
            clauses.Add("event_type = ANY(@eventTypes)");
            p.Add(("eventTypes", filter.EventTypes.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Text));
        }
        if (!string.IsNullOrEmpty(filter.TargetOrgId))
        {
            clauses.Add("target_org_id = @targetOrgId");
            p.Add(("targetOrgId", filter.TargetOrgId, null));
        }
        if (!string.IsNullOrEmpty(filter.ActorId))
        {
            clauses.Add("(actor_id = @actorId OR actor_primary_org_id = @actorId)");
            p.Add(("actorId", filter.ActorId, null));
        }
        if (filter.OccurredAtFrom is { } fromTs)
        {
            clauses.Add("occurred_at >= @occurredAtFrom");
            p.Add(("occurredAtFrom", fromTs, null));
        }
        if (filter.OccurredAtTo is { } toTs)
        {
            clauses.Add("occurred_at <= @occurredAtTo");
            p.Add(("occurredAtTo", toTs, null));
        }
        if (filter.VisibilityScopes is { Count: > 0 })
        {
            clauses.Add("visibility_scope = ANY(@visibilityScopes)");
            p.Add(("visibilityScopes", filter.VisibilityScopes.ToArray(), NpgsqlDbType.Array | NpgsqlDbType.Text));
        }

        return (string.Join(" AND ", clauses), p);
    }

    private static void ApplyParameters(
        NpgsqlCommand cmd,
        List<(string Name, object Value, NpgsqlDbType? Type)> parameters)
    {
        foreach (var (name, value, type) in parameters)
        {
            if (type.HasValue)
                cmd.Parameters.Add(new NpgsqlParameter(name, type.Value) { Value = value });
            else
                cmd.Parameters.AddWithValue(name, value);
        }
    }

    private static AuditProjectionRow ReadRow(NpgsqlDataReader reader)
    {
        var occurredAt = reader.GetFieldValue<DateTime>(reader.GetOrdinal("occurred_at"));
        var projectedAt = reader.GetFieldValue<DateTime>(reader.GetOrdinal("projected_at"));
        return new AuditProjectionRow(
            ProjectionId: reader.GetGuid(reader.GetOrdinal("projection_id")),
            EventId: reader.GetGuid(reader.GetOrdinal("event_id")),
            OutboxId: reader.GetInt64(reader.GetOrdinal("outbox_id")),
            EventType: reader.GetString(reader.GetOrdinal("event_type")),
            VisibilityScope: reader.GetString(reader.GetOrdinal("visibility_scope")),
            TargetOrgId: reader.IsDBNull(reader.GetOrdinal("target_org_id")) ? null : reader.GetString(reader.GetOrdinal("target_org_id")),
            TargetResourceId: reader.IsDBNull(reader.GetOrdinal("target_resource_id")) ? null : reader.GetString(reader.GetOrdinal("target_resource_id")),
            ActorId: reader.IsDBNull(reader.GetOrdinal("actor_id")) ? null : reader.GetString(reader.GetOrdinal("actor_id")),
            ActorPrimaryOrgId: reader.IsDBNull(reader.GetOrdinal("actor_primary_org_id")) ? null : reader.GetString(reader.GetOrdinal("actor_primary_org_id")),
            OccurredAt: new DateTimeOffset(DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc)),
            CorrelationId: reader.IsDBNull(reader.GetOrdinal("correlation_id")) ? null : reader.GetGuid(reader.GetOrdinal("correlation_id")),
            DetailsJson: reader.GetString(reader.GetOrdinal("details")),
            ProjectedAt: new DateTimeOffset(DateTime.SpecifyKind(projectedAt, DateTimeKind.Utc)));
    }
}
