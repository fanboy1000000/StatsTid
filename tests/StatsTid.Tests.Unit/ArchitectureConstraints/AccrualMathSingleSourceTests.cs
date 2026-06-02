using System.Text.RegularExpressions;

namespace StatsTid.Tests.Unit.ArchitectureConstraints;

/// <summary>
/// S61 / TASK-6101 — single-source guard for the Danish monthly-accrual earned-to-date
/// formula (Ferieloven <em>samtidighedsferie</em>, ADR-030).
///
/// <para>
/// Before S61 the earned-to-date math was triplicated: the authoritative copy in the Rule
/// Engine's <c>AccrualCalculator</c> plus byte-identical Backend-local mirrors in the Balance
/// and Skema endpoints (the Backend may not reference the RuleEngine assembly — PAT-005 keeps
/// the validate-entitlement boundary HTTP-only). S61 consolidated the formula body into the
/// dependency-free SharedKernel leaf <c>StatsTid.SharedKernel.Calendar.AccrualMath</c>; every
/// caller now delegates and the math exists in exactly one place.
/// </para>
///
/// <para>
/// This test is the forward-looking drift-detection layer that keeps the formula from
/// re-triplicating. It uses the file-text source-scan idiom of
/// <see cref="PlannerBypassGuardTests"/> (walk to the repo root, enumerate <c>src/**/*.cs</c>,
/// regex over the source text), but tuned to ignore XML-doc comment lines so the formula
/// description that survives in the delegators' <c>&lt;summary&gt;</c> blocks is not mistaken
/// for an executable re-implementation.
/// </para>
///
/// <para><b>What "the formula" means here (two executable fingerprints):</b></para>
/// <list type="bullet">
///   <item><description>The cumulative-earning arithmetic
///   <c>... * monthsElapsed / 12m</c> — the literal <c>/ 12m</c> decimal divide that turns an
///   elapsed-month count into a fractional ferieår share. This is the load-bearing line of
///   <see cref="StatsTid.SharedKernel.Calendar.AccrualMath.EarnedToDate"/>.</description></item>
///   <item><description>An executable <c>MonthIndex(</c> method/declaration — the absolute
///   month-ordinal helper the formula uses to count elapsed months across year boundaries.
///   </description></item>
/// </list>
///
/// <para>
/// If a private <c>EarnedToDate</c>/<c>MonthIndex</c> mirror were reintroduced into
/// <c>AccrualCalculator</c> or any Backend endpoint, that file's executable text would contain
/// <c>/ 12m</c> and/or an executable <c>MonthIndex(</c>, the per-fingerprint count would exceed
/// one, and the corresponding assertion below would fail naming the offending file(s).
/// </para>
/// </summary>
public class AccrualMathSingleSourceTests
{
    /// <summary>The single file (repo-relative, forward-slash) that may host the formula body.</summary>
    private const string CanonicalRelativePath =
        "src/SharedKernel/StatsTid.SharedKernel/Calendar/AccrualMath.cs";

    /// <summary>
    /// The cumulative-earning divide. <c>/ 12m</c> (decimal literal divide by 12) is specific to
    /// the accrual share calc — it does not appear incidentally elsewhere in the formula body.
    /// Matched against comment-stripped source so the <c>&lt;c&gt;… / 12&lt;/c&gt;</c> doc text
    /// (which has no <c>m</c> suffix anyway) cannot trip it.
    /// </summary>
    private static readonly Regex EarnedDividePattern =
        new(@"/\s*12m\b", RegexOptions.Compiled);

    /// <summary>
    /// An EXECUTABLE <c>MonthIndex(</c> — a call or declaration, i.e. the identifier immediately
    /// followed by an open paren. The Backend endpoints mention the word "MonthIndex" only in a
    /// prose comment ("the Backend-local EarnedToDate/MonthIndex mirror was removed"), which is
    /// stripped before matching and also lacks the trailing paren.
    /// </summary>
    private static readonly Regex MonthIndexCallPattern =
        new(@"\bMonthIndex\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// The executable earned-to-date arithmetic (the <c>/ 12m</c> ferieår-share divide) appears
    /// in exactly one file: <see cref="CanonicalRelativePath"/>. Any other <c>src/**/*.cs</c>
    /// hit means the formula was copied back out of the SharedKernel — a re-triplication that
    /// breaks the S61 single-source invariant.
    /// </summary>
    [Fact]
    public void EarnedToDateArithmetic_LivesOnlyInAccrualMath()
    {
        var hits = ScanSrcForExecutablePattern(EarnedDividePattern);

        Assert.True(
            hits.Count == 1 && AreEqualPath(hits[0], CanonicalRelativePath),
            BuildFailureMessage(
                "the earned-to-date arithmetic ('... * monthsElapsed / 12m')", hits));
    }

    /// <summary>
    /// An executable <c>MonthIndex(</c> (call or declaration) appears in exactly one file:
    /// <see cref="CanonicalRelativePath"/>. Reintroducing a Backend/RuleEngine-local
    /// <c>MonthIndex</c> helper to power a re-declared <c>EarnedToDate</c> would add a second hit
    /// and fail this assertion.
    /// </summary>
    [Fact]
    public void ExecutableMonthIndex_LivesOnlyInAccrualMath()
    {
        var hits = ScanSrcForExecutablePattern(MonthIndexCallPattern);

        Assert.True(
            hits.Count == 1 && AreEqualPath(hits[0], CanonicalRelativePath),
            BuildFailureMessage("an executable MonthIndex(...)", hits));
    }

    /// <summary>
    /// The delegators carry NONE of the executable formula. <c>AccrualCalculator</c> (RuleEngine)
    /// and both Backend endpoints (Balance + Skema) must only DELEGATE — their executable text may
    /// not contain the <c>/ 12m</c> divide nor an executable <c>MonthIndex(</c>. This is the
    /// direct anti-property: it fails the instant a private <c>EarnedToDate</c> body is pasted
    /// back into any of these former mirror sites, even if some unrelated future file legitimately
    /// hosts a second copy (which the per-fingerprint counts above would catch separately).
    /// </summary>
    [Theory]
    [InlineData("src/RuleEngine/StatsTid.RuleEngine.Api/Rules/AccrualCalculator.cs")]
    [InlineData("src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs")]
    [InlineData("src/Backend/StatsTid.Backend.Api/Endpoints/SkemaEndpoints.cs")]
    public void FormerMirrorSites_OnlyDelegate_NoFormulaBody(string relativePath)
    {
        var repoRoot = LocateRepoRoot();
        var absolute = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(absolute),
            $"Expected former-mirror file '{relativePath}' to exist; if it moved, update this guard.");

        var executableText = StripComments(File.ReadAllText(absolute));

        Assert.False(EarnedDividePattern.IsMatch(executableText),
            $"'{relativePath}' contains the earned-to-date arithmetic ('/ 12m'). After S61/TASK-6101 " +
            "this file must DELEGATE to StatsTid.SharedKernel.Calendar.AccrualMath, not re-declare the " +
            "formula. Remove the local copy and call AccrualMath.EarnedToDate.");
        Assert.False(MonthIndexCallPattern.IsMatch(executableText),
            $"'{relativePath}' contains an executable MonthIndex(...). The month-ordinal helper lives " +
            "only inside AccrualMath; this file must not re-introduce it.");
    }

