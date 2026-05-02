namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Per-row local-configuration mutation event (pre-S21 — patch-bag shape).
///
/// <strong>Post-S21: emission retired; deserialization preserved for pre-S21 event-store
/// history.</strong> The per-row API that emitted this event was removed by ADR-017 D5 in
/// favor of the profile-shaped <see cref="LocalAgreementProfileChanged"/>. This class stays
/// registered in <c>EventSerializer</c> indefinitely so existing audit queries and replay
/// paths against pre-S21 events continue to deserialize correctly (ADR-002 append-only
/// invariant — old events are never rewritten).
/// </summary>
public sealed class LocalConfigurationChanged : DomainEventBase
{
    public override string EventType => "LocalConfigurationChanged";

    public required Guid ConfigId { get; init; }
    public required string OrgId { get; init; }
    public required string ConfigArea { get; init; }
    public required string ConfigKey { get; init; }
    public required string ConfigValue { get; init; }
    public string? PreviousValue { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
}
