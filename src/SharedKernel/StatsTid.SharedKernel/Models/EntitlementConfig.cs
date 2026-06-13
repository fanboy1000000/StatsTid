namespace StatsTid.SharedKernel.Models;

public enum EntitlementType
{
    VACATION,
    SPECIAL_HOLIDAY,
    CARE_DAY,
    CHILD_SICK,
    SENIOR_DAY
}

public enum AccrualModel
{
    IMMEDIATE,
    MONTHLY_ACCRUAL
}

public sealed class EntitlementConfig
{
    public required Guid ConfigId { get; init; }
    public required string EntitlementType { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required decimal AnnualQuota { get; init; }
    public required string AccrualModel { get; init; }
    public required int ResetMonth { get; init; }
    public required decimal CarryoverMax { get; init; }
    public required bool ProRateByPartTime { get; init; }
    public required bool IsPerEpisode { get; init; }
    public int? MinAge { get; init; }
    public string? Description { get; init; }

    /// <summary>
    /// S73 / TASK-7301 (SPRINT-73 R2, owner ruling D-A 2026-06-13): when TRUE, a registration of
    /// this entitlement type must equal the day's ADR-032 consumption basis EXACTLY (the Skema
    /// save guard enforces it; partial hours are rejected 422 <c>absence_full_day_only</c>).
    /// CARE_DAY + SENIOR_DAY carry TRUE by construction (DB CHECK
    /// <c>entitlement_configs_full_day_only_types</c> + the admin 422 guard); CHILD_SICK stays
    /// hours-based. Defaulted FALSE for compile-time backward compatibility — repo read/insert
    /// paths bind the column explicitly.
    /// </summary>
    public bool FullDayOnly { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Row-version optimistic-concurrency token (ADR-019 D2.2 propagation; column added by the
    /// S25 / s25-d2-2-version migration block). Mirrors <see cref="WageTypeMapping.Version"/>:
    /// BIGINT (long) row-version, first-insert is <c>1</c>; each in-place UPDATE bumps it by one.
    /// Combined with <c>If-Match: "&lt;version&gt;"</c> on PUT (RFC 7232 quoted), this provides
    /// optimistic-concurrency control on admin-config edits. Defaulted to <c>1</c> for
    /// compile-time backward compatibility — the v3 repo reads the column from the DB and sets
    /// this property explicitly; legacy seed paths that bypass the repo work via the DB DEFAULT.
    /// </summary>
    public long Version { get; init; } = 1;

    /// <summary>
    /// Inclusive lower bound of this row's effective range (S30 / TASK-3002 +
    /// s30-d2-ec-effective-dating migration). DB column is <c>DATE NOT NULL</c>; pre-launch
    /// backfill set existing rows to the sentinel <c>0001-01-01</c>. Defaulted here to
    /// <c>default(DateOnly)</c> for compile-time backward compatibility — repo INSERT paths
    /// bind the caller-supplied value explicitly. See ADR-021 D2.
    /// </summary>
    public DateOnly EffectiveFrom { get; init; }

    /// <summary>
    /// Exclusive upper bound of this row's effective range (S30 / TASK-3002). <c>null</c> means
    /// "currently open / unbounded above" — at most one open row per natural key per the
    /// <c>idx_ec_natural_key_open</c> partial-unique-index. A non-null value means the row has
    /// been superseded or soft-deleted (<c>effective_to = closure_date</c>). See ADR-021 D2.
    /// </summary>
    public DateOnly? EffectiveTo { get; init; }
}
