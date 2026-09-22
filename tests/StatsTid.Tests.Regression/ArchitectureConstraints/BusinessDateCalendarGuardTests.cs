using System.Text.RegularExpressions;

namespace StatsTid.Tests.Regression.ArchitectureConstraints;

/// <summary>
/// S142 Step-7a WARNING 3 — a repo-wide net over the 64 converted business-date sites, most of which
/// have no behavioural pin of their own.
///
/// <para>
/// <b>Why this exists.</b> S142 moved every business date from the UTC calendar day to the
/// Europe/Copenhagen day: every StatsTid user is Danish, and between Danish midnight and UTC
/// midnight the UTC calendar still reads YESTERDAY, so an HR user working at 00:30 recorded a change
/// as effective the day before. Twelve tasks converted 64 sites, and **roughly twenty of those sites
/// have no discriminating behavioural test** — not because anyone was careless, but because each
/// would need its own seeded fixture and container boot, and the per-task criterion was "at least one
/// pin that fails on the old code", which every task met.
/// </para>
///
/// <para>
/// <b>What this guard can and cannot claim.</b> It catches a REVERT — a converted site changed back
/// to a retired spelling this file knows. It does not and cannot prove the 64 sites are correct, and
/// an unlisted novel respelling will pass. An earlier version of this doc said it "keeps all 64 sites
/// on the Copenhagen calendar", which the external lens correctly called an overstatement, and which
/// also contradicted the sentence above it about how many sites are pinned. *A net, described as a
/// net.*
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
    /// <c>DateTime.Today</c> (machine-local), and a DAY taken from <c>GetLocalNow()</c>.
    ///
    /// <para><b>Every alternative ends in a calendar day, deliberately.</b> An earlier version matched
    /// a bare <c>GetLocalNow()</c>, which the external lens correctly called a false-failure source:
    /// held as a <c>DateTimeOffset</c> it is an INSTANT, and the class doc above promises instants are
    /// exempt. A guard that contradicts its own stated exemption trains people to suppress it.</para>
    /// </summary>
    private static readonly Regex RetiredDayDerivation = new(
        // `\s*` before every `(`: `FromDateTime (DateTime.UtcNow)` and `GetUtcNow ()` are ordinary
        // legal spellings, not exotic evasions, and both defeated the first version (cycle 3).
        @"DateOnly\s*\.\s*FromDateTime\s*\([^;)]*(?:UtcNow|GetUtcNow\s*\(\)|GetLocalNow\s*\(\)|DateTime\.Today|DateTime\.Now)"
        // `.Date` must end on a word boundary. Without it, `GetUtcNow().DateTime` — an INSTANT read
        // the class doc above explicitly exempts — matched the `Date` prefix and was reported as a
        // calendar-day derivation. Step-7a cycle 3 caught that: a guard contradicting its own stated
        // exemption is how people learn to suppress guards.
        + @"|Get(?:Utc|Local)Now\s*\(\)\s*\.\s*(?:(?:Utc|Local)?DateTime\s*\.\s*)?Date\b"
        // `.UtcDateTime.Date` after DateTimeOffset.UtcNow was missed, and the class doc claimed to
        // cover exactly that spelling — the guard contradicting its own documentation again.
        + @"|DateTimeOffset\s*\.\s*(?:UtcNow|Now)\s*\.\s*(?:(?:Utc|Local)?DateTime\s*\.\s*)?Date\b"
        + @"|DateTime\s*\.\s*(?:UtcNow|Now)\s*\.\s*Date\b"
        + @"|\bDateTime\s*\.\s*Today\b",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// The database deciding a <b>calendar day</b> — owner ruling OQ-7 moved every one of these into
    /// the application, where the zone is explicit and testable.
    ///
    /// <para><b>Every alternative must end in a DAY.</b> A bare <c>LOCALTIMESTAMP</c> was matched by
    /// the first version and is wrong to match: it yields a timestamp without time zone, not a
    /// calendar day, so the guard would have refused legitimate SQL while claiming it "lets the
    /// database decide the day" (Step-7a cycle 3, external lens). Only a cast to <c>date</c> makes it
    /// a day. The same reasoning added the whitespace-tolerant and <c>CAST(… AS date)</c> spellings:
    /// those are ordinary equivalents a revert would plausibly use, not exotic evasions.</para>
    /// </summary>
    private static readonly Regex DatabaseDecidedDay = new(
        @"\bCURRENT_DATE\b"
        + @"|\b(?:now\s*\(\)|CURRENT_TIMESTAMP|LOCALTIMESTAMP)\s*::\s*date\b"
        + @"|\bCAST\s*\(\s*(?:now\s*\(\)|CURRENT_TIMESTAMP|LOCALTIMESTAMP)\s+AS\s+date\s*\)"
        + @"|\bdate_trunc\s*\(\s*'day'\s*,\s*now\s*\(\)",
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

    /// <summary>
    /// ★ This fact KEEPS string literals; the one above strips them. That asymmetry is the whole
    /// point, and getting it wrong made this test vacuous on its first outing (S142 Step-7a cycle 2).
    ///
    /// <para><b>SQL in C# lives ONLY inside string literals.</b> The first version of this guard
    /// stripped every literal before matching — so the `CURRENT_DATE` pattern could never see a
    /// single line of SQL, and every statement this sprint repaired (all of them in <c>"""raw"""</c>
    /// strings, which the stripper deleted wholesale) could have been reverted with CI still green.
    /// The sprint log meanwhile claimed all 64 rows were pinned. <b>A guard against tests that cannot
    /// fail, half of which could not fail.</b></para>
    ///
    /// <para>Comments are still stripped, for the reason they always were: this repository quotes the
    /// retired SQL when explaining why it was removed, and all 30 such matches at HEAD are comment
    /// lines. Executable strings containing these tokens are exactly what we want to see.</para>
    /// </summary>
    [Fact]
    public void NoProductionSql_LetsTheDatabaseDecideTheDay()
    {
        var offenders = ScanCSharp(DatabaseDecidedDay, stripStringLiterals: false);

        Assert.True(
            offenders.Count == 0,
            "These files let the DATABASE SERVER decide a calendar day. That day is whatever zone "
            + "the Postgres container happens to run in — a value nobody sets, sees or can pin in a "
            + "test. Owner ruling OQ-7 moved every one of them into the application: bind an "
            + "application-supplied @today derived from CopenhagenBusinessDate.\n\n"
            + string.Join("\n", offenders));
    }

    // ── scanning ────────────────────────────────────────────────────────────────────────────────

    private static List<string> ScanCSharp(Regex pattern, bool stripStringLiterals = true)
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

            var executable = StripComments(File.ReadAllText(file), stripStringLiterals);
            var match = pattern.Match(executable);
            if (!match.Success) continue;

            offenders.Add($"  {Path.GetRelativePath(root, file).Replace('\\', '/')} — `{Truncate(match.Value)}`");
        }

        return offenders;
    }

    /// <summary>
    /// Strips comments always; strips string literals only when asked.
    ///
    /// <para><b>Comments must always go</b> because this repository deliberately <i>quotes</i> the
    /// retired shapes when explaining why they were removed — S142 rewrote roughly forty such
    /// comments, and a guard that fired on its own explanation would push authors toward deleting the
    /// explanation.</para>
    ///
    /// <para><b>String literals are conditional, and that is the correction.</b> For the C# fact,
    /// stripping them is right: the retired expressions are code, and the patterns also appear inside
    /// error-message text. For the SQL fact it was fatal — SQL exists in C# <i>only</i> inside
    /// literals, so stripping them left that fact scanning for `CURRENT_DATE` among bare identifiers,
    /// where it could never appear. Caught at Step-7a cycle 2.</para>
    ///
    /// <para><b>Why a single-pass scanner and not regexes.</b> The first version stripped comments
    /// with one regex and strings with another, comments first. Both review lenses caught the same
    /// class of bug in that: a comment marker INSIDE a string (a <c>//</c> in a URL, a <c>/*</c> in
    /// SQL) erased the rest of that line, which could hide a real offender — and a quote inside a
    /// comment could do the mirror image. Order-of-stripping cannot fix it, because each pass is
    /// blind to the other's context. The scanner below walks the source once and recognises a
    /// comment ONLY when it is genuinely outside every literal, which is the only way to get this
    /// right.</para>
    /// </summary>
    private static string StripComments(string source, bool stripStringLiterals)
    {
        var output = new System.Text.StringBuilder(source.Length);
        var i = 0;

        while (i < source.Length)
        {
            // ── STRING LITERALS, with optional $ / @ prefixes in either legal order ──
            //
            // ★ AN INTERPOLATED STRING IS NEVER STRIPPED. Its holes contain EXECUTABLE C#, so
            //   stripping `$@"Day: {DateTime.UtcNow.Date}"` deletes a genuine offender — a FALSE
            //   PASS, and the third disguise of this guard's recurring defect (Step-7a cycle 3,
            //   external lens). Properly lexing hole-vs-text needs real interpolation tracking;
            //   keeping the whole construct is the conservative alternative, and it errs the right
            //   way: the literal TEXT is then scanned too, which can only raise a loud false
            //   positive, never hide a real one. That is the same trade the rest of this scanner
            //   makes, stated rather than stumbled into.
            {
                var p = i;
                var interpolated = false;
                var verbatim = false;
                while (p < source.Length && (source[p] == '$' || source[p] == '@'))
                {
                    if (source[p] == '$') interpolated = true; else verbatim = true;
                    p++;
                }

                if (p < source.Length && source[p] == '"')
                {
                    var keep = !stripStringLiterals || interpolated;
                    int close;

                    if (Match(source, p, "\"\"\""))
                    {
                        // Raw string. The fence is ANY run of 3+ quotes and the closer must be a run
                        // of the SAME length — assuming 3 meant a 4-quote fence closed early on its
                        // own legal content, after which the real closer opened a phantom string
                        // that swallowed the rest of the file.
                        var fence = 0;
                        while (p + fence < source.Length && source[p + fence] == '"') fence++;

                        close = source.Length;
                        for (var k = p + fence; k + fence <= source.Length; k++)
                        {
                            if (source[k] != '"') continue;
                            var run = 0;
                            while (k + run < source.Length && source[k + run] == '"') run++;
                            if (run == fence) { close = k + fence; break; }
                            k += run - 1;
                        }
                    }
                    else if (verbatim)
                    {
                        // @"…" — a doubled quote is an escaped quote, and the literal may span lines.
                        var j = p + 1;
                        while (j < source.Length)
                        {
                            if (source[j] == '"')
                            {
                                if (Match(source, j, "\"\"")) { j += 2; continue; }
                                j++; break;
                            }
                            j++;
                        }
                        close = j;
                    }
                    else
                    {
                        // Ordinary "…" — backslash escapes, and it cannot span a newline.
                        var j = p + 1;
                        while (j < source.Length && source[j] != '\n')
                        {
                            // Math.Min: a file ending in a backslash escape with no closing quote
                            // would otherwise overshoot Length and throw on the slice below —
                            // uncompilable input, but a guard should report, not crash.
                            if (source[j] == '\\') { j = Math.Min(j + 2, source.Length); continue; }
                            if (source[j] == '"') { j++; break; }
                            j++;
                        }
                        close = j;
                    }

                    Emit(output, source, i, close, keep);
                    i = close;
                    continue;
                }
            }

            // ── char literal: '…' — skipped so a quote inside it cannot open a string.
            //    BOUNDED to a few characters: a real char literal is at most ~10 (`'￿'`). An
            //    unbounded scan swallowed to end of line whenever an apostrophe appeared in prose
            //    or after a stray quote, which could hide an offender (Step-7a cycle 3). If no
            //    closing quote appears within the bound, this is not a char literal — emit the
            //    apostrophe as ordinary code and carry on, which also handles `#region Don't`.
            if (source[i] == '\'')
            {
                const int maxCharLiteral = 12;
                var j = i + 1;
                var closed = false;
                while (j < source.Length && j - i <= maxCharLiteral && source[j] != '\n')
                {
                    if (source[j] == '\\') { j += 2; continue; }
                    if (source[j] == '\'') { j++; closed = true; break; }
                    j++;
                }

                if (closed)
                {
                    output.Append(' ');
                    i = j;
                }
                else
                {
                    output.Append(source[i]);
                    i++;
                }
                continue;
            }

            // ── comments: only recognised HERE, i.e. outside any literal ──
            if (Match(source, i, "//"))
            {
                var nl = source.IndexOf('\n', i);
                i = nl < 0 ? source.Length : nl;
                output.Append(' ');
                continue;
            }

            if (Match(source, i, "/*"))
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? source.Length : end + 2;
                output.Append(' ');
                continue;
            }

            output.Append(source[i]);
            i++;
        }

        return output.ToString();

        static bool Match(string s, int at, string token) =>
            at + token.Length <= s.Length && string.CompareOrdinal(s, at, token, 0, token.Length) == 0;

        static void Emit(System.Text.StringBuilder sb, string s, int from, int to, bool keep)
        {
            if (keep) sb.Append(s, from, to - from);
            else sb.Append(' ');
        }
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
