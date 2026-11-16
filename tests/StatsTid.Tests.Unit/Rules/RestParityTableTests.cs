using System.Globalization;
using System.Text.Json;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S148 TASK-14813-T / TASK-14843-T (AC-S0-4a, AC-S0-3g; the engine half) - the shared rest-parity
/// scenario table, schema 2.
///
/// <para>
/// <b>Plain language.</b> The 11-hour rest rule lives in two places: the server's check (the
/// authority) and Skema's own instant warning. One tracked JSON file,
/// <c>tests/StatsTid.Tests.Unit/TestData/rest-parity-scenarios.json</c>, holds 33 rest scenarios
/// with the verdict each must get: for every piece of work that ends, the longest unbroken rest in the
/// 24 elapsed hours (Danish time) before it, dated the day the work ends, one finding per day, shown
/// rounded down to one decimal. This fact checks the server's engine against every row; the screen's
/// vitest (<c>restParity.test.ts</c>) checks the screen against the same file, so the two cannot
/// drift apart without one of the tests turning red. The file is read in place by both runners and
/// never copied.
/// </para>
///
/// <para>
/// <b>Expected values come from the spec</b> (research adjudication RR-1, RR-2 and RR-7, the
/// wave-3b brief adjudication's items 6, 9 and 10), worked by hand into the file, never read back from
/// the engine. Each item is <c>{ date, restHours }</c>; the engine-only row fields are
/// <c>derogationAllowed</c>, <c>projectHours</c>, <c>expectedEngineWarningDates</c> and
/// <c>expectedEngineRestNotCheckedDays</c>.
/// </para>
///
/// <para>
/// <b>Kind N</b> (names <see cref="ComplianceWorkInterval"/> and <c>Evaluate</c>'s interval and zone
/// parameters, which this sprint creates). Proved red by named mutations, each run alone in a detached
/// worktree at the 3b gate (the report names the rows each turns red): M-24 (midnight continuity
/// dropped), M-25 (any kept interval makes a day analysed), M-26 (work before the period is left out of
/// the windows), M-27 (work dated after the period is no longer trimmed from the daily check, so the
/// upper-edge rows are judged as the next month sees them), M-29 and M-30 (the join's end or start test loosened), M-31 (the 8-hour floor compared
/// strictly). A compile error is not RED.
/// </para>
///
/// <para>
/// Red conditions: for each row the set of <c>(Date, ActualValue)</c> over the DAILY_REST findings,
/// each finding's message naming its own date, its severity (WARNING exactly for the dates in
/// <c>expectedEngineWarningDates</c>) and <c>RestNotCheckedDays</c> must equal the row's values; a file
/// that does not parse, is not schema 2 or lacks an expected id, or holds an id that is not expected,
/// fails the non-vacuity fact.
/// </para>
/// </summary>
public class RestParityTableTests
{
    private static readonly string[] ExpectedIds =
    {
        "interior-short-rest",
        "interior-short-rest-derogation",
        "midnight-halves-alone",
        "midnight-halves-then-same-day-work",
        "exactly-eleven-hours",
        "partly-clocked-day",
        "lower-month-edge-short-rest",
        "upper-month-edge-belongs-to-next-month",
        "midnight-halves-across-month-edge",
        "reversed-interval-ignored",
        "continuation-day-without-other-work",
        "chain-over-two-midnights",
        "end-2358-does-not-join",
        "start-0001-does-not-join",
        "rest-shown-is-rounded-down",
        "overlapping-intervals-are-one-work-block",
        "split-day-alone",
        "split-day-then-night-shift",
        "twenty-four-hour-shift",
        "exactly-thirteen-hours",
        "thirteen-hours-one-minute",
        "spring-forward-night-is-an-hour-shorter",
        "fall-back-night-is-an-hour-longer",
        "two-days-back-across-a-23-hour-day",
        "derogation-floor-eight-hours-is-a-warning",
        "derogation-below-floor-is-a-violation",
        "end-2359-is-the-end-of-the-day",
        "seconds-rest-rounds-once",
        "spring-forward-typed-0230-is-the-changeover",
        "fall-back-typed-0230-end-is-the-later-instant",
        "fall-back-typed-0230-start-is-the-earlier-instant",
        "night-shift-over-the-upper-month-edge",
        "spring-forward-work-inside-the-missing-hour-is-still-judged",
    };

    public static IEnumerable<object[]> RowIds() => ExpectedIds.Select(id => new object[] { id });

