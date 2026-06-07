using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Services;

/// <summary>
/// S66 / TASK-6603 — the vacation-consumption ("feriedage") calculator (ADR-032 D1/D3).
/// Converts an absence's <c>Hours</c> into entitlement day-equivalents (<c>feriedage</c>)
/// against the employee's REAL per-day norm (<c>fullDayHours</c>), replacing the legacy flat
/// <c>Hours ÷ 7.4</c> divisor. A half-time employee's full work day (3.7h) now consumes a full
/// feriedag (1.0), not 0.5.
///
/// <para>
/// <b>Composition (the S65 drift-proof seam, PAT/ADR-032 D1).</b> The per-weekday norm is NOT
/// recomputed here — it is delegated to <see cref="DailyNormCalculator"/> (one shared norm impl,
/// consumed by the Skema month read, the Balance year-overview read, and now consumption). This
/// calculator only adds the ADR-032 D3 semantics on top of that norm:
/// <list type="bullet">
///   <item><description>weekend (norm == 0) ⇒ <c>fullDayHours = 0</c>;</description></item>
///   <item><description><c>ANNUAL_ACTIVITY</c> (academic, norm == null but a dated profile DOES
///     cover the day) ⇒ fallback <c>7.4 × partTimeFraction</c> (the AbsenceRule convention —
///     vacation stays bookable for academics; ADR-032 D3);</description></item>
///   <item><description>no dated profile covering the day (norm == null AND profile == null) ⇒
///     <c>null</c> propagates — the caller's existing anchor-422 family handles it
///     (ADR-032 D3; do NOT relax).</description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>How the two null cases are told apart without duplicating norm logic.</b>
/// <see cref="DailyNormCalculator"/> deliberately collapses BOTH "ANNUAL_ACTIVITY" and
/// "no dated profile" to a <c>null</c> norm. ADR-032 D3 needs them split (fallback vs propagate).
/// So this calculator independently resolves the dated profile for the day (the SAME
/// <see cref="IEmploymentProfileResolver"/> the Skema/norm path uses) and uses the profile's
/// presence as the discriminator: a non-null profile with a null norm is the academic case; a
/// null profile is the no-profile case. The norm VALUE itself still comes from
/// <see cref="DailyNormCalculator"/> — no second weekday/config/OK resolution lives here.
/// </para>
///
/// <para>
/// <b>feriedage rounding (ADR-032 D1).</b>
/// <c>feriedage = Math.Round(hours / fullDayHours, 4, MidpointRounding.AwayFromZero)</c> —
/// 4 decimals, AwayFromZero, matching the Postgres <c>ROUND(.., 4)</c> backfill convention so the
/// live-write value and the backfilled value agree byte-for-byte.
/// </para>
///
/// <para>
/// <b>Per-request cache.</b> Both the resolved norm and the resolved profile are cached per date
/// within a single <see cref="ComputeAsync"/> call (an absence batch typically references few
/// distinct dates, and a date often carries several rows). No shared mutable state ⇒ singleton-safe.
/// </para>
/// </summary>
public sealed class ConsumptionCalculator
{
    private readonly DailyNormCalculator _dailyNormCalculator;
    private readonly IEmploymentProfileResolver _profileResolver;

    /// <summary>
    /// The ANNUAL_ACTIVITY (academic) fallback base — the legacy standard work day (7.4h). Per
    /// ADR-032 this is NO LONGER the consumption divisor for the WEEKLY_HOURS case (that is the
    /// real per-day norm); it survives ONLY as the academic-fallback base, scaled by the dated
    /// part-time fraction. Single source: <see cref="EntitlementMapping.StandardDayHours"/>.
    /// </summary>
    private const decimal StandardDayHours = EntitlementMapping.StandardDayHours;

    public ConsumptionCalculator(
        DailyNormCalculator dailyNormCalculator,
        IEmploymentProfileResolver profileResolver)
    {
        _dailyNormCalculator = dailyNormCalculator;
        _profileResolver = profileResolver;
    }

