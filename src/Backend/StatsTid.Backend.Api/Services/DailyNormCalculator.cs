using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Backend.Api.Services;

/// <summary>
/// S65 / TASK-6502 — the per-day "Arbejdstid"-norm resolver extracted VERBATIM from the
/// TASK-5603 loop that previously lived inline in <c>SkemaEndpoints</c> GET <c>/month</c>
/// (the only behavior change is that the math now lives in one place, consumed by both the
/// Skema month read and the new Balance year-overview read).
///
/// <para>
/// <b>What it computes (PURE READ — no rule-engine HTTP call, no rule logic, P2).</b> For each
/// calendar day in the requested range it resolves the employee's REAL per-day norm via:
/// <list type="bullet">
///   <item><description>the dated employment profile for THAT day
///     (<see cref="IEmploymentProfileResolver.GetByEmployeeIdAtAsync"/>) — so a mid-month
///     <c>part_time_fraction</c> change is reflected day-by-day; and</description></item>
///   <item><description>the merged agreement config
///     (<see cref="ConfigResolutionService.ResolveAsync"/> → <c>WeeklyNormHours</c> /
///     <c>NormModel</c>), with the OK version resolved PER DAY via
///     <see cref="OkVersionResolver.ResolveVersion(DateOnly)"/> (NOT <c>year &gt;= 2026</c> — the
///     OK24→OK26 switch is 2026-04-01).</description></item>
/// </list>
/// Daily norm = <c>WeeklyNorm × fraction / 5</c>, rounded to 2 decimals. Weekends ⇒ 0.
/// <c>ANNUAL_ACTIVITY</c> (academic) ⇒ <c>null</c> (a weekday split would be wrong — do NOT
/// approximate). No dated profile covering the day ⇒ <c>null</c> (graceful, ADR-023 D3 —
/// blank rather than a fabricated norm; e.g. days before <c>employment_start_date</c>).
/// </para>
///
/// <para>
/// <b>Per-request config cache.</b> Config resolution is cached per
/// <c>okVersion|agreementCode|position|fraction|orgId</c> within a single
/// <see cref="ComputeRangeAsync"/> call so the merged config is not re-resolved for every
/// weekday — exactly the cache the inline loop used. The cache is local to each call (no shared
/// mutable state), so the calculator is registered as a singleton safely.
/// </para>
///
/// <para>
/// <b>Determinism (P2/P4).</b> No wall-clock: every day's OK version, profile, and config are
/// resolved from the day itself and the dated stores, so two identical requests produce
/// byte-identical results.
/// </para>
/// </summary>
public sealed class DailyNormCalculator
{
    private readonly IEmploymentProfileResolver _profileResolver;
    private readonly ConfigResolutionService _configResolver;

    public DailyNormCalculator(
        IEmploymentProfileResolver profileResolver,
        ConfigResolutionService configResolver)
    {
        _profileResolver = profileResolver;
        _configResolver = configResolver;
    }

    /// <summary>
    /// One day's resolved norm. <see cref="Hours"/> is <c>null</c> when the day carries no
    /// meaningful per-weekday norm (ANNUAL_ACTIVITY / no dated profile) and <c>0</c> on weekends.
    /// </summary>
    public readonly record struct DailyNorm(DateOnly Date, decimal? Hours);

    /// <summary>
    /// Resolves the per-day norm for every day in the inclusive range
    /// <c>[rangeStart, rangeEnd]</c>. <paramref name="fallbackOrgId"/> is the employee's primary
    /// org id, used when the dated profile has no <c>OrgId</c> (matches the inline loop's
    /// <c>profile.OrgId ?? user.PrimaryOrgId</c>).
    /// </summary>
    public async Task<IReadOnlyList<DailyNorm>> ComputeRangeAsync(
        string employeeId,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        string fallbackOrgId,
        CancellationToken ct = default)
    {
        var result = new List<DailyNorm>();
        // Config resolution is cached per (okVersion, agreementCode, position, fraction, orgId)
        // within this request so we don't re-resolve the merged config for every weekday.
        var normCache = new Dictionary<string, (decimal WeeklyNormHours, NormModel NormModel)>(StringComparer.Ordinal);

        for (var day = rangeStart; day <= rangeEnd; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                result.Add(new DailyNorm(day, 0m));
                continue;
            }

            // Dated profile for THIS day (part_time_fraction, position, agreement, org).
            var profile = await _profileResolver.GetByEmployeeIdAtAsync(employeeId, day, ct);
            if (profile is null)
            {
                // No dated profile covering this day — graceful: blank norm rather than
                // fabricating one (mirrors the ADR-023 D3 graceful-fallback split for HTTP
                // read consumers).
                result.Add(new DailyNorm(day, null));
                continue;
            }

            var hours = await ComputeForProfileAsync(profile, day, fallbackOrgId, normCache, ct);
            result.Add(new DailyNorm(day, hours));
        }

