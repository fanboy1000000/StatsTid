namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S148 C-10 (ADR-043 D9). One event per write to an institution's customary days (create,
/// withdraw, reinstate), carrying the row's state AFTER the write (the
/// <see cref="LocalAgreementProfileChanged"/> precedent of one event per family). Emitted on the
/// organisation's stream; rides ONE transaction with the <c>customary_days</c> row, its
/// <c>customary_days_audit</c> row and the outbox append (ADR-018 D3). Nothing emits it yet; its
/// emitter is TASK-14834, and the emission described is the intended one.
///
/// The day is either recurring (<see cref="RecurringMonth"/> + <see cref="RecurringDay"/>) or a
/// <see cref="SingleDate"/>. <see cref="ValidTo"/> is end-exclusive (ADR-018 D8). Every nullable
/// member is NON-required (S66 round-trip lesson).
/// </summary>
public sealed class CustomaryDayChanged : DomainEventBase
{
    public override string EventType => "CustomaryDayChanged";

    public required Guid CustomId { get; init; }
    public required string OrgId { get; init; }

    /// <summary>CREATED, WITHDRAWN or REINSTATED.</summary>
    public required string Action { get; init; }

    public string? AgreementCode { get; init; }
    public int? RecurringMonth { get; init; }
    public int? RecurringDay { get; init; }
    public DateOnly? SingleDate { get; init; }
    public required string Kind { get; init; }
    public required string Part { get; init; }
    public TimeOnly? PartFromTime { get; init; }
    public decimal? PartHours { get; init; }
    public string? PaidMode { get; init; }
    public required DateOnly ValidFrom { get; init; }
    public DateOnly? ValidTo { get; init; }

    public Guid? PrecedingCustomId { get; init; }
    public long? VersionBefore { get; init; }
    public required long VersionAfter { get; init; }
}
