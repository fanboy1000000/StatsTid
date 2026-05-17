namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S33 / TASK-3304 (ADR-023 D2) — Phase 4e replay-data trail for admin mutation
/// of <c>users.agreement_code</c>. No consumer in S33; emitted by
/// AdminEndpoints PUT <c>/api/admin/users/{userId}</c> (TASK-3309) whenever
/// the narrow predicate
/// <c>request.AgreementCode is not null &amp;&amp; request.AgreementCode != existingUser.AgreementCode</c>
/// holds. Rides the same atomic tx as <see cref="UserUpdated"/> per ADR-018 D3.
///
/// <para>
/// <b>Why a separate event when UserUpdated already carries AgreementCode?</b>
/// UserUpdated fires on every PUT regardless of whether agreement_code mutated;
/// UserAgreementCodeChanged fires ONLY when agreement_code mutated. The narrow
/// signal makes the Phase 4e production-readiness replay-data trail walkable
/// without parsing every UserUpdated event's old-vs-new diff.
/// </para>
///
/// <para>
/// <b>Documented determinism gap (ADR-023 D2).</b> agreement_code stays on
/// the live <c>users</c> table; admin mutation flips replays of past
/// PCS-routed calculations for that employee. Pre-launch posture accepts
/// this; Phase 4e launch-blocking work will move agreement_code into a
/// dated history table and consume these events to backfill that history.
/// </para>
///
/// <para>
/// <b>EffectiveFrom is DateOnly</b> (refinement cycle 1 Reviewer W1
/// absorption — pins the type explicitly so Phase 4e DateOnly predicates
/// match the resolver's asOfDate parameter without truncation logic).
/// </para>
///
/// <para>
/// <b>Actor/correlation propagation.</b> <c>ActorId</c>, <c>ActorRole</c>,
/// and <c>CorrelationId</c> are inherited from <see cref="DomainEventBase"/>
/// and populated by the AdminEndpoints emission site (TASK-3309) at
/// init-time, matching the project-wide convention.
/// </para>
/// </summary>
public sealed class UserAgreementCodeChanged : DomainEventBase
{
    public override string EventType => "UserAgreementCodeChanged";

    public required string UserId { get; init; }
    public required string OldAgreementCode { get; init; }
    public required string NewAgreementCode { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
}
