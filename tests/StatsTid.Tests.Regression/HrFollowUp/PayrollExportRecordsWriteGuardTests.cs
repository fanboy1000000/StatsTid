using System.Text.RegularExpressions;

namespace StatsTid.Tests.Regression.HrFollowUp;

/// <summary>
/// S140 / TASK-14005 (refinement B3; HRP-022) — a plain source-tree scan proving the Backend
/// performs NO WRITE of any kind to <c>payroll_export_records</c>. That table is owned and
/// written SOLELY by the Payroll service (ADR-034 D4); the Backend's HRP-022
/// (approved-but-not-exported) read and the pre-existing backdate-worklist read are permitted to
/// SELECT it, never to mutate it.
///
/// <para>
/// <b>Not Docker-gated, deliberately.</b> This is a text scan over <c>src/Backend</c> and
/// <c>src/Infrastructure</c> — it touches no database and needs no container, so unlike the rest
/// of TASK-14005's pins it runs (and is reported) locally, in the <c>Category!=Docker</c> suite.
/// </para>
///
/// <para>
/// <b>RED conditions</b> (each independently provable by reverting the corresponding fix):
/// <list type="bullet">
///   <item><see cref="Backend_And_Infrastructure_NeverWriteTo_PayrollExportRecords"/> — pasting an
///   <c>INSERT INTO payroll_export_records</c> / <c>UPDATE payroll_export_records</c> /
///   <c>DELETE FROM payroll_export_records</c> statement anywhere under the two scanned trees
///   turns this red. (Whitespace between the verb and the table name is tolerated — a
///   multi-line SQL literal is exactly where a write would appear — so splitting the statement
///   across lines does not evade the scan.)</item>
///   <item><see cref="TheScan_ReachesRealFiles"/> — the companion "is this check vacuous"
///   pin (the <c>ToleranceAllowListTests</c> discipline): it asserts the table name is
///   mentioned in more than zero places across the two trees, so a scan rooted at the wrong
///   directory (which would make the write-guard pass by finding nothing at all) is caught
///   rather than silently green.</item>
/// </list>
/// </para>
/// </summary>
public sealed class PayrollExportRecordsWriteGuardTests
{
    private const string TableName = "payroll_export_records";

    /// <summary>
    /// Matches a write verb immediately followed (across any amount of whitespace, including
    /// newlines — SQL literals in this codebase are C# raw string literals spanning several
    /// lines) by the table name. Case-insensitive: the codebase's SQL keywords are upper-case,
    /// but the guard must not depend on that convention holding forever.
    /// </summary>
    private static readonly Regex WriteVerbPattern = new(
        @"\b(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+" + TableName + @"\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ScannedTrees = { "src/Backend", "src/Infrastructure" };

    [Fact]
    public void Backend_And_Infrastructure_NeverWriteTo_PayrollExportRecords()
    {
        var root = LocateRepoRoot();
        var offenders = new List<string>();

        foreach (var tree in ScannedTrees)
        {
            var treeRoot = Path.Combine(root, tree.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(treeRoot))
                continue; // proven unreachable by TheScan_ReachesRealFiles below if BOTH are missing

            foreach (var file in Directory.EnumerateFiles(treeRoot, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (WriteVerbPattern.IsMatch(text))
                    offenders.Add(Path.GetRelativePath(root, file));
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The companion "not vacuous" pin. RED if the scan were rooted somewhere with no C# files
    /// at all (e.g. a typo'd tree name), which would make the write-guard above pass by finding
    /// nothing to scan rather than by finding real, clean SELECT-only usage.
    /// </summary>
    [Fact]
    public void TheScan_ReachesRealFiles()
    {
        var root = LocateRepoRoot();
        var mentionCount = 0;

        foreach (var tree in ScannedTrees)
        {
            var treeRoot = Path.Combine(root, tree.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(treeRoot))
                continue;

            foreach (var file in Directory.EnumerateFiles(treeRoot, "*.cs", SearchOption.AllDirectories))
                mentionCount += Regex.Matches(File.ReadAllText(file), TableName).Count;
        }

        // As of S140 four files mention the table (HrFollowUpApprovalResponses.cs,
        // ApprovalEndpoints.cs, HrBackdateWorklistRepository.cs, HrFollowUpApprovalReadRepository.cs)
        // — a floor of 1 is enough to prove the scan is reaching real files without pinning an
        // exact count that would need updating on every unrelated future mention.
        Assert.True(mentionCount > 0,
            $"Expected at least one mention of '{TableName}' under {string.Join(", ", ScannedTrees)} " +
            "— zero means the scan is rooted somewhere it cannot see real files.");
    }

    /// <summary>
    /// Walks up from the test runtime base directory to the solution root, anchored on
    /// <c>StatsTid.sln</c> — NOT <c>docker/postgres/init.sql</c>
    /// (<c>StatsTidWebApplicationFactory.LocateInitSql</c>'s anchor): the test project copies
    /// <c>docker/postgres/init.sql</c> into its OWN build output as content, so that anchor matches
    /// the bin directory itself and would silently root this scan there — where <c>src/</c> does not
    /// exist — instead of at the real repository root. <c>StatsTid.sln</c> is never copied to any
    /// build output, so it uniquely identifies the true root.
    /// </summary>
    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "StatsTid.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not locate the solution root (StatsTid.sln not found walking up from " +
            AppContext.BaseDirectory + ").");
    }
}
