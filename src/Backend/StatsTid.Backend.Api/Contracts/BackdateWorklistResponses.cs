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
/// <paramref name="Resolution"/> is <c>RECALCULATED</c>, <c>DISMISSED</c> or
/// <c>HANDLED_MANUALLY</c> once resolved (null while open). <paramref name="ResolutionBlockedBy"/>
/// (S144) is the block set RECORDED at resolution — the register ids that were in force on the row
/// when HR resolved it (<c>[]</c> when nothing blocked) — and is null while the row is open; unlike
/// <paramref name="RecalcBlockedBy"/> it is a stamped fact, not re-derived.
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
    IReadOnlyList<string>? ResolutionBlockedBy,
    long Version);

/// <summary>
/// POST /api/hr/backdate-worklist/{worklistId}/resolve body. <paramref name="Resolution"/> is
/// <c>RECALCULATED</c>, <c>DISMISSED</c> or <c>HANDLED_MANUALLY</c> — HR's assertion, recorded
/// alongside the derived flags, never replacing them. RECALCULATED is refused (409
/// <c>worklist-recalc-blocked</c>) on a row whose re-plan is blocked; HANDLED_MANUALLY ("fixed
/// outside the system") is the verb for such a row. On an EXPORTED_MONTH row both RECALCULATED and
/// HANDLED_MANUALLY are Global-Admin-only. <paramref name="Reason"/> is required (a resolution
/// without a reason is not an audit trail).
/// </summary>
public sealed record ResolveBackdateWorklistRequest(string Resolution, string Reason);

/// <summary>
/// The resolve 200 body — the post-write state; <paramref name="Version"/> also rides the ETag
/// header. <paramref name="ResolutionBlockedBy"/> (S144) is the block set this resolution stamped on
/// the row (<c>[]</c> when nothing blocked; never null on this body).
/// </summary>
public sealed record BackdateWorklistResolveResponse(
    Guid WorklistId,
    string EmployeeId,
    string Resolution,
    DateTimeOffset ResolvedAt,
    IReadOnlyList<string> ResolutionBlockedBy,
    long Version);
