namespace StatsTid.SharedKernel.Audit;

/// <summary>
/// S43 / ADR-026 D2. Context threaded from the emitting endpoint to the
/// <see cref="IAuditProjectionMapper{TEvent}"/>. Carries event-independent
/// fields the mapper would otherwise need to plumb through every domain
/// event (actor identity, correlation, occurrence time).
/// </summary>
/// <param name="ActorId">JWT <c>sub</c> claim of the user / system actor.</param>
/// <param name="ActorPrimaryOrgId">JWT <c>org_id</c> claim; denormalized
/// into <c>audit_projection.actor_primary_org_id</c> for the scope-by-actor
/// secondary query path per ADR-026 D5.</param>
/// <param name="CorrelationId">Correlation ID from the request scope per
/// <c>CorrelationIdMiddleware</c>.</param>
/// <param name="OccurredAt">Event occurrence time (from the event itself).</param>
/// <param name="ResolvedTargetOrgId">Pre-resolved target_org_id for events
/// where the mapper can't compute it from the event payload alone (e.g.,
/// <c>RoleAssignmentGranted</c> needs the user's primary_org_id but the
/// event only carries the scope org). Endpoint resolves before invoking
/// the mapper; mapper uses this when the event payload doesn't carry the
/// target. <c>null</c> when not applicable. Per ADR-026 D2 L134 "endpoint
/// resolves the lookup BEFORE invoking the mapper and passes the resolved
/// value through the Context parameter." Added at S44 / TASK-4407.</param>
public sealed record AuditProjectionContext(
    string? ActorId,
    string? ActorPrimaryOrgId,
    Guid? CorrelationId,
    DateTimeOffset OccurredAt,
    string? ResolvedTargetOrgId = null);
