using StatsTid.SharedKernel.Models;

namespace StatsTid.RuleEngine.Api.Rules;

/// <summary>
/// Pure function: validates working time compliance against EU directive 2003/88/EC
/// and Danish Arbejdstidsloven. Checks daily rest (11h), weekly rest, max daily hours,
/// and 48h/week ceiling. No I/O, fully deterministic, version-aware via AgreementRuleConfig.
///
/// S20 / TASK-2006: this rule decomposes into four separately-registered classifications
/// — <see cref="MaxDailyRuleId"/>, <see cref="DailyRestRuleId"/>,
/// <see cref="WeeklyRestRuleId"/>, <see cref="Weekly48HCeilingRuleId"/>. The legacy
/// <see cref="RuleId"/> entry point (<see cref="Evaluate"/>) is preserved verbatim so
/// existing callers (Program.cs <c>/api/rules/check-compliance</c>, the existing
/// regression suite, and the Sprint 16 unit tests) continue to work without
/// modification — it runs all four checks and unions the findings under the legacy id.
///
/// <para>
/// <strong>ADR-039 segmentation note.</strong> The rest checks reconstruct one continuous stint
/// across midnight (and, via the GAP-B widen, across a period edge). This is safe with respect to
/// OK-version segmentation for a simple reason: these rest checks run ONLY on the unsegmented
/// monthly compliance path (<c>/api/rules/check-compliance</c> → <see cref="Evaluate"/>).
/// PeriodCalculationService's per-segment payroll evaluation never invokes them — it evaluates only
/// NORM_CHECK / SUPPLEMENT / OVERTIME / ON_CALL (+ absence / flex). So a stint is never split across
/// two OK segments underneath these checks; there is no cross-segment continuity to preserve here.
/// (The earlier ADR-016 "aligned-window / RejectIfMultipleSegments" rationale was incorrect — that
/// mechanism governs how per-segment rule RESULTS merge, not these checks.)
/// </para>
/// </summary>
public static class RestPeriodRule
{
    /// <summary>
    /// Legacy RuleId — preserved for backward compatibility with the
    /// <c>/api/rules/check-compliance</c> endpoint and existing tests that assert against
    /// the unified compliance check.
    /// </summary>
    public const string RuleId = "REST_PERIOD_CHECK";

    // S20 / TASK-2006 — multi-mode decomposition (ADR-016 D2):
    public const string MaxDailyRuleId = "REST_PERIOD_MAX_DAILY";
    public const string DailyRestRuleId = "REST_PERIOD_DAILY_REST";
    public const string WeeklyRestRuleId = "REST_PERIOD_WEEKLY_REST";
    public const string Weekly48HCeilingRuleId = "REST_PERIOD_48H_CEILING";

    /// <summary>
    /// Evaluates all compliance checks for the given period.
    /// Pure function, deterministic, no I/O. Result is tagged with the legacy
    /// <see cref="RuleId"/>; callers needing per-check classification should call the
    /// individual <see cref="EvaluateMaxDailyHours"/>, <see cref="EvaluateDailyRest"/>,
    /// <see cref="EvaluateWeeklyRest"/>, or <see cref="Evaluate48HCeiling"/> entry points.
    /// </summary>
    public static ComplianceCheckResult Evaluate(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var violations = new List<ComplianceViolation>();
        var warnings = new List<ComplianceViolation>();

        var periodEntries = NormalizeEntries(entries, periodStart, periodEnd);

        // 1. Max daily hours check (works for all entries, including hours-only). Hours-summing
        //    checks consume the PERIOD-FILTERED per-day rows so out-of-period hours never leak
        //    into period totals (ADR-039 BLOCKER-2).
        CheckMaxDailyHours(periodEntries, config, violations, warnings);

        // 2. Daily rest check. Reconstructs stints from the RAW (un-period-filtered) entries so a
        //    boundary crossing's out-of-window pre-half still rejoins its in-window post-half
        //    (ADR-039 BLOCKER-2); period relevance is then applied to whole STINTS, not rows.
        CheckDailyRest(entries, periodStart, periodEnd, config, violations, warnings);

        // 3. Weekly rest check — same raw-then-relevance stint reconstruction (BLOCKER-2).
        CheckWeeklyRest(entries, periodStart, periodEnd, config, violations, warnings);

        // 4. 48h/week ceiling (works for all entries) — period-filtered per-day rows.
        CheckWeeklyMaxHours(periodEntries, periodStart, periodEnd, config, violations, warnings);

        return new ComplianceCheckResult
        {
            RuleId = RuleId,
            EmployeeId = profile.EmployeeId,
            Success = violations.Count == 0,
            Violations = violations,
            Warnings = warnings
        };
    }

