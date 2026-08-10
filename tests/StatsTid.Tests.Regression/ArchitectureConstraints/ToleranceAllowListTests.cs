using System.Globalization;
using System.Text.RegularExpressions;
using StatsTid.Backend.Api;

namespace StatsTid.Tests.Regression.ArchitectureConstraints;

/// <summary>
/// S127 / TASK-12705 · AC-1 — <b>no unmandated copy of the allocation tolerance.</b>
///
/// <para>Refinement §3.8 counted FIVE hand-written encodings of "is this day's worked total equal to
/// its allocated total". TASK-12705 collapsed the three backend ones into
/// <see cref="AllocationBalance"/>. This guard is what stops a sixth appearing: it scans the
/// code-bearing files of the whole repository for the tolerance — by NAME
/// (<c>AllocationTolerance</c>, <c>ALLOCATION_TOLERANCE</c>) and by VALUE (a bare <c>0.005</c>) — and
/// fails on any occurrence outside an explicit, argued allow-list.</para>
///
/// <para><b>A reference to the shared constant is not a copy and is not matched.</b> The consolidated
/// declaration is <c>AllocationBalance.Tolerance</c>; call sites that use it spell it that way and
/// this scan never sees them. What the scan looks for is the OLD private-constant name and the raw
/// number — i.e. exactly the two forms a re-introduced copy takes.</para>
///
/// <para><b>Why the allow-list is per FILE with a written reason.</b> An allow-list of bare paths rots
/// silently: entries outlive what justified them, and nobody can tell an argued exemption from a
/// forgotten one. Each entry below carries the argument for its own existence, and
/// <see cref="EveryAllowListEntry_IsLive"/> deletes the other failure mode by failing when an entry no
/// longer matches anything — a stale exemption is a hole, not a harmless leftover.</para>
///
/// <para><b>The three ways a check like this is worthless, and what is done about each:</b></para>
/// <list type="number">
///   <item><b>It never fires.</b> A scan rooted at the wrong directory, or a regex that cannot match
///     its target, passes forever. <see cref="TheScan_ReachesTheTreeItClaimsTo"/> asserts the roots
///     resolved to real files (per-root floors, not one aggregate) and that the pattern matched a
///     non-trivial number of files BEFORE the allow-list was applied.</item>
///   <item><b>It passes because the thing it guards was deleted.</b>
///     <see cref="SharedPredicate_StillDeclaresTheTolerance"/> asserts the canonical declaration is
///     still there, so "no copies" cannot come to mean "no original either".</item>
///   <item><b>Comment-stripping quietly eats real code.</b> The strip is what keeps ~10 prose mentions
///     of the tolerance from false-failing a correct tree, which makes it the most dangerous part of
///     the check: over-strip and the scan goes blind. <see cref="CommentStripping_HidesProseOnly"/>
///     drives it both ways on synthetic input built FROM the real constant.</item>
/// </list>
///
/// <para><b>Scope.</b> <c>.claude/</c> and <c>docs/</c> are not scanned — they are prose, and the AC
/// says so explicitly. Build output (<c>bin</c>, <c>obj</c>, <c>dist</c>), <c>node_modules</c>,
/// coverage output and the OpenAPI-generated TypeScript client are excluded as generated or vendored.
/// Everything else under <c>src</c>, <c>frontend</c>, <c>tests</c>, <c>tools</c>, <c>docker</c> and
/// <c>e2e</c> is in.</para>
/// </summary>
public class ToleranceAllowListTests
{
    /// <summary>Where the one production declaration of the predicate lives.</summary>
    private const string SharedPredicatePath =
        "src/Backend/StatsTid.Backend.Api/AllocationBalance.cs";

    /// <summary>
    /// The file the three backend encodings were collapsed OUT of. Called out separately from the
    /// repo-wide scan because this is the direct anti-property: it goes red the instant the expression
    /// is pasted back into an endpoint handler, even if some other file legitimately gained an entry
    /// in the allow-list below.
    /// </summary>
    private const string FormerMirrorSitePath =
        "src/Backend/StatsTid.Backend.Api/Endpoints/ApprovalEndpoints.cs";

