using System.Globalization;
using System.Text.Json;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Rules;

/// <summary>
/// S148 TASK-14813-T (AC-S0-4a, the engine half) - the shared rest-parity scenario table.
///
/// <para>
/// <b>Plain language.</b> The 11-hour rest rule lives in two places: the server's check (the
/// authority) and Skema's own instant warning. One tracked JSON file,
/// <c>tests/StatsTid.Tests.Unit/TestData/rest-parity-scenarios.json</c>, holds sixteen rest scenarios
/// with the verdict each must get. This fact checks the server's engine against every row; the
/// screen's vitest (<c>restParity.test.ts</c>) checks the screen against the same file, so the two
/// cannot drift apart without one of the tests turning red. The file is read in place by both
/// runners and never copied.
/// </para>
///
/// <para>
/// <b>Expected values come from the spec</b> (the refinement's S0-3 cases, the month-edge ruling
/// "a rest belongs to the period in which it ends", the 11-hour boundary), written into the file by
/// hand, never read back from the engine.
/// </para>
///
/// <para>
/// <b>Kind N</b> (names <see cref="ComplianceWorkInterval"/> and <c>Evaluate</c>'s interval parameter,
/// which this sprint creates). Proved red by named mutations (M-24 to M-26 at the wave gate; M-29 and M-30 at the fix-up gate), each run alone
/// in a detached worktree: <b>M-24</b> (midnight continuity dropped) turns red the rows
/// <c>midnight-halves-alone</c>, <c>midnight-halves-then-same-day-work</c>,
/// <c>midnight-halves-across-month-edge</c>, <c>continuation-day-without-other-work</c> and
/// <c>chain-over-two-midnights</c>; <b>M-25</b> (any kept interval makes a day analysed) turns red
/// <c>partly-clocked-day</c> (list <c>[2026-03-16]</c>, actual empty); <b>M-26</b> (the daily check
/// keeps the old overlap filter) turns red <c>lower-month-edge-short-rest</c> (31 Mar to 1 Apr, 9.0;
/// actual none). Four rows added by the Step 5a fix-up: <c>end-2358-does-not-join</c> (kind N, <b>M-29</b>: the
/// join end test loosened from 23:59 to 23:00 turns it red, expected one 0.0 finding, actual none),
/// <c>start-0001-does-not-join</c> (kind N, <b>M-30</b>: the join start test loosened to include 00:01),
/// <c>midpoint-rest-rounds-half-up</c> (kind F: with the half-up rounding reverted the value is 8.4, not 8.5) and
/// <c>overlapping-intervals-show-zero</c> (kind R: the clamp that shows a negative rest as 0.0 is kept).
/// A compile error is not RED.
/// </para>
///
/// <para>
/// Red conditions: for each row the set of <c>(Date, ActualValue)</c> over the DAILY_REST findings,
/// each finding's message naming its <c>to</c> day, its severity (WARNING exactly when the row allows
/// derogation) and <c>RestNotCheckedDays</c> must equal the row's values; a file that does not parse,
/// is not schema 1 or lacks an expected id, or holds an id that is not expected, fails the non-vacuity fact.
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
        "midpoint-rest-rounds-half-up",
        "overlapping-intervals-show-zero",
    };

    public static IEnumerable<object[]> RowIds() => ExpectedIds.Select(id => new object[] { id });

    [Fact]
    public void Table_IsParsed_SchemaOne_AndHoldsExactlyTheExpectedRows()
    {
        var root = ReadTable();

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Contains(
            "belongs to the period in which it ends; `from` is the day the earlier shift started",
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

        // Exactly as the Backend reads: intervals dated in [periodStart - 1, periodEnd] only.
        var readStart = periodStart.AddDays(-1);
        var intervals = row.GetProperty("intervals").EnumerateArray()
            .Select(iv => new ComplianceWorkInterval(
                Date(iv, "date"),
                TimeOnly.ParseExact(iv.GetProperty("start").GetString()!, "HH:mm", CultureInfo.InvariantCulture),
                TimeOnly.ParseExact(iv.GetProperty("end").GetString()!, "HH:mm", CultureInfo.InvariantCulture)))
            .Where(iv => iv.Date >= readStart && iv.Date <= periodEnd)
            .ToList();

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
            Profile(), entries, periodStart, periodEnd, Config(derogation), intervals);

        var findings = result.Violations.Concat(result.Warnings)
            .Where(f => f.ViolationType == ComplianceViolationType.DAILY_REST)
            .ToList();

        var expected = row.GetProperty("expectedDailyRest").EnumerateArray()
            .Select(r => (From: Date(r, "from"), To: Date(r, "to"), Gap: r.GetProperty("gapHours").GetDecimal()))
            .ToList();

        Assert.Equal(
            expected.Select(e => (e.From, e.Gap)).OrderBy(e => e.From),
            findings.Select(f => (f.Date, f.ActualValue)).OrderBy(e => e.Date));

        foreach (var e in expected)
        {
            var finding = findings.Single(f => f.Date == e.From);
            Assert.Contains(e.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), finding.Message);
            Assert.Equal(
                derogation ? ComplianceSeverity.WARNING : ComplianceSeverity.VIOLATION,
                finding.Severity);
        }

        var expectedNotChecked = row.GetProperty("expectedEngineRestNotCheckedDays").EnumerateArray()
            .Select(d => DateOnly.ParseExact(d.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToList();
        Assert.Equal(expectedNotChecked, result.RestNotCheckedDays);
    }

    private static DateOnly Date(JsonElement node, string name) =>
        DateOnly.ParseExact(node.GetProperty(name).GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

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
        FlexCarryoverMax = 150.0m,
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
