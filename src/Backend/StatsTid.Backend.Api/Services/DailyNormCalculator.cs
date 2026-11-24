using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
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
///   <item><description>the merged agreement config in force on THAT day
///     (<see cref="IAgreementConfigLookup.ResolveAsync"/> → <c>WeeklyNormHours</c> /
///     <c>NormModel</c>; central → position override → the profile of the organisation the dated
///     record names, ADR-043 D12), with the OK version resolved PER DAY via
///     <see cref="OkVersionResolver.ResolveVersion(DateOnly)"/> (NOT <c>year &gt;= 2026</c> — the
///     OK24→OK26 switch is 2026-04-01).</description></item>
/// </list>
/// Daily norm = <see cref="DayNorm.ForWeekday"/>: <c>WeeklyNorm × fraction / 5</c>, rounded to
/// 2 decimals half-to-even. Weekends ⇒ 0 (<see cref="DayNorm.IsWeekday"/>). S145 / TASK-14509
/// (R11): the formula is no longer written out here — it is the ONE shared helper the Rule
/// Engine's week-by-week core uses too, so the Skema screen and the payslip agree to the cent
/// (the same formula as before, so every value is unchanged; one implementation instead of two).
/// <c>ANNUAL_ACTIVITY</c> (academic) ⇒ <c>null</c> (a weekday split would be wrong — do NOT
/// approximate). No dated profile covering the day ⇒ <c>null</c> (graceful, ADR-023 D3 —
/// blank rather than a fabricated norm; e.g. days before <c>employment_start_date</c>).
/// </para>
///
/// <para>
/// <b>One configuration per stretch of unchanged days (B6, N-3).</b> A configuration is resolved
/// once for a stretch of days on which its key — the organisation, agreement, OK version and position
/// the day's dated record gives — is the same and no layer of that key gets a new version: no
/// published agreement-config version, position-override version or local-profile activation starts
/// inside the stretch (the three date reads, <see cref="AgreementConfigRepository.GetPublishedAppliesFromDatesAsync"/>,
/// <see cref="PositionOverrideRepository.GetVersionAppliesFromDatesAsync"/>,
/// <see cref="LocalAgreementProfileRepository.GetActivationsInPeriodAsync"/>, the cuts payroll's planner
/// reads). A configuration resolved for one stretch is never used for a day outside it: each day asks with
/// its own key, and a stretch ends the day before its key's next version starts. A local profile ends only
/// when its successor starts (supersession; there is no close without one), so the activations cover every
/// change of the local layer. The stretches live for one HTTP request (<see cref="HttpContext.Items"/>),
/// so every caller in a request shares them — the Skema month's <c>dailyNorm</c>, <c>consumptionBasis</c>,
/// the month-end scalar and the served minimum rest, and the year overview's twelve months — and the next
/// request reads the configuration again, so a publish is seen by the next request. Like
/// <see cref="HttpContext"/> itself, the request's stretches are used by one thread at a time. A calculator
/// built without the three date repositories (a test's direct construction) cuts at every day, and one built
/// without an HTTP context accessor keeps its stretches for one call: both resolve every day correctly,
/// only with more reads.
/// </para>
///
/// <para>
/// A lookup refusal (<see cref="ConfigLookupUnavailableException"/>, <see cref="NoConfigInForceException"/>)
/// is not caught here: it reaches the caller and the Backend's one mapping of the two
/// (<c>ConfigLookupRefusalMiddleware</c>). The one exception is <see cref="ComputeMonthNormAsync"/>'s weekday
/// that no dated record covers, where <see cref="NoConfigInForceException"/> alone is answered by the rule its
/// summary states (the T14b stack's 5a ruling T2, the second ruled exception to B4's no-catch rule). A failed
/// date read is the same refusal: its database error is logged once and answered as
/// <see cref="ConfigLookupUnavailableException"/>, never as a cut not made.
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
    // Per instance, so two calculators in one request (a test host's own) never share stretches.
    private readonly object _requestStretchesKey = new();

    private readonly IEmploymentProfileResolver _profileResolver;
    private readonly IAgreementConfigLookup _configLookup;
    private readonly AgreementConfigRepository? _agreementConfigRepo;
    private readonly PositionOverrideRepository? _positionOverrideRepo;
    private readonly LocalAgreementProfileRepository? _localAgreementProfileRepo;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<DailyNormCalculator> _logger;

    /// <summary>
    /// The Backend host supplies every parameter (each is registered). The trailing five are optional so a
    /// test can build the calculator from the resolver and the lookup alone; see the class remark for what
    /// such a calculator does.
    /// </summary>
    public DailyNormCalculator(
        IEmploymentProfileResolver profileResolver,
        IAgreementConfigLookup configLookup,
        AgreementConfigRepository? agreementConfigRepo = null,
        PositionOverrideRepository? positionOverrideRepo = null,
        LocalAgreementProfileRepository? localAgreementProfileRepo = null,
        IHttpContextAccessor? httpContextAccessor = null,
        ILogger<DailyNormCalculator>? logger = null)
    {
        _profileResolver = profileResolver;
        _configLookup = configLookup;
        _agreementConfigRepo = agreementConfigRepo;
        _positionOverrideRepo = positionOverrideRepo;
        _localAgreementProfileRepo = localAgreementProfileRepo;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger ?? NullLogger<DailyNormCalculator>.Instance;
    }

    /// <summary>
    /// One day's resolved norm. <see cref="Hours"/> is <c>null</c> when the day carries no
    /// meaningful per-weekday norm (ANNUAL_ACTIVITY / no dated profile) and <c>0</c> on weekends.
    /// </summary>
    public readonly record struct DailyNorm(DateOnly Date, decimal? Hours);

    /// <summary>One day's configuration in force; <c>null</c> where no dated record covers the day.</summary>
    public readonly record struct DailyConfig(DateOnly Date, AgreementRuleConfig? Config);

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
        var stretches = RequestStretches();
        var result = new List<DailyNorm>();

        for (var day = rangeStart; day <= rangeEnd; day = day.AddDays(1))
        {
            // S145 / TASK-14509 — the shared weekend test (DayNorm); same Saturday/Sunday rule.
            if (!DayNorm.IsWeekday(day))
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

            var hours = await ComputeForProfileAsync(profile, day, fallbackOrgId, rangeEnd, stretches, ct);
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
    /// day; org falls back to <paramref name="fallbackOrgId"/> when the profile carries none. The
    /// configuration is the one of the day's stretch, which may reach as far as
    /// <paramref name="horizonEnd"/> (the caller's last day).
    /// </para>
    ///
    /// <para>
    /// S145 / TASK-14509 (R11): the weekday amount is <see cref="DayNorm.ForWeekday"/> — the same
    /// function the Rule Engine's week-by-week core judges each weekday with. It is the formula
    /// above, unchanged (<c>Math.Round(decimal, 2)</c> already rounded half-to-even, which is what
    /// the helper states explicitly), so no value moves; what changes is that there is now one
    /// implementation, so the screen and the payslip cannot drift apart.
    /// </para>
    /// </summary>
    private async Task<decimal?> ComputeForProfileAsync(
        EmploymentProfile profile,
        DateOnly day,
        string fallbackOrgId,
        DateOnly horizonEnd,
        RequestStretchSet stretches,
        CancellationToken ct)
    {
        var config = await ConfigInForceAsync(
            FullChainKey(profile.OrgId ?? fallbackOrgId, profile.AgreementCode, profile.Position, day),
            day, horizonEnd, stretches, ct);

        if (config.NormModel == NormModel.ANNUAL_ACTIVITY)
        {
            // Academic annual-activity norm: a per-weekday split is not meaningful.
            return null;
        }

        return DayNorm.ForWeekday(config.WeeklyNormHours, profile.PartTimeFraction);
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
        if (!DayNorm.IsWeekday(day))
            return 0m;

        return await ComputeForProfileAsync(profile, day, fallbackOrgId, day, RequestStretches(), ct);
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
    /// read-only display scalar must never 500 the whole month GET over a data gap in the
    /// employee's record. A configuration-lookup refusal is not that: it propagates like every
    /// other read of the configuration, so the screen names it (P-13). No second copy of the
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
        var profile = await ProfileOrNullAsync(employeeId, day, ct);
        if (profile is null)
            return null;

        // Deliberately NO weekend short-circuit: the weekday formula applies regardless of
        // where the anchor day falls in the week (R10 "weekend placement irrelevant").
        return await ComputeForProfileAsync(profile, day, fallbackOrgId, day, RequestStretches(), ct);
    }

    /// <summary>
    /// The configuration in force on every day of <c>[rangeStart, rangeEnd]</c>, weekends included, for the
    /// employee's dated record of that day (the full chain, the same resolution as <see cref="ComputeRangeAsync"/>).
    /// <c>null</c> where no dated record covers the day, or the record lacks its agreement-code or organisation
    /// row (<see cref="EmployeeProfileNotFoundException"/>, fail-soft as the month-end scalar). Backs the Skema
    /// month's served minimum rest (S0-4). On the month's own days the Skema month reaches that second null on
    /// weekend days only: a weekday gap fails the month through the daily-norm rows, since
    /// <see cref="ComputeRangeAsync"/> resolves every weekday without catching it (5a ruling T8). On the day after the
    /// month, which the daily-norm rows do not cover, it is reached on any day.
    /// </summary>
    public async Task<IReadOnlyList<DailyConfig>> ResolveConfigsAsync(
        string employeeId,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        string fallbackOrgId,
        CancellationToken ct = default)
    {
        var stretches = RequestStretches();
        var result = new List<DailyConfig>();
        for (var day = rangeStart; day <= rangeEnd; day = day.AddDays(1))
        {
            var profile = await ProfileOrNullAsync(employeeId, day, ct);
            result.Add(new DailyConfig(day, profile is null
                ? null
                : await ConfigInForceAsync(
                    FullChainKey(profile.OrgId ?? fallbackOrgId, profile.AgreementCode, profile.Position, day),
                    day, rangeEnd, stretches, ct)));
        }

        return result;
    }

    /// <summary>
    /// The full chain (central → position override → the organisation's local profile) in force on
    /// <paramref name="day"/> for a dated record's organisation, agreement and position, at the day's OK version,
    /// reused for the day's stretch, which may reach as far as <paramref name="horizonEnd"/>.
    /// </summary>
    public Task<AgreementRuleConfig> ResolveConfigAsync(
        string orgId, string agreementCode, string? position, DateOnly day, DateOnly horizonEnd,
        CancellationToken ct = default) =>
        ConfigInForceAsync(FullChainKey(orgId, agreementCode, position, day), day, horizonEnd, RequestStretches(), ct);

    /// <summary>
    /// The central agreement configuration alone in force on <paramref name="day"/> at the day's OK version
    /// (<see cref="IAgreementConfigLookup.ResolveCentralAsync"/>), reused for the day's stretch, which may reach
    /// as far as <paramref name="horizonEnd"/>.
    /// </summary>
    public Task<AgreementRuleConfig> ResolveCentralConfigAsync(
        string agreementCode, DateOnly day, DateOnly horizonEnd, CancellationToken ct = default) =>
        ConfigInForceAsync(CentralKey(agreementCode, day), day, horizonEnd, RequestStretches(), ct);

    /// <summary>
    /// The hours expected in a month on Teamoversigt and on the balance summary (B-9; NR-20 (b); N-2): over the
    /// month's weekdays d, the sum of <c>DayNorm.ForWeekday(W_d, f)</c>, each day rounded by the helper, never
    /// once over the month. The payslip sums each day's own norm too, so the screens and pay agree when a
    /// configured weekly norm changes inside the month; a change of part-time fraction still differs (QUAL-226).
    /// <list type="bullet">
    ///   <item><description><b>W_d</b> is the weekly norm of the full chain in force on d for d's own dated
    ///   record (<paramref name="recordOn"/>), at d's OK version; on a day no record covers, for the month-end
    ///   record's organisation, agreement and position.</description></item>
    ///   <item><description><b>f</b> is <paramref name="monthEndFraction"/>, the part-time fraction at month end
    ///   as the screen reads it; never each day's own fraction (the owner's QUAL-198 ruling).</description></item>
    ///   <item><description><b>No record at month end</b> (N-2; a profile closed before it, or none at all):
    ///   W_d is the central configuration alone in force on d for <paramref name="noRecordAgreementCode"/> (the
    ///   screen's agreement source), at d's OK version, and f = 1.0 (ADR-023 D3: a graceful absence, the screen
    ///   renders).</description></item>
    ///   <item><description><b>An annual-activity agreement</b> (the month-end record's month-end configuration
    ///   says so; BC-5 (a)) keeps the formula it had: weekdays × <c>DayNorm.ForWeekday(W, f)</c>, W that
    ///   configuration's weekly norm.</description></item>
    ///   <item><description><b>A weekday no dated record covers whose key has no configuration in force that
    ///   day</b> (a new agreement published from a later date; the T14b stack's 5a ruling T2, N-2): W_d is the
    ///   same key's configuration in force at month end, and the day counts 0.00 h when none is in force at month
    ///   end either. Such a day is already a stand-in (QUAL-226), so it never fails the screen. This is the second
    ///   ruled exception to B4's no-catch rule: exactly <see cref="NoConfigInForceException"/>, exactly on such a
    ///   day, here only. A day a record covers still refuses by name, the month-end record's month-end read still
    ///   refuses, and <see cref="ConfigLookupUnavailableException"/> always propagates. A refusal on such a day d
    ///   is remembered for the request on every day on or before the day before the agreement's next published
    ///   central version start, at most month end (from the date reads the stretch already makes): no published
    ///   version applies on d, so none applies on an earlier day either (5a ruling U1). So in the wired host such
    ///   days ask the lookup a question it refuses at most once per agreement and OK version per month computed,
    ///   however many employees the month is computed for, and both of today's callers (Teamoversigt, the balance
    ///   summary) compute one month per request: a roster does not pay one refused read and one Error log line
    ///   per person per day. A calculator built without the date repositories remembers a refusal on d through d
    ///   only: no day on or before a refused one asks again, so each refused weekday asks once and each later
    ///   no-record weekday asks anew, except that a refused month-end fallback question is remembered through
    ///   month end, so no later day of the month asks. Without an HTTP context accessor the memo, like the stretches, lasts one call
    ///   (the class remark).</description></item>
    /// </list>
    /// No compiled value and no floor: any other lookup refusal propagates (P-13). Every configuration is resolved
    /// once per stretch (the class remark).
    /// </summary>
    public async Task<decimal> ComputeMonthNormAsync(
        Func<DateOnly, DatedRecord?> recordOn,
        DateOnly monthStart,
        DateOnly monthEnd,
        decimal monthEndFraction,
        string noRecordAgreementCode,
        CancellationToken ct = default)
    {
        var stretches = RequestStretches();
        var monthEndRecord = recordOn(monthEnd);

        // The annual-activity test first: it needs the month-end configuration only, so a version that starts
        // inside the month cannot refuse an agreement whose figure never reads the earlier days.
        if (monthEndRecord is not null)
        {
            var monthEndConfig = await ConfigInForceAsync(
                FullChainKey(monthEndRecord.OrgId, monthEndRecord.AgreementCode, monthEndRecord.Position, monthEnd),
                monthEnd, monthEnd, stretches, ct);
            if (monthEndConfig.NormModel == NormModel.ANNUAL_ACTIVITY)
            {
                var weekdays = 0;
                for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
                {
                    if (DayNorm.IsWeekday(day))
                        weekdays++;
                }

                return weekdays * DayNorm.ForWeekday(monthEndConfig.WeeklyNormHours, monthEndFraction);
            }
        }

        var fraction = monthEndRecord is null ? 1.0m : monthEndFraction;
        var sum = 0m;
        for (var day = monthStart; day <= monthEnd; day = day.AddDays(1))
        {
            if (!DayNorm.IsWeekday(day))
                continue;

            var dayRecord = recordOn(day);
            var record = dayRecord ?? monthEndRecord;
            var key = record is null
                ? CentralKey(noRecordAgreementCode, day)
                : FullChainKey(record.OrgId, record.AgreementCode, record.Position, day);

            // The OK version changes only on 1 April, so the key built for d is the month end's key too.
            var config = dayRecord is not null
                ? await ConfigInForceAsync(key, day, monthEnd, stretches, ct)
                : await NoRecordConfigAsync(key, day) ?? await NoRecordConfigAsync(key, monthEnd);
            if (config is not null)
                sum += DayNorm.ForWeekday(config.WeeklyNormHours, dayRecord?.Fraction ?? fraction);
        }

        return sum;

        // The configuration of a no-record weekday's key in force on asOf, or null when none is (5a ruling T2).
        async Task<AgreementRuleConfig?> NoRecordConfigAsync(StretchKey key, DateOnly asOf)
        {
            if (stretches.NoneInForce(key, asOf))
                return null;

            var plan = await PlanStretchAsync(key, asOf, monthEnd, stretches, ct);
            if (plan.Config is not null)
                return plan.Config;

            try
            {
                return await ResolveStretchAsync(key, asOf, plan.To, stretches, ct);
            }
            catch (NoConfigInForceException)
            {
                // 5a ruling T2: the second ruled catch of a lookup refusal (B4's exception), this type on a weekday
                // no dated record covers only. The lookup's Error log line for this refusal is expected: the screen
                // is still served (5a ruling T11). No published central version applies on asOf, so none applies on
                // an earlier day or before the agreement's next central version starts: the request asks no more
                // for a no-record day on or before CentralTo (5a ruling U1).
                stretches.AddNoneInForce(key, plan.CentralTo);
                return null;
            }
        }
    }

    /// <summary>The dated profile, or <c>null</c> when none covers the day or it lacks its agreement-code or
    /// organisation row (<see cref="EmployeeProfileNotFoundException"/>; fail-soft for a display read, the
    /// authoritative write paths keep fail-louding).</summary>
    private async Task<EmploymentProfile?> ProfileOrNullAsync(string employeeId, DateOnly day, CancellationToken ct)
    {
        try
        {
            return await _profileResolver.GetByEmployeeIdAtAsync(employeeId, day, ct);
        }
        catch (EmployeeProfileNotFoundException)
        {
            return null;
        }
    }

    private static StretchKey FullChainKey(string orgId, string agreementCode, string? position, DateOnly day) =>
        new(orgId, agreementCode, OkVersionResolver.ResolveVersion(day), position, CentralOnly: false);

    private static StretchKey CentralKey(string agreementCode, DateOnly day) =>
        new(null, agreementCode, OkVersionResolver.ResolveVersion(day), null, CentralOnly: true);

    /// <summary>
    /// The configuration of <paramref name="key"/> in force on <paramref name="day"/>: from a stretch of the
    /// request that covers the day, else resolved through the lookup for a new stretch from the day to the day
    /// before the key's next version starts, at most <paramref name="horizonEnd"/>. The key's latest stretch
    /// ending before the day is extended instead when no version starts after its end, up to and including the
    /// day (its configuration is then the day's too): the year overview's next month, or a month whose first
    /// weekday follows a weekend.
    /// </summary>
    private async Task<AgreementRuleConfig> ConfigInForceAsync(
        StretchKey key, DateOnly day, DateOnly horizonEnd, RequestStretchSet stretches, CancellationToken ct)
    {
        var plan = await PlanStretchAsync(key, day, horizonEnd, stretches, ct);
        return plan.Config ?? await ResolveStretchAsync(key, day, plan.To, stretches, ct);
    }

    /// <summary>
    /// <see cref="ConfigInForceAsync"/> before its lookup: the configuration of a stretch of the request that covers
    /// the day or of the extended earlier stretch, else none yet, with <c>To</c> the last day a new stretch from the
    /// day may reach. <c>CentralTo</c> is that bound counted on the central layer's version starts alone, the one
    /// layer whose absence refuses (<see cref="NoConfigInForceException"/>), so a refusal on the day holds through it.
    /// </summary>
    private async Task<StretchPlan> PlanStretchAsync(
        StretchKey key, DateOnly day, DateOnly horizonEnd, RequestStretchSet stretches, CancellationToken ct)
    {
        if (stretches.Covering(key, day) is { } covering)
            return new StretchPlan(covering.Config, covering.To, covering.To);

        var to = day;
        var centralTo = day;
        ConfigStretch? extended = null;
        if (horizonEnd > day
            && _agreementConfigRepo is not null && _positionOverrideRepo is not null && _localAgreementProfileRepo is not null)
        {
            // The strict lower bound is the earlier stretch's last day, else the day before, so a version
            // starting on the day itself (or between the two) is seen: it ends the earlier stretch, which is
            // then not extended.
            var earlier = stretches.LatestEndingBefore(key, day);
            var starts = await VersionStartsAsync(key, earlier?.To ?? day.AddDays(-1), horizonEnd, ct);
            var next = horizonEnd.AddDays(1);
            var nextCentral = next;
            var startsByDay = false;
            foreach (var (start, central) in starts)
            {
                if (start <= day)
                {
                    startsByDay = true;
                    continue;
                }

                if (start < next)
                    next = start;
                if (central && start < nextCentral)
                    nextCentral = start;
            }

            to = next.AddDays(-1);
            centralTo = nextCentral.AddDays(-1);
            if (!startsByDay)
                extended = earlier;
        }

        if (extended is not null)
        {
            extended.To = to;
            return new StretchPlan(extended.Config, to, to);
        }

        return new StretchPlan(null, to, centralTo);
    }

    /// <summary>Resolves <paramref name="key"/> through the lookup as of <paramref name="day"/> and keeps it for the
    /// request as the stretch <c>[day, to]</c>.</summary>
    private async Task<AgreementRuleConfig> ResolveStretchAsync(
        StretchKey key, DateOnly day, DateOnly to, RequestStretchSet stretches, CancellationToken ct)
    {
        var config = key.CentralOnly
            ? await _configLookup.ResolveCentralAsync(key.AgreementCode, key.OkVersion, day, ct)
            : await _configLookup.ResolveAsync(key.OrgId!, key.AgreementCode, key.OkVersion, key.Position, day, ct);
        stretches.Add(key, new ConfigStretch(day, to, config));
        return config;
    }

    /// <summary>
    /// The dates inside <c>(<paramref name="afterExclusive"/>, <paramref name="toInclusive"/>]</c> on which a
    /// new version of one of <paramref name="key"/>'s layers comes into force, each marked whether it is the
    /// central layer's: the central layer's published versions, and for the full chain the position override's
    /// versions (when a position is given) and the organisation's local-profile activations.
    /// </summary>
    private async Task<List<(DateOnly Start, bool Central)>> VersionStartsAsync(
        StretchKey key, DateOnly afterExclusive, DateOnly toInclusive, CancellationToken ct)
    {
        var starts = (await ReadDatesAsync(
                () => _agreementConfigRepo!.GetPublishedAppliesFromDatesAsync(
                    key.AgreementCode, key.OkVersion, afterExclusive, toInclusive, ct),
                "central agreement configuration", key))
            .Select(start => (start, true))
            .ToList();
        if (key.CentralOnly)
            return starts;

        if (key.Position is not null)
        {
            starts.AddRange((await ReadDatesAsync(
                    () => _positionOverrideRepo!.GetVersionAppliesFromDatesAsync(
                        key.AgreementCode, key.OkVersion, key.Position, afterExclusive, toInclusive, ct),
                    "position override", key))
                .Select(start => (start, false)));
        }

        var activations = await ReadDatesAsync(
            () => _localAgreementProfileRepo!.GetActivationsInPeriodAsync(
                key.OrgId!, key.AgreementCode, key.OkVersion, afterExclusive, toInclusive, ct),
            "local agreement profile", key);
        starts.AddRange(activations.Select(activation => (activation.EffectiveFrom, false)));
        return starts;
    }

    /// <summary>
    /// Runs one date read. A database error is logged once and refused as
    /// <see cref="ConfigLookupUnavailableException"/>, the lookup's own outcome for a failed configuration read
    /// (P-13); every other exception propagates unchanged.
    /// </summary>
    private async Task<T> ReadDatesAsync<T>(Func<Task<T>> read, string layer, StretchKey key)
    {
        try
        {
            return await read();
        }
        catch (DbException ex)
        {
            _logger.LogWarning(ex,
                "The {Layer} version-date read failed for {AgreementCode}/{OkVersion} (position {Position}, " +
                "organisation {OrgId}); refused as unavailable",
                layer, key.AgreementCode, key.OkVersion, key.Position ?? "(none)", key.OrgId ?? "(none)");
            throw new ConfigLookupUnavailableException($"{layer} version-date", ex);
        }
    }

    /// <summary>The request's stretches, or a set for this call alone when there is no HTTP request.</summary>
    private RequestStretchSet RequestStretches()
    {
        var items = _httpContextAccessor?.HttpContext?.Items;
        if (items is null)
            return new RequestStretchSet();
        if (items.TryGetValue(_requestStretchesKey, out var existing) && existing is RequestStretchSet set)
            return set;
        var created = new RequestStretchSet();
        items[_requestStretchesKey] = created;
        return created;
    }

    /// <summary>What a configuration is resolved for: the full chain for an organisation, agreement, OK version
    /// and position, or the central layer alone (no organisation, no position).</summary>
    private readonly record struct StretchKey(
        string? OrgId, string AgreementCode, string OkVersion, string? Position, bool CentralOnly);

    /// <summary>One resolved stretch: <c>[From, To]</c>, inclusive, and the configuration in force on each of
    /// its days.</summary>
    private sealed class ConfigStretch(DateOnly from, DateOnly to, AgreementRuleConfig config)
    {
        public DateOnly From { get; } = from;
        public DateOnly To { get; set; } = to;
        public AgreementRuleConfig Config { get; } = config;
    }

    /// <summary>What <see cref="PlanStretchAsync"/> found: the configuration when a stretch already holds the day,
    /// else <c>null</c>; the last day a new stretch from the day may reach; and that bound on central starts
    /// alone.</summary>
    private readonly record struct StretchPlan(AgreementRuleConfig? Config, DateOnly To, DateOnly CentralTo);

    private sealed class RequestStretchSet
    {
        private readonly Dictionary<StretchKey, List<ConfigStretch>> _byKey = new();

        // 5a rulings T2 and U1's memo, written and read by ComputeMonthNormAsync's no-record weekdays only: per
        // agreement and OK version, the last day on or before which no published central version is in force. A
        // refusal on d proves it through the day before the next central version start (CentralTo; d itself for a
        // calculator without the date repositories) and on every earlier day, since the in-force read takes the
        // published version dated on or before the day (AgreementConfigRepository.GetInForceAsync): none for d is
        // none for an earlier day. The central layer alone decides that refusal (ConfigResolutionService), so the
        // day holds for every key of the pair, and the latest day proved covers every earlier one.
        private readonly Dictionary<(string AgreementCode, string OkVersion), DateOnly> _noneInForceThrough = new();

        public bool NoneInForce(StretchKey key, DateOnly day) =>
            _noneInForceThrough.TryGetValue((key.AgreementCode, key.OkVersion), out var through) && day <= through;

        public void AddNoneInForce(StretchKey key, DateOnly through)
        {
            var pair = (key.AgreementCode, key.OkVersion);
            if (!_noneInForceThrough.TryGetValue(pair, out var known) || through > known)
                _noneInForceThrough[pair] = through;
        }

        public ConfigStretch? Covering(StretchKey key, DateOnly day) =>
            _byKey.TryGetValue(key, out var stretches)
                ? stretches.Find(stretch => stretch.From <= day && day <= stretch.To)
                : null;

        public ConfigStretch? LatestEndingBefore(StretchKey key, DateOnly day) =>
            _byKey.TryGetValue(key, out var stretches)
                ? stretches.Where(stretch => stretch.To < day).MaxBy(stretch => stretch.To)
                : null;

        public void Add(StretchKey key, ConfigStretch stretch)
        {
            if (!_byKey.TryGetValue(key, out var stretches))
                _byKey[key] = stretches = new List<ConfigStretch>();
            stretches.Add(stretch);
        }
    }
}

