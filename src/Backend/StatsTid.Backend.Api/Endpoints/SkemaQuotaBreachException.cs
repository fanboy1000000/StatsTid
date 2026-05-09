namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// Internal signal raised inside the atomic Skema POST transaction
/// (<c>POST /api/skema/{employeeId}/save</c>) when the in-tx
/// <c>EntitlementBalanceRepository.CheckAndAdjustAsync(conn, tx, ...)</c>
/// surfaces a quota breach (concurrent modification raced past the
/// pre-validation HTTP check). Caught at the endpoint boundary, where
/// it triggers <c>tx.RollbackAsync</c> on the outer save transaction
/// (atomic bundle-rollback per ADR-018 D3 — ALL prior
/// <c>TimeEntryRegistered</c> + <c>AbsenceRegistered</c> +
/// <c>EntitlementBalanceAdjusted</c> events from the same handler
/// call are rolled back together) and a 422 response with the same
/// body shape as the pre-validation 422 path so the frontend retry
/// contract (<c>useSkema.ts</c>) is unchanged.
///
/// <para>
/// S27 TASK-2706 restoring S26 TASK-2604 shape: the original was a
/// nested-private class on <c>SkemaEndpoints</c> + (was reverted at
/// <c>62cfb20</c> along with the atomic-tx wrap). Re-introduced now
/// with the projection-table layer (<c>time_entries_projection</c> +
/// <c>absences_projection</c>, S27 TASK-2702/TASK-2704) in place so
/// the synchronous-in-tx projection writes preserve read-your-write
/// across the rolled-back-then-retried flow. Promoted to a top-level
/// internal type per S27 TASK-2706 file-scope (separate file).
/// </para>
///
/// <para>
/// Constructor argument order is <c>(EntitlementType, RequestedDays,
/// CurrentUsed, EffectiveQuota)</c>. The endpoint catch block computes
/// <c>remaining = EffectiveQuota - CurrentUsed</c> when shaping the
/// 422 body so the frontend sees the same numeric semantics as the
/// pre-validation path.
/// </para>
/// </summary>
internal sealed class SkemaQuotaBreachException : Exception
{
    /// <summary>The entitlement type (e.g. <c>VACATION</c>, <c>CARE_DAY</c>).</summary>
    public string EntitlementType { get; }

    /// <summary>Days requested by this save call for <see cref="EntitlementType"/>.</summary>
    public decimal RequestedDays { get; }

    /// <summary>Current used balance (post-INSERT-zero-state, pre-failed-UPDATE) under the outer tx snapshot.</summary>
    public decimal CurrentUsed { get; }

    /// <summary>Effective quota (annual or pro-rated) for <see cref="EntitlementType"/>.</summary>
    public decimal EffectiveQuota { get; }

    public SkemaQuotaBreachException(
        string entitlementType,
        decimal requestedDays,
        decimal currentUsed,
        decimal effectiveQuota)
        : base($"Skema quota breach: {entitlementType} request {requestedDays}d would exceed quota (currently used {currentUsed}d, effective quota {effectiveQuota}d)")
    {
        EntitlementType = entitlementType;
        RequestedDays = requestedDays;
        CurrentUsed = currentUsed;
        EffectiveQuota = effectiveQuota;
    }
}
