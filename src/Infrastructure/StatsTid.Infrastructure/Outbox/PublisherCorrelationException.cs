namespace StatsTid.Infrastructure.Outbox;

/// <summary>
/// Thrown by <see cref="OutboxPublisher"/> when an <c>events</c>-table INSERT
/// raises <c>PostgresException</c> with <c>SqlState == "23505"</c> (UNIQUE
/// violation) AND the lookup-by-<c>event_id</c> recovery branch finds no
/// matching row in <c>events</c>. Per ADR-018 D4, this means the
/// <c>(stream_id, stream_version)</c> slot was claimed by a DIFFERENT
/// outbox row's event — the current outbox row should NOT be marked
/// published, and the situation requires manual reconcile.
///
/// <para>
/// Under normal operation (D6 single-writer-per-stream + D4 step-2 FOR UPDATE
/// serialization), this branch is unreachable: each <c>stream_id</c> has
/// exactly one writer service, and that service's per-service publisher
/// serializes via <c>SELECT 1 FROM event_streams WHERE stream_id = @id FOR UPDATE</c>.
/// The exception exists as a defensive surface to make the violation loud
/// rather than silently mark the wrong outbox row published.
/// </para>
///
/// <para>
/// Recovery: the publisher rolls back its own tx, increments
/// <c>attempts</c> + <c>last_error</c> + <c>last_attempt_at</c> on the outbox
/// row, and surfaces this exception to the operator dashboard. Manual
/// reconciliation determines which outbox row (if any) belongs to the
/// existing canonical event and which should be expired.
/// </para>
/// </summary>
public sealed class PublisherCorrelationException : Exception
{
    public PublisherCorrelationException(string message)
        : base(message)
    {
    }

    public PublisherCorrelationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
