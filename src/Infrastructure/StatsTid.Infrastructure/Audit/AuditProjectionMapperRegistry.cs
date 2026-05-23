using System.Collections.Concurrent;
using System.Reflection;
using StatsTid.SharedKernel.Audit;

namespace StatsTid.Infrastructure.Audit;

/// <summary>
/// S43 / ADR-026 D2 dispatch-lookup implementation. Resolves
/// <see cref="IAuditProjectionMapper{TEvent}"/> instances from the
/// <see cref="IServiceProvider"/> via the closed generic type
/// <c>IAuditProjectionMapper&lt;eventType&gt;</c>.
///
/// <para>
/// Lives in <c>Infrastructure</c> (NOT <c>SharedKernel</c>) so the
/// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> dependency
/// stays out of <c>SharedKernel</c> per the post-S19 <c>b4fc670</c>
/// cleanup discipline (RuleEngine.Api references SharedKernel without
/// pulling DI packages).
/// </para>
///
/// <para>
/// <see cref="TryMap"/> resolves the mapper for the runtime event type and
/// invokes <c>Map</c> via cached <see cref="MethodInfo"/> reflection so
/// dispatch cost stays bounded across the backfill loop (one MethodInfo
/// lookup per distinct event type, not per row).
/// </para>
/// </summary>
public sealed class AuditProjectionMapperRegistry : IAuditProjectionMapperRegistry
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<Type, MethodInfo?> _mapMethodCache = new();
    private readonly HashSet<string> _registeredEventTypeNames;

    public AuditProjectionMapperRegistry(
        IServiceProvider serviceProvider,
        IEnumerable<RegisteredAuditEventType>? registeredEventTypes = null)
    {
        _serviceProvider = serviceProvider;
        _registeredEventTypeNames = (registeredEventTypes ?? Array.Empty<RegisteredAuditEventType>())
            .Select(r => r.EventTypeName)
            .ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> RegisteredEventTypeNames => _registeredEventTypeNames;

    public object? GetMapperFor(Type eventType)
    {
        var mapperType = typeof(IAuditProjectionMapper<>).MakeGenericType(eventType);
        return _serviceProvider.GetService(mapperType);
    }

    public AuditProjectionRowData? TryMap(object @event, AuditProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var eventType = @event.GetType();
        var mapper = GetMapperFor(eventType);
        if (mapper is null) return null;

        var mapMethod = _mapMethodCache.GetOrAdd(eventType, t =>
            typeof(IAuditProjectionMapper<>).MakeGenericType(t).GetMethod("Map"));
        if (mapMethod is null) return null;

        return (AuditProjectionRowData?)mapMethod.Invoke(mapper, new object[] { @event, context });
    }
}
