namespace StatsTid.SharedKernel.Calendar;

/// <summary>
/// One dated part-time-fraction period for an employee — the unit of input to the cumulative
/// piecewise accrual (<see cref="AccrualMath.EarnedToDatePiecewise"/>, ADR-030 D8).
///
/// <para>Danish <em>samtidighedsferie</em> earns each accrual month at the employment fraction in
/// effect that month; an employee whose hours change mid-ferieår therefore has a small ordered
/// history of these periods (e.g. full-time Sep–Dec, then 0,5 from Jan). The live (current) period
/// is normally open-ended (<see cref="To"/> = <c>null</c>).</para>
///
/// <para><b>End-exclusive (half-open) semantics:</b> a period covers a calendar date <c>d</c> iff
/// <c>From &lt;= d AND (To is null OR To &gt; d)</c>. The <see cref="To"/> day is therefore the
/// first day NOT covered, so adjacent periods abut without overlap or gap when the successor's
/// <see cref="From"/> equals the predecessor's <see cref="To"/> (e.g. <c>(…, 2026-01-01, 1.0)</c>
/// immediately followed by <c>(2026-01-01, null, 0.5)</c> — 1 Jan belongs to the 0,5 period only).</para>
///
/// <para>Immutable value type (PAT-001): a <c>readonly record struct</c> carries no identity and no
/// mutable state, so it is safe to pass through the pure, deterministic rule engine (ADR-002).</para>
/// </summary>
/// <param name="From">Inclusive first day this fraction is in effect.</param>
/// <param name="To">Exclusive end day (first day NO LONGER in effect); <c>null</c> = open-ended (live).</param>
/// <param name="Fraction">Employment fraction in effect over the period; 1.0 = full-time. Stored at
/// <c>NUMERIC(4,3)</c> scale (≤ 3 decimals).</param>
public readonly record struct FractionPeriod(DateOnly From, DateOnly? To, decimal Fraction)
{
    /// <summary>
    /// True iff this period covers <paramref name="date"/> under the half-open
    /// <c>[From, To)</c> rule (see the type summary). A <c>null</c> <see cref="To"/> covers every
    /// date at or after <see cref="From"/>.
    /// </summary>
    public bool Covers(DateOnly date) =>
        From <= date && (To is null || To.Value > date);
}