/// <summary>A dated employment record on one day: the agreement, the position and the organisation it names.</summary>
// CI-M5 MUTATION M-12: the record carries the day's own part-time fraction, which ComputeMonthNormAsync uses instead of the month-end one
public sealed record DatedRecord(string AgreementCode, string? Position, string OrgId, decimal? Fraction = null);

/// <summary>
/// The dated employment records of a set of employees over a range, read set-based in three statements on the
/// caller's connection (<c>employee_profiles</c>, <c>user_agreement_codes</c> and <c>user_organisations</c> rows
/// overlapping the range, <c>= ANY(@ids)</c>), so a screen reads each employee's records once for the month
/// instead of once per day (N-3). A day's record follows <see cref="EmploymentProfileResolver"/>'s rules: the
/// profile row covering the day (joined to its user), end-exclusive (ADR-018 D8), then the organisation row and
/// the agreement-code row covering it, the latest start winning where rows overlap; on overlapping profile rows only
/// the timeline does, since the resolver's profile read has no order and which row it returns is undefined
/// (QUAL-263). A covering profile row without either of the other two is a record gap, as the resolver's
/// <see cref="EmployeeProfileNotFoundException"/> is; the caller decides what a gap means (ADR-023 D3).
/// </summary>
public sealed class DatedRecordTimeline
{
    private readonly Dictionary<string, List<DatedRow>> _profiles;
    private readonly Dictionary<string, List<DatedRow>> _agreementCodes;
    private readonly Dictionary<string, List<DatedRow>> _organisations;

