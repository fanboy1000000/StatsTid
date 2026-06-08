using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S68 / ADR-033 D5/D10 — the PENDING_REVIEW signal. Emitted ONCE (idempotent on the settlement
/// identity) when an atomic close auto-resolves the §21/§24/§26/godtgørelse buckets but finds an
/// untaken-beyond-transfer remainder that may be §34 forfeiture vs §22 feriehindring — which a
/// human MUST disposition (auto-forfeiting a possibly-feriehindret employee is a legal violation,
/// ADR-033 D10). The settlement row is committed in <c>PENDING_REVIEW</c> in the same tx; the D3
/// due-check treats PENDING_REVIEW as NOT-due, so the poll does not re-flag (no alert storm).
///
/// Carries the same immutable <see cref="Snapshot"/> + identity as the other settlement events so
/// the operator-review surface and replay see the exact settle-time inputs. <see cref="FlaggedDays"/>
/// is the un-auto-resolved remainder the operator must partition into §34 vs §22.
///
/// Stream: <c>employee-{EmployeeId}</c>. ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson
/// (non-required nullable snapshot + defaulted day-count).
/// </summary>
public sealed class SettlementManualReviewFlagged : DomainEventBase
{
    public override string EventType => "SettlementManualReviewFlagged";

    // Settlement identity (ADR-033 D5).
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }
    public int Sequence { get; init; }

    /// <summary>The immutable settle-time input snapshot (ADR-033 D3).</summary>
    public VacationSettlementSnapshot? Snapshot { get; init; }

    /// <summary>
    /// The un-auto-resolved remainder (days) the operator must disposition into §34 forfeiture
    /// vs §22 feriehindring (ADR-033 D10). Informational on this signal event — the authoritative
    /// bucket day-counts are emitted by the resolving §34/§22/§25 events.
    /// </summary>
    public decimal FlaggedDays { get; init; }
}