        return result;
    }

    /// <summary>
    /// S66 / TASK-6604 (ADR-032 D4) — the per-day weekday-norm core, factored OUT of the
    /// <see cref="ComputeRangeAsync"/> loop VERBATIM so it can be driven by a caller-supplied
    /// (in-hand) <see cref="EmploymentProfile"/> rather than only a resolver-resolved one. The
    /// profile-change revaluation needs the norm computed from the NEW (uncommitted) part-time
    /// fraction / position, which the dated resolver cannot observe mid-tx (it owns a separate
    /// connection — ADR-032 D4); this overload lets the PUT pass the in-hand profile WITHOUT
    /// any second copy of the <c>WeeklyNorm × fraction / 5</c> formula (the S65 "one shared norm
    /// impl" invariant is preserved — both the range loop and the revaluation path now route the
    /// SAME math through here).
    ///
    /// <para>
    /// Behavior preserved byte-for-byte vs the prior inline loop: weekend ⇒ 0 (handled by the
    /// caller before calling, kept here too for the standalone overload below), ANNUAL_ACTIVITY ⇒
    /// null, otherwise <c>Math.Round(WeeklyNorm × fraction / 5, 2)</c>. OK version is resolved per
    /// day; org falls back to <paramref name="fallbackOrgId"/> when the profile carries none.
    /// </para>
    /// </summary>
    private async Task<decimal?> ComputeForProfileAsync(
        EmploymentProfile profile,
        DateOnly day,
        string fallbackOrgId,
        Dictionary<string, (decimal WeeklyNormHours, NormModel NormModel)> normCache,
        CancellationToken ct)
    {
        var okVersion = OkVersionResolver.ResolveVersion(day);
        var orgId = profile.OrgId ?? fallbackOrgId;
        var cacheKey = $"{okVersion}|{profile.AgreementCode}|{profile.Position}|{profile.PartTimeFraction}|{orgId}";
        if (!normCache.TryGetValue(cacheKey, out var resolved))
        {
            var config = await _configResolver.ResolveAsync(
                orgId, profile.AgreementCode, okVersion, profile.Position, ct);
            resolved = (config.WeeklyNormHours, config.NormModel);
            normCache[cacheKey] = resolved;
        }

        if (resolved.NormModel == NormModel.ANNUAL_ACTIVITY)
        {
            // Academic annual-activity norm: a per-weekday split is not meaningful.
            return null;
        }

        return Math.Round(resolved.WeeklyNormHours * profile.PartTimeFraction / 5m, 2);
    }

    /// <summary>
    /// S66 / TASK-6604 (ADR-032 D4) — single-day in-hand norm: resolves the weekday norm for
    /// <paramref name="day"/> from the caller-supplied <paramref name="profile"/> (its
    /// part-time-fraction / position / agreement / org), applying the SAME weekend-0 /
    /// ANNUAL_ACTIVITY-null / <c>WeeklyNorm × fraction / 5</c> rules as the range loop. Used by the
    /// profile-PUT revaluation so the post-change norm is computed from the in-hand values the dated
    /// resolver cannot yet see (uncommitted row, separate connection — ADR-032 D4). No second copy
    /// of the norm formula: this delegates to the shared <see cref="ComputeForProfileAsync"/> core.
    /// </summary>
    public async Task<decimal?> ComputeNormForProfileAsync(
        EmploymentProfile profile,
        DateOnly day,
        string fallbackOrgId,
        CancellationToken ct = default)
    {
        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return 0m;

        var normCache = new Dictionary<string, (decimal WeeklyNormHours, NormModel NormModel)>(StringComparer.Ordinal);
        return await ComputeForProfileAsync(profile, day, fallbackOrgId, normCache, ct);
    }

    /// <summary>
    /// S72 / TASK-7201 (SPRINT-72 R10) — the employee's WEEKDAY full-day norm as-of
    /// <paramref name="day"/>, INDEPENDENT of that day's weekend placement: the dated profile
    /// + merged config are resolved AT <paramref name="day"/> (the month GET passes the viewed
    /// month's LAST day) and the weekday formula <c>WeeklyNorm × fraction / 5</c> (rounded to
    /// 2 decimals) is applied with NO weekend-0 short-circuit — a month ending on a Sunday
    /// yields the same scalar as one ending on a Wednesday, by construction. This backs the
    /// month GET's <c>fullDayNormAtMonthEnd</c> scalar (the D-A hours-first balance-card
    /// conversion source — the FE never does client-side norm math).
    ///
    /// <para>
    /// Fail-soft (R10): returns <c>null</c> when no dated profile covers the day, when the
    /// resolved config is <c>ANNUAL_ACTIVITY</c> (a per-weekday split is not meaningful), or
    /// when the resolver fail-louds on the S34 data-integrity contract
    /// (<see cref="EmployeeProfileNotFoundException"/> — profile row without a covering
    /// agreement-code row). The FE renders the hours headline em-dashed on <c>null</c>; a
    /// read-only display scalar must never 500 the whole month GET. No second copy of the
    /// norm formula: this delegates to the same <see cref="ComputeForProfileAsync"/> core as
    /// the range loop and the revaluation path (the S65 one-shared-norm-impl invariant).
    /// </para>
    /// </summary>
    public async Task<decimal?> ComputeWeekdayNormAtAsync(
        string employeeId,
        DateOnly day,
        string fallbackOrgId,
        CancellationToken ct = default)
    {
        EmploymentProfile? profile;
        try
        {
            profile = await _profileResolver.GetByEmployeeIdAtAsync(employeeId, day, ct);
        }
        catch (EmployeeProfileNotFoundException)
        {
            // Data-integrity edge (profile row, no covering agreement-code row): fail-soft
            // for this display scalar — the authoritative write paths keep fail-louding.
            profile = null;
        }

        if (profile is null)
            return null;

        var normCache = new Dictionary<string, (decimal WeeklyNormHours, NormModel NormModel)>(StringComparer.Ordinal);
        // Deliberately NO weekend short-circuit: the weekday formula applies regardless of
        // where the anchor day falls in the week (R10 "weekend placement irrelevant").
        return await ComputeForProfileAsync(profile, day, fallbackOrgId, normCache, ct);
    }
}