    /// <summary>
    /// S20 — single-check entry point for max daily hours
    /// (<see cref="MaxDailyRuleId"/>). Reuses the existing <see cref="CheckMaxDailyHours"/>
    /// helper; logic is bit-identical to the corresponding branch of <see cref="Evaluate"/>.
    /// </summary>
    public static ComplianceCheckResult EvaluateMaxDailyHours(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var violations = new List<ComplianceViolation>();
        var warnings = new List<ComplianceViolation>();
        var periodEntries = NormalizeEntries(entries, periodStart, periodEnd);
        CheckMaxDailyHours(periodEntries, config, violations, warnings);
        return BuildResult(MaxDailyRuleId, profile, violations, warnings);
    }

    /// <summary>
    /// S20 — single-check entry point for daily rest (<see cref="DailyRestRuleId"/>).
    /// Reuses the existing <see cref="CheckDailyRest"/> helper; logic is bit-identical
    /// to the corresponding branch of <see cref="Evaluate"/>.
    /// </summary>
    public static ComplianceCheckResult EvaluateDailyRest(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var violations = new List<ComplianceViolation>();
        var warnings = new List<ComplianceViolation>();
        // Raw entries + bounds — stint reconstruction happens before period relevance (BLOCKER-2).
        CheckDailyRest(entries, periodStart, periodEnd, config, violations, warnings);
        return BuildResult(DailyRestRuleId, profile, violations, warnings);
    }

    /// <summary>
    /// S20 — single-check entry point for weekly rest (<see cref="WeeklyRestRuleId"/>).
    /// Reuses the existing <see cref="CheckWeeklyRest"/> helper; logic is bit-identical
    /// to the corresponding branch of <see cref="Evaluate"/>.
    /// </summary>
    public static ComplianceCheckResult EvaluateWeeklyRest(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var violations = new List<ComplianceViolation>();
        var warnings = new List<ComplianceViolation>();
        // Raw entries + bounds — stint reconstruction happens before period relevance (BLOCKER-2).
        CheckWeeklyRest(entries, periodStart, periodEnd, config, violations, warnings);
        return BuildResult(WeeklyRestRuleId, profile, violations, warnings);
    }

    /// <summary>
    /// S20 — single-check entry point for the 48h/week ceiling
    /// (<see cref="Weekly48HCeilingRuleId"/>). Reuses the existing
    /// <see cref="CheckWeeklyMaxHours"/> helper; logic is bit-identical to the
    /// corresponding branch of <see cref="Evaluate"/>.
    /// </summary>
    public static ComplianceCheckResult Evaluate48HCeiling(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config)
    {
        var violations = new List<ComplianceViolation>();
        var warnings = new List<ComplianceViolation>();
        var periodEntries = NormalizeEntries(entries, periodStart, periodEnd);
        CheckWeeklyMaxHours(periodEntries, periodStart, periodEnd, config, violations, warnings);
        return BuildResult(Weekly48HCeilingRuleId, profile, violations, warnings);
    }

