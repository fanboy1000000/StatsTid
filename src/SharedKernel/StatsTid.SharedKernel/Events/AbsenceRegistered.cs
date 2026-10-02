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

    /// <summary>
    /// Which kind of write produced this absence (S145 / A0, QUAL-192/193): <c>SKEMA</c> for a
    /// Skema save, which REPLACES the cell's earlier row; null means <c>TIME_POST</c>, an
    /// additive registration. Nullable and NOT required, so every pre-S145 serialized event
    /// keeps deserializing to null, which is exactly <c>TIME_POST</c>: events stay append-only
    /// and the old ones stay additive. Resolve with
    /// <see cref="Models.HoursSource.Resolve(string?)"/>; the projection stores the resolved value.
    /// </summary>
    public string? Source { get; init; }
}
