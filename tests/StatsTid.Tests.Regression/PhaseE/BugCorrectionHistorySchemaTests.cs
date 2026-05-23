using System.Diagnostics;
using System.Text.RegularExpressions;

namespace StatsTid.Tests.Regression.PhaseE;

/// <summary>
/// S40 TASK-4006 — Phase E continuous-validation tests per ADR-024 L122 + D3 L102-113.
///
/// Validates the <c>bug_correction_history</c> schema invariants on the agreement source
/// register (<c>docs/references/agreement-source-register.md</c>). Cutover-independent —
/// these checks parse the markdown register only; they do not depend on the rule engine
/// or payroll surfaces. Plain regression (no Docker harness).
///
/// Per ADR-024 D3 (L102-113), every <c>bug_correction_history</c> entry must carry:
///   { date, from_value, to_value, source (or legacy source_url), commit, classifier,
///     was_agreed, materially_wrong, action }
/// with constrained enum values and a resolvable git commit (or documented WIP placeholder).
/// </summary>
public class BugCorrectionHistorySchemaTests
{
    // --- Schema definitions per ADR-024 D3 ---

    private static readonly HashSet<string> WasAgreedValues = new()
    {
        "YES", "NO", "PENDING"
    };

    private static readonly HashSet<string> MateriallyWrongFixedValues = new()
    {
        "NO_PRE_LAUNCH",
        "YES_PRE_LAUNCH_BUT_BROKEN",
        "YES_WITH_PAST_IMPACT",
        "PENDING_PHASE_B",
    };

    // PENDING_S<NN> is also valid (e.g. PENDING_S40) — regex check.
    private static readonly Regex MateriallyWrongPendingSprintPattern =
        new(@"^PENDING_S\d+$", RegexOptions.Compiled);

    private static readonly HashSet<string> ActionValues = new()
    {
        "bug-fix-without-recompute",
        "bug-fix-with-recompute",
        "decision-recorded-fix-deferred",
        "provisional-pending-phase-b",
    };

    private static readonly Regex DateFormatPattern =
        new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    // Real git SHA: 7-40 hex chars.
    private static readonly Regex GitShaPattern =
        new(@"^[0-9a-f]{7,40}$", RegexOptions.Compiled);

    // WIP placeholder: <this S<NN>... commit> — permissive variant captures both
    // simple "<this S37 commit>" and compound "<this S37 commit + earlier TASK-3701 commit>".
    private static readonly Regex CommitPlaceholderPattern =
        new(@"^<this S\d+[^>]*commit>$", RegexOptions.Compiled);

    // --- Parser ---

    private sealed record BugCorrectionEntry(
        int SourceLineNumber,
        string RawEntry,
        IReadOnlyDictionary<string, string> Fields);

    /// <summary>
    /// Locates the agreement source register on disk. Tries the standard bin/Debug/net8.0
    /// relative path (5 levels up); falls back to the STATSTID_REPO_ROOT environment variable.
    /// </summary>
    private static string LocateSourceRegister()
    {
        var envRoot = Environment.GetEnvironmentVariable("STATSTID_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            var envCandidate = Path.GetFullPath(
                Path.Combine(envRoot, "docs", "references", "agreement-source-register.md"));
            if (File.Exists(envCandidate))
                return envCandidate;
        }

