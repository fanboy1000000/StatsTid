namespace StatsTid.SharedKernel.Audit;

/// <summary>
/// S44 / TASK-4406. Row shape returned by
/// <c>AuditProjectionRepository.QueryByOrgScopeAsync</c> — matches the
/// <c>audit_projection</c> table columns 1:1.
///
/// <para>
/// <c>DetailsJson</c> is the raw JSONB text exactly as the mapper serialized
/// it (caller deserializes per the per-event-type schema documented in the
/// catalog). <c>VisibilityScope</c> is the wire-string value
/// (TENANT_TARGETED / GLOBAL_TENANT_VISIBLE / GLOBAL_ADMIN_ONLY) — NOT the
/// enum; consumers map to the enum if needed.
/// </para>
/// </summary>
public sealed record AuditProjectionRow(
    Guid ProjectionId,
    Guid EventId,
    long OutboxId,
    string EventType,
    string VisibilityScope,
    string? TargetOrgId,
    string? TargetResourceId,
    string? ActorId,
    string? ActorPrimaryOrgId,
    DateTimeOffset OccurredAt,
    Guid? CorrelationId,
    string DetailsJson,
    DateTimeOffset ProjectedAt);

/// <summary>
/// S44 / TASK-4406. Filter parameters for
/// <c>AuditProjectionRepository.QueryByOrgScopeAsync</c>. All fields
/// optional except pagination; any combination is valid.
/// </summary>
/// <param name="EventTypes">Restrict to these event_type values (multi-value).
/// Null or empty = no event_type filter.</param>
/// <param name="TargetOrgId">Restrict to this single target_org_id. Caller
/// must verify it falls within the actor's accessibleOrgIds before passing.
/// Null = no target_org_id filter.</param>
/// <param name="ActorId">Restrict to this actor (matches both
/// actor_id and the secondary actor_primary_org_id path). Null = no filter.</param>
/// <param name="OccurredAtFrom">Inclusive lower bound on occurred_at. Null = no lower bound.</param>
/// <param name="OccurredAtTo">Inclusive upper bound on occurred_at. Null = no upper bound.</param>
/// <param name="VisibilityScopes">Restrict to these visibility_scope values
/// (multi-value; subset of the 3-tier enum). Null or empty = no visibility filter
/// (applied AFTER the scope-by-target / scope-by-actor coverage clause).</param>
public sealed record AuditQueryFilter(
    IReadOnlyList<string>? EventTypes = null,
    string? TargetOrgId = null,
    string? ActorId = null,
    DateTimeOffset? OccurredAtFrom = null,
    DateTimeOffset? OccurredAtTo = null,
    IReadOnlyList<string>? VisibilityScopes = null);
