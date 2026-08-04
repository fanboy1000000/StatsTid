using StatsTid.SharedKernel.Events;

namespace StatsTid.SharedKernel.Interfaces;

public interface IEventStore
{
    Task AppendAsync(string streamId, IDomainEvent @event, CancellationToken ct = default);
    Task<IReadOnlyList<IDomainEvent>> ReadStreamAsync(string streamId, CancellationToken ct = default);
    Task<IReadOnlyList<IDomainEvent>> ReadAllAsync(int fromPosition = 0, int maxCount = 1000, CancellationToken ct = default);

    /// <summary>
    /// S126 / F5 — the MOST RECENT event of type <typeparamref name="T"/> on a stream, or
    /// <c>null</c> if the stream carries none.
    ///
    /// <para><b>Why this exists.</b> Three read paths wanted exactly one fact — the latest
    /// <c>FlexBalanceUpdated</c> — and got it by calling <see cref="ReadStreamAsync"/> and running
    /// <c>.OfType&lt;T&gt;().LastOrDefault()</c> over the result. On the CONSOLIDATED
    /// <c>employee-{id}</c> stream (ADR-018 D6) that means loading and JSON-deserializing every
    /// event the employee has ever accumulated — time registrations, entitlement revaluations,
    /// waivers, feriehindring, termination payouts — to read one decimal. The stream grows with
    /// every time registration, so the cost grows with employment length and is invisible in demo
    /// data (which ships zero time registrations).</para>
    ///
    /// <para><b>Equivalence.</b> <c>ORDER BY stream_version DESC LIMIT 1</c> names the same row as
    /// <c>LastOrDefault()</c> over a <c>stream_version ASC</c> read, because
    /// <c>UNIQUE (stream_id, stream_version)</c> makes the ordering total. One documented behaviour
    /// difference: the old form deserialized every preceding event, so a malformed or
    /// unregistered <c>event_type</c> anywhere on the stream threw; this form never reads those
    /// rows. Strictly more robust, but it IS a difference, not a pure equivalence.</para>
    /// </summary>
    Task<T?> ReadLatestOfTypeAsync<T>(string streamId, CancellationToken ct = default)
        where T : class, IDomainEvent;
}