    private DatedRecordTimeline(
        Dictionary<string, List<DatedRow>> profiles,
        Dictionary<string, List<DatedRow>> agreementCodes,
        Dictionary<string, List<DatedRow>> organisations)
    {
        _profiles = profiles;
        _agreementCodes = agreementCodes;
        _organisations = organisations;
    }

    /// <summary>Reads the rows of <paramref name="employeeIds"/> overlapping <c>[from, to]</c>.</summary>
    public static async Task<DatedRecordTimeline> ReadAsync(
        NpgsqlConnection conn, IReadOnlyCollection<string> employeeIds, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        const string profilesSql =
            """
            SELECT ep.employee_id, ep.effective_from, ep.effective_to, ep.position, ep.part_time_fraction
            FROM employee_profiles ep
            INNER JOIN users u ON u.user_id = ep.employee_id
            WHERE ep.employee_id = ANY(@ids)
              AND ep.effective_from <= @to AND (ep.effective_to IS NULL OR ep.effective_to > @from)
            """;
        const string agreementCodesSql =
            """
            SELECT user_id, effective_from, effective_to, agreement_code
            FROM user_agreement_codes
            WHERE user_id = ANY(@ids)
              AND effective_from <= @to AND (effective_to IS NULL OR effective_to > @from)
            """;
        const string organisationsSql =
            """
            SELECT user_id, effective_from, effective_to, org_id
            FROM user_organisations
            WHERE user_id = ANY(@ids)
              AND effective_from <= @to AND (effective_to IS NULL OR effective_to > @from)
            """;

        var ids = employeeIds.ToArray();
        await using var profiles = new NpgsqlCommand(profilesSql, conn);
        await using var agreementCodes = new NpgsqlCommand(agreementCodesSql, conn);
        await using var organisations = new NpgsqlCommand(organisationsSql, conn);
        return new DatedRecordTimeline(
            await ReadRowsAsync(profiles, ids, from, to, ct),
            await ReadRowsAsync(agreementCodes, ids, from, to, ct),
            await ReadRowsAsync(organisations, ids, from, to, ct));
    }

