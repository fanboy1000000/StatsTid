using System.Text.RegularExpressions;
using StatsTid.SharedKernel.Calendar;
using StatsTid.Tests.Regression.Hosting;

namespace StatsTid.Tests.Regression.ArchitectureConstraints;

/// <summary>
/// S142 / TASK-14207 — <b>the parity pin for census rows 12 and 53.</b>
///
/// <para><b>What this protects, in plain language.</b> Two pieces of StatsTid have to agree on
/// what day it is. One is the year-overview screen (<c>BalanceEndpoints.cs</c>), which marks a
/// period past, current ("Nu") or future and dates every lookup it makes. The other is the
/// vacation settlement engine (<c>VacationSettlementService.cs</c>), which values a closed
/// ferieår. The settlement block at <c>VacationSettlementService.cs:1462-1489</c> exists for one
/// reason: to reproduce the year-overview reader's chain byte-for-byte, so the number the engine
/// settles and the number the employee reads on screen come from the same inputs. If the two ever
/// derive a DIFFERENT "today", they disagree for the one-to-two-hour window between Danish
/// midnight and UTC midnight every night — the screen would mark a period "now" that the engine
/// considers past, or the reverse, and only when a <c>user_agreement_codes</c> row happened to
/// start on exactly that date would anyone notice.</para>
///
/// <para><b>Why the two sites had to move together.</b> Before S142 both derived the UTC calendar
/// day. That was WRONG (every StatsTid user is Danish; a user working at 00:30 in Copenhagen was
/// recorded against yesterday) but it was wrong IDENTICALLY, so the parity contract held. Moving
/// only one would have converted a shared off-by-one into a genuine disagreement between the
/// engine and the screen — strictly worse than the bug being fixed. Hence one task, one commit.</para>
///
/// <para><b>Why this test asserts against a LITERAL and not against the other site.</b> The
/// obvious pin — "assert site A's day equals site B's day" — is exactly the test that cannot
/// detect this sprint's defect: before S142 both sites agreed, on the wrong day, and such a test
/// passed. Parity is necessary but not sufficient. Every assertion below therefore compares the
/// derived day to a hand-computed literal (<c>2026-07-16</c>, <c>2026-01-16</c>,
/// <c>2026-01-15</c>) — never to the other site, and never to a value produced by calling the
/// helper under test.</para>
///
/// <para><b>Why the pin is source-anchored rather than an end-to-end behavioural assertion.</b>
/// Both sites need a live Postgres to execute (the endpoint serves from the database; the
/// settlement pass runs inside a caller-owned transaction), so a behavioural pin over both is
/// Docker-gated and cannot be run — or RED-verified — on a developer machine without Docker. This
/// class closes the gap the other way, and closes it completely, by machine-checking every link in
/// the chain from each call site to the shared, literal-pinned implementation:</para>
/// <list type="number">
///   <item><description><see cref="BothParitySites_DeriveToday_FromTheCopenhagenBusinessDay"/> —
///   the statement that produces <c>today</c> at each site is the Copenhagen derivation
///   (located by walking BACK from the shared consumer call, so it pins the real site rather than
///   any same-named local).</description></item>
///   <item><description><see cref="SettlementHelper_DelegatesToTheSharedCopenhagenBusinessDate"/> —
///   the settlement site's one level of indirection (<c>CopenhagenToday()</c>) resolves to the same
///   shared function, so the indirection is not a place a divergence can hide.</description></item>
///   <item><description><see cref="BothParityFiles_BindCopenhagenBusinessDate_ToTheSharedKernel"/> —
///   the identifier <c>CopenhagenBusinessDate</c> in both files binds to the SharedKernel type this
///   test evaluates, with no <c>using</c>-alias redirecting it elsewhere.</description></item>
///   <item><description><see cref="SharedDerivation_AtEachBoundaryInstant_EqualsTheLiteralDanishDay"/> —
///   that shared function returns the literal Danish day at all three wave-1 boundary
///   instants.</description></item>
///   <item><description><see cref="NeitherParityFile_DerivesACalendarDay_FromTheUtcInstant"/> —
///   the anti-property: the pre-S142 UTC-day shape appears in neither file's executable text, so a
///   partial revert fails here even if it left the assertions above satisfied
///   elsewhere.</description></item>
/// </list>
///
/// <para>The Docker-gated behavioural companion for the endpoint half is
/// <see cref="StatsTid.Tests.Regression.Balance.YearOverviewCopenhagenDayTests"/>, which asserts
/// the SERVED <c>today</c> at the same divergent instant. This class runs without Docker precisely
/// so the RED can be demonstrated locally.</para>
///
/// <para><b>Instants come from <see cref="BoundaryInstants"/></b> (TASK-14200, wave 1) rather than
/// being re-derived here — that is the whole point of centralising them, and it is the concrete
/// dependency this task has on the wave-1 harness.</para>
/// </summary>
public sealed class YearOverviewSettlementClockParityTests
{
    // ── The two sites, repo-relative (forward slashes; normalised per-platform below) ──

