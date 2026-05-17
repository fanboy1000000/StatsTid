namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S34 / TASK-3404 (ADR-023 D2 option (b)) — Phase 4e replay-data trail for the
/// FIRST-EVER agreement-code assignment to a user. Signals "no predecessor".
///
/// <para>
/// <b>Emission sites.</b> Emitted by <c>UserAgreementCodeBackfillSeeder</c> at
/// bootstrap (TASK-3403) — one event per existing user when the dated
/// <c>user_agreement_codes</c> history table is first populated — and by
/// <c>AdminEndpoints POST /api/admin/users</c> (TASK-3407) for net-new
/// admin-created users at INSERT time. Rides the same atomic tx as the
/// associated <see cref="UserCreated"/> emission per ADR-018 D3.
/// </para>
///
/// <para>
/// <b>Why a separate event from <see cref="UserAgreementCodeChanged"/>?</b>
/// Seeded carries no predecessor (<c>OldAgreementCode</c> deliberately ABSENT);
/// Changed always has both old and new. Keeping the two shapes distinct makes
/// the Phase 4e replay-data trail walkable without nullable-old-value parsing
/// and aligns with the lifecycle distinction surfaced in <c>EntitlementConfig</c>
/// and <c>EmployeeProfile</c> sibling lineages.
/// </para>
///
/// <para>
/// <b>EffectiveFrom is DateOnly</b> (matches S33 <see cref="UserAgreementCodeChanged"/>
/// — Phase 4e DateOnly predicates align with the resolver's asOfDate parameter
/// without truncation logic).
/// </para>
///
/// <para>
/// <b>Actor/correlation propagation.</b> <c>ActorId</c>, <c>ActorRole</c>, and
/// <c>CorrelationId</c> are inherited from <see cref="DomainEventBase"/> and
/// populated by the emission site at init-time per project-wide convention.
/// For the bootstrap seeder, ActorId/ActorRole reflect the system principal.
/// </para>
/// </summary>
public sealed class UserAgreementCodeSeeded : DomainEventBase
{
    public override string EventType => "UserAgreementCodeSeeded";

    public required string UserId { get; init; }
    public required string AgreementCode { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public required long RowVersion { get; init; }
}
