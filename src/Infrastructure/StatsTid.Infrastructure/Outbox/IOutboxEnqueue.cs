using Npgsql;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Infrastructure.Outbox;

/// <summary>
/// In-tx event-emit surface for state-change sites (Backend.Api, Payroll, External).
///
/// <para>
/// Lives in <c>StatsTid.Infrastructure.Outbox</c> rather than alongside
/// <see cref="StatsTid.SharedKernel.Interfaces.IEventStore"/> because the parameter
/// list exposes <see cref="NpgsqlConnection"/> / <see cref="NpgsqlTransaction"/>.
/// Putting those on a SharedKernel interface would force an Npgsql package
/// reference onto <c>StatsTid.SharedKernel</c>, which would transitively reach
/// <c>StatsTid.RuleEngine.Api</c> and regress the post-S19 <c>b4fc670</c>
/// assembly-graph cleanup that keeps the rule engine Npgsql-free. See ADR-018 D3
/// (cycle-6 split-interface design) for the full rationale.
/// </para>
/// <para>
/// The single concrete implementation <c>PostgresEventStore</c> implements both
/// <see cref="StatsTid.SharedKernel.Interfaces.IEventStore"/> (read/append surface
/// for publishers and historical readers) and this interface (in-tx enqueue
/// surface for state-change sites). DI registers the concrete once and exposes
/// it under both contracts.
/// </para>
/// </summary>
public interface IOutboxEnqueue
{
    /// <summary>
    /// Enqueues an event into <c>outbox_events</c> within the caller-supplied
    /// transaction. The caller commits or rolls back; outbox visibility follows
    /// tx commit/rollback. A separate per-service <c>OutboxPublisher</c> drains
    /// <c>outbox_events</c> to the canonical event store with at-least-once
    /// semantics (see ADR-018 D4).
    /// </summary>
    /// <param name="conn">The caller's open <see cref="NpgsqlConnection"/>; the
    /// enqueue INSERT runs on this connection so it participates in <paramref name="tx"/>.</param>
    /// <param name="tx">The caller's active <see cref="NpgsqlTransaction"/>; the
    /// enqueue INSERT joins this transaction. Outbox visibility is bound to
    /// <paramref name="tx"/>.Commit / <paramref name="tx"/>.Rollback.</param>
    /// <param name="streamId">The event-stream identifier
    /// (e.g. <c>local-agreement-profile-{org}-{agreement}-{ok_version}</c>).
    /// Per ADR-018 D6 each <c>stream_id</c> is owned by exactly one service.</param>
    /// <param name="event">The domain event to enqueue. The implementation
    /// assigns a fresh <c>event_id</c> per ADR-018 D1 and stores it on the
    /// outbox row for at-least-once correlation at publish time.</param>
    /// <param name="ct">Cancellation token.</param>
    Task EnqueueAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string streamId,
        IDomainEvent @event,
        CancellationToken ct = default);

    /// <summary>
    /// Same enqueue contract as <see cref="EnqueueAsync"/>, but returns the
    /// freshly-allocated <c>outbox_id BIGSERIAL</c> assigned by PostgreSQL via
    /// <c>RETURNING outbox_id</c>. S27 Phase 4c.6 atomic write paths
    /// (TASK-2706 Skema, TASK-2707 Time) capture this value at write time and
    /// stamp it on the corresponding read-path projection row
    /// (<c>time_entries_projection</c> / <c>absences_projection</c>) inside the
    /// same transaction so projections are visible synchronously without
    /// waiting for the per-service <see cref="OutboxPublisher"/> drain
    /// (read-your-write per ADR-018 D3 + Phase 4c.6 projection-table design).
    /// <para>
    /// This is an OVERLOAD added in S27 — the existing <see cref="EnqueueAsync"/>
    /// signature is preserved so all 31 S22-S26 callers compile unchanged.
    /// Implementations must produce identical persistent state for both
    /// overloads (same column set, same values); the only difference is that
    /// this overload surfaces the assigned <c>outbox_id</c> rather than
    /// discarding it.
    /// </para>
    /// </summary>
    /// <param name="conn">The caller's open <see cref="NpgsqlConnection"/>; the
    /// enqueue INSERT runs on this connection so it participates in <paramref name="tx"/>.</param>
    /// <param name="tx">The caller's active <see cref="NpgsqlTransaction"/>; the
    /// enqueue INSERT joins this transaction. Outbox visibility is bound to
    /// <paramref name="tx"/>.Commit / <paramref name="tx"/>.Rollback.</param>
    /// <param name="streamId">The event-stream identifier (see <see cref="EnqueueAsync"/>).</param>
    /// <param name="event">The domain event to enqueue (see <see cref="EnqueueAsync"/>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The freshly-allocated <c>outbox_id BIGSERIAL</c> assigned by
    /// PostgreSQL for the inserted row.</returns>
    Task<long> EnqueueAndReturnIdAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string streamId,
        IDomainEvent @event,
        CancellationToken ct = default);
}
