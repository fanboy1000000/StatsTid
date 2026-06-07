namespace StatsTid.Backend.Api.Endpoints;

/// <summary>
/// S66 / TASK-6603 FIX-FORWARD — internal signal raised INSIDE the locked Skema POST transaction
/// (<c>POST /api/skema/{employeeId}/save</c>) when an in-lock consumption re-check fails against the
/// AUTHORITATIVE per-row <c>fullDayHours</c> re-derived under the advisory lock (ADR-032 D2/D3).
///
/// <para>
/// Two failure modes, both arising only when a profile change RACED between the pre-tx fast guard and
/// the in-lock authoritative recompute (the pre-tx guards stay the cheap common path; this is the
/// race loser's clean validation error, never a 500):
/// <list type="bullet">
///   <item><description><b>profile-missing (B1)</b>: an entitlement-consuming row whose date has no
///     covering dated profile under the in-lock read ⇒ <c>fullDayHours</c> null ⇒ unvalued
///     consumption. Surfaces as the SAME <c>employment_profile_missing</c> 422 the pre-tx anchor
///     guard returns (D3 no-profile rule — fail-closed, never persist a null-valued consuming
///     row).</description></item>
///   <item><description><b>norm-cap (B2)</b>: a date's all-types absence-hour total exceeds the
///     in-lock authoritative <c>fullDayHours</c> (a racing PUT lowered the norm) ⇒ a per-row
///     <c>dayEquivalent &gt; 1.0</c> would persist. Surfaces as the SAME
///     <c>"Total absence hours exceed norm day"</c> 422 the pre-tx D3 cap returns.</description></item>
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
