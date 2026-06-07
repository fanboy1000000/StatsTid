namespace StatsTid.SharedKernel.Events;

/// <summary>
/// S66 / ADR-032 D4. Emitted from the profile PUT transaction when a fullDayHours-affecting
/// field (part_time_fraction or position) changes and future-dated absences are revalued.
/// One event per (entitlementType, entitlementYear) group of affected absences.
/// Stream: consolidated <c>employee-{employeeId}</c> (ADR-018 D6).
/// Replay applies the replacement set + delta verbatim — deterministic, no recompute.
/// </summary>
public sealed class EntitlementBalanceRevalued : DomainEventBase
{
    public override string EventType => "EntitlementBalanceRevalued";

    public required string EmployeeId { get; init; }
    public required string EntitlementType { get; init; }
    public required int EntitlementYear { get; init; }

    /// <summary>The per-absence replacement set: each booking's recomputed feriedage.</summary>
    public required IReadOnlyList<AbsenceFeriedageReplacement> Replacements { get; init; }

    /// <summary>The aggregate used adjustment for this group (may be negative).</summary>
    public required decimal UsedDelta { get; init; }

    /// <summary>The profile-change event that caused this revaluation.</summary>
    public required Guid TriggeringProfileEventId { get; init; }
}

/// <summary>
/// One absence's revalued feriedage within an <see cref="EntitlementBalanceRevalued"/> event.
/// </summary>
public sealed record AbsenceFeriedageReplacement(Guid AbsenceEventId, decimal NewFeriedage);
