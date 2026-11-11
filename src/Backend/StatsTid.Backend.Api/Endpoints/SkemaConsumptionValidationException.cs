namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// Internal signal raised INSIDE the locked Skema POST transaction
/// (<c>POST /api/skema/{employeeId}/save</c>) when one of its in-lock, authoritative validation
/// re-checks fails, before any write (ADR-032 D2/D3; ADR-043 D2).
///
/// <para>
/// Each re-check mirrors a pre-tx guard, which stays the cheap common path; this is the race loser's
/// clean validation error, never a 500. The failure modes:
/// <list type="bullet">
///   <item><description><b>profile-missing (B1)</b>: an entitlement-consuming row whose date has no
///     covering dated profile under the in-lock read ⇒ <c>fullDayHours</c> null ⇒ unvalued
///     consumption. Surfaces as the SAME <c>employment_profile_missing</c> 422 the pre-tx anchor
///     guard returns (D3 no-profile rule — fail-closed, never persist a null-valued consuming
///     row). Arises when a profile change raced the save.</description></item>
///   <item><description><b>norm-cap (B2)</b>: a date's all-types absence-hour total exceeds the
///     in-lock authoritative <c>fullDayHours</c> (a racing PUT lowered the norm) ⇒ a per-row
///     <c>dayEquivalent &gt; 1.0</c> would persist. Surfaces as the SAME
///     <c>"Total absence hours exceed norm day"</c> 422 the pre-tx D3 cap returns.</description></item>
///   <item><description><b>full-day-only (B3)</b>: a care or senior day whose hours no longer equal
///     the in-lock basis (a racing PUT changed it). Surfaces as the SAME <c>absence_full_day_only</c>
///     422 the pre-tx full-day guard returns.</description></item>
///   <item><description><b>paid-absence day bound</b>: a touched date whose credit-class paid
///     absence, as the day will stand after the save, exceeds max(0, planned − worked) on the state
///     committed while the lock is held. Arises mostly when another save of the same employee
///     committed while this one waited for the lock (two saves that each saw the day without the
///     other), or a racing PUT lowered the basis. Surfaces as the SAME 422 the pre-tx bound check
///     returns (<c>kind = "paid-absence-day-bound"</c>).</description></item>
///   <item><description><b>child's-sick illness chain</b>: a child's 2nd or 3rd sick day whose
///     earlier days of the same illness are missing on the absence rows as committed while the lock
///     is held (a day with no planned hours needs none), or which the OK version of the illness's
///     first day does not give (QUAL-236). Surfaces as the SAME 422 the pre-tx chain check returns
///     (<c>kind = "child-sick-chain"</c>).</description></item>
/// </list>
/// </para>
///
/// <para>
/// Mirrors <see cref="SkemaQuotaBreachException"/>: caught at the endpoint boundary, where it triggers
/// <c>tx.RollbackAsync</c> on the outer save transaction (atomic bundle-rollback per ADR-018 D3) and
/// returns a 422 whose body is byte-identical to the matching pre-tx guard so the frontend
/// (<c>useSkema.ts</c>) retry contract is unchanged. It carries the already-shaped 422 body
/// (<see cref="Body"/>) so the catch block does not re-derive the shape — the throw site, which owns
/// the failing date/type context, builds it exactly as its pre-tx sibling does.
/// </para>
/// </summary>
internal sealed class SkemaConsumptionValidationException : Exception
{
    /// <summary>The pre-shaped 422 response body (same shape as the matching pre-tx guard).</summary>
    public object Body { get; }

    public SkemaConsumptionValidationException(object body)
        : base("Skema consumption validation failed under the in-lock authoritative recompute (ADR-032 D2/D3).")
    {
        Body = body;
    }
}