        // bin/Debug/net8.0 -> repo root: 5 levels up
        // C:\StatsTid\tests\StatsTid.Tests.Regression\bin\Debug\net8.0  -> C:\StatsTid
        var baseCandidate = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "references", "agreement-source-register.md"));
        if (File.Exists(baseCandidate))
            return baseCandidate;

        throw new FileNotFoundException(
            $"Could not locate agreement-source-register.md. Tried:\n" +
            $"  - {baseCandidate}\n" +
            $"  - STATSTID_REPO_ROOT env (={envRoot ?? "(unset)"})\n" +
            "Set STATSTID_REPO_ROOT to the repo root if running outside the standard test layout.");
    }

    /// <summary>
    /// Parses all non-empty bug_correction_history entries from the source register.
    /// Returns flattened (entry, field-map) pairs — one per JSON-like object inside the
    /// outer array. Order preserved by source line number for diagnostic context.
    /// </summary>
    private static IReadOnlyList<BugCorrectionEntry> LoadBugCorrectionEntries()
    {
        var path = LocateSourceRegister();
        var lines = File.ReadAllLines(path);

        // Markdown row pattern: leading `| `bug_correction_history` | ` then payload then ` |`.
        // Non-empty payload starts with a backtick-wrapped array literal: `[{...}]`.
        // We match the inner array content (between the wrapping backticks).
        // The row may be very long (single-line table cell).
        var rowPattern = new Regex(
            @"^\|\s*`bug_correction_history`\s*\|\s*`(\[.+\])`\s*\|",
            RegexOptions.Compiled);

        var entries = new List<BugCorrectionEntry>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var rowMatch = rowPattern.Match(line);
            if (!rowMatch.Success) continue;

            var arrayLiteral = rowMatch.Groups[1].Value;
            int sourceLineNumber = i + 1;

            // Split the array into individual {...} objects. The array contains 1+ objects
            // separated by "}, {" or just "}{" — but in practice the register uses one
            // object per entry. Robust splitter: track brace depth.
            var objectStrings = SplitTopLevelObjects(arrayLiteral, sourceLineNumber);

            foreach (var objStr in objectStrings)
            {
                var fields = ParseJsonLikeObject(objStr, sourceLineNumber);
                entries.Add(new BugCorrectionEntry(sourceLineNumber, objStr, fields));
            }
        }

        return entries;
    }

    /// <summary>
    /// Splits a top-level "[ {..}, {..} ]" string into individual object substrings.
    /// Brace-depth aware — handles nested braces and quoted strings.
    /// </summary>
    private static List<string> SplitTopLevelObjects(string arrayLiteral, int sourceLineNumber)
    {
        var result = new List<string>();
        int depth = 0;
        int objectStart = -1;
        bool inQuotes = false;
        char quoteChar = '"';

        for (int i = 0; i < arrayLiteral.Length; i++)
        {
            char c = arrayLiteral[i];

            if (inQuotes)
            {
                if (c == '\\' && i + 1 < arrayLiteral.Length)
                {
                    i++; // skip escaped char
                    continue;
                }
                if (c == quoteChar)
                    inQuotes = false;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                inQuotes = true;
                quoteChar = c;
                continue;
            }

            if (c == '{')
            {
                if (depth == 0)
                    objectStart = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && objectStart >= 0)
                {
                    result.Add(arrayLiteral.Substring(objectStart, i - objectStart + 1));
                    objectStart = -1;
                }
            }
        }

        Assert.True(result.Count > 0,
            $"Could not parse any objects out of array literal at SR line {sourceLineNumber}. " +
            $"Verify table structure. Raw: {Truncate(arrayLiteral, 240)}");
        return result;
    }

    /// <summary>
    /// Parses a JSON-like object literal with unquoted keys into a field map.
    /// Format: {key1: value1, key2: "string value", key3: NO}
    /// Values may be quoted strings, bare identifiers, or numbers/dates.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ParseJsonLikeObject(
        string objectString, int sourceLineNumber)
    {
        // Strip the outer { ... }
        if (!objectString.StartsWith("{") || !objectString.EndsWith("}"))
        {
            throw new FormatException(
                $"SR line {sourceLineNumber}: expected object literal '{{...}}' but got " +
                $"'{Truncate(objectString, 120)}'");
        }

        var inner = objectString.Substring(1, objectString.Length - 2);
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Walk the inner content tracking quote state + brace/bracket depth so commas
        // inside quoted strings or nested structures don't split fields.
        var fieldStrings = SplitTopLevelCommas(inner);

        foreach (var fieldStr in fieldStrings)
        {
            var trimmed = fieldStr.Trim();
            if (trimmed.Length == 0) continue;

            // Split on the FIRST colon — values may contain colons (e.g. URLs, times).
            int colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0)
            {
                throw new FormatException(
                    $"SR line {sourceLineNumber}: field has no key:value separator: " +
                    $"'{Truncate(trimmed, 120)}'");
            }

            var key = trimmed.Substring(0, colonIdx).Trim();
            var value = trimmed.Substring(colonIdx + 1).Trim();

            // Strip surrounding quotes if present (single or double).
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value.Substring(1, value.Length - 2);
            }

            fields[key] = value;
        }

        return fields;
    }

    /// <summary>
    /// Splits on commas not inside quoted strings or nested {} / [] structures.
    /// </summary>
    private static List<string> SplitTopLevelCommas(string s)
    {
        var parts = new List<string>();
        int start = 0;
        int braceDepth = 0;
        int bracketDepth = 0;
        bool inQuotes = false;
        char quoteChar = '"';

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            if (inQuotes)
            {
                if (c == '\\' && i + 1 < s.Length)
                {
                    i++;
                    continue;
                }
                if (c == quoteChar)
                    inQuotes = false;
                continue;
            }

            switch (c)
            {
                case '"':
                case '\'':
                    inQuotes = true;
                    quoteChar = c;
                    break;
                case '{': braceDepth++; break;
                case '}': braceDepth--; break;
                case '[': bracketDepth++; break;
                case ']': bracketDepth--; break;
                case ',':
                    if (braceDepth == 0 && bracketDepth == 0)
                    {
                        parts.Add(s.Substring(start, i - start));
                        start = i + 1;
                    }
                    break;
            }
        }

        if (start < s.Length)
            parts.Add(s.Substring(start));

        return parts;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max) + "...";

    // --- Tests ---

    /// <summary>
    /// Test 1: All non-empty entries can be parsed without error and the count is &gt;0.
    /// </summary>
    [Fact]
    public void MarkdownParse_ExtractsAllNonEmptyEntries_WithoutError()
    {
        var entries = LoadBugCorrectionEntries();

        Assert.True(entries.Count > 0,
            "Expected at least one non-empty bug_correction_history entry in the source " +
            "register, but parser found zero. Verify table structure or that the register " +
            "file is reachable.");

        // Sanity: each entry must have at least one parsed field.
        foreach (var e in entries)
        {
            Assert.True(e.Fields.Count > 0,
                $"SR line {e.SourceLineNumber}: parsed entry has zero fields. " +
                $"Raw: {Truncate(e.RawEntry, 240)}");
        }
    }

    /// <summary>
    /// Test 2: Every entry carries all 9 required schema fields per ADR-024 L102-113.
    /// Either <c>source</c> or legacy <c>source_url</c> satisfies the URL field per ADR-024 L106.
    /// </summary>
    [Fact]
    public void AllEntries_HaveNineRequiredFields()
    {
        var entries = LoadBugCorrectionEntries();
        Assert.NotEmpty(entries);

        // Required keys other than the URL field.
        string[] required = {
            "date", "from_value", "to_value",
            "commit", "classifier",
            "was_agreed", "materially_wrong", "action"
        };

        var failures = new List<string>();

        foreach (var e in entries)
        {
            foreach (var key in required)
            {
                if (!e.Fields.ContainsKey(key))
                {
                    failures.Add(
                        $"SR line {e.SourceLineNumber}: missing required field '{key}'. " +
                        $"Found keys: [{string.Join(", ", e.Fields.Keys)}]");
                }
            }

            // URL field: either 'source' or 'source_url' must be present.
            if (!e.Fields.ContainsKey("source") && !e.Fields.ContainsKey("source_url"))
            {
                failures.Add(
                    $"SR line {e.SourceLineNumber}: missing both 'source' and 'source_url' " +
                    $"(at least one is required per ADR-024 L106). " +
                    $"Found keys: [{string.Join(", ", e.Fields.Keys)}]");
            }
        }

        Assert.True(failures.Count == 0,
            "bug_correction_history schema field-presence violations:\n  - " +
            string.Join("\n  - ", failures));
    }

    /// <summary>
    /// Test 3: Enum-valued fields (was_agreed, materially_wrong, action) carry only spec-allowed values.
    /// </summary>
    [Fact]
    public void AllEntries_EnumValuesValid()
    {
        var entries = LoadBugCorrectionEntries();
        Assert.NotEmpty(entries);

        var failures = new List<string>();

        foreach (var e in entries)
        {
            if (e.Fields.TryGetValue("was_agreed", out var wa))
            {
                if (!WasAgreedValues.Contains(wa))
                {
                    failures.Add(
                        $"SR line {e.SourceLineNumber}: was_agreed='{wa}' not in " +
                        $"{{{string.Join(", ", WasAgreedValues)}}}");
                }
            }

            if (e.Fields.TryGetValue("materially_wrong", out var mw))
            {
                bool ok = MateriallyWrongFixedValues.Contains(mw)
                       || MateriallyWrongPendingSprintPattern.IsMatch(mw);
                if (!ok)
                {
                    failures.Add(
                        $"SR line {e.SourceLineNumber}: materially_wrong='{mw}' not in " +
                        $"{{{string.Join(", ", MateriallyWrongFixedValues)}, PENDING_S<NN>}}");
                }
            }

            if (e.Fields.TryGetValue("action", out var act))
            {
                if (!ActionValues.Contains(act))
                {
                    failures.Add(
                        $"SR line {e.SourceLineNumber}: action='{act}' not in " +
                        $"{{{string.Join(", ", ActionValues)}}}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "bug_correction_history enum-value violations:\n  - " +
            string.Join("\n  - ", failures));
    }

    /// <summary>
    /// Test 4: The <c>date</c> field is ISO 8601 YYYY-MM-DD.
    /// </summary>
    [Fact]
    public void AllEntries_DateFieldFormatValid()
    {
        var entries = LoadBugCorrectionEntries();
        Assert.NotEmpty(entries);

        var failures = new List<string>();

        foreach (var e in entries)
        {
            if (!e.Fields.TryGetValue("date", out var date))
            {
                failures.Add($"SR line {e.SourceLineNumber}: date field absent");
                continue;
            }

            if (!DateFormatPattern.IsMatch(date))
            {
                failures.Add(
                    $"SR line {e.SourceLineNumber}: date='{date}' does not match YYYY-MM-DD");
            }
        }

        Assert.True(failures.Count == 0,
            "bug_correction_history date-format violations:\n  - " +
            string.Join("\n  - ", failures));
    }

    /// <summary>
    /// Test 5: The <c>commit</c> field is either a resolvable git SHA (verified via
    /// <c>git cat-file -e SHA^{commit}</c>) or a WIP placeholder of the form
    /// <c>&lt;this S&lt;NN&gt;... commit&gt;</c>.
    /// </summary>
    [Fact]
    public void AllEntries_CommitFieldResolvesOrIsPlaceholder()
    {
        var entries = LoadBugCorrectionEntries();
        Assert.NotEmpty(entries);

        var failures = new List<string>();

        foreach (var e in entries)
        {
            if (!e.Fields.TryGetValue("commit", out var commit) || string.IsNullOrWhiteSpace(commit))
            {
                failures.Add($"SR line {e.SourceLineNumber}: commit field absent or empty");
                continue;
            }

            // Placeholder form — documented WIP, accepted.
            if (CommitPlaceholderPattern.IsMatch(commit))
                continue;

            // Real-looking SHA — verify via git cat-file.
            if (GitShaPattern.IsMatch(commit))
            {
                if (TryResolveCommit(commit, out var diagnostic))
                    continue;

                failures.Add(
                    $"SR line {e.SourceLineNumber}: commit='{commit}' looks like a SHA but " +
                    $"does not resolve in the local repo. git diagnostic: {diagnostic}");
                continue;
            }

            failures.Add(
                $"SR line {e.SourceLineNumber}: commit='{commit}' is neither a 7-40 hex SHA " +
                $"nor a '<this S<NN>... commit>' placeholder");
        }

        Assert.True(failures.Count == 0,
            "bug_correction_history commit-resolution violations:\n  - " +
            string.Join("\n  - ", failures));
    }

    /// <summary>
    /// Runs <c>git cat-file -e &lt;sha&gt;^{commit}</c> in the repo root. Returns true on exit 0.
    /// </summary>
    private static bool TryResolveCommit(string sha, out string diagnostic)
    {
        // Repo root: parent of the docs/ folder we already located.
        var registerPath = LocateSourceRegister();
        var repoRoot = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(registerPath)!, "..", ".."));

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"cat-file -e {sha}^{{commit}}",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                diagnostic = "Process.Start returned null";
                return false;
            }

            if (!proc.WaitForExit(10_000))
            {
                try { proc.Kill(); } catch { /* best-effort */ }
                diagnostic = "git cat-file timed out after 10s";
                return false;
            }

            if (proc.ExitCode == 0)
            {
                diagnostic = "ok";
                return true;
            }

            var stderr = proc.StandardError.ReadToEnd().Trim();
            diagnostic = $"exit={proc.ExitCode}, stderr='{stderr}'";
            return false;
        }
        catch (Exception ex)
        {
            diagnostic = $"exception: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }
}
