using System.Text;

namespace StatsTid.Tests.Unit.Serialization.Goldens;

/// <summary>
/// The reader, comparer and capture writer for the persisted-bytes goldens (TASK-14900, ADR-045).
///
/// <para>
/// <b>Plain language.</b> A golden is a literal: the exact text the system stores today, written into a data
/// file next to these tests. The facts compare what the code writes now with that file, so a runtime or
/// library move that changed one stored byte turns a fact red. The file format is one entry per line,
/// <c>name TAB value</c>, sorted by name (ordinal), the value on a single line. A reader trims a trailing
/// carriage return, because a checkout on Windows turns LF into CRLF and the repository has no
/// <c>.gitattributes</c>.
/// </para>
///
/// <para>
/// <b>Capture.</b> When a data file is absent, the comparing fact writes the actual text to
/// <c>golden-capture/&lt;file&gt;</c> under the test output directory and fails with a "golden capture"
/// message. The capture is a generated artifact: it is copied, unedited, into the data file, never
/// hand-written, and a red on another platform is a stop, not a re-capture.
/// </para>
/// </summary>
internal static class GoldenFile
{
    private static string DataPath(string file) =>
        Path.Combine(AppContext.BaseDirectory, "Serialization", "Goldens", file);

    /// <summary>Reads the data file, or fails telling the reader how to capture it.</summary>
    public static IReadOnlyDictionary<string, string> Load(string file)
    {
        var path = DataPath(file);
        if (!File.Exists(path))
            Assert.Fail($"golden data file '{file}' is missing from the output directory; run the capturing fact once to produce golden-capture/{file}.");
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(path, new UTF8Encoding(false)))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var tab = line.IndexOf('\t');
            Assert.True(tab > 0, $"golden '{file}': a line without a tab separator: {line}");
            Assert.True(map.TryAdd(line[..tab], line[(tab + 1)..]), $"golden '{file}': duplicate name '{line[..tab]}'");
        }
        return map;
    }

    /// <summary>
    /// Compares <paramref name="actual"/> with the data file: same names, same values. A missing file is
    /// captured (see the class remarks) and fails.
    /// </summary>
    public static void AssertMatches(string file, IReadOnlyDictionary<string, string> actual)
    {
        foreach (var kv in actual)
            Assert.False(kv.Key.Contains('\t') || kv.Value.Contains('\n') || kv.Value.Contains('\r'),
                $"golden '{file}': '{kv.Key}' is not a single-line entry");

        var path = DataPath(file);
        if (!File.Exists(path))
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "golden-capture");
            Directory.CreateDirectory(dir);
            var text = string.Concat(actual.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Key}\t{kv.Value}\n"));
            var target = Path.Combine(dir, file);
            File.WriteAllText(target, text, new UTF8Encoding(false));
            Assert.Fail($"golden capture: '{file}' is absent; the actual text ({actual.Count} entries) was written to {target}.");
        }

        var expected = Load(file);
        var problems = new List<string>();
        foreach (var name in expected.Keys.Except(actual.Keys, StringComparer.Ordinal))
            problems.Add($"missing in actual: {name}");
        foreach (var name in actual.Keys.Except(expected.Keys, StringComparer.Ordinal))
            problems.Add($"not in golden: {name}");
        foreach (var kv in actual)
            if (expected.TryGetValue(kv.Key, out var want) && !string.Equals(want, kv.Value, StringComparison.Ordinal))
                problems.Add($"{kv.Key}\n   golden: {want}\n   actual: {kv.Value}");
        Assert.True(problems.Count == 0, $"golden '{file}' differs in {problems.Count} entries:\n" + string.Join("\n", problems));
    }
}
