namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S70 / ADR-033 slice 3a (SPRINT-70 R2). Emitted by the <c>SettlementCloseService</c> Step-A
/// poller when a FUTURE-dated <c>employment_end_date</c> passes and the deferred deactivation
/// flip applies: <c>is_active=false</c> + <c>end_date_deactivated=true</c> (provenance) +
/// <c>users.version</c> bump (ADR-018 D7), all in the SAME per-leaver advisory-locked tx (R12)
/// together with the R1(e) side effects, the outbox append and the ADR-026 audit projection.
///
/// Step A is UNGATED by the D13 <c>Settlement:GoLiveDate</c> — the flip is a lifecycle fact,
/// not a settlement (R2). Same-tx deactivation at SET time rides
/// <see cref="EmployeeEmploymentEndDateSet"/> instead; THIS event is exclusively the poller's
/// deferred flip.
///
/// Stream: <c>employee-{EmployeeId}</c>. SYSTEM actor per the settlement actor convention
/// (base <see cref="DomainEventBase.ActorId"/> = <c>system:settlement-close:…</c>,
/// <see cref="DomainEventBase.ActorRole"/> = <c>"System"</c> — the S68
/// <c>VacationSettlementService</c> precedent).
/// </summary>
public sealed class EmployeeEndDateDeactivationApplied : DomainEventBase
{
    public override string EventType => "EmployeeEndDateDeactivationApplied";

    public required string EmployeeId { get; init; }

    /// <summary>The passed <c>employment_end_date</c> that triggered the flip (the LAST day employed, R1).</summary>
    public required DateOnly EndDate { get; init; }

    // is_active transition (the flip's predicate re-evaluates is_active=TRUE in the UPDATE's WHERE, R2)
    public required bool OldIsActive { get; init; }
    public required bool NewIsActive { get; init; }

    // Optimistic-concurrency row-version transition (ADR-018 D7 — a held ETag must not survive the flip)
    public required long VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
}
