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
}
