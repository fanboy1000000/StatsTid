namespace StatsTid.Infrastructure.Outbox;

/// <summary>
/// Per-service identity record for the outbox topology (ADR-018 D2 / D6).
///
/// <para>
/// Each service that hosts an <see cref="OutboxPublisher"/> registers a singleton
/// <c>OutboxServiceContext</c> at startup carrying its own <c>service_id</c>
/// (e.g. <c>"backend-api"</c>, <c>"payroll"</c>, <c>"external"</c>). The context
/// is consumed by:
/// </para>
/// <list type="bullet">
/// <item><see cref="PostgresEventStore.EnqueueAsync"/> — stamps <c>service_id</c>
/// on each row inserted into <c>outbox_events</c>, scoping the row to the
/// publisher partition that owns the originating service's streams.</item>
/// <item><see cref="OutboxPublisher"/> — narrows the polling query to
/// <c>WHERE service_id = @ownServiceId</c> so a service publishes only its own
/// outbox partition (ADR-018 D2 per-service publisher topology).</item>
/// </list>
/// <para>
/// Per ADR-018 D6, each <c>stream_id</c> is owned by exactly one service. The
/// <c>service_id</c> on each outbox row is the structural pointer back to the
/// owning publisher partition. Orchestrator MAY NOT register a
/// <see cref="OutboxPublisher"/> nor an <c>OutboxServiceContext</c>.
/// </para>
/// </summary>
/// <param name="ServiceId">The service identifier; must match one of the
/// allowed values per ADR-018 D6 stream-ownership table:
/// <c>"backend-api"</c>, <c>"payroll"</c>, <c>"external"</c>.</param>
public sealed record OutboxServiceContext(string ServiceId);