    /// <summary>One permitted home for the tolerance, with the argument for permitting it.</summary>
    private sealed record Allowed(string RelativePath, string Why);

    /// <summary>
    /// The exhaustive allow-list. Adding an entry here is a design decision — it says "this copy of
    /// the number is intended and here is why" — and it needs the same scrutiny as adding the copy.
    /// </summary>
    private static readonly Allowed[] AllowList =
    {
        // ── production ──
        new(SharedPredicatePath,
            "THE declaration. AllocationBalance.Tolerance is the single production home of the "
            + "number and of the per-day rule that uses it."),

        new("frontend/src/lib/allocation.ts",
            "ADR-028 D4: the browser cannot call the backend predicate per keystroke, so the grid's "
            + "'Alt fordelt / Ikke fordelt' mirror carries the constant by hand. Mandated, not "
            + "tolerated — and pinned by allocation.test.ts."),

        new("frontend/src/components/SkemaGrid.tsx",
            "NOT this rule. This is `absenceOverNorm` — summed absence hours against the DAILY NORM — "
            + "which merely borrows the same numeric epsilon. The file already imports the real "
            + "encoding from lib/allocation for the allocation rule; binding this second, independent "
            + "rule to the allocation constant would make one number govern two unrelated decisions "
            + "(refinement §3.8, rev-4 correction)."),

        // ── tests that pin the frontend constant ──
        new("frontend/src/lib/__tests__/allocation.test.ts",
            "Pins ALLOCATION_TOLERANCE to its value. A test that asserts a constant must name it."),

        new("frontend/src/components/__tests__/SkemaDayPanel.test.tsx",
            "Names the tolerance in a test title while pinning the day-panel mirror's classification "
            + "boundary."),

        // ── EXTENSIONS to the allow-list as written in refinement AC-1, argued individually ──
        new("tests/StatsTid.Tests.Regression/Outbox/AllocationPredicateCharacterizationTests.cs",
            "EXTENSION (post-dates AC-1). TASK-12700's AC-2 baseline. Its "
            + "AllocationPredicateRoundingLimitTests exist to write down, as executable arithmetic, "
            + "the argument that |Δ| == 0.005 is unreachable once both operands are rounded to "
            + "hundredths — it CANNOT do that without naming the value, and its non-vacuity guard "
            + "asserts the raw pair really sits on that boundary. AC-2 also forbids editing the file. "
            + "It re-implements no rule: it never touches worked/allocated data and never decides a "
            + "verdict."),

        new("tests/StatsTid.Tests.DemoSeed/AllocatedMonthTests.cs",
            "EXTENSION (post-dates AC-1) and the one entry here that is ACCEPTED DEBT rather than a "
            + "design intent. TASK-12701a added a private AllocationTolerance const plus a by-hand "
            + "|round(w,2) − round(al,2)| < tol comparison, to assert the generated demo months would "
            + "pass the real gate. That is a genuine further copy of the rule. It cannot simply "
            + "delegate: StatsTid.Tests.DemoSeed references only tools/StatsTid.DemoSeed, so reaching "
            + "AllocationBalance would mean giving a seeding-test project a dependency on the Backend "
            + "API assembly — an architecture call, and outside TASK-12705's file scope. Recorded here "
            + "so it is visible and dated rather than invisible."),
    };

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  Patterns
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The tolerance by name or by value.
    ///
    /// <para>The numeric arm is guarded on both sides, and both guards are load-bearing:</para>
    /// <list type="bullet">
    ///   <item><c>(?&lt;![\w.])</c> — so <c>10.005</c> and <c>10.0050</c> (real, unrelated values in
    ///     the termination-crystallization rounding tests) are not read as the tolerance embedded in a
    ///     larger number.</item>
    ///   <item><c>(?![1-9])</c> — so <c>0.0050</c> and <c>0.00500</c> DO match (the same value written
    ///     with trailing zeros, which is how a copy would most plausibly evade a naive literal search)
    ///     while <c>0.0051</c>, a different number, does not.</item>
    /// </list>
    /// </summary>
    private static readonly Regex TolerancePattern = new(
        @"\bAllocationTolerance\b|\bALLOCATION_TOLERANCE\b|(?<![\w.])0\.005(?![1-9])",
        RegexOptions.Compiled);

