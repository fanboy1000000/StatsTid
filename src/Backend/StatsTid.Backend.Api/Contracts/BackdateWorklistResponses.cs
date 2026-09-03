namespace StatsTid.Backend.Api.Contracts;

// S138 / TASK-13803 (ADR-040 D8, Increment 3 — PAT-012 typed contracts) — the HR backdate
// diagnostic worklist wire shapes. HR-ONLY surface (HROrAbove + the LocalHR per-scope floor):
// nothing here reaches an Employee-facing DTO, so the correction's effectiveFrom may be carried.
// Serialized camelCase via the .NET 8 minimal-API JsonSerializerDefaults.Web default.

/// <summary>
/// One correction that touched a worklist row, with the baseline it captured at its own append
/// time and the per-trigger derived flags. <paramref name="RecalculatedSince"/> is non-null only
/// on EXPORTED_MONTH rows (the export's CURRENT content hash differs from
/// <paramref name="BaselineContentHash"/>); <paramref name="ReversedSince"/> only on SETTLED_YEAR
/// rows (a later settlement sequence exists, or the baseline row is now REVERSED);
/// <paramref name="RecalcBlockedBy"/> is the register id (<c>QUAL-149</c> / <c>QUAL-150</c>) when
/// THIS trigger blocks the payroll re-plan today, else null.
/// </summary>
public sealed record BackdateWorklistTriggerDto(
    string Kind,
    Guid EventId,
    DateOnly EffectiveFrom,
    DateTimeOffset AppendedAt,
    string ActorId,
    string? BaselineContentHash,
    int? BaselineSettlementSequence,
    string? BaselineSettlementState,
    bool? RecalculatedSince,
    bool? ReversedSince,
    string? RecalcBlockedBy);

/// <summary>
/// The GET /api/hr/backdate-worklist list element. <paramref name="Kind"/> is
/// <c>EXPORTED_MONTH</c> (keys <paramref name="Year"/>/<paramref name="Month"/>/
/// <paramref name="ExportId"/>) or <c>SETTLED_YEAR</c> (keys <paramref name="EntitlementType"/>/
/// <paramref name="EntitlementYear"/>). <paramref name="RecalcBlockedBy"/> is the SET of register
/// ids over all triggers (empty when the re-plan is not blocked, or for SETTLED_YEAR rows).
/// <paramref name="RecalculatedSince"/> / <paramref name="ReversedSince"/> are the row-level flags
/// (true only when EVERY trigger's baseline has been superseded); the other kind's flag is null.
/// <paramref name="Version"/> also rides the ETag the resolve endpoint expects as If-Match.
/// </summary>
public sealed record BackdateWorklistRow(
    Guid WorklistId,
    string EmployeeId,
    string Kind,
    int? Year,
    int? Month,
    Guid? ExportId,
    string? EntitlementType,
    int? EntitlementYear,
    IReadOnlyList<BackdateWorklistTriggerDto> Triggers,
    IReadOnlyList<string> RecalcBlockedBy,
    bool? RecalculatedSince,
    bool? ReversedSince,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset? ResolvedAt,
    string? ResolvedBy,
    string? Resolution,
    string? ResolutionReason,
    long Version);

/// <summary>
/// POST /api/hr/backdate-worklist/{worklistId}/resolve body. <paramref name="Resolution"/> is
/// <c>RECALCULATED</c> or <c>DISMISSED</c> — HR's assertion, recorded alongside the derived flags,
/// never replacing them. <paramref name="Reason"/> is required (a dismissal without a reason is not
/// an audit trail).
/// </summary>
public sealed record ResolveBackdateWorklistRequest(string Resolution, string Reason);

/// <summary>The resolve 200 body — the post-write state; <paramref name="Version"/> also rides the ETag header.</summary>
public sealed record BackdateWorklistResolveResponse(
    Guid WorklistId,
    string EmployeeId,
    string Resolution,
    DateTimeOffset ResolvedAt,
    long Version);
