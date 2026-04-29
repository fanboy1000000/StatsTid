namespace StatsTid.Tests.Unit.ArchitectureConstraints;

/// <summary>
/// CV rule TASK-2002 — Planner-bypass guard.
///
/// <para>
/// PAT-005 mandates that all rule-engine invocations from the Payroll integration
/// go via <c>PeriodCalculationService</c> (HTTP POST to <c>/api/rules/evaluate</c>),
/// and that no code under <c>src/Integrations/</c> may post directly to that
/// endpoint except the one allowlisted call site.
/// </para>
///
/// <para>
/// This test implements the forward-looking drift-detection layer described in
/// ADR-016 §D9 (Sprint 20). The rule is anchored on file path rather than method
/// signature so it holds for both the current and the planned TASK-2008 signature
/// of <c>PeriodCalculationService.CalculateAsync</c>.
/// </para>
///
/// <para>
/// Allowlisted call site:
/// <c>src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs</c>
/// </para>
///
/// <para>
/// Detection mechanism: file-text regex scan (same technique used by
/// <c>AuthorizationPolicyWiringTests</c> and the pre-existing CV checks).
/// Pattern: any line that contains a string literal matching <c>/api/rules/evaluate</c>
/// (exact path, not sub-paths like evaluate-absence or evaluate-flex) in combination
/// with a POST verb (<c>PostAsJsonAsync</c> / <c>PostAsync</c> appearing within the
/// same file). This matches how <c>PeriodCalculationService.cs</c> calls the endpoint
/// today and catches future copy-paste bypasses.
/// </para>
/// </summary>
public class PlannerBypassGuardTests
{
    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------

    /// <summary>
    /// The sole file (relative path from the repo root) that is allowed to
    /// POST to <c>/api/rules/evaluate</c> from within <c>src/Integrations/</c>.
    ///
    /// File-level allowlist (not method-level) — intentional per TASK-2002 scope
    /// note: TASK-2008 will change the method signature but the file stays the same.
    /// </summary>
    private const string AllowlistedRelativePath =
        "src/Integrations/StatsTid.Integrations.Payroll/Services/PeriodCalculationService.cs";

    // -----------------------------------------------------------------------
    // Regex patterns
    // -----------------------------------------------------------------------

    /// <summary>
    /// Matches the guarded endpoint path appearing in source text.
    /// Anchored to the exact path segment — not evaluate-absence or evaluate-flex.
    /// The lookahead <c>(?![/-])</c> prevents matching sub-paths like
    /// <c>evaluate-absence</c> or <c>evaluate-flex</c> or a deeper URL segment.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex EvaluatePathPattern =
        new(
            @"/api/rules/evaluate(?![/\-\w])",
            System.Text.RegularExpressions.RegexOptions.Compiled |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Matches HTTP POST calls (the two method forms used by the codebase).
    /// Both variants appear in <c>PeriodCalculationService</c> today (PostAsJsonAsync).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex HttpPostPattern =
        new(
            @"\bPostAsJsonAsync\b|\bPostAsync\b",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// No file under <c>src/Integrations/</c> may post to <c>/api/rules/evaluate</c>
    /// unless it is the allowlisted <c>PeriodCalculationService.cs</c>.
    ///
    /// A violation means some integration-layer code is bypassing
    /// <c>PeriodCalculationService</c> and calling the rule engine directly —
    /// which breaks the PAT-005 architectural guarantee and circumvents any
    /// temporal-segmentation logic the planner will introduce in S20.
    /// </summary>
    [Fact]
    public void NoIntegrationFile_OtherThanAllowlist_PostsToApiRulesEvaluate()
    {
        var repoRoot = LocateRepoRoot();
        var integrationsDir = Path.Combine(repoRoot, "src", "Integrations");

        Assert.True(Directory.Exists(integrationsDir),
            $"src/Integrations directory not found under repo root '{repoRoot}'. " +
            "If the directory was renamed, update AllowlistedRelativePath and this check.");

        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(integrationsDir, "*.cs", SearchOption.AllDirectories))
        {
            // Skip generated artifacts that may end up under obj/.
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                continue;

            var text = File.ReadAllText(file);

            // A file is a candidate only if it contains both:
            //   (a) a string literal with the guarded path, AND
            //   (b) an HTTP POST call.
            // Both conditions must hold to flag the file, which avoids false
            // positives on comments, test fixtures, or config files that
            // merely reference the path as a constant without calling it.
            if (!EvaluatePathPattern.IsMatch(text)) continue;
            if (!HttpPostPattern.IsMatch(text)) continue;

            // Normalise to forward slashes for cross-platform comparison.
            var relativePath = Path.GetRelativePath(repoRoot, file)
                .Replace('\\', '/');

            if (string.Equals(relativePath, AllowlistedRelativePath, StringComparison.OrdinalIgnoreCase))
                continue; // Expected — this is the canonical call site.

            violations.Add(relativePath);
        }

        Assert.True(violations.Count == 0,
            $"The following file(s) under src/Integrations/ post directly to /api/rules/evaluate " +
            $"without going through PeriodCalculationService (PAT-005 / planner-bypass guard TASK-2002). " +
            $"Either route the call through PeriodCalculationService, or add the file to the allowlist " +
            $"with explicit Orchestrator approval and a KB entry:{Environment.NewLine}" +
            string.Join(Environment.NewLine, violations.Select(v => $"  - {v}")));
    }

    /// <summary>
    /// Sanity guard: confirms the allowlisted file still exists and still
    /// contains a POST to <c>/api/rules/evaluate</c>. If the legitimate call
    /// site is removed or renamed, this test fails loudly so the allowlist
    /// entry does not become a stale no-op that silently passes the guard.
    /// </summary>
    [Fact]
    public void AllowlistedFile_StillContainsTheGuardedCall()
    {
        var repoRoot = LocateRepoRoot();
        var allowlistedPath = Path.Combine(repoRoot,
            AllowlistedRelativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(allowlistedPath),
            $"The allowlisted file '{AllowlistedRelativePath}' no longer exists. " +
            "Update the allowlist in PlannerBypassGuardTests to reflect the new location.");

        var text = File.ReadAllText(allowlistedPath);

        Assert.True(EvaluatePathPattern.IsMatch(text) && HttpPostPattern.IsMatch(text),
            $"The allowlisted file '{AllowlistedRelativePath}' no longer contains a POST " +
            "to /api/rules/evaluate. If the call was intentionally removed, remove the allowlist " +
            "entry from PlannerBypassGuardTests and confirm PAT-005 is still satisfied.");
    }

    // -----------------------------------------------------------------------
    // Helper
    // -----------------------------------------------------------------------

    private static string LocateRepoRoot()
    {
        // Walk up from the test bin output directory until we find a .sln file.
        // This is the same technique used by AuthorizationPolicyWiringTests
        // (LocateRepoSrcDirectory) — robust against shadow-copy layouts.
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