    /// <summary>Census row 12 — the year-overview reader's sole past/current/future + "Nu" authority.</summary>
    private const string YearOverviewFile =
        "src/Backend/StatsTid.Backend.Api/Endpoints/BalanceEndpoints.cs";

    /// <summary>Census row 53 — the settlement pass's D9 reader-parity operand.</summary>
    private const string SettlementFile =
        "src/Infrastructure/StatsTid.Infrastructure/VacationSettlementService.cs";

    // ── The CONSUMER each site feeds. Both sites exist to key this exact dated read, which is
    //    what makes them a parity pair in the first place; anchoring on the consumer (rather than
    //    on a line number, or on the first `var today` in the file) means the guard keeps pointing
    //    at the right statement when the file is edited above it, and means it can tell the
    //    reader-parity `today` in VacationSettlementService apart from the UNRELATED
    //    `var today = CopenhagenToday();` in its supersession due-check.

    private const string YearOverviewConsumerAnchor =
        "userAgreementCodeRepo.GetByUserIdAtAsync(employeeId, today, ct)";

    private const string SettlementConsumerAnchor =
        "_agreementCodeRepo.GetByUserIdAtAsync(employeeId, today, ct)";

    // ── The derivation each site is REQUIRED to use. Written as exact source text so a revert to
    //    any other expression — the pre-S142 UTC day, a hand-rolled offset, DateTime.Today — fails
    //    with the offending text quoted back.

    private const string YearOverviewRequiredDerivation = "CopenhagenBusinessDate.Today(timeProvider)";

    private const string SettlementRequiredDerivation = "CopenhagenToday()";

    /// <summary>The settlement file's private adapter, asserted verbatim so the indirection in
    /// <see cref="SettlementRequiredDerivation"/> cannot quietly become something else.</summary>
    private const string SettlementHelperDeclaration =
        "private DateOnly CopenhagenToday() => CopenhagenBusinessDate.Today(_timeProvider);";

    /// <summary>The namespace whose <c>CopenhagenBusinessDate</c> this test evaluates.</summary>
    private const string SharedKernelCalendarUsing = "using StatsTid.SharedKernel.Calendar;";

    /// <summary><c>var today = &lt;expression&gt;;</c> — the word boundary after <c>today</c> keeps
    /// it from matching <c>var todayAgreementCode = …</c> on the following line.</summary>
    private static readonly Regex TodayAssignment =
        new(@"\bvar\s+today\s*=\s*(?<expr>[^;]+);", RegexOptions.Compiled);

