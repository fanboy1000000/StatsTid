namespace StatsTid.SharedKernel.Models;

/// <summary>
/// S40 / TASK-4003 — role-within-agreement override row (ADR-024 D1 + D2). Sits between
/// <see cref="AgreementConfigEntity"/> and <c>position_override_configs</c> in the
/// ConfigResolutionService chain (S41 cutover wires it up). Allows admins to disable
/// specific compensation features (6 boolean disablers) or override quantitative agreement
/// settings (4 nullable quantitative columns) for a given
/// <c>(employment_category, agreement_code, ok_version)</c> triple — without forking the
/// underlying agreement config.
///
/// <para>
/// <b>Versioned-history shape (ADR-018 D7 + ADR-020 D2 + ADR-023 D8 — 5th repository).</b>
/// Each row carries <see cref="EffectiveFrom"/> + <see cref="EffectiveTo"/> under end-exclusive
/// <c>[from, to)</c> semantics (ADR-018 D9) and a monotonic <see cref="Version"/> column for
/// ADR-019 admin-strict If-Match optimistic concurrency. The partial-unique-index
/// <c>idx_role_config_overrides_live</c> enforces "at most one live (open) row per
/// (employment_category, agreement_code, ok_version)".
/// </para>
///
/// <para>
/// <b>Tri-state semantics for <see cref="MerarbejdeCompensationRight"/></b> (ADR-024 D2):
/// <c>NULL</c> = inherit from agreement_configs; <c>"CONTRACTUAL"</c> = mandatory paid
/// merarbejde (e.g. AC academic staff); <c>"DISCRETIONARY"</c> = manager-discretionary;
/// <c>"NONE"</c> = no merarbejde compensation for this role.
/// </para>
///
/// <para>
/// <b>NULL = inherit</b> for all 11 override fields (1 tri-state + 6 booleans + 4
/// quantitative). The ConfigResolutionService chain (S41) walks
/// agreement_configs → role_config_overrides → position_override_configs, picking the first
/// non-null value at each layer. This model is a faithful row projection — no merging
/// happens in the SharedKernel layer.
/// </para>
/// </summary>
public sealed record class RoleConfigOverride
{
    // -- Identity + history window ----------------------------------------
    public required Guid OverrideId { get; init; }
    public required string EmploymentCategory { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required DateOnly EffectiveFrom { get; init; }
    public DateOnly? EffectiveTo { get; init; }
    public required long Version { get; init; }

    // -- Tri-state (ADR-024 D2) -------------------------------------------
    /// <summary>
    /// One of <c>"CONTRACTUAL"</c>, <c>"DISCRETIONARY"</c>, <c>"NONE"</c>, or
    /// <c>null</c> (inherit). Schema CHECK constraint enforces the closed set.
    /// </summary>
    public string? MerarbejdeCompensationRight { get; init; }

    // -- 6 boolean disablers (null = inherit) -----------------------------
    public bool? HasMerarbejde { get; init; }
    public bool? HasOvertime { get; init; }
    public bool? HasEveningSupplement { get; init; }
    public bool? HasNightSupplement { get; init; }
    public bool? HasWeekendSupplement { get; init; }
    public bool? HasHolidaySupplement { get; init; }

    // -- 4 quantitative overrides (null = inherit) ------------------------
    public decimal? MaxFlexBalance { get; init; }
    public decimal? FlexCarryoverMax { get; init; }
    public int? NormPeriodWeeks { get; init; }
    public decimal? WeeklyNormHours { get; init; }

    // -- Audit metadata ---------------------------------------------------
    public required DateTime CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public required string CreatedByRole { get; init; }
}

/// <summary>
/// S40 / TASK-4003 — mutable-fields projection consumed by
/// <c>RoleConfigOverrideRepository.SupersedeAndCreateAsync</c>. Carries ONLY the 11
/// override fields (1 tri-state + 6 booleans + 4 quantitative) that the caller chooses
/// per write; identity (override_id), natural key, history window, version, and audit
/// metadata are not part of this shape — those are repository-owned or supplied as
/// separate method parameters.
///
/// <para>
/// Mirrors the S22 LocalAgreementProfile pattern of carving the "patch" surface off
/// the full entity so admin endpoints can deserialize a narrower payload and so the
/// repository signature does not invite callers to mutate immutable fields.
/// </para>
/// </summary>
public sealed record RoleConfigOverrideFields(
    string? MerarbejdeCompensationRight,
    bool? HasMerarbejde,
    bool? HasOvertime,
    bool? HasEveningSupplement,
    bool? HasNightSupplement,
    bool? HasWeekendSupplement,
    bool? HasHolidaySupplement,
    decimal? MaxFlexBalance,
    decimal? FlexCarryoverMax,
    int? NormPeriodWeeks,
    decimal? WeeklyNormHours);
