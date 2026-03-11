using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Config;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// Tests for the RestPeriodRule (Sprint 16).
/// Validates working time compliance: daily rest (11h), weekly rest,
/// max daily hours (13h), 48h/week ceiling, derogation behavior,
/// voluntary unsocial hours, and hours-only entries.
/// Pure function, deterministic, no I/O.
/// </summary>
public class RestPeriodRuleTests
{
    private static readonly DateOnly Monday = new(2024, 4, 8);
    private static readonly DateOnly Sunday = Monday.AddDays(6);

    private static EmploymentProfile CreateProfile(string agreement = "AC", string okVersion = "OK24") => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = agreement,
        OkVersion = okVersion,
        WeeklyNormHours = 37.0m,
        EmploymentCategory = "STANDARD",
    };

    private static AgreementRuleConfig CreateConfig(
        bool derogation = false,
        decimal maxDaily = 13.0m,
        decimal minRest = 11.0m) => new()
    {
        AgreementCode = "AC",
        OkVersion = "OK24",
        WeeklyNormHours = 37.0m,
        HasOvertime = false,
        HasMerarbejde = true,
        MaxFlexBalance = 150.0m,
        FlexCarryoverMax = 150.0m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        MaxDailyHours = maxDaily,
        MinimumRestHours = minRest,
        RestPeriodDerogationAllowed = derogation,
        WeeklyMaxHoursReferencePeriod = 17,
        VoluntaryUnsocialHoursAllowed = true,
    };

    private static TimeEntry CreateTimedEntry(
        DateOnly date,
        decimal hours,
        TimeOnly startTime,
        TimeOnly endTime,
        bool voluntaryUnsocial = false) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        StartTime = startTime,
        EndTime = endTime,
        AgreementCode = "AC",
        OkVersion = "OK24",
        VoluntaryUnsocialHours = voluntaryUnsocial,
    };

    private static TimeEntry CreateHoursOnlyEntry(DateOnly date, decimal hours) => new()
    {
        EmployeeId = "EMP001",
        Date = date,
        Hours = hours,
        AgreementCode = "AC",
        OkVersion = "OK24",
    };

    // ---------------------------------------------------------------
    // 1. Daily rest: gap below minimum → VIOLATION
    // ---------------------------------------------------------------
    [Fact]
    public void DailyRest_BelowMinimum_ReturnsViolation()
    {
        // Day 1 ends at 22:00, Day 2 starts at 07:00 → rest = 9h (< 11h)
        var entries = new List<TimeEntry>
        {
            CreateTimedEntry(Monday, 8m, new TimeOnly(14, 0), new TimeOnly(22, 0)),
            CreateTimedEntry(Monday.AddDays(1), 8m, new TimeOnly(7, 0), new TimeOnly(15, 0)),
        };

        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, CreateConfig());

        Assert.False(result.Success);
        Assert.Equal(RestPeriodRule.RuleId, result.RuleId);
        Assert.Contains(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST &&
            v.Severity == ComplianceSeverity.VIOLATION);
    }

    // ---------------------------------------------------------------
    // 2. Daily rest: gap above minimum → no violation
    // ---------------------------------------------------------------
    [Fact]
    public void DailyRest_AboveMinimum_NoViolation()
    {
        // Day 1 ends at 17:00, Day 2 starts at 07:00 → rest = 14h (>= 11h)
        var entries = new List<TimeEntry>
        {
            CreateTimedEntry(Monday, 8m, new TimeOnly(9, 0), new TimeOnly(17, 0)),
            CreateTimedEntry(Monday.AddDays(1), 8m, new TimeOnly(7, 0), new TimeOnly(15, 0)),
        };

        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, CreateConfig());

        Assert.DoesNotContain(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST);
        Assert.DoesNotContain(result.Warnings, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST);
    }

    // ---------------------------------------------------------------
    // 3. Voluntary unsocial hours → skips rest check
    // ---------------------------------------------------------------
    [Fact]
    public void DailyRest_VoluntaryUnsocial_SkipsRestCheck()
    {
        // Day 1 ends at 23:00, Day 2 starts at 05:00 → rest = 6h
        // But Day 2 is voluntary unsocial → rest check skipped
        var entries = new List<TimeEntry>
        {
            CreateTimedEntry(Monday, 8m, new TimeOnly(15, 0), new TimeOnly(23, 0)),
            CreateTimedEntry(Monday.AddDays(1), 4m, new TimeOnly(5, 0), new TimeOnly(9, 0), voluntaryUnsocial: true),
        };

        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, CreateConfig());

        Assert.DoesNotContain(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST);
        Assert.DoesNotContain(result.Warnings, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST);
    }

    // ---------------------------------------------------------------
    // 4. Voluntary unsocial hours still count toward 48h ceiling
    // ---------------------------------------------------------------
    [Fact]
    public void VoluntaryUnsocial_StillCountsToward48hCeiling()
    {
        // 7 days × ~7.3h voluntary = 51.1h/week average > 48h
        var entries = new List<TimeEntry>();
        for (int i = 0; i < 7; i++)
        {
            entries.Add(new TimeEntry
            {
                EmployeeId = "EMP001",
                Date = Monday.AddDays(i),
                Hours = 7.3m,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(15, 18),
                AgreementCode = "AC",
                OkVersion = "OK24",
                VoluntaryUnsocialHours = true,
            });
        }

        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, CreateConfig());

        Assert.Contains(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.WEEKLY_MAX_HOURS &&
            v.Severity == ComplianceSeverity.VIOLATION);
    }

    // ---------------------------------------------------------------
    // 5. Weekly rest: all 7 days worked → VIOLATION
    // ---------------------------------------------------------------
    [Fact]
    public void WeeklyRest_AllSevenDaysWorked_ReturnsViolation()
    {
        var entries = new List<TimeEntry>();
        for (int i = 0; i < 7; i++)
        {
            entries.Add(CreateTimedEntry(
                Monday.AddDays(i), 7.4m,
                new TimeOnly(8, 0), new TimeOnly(15, 24)));
        }

        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, CreateConfig());

        Assert.Contains(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.WEEKLY_REST);
    }

    // ---------------------------------------------------------------
    // 6. Max daily hours: exceeds limit → VIOLATION
    // ---------------------------------------------------------------
    [Fact]
    public void MaxDailyHours_ExceedsLimit_ReturnsViolation()
    {
        // 14h in one day > 13h limit
        var entries = new List<TimeEntry>
        {
            CreateTimedEntry(Monday, 14m, new TimeOnly(6, 0), new TimeOnly(20, 0)),
        };

        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, CreateConfig());

        Assert.False(result.Success);
        Assert.Contains(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.MAX_DAILY_HOURS &&
            v.ActualValue == 14m &&
            v.ThresholdValue == 13m);
    }

    // ---------------------------------------------------------------
    // 7. Max daily hours: within limit → no violation
    // ---------------------------------------------------------------
    [Fact]
    public void MaxDailyHours_WithinLimit_NoViolation()
    {
        // 12h in one day <= 13h limit
        var entries = new List<TimeEntry>
        {
            CreateTimedEntry(Monday, 12m, new TimeOnly(7, 0), new TimeOnly(19, 0)),
        };

        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, CreateConfig());

        Assert.DoesNotContain(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.MAX_DAILY_HOURS);
    }

    // ---------------------------------------------------------------
    // 8. Weekly max hours: over 48h average → VIOLATION
    // ---------------------------------------------------------------
    [Fact]
    public void WeeklyMaxHours_Over48Average_ReturnsViolation()
    {
        // 5 days × 10h = 50h in 7-day period → 50h/week average > 48h
        var entries = new List<TimeEntry>();
        for (int i = 0; i < 5; i++)
        {
            entries.Add(CreateTimedEntry(
                Monday.AddDays(i), 10m,
                new TimeOnly(7, 0), new TimeOnly(17, 0)));
        }

        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, CreateConfig());

        Assert.Contains(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.WEEKLY_MAX_HOURS &&
            v.Severity == ComplianceSeverity.VIOLATION);
    }

    // ---------------------------------------------------------------
    // 9. Derogation allowed → rest breach is WARNING, not VIOLATION
    // ---------------------------------------------------------------
    [Fact]
    public void Derogation_Allowed_ReturnsWarning()
    {
        // Day 1 ends at 22:00, Day 2 starts at 07:00 → rest = 9h (< 11h)
        // derogation = true → WARNING
        var entries = new List<TimeEntry>
        {
            CreateTimedEntry(Monday, 8m, new TimeOnly(14, 0), new TimeOnly(22, 0)),
            CreateTimedEntry(Monday.AddDays(1), 8m, new TimeOnly(7, 0), new TimeOnly(15, 0)),
        };

        var config = CreateConfig(derogation: true);
        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, config);

        // Should be a WARNING, not a VIOLATION
        Assert.Contains(result.Warnings, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST &&
            v.Severity == ComplianceSeverity.WARNING);
        Assert.DoesNotContain(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST);
        // Success should be true since warnings don't affect it
        Assert.True(result.Success);
    }

    // ---------------------------------------------------------------
    // 10. Derogation not allowed → rest breach is VIOLATION
    // ---------------------------------------------------------------
    [Fact]
    public void Derogation_NotAllowed_ReturnsViolation()
    {
        // Same scenario as above but derogation = false → VIOLATION
        var entries = new List<TimeEntry>
        {
            CreateTimedEntry(Monday, 8m, new TimeOnly(14, 0), new TimeOnly(22, 0)),
            CreateTimedEntry(Monday.AddDays(1), 8m, new TimeOnly(7, 0), new TimeOnly(15, 0)),
        };

        var config = CreateConfig(derogation: false);
        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, config);

        Assert.Contains(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST &&
            v.Severity == ComplianceSeverity.VIOLATION);
        Assert.DoesNotContain(result.Warnings, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST);
        Assert.False(result.Success);
    }

    // ---------------------------------------------------------------
    // 11. Hours-only entries: skip rest check, still check daily max
    // ---------------------------------------------------------------
    [Fact]
    public void HoursOnly_SkipsRestCheck_StillChecksDailyMax()
    {
        // No StartTime/EndTime → rest gap check skipped, but daily max still enforced
        var entries = new List<TimeEntry>
        {
            CreateHoursOnlyEntry(Monday, 14m), // exceeds 13h daily max
            CreateHoursOnlyEntry(Monday.AddDays(1), 7m),
        };

        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, CreateConfig());

        // Daily max should fire
        Assert.Contains(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.MAX_DAILY_HOURS);
        // Daily rest should NOT fire (no time-of-day data)
        Assert.DoesNotContain(result.Violations, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST);
        Assert.DoesNotContain(result.Warnings, v =>
            v.ViolationType == ComplianceViolationType.DAILY_REST);
    }

    // ---------------------------------------------------------------
    // 12. Empty entries → success, no violations
    // ---------------------------------------------------------------
    [Fact]
    public void EmptyEntries_ReturnsSuccess()
    {
        var entries = new List<TimeEntry>();

        var result = RestPeriodRule.Evaluate(CreateProfile(), entries, Monday, Sunday, CreateConfig());

        Assert.True(result.Success);
        Assert.Equal(RestPeriodRule.RuleId, result.RuleId);
        Assert.Equal("EMP001", result.EmployeeId);
        Assert.Empty(result.Violations);
        Assert.Empty(result.Warnings);
    }

    // ---------------------------------------------------------------
    // 13. CentralAgreementConfigs: AC has derogation disabled
    // ---------------------------------------------------------------
    [Fact]
    public void AC_Config_HasDerogationDisabled()
    {
        var config = CentralAgreementConfigs.GetConfig("AC", "OK24");
        Assert.False(config.RestPeriodDerogationAllowed);
    }

    // ---------------------------------------------------------------
    // 14. CentralAgreementConfigs: HK has derogation enabled
    // ---------------------------------------------------------------
    [Fact]
    public void HK_Config_HasDerogationEnabled()
    {
        var config = CentralAgreementConfigs.GetConfig("HK", "OK24");
        Assert.True(config.RestPeriodDerogationAllowed);
    }
}