    /// <summary>
    /// Sanity guard against the single-source assertions silently passing because the canonical
    /// file lost the formula (e.g. an accidental gut of AccrualMath). If AccrualMath stops being
    /// the home of BOTH fingerprints, fail loudly so the count-of-one tests above can't pass
    /// vacuously (zero hits everywhere).
    /// </summary>
    [Fact]
    public void AccrualMath_StillHostsTheFormula()
    {
        var repoRoot = LocateRepoRoot();
        var absolute = Path.Combine(repoRoot, CanonicalRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(absolute),
            $"Canonical formula home '{CanonicalRelativePath}' not found. Update CanonicalRelativePath.");

        var executableText = StripComments(File.ReadAllText(absolute));
        Assert.True(EarnedDividePattern.IsMatch(executableText),
            "AccrualMath.cs no longer contains the '/ 12m' earned-to-date arithmetic — the single " +
            "source of truth was gutted. The single-source guards would pass vacuously; restore the formula.");
        Assert.True(MonthIndexCallPattern.IsMatch(executableText),
            "AccrualMath.cs no longer contains an executable MonthIndex(...) — restore the month-ordinal helper.");
    }

    // ── helpers ──

    /// <summary>
    /// Enumerates <c>src/**/*.cs</c> (skipping <c>obj</c>/<c>bin</c>), strips comment lines, and
    /// returns the repo-relative (forward-slash) paths whose EXECUTABLE text matches
    /// <paramref name="pattern"/>.
    /// </summary>
    private static List<string> ScanSrcForExecutablePattern(Regex pattern)
    {
        var repoRoot = LocateRepoRoot();
        var srcDir = Path.Combine(repoRoot, "src");
        Assert.True(Directory.Exists(srcDir), $"src directory not found under repo root '{repoRoot}'.");

        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories))
        {
            if (IsGeneratedPath(file)) continue;

            var executableText = StripComments(File.ReadAllText(file));
            if (!pattern.IsMatch(executableText)) continue;

            hits.Add(Path.GetRelativePath(repoRoot, file).Replace('\\', '/'));
        }
        return hits;
    }

    /// <summary>True for build-output paths (obj/bin) that may shadow-copy source.</summary>
    private static bool IsGeneratedPath(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    /// <summary>
    /// Removes C# comments so the formula's prose DESCRIPTION (which appears verbatim in the
    /// delegators' XML-doc <c>&lt;summary&gt;</c>) is not scanned as executable code. Strips
    /// block comments (<c>/* … */</c>, including <c>/// </c>-free doc blocks) then any line whose
    /// first non-whitespace is <c>//</c> or <c>///</c>. Conservative: it does not attempt to parse
    /// string literals (the accrual fingerprints never appear inside a string literal in this
    /// codebase), keeping the stripper simple and deterministic.
    /// </summary>
    private static string StripComments(string source)
    {
        // 1) Block comments (covers /* */ and any /** */ doc blocks).
        var noBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        // 2) Whole-line // and /// comments (the form AccrualCalculator's formula doc uses).
        var kept = noBlocks
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));

        return string.Join('\n', kept);
    }

    private static bool AreEqualPath(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string BuildFailureMessage(string what, IReadOnlyList<string> hits)
    {
        var found = hits.Count == 0
            ? "(no file matched — the formula may have been removed entirely)"
            : string.Join(Environment.NewLine, hits.Select(h => $"  - {h}"));
        return
            $"S61/TASK-6101 single-source invariant: {what} must exist in EXACTLY one file " +
            $"('{CanonicalRelativePath}'). Found {hits.Count} occurrence(s) under src/:{Environment.NewLine}{found}" +
            $"{Environment.NewLine}If a former mirror (AccrualCalculator / Balance / Skema endpoints) re-declared the " +
            "formula, delete the local copy and delegate to AccrualMath.EarnedToDate. If a NEW legitimate home is " +
            "intended, that requires Orchestrator approval + a KB/ADR update, then update CanonicalRelativePath here.";
    }

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