    /// <summary>
    /// The employee's record on <paramref name="day"/>, or <c>null</c> with the missing table named
    /// (<c>employment_profile</c>, <c>organisation</c> or <c>agreement_code</c>; server log only, no date).
    /// </summary>
    public (DatedRecord? Record, string? MissingRecord) On(string employeeId, DateOnly day)
    {
        var profile = Covering(_profiles, employeeId, day);
        if (profile is null)
            return (null, "employment_profile");
        var organisation = Covering(_organisations, employeeId, day);
        if (organisation?.Value is null)
            return (null, "organisation");
        var agreementCode = Covering(_agreementCodes, employeeId, day);
        if (agreementCode?.Value is null)
            return (null, "agreement_code");
        return (new DatedRecord(agreementCode.Value, profile.Value, organisation.Value, profile.Fraction), null);
    }

    private static DatedRow? Covering(Dictionary<string, List<DatedRow>> rows, string employeeId, DateOnly day)
    {
        if (!rows.TryGetValue(employeeId, out var list))
            return null;
        DatedRow? covering = null;
        foreach (var row in list)
        {
            if (row.From <= day && (row.To is null || row.To > day) && (covering is null || row.From > covering.From))
                covering = row;
        }

        return covering;
    }

    private static async Task<Dictionary<string, List<DatedRow>>> ReadRowsAsync(
        NpgsqlCommand cmd, string[] ids, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = new Dictionary<string, List<DatedRow>>(StringComparer.Ordinal);
        cmd.Parameters.AddWithValue("ids", ids);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var employeeId = reader.GetString(0);
            if (!rows.TryGetValue(employeeId, out var list))
                rows[employeeId] = list = new List<DatedRow>();
            list.Add(new DatedRow(
                reader.GetFieldValue<DateOnly>(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateOnly>(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetDecimal(4) : null));
        }

        return rows;
    }

    /// <summary>One dated row: <c>[From, To)</c>, end-exclusive, and its value (the position, which may be null,
    /// the agreement code or the organisation).</summary>
    private sealed record DatedRow(DateOnly From, DateOnly? To, string? Value, decimal? Fraction = null);
}
