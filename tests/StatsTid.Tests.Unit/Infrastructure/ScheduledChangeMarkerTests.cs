using StatsTid.Infrastructure;

namespace StatsTid.Tests.Unit.Infrastructure;

/// <summary>
/// S141 / TASK-14116 (refinement B0, owner requirement 2026-09-11) — pins on the SCHEDULED-CHANGE
/// MARKER the three employee LIST reads now carry. Locally runnable: no DB, no Docker.
///
/// <para>
/// <b>What this is about, for a reader who is not the code's author.</b> Sprint 141 lets HR date an
/// employment change ahead — "she moves to 0.6 on 1 November". The owner then asked the question that
/// reframed the sprint: <i>should it not be visible to an HR employee looking at a page, that another
/// has scheduled a change?</i> Three screens could not answer it, because their responses had no
/// field for it: the organisation roster, the people-search overlay, and the person-reference chips.
/// They now each carry one date — the day the next scheduled change lands, or nothing at all.
/// </para>
///
/// <para>
/// <b>Why the tests look like text assertions rather than database assertions.</b> The behaviour
/// being protected lives in SQL, and executing SQL needs Postgres, which needs Docker, which is not
/// available on this machine. Two things are nonetheless genuinely falsifiable WITHOUT a database and
/// are the two things most likely to be got wrong by a later edit:
/// <list type="number">
///   <item><b>The definition of "scheduled" itself.</b> A zero-width row <c>[f, f)</c> covers no day
///     and is the trace a CANCELLED scheduled change leaves behind. Drop that exclusion and the
///     roster starts announcing changes that were deliberately called off — confidently wrong, which
///     is worse than silent. No database is needed to catch a predicate that has lost the clause.</item>
///   <item><b>That there is still only ONE definition.</b> The rule is written in
///     <see cref="EmploymentTimelineSql"/> and the three list reads splice it in. If someone
///     hand-writes a fourth copy, the copies drift and the roster and the profile page start
///     disagreeing about whether a change exists — a disagreement nobody would notice until an HR
///     user saw it.</item>
/// </list>
/// Whether the composed statements PARSE and return the right rows is Docker-gated and is verified in
/// CI by the endpoint contract suites, not here.
/// </para>
/// </summary>
public sealed class ScheduledChangeMarkerTests
{
    // ── The rule: what counts as a scheduled change ──────────────────────────────────────────────

    /// <summary>
    /// A CANCELLED scheduled change must not be reported. Retiring a scheduled row leaves a
    /// zero-width interval <c>[f, f)</c> behind (S141 B4); it covers no day, so it is not a change
    /// that is coming. This pin fails the moment the exclusion is dropped from the canonical
    /// predicate.
    /// </summary>
    [Fact]
    public void ScheduledRowPredicate_ExcludesZeroWidthRows_SoACancelledChangeIsNeverAnnounced()
    {
        Assert.Contains(
            "s.effective_to IS NULL OR s.effective_to > s.effective_from",
            EmploymentTimelineSql.ScheduledRowPredicate);
    }

    /// <summary>
    /// "Scheduled" means STRICTLY after today. A row starting today is in force, not scheduled —
    /// marking it would tell HR a change is coming when they are already looking at it.
    /// </summary>
    [Fact]
    public void ScheduledRowPredicate_IsStrictlyAfterToday_NotOnOrAfter()
    {
        Assert.Contains("s.effective_from > @today", EmploymentTimelineSql.ScheduledRowPredicate);
        Assert.DoesNotContain("s.effective_from >= @today", EmploymentTimelineSql.ScheduledRowPredicate);
    }

    /// <summary>
    /// The marker reads BOTH dated employment timelines. It answers "is it safe to act on this row",
    /// not "does the job title change" — and the edit drawer writes the agreement code on every save,
    /// dated, so a scheduled agreement change is as much a reason to look before editing. Covering
    /// only the profile timeline would leave the owner's defect intact one field over.
    /// </summary>
    [Fact]
    public void EarliestScheduledChangeLateral_CoversBothProfileAndAgreementTimelines()
    {
        var sql = EmploymentTimelineSql.EarliestScheduledChangeLateral;
        Assert.Contains("FROM employee_profiles s", sql);
        Assert.Contains("FROM user_agreement_codes s", sql);
        // Both branches apply the SAME rule — spliced, not retyped.
        Assert.Equal(2, CountOccurrences(sql, EmploymentTimelineSql.ScheduledRowPredicate));
    }

    // ── The shape: it cannot multiply rows ───────────────────────────────────────────────────────

