namespace StatsTid.SharedKernel.Models;

/// <summary>
/// Local agreement profile (S21 — replaces the per-key local_configurations patch bag).
/// One profile per (org_id, agreement_code, ok_version) is "currently active" at any time
/// (effective_to IS NULL); closed predecessors are retained for history. Lifecycle is
/// effective_to-only — there is no separate is_active boolean (ADR-017 D1, cycle-1 review).
///
/// Overridable columns (NULL = inherit central):
///   weekly_norm_hours, max_flex_balance, flex_carryover_max,
///   max_overtime_hours_per_period, overtime_requires_pre_approval.
/// </summary>
public sealed class LocalAgreementProfile
{
    public required Guid ProfileId { get; init; }
    public required string OrgId { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }

    // Overridable fields (5). NULL = inherit central.
    public decimal? WeeklyNormHours { get; init; }
    public decimal? MaxFlexBalance { get; init; }
    public decimal? FlexCarryoverMax { get; init; }
    public decimal? MaxOvertimeHoursPerPeriod { get; init; }
    public bool? OvertimeRequiresPreApproval { get; init; }

    public required string CreatedBy { get; init; }
    public required DateTime CreatedAt { get; init; }
}

/// <summary>
/// Static metadata co-located with <see cref="LocalAgreementProfile"/>. Holds the legacy
/// config_key → column mapping used by the S21 migration runner (TASK-2106) and the
/// informational-key whitelist used by the migration's drop-with-audit path
/// (ADR-017 D4 + Q6 sub-question 6a).
/// </summary>
public static class LocalAgreementProfileMetadata
{
    /// <summary>
    /// Maps pre-S21 config_key strings (as stored in the legacy local_configurations table)
    /// to LocalAgreementProfile column names. Single source of truth consumed by
    /// LocalAgreementProfileMigrator (TASK-2106) and the migration's known-overridable-key
    /// classification logic (per ADR-017 D4 cycle-1 resolution).
    ///
    /// New overridable columns added to LocalAgreementProfile must add an entry here.
    /// Schema and this map are paired — drift between them is the silent-no-op-overrides
    /// failure mode S21 is fixing (per the SPRINT-21 problem statement).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LegacyKeyToColumn =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MaxFlexBalance"]              = "max_flex_balance",
            ["FlexCarryoverMax"]            = "flex_carryover_max",
            ["WeeklyNormHours"]             = "weekly_norm_hours",
            ["MaxOvertimeHoursPerPeriod"]   = "max_overtime_hours_per_period",
            ["OvertimeRequiresPreApproval"] = "overtime_requires_pre_approval",
        };

    /// <summary>
    /// Pre-S21 config_keys that are stored in the legacy table but never read by the rule
    /// engine (informational only). The S21 migration drops these rows with audit-log
    /// emission per ADR-017 D4 + Q6 sub-question 6a.
    /// </summary>
    public static readonly IReadOnlySet<string> LegacyInformationalKeys =
        new HashSet<string>(StringComparer.Ordinal) { "PlanningStartDay", "ApprovalCutoffDay" };
}

/// <summary>
/// Result of a save-time field alignment check (ADR-017 D9a). When alignment fails,
/// callers surface <see cref="ErrorCode"/> and <see cref="NearestValidDates"/> in the
/// structured save-validation error payload (ISO-8601 dates).
/// </summary>
public sealed record FieldAlignmentResult(
    bool IsAligned,
    string? ErrorCode,
    IReadOnlyList<DateOnly>? NearestValidDates);

/// <summary>
/// Per-field save-time alignment validators for <see cref="LocalAgreementProfile"/>
/// effective_from values (ADR-017 D9a + cycle-1 review).
///
/// This is a static const map — NOT a runtime-resolved IRuleClassificationProvider
/// lookup. The map's extensibility is bounded to DateOnly-aligned constraints;
/// timestamp-level alignment (e.g., midnight Copenhagen time) would require widening
/// the signature and is forward-looking out-of-scope.
///
/// Fields with no entry in <see cref="ByFieldName"/> are treated as "no policy" by the
/// validator (always aligned). Add an entry here when a new overridable field requires
/// alignment.
/// </summary>
public static class LocalAgreementProfileAlignmentPolicies
{
    public static readonly IReadOnlyDictionary<string, Func<DateOnly, FieldAlignmentResult>> ByFieldName =
        new Dictionary<string, Func<DateOnly, FieldAlignmentResult>>(StringComparer.Ordinal)
        {
            // WeeklyNormHours must align to Monday — NormCheckRule.WEEKLY's window edge.
            // ADR-016 D2 inventory: (window, aligned-window, calculation).
            ["WeeklyNormHours"] = effectiveFrom =>
            {
                if (effectiveFrom.DayOfWeek == DayOfWeek.Monday)
                    return new FieldAlignmentResult(true, null, null);

                // Compute the previous and next Monday for the structured error payload
                // (per ADR-017 D9a error message convention: ISO-8601 dates).
                int daysFromMonday = ((int)effectiveFrom.DayOfWeek + 6) % 7;
                var previousMonday = effectiveFrom.AddDays(-daysFromMonday);
                var nextMonday = previousMonday.AddDays(7);
                return new FieldAlignmentResult(
                    IsAligned: false,
                    ErrorCode: "NOT_MONDAY_ALIGNED",
                    NearestValidDates: new[] { previousMonday, nextMonday });
            },

            // The remaining 4 overridable fields (MaxFlexBalance, FlexCarryoverMax,
            // MaxOvertimeHoursPerPeriod, OvertimeRequiresPreApproval) have no alignment
            // requirement today and are intentionally absent from this map. The validator
            // treats absence as "no policy" → automatically aligned. New fields with
            // alignment add an entry here.
        };
}
