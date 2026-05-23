using Microsoft.Extensions.DependencyInjection;
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
/// </summary>
public sealed class AuditProjectionMapperRegistry : IAuditProjectionMapperRegistry
{
    private readonly IServiceProvider _serviceProvider;

    public AuditProjectionMapperRegistry(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object? GetMapperFor(Type eventType)
    {
        var mapperType = typeof(IAuditProjectionMapper<>).MakeGenericType(eventType);
        return _serviceProvider.GetService(mapperType);
    }
}
