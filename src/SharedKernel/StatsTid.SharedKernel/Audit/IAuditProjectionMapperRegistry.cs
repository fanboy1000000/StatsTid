namespace StatsTid.SharedKernel.Audit;

/// <summary>
/// S43 / ADR-026 D2 dispatch lookup. Endpoint code may resolve mappers via
/// per-type constructor injection (DI returns the typed
/// <see cref="IAuditProjectionMapper{TEvent}"/> directly); the backfill loop
/// (<c>AuditProjectionBackfillService</c>) needs a runtime-keyed lookup
/// because it dispatches events whose type is known only as a
/// <see cref="Type"/> instance recovered from <c>events.event_type</c> via
/// <c>EventSerializer</c>.
///
/// <para>
/// Sub-Sprint 2 (S44) picks the dispatch shape at endpoint wire time. Sub-Sprint 1
/// (S43) ships both — direct DI plus the registry — so neither bakes in
/// before the ergonomics surface in S44.
/// </para>
/// </summary>
public interface IAuditProjectionMapperRegistry
{
    /// <summary>
    /// Look up the registered <see cref="IAuditProjectionMapper{TEvent}"/>
    /// for <paramref name="eventType"/>, or <c>null</c> when none is
    /// registered (the backfill loop logs WARN and skips — Sub-Sprint 2 will
    /// populate the ~53 mappers).
    /// </summary>
    /// <param name="eventType">Closed event type recovered from
    /// <c>events.event_type</c> via <c>EventSerializer</c>.</param>
    /// <returns>The mapper instance typed as <c>object</c>; callers invoke
    /// <c>Map</c> via reflection or known-type dispatch. The returned object's
    /// concrete type is <see cref="IAuditProjectionMapper{TEvent}"/> with
    /// <c>TEvent = eventType</c>.</returns>
    object? GetMapperFor(Type eventType);

    /// <summary>
    /// Non-generic dispatch helper — resolves the mapper for
    /// <c>@event.GetType()</c> and invokes <c>Map</c>, returning the
    /// projection row data or <c>null</c> when no mapper is registered for
    /// that event type. Used by <c>AuditProjectionBackfillService</c> which
    /// iterates events by stored type and can't statically resolve the
    /// closed-generic mapper interface.
    /// </summary>
    AuditProjectionRowData? TryMap(object @event, AuditProjectionContext context);

    /// <summary>
    /// Set of registered event type names (matching
    /// <c>EventSerializer.Serialize</c>'s <c>EventType</c> property + the
    /// <c>events.event_type</c> column value). Used by
    /// <c>AuditProjectionBackfillService</c> to narrow the SELECT by event
    /// type so the backfill doesn't scan the full events log when only a
    /// subset have mappers. Empty when no mappers are registered (Sub-Sprint
    /// 1 default) — backfill returns 0 rows scanned and skips the loop.
    /// Per Step 7a cycle 1 Codex W1 absorption.
    /// </summary>
    IReadOnlyCollection<string> RegisteredEventTypeNames { get; }
}

/// <summary>
/// Marker registration used by the audit projection mapper DI pipeline.
/// Sub-Sprint 2 mapper registration extension methods will add one
/// <see cref="RegisteredAuditEventType"/> instance per mapper alongside
/// the <see cref="IAuditProjectionMapper{TEvent}"/> registration so the
/// registry can enumerate the registered set without IServiceProvider
/// introspection.
/// </summary>
public sealed record RegisteredAuditEventType(Type EventType, string EventTypeName);
