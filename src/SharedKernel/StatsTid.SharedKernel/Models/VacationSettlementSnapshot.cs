namespace StatsTid.SharedKernel.Models;

/// <summary>
/// S68 / ADR-033 D3 — the immutable settle-time INPUT snapshot for one
/// <c>(employee, entitlement_type, entitlement_year)</c> boundary close.
///
/// The settled quantity is a pure function of THIS snapshot, captured atomically at
/// settle time (ADR-033 D3 quantity-determinism, P2/P4): replay reads the recorded
/// snapshot verbatim and NEVER re-derives. It is carried in every settlement event's
/// payload and is serialized into <c>vacation_settlements.snapshot jsonb</c> by the
/// settlement repository (TASK-6801).
///
/// Immutable init-only record (PAT-001). Pure value object — no I/O, no DB access.
///
/// Fields (each pinned as-of the boundary date, per ADR-033 D3):
/// <list type="bullet">
///   <item>the recorded per-absence <c>feriedage</c> (ADR-032 D2 — the authoritative "used" input);</item>
///   <item>the closed-year balance: earned / used / planned / carryover_in;</item>
///   <item>the dated config: annual_quota / carryover_max / reset_month / ok_version;</item>
///   <item>the §21 transfer-agreement days (ADR-033 D8 record; 0 when no agreement);</item>
///   <item>the impediment (feriehindring) status (ADR-033 D10).</item>
/// </list>
///
/// ROUND-TRIPPABILITY (ADR-033 / EventSerializerCoverageTests, S66 e0d1dc3 lesson):
/// every settlement event references this object as a NON-required, nullable property,
/// so the coverage test's <c>GetUninitializedObject</c> instance (which leaves the
/// reference null) still serializes + deserializes cleanly. This object's own members
/// are value-typed-with-defaults plus one defaulted collection, so a real production
/// instance round-trips with no required-initializer enforcement at deserialize time.
/// </summary>
public sealed record VacationSettlementSnapshot
{
    /// <summary>
    /// The recorded per-absence <c>feriedage</c> consumed within the closed entitlement year
    /// (ADR-032 D2 — <c>absences_projection.feriedage</c>, the authoritative consumption record).
    /// The settle-time "used" input is the sum of these; carrying the components keeps the
    /// snapshot auditable. Defaulted to an empty list so an uninitialized instance is
    /// round-trippable (the coverage test prefills collections; production binds the real set).
    /// </summary>
    public IReadOnlyList<RecordedAbsenceFeriedage> RecordedAbsences { get; init; }
        = new List<RecordedAbsenceFeriedage>();

    // --- Closed-year balance (ADR-033 D3; the EntitlementBalance operands at the boundary) ---

    /// <summary>Days earned at the boundary (AccrualMath.EarnedToDate crystallized as-of the boundary date).</summary>
    public decimal Earned { get; init; }

    /// <summary>Days used in the closed year (the recorded-feriedage total; the authoritative "used").</summary>
    public decimal Used { get; init; }

    /// <summary>Days planned-but-not-yet-taken at the boundary.</summary>
    public decimal Planned { get; init; }

    /// <summary>Carryover days that entered the closed year (carryover_in of the year being settled).</summary>
    public decimal CarryoverIn { get; init; }

    // --- Dated config pinned as-of the boundary (ADR-033 D3) ---

    /// <summary>The dated <c>annual_quota</c> in force at the boundary.</summary>
    public decimal AnnualQuota { get; init; }

    /// <summary>The dated <c>carryover_max</c> in force at the boundary (the §21 transfer ceiling operand).</summary>
    public decimal CarryoverMax { get; init; }

    /// <summary>The dated <c>reset_month</c> (entitlement-year boundary month) in force at the boundary.</summary>
    public int ResetMonth { get; init; }

    /// <summary>
    /// The OK-version in force at the boundary (version correctness, priority 4). Reference type;
    /// left null only on an uninitialized coverage-test instance — production always binds it.
    /// </summary>
    public string? OkVersion { get; init; }

    // --- §21 transfer agreement (ADR-033 D8) ---

    /// <summary>
    /// The written-agreement transfer days from the §21 <c>vacation_transfer_agreements</c> record
    /// (ADR-033 D8), pinned as-of the 31 Dec deadline. 0 when no agreement exists (the law's default
    /// is §24 auto-payout). The D6 carryover-write reads THIS as the §21 provenance component.
    /// </summary>
    public decimal TransferAgreementDays { get; init; }

    // --- Impediment / feriehindring status (ADR-033 D10) ---

    /// <summary>
    /// Whether the employee is (or may be) feriehindret across the period (§22 sickness/barsel).
    /// When true, a forfeiture-candidate remainder must NOT be auto-§34-forfeited — the close
    /// fails closed to PENDING_REVIEW (ADR-033 D10). Defaults to false; the impediment signal is
    /// modeled in slice 4, so pre-slice-4 this is the conservative not-impeded default carried
    /// for forward contract stability.
    /// </summary>
    public bool IsFeriehindret { get; init; }
}

/// <summary>
/// One absence's recorded <c>feriedage</c> within a <see cref="VacationSettlementSnapshot"/>
/// (ADR-032 D2). Positional init-only record (PAT-001).
/// </summary>
public sealed record RecordedAbsenceFeriedage(Guid AbsenceEventId, decimal Feriedage);
