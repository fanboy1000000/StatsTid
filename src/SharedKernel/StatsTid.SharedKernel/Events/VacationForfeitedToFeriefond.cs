using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S68 / ADR-033 D5 (§34). Forfeiture of untaken-not-transferred-not-paid ferie days to
/// Arbejdsmarkedets Feriefond. This is the figure the S66 D9 disposition row displays as the
/// VACATION "Til udløb" / Feriefonden-lost quantity.
///
/// NOT auto-executed pre-modeling (ADR-033 D10): auto-forfeiting an employee who is or may be
/// feriehindret (§22) is a legal violation. Until the impediment signal is modeled (slice 4),
/// a forfeiture-candidate remainder routes the close to PENDING_REVIEW + a
/// <see cref="SettlementManualReviewFlagged"/> signal; a human operator's resolution is what
/// ultimately emits THIS event. The contract is defined now so the operator-resolution path
/// (and slice-4 automation) emit a registered, replayable event.
///
/// Stream: <c>employee-{EmployeeId}</c>. Replay reads <see cref="Snapshot"/> verbatim (ADR-033 D3).
/// ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson (non-required nullable snapshot + defaulted day-count).
/// </summary>
public sealed class VacationForfeitedToFeriefond : DomainEventBase
{
    public override string EventType => "VacationForfeitedToFeriefond";

    // Settlement identity (ADR-033 D5).
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }
    public int Sequence { get; init; }

    /// <summary>The immutable settle-time input snapshot (ADR-033 D3).</summary>
    public VacationSettlementSnapshot? Snapshot { get; init; }

    /// <summary>The §34 forfeiture bucket day-count (<c>forfeit_days</c>; ADR-033 D5 shared vocabulary).</summary>
    public decimal ForfeitDays { get; init; }
}