    [Fact]
    public void Table_IsParsed_SchemaTwo_AndHoldsExactlyTheExpectedRows()
    {
        var root = ReadTable();

        Assert.Equal(2, root.GetProperty("schemaVersion").GetInt32());
        Assert.Contains(
            "dated the day its work ends",
            root.GetProperty("about").GetString());

        var ids = root.GetProperty("scenarios").EnumerateArray()
            .Select(s => s.GetProperty("id").GetString()!)
            .ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ExpectedIds.OrderBy(i => i), ids.OrderBy(i => i));
    }

    [Theory]
    [MemberData(nameof(RowIds))]
    public void SharedScenarioTable_EngineVerdicts(string id)
    {
        var row = ReadTable().GetProperty("scenarios").EnumerateArray()
            .Single(s => s.GetProperty("id").GetString() == id);

        var periodStart = Date(row, "periodStart");
        var periodEnd = Date(row, "periodEnd");
        var derogation = row.GetProperty("derogationAllowed").GetBoolean();

        // Every interval of the row goes to the engine untrimmed: the engine itself ignores work dated
        // after the period in the daily check (M-27). The compliance read sends [periodStart - 2, periodEnd];
        // the rows also hold the day after (as the Skema screen is fed), which the range assertion below allows.
        var readStart = periodStart.AddDays(-2);
        var intervals = row.GetProperty("intervals").EnumerateArray()
            .Select(iv => new ComplianceWorkInterval(
                Date(iv, "date"),
                Time(iv, "start"),
                Time(iv, "end")))
            .ToList();
        Assert.All(intervals, iv => Assert.InRange(iv.Date, readStart, periodEnd.AddDays(1)));
        if (id is "night-shift-over-the-upper-month-edge" or "upper-month-edge-belongs-to-next-month")
            Assert.Contains(intervals, iv => iv.Date > periodEnd);

        var entries = row.GetProperty("projectHours").EnumerateArray()
            .Select(p => new TimeEntry
            {
                EmployeeId = "EMP001",
                Date = Date(p, "date"),
                Hours = p.GetProperty("hours").GetDecimal(),
                AgreementCode = "AC",
                OkVersion = "OK24",
            })
            .ToList();

        var result = RestPeriodRule.Evaluate(
            Profile(), entries, periodStart, periodEnd, Config(derogation), intervals, StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Zone);

        var findings = result.Violations.Concat(result.Warnings)
            .Where(f => f.ViolationType == ComplianceViolationType.DAILY_REST)
            .ToList();

        var expected = row.GetProperty("expectedDailyRest").EnumerateArray()
            .Select(r => (Date: Date(r, "date"), Hours: r.GetProperty("restHours").GetDecimal()))
            .ToList();
        var expectedWarnings = row.GetProperty("expectedEngineWarningDates").EnumerateArray()
            .Select(d => DateOnly.ParseExact(d.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToList();

        // S148 3b (RR-1): the finding is dated the day its work ends and carries the longest unbroken rest.
        Assert.Equal(
            expected.OrderBy(e => e.Date),
            findings.Select(f => (f.Date, f.ActualValue)).OrderBy(e => e.Date));

        foreach (var e in expected)
        {
            var finding = findings.Single(f => f.Date == e.Date);
            Assert.Contains(e.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), finding.Message);
            // S148 3b (RR-2): a WARNING only where the row names the date (derogation and a rest of 8.0 or more).
            Assert.Equal(
                expectedWarnings.Contains(e.Date) ? ComplianceSeverity.WARNING : ComplianceSeverity.VIOLATION,
                finding.Severity);
        }

        var expectedNotChecked = row.GetProperty("expectedEngineRestNotCheckedDays").EnumerateArray()
            .Select(d => DateOnly.ParseExact(d.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(expectedNotChecked, result.RestNotCheckedDays);
    }

    private static DateOnly Date(JsonElement node, string name) =>
        DateOnly.ParseExact(node.GetProperty(name).GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"HH:mm" or "HH:mm:ss".</summary>
    private static TimeOnly Time(JsonElement node, string name) =>
        TimeOnly.ParseExact(node.GetProperty(name).GetString()!, new[] { "HH:mm", "HH:mm:ss" }, CultureInfo.InvariantCulture);

    private static JsonElement ReadTable()
    {
        var path = Path.Combine(LocateRepoRoot(), "tests", "StatsTid.Tests.Unit", "TestData", "rest-parity-scenarios.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static EmploymentProfile Profile() => new()
    {
        EmployeeId = "EMP001",
        AgreementCode = "AC",
        OkVersion = "OK24",
        EmploymentCategory = "STANDARD",
    };

    private static AgreementRuleConfig Config(bool derogation) => new()
    {
        AgreementCode = "AC",
        OkVersion = "OK24",
        WeeklyNormHours = 37.0m,
        HasOvertime = false,
        HasMerarbejde = true,
        MaxFlexBalance = 150.0m,
        EveningSupplementEnabled = false,
        NightSupplementEnabled = false,
        WeekendSupplementEnabled = false,
        HolidaySupplementEnabled = false,
        MaxDailyHours = 13.0m,
        MinimumRestHours = 11.0m,
        RestPeriodDerogationAllowed = derogation,
        WeeklyMaxHoursReferencePeriod = 17,
        VoluntaryUnsocialHoursAllowed = true,
    };

    /// <summary>Walk up from the test bin output to the repo root (the directory holding a .sln).</summary>
    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate repository root (directory containing *.sln) from test bin output. " +
            $"Searched upward from: {AppContext.BaseDirectory}");
    }
}
