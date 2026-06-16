using StatsTid.SharedKernel.Models;

namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S68 / ADR-033 D5 (§22) — contract fixed in slice 1a; modeled in slice 4 (SPRINT-79).
/// Feriehindring (sickness/barsel) transfer of the impeded tranche into the next entitlement
/// year's <c>carryover_in</c>. Like §21, this is BALANCE-ONLY — it emits NO payroll line
/// (ADR-033 D1/D7). §22 RESCUES days from the §34 forfeiture bucket (<c>forfeit_days</c>):
/// the resolve handler computes forfeit_days := forfeit_days - impeded and credits the rescued
/// days as <see cref="TransferDays"/>. The §22 transfer is SEPARATELY-CAPPED from §21
/// (≤4 weeks / 20 days) — it is NOT a combined ceiling and does NOT take precedence over §21.
///
/// NOT auto-executed pre-modeling (ADR-033 D10) — the impediment signal is resolved via the
/// PENDING_REVIEW path, which slice 4 settles to the FERIEHINDRING disposition. The event is
/// emitted on resolution so replay reproduces the rescued day-count AND the audit rationale.
///
/// Stream: <c>employee-{EmployeeId}</c>. ROUND-TRIPPABILITY per the S66 e0d1dc3 lesson
/// (non-required nullable snapshot + reason + defaulted day-count).
/// </summary>
public sealed class FeriehindringTransferred : DomainEventBase
{
    public override string EventType => "FeriehindringTransferred";

    // Settlement identity (ADR-033 D5).
    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public int EntitlementYear { get; init; }
    public int Sequence { get; init; }

    /// <summary>The immutable settle-time input snapshot (ADR-033 D3).</summary>
    public VacationSettlementSnapshot? Snapshot { get; init; }

    /// <summary>The §22 feriehindring transfer bucket day-count (<c>feriehindring_transfer_days</c>; ADR-033 D5).</summary>
    public decimal TransferDays { get; init; }

    /// <summary>
    /// The durable §22 impediment rationale (sickness/barsel etc.) — the PRIMARY home for the
    /// reason, so replay reproduces the audit record. The <c>feriehindring_reason</c> settlement
    /// column is a queryable mirror. Nullable for round-trip-safety (non-required nullable member);
    /// the slice-4 resolve path always populates it on a FERIEHINDRING disposition.
    /// </summary>
    public string? FeriehindringReason { get; init; }
}