    private static readonly string[] ScanRoots = { "src", "frontend", "tests", "tools", "docker", "e2e" };

    private static readonly string[] ScannedExtensions =
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".sql", ".py", ".ps1", ".psm1", ".sh",
        ".yml", ".yaml", ".json",
    };

    /// <summary>Build output, vendored trees and coverage artefacts — never source.</summary>
    private static readonly string[] ExcludedDirectorySegments =
    {
        "node_modules", "bin", "obj", "dist", "coverage", ".vite", "TestResults", ".git",
    };

    /// <summary>
    /// Generated files, excluded because their content is derived rather than authored. Currently the
    /// OpenAPI-generated TypeScript client (PAT-012): a tolerance appearing there would have come from
    /// the spec, which is itself generated from the C# that this scan already covers.
    /// </summary>
    private static readonly string[] GeneratedFiles = { "frontend/src/lib/api-types.ts" };

    /// <summary>
    /// This file, excluded from its own scan — and the exclusion argued rather than assumed, because
    /// "the checker exempts itself" is otherwise indistinguishable from a hole.
    ///
    /// <para>A check that searches for three spellings of a thing necessarily CONTAINS those three
    /// spellings: <see cref="TolerancePattern"/> quotes both identifiers verbatim, and the allow-list
    /// entries quote the value while arguing about it. Excluding the file is therefore mechanical, not
    /// a judgement about its content — and it is the reason this file declares no tolerance of its own:
    /// <see cref="CommentStripping_HidesProseOnly"/> builds its probe text from
    /// <see cref="AllocationBalance.Tolerance"/> at runtime rather than from a literal, precisely so
    /// the exclusion never has to cover a real second copy.</para>
    ///
    /// <para><see cref="SelfExclusion_IsStillNecessary"/> asserts the exclusion is still earning its
    /// place, so it cannot outlive the reason for it.</para>
    /// </summary>
    private const string SelfPath =
        "tests/StatsTid.Tests.Regression/ArchitectureConstraints/ToleranceAllowListTests.cs";

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  AC-1
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>AC-1.</b> No code-bearing file outside the allow-list mentions the tolerance in executable
    /// text.
    /// </summary>
    [Fact]
    public void Tolerance_HasNoCopyOutsideTheAllowList()
    {
        var allowed = AllowList.Select(a => a.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var offenders = ScanForExecutableMatches()
            .Where(hit => !allowed.Contains(hit.Path))
            .ToList();

        Assert.True(offenders.Count == 0,
            "S127/TASK-12705 AC-1 — the allocation tolerance was found outside its allow-list:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, offenders.Select(o => $"  - {o.Path}: {o.Sample}"))
            + Environment.NewLine + Environment.NewLine
            + "The per-day rule lives in exactly one production place, "
            + $"{SharedPredicatePath} (AllocationBalance.Evaluate). Call it instead of re-declaring "
            + "the number. If a NEW copy is genuinely intended, that is a design decision: add an "
            + "entry to AllowList in this file WITH the argument for it.");
    }

    /// <summary>
    /// The direct anti-property. <c>ApprovalEndpoints.cs</c> held all three backend encodings before
    /// TASK-12705; it must now only delegate. Stated separately from the repo-wide scan so that
    /// pasting the expression back into an endpoint handler fails with a message about THAT, rather
    /// than as one line in a generic offender list.
    /// </summary>
    [Fact]
    public void FormerMirrorSite_OnlyDelegates()
    {
        var text = ExecutableText(AbsolutePath(FormerMirrorSitePath));

        Assert.False(TolerancePattern.IsMatch(text),
            $"'{FormerMirrorSitePath}' contains the allocation tolerance again. Since S127/TASK-12705 "
            + "the send gate, the team-overview hasWarning chip and the allocation-breakdown imbalance "
            + "flag all evaluate the rule through AllocationBalance.Evaluate. Delete the local copy "
            + "and call it — the three surfaces agreeing is the invariant, and a re-inlined expression "
            + "is how they stopped agreeing before.");
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  Guards against this check being worthless
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Every allow-list entry names a file that exists AND still matches. An entry that matches
    /// nothing is a permanently open exemption for a file nobody is watching, so it must be deleted
    /// rather than left "just in case".
    ///
    /// <para>This is also what proves the scan REACHES the allow-listed files at all: if the roots or
    /// the extension set were wrong, the C# and the TypeScript entries would stop matching here before
    /// <see cref="Tolerance_HasNoCopyOutsideTheAllowList"/> could pass vacuously.</para>
    /// </summary>
    [Fact]
    public void EveryAllowListEntry_IsLive()
    {
        var stale = new List<string>();

        foreach (var entry in AllowList)
        {
            var absolute = AbsolutePath(entry.RelativePath);
            if (!File.Exists(absolute))
            {
                stale.Add($"{entry.RelativePath}: file does not exist (moved or deleted?)");
                continue;
            }

            if (!TolerancePattern.IsMatch(ExecutableText(absolute)))
                stale.Add($"{entry.RelativePath}: no longer mentions the tolerance in executable text");
        }

        Assert.True(stale.Count == 0,
            "Stale AC-1 allow-list entries — each is an exemption with nothing left to exempt, so "
            + "delete it (an unused entry silently re-permits a future copy in that file):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, stale.Select(s => $"  - {s}")));
    }

    /// <summary>
    /// Non-vacuity, in the two ways this check could silently stop working: a root that resolves to
    /// nothing, and a pattern that matches nothing.
    ///
    /// <para>Floors are PER ROOT rather than one aggregate, because an aggregate hides exactly the
    /// interesting failure — <c>frontend</c> dropping out would still leave a four-figure total from
    /// <c>src</c> and <c>tests</c>, and two of the allow-listed encodings live in <c>frontend</c>. They
    /// are set well below current counts; they exist to detect a root that vanished, not to track
    /// growth. <c>e2e</c> is deliberately floor-free: it is currently empty (the S127 E2E rebuild has
    /// not landed) and is scanned so that it is covered the moment it is populated.</para>
    /// </summary>
    [Fact]
    public void TheScan_ReachesTheTreeItClaimsTo()
    {
        var byRoot = ScanRoots.ToDictionary(
            r => r,
            r => ScannedFiles(r).Count,
            StringComparer.Ordinal);

        var floors = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["src"] = 200,
            ["frontend"] = 80,
            ["tests"] = 150,
            ["tools"] = 10,
            ["docker"] = 5,
        };

        foreach (var (root, floor) in floors)
        {
            Assert.True(byRoot[root] >= floor,
                $"AC-1 scan reached only {byRoot[root]} file(s) under '{root}' (floor {floor}). "
                + "The root did not resolve, or the extension/exclusion sets stopped matching real "
                + "source. Until that is fixed, a copy of the tolerance in that tree is invisible.");
        }

        // ... and the pattern must actually be finding things. A regex that cannot match its target
        // is the failure mode the S125 review named: a detector whose green means nothing.
        var matched = ScanForExecutableMatches();
        Assert.True(matched.Count >= 5,
            $"The tolerance pattern matched executable text in only {matched.Count} file(s) across the "
            + "whole tree. The known-good tree has both the shared C# declaration and several "
            + "TypeScript encodings, so this reads as a broken pattern or a broken scan rather than a "
            + "clean repository.");
    }

    /// <summary>
    /// The self-exclusion still earns its place. If this file ever stops matching its own pattern, the
    /// exclusion has become a silent exemption for a file nobody is checking, and it must be deleted.
    /// Asserted rather than reasoned about, because "the checker obviously contains its own patterns"
    /// is exactly the kind of obviously-true premise that stops being true after a refactor.
    /// </summary>
    [Fact]
    public void SelfExclusion_IsStillNecessary()
    {
        var self = AbsolutePath(SelfPath);
        Assert.True(File.Exists(self),
            $"The AC-1 checker's self-exclusion names '{SelfPath}', which does not exist. If this file "
            + "was renamed or moved, update SelfPath — until then the real file is being scanned and "
            + "will fail against its own pattern definitions.");

        Assert.True(TolerancePattern.IsMatch(ExecutableText(self)),
            $"'{SelfPath}' no longer matches the tolerance pattern in executable text, so excluding it "
            + "from the scan now exempts a file for no reason. Delete SelfPath and its exclusion.");
    }

    /// <summary>
    /// The canonical declaration is still there. Without this, gutting
    /// <see cref="AllocationBalance"/> would turn AC-1 green — "no copies anywhere" is also what a
    /// repository with no rule at all looks like.
    /// </summary>
    [Fact]
    public void SharedPredicate_StillDeclaresTheTolerance()
    {
        var text = ExecutableText(AbsolutePath(SharedPredicatePath));

        Assert.True(TolerancePattern.IsMatch(text),
            $"'{SharedPredicatePath}' no longer declares the tolerance in executable text. The AC-1 "
            + "guard would now pass vacuously. Restore AllocationBalance.Tolerance.");
    }

    /// <summary>
    /// The comment stripper, driven both ways.
    ///
    /// <para>Comment-stripping is required — a literal sweep hits about ten PROSE mentions of the
    /// tolerance across the endpoints, the Skema components, the DemoSeed builder and the
    /// characterization baseline, and failing on those would make AC-1 unsatisfiable on a correct
    /// tree. But an over-eager stripper is worse than none: it would blind the scan while leaving it
    /// green. So this asserts BOTH directions, per comment syntax.</para>
    ///
    /// <para>The probe text is built from <see cref="AllocationBalance.Tolerance"/> itself rather than
    /// from a literal, for two reasons: it cannot drift from the real constant, and it keeps this file
    /// out of its own allow-list — a checker that had to exempt itself would be a place a copy could
    /// hide.</para>
    /// </summary>
    [Theory]
    [InlineData(".cs")]
    [InlineData(".ts")]
    [InlineData(".sql")]
    [InlineData(".py")]
    public void CommentStripping_HidesProseOnly(string extension)
    {
        var value = AllocationBalance.Tolerance.ToString(CultureInfo.InvariantCulture);
        var (open, close) = extension switch
        {
            ".sql" => ("--", ""),
            ".py" => ("#", ""),
            _ => ("//", ""),
        };

        var commented = $"{open} the tolerance is {value}{close}\nvar unrelated = 1;\n";
        Assert.False(TolerancePattern.IsMatch(StripComments(commented, extension)),
            $"the {extension} stripper left a PROSE mention of {value} in the executable text — AC-1 "
            + "would false-fail on a correct tree.");

        var code = $"threshold = {value};\n";
        Assert.True(TolerancePattern.IsMatch(StripComments(code, extension)),
            $"the {extension} stripper removed EXECUTABLE text declaring {value} — the scan is blind "
            + "and its green means nothing.");
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  Scanner
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private sealed record Hit(string Path, string Sample);

    /// <summary>
    /// Every scanned file whose comment-stripped text mentions the tolerance, with the first matching
    /// line quoted for the failure message.
    /// </summary>
    private static List<Hit> ScanForExecutableMatches()
    {
        var hits = new List<Hit>();

        foreach (var root in ScanRoots)
        foreach (var file in ScannedFiles(root))
        {
            var executable = ExecutableText(file);
            if (!TolerancePattern.IsMatch(executable)) continue;

            var sample = executable
                .Split('\n')
                .FirstOrDefault(TolerancePattern.IsMatch)
                ?.Trim() ?? "(match not on a single line)";
            if (sample.Length > 140) sample = sample[..140] + "…";

            hits.Add(new Hit(RelativePath(file), sample));
        }

        return hits;
    }

    /// <summary>
    /// Code-bearing files under one scan root, minus build output, vendored and generated trees.
    ///
    /// <para>Directories are PRUNED during the walk rather than filtered afterwards. That is not a
    /// micro-optimisation: <c>frontend/node_modules</c> alone is tens of thousands of files, and
    /// enumerating it before discarding it would make this guard slow enough that someone eventually
    /// disables it.</para>
    /// </summary>
    private static List<string> ScannedFiles(string root)
    {
        var absoluteRoot = AbsolutePath(root);
        var files = new List<string>();
        if (!Directory.Exists(absoluteRoot)) return files;

        var pending = new Stack<string>();
        pending.Push(absoluteRoot);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (ExcludedDirectorySegments.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                pending.Push(sub);
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!ScannedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                    continue;

                var relative = RelativePath(file);
                if (GeneratedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;
                if (string.Equals(relative, SelfPath, StringComparison.OrdinalIgnoreCase)) continue;

                files.Add(file);
            }
        }

        return files;
    }

    private static string ExecutableText(string absolutePath) =>
        StripComments(File.ReadAllText(absolutePath), Path.GetExtension(absolutePath));

    /// <summary>
    /// Removes comments so prose mentions of the tolerance are not read as re-declarations.
    ///
    /// <para>Three syntaxes, by extension: C-like (<c>.cs .ts .tsx .js .jsx</c>) strips
    /// <c>/* … */</c> blocks — which is what covers <c>///</c>-free doc blocks and the <c>*</c>-prefixed
    /// continuation lines inside them — then whole-line <c>//</c> and <c>///</c> comments, then
    /// trailing <c>//</c> comments; SQL additionally strips <c>--</c> to end of line; hash-comment
    /// files strip <c>#</c> to end of line. <c>.json</c> has no comments and is scanned raw.</para>
    ///
    /// <para><b>Stated limits, because a stripper that pretends to parse is worse than one that says
    /// what it does not do.</b> It does not tokenise string literals, so a <c>//</c> or <c>#</c>
    /// inside a string truncates that line — a false NEGATIVE, never a false failure. The trailing-
    /// <c>//</c> rule deliberately ignores a <c>//</c> preceded by <c>:</c> so that URLs
    /// (<c>https://…</c>) do not eat the rest of their line. Both are conservative in the direction of
    /// missing a copy rather than inventing one, and
    /// <see cref="CommentStripping_HidesProseOnly"/> pins the behaviour that matters in both
    /// directions.</para>
    /// </summary>
    private static string StripComments(string source, string extension)
    {
        switch (extension.ToLowerInvariant())
        {
            case ".json":
                return source;

            case ".py":
            case ".ps1":
            case ".psm1":
            case ".sh":
            case ".yml":
            case ".yaml":
                return string.Join('\n', source.Split('\n').Select(StripHashComment));

            case ".sql":
            {
                var noBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                return string.Join('\n', noBlocks.Split('\n')
                    .Select(line => Regex.Replace(line, @"--.*$", string.Empty)));
            }

            default:
            {
                var noBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                return string.Join('\n', noBlocks
                    .Split('\n')
                    .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    .Select(line => Regex.Replace(line, @"(?<!:)//.*$", string.Empty)));
            }
        }
    }

    private static string StripHashComment(string line)
    {
        var idx = line.IndexOf('#');
        return idx < 0 ? line : line[..idx];
    }

    // ── paths ──

    private static string AbsolutePath(string relative) =>
        Path.Combine(LocateRepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RelativePath(string absolute) =>
        Path.GetRelativePath(LocateRepoRoot(), absolute).Replace('\\', '/');

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
            "Could not locate the repository root (a directory containing *.sln) from the test bin "
            + $"output. Searched upward from: {AppContext.BaseDirectory}");
    }
}
