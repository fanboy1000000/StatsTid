namespace StatsTid.SharedKernel.Events;

/// <summary>
/// Emitted on every successful profile save (CREATE, SUPERSEDE, or DEACTIVATE).
/// One event per save — delta payload carries per-field old/new pairs (ADR-017 D6).
/// PrecedingProfileId enables walking the supersession chain for audit reconstruction.
///
/// Pre-S21 events use the per-row LocalConfigurationChanged type; that type stays
/// registered in EventSerializer for historical replay (ADR-017 D7) but is not emitted
/// post-S21.
///
/// ActorId, ActorRole, CorrelationId, EventId, OccurredAt, and Version are inherited
/// from <see cref="DomainEventBase"/> — do not redeclare here (PAT-004).
/// </summary>
public sealed class LocalAgreementProfileChanged : DomainEventBase
{
    public override string EventType => "LocalAgreementProfileChanged";

    public required Guid ProfileId { get; init; }
    public required string OrgId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required DateOnly EffectiveFrom { get; init; }

    /// <summary>
    /// Per-field old/new pairs describing the delta between the predecessor profile and the
    /// new row (ADR-017 D6).
    /// </summary>
    /// <remarks>
    /// Not marked <c>required</c> so the property defaults to an empty dictionary rather
    /// than <c>null</c>. This matches the <see cref="SegmentManifestCreated"/> precedent
    /// (ADR-016 D10) and lets the EventSerializer coverage round-trip test instantiate
    /// the type generically. Producers (the profile-write endpoint) always populate this
    /// with the full delta — empty only when the test harness builds an uninitialised
    /// instance.
    /// </remarks>
    public IReadOnlyDictionary<string, FieldChange> ChangedFields { get; init; }
        = new Dictionary<string, FieldChange>();

    /// <summary>
    /// The profile_id of the predecessor row that this save closed (set effective_to=today
    /// on). Null on first creation (no predecessor) or on standalone deactivation
    /// (DEACTIVATED action with no successor — see ADR-017 D1's deactivation-without-
    /// supersession case).
    /// </summary>
    public Guid? PrecedingProfileId { get; init; }
}