    /// <summary>
    /// The lateral AGGREGATES. <c>MIN()</c> over an empty set is one row containing NULL, so the join
    /// yields exactly one row per outer row BY CONSTRUCTION — it cannot fan out even on malformed
    /// data. That matters most in the people-search read, whose total is counted in a separate CTE
    /// BEFORE this join: a fan-out there would ship more rows than the count travelling with them.
    /// An <c>ORDER BY … LIMIT 1</c> would give the same answer on well-formed data and a corrupted
    /// page on bad data, which is why this pin names the aggregate specifically.
    /// </summary>
    [Fact]
    public void EarliestScheduledChangeLateral_AggregatesSoItCannotFanOutAPagedResult()
    {
        var sql = EmploymentTimelineSql.EarliestScheduledChangeLateral;
        Assert.Contains("SELECT MIN(x.effective_from) AS scheduled_change_from", sql);
        Assert.Contains("ON TRUE", sql);
    }

    /// <summary>
    /// The splice contract: the fragment correlates on <c>u.user_id</c>, binds the injected server day
    /// as <c>@today</c> (never <c>CURRENT_DATE</c> — QUAL-157's fixed-clock seam, or a date-sensitive
    /// test could not pin anything), is newline-padded at both ends so it can sit between two raw
    /// string literals, and has balanced parentheses.
    /// </summary>
    [Fact]
    public void EarliestScheduledChangeLateral_HonoursItsSpliceContract()
    {
        var sql = EmploymentTimelineSql.EarliestScheduledChangeLateral;
        Assert.StartsWith("\n", sql);
        Assert.EndsWith("\n", sql);
        Assert.Contains("s.employee_id = u.user_id", sql);
        Assert.Contains("s.user_id = u.user_id", sql);
        Assert.Contains(") sched ON TRUE", sql);
        Assert.DoesNotContain("CURRENT_DATE", sql);
        Assert.Equal(CountOccurrences(sql, "("), CountOccurrences(sql, ")"));
    }

    // ── Single definition: the three list reads splice it, they do not retype it ──────────────────

    /// <summary>
    /// All three list reads that display an employment value use the ONE shared fragment. Stated as a
    /// source-level guard because the SQL is a private literal inside the repository — the same
    /// approach the accrual-math and planner-bypass guards take. If a fourth copy of the rule is
    /// hand-written here, this is what catches it before the copies drift apart.
    /// </summary>
    [Fact]
    public void AllThreeListReads_SpliceTheSharedFragment_RatherThanRetypingTheRule()
    {
        var source = File.ReadAllText(Path.Combine(
            LocateRepoRoot(), "src", "Infrastructure", "StatsTid.Infrastructure",
            "ApprovalPeriodRepository.cs"));

        // One splice each: the organisation roster, the person-reference resolver, the people search.
        Assert.Equal(3, CountOccurrences(
            source, "EmploymentTimelineSql.EarliestScheduledChangeLateral"));

        // And nobody has quietly re-typed the rule alongside the splice.
        Assert.Equal(0, CountOccurrences(source, EmploymentTimelineSql.ScheduledRowPredicate));
    }

    /// <summary>
    /// The two SINGLE-EMPLOYEE reads wave 1 added (the profile page / edit drawer and the agreement
    /// side) still state the identical predicate. They spell it out inline because those statements
    /// also SELECT the scheduled row's VALUES, which the list reads deliberately do not — so the text
    /// is duplicated ON PURPOSE, and this pin is what makes that duplication safe: change the rule in
    /// <see cref="EmploymentTimelineSql"/> without changing them and the build goes red here rather
    /// than the roster and the profile page silently disagreeing in front of an HR user.
    /// </summary>
    [Theory]
    [InlineData("EmployeeProfileRepository.cs")]
    [InlineData("UserAgreementCodeRepository.cs")]
    public void WaveOneSingleEmployeeReads_StateTheIdenticalRule(string fileName)
    {
        var source = File.ReadAllText(Path.Combine(
            LocateRepoRoot(), "src", "Infrastructure", "StatsTid.Infrastructure", fileName));

        // The wave-1 statements are multi-line and column-aligned, so compare on the SQL tokens
        // rather than on whitespace: both halves of the rule must be present, correlated to the
        // scheduled-row alias `s`.
        var normalized = Normalize(source);
        Assert.Contains("s.effective_from > @today", normalized);
        Assert.Contains("(s.effective_to IS NULL OR s.effective_to > s.effective_from)", normalized);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var i = haystack.IndexOf(needle, StringComparison.Ordinal);
        while (i >= 0)
        {
            count++;
            i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal);
        }
        return count;
    }

    /// <summary>Collapse every run of whitespace to a single space, so a column-aligned SQL
    /// statement and a one-line predicate compare on their tokens.</summary>
    private static string Normalize(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

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
