namespace StatsTid.SharedKernel.Events;

public sealed class AbsenceRegistered : DomainEventBase
{
    public override string EventType => "AbsenceRegistered";
    public required string EmployeeId { get; init; }
    public required DateOnly Date { get; init; }
    public required string AbsenceType { get; init; }
    public required decimal Hours { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }

    /// <summary>
    /// The per-absence consumed feriedage (entitlement day-equivalents) computed at booking
    /// per ADR-032 D1/D2 — the authoritative consumption record. Nullable and NOT required:
    /// pre-S66 serialized events lack this field and must keep deserializing to null
    /// (ADR-032 D2 backward-compat contract); they are backfilled as hours/7.4 by
    /// ProjectionBackfillService.
    /// </summary>
    public decimal? Feriedage { get; init; }
}
