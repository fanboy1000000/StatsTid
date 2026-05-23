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
public sealed record AuditProjectionContext(
    string? ActorId,
    string? ActorPrimaryOrgId,
    Guid? CorrelationId,
    DateTimeOffset OccurredAt);