    /// <summary>
    /// One absence row's consumption result. <see cref="FullDayHours"/> is the resolved per-day
    /// norm under ADR-032 D3 (0 on weekends, 7.4×fraction for ANNUAL_ACTIVITY, null when no dated
    /// profile covers the day). <see cref="Feriedage"/> is the consumed day-equivalent, or null
    /// when <see cref="FullDayHours"/> is null (no-profile) — the caller's anchor-422 handles it.
    /// </summary>
    public readonly record struct Consumption(DateOnly Date, decimal? FullDayHours, decimal? Feriedage);

    /// <summary>
    /// Resolves <c>fullDayHours</c> for a single day per ADR-032 D3 (weekend 0 / ANNUAL_ACTIVITY
    /// 7.4×fraction / no-profile null), reusing <see cref="DailyNormCalculator"/> for the weekday
    /// norm. <paramref name="fallbackOrgId"/> mirrors the norm calculator's fallback org.
    /// </summary>
    public async Task<decimal?> FullDayHoursAsync(
        string employeeId,
        DateOnly date,
        string fallbackOrgId,
        CancellationToken ct = default)
    {
        // Per-weekday norm from the SHARED resolver (no duplicate norm logic here).
        var norms = await _dailyNormCalculator.ComputeRangeAsync(employeeId, date, date, fallbackOrgId, ct);
        var norm = norms[0].Hours;

        // norm has a concrete value (weekend 0 or a real weekday norm) ⇒ that IS fullDayHours.
        if (norm is not null)
            return norm;

        // norm == null: either ANNUAL_ACTIVITY (academic) OR no dated profile. Discriminate by
        // the dated profile's presence (ADR-032 D3) WITHOUT re-resolving the norm/config.
        var profile = await _profileResolver.GetByEmployeeIdAtAsync(employeeId, date, ct);
        if (profile is null)
            return null; // no profile covering the day — propagate (anchor-422 family).

        // ANNUAL_ACTIVITY academic fallback: 7.4 × dated part-time fraction (AbsenceRule convention).
        return StandardDayHours * profile.PartTimeFraction;
    }

    /// <summary>
    /// Computes the per-row consumed feriedage for a batch of (date, hours) absence rows.
    /// Resolves <c>fullDayHours</c> per distinct date (cached) and applies the ADR-032 D1
    /// rounding. A row on a no-profile day yields a null <see cref="Consumption.Feriedage"/>.
    ///
    /// <para>
    /// The returned list is index-aligned with <paramref name="rows"/> (one result per input row,
    /// same order) so the caller can pair each result with the absence it computed it from.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<Consumption>> ComputeAsync(
        string employeeId,
        IReadOnlyList<(DateOnly Date, decimal Hours)> rows,
        string fallbackOrgId,
        CancellationToken ct = default)
    {
        var fullDayCache = new Dictionary<DateOnly, decimal?>();
        var result = new List<Consumption>(rows.Count);

        foreach (var (date, hours) in rows)
        {
            if (!fullDayCache.TryGetValue(date, out var fullDay))
            {
                fullDay = await FullDayHoursAsync(employeeId, date, fallbackOrgId, ct);
                fullDayCache[date] = fullDay;
            }

            result.Add(new Consumption(date, fullDay, ToFeriedage(hours, fullDay)));
        }

        return result;
    }

    /// <summary>
    /// ADR-032 D1 consumption conversion: <c>Math.Round(hours / fullDayHours, 4,
    /// MidpointRounding.AwayFromZero)</c>. Null/zero <paramref name="fullDayHours"/> ⇒ null
    /// (no meaningful divisor: no-profile null propagates; a 0-norm weekend never carries an
    /// entitlement-consuming row past the D3 guard, so it is never divided here).
    /// </summary>
    public static decimal? ToFeriedage(decimal hours, decimal? fullDayHours)
    {
        if (fullDayHours is not { } fdh || fdh <= 0m)
            return null;
        return Math.Round(hours / fdh, 4, MidpointRounding.AwayFromZero);
    }
}
