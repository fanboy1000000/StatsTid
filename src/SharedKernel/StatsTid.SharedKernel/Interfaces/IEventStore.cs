using StatsTid.SharedKernel.Events;

namespace StatsTid.SharedKernel.Interfaces;

public interface IEventStore
{
    Task AppendAsync(string streamId, IDomainEvent @event, CancellationToken ct = default);
    Task<IReadOnlyList<IDomainEvent>> ReadStreamAsync(string streamId, CancellationToken ct = default);
    Task<IReadOnlyList<IDomainEvent>> ReadAllAsync(int fromPosition = 0, int maxCount = 1000, CancellationToken ct = default);
}
