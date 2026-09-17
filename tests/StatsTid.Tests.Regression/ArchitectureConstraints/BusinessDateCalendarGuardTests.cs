using System.Text.RegularExpressions;

namespace StatsTid.Tests.Regression.ArchitectureConstraints;

/// <summary>
/// S142 Step-7a WARNING 3 — the repo-wide guard that keeps all 64 converted business-date sites on
/// the Copenhagen calendar, rather than the ~20 that happen to have a behavioural pin.
///
/// <para>
/// <b>Why this exists.</b> S142 moved every business date from the UTC calendar day to the
/// Europe/Copenhagen day: every StatsTid user is Danish, and between Danish midnight and UTC
/// midnight the UTC calendar still reads YESTERDAY, so an HR user working at 00:30 recorded a change
/// as effective the day before. Twelve tasks converted 64 sites. **Roughly twenty of those rows have
/// no discriminating behavioural test** — not because anyone was careless, but because each would
/// need its own seeded fixture and container boot, and the per-task criterion was "at least one pin
/// that fails on the old code", which every task met.
/// </para>
///
/// <para>
/// The gap that leaves is narrow but real: <b>a single site reverted to the UTC day would break no
/// test.</b> The sprint-end review found the sprint log had declared three unpinned rows when about
/// twenty were unpinned — a record-accuracy problem on top of a durability one. A source-text guard
/// closes both at once, and costs one fast non-Docker test instead of twenty container boots.
/// </para>
///
/// <para>
/// <b>What this guard is NOT.</b> It cannot prove a site computes the right day — only that it does
/// not compute the retired one. Behavioural proof lives in the pinned boundary facts
/// (<see cref="StatsTid.Tests.Regression.Hosting.BoundaryInstants"/>). This is the net underneath
/// them, not a replacement: it catches a revert, a copy-paste of the old shape into a new endpoint,
/// and a well-meant "simplification" back to <c>DateTime.UtcNow</c>.
/// </para>
///
/// <para>
/// <b>Instants are deliberately untouched.</b> <c>created_at</c>, <c>updated_at</c>, audit
/// timestamps, outbox ordering and token expiry are moments in time, stay UTC, and must never be
/// routed through the Copenhagen helper — converting an instant to a calendar day and back corrupts
/// ordering. The patterns below therefore match only expressions that turn a clock reading into a
/// <b>calendar day</b>; a bare <c>GetUtcNow()</c> or <c>DateTime.UtcNow</c> assigned to a timestamp
/// is correct and is not matched.
/// </para>
/// </summary>
public sealed class BusinessDateCalendarGuardTests
{
    /// <summary>
    /// The retired shapes, each of which turns a clock reading into a CALENDAR DAY:
    /// <c>DateOnly.FromDateTime(... UtcNow ...)</c>, <c>....UtcDateTime.Date</c>,
    /// <c>DateTime.Today</c> (machine-local), and <c>GetLocalNow()</c>.
    /// </summary>
    private static readonly Regex RetiredDayDerivation = new(
        @"DateOnly\.FromDateTime\([^;)]*(?:UtcNow|GetUtcNow\(\)|DateTime\.Today|DateTime\.Now)"
        + @"|GetUtcNow\(\)\.UtcDateTime\.Date"
        + @"|DateTime\.UtcNow\.Date"
        + @"|\bDateTime\.Today\b"
        + @"|GetLocalNow\(\)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>The database deciding a day — owner ruling OQ-7 moved every one of these into the app.</summary>
    private static readonly Regex DatabaseDecidedDay = new(
        @"\bCURRENT_DATE\b|\bnow\(\)::date\b|\bLOCALTIMESTAMP\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void NoProductionSource_DerivesABusinessDay_FromTheUtcOrMachineClock()
    {
        var offenders = ScanCSharp(RetiredDayDerivation);

        Assert.True(
            offenders.Count == 0,
            "These files derive a calendar day from the UTC or machine clock, which S142 retired "
            + "(ADR-041). Every StatsTid user is Danish; between Danish midnight and UTC midnight "
            + "the UTC calendar still reads YESTERDAY, so a business date taken from it is a day "
            + "early. Use CopenhagenBusinessDate.Today(timeProvider).\n\n"
            + "If the value is genuinely an INSTANT (created_at, an audit stamp, outbox ordering, "
            + "token expiry) then it should not be going through DateOnly at all — keep the "
            + "DateTimeOffset and it will not match this guard.\n\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void NoProductionSql_LetsTheDatabaseDecideTheDay()
    {
        var offenders = ScanCSharp(DatabaseDecidedDay);

        Assert.True(
            offenders.Count == 0,
            "These files let the DATABASE SERVER decide a calendar day. That day is whatever zone "
            + "the Postgres container happens to run in — a value nobody sets, sees or can pin in a "
            + "test. Owner ruling OQ-7 moved every one of them into the application: bind an "
            + "application-supplied @today derived from CopenhagenBusinessDate.\n\n"
            + string.Join("\n", offenders));
    }

    // ── scanning ────────────────────────────────────────────────────────────────────────────────

    private static List<string> ScanCSharp(Regex pattern)
    {
        var root = LocateRepoRoot();
        var src = Path.Combine(root, "src");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            // Generated output and build artefacts are not authored source.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var executable = StripComments(File.ReadAllText(file));
            var match = pattern.Match(executable);
            if (!match.Success) continue;

            offenders.Add($"  {Path.GetRelativePath(root, file).Replace('\\', '/')} — `{Truncate(match.Value)}`");
        }

        return offenders;
    }

    /// <summary>
    /// Strips comments and string literals before matching.
    ///
    /// <para>Comments must go because this repository deliberately <i>quotes</i> the retired shapes
    /// when explaining why they were removed — S142 rewrote roughly forty such comments, and a guard
    /// that fired on its own explanation would push authors toward deleting the explanation. String
    /// literals go for the same reason: the SQL patterns above appear inside error messages and this
    /// very test's own text.</para>
    /// </summary>
    private static string StripComments(string source)
    {
        var withoutBlock = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        var withoutLine = Regex.Replace(withoutBlock, @"//[^\n]*", " ");
        var withoutRawStrings = Regex.Replace(withoutLine, "\"\"\".*?\"\"\"", " \"\" ", RegexOptions.Singleline);
        return Regex.Replace(withoutRawStrings, @"@?""(?:[^""\\\n]|\\.|"""")*""", " \"\" ");
    }

    private static string Truncate(string value) =>
        value.Length <= 90 ? value.Replace('\n', ' ') : value[..90].Replace('\n', ' ') + "…";

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StatsTid.sln")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
