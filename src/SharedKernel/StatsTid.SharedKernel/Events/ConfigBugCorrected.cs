namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Generalized correction-policy event covering bug-fixes across all five config
/// surfaces (agreement_configs / entitlement_configs / wage_type_mappings /
/// position_override_configs / role_config_overrides). Records the from/to
/// values, the source attribution, the classifier label, and the action taken.
/// <para>
/// <c>ConfigSurface</c> values: <c>agreement_configs</c>, <c>entitlement_configs</c>,
/// <c>wage_type_mappings</c>, <c>position_override_configs</c>, <c>role_config_overrides</c>.
/// </para>
/// <para>
/// <c>Action</c> values: <c>bug-fix-without-recompute</c>, <c>bug-fix-with-recompute</c>,
/// <c>decision-recorded-fix-deferred</c>, <c>provisional-pending-phase-b</c>.
/// </para>
/// ADR-024 D6 generalized correction policy. Sprint 40 / Phase 4d-4.
/// </summary>
public sealed class ConfigBugCorrected : DomainEventBase
{
    public override string EventType => "ConfigBugCorrected";

    public required string ConfigSurface { get; init; }
    public required string ConfigKey { get; init; }
    public required string FromValue { get; init; }
    public required string ToValue { get; init; }
    public required string Source { get; init; }
    public required string Classifier { get; init; }
    public required string Action { get; init; }
}
