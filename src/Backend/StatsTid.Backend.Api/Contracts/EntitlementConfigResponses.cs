using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Contracts;

// S118 / TASK-11800 (Fork B retrofit Pass 5, PAT-010/PAT-012) — the ONE entitlement-config
// child record, per SPRINT-118 owner ruling #2 (the drift-repair class). Before S118 the
// child shape had THREE mapper copies: two byte-identical 16-member mappers
// (EntitlementConfigEndpoints.MapToResponse + AgreementEntitlementEndpoints.MapToResponse)
// and one DRIFTED 15-member inline mapper (AgreementConfigEndpoints.MapEntitlementToResponse,
// the by-id embedded rows) that OMITTED `fullDayOnly`. All three now emit this record via
// <see cref="EntitlementConfigResponse.FromModel"/> — the by-id embedded rows GAIN
// `fullDayOnly` (additive; read-side display-only this pass — the write-side PUT-body gap is
// a NAMED DEFERRED DEFECT, owner-visible, NOT S118 scope).
//
// REFUSED enum sets (P4-open BY DESIGN, per the S118 exclusions): entitlementType (init.sql
// deliberately has "No entitlement_type CHECK"), accrualModel, agreementCode, okVersion.

/// <summary>
/// The 16-member entitlement-config row — shared by ALL child-shape emission sites:
/// the /api/admin/entitlement-configs list/by-id/POST-201/PUT-200, the
/// /api/agreement-configs/{configId}/entitlements list/POST-201/PUT-200, and the
/// agreement-config by-id GET's embedded <c>entitlements[]</c> rows. <c>version</c> is the
/// row-version token for composing child <c>If-Match</c>. The 412/409 error-body
/// <c>currentState</c> envelopes stay anonymous/untyped (S118 exclusion) — they embed this
/// record's serialization but are never declared via .Produces.
/// </summary>
public sealed record EntitlementConfigResponse(
    Guid ConfigId,
    string EntitlementType,
    string AgreementCode,
    string OkVersion,
    decimal AnnualQuota,
    string AccrualModel,
    int ResetMonth,
    decimal CarryoverMax,
    bool ProRateByPartTime,
    bool IsPerEpisode,
    int? MinAge,
    string? Description,
    // S73 / TASK-7301 (R2): served so the admin editor can round-trip the flag.
    bool FullDayOnly,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    long Version)
{
    /// <summary>
    /// The single shape-defining mapper (ruling #2's collapse point). Every endpoint-side
    /// child mapper delegates here — the three pre-S118 copies can no longer drift.
    /// </summary>
    public static EntitlementConfigResponse FromModel(EntitlementConfig c) => new(
        ConfigId: c.ConfigId,
        EntitlementType: c.EntitlementType,
        AgreementCode: c.AgreementCode,
        OkVersion: c.OkVersion,
        AnnualQuota: c.AnnualQuota,
        AccrualModel: c.AccrualModel,
        ResetMonth: c.ResetMonth,
        CarryoverMax: c.CarryoverMax,
        ProRateByPartTime: c.ProRateByPartTime,
        IsPerEpisode: c.IsPerEpisode,
        MinAge: c.MinAge,
        Description: c.Description,
        FullDayOnly: c.FullDayOnly,
        EffectiveFrom: c.EffectiveFrom,
        EffectiveTo: c.EffectiveTo,
        Version: c.Version);
}