    /// <summary>
    /// The pre-S142 shape: a CALENDAR DAY derived from the UTC instant. Deliberately narrow — it
    /// matches <c>DateOnly.FromDateTime(… GetUtcNow() …)</c> and nothing else, so a legitimate
    /// INSTANT read (an audit timestamp, an outbox ordering stamp — which must stay UTC) is not
    /// caught by a guard that has no business touching it.
    /// </summary>
    private static readonly Regex UtcCalendarDayDerivation =
        new(@"DateOnly\.FromDateTime\([^;]*GetUtcNow\(\)", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// A <c>using</c> ALIAS that would rebind the <c>CopenhagenBusinessDate</c> identifier to some
    /// other type — the one way facts 1–2 could be textually satisfied while the code called
    /// something else entirely.
    /// </summary>
    private static readonly Regex CopenhagenBusinessDateAlias =
        new(@"using\s+CopenhagenBusinessDate\s*=", RegexOptions.Compiled);

    // ════════════════════════════════════════════════════════════════════════
    // 1. THE PARITY PIN.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>The headline fact.</b> Both parity sites derive their business day from the Copenhagen
    /// calendar, and that derivation — evaluated once, at the summer instant where the two
    /// calendars disagree — yields the literal Danish day 2026-07-16.
    ///
    /// <para>2026-07-15 22:30Z is 2026-07-16 00:30 in Copenhagen (CEST, UTC+02:00): the Danish
    /// calendar has rolled over to the 16th while the UTC calendar still reads the 15th. The
    /// expected day below is that hand-computed 16th, written as a literal — NOT obtained by
    /// calling the helper the test is validating, which would assert only that the helper agrees
    /// with itself.</para>
    ///
    /// <para><b>RED before the S142 change:</b> both sites read
    /// <c>DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)</c>, so the first assertion
    /// fails naming the file and quoting the UTC expression it found.</para>
    /// </summary>
    [Fact]
    public void BothParitySites_DeriveToday_FromTheCopenhagenBusinessDay()
    {
        var yearOverviewDerivation = ExtractTodayDerivation(YearOverviewFile, YearOverviewConsumerAnchor);
        var settlementDerivation = ExtractTodayDerivation(SettlementFile, SettlementConsumerAnchor);

        Assert.True(
            yearOverviewDerivation == YearOverviewRequiredDerivation,
            ParityFailure(
                site: "census row 12 — the year-overview reader",
                file: YearOverviewFile,
                expected: YearOverviewRequiredDerivation,
                actual: yearOverviewDerivation));

        Assert.True(
            settlementDerivation == SettlementRequiredDerivation,
            ParityFailure(
                site: "census row 53 — the settlement pass's D9 reader-parity operand",
                file: SettlementFile,
                expected: SettlementRequiredDerivation,
                actual: settlementDerivation));

        // The day BOTH sites therefore produce, at an instant where UTC and Copenhagen disagree.
        // Asserted against the literal 16th, so this still fails if the shared helper is the thing
        // that regresses — the case a site-against-site comparison would sail straight past.
        var divergentInstant = BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen;
        var sharedDay = CopenhagenBusinessDate.Today(new FixedTimeProvider(divergentInstant));

        Assert.True(
            sharedDay == new DateOnly(2026, 7, 16),
            $"At {divergentInstant:yyyy-MM-dd HH:mm}Z the Copenhagen calendar day is 2026-07-16 " +
            $"(CEST, UTC+02:00 ⇒ local 2026-07-16 00:30), but the shared derivation both parity " +
            $"sites use returned {sharedDay:yyyy-MM-dd}. The UTC calendar day at this instant is " +
            "2026-07-15 — if that is what came back, the shared helper has regressed to the UTC " +
            "day and BOTH the year-overview screen and the vacation settlement engine are now a " +
            "day early for anyone working after Danish midnight.");
    }

    /// <summary>
    /// The settlement site reaches the shared derivation through one private adapter
    /// (<c>CopenhagenToday()</c>). This pins that adapter verbatim, so the indirection cannot
    /// become a second, divergent implementation while fact 1 still reads as satisfied.
    /// </summary>
    [Fact]
    public void SettlementHelper_DelegatesToTheSharedCopenhagenBusinessDate()
    {
        var executable = ExecutableTextOf(SettlementFile);

        Assert.True(
            executable.Contains(SettlementHelperDeclaration, StringComparison.Ordinal),
            $"'{SettlementFile}' no longer declares the adapter exactly as:{Environment.NewLine}" +
            $"    {SettlementHelperDeclaration}{Environment.NewLine}" +
            "The census-row-53 site derives its business day by calling this adapter, so a changed " +
            "body silently changes which calendar the settlement engine settles against — without " +
            "touching the call site the other assertions guard. If the adapter is being renamed or " +
            "reshaped, update SettlementRequiredDerivation and SettlementHelperDeclaration together, " +
            "and keep both sites on StatsTid.SharedKernel.Calendar.CopenhagenBusinessDate.Today.");
    }

    /// <summary>
    /// Both files bind <c>CopenhagenBusinessDate</c> to <c>StatsTid.SharedKernel.Calendar</c> — the
    /// type this test evaluates against literals — and neither aliases the name to something else.
    /// This is the link that turns "the source text says CopenhagenBusinessDate.Today" into "the
    /// compiled code calls the function asserted below".
    /// </summary>
    [Theory]
    [InlineData(YearOverviewFile)]
    [InlineData(SettlementFile)]
    public void BothParityFiles_BindCopenhagenBusinessDate_ToTheSharedKernel(string relativePath)
    {
        var source = ReadSource(relativePath);

        Assert.True(
            source.Contains(SharedKernelCalendarUsing, StringComparison.Ordinal),
            $"'{relativePath}' no longer has `{SharedKernelCalendarUsing}`. Its CopenhagenBusinessDate " +
            "reference would then resolve to some other type (or not compile), and the literal-pinned " +
            "guarantee this test provides would no longer apply to this file.");

        Assert.False(
            CopenhagenBusinessDateAlias.IsMatch(source),
            $"'{relativePath}' aliases the CopenhagenBusinessDate identifier with a `using … =` " +
            "directive. The parity guard asserts source TEXT; an alias would let that text keep " +
            "reading correctly while the code called a different implementation. Remove the alias.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // 2. The literal-pinned behaviour of the shared derivation, at all three
    //    wave-1 boundary instants.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The derivation both parity sites use, at each of the three canonical instants from
    /// <see cref="BoundaryInstants"/>, against hand-computed literal days.
    ///
    /// <para>Three instants rather than one because three DIFFERENT wrong implementations are
    /// plausible here and no single instant separates all of them: a no-conversion (raw UTC day)
    /// implementation, a hardcoded <c>+01:00</c> (winter-only) offset, and a hardcoded
    /// <c>+02:00</c> (summer-only) offset. The summer instant kills the first two; the
    /// calendars-agree winter instant kills the third. Each instant's own doc comment on
    /// <see cref="BoundaryInstants"/> records which wrong implementation it exists to fail.</para>
    ///
    /// <para>All three expected values are literals. None is computed by calling
    /// <c>CopenhagenBusinessDate.Today</c>, by adding an offset to the instant, or by any other
    /// route that would re-derive the answer from the code under test.</para>
    /// </summary>
    [Theory]
    // Summer, 22:30Z: Copenhagen is CEST (+02:00) ⇒ local 2026-07-16 00:30 ⇒ the 16th.
    // UTC still reads the 15th, and a fixed +01:00 lands on 23:30 — still the 15th. Both fail here.
    [InlineData(2026, 7, 15, 22, 30, 2026, 7, 16)]
    // Winter, 23:30Z: Copenhagen is CET (+01:00) ⇒ local 2026-01-16 00:30 ⇒ the 16th.
    // A raw UTC day reads the 15th and fails; a fixed +01:00 happens to be right here, which is
    // exactly why the third row exists.
    [InlineData(2026, 1, 15, 23, 30, 2026, 1, 16)]
    // Winter, 22:30Z: Copenhagen is CET (+01:00) ⇒ local 2026-01-15 23:30 ⇒ still the 15th, and
    // UTC agrees. A fixed +02:00 would roll to the 16th an hour early and fail only here.
    [InlineData(2026, 1, 15, 22, 30, 2026, 1, 15)]
    public void SharedDerivation_AtEachBoundaryInstant_EqualsTheLiteralDanishDay(
        int utcYear, int utcMonth, int utcDay, int utcHour, int utcMinute,
        int expectedYear, int expectedMonth, int expectedDay)
    {
        var instant = new DateTimeOffset(utcYear, utcMonth, utcDay, utcHour, utcMinute, 0, TimeSpan.Zero);
        var expected = new DateOnly(expectedYear, expectedMonth, expectedDay);

        var actual = CopenhagenBusinessDate.Today(new FixedTimeProvider(instant));

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// The inline data above must stay the instants wave 1 centralised, not a drifted private copy.
    /// xUnit <c>[InlineData]</c> takes compile-time constants only, so the instants are spelled out
    /// there; this fact is what keeps those literals honest against
    /// <see cref="BoundaryInstants"/>.
    /// </summary>
    [Fact]
    public void TheoryInstants_AreTheWave1BoundaryInstants()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 7, 15, 22, 30, 0, TimeSpan.Zero),
            BoundaryInstants.SummerEveningAlreadyTomorrowInCopenhagen);
        Assert.Equal(
            new DateTimeOffset(2026, 1, 15, 23, 30, 0, TimeSpan.Zero),
            BoundaryInstants.WinterEveningAlreadyTomorrowInCopenhagen);
        Assert.Equal(
            new DateTimeOffset(2026, 1, 15, 22, 30, 0, TimeSpan.Zero),
            BoundaryInstants.WinterEveningCalendarsStillAgree);
    }

    // ════════════════════════════════════════════════════════════════════════
    // 3. The anti-property.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Neither parity file derives a CALENDAR DAY from the UTC instant anywhere in its executable
    /// text. This is the direct anti-property of the S142 change: it fails the moment the pre-S142
    /// <c>DateOnly.FromDateTime(… GetUtcNow() …)</c> shape reappears in either file — including at
    /// a site the anchored assertions above do not cover.
    ///
    /// <para><b>Deliberately narrow.</b> It matches only a DAY derived from the instant. A bare
    /// <c>GetUtcNow()</c> is untouched, because instants — <c>created_at</c>, audit timestamps,
    /// outbox ordering — must stay UTC; a guard that banned them would push a correct pattern out
    /// of the codebase and break the audit chain, which is an inviolable invariant.</para>
    ///
    /// <para>Comments are stripped first: the S142 rationale comments at both sites QUOTE the old
    /// expression in prose to explain what changed and why, and that prose must not trip the guard
    /// (nor should a future author have to omit the explanation to keep a test green).</para>
    /// </summary>
    [Theory]
    [InlineData(YearOverviewFile)]
    [InlineData(SettlementFile)]
    public void NeitherParityFile_DerivesACalendarDay_FromTheUtcInstant(string relativePath)
    {
        var executable = ExecutableTextOf(relativePath);
        var match = UtcCalendarDayDerivation.Match(executable);

        Assert.False(
            match.Success,
            $"'{relativePath}' derives a calendar day from the UTC instant: " +
            $"`{Truncate(match.Value)}`. S142 moved every BUSINESS DATE in this file to the " +
            "Europe/Copenhagen calendar day (CopenhagenBusinessDate.Today) because all StatsTid " +
            "users are Danish and the UTC calendar still reads YESTERDAY between Danish midnight " +
            "and UTC midnight. If this is genuinely an INSTANT rather than a business date, it " +
            "should not be going through DateOnly at all — keep the DateTimeOffset.");
    }

    // ════════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the expression text of the <c>var today = …;</c> statement that feeds
    /// <paramref name="consumerAnchor"/> — i.e. the NEAREST such assignment preceding the anchor.
    /// Operates on comment-stripped text so a commented-out or quoted expression can never be
    /// mistaken for the live one.
    /// </summary>
    private static string ExtractTodayDerivation(string relativePath, string consumerAnchor)
    {
        var executable = ExecutableTextOf(relativePath);

        var anchorIndex = executable.IndexOf(consumerAnchor, StringComparison.Ordinal);
        Assert.True(
            anchorIndex >= 0,
            $"Could not find the consumer anchor `{consumerAnchor}` in '{relativePath}'. This guard " +
            "locates the parity site by the dated read it keys, not by line number. If that read was " +
            "renamed or restructured, update the anchor constant here — do not delete the guard: the " +
            "parity contract between the year-overview reader and the settlement pass is what it protects.");

        Assert.True(
            executable.IndexOf(consumerAnchor, anchorIndex + 1, StringComparison.Ordinal) < 0,
            $"The consumer anchor `{consumerAnchor}` appears more than once in '{relativePath}', so it " +
            "no longer identifies a unique site. Pick a narrower anchor.");

        var candidates = TodayAssignment.Matches(executable[..anchorIndex]);
        Assert.True(
            candidates.Count > 0,
            $"Found the consumer anchor in '{relativePath}' but no preceding `var today = …;` statement. " +
            "The parity site's business-day derivation appears to have been removed or renamed.");

        return candidates[^1].Groups["expr"].Value.Trim();
    }

    /// <summary>The file's source with all comments removed.</summary>
    private static string ExecutableTextOf(string relativePath) => StripComments(ReadSource(relativePath));

    private static string ReadSource(string relativePath)
    {
        var absolute = Path.Combine(
            LocateRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(
            File.Exists(absolute),
            $"Expected parity site '{relativePath}' to exist (looked at '{absolute}'). If the file " +
            "moved, update the path constant in this guard.");

        return File.ReadAllText(absolute);
    }

    /// <summary>
    /// Removes C# comments so prose is never scanned as code. Strips block comments, then any line
    /// whose first non-whitespace is <c>//</c> (covering <c>///</c> doc lines). Same conservative
    /// stripper as <c>AccrualMathSingleSourceTests</c>: it does not parse string literals, which is
    /// fine here — none of the patterns above appears inside a string literal in either file.
    /// </summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

        var kept = withoutBlocks
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal));

        return string.Join('\n', kept);
    }

    private static string ParityFailure(string site, string file, string expected, string actual) =>
        $"S142 parity ({site}, {file}): the business day must be derived as{Environment.NewLine}" +
        $"    var today = {expected};{Environment.NewLine}" +
        $"but the statement feeding the dated read is{Environment.NewLine}" +
        $"    var today = {actual};{Environment.NewLine}{Environment.NewLine}" +
        "These two sites are a PAIR. VacationSettlementService reproduces the year-overview " +
        "reader's chain byte-for-byte so the settled figure and the figure on screen agree; if " +
        "they derive different days they disagree for one to two hours every night (between " +
        "Danish midnight and UTC midnight), with the screen marking a period \"now\" that the " +
        "settlement engine considers past, or the reverse. Move both or neither — and note that " +
        "a test pinning only WithFixedToday(DateOnly) cannot detect this, because that harness " +
        "pins UTC midnight, the one moment where the two calendars agree.";

    private static string Truncate(string value) =>
        value.Length <= 120 ? value : value[..120] + "…";

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
