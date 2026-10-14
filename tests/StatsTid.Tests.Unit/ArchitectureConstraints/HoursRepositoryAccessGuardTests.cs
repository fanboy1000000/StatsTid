using System.Text.RegularExpressions;

namespace StatsTid.Tests.Unit.ArchitectureConstraints;

/// <summary>
/// S145 / TASK-14514 (N3, ruling R25) — the read-only cross-context contract on the two hours projections, held by
/// CI build over build.
///
/// <para>
/// <b>Plain language.</b> From S145 payroll reads the employee's recorded hours itself, straight from the Backend's
/// <c>time_entries_projection</c> and <c>absences_projection</c> tables, through the two Infrastructure
/// repositories (<c>TimeEntryProjectionRepository</c>, <c>AbsenceProjectionRepository</c>). That crosses a bounded
/// context, so ruling R25 limits it to ONE type, <c>ProjectionHoursReader</c>, which only reads and which the host
/// registers through its own <c>AddProjectionHoursReader</c> extension (R58). If an endpoint or any other
/// integration class named a projection repository directly, nothing would stop it from reading (or one day
/// writing) the Backend's tables, and the contract would be a promise instead of a check. This guard makes it a
/// check: under <c>src/Integrations/**</c>, only <c>ProjectionHoursReader.cs</c> may name either repository.
/// </para>
///
/// <para>
/// <b>Same shape as</b> <see cref="PlannerBypassGuardTests"/>: a file-text scan with a file-level allowlist (the
/// allowlist stays this ONE file because the registration lives in it, so the host's <c>Program.cs</c> never has to
/// be allow-listed, which would also let an endpoint read the repositories).
/// </para>
///
/// <para>
/// <b>Red conditions</b>: RED by INJECTION (MU-39), not by reverting: in a throwaway checkout, add a
/// <c>TimeEntryProjectionRepository</c> reference to a non-allowlisted Payroll file (for example a field in
/// <c>PeriodCalculationService.cs</c>). The guard then lists that file. On the tree before TASK-14508 no
/// Integrations file names either repository, so the guard passes today (a guard, exempt from the "every
/// non-guard pin is red on master" rule).
/// </para>
/// </summary>
public class HoursRepositoryAccessGuardTests
{
    /// <summary>The one file allowed to name the projection repositories under <c>src/Integrations/</c>.</summary>
    private const string AllowlistedFileName = "ProjectionHoursReader.cs";

    /// <summary>The project the allowlisted file must live in (R25: payroll-local).</summary>
    private const string AllowlistedProjectDir = "src/Integrations/StatsTid.Integrations.Payroll/";

    private static readonly Regex RepositoryNamePattern =
        new(@"\b(TimeEntryProjectionRepository|AbsenceProjectionRepository)\b", RegexOptions.Compiled);

    [Fact]
    public void OnlyProjectionHoursReader_NamesTheProjectionRepositories_UnderIntegrations()
    {
        var repoRoot = LocateRepoRoot();
        var integrationsDir = Path.Combine(repoRoot, "src", "Integrations");

        Assert.True(Directory.Exists(integrationsDir),
            $"src/Integrations directory not found under repo root '{repoRoot}'. " +
            "If the directory was renamed, update AllowlistedProjectDir and this check.");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(integrationsDir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;

            if (!RepositoryNamePattern.IsMatch(File.ReadAllText(file)))
                continue;

            var relativePath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

            var isAllowlisted =
                string.Equals(Path.GetFileName(file), AllowlistedFileName, StringComparison.Ordinal) &&
                relativePath.StartsWith(AllowlistedProjectDir, StringComparison.OrdinalIgnoreCase);
            if (isAllowlisted)
                continue; // Expected: the one reader (R25), which also registers the repositories (R58).

            violations.Add(relativePath);
        }

        Assert.True(violations.Count == 0,
            $"The following file(s) under src/Integrations/ name TimeEntryProjectionRepository or " +
            $"AbsenceProjectionRepository outside ProjectionHoursReader.cs. Payroll's access to the Backend's hours " +
            $"projections is one read-only type (S145 R25 / N3); route the read through IRecordedHoursReader, or " +
            $"add the file to the allowlist with explicit Orchestrator approval and an ADR-016 amendment:" +
            $"{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations.Select(v => $"  - {v}")));
    }

    private static string LocateRepoRoot()
    {
        // Walk up from the test bin output directory until we find a .sln file (the PlannerBypassGuardTests technique).
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