    private static IReadOnlyList<TimeEntry> NormalizeEntries(
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd) =>
        entries
            .Where(e => e.Date >= periodStart && e.Date <= periodEnd)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.StartTime)
            .ToList();

    private static ComplianceCheckResult BuildResult(
        string ruleId,
        EmploymentProfile profile,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings) =>
        new()
        {
            RuleId = ruleId,
            EmployeeId = profile.EmployeeId,
            Success = violations.Count == 0,
            Violations = violations,
            Warnings = warnings
        };

    /// <summary>
    /// Check 1: Max daily hours. Sum hours per day, flag if exceeding MaxDailyHours.
    /// </summary>
    private static void CheckMaxDailyHours(
        IReadOnlyList<TimeEntry> entries,
        AgreementRuleConfig config,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        var dailyHours = entries
            .GroupBy(e => e.Date)
            .Select(g => new { Date = g.Key, TotalHours = g.Sum(e => e.Hours) });

        foreach (var day in dailyHours)
        {
            if (day.TotalHours > config.MaxDailyHours)
            {
                violations.Add(new ComplianceViolation
                {
                    ViolationType = ComplianceViolationType.MAX_DAILY_HOURS,
                    Date = day.Date,
                    ActualValue = day.TotalHours,
                    ThresholdValue = config.MaxDailyHours,
                    Severity = ComplianceSeverity.VIOLATION,
                    Message = $"Daglig arbejdstid {day.TotalHours:F1}t overstiger maksimum {config.MaxDailyHours:F1}t"
                });
            }
        }
    }

    /// <summary>
    /// Check 2: Daily rest (11h minimum between the end of one working day and the start of the
    /// next). Only entries with both StartTime and EndTime can be analyzed. Voluntary unsocial
    /// hours skip this check (but not 48h ceiling). Derogation-allowed agreements get WARNING
    /// instead of VIOLATION.
    ///
    /// <para>
    /// ADR-039 (S132 TASK-1a + 1b-3) — the gap is computed from ABSOLUTE INSTANTS of reconstructed
    /// work stints, NOT from per-day clock fields. This fixes two related defects: (1) QUAL-001 —
    /// a midnight-crossing shift filed as one row (23:00→02:00) previously made the rest look like
    /// ~29h (its 02:00 end was read as day-N's end, then a full day added) when the true rest to a
    /// 07:00 start is 5h; (2) the normalized-halves case, where reading day D's end as 00:00 and
    /// day D+1's start as 00:00 manufactured a false 0-hour gap at midnight. Both are now correct
    /// because the two halves (ADR-039 D4 continuity link) reconstruct into ONE continuous stint
    /// whose absolute end is the real end instant.
    /// </para>
    /// </summary>
    private static void CheckDailyRest(
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        // Reconstruct from the RAW entries (BLOCKER-2) so a boundary crossing's out-of-window
        // pre-half rejoins its in-window post-half; THEN keep only stints overlapping the period.
        var stints = ReconstructStints(entries, config)
            .Where(s => IsPeriodRelevant(s, periodStart, periodEnd))
            .ToList();

        // Collapse stints into day-clusters keyed by the calendar day of the absolute START.
        // This preserves the pre-fix "a working day is one unit" semantics — a lunch-split day
        // (09:00–12:00 + 13:00–17:00) stays ONE cluster, so an intra-day break is never mistaken
        // for a rest-period breach — while using absolute instants so a midnight crossing no
        // longer produces a false 0-hour gap or a bogus ~29h rest. A crossing stint clusters
        // under its START day, so its post-midnight portion does not open a spurious next-day
        // working period.
        var clusters = stints
            .GroupBy(s => s.StartDate)
            .Select(g => new
            {
                Date = g.Key,
                Start = g.Min(s => s.AbsStart),
                End = g.Max(s => s.AbsEnd),
                AllVoluntary = g.All(s => s.AllVoluntary)
            })
            .OrderBy(c => c.Start)
            .ToList();

        for (int i = 0; i < clusters.Count - 1; i++)
        {
            var current = clusters[i];
            var next = clusters[i + 1];

            // Voluntary unsocial hours skip the rest check (unchanged policy): if either side is
            // wholly voluntary-and-allowed, no daily-rest breach is raised across that pair.
            if (current.AllVoluntary || next.AllVoluntary)
                continue;

            // Absolute rest gap: from one working day's end instant to the next day's start instant.
            var restHours = (decimal)(next.Start - current.End).TotalHours;

            if (restHours < config.MinimumRestHours)
            {
                var severity = config.RestPeriodDerogationAllowed
                    ? ComplianceSeverity.WARNING
                    : ComplianceSeverity.VIOLATION;

                // NOTE(b): overlapping stints (possible via /time, which has no overlap validation)
                // drive restHours negative — keep the VIOLATION (comparison uses the true value)
                // but clamp the DISPLAYED value to 0 so the message never reads an ugly "-1.0t".
                var displayRest = Math.Max(0m, restHours);

                var finding = new ComplianceViolation
                {
                    ViolationType = ComplianceViolationType.DAILY_REST,
                    Date = current.Date,
                    ActualValue = Math.Round(displayRest, 1),
                    ThresholdValue = config.MinimumRestHours,
                    Severity = severity,
                    Message = $"Hvileperiode mellem {current.Date:yyyy-MM-dd} og {next.Date:yyyy-MM-dd} er {displayRest:F1}t — minimum er {config.MinimumRestHours:F1}t"
                };

                if (severity == ComplianceSeverity.WARNING)
                    warnings.Add(finding);
                else
                    violations.Add(finding);
            }
        }
    }

    /// <summary>
    /// Check 3: Weekly rest — at least one 24-hour uninterrupted rest per 7-day period.
    /// Simplified check: if every day in a 7-day window has work, flag as a potential weekly rest
    /// violation.
    ///
    /// <para>
    /// ADR-039 (S132 TASK-1b-3) — a working day is counted from the START day of each reconstructed
    /// stint. A midnight-crossing shift is ONE stint starting on day D, so it counts D only and does
    /// NOT manufacture a false extra worked-day on D+1 that could flip a compliant 6-day week into a
    /// flagged 7-day week (research §1 interim ruling: count the shift under its start-day only).
    /// </para>
    /// </summary>
    private static void CheckWeeklyRest(
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        // Worked days = the START day of each reconstructed non-voluntary stint (crossing halves
        // collapse into one stint, counted under its start day only — no false extra worked-day).
        // BLOCKER-2: reconstruct from RAW entries and keep only stints overlapping the period, so a
        // boundary crossing on periodStart-1 counts under periodStart-1 (outside every window here)
        // and does NOT falsely mark periodStart as worked via its in-window post-half.
        var workDays = ReconstructStints(entries, config)
            .Where(s => IsPeriodRelevant(s, periodStart, periodEnd))
            .Where(s => !s.AllVoluntary)
            .Select(s => s.StartDate)
            .ToHashSet();

        // Slide a 7-day window across the period
        var windowStart = periodStart;
        while (windowStart.AddDays(6) <= periodEnd)
        {
            var windowEnd = windowStart.AddDays(6);
            var daysWorked = 0;
            for (var d = windowStart; d <= windowEnd; d = d.AddDays(1))
            {
                if (workDays.Contains(d))
                    daysWorked++;
            }

            // If all 7 days have work, no weekly rest day exists
            if (daysWorked == 7)
            {
                var severity = config.RestPeriodDerogationAllowed
                    ? ComplianceSeverity.WARNING
                    : ComplianceSeverity.VIOLATION;

                var finding = new ComplianceViolation
                {
                    ViolationType = ComplianceViolationType.WEEKLY_REST,
                    Date = windowStart,
                    ActualValue = 0,
                    ThresholdValue = 1,
                    Severity = severity,
                    Message = $"Ingen ugentlig hviledag i perioden {windowStart:yyyy-MM-dd} til {windowEnd:yyyy-MM-dd}"
                };

                if (severity == ComplianceSeverity.WARNING)
                    warnings.Add(finding);
                else
                    violations.Add(finding);
            }

            windowStart = windowStart.AddDays(7);
        }
    }

    /// <summary>
    /// Check 4: 48h/week ceiling over reference period.
    /// Average weekly hours must not exceed 48h.
    /// Voluntary unsocial hours STILL count (EU directive maximum is absolute).
    /// </summary>
    private static void CheckWeeklyMaxHours(
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        AgreementRuleConfig config,
        List<ComplianceViolation> violations,
        List<ComplianceViolation> warnings)
    {
        var totalHours = entries.Sum(e => e.Hours);
        var periodDays = periodEnd.DayNumber - periodStart.DayNumber + 1;
        var periodWeeks = periodDays / 7.0m;

        // Only check if we have at least 1 week of data
        if (periodWeeks < 1)
            return;

        var avgWeeklyHours = totalHours / periodWeeks;

        if (avgWeeklyHours > 48.0m)
        {
            violations.Add(new ComplianceViolation
            {
                ViolationType = ComplianceViolationType.WEEKLY_MAX_HOURS,
                Date = periodStart,
                ActualValue = Math.Round(avgWeeklyHours, 1),
                ThresholdValue = 48.0m,
                Severity = ComplianceSeverity.VIOLATION,
                Message = $"Gennemsnitlig ugentlig arbejdstid {avgWeeklyHours:F1}t overstiger EU-loftet paa 48t/uge over referenceperioden"
            });
        }
    }

    // -------------------------------------------------------------------
    // ADR-039 (S132 TASK-1a + 1b-3) — continuous-stint reconstruction for the rest checks.
    // -------------------------------------------------------------------

    /// <summary>
    /// A single continuous work stint expressed as ABSOLUTE instants. A midnight-crossing shift is
    /// ONE stint spanning two calendar days (ADR-039), never two day-buckets — so rest gaps and
    /// worked-day counts are computed from real wall-clock instants, not from a per-day view that
    /// mis-reads the midnight boundary.
    /// </summary>
    private readonly record struct WorkStint(
        DateTime AbsStart, DateTime AbsEnd, DateOnly StartDate, bool AllVoluntary);

    /// <summary>
    /// Reconstructs continuous work stints from the RAW (un-period-filtered) entries. Callers apply
    /// period relevance to the returned stints (see <see cref="IsPeriodRelevant"/>), NOT to the rows
    /// beforehand — otherwise a boundary crossing's out-of-window half would be dropped before its
    /// sibling could rejoin it (ADR-039 BLOCKER-2).
    ///
    /// <para>
    /// Rows sharing a non-null <see cref="TimeEntry.SourceStintId"/> are the two halves of one
    /// midnight-crossing stint (ADR-039 D4); a null-id row is its own stint. Within a stint, a row
    /// whose <c>EndTime ≤ StartTime</c> crosses midnight — either a RAW crossing entry that reached
    /// the rule un-normalized (the QUAL-001 case), OR the pre-half's <c>00:00</c> end-of-day
    /// sentinel — so its end instant is on the following calendar day. Only timed entries (both
    /// StartTime and EndTime present) participate; hours-only entries have no clock and cannot bound
    /// a rest gap (unchanged from the pre-fix checks).
    /// </para>
    /// </summary>
    private static List<WorkStint> ReconstructStints(
        IReadOnlyList<TimeEntry> entries, AgreementRuleConfig config)
    {
        var byStint = new Dictionary<Guid, List<TimeEntry>>();
        var singletons = new List<TimeEntry>();

        foreach (var e in entries)
        {
            if (!e.StartTime.HasValue || !e.EndTime.HasValue)
                continue;

            if (e.SourceStintId is Guid id)
            {
                if (!byStint.TryGetValue(id, out var list))
                {
                    list = new List<TimeEntry>(2);
                    byStint[id] = list;
                }
                list.Add(e);
            }
            else
            {
                singletons.Add(e);
            }
        }

        var stints = new List<WorkStint>(byStint.Count + singletons.Count);
        foreach (var grp in byStint.Values)
            stints.Add(BuildStint(grp, config));
        foreach (var e in singletons)
            stints.Add(BuildStint(new[] { e }, config));

        return stints;
    }

    /// <summary>
    /// Builds one stint's absolute interval from its constituent rows. The stint start is the
    /// earliest row-start instant; the stint end is the latest row-end instant, where a row that
    /// crosses midnight (<c>EndTime ≤ StartTime</c>) has its end instant advanced to the next day.
    /// The stint is "all voluntary" only if every constituent row is voluntary-and-allowed.
    /// </summary>
    private static WorkStint BuildStint(IReadOnlyList<TimeEntry> rows, AgreementRuleConfig config)
    {
        var absStart = DateTime.MaxValue;
        var absEnd = DateTime.MinValue;
        var allVoluntary = true;

        foreach (var r in rows)
        {
            var start = r.Date.ToDateTime(r.StartTime!.Value);
            var end = r.Date.ToDateTime(r.EndTime!.Value);
            if (r.EndTime!.Value <= r.StartTime!.Value)
                end = end.AddDays(1); // crosses midnight → end instant is on the following day

            if (start < absStart) absStart = start;
            if (end > absEnd) absEnd = end;
            allVoluntary &= r.VoluntaryUnsocialHours && config.VoluntaryUnsocialHoursAllowed;
        }

        return new WorkStint(absStart, absEnd, DateOnly.FromDateTime(absStart), allVoluntary);
    }

    /// <summary>
    /// True when a stint overlaps the (closed) calendar period <c>[periodStart, periodEnd]</c> in
    /// absolute terms. Stints are reconstructed from the RAW (un-period-filtered) entries so a
    /// boundary crossing joins correctly (BLOCKER-2); relevance is then applied to whole stints:
    /// a crossing on <c>periodStart-1</c> (fetched via the ADR-039 GAP-B one-day widen) overlaps the
    /// period (its post-midnight portion is in-window) and is kept — under its true start day
    /// <c>periodStart-1</c>, so it never falsely marks <c>periodStart</c> as worked. A fully
    /// out-of-period entry (e.g. a non-crossing <c>periodStart-1</c> shift, or anything after
    /// <c>periodEnd</c>) does not overlap and is dropped.
    /// </summary>
    private static bool IsPeriodRelevant(WorkStint stint, DateOnly periodStart, DateOnly periodEnd)
    {
        var periodStartInstant = periodStart.ToDateTime(TimeOnly.MinValue);          // periodStart 00:00
        var periodEndExclusive = periodEnd.AddDays(1).ToDateTime(TimeOnly.MinValue);  // periodEnd+1 00:00
        return stint.AbsStart < periodEndExclusive && stint.AbsEnd > periodStartInstant;
    }
}
