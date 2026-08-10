using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Security;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using ReportingLineModel = StatsTid.SharedKernel.Models.ReportingLine;

namespace StatsTid.Tests.Regression.Outbox;

// ═══════════════════════════════════════════════════════════════════════════════════════════════
//  S127 / TASK-12700 — the AC-2 characterization baseline for the allocation predicate.
//
//  Placed in Outbox/ beside AllocationGateTests.cs deliberately: that file is encoding 5 of the
//  predicate (refinement §3.8) — a hand-written MIRROR of the gate arithmetic that would stay green
//  if the gate were deleted outright. This file is what replaces trust in it. Nothing here computes
//  the rule; everything here drives the three PRODUCTION encodings through their HTTP surfaces and
//  compares them to verdicts written out by hand from the domain rule.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Shared container + booted API for <see cref="AllocationPredicateCharacterizationTests"/>. ONE
/// Postgres testcontainer and ONE <see cref="StatsTidWebApplicationFactory"/> for the whole class
/// (xUnit <c>IClassFixture</c>) — every case seeds its own employee/leader/project/period under a
/// case-unique id prefix, so the cases are isolated without a per-case container.
/// </summary>
public sealed class AllocationPredicateCharacterizationFixture : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public StatsTidWebApplicationFactory Factory { get; private set; } = null!;
    public DbConnectionFactory Db { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        ConnectionString = _harness.ConnectionString;
        Factory = new StatsTidWebApplicationFactory(ConnectionString);
        Db = new DbConnectionFactory(ConnectionString);
    }

    public async Task DisposeAsync()
    {
        Factory?.Dispose();
        if (_harness is not null)
            await _harness.DisposeAsync();
    }
}

/// <summary>
/// <b>S127 / TASK-12700 — AC-2: a pre-consolidation characterization baseline of the allocation
/// predicate, captured BEFORE the five encodings are collapsed into one.</b>
///
/// <para><b>Why this exists and why it had to run first.</b> Sprint 127 collapses five hand-written
/// copies of "is this day's worked total equal to its distributed total" into one shared predicate.
/// A baseline captured AFTER that collapse compares the shared predicate to itself and proves
/// nothing. So this is an explicit, Orchestrator-authorized exception to the Test &amp; QA agent's
/// normal run-last constraint (<c>docs/AGENTS.md:37</c>) — a characterization capture, whose whole
/// value is that it predates the change.</para>
///
/// <para><b>What is characterized.</b> The three PRODUCTION encodings, each an inline expression
/// inside an endpoint handler — there is no callable function to unit-test, so each case is driven
/// through the encoding's own HTTP surface:</para>
/// <list type="number">
///   <item><b>The gate</b> — <c>ApprovalEndpoints.cs:1488</c>, reached via
///     <c>POST /api/approval/{periodId}/employee-approve</c>. Balanced ⇒ <c>200</c>; imbalanced ⇒
///     <c>422 {kind:"allocation", unbalancedDays:[…]}</c>.</item>
///   <item><b>Team-overview <c>hasWarning</c></b> — <c>:1109</c>, via
///     <c>GET /api/approval/team-overview?year=&amp;month=</c>.</item>
///   <item><b>Allocation-breakdown <c>hasAllocationImbalance</c></b> — <c>:1284</c>, via
///     <c>GET /api/approval/{employeeId}/allocation-breakdown?year=&amp;month=</c>.</item>
/// </list>
///
/// <para><b>THE RULE, STATED INDEPENDENTLY.</b> The expected verdicts in the table below were
/// written out from this statement of the rule, not read back off the implementation:</para>
/// <list type="bullet">
///   <item><c>worked(day)</c> = the hours the employee recorded as time at work = Σ(interval end −
///     interval start) + manually added hours, from <c>work_time_projection</c>. No row for the day
///     ⇒ 0.</item>
///   <item><c>distributed(day)</c> = Σ hours of ordinary-work registrations that name a project =
///     <c>time_entries_projection</c> rows with <c>activity_type = 'NORMAL'</c> AND
///     <c>task_id IS NOT NULL</c>. An absence-type row does not count however many hours it carries;
///     neither does an ordinary row with no project ("Ikke fordelt").</item>
///   <item>Both totals are expressed in <b>whole hundredths of an hour</b> (rounded to 2 decimals)
///     BEFORE they are compared — that is the granularity the product displays and settles in.</item>
///   <item>The day is <b>balanced</b> iff the two rounded totals are equal. Because both operands
///     are whole hundredths, the smallest real difference is 0.01.</item>
///   <item>When imbalanced, the direction is <b>"under"</b> when worked &gt; distributed (the
///     employee has worked more than they have distributed onto projects) and <b>"over"</b>
///     otherwise.</item>
///   <item>A period (gate) or a month (both read surfaces) is balanced iff EVERY day carrying any
///     worked or any distributed hours is balanced. A day with neither is not compared at all.</item>
/// </list>
///
/// <para><b>THE VALUE TABLE.</b> Each row is asserted against all three surfaces.</para>
/// <code>
///  #  case id                       worked (raw)          distributed (raw)                 w₂     d₂    |Δ₂|   verdict     dir
///  ─  ────────────────────────────  ────────────────────  ────────────────────────────────  ─────  ─────  ─────  ──────────  ─────
///  1  C1_exact_match                manual 7.4000         NORMAL 7.4000 @project            7.40   7.40   0.00   BALANCED    —
///  2  C2_one_oere_short             manual 7.4000         NORMAL 7.3900 @project            7.40   7.39   0.01   IMBALANCED  under
///  3  C3_one_oere_over              manual 7.4000         NORMAL 7.4100 @project            7.40   7.41   0.01   IMBALANCED  over
///  4  C4_interval_7point4_vs_7point40
///                                   intervals 08:00-15:24 NORMAL 7.4000 @project            7.40   7.40   0.00   BALANCED    —
///                                   (26 640 s ⇒ 7.4)      (stored NUMERIC(8,4) ⇒ 7.4000)
///  5  C5_sub_oere_noise             manual 7.4151         NORMAL 7.4249 @project            7.42   7.42   0.00   BALANCED    —
///  6  C6_no_project_named           manual 7.4000         NORMAL 7.4000, task_id NULL       7.40   0.00   7.40   IMBALANCED  under
///  7  C7_distributed_without_work   (no work_time row)    NORMAL 5.0000 @project            0.00   5.00   5.00   IMBALANCED  over
///  8  C8_absence_row_not_distributed
///                                   manual 7.4000         NORMAL 3.0000 @project +          7.40   3.00   4.40   IMBALANCED  under
///                                                         VACATION 4.4000 @project
/// </code>
///
/// <para>Derivations worth spelling out, because they are the ones a reader would otherwise take on
/// trust:</para>
/// <list type="bullet">
///   <item><b>#4 is the "7.40 vs 7.4" pair the AC asks for.</b> The two sides arrive by different
///     routes and at different decimal SCALES: worked is computed as 26 640 s ÷ 3600 = <c>7.4</c>
///     (scale 1), distributed is read from a <c>NUMERIC(8,4)</c> column as <c>7.4000</c> (scale 4).
///     They are the same VALUE, so the day is balanced.</item>
///   <item><b>#5 is the case that proves the rounding happens BEFORE the comparison.</b> The raw
///     values differ by <c>0.0098</c>, which is nearly twice the 0.005 tolerance — a naive
///     compare-then-tolerate would call this day imbalanced on all three surfaces. It is balanced
///     only because 7.4151 and 7.4249 both round to 7.42 first. If the collapse in TASK-12705 drops
///     the <c>Math.Round(…, 2)</c>, this row goes red on all three encodings. #5 is also the ONLY
///     row where the rounded totals differ from the raw ones — which is how it caught, at capture
///     time, that the allocation-breakdown reports its month-level <c>worked</c> / <c>allocated</c>
///     RAW and rounds only inside the per-day comparison. Both are pinned (see <c>Row</c>).</item>
///   <item><b>#6 replaces the "nothing distributed at all" case, which is unreachable.</b> A day with
///     worked hours and NO time entry at all fails the WORKDAY-COVERAGE check that sits above the
///     gate (<c>:1387-1444</c>) and 422s with <c>"Ikke alle arbejdsdage er dækket"</c> before the
///     allocation gate is ever evaluated — asserted, not assumed, by
///     <see cref="GateIsBelowCoverage_WorkedHoursWithNoRegistrationAtAll_IsRefusedByCoverageNotAllocation"/>.
///     The reachable form of "the employee distributed nothing" is an ordinary entry with no project
///     named: it satisfies coverage, and contributes zero to distributed.</item>
///   <item><b>#7 has no <c>work_time_projection</c> row at all</b>, which is how worked reaches 0
///     while the day still exists in the comparison (it enters through the distributed side).</item>
/// </list>
///
/// <para><b>THE FIXTURE, and why the gate is reachable.</b> The gate at <c>:1488</c> only runs once
/// the coverage check above it passes, and coverage demands a registration on EVERY expected workday
/// of the period. Each case therefore seeds a full-month <c>MONTHLY</c> period (2026-05) in which
/// every weekday EXCEPT the case day carries a full-day <c>VACATION</c> row in
/// <c>absences_projection</c>. Absences satisfy coverage and are read from a table neither the worked
/// map nor the distributed map touches, so those days contribute NOTHING to any of the three
/// encodings. The result is that all three surfaces — the period-scoped gate and the two
/// month-scoped reads — compare EXACTLY ONE day: the case day. That alignment is what makes the
/// falsifiability discriminator below bite in both directions.</para>
///
/// <para>Each case also creates its OWN <c>projects</c> row (a distributed entry must name a project)
/// under a case-unique <c>project_code</c>. This fixture deliberately shares nothing with
/// TASK-12701a's seeding; there is no dependency between the two tasks.</para>
///
/// <para><b>THE FALSIFIABILITY DISCRIMINATOR (AC-2).</b> This baseline was verified to go RED under
/// three INDEPENDENT single-operator inversions, each applied alone and then reverted:</para>
/// <list type="bullet">
///   <item>invert <c>:1488</c> (<c>&lt;</c> → <c>&gt;</c>) — the balanced rows start returning 422 and
///     the imbalanced rows start returning 200.</item>
///   <item>invert <c>:1109</c> (<c>&gt;</c> → <c>&lt;</c>) — <c>hasWarning</c> flips on every row.</item>
///   <item>invert <c>:1284</c> (<c>&gt;</c> → <c>&lt;</c>) — <c>hasAllocationImbalance</c> flips on
///     every row.</item>
/// </list>
/// <para>Inverting the gate alone is NOT sufficient evidence and was not accepted as such: encodings
/// 2 and 3 are asserted by their own surfaces, and each inversion was run separately.</para>
///
/// <para><b>THE LIMIT OF THIS BASELINE — read this before citing it.</b> The gate at <c>:1488</c>
/// spells the predicate as <c>|Δ| &lt; tolerance ⇒ balanced</c> while the two read surfaces spell it
/// as <c>|Δ| &gt; tolerance ⇒ warn</c>. Those two differ ONLY at <c>|Δ| == 0.005</c> exactly. Both
/// sides of every comparison are <c>Math.Round(…, 2)</c> first, so every operand is a whole number of
/// hundredths and every difference is a whole number of hundredths: <c>0.005</c> is half a hundredth
/// and is <b>arithmetically unreachable</b>. This baseline therefore <b>cannot discriminate the
/// <c>&lt;</c>/<c>&gt;</c> strictness split between the encodings, and asserts nothing about it.</b> It
/// proves the collapse behaviour-preserving over the reachable input space; it proves nothing about
/// that boundary. <see cref="AllocationPredicateRoundingLimitTests"/> spells the unreachability
/// argument out as executable arithmetic over the comparison's OPERANDS, so the limit is written down
/// somewhere it can be checked rather than only asserted in prose.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AllocationPredicateCharacterizationTests
    : IClassFixture<AllocationPredicateCharacterizationFixture>
{
    private const string DevFallbackSigningKey = "StatsTid_Sprint3_DevKey_MustBeAtLeast32BytesLong!";
    private const string Org = "STY02";

    // The characterized month, and the single day inside it that every case varies.
    // 2026-05-04 is a Monday and is not a Danish public holiday (init.sql seeds only 14/24/25 May
    // 2026) — both facts are ASSERTED in the fixture rather than assumed, see SeedCaseAsync.
    private static readonly DateOnly MonthStart = new(2026, 5, 1);
    private static readonly DateOnly MonthEnd = new(2026, 5, 31);
    private static readonly DateOnly CaseDay = new(2026, 5, 4);

    private readonly AllocationPredicateCharacterizationFixture _fx;

    public AllocationPredicateCharacterizationTests(AllocationPredicateCharacterizationFixture fx)
        => _fx = fx;

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  The value table — expected verdicts written from the rule, NOT read from the implementation.
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>One registration in <c>time_entries_projection</c> on the case day.</summary>
    public sealed record Entry(decimal Hours, string ActivityType, bool NamesProject);

    /// <summary>
    /// One row of the value table.
    ///
    /// <para><paramref name="ExpectedWorked"/> / <paramref name="ExpectedDistributed"/> are the
    /// hand-computed totals ROUNDED to hundredths — the values the predicate compares, and the values
    /// the gate echoes back in its <c>unbalancedDays</c> payload.
    /// <paramref name="ExpectedBalanced"/> and <paramref name="ExpectedDirection"/> are the verdict
    /// derived from them by the rule stated in the class summary.</para>
    ///
    /// <para><paramref name="RawWorked"/> / <paramref name="RawDistributed"/> are the same totals
    /// UNROUNDED. They exist because the allocation-breakdown response reports its month-level
    /// <c>worked</c> / <c>allocated</c> figures raw, and rounds only inside the per-day comparison —
    /// so the two differ on C5 and nowhere else. Pinning both is what makes that separation part of
    /// the characterized behaviour: routing the month totals through the shared per-day predicate
    /// during the collapse would change them.</para>
    /// </summary>
    public sealed record Row(
        string Description,
        decimal? ManualHours,
        string? Intervals,
        Entry[] Entries,
        decimal ExpectedWorked,
        decimal ExpectedDistributed,
        decimal RawWorked,
        decimal RawDistributed,
        bool ExpectedBalanced,
        string? ExpectedDirection);

    private static readonly IReadOnlyDictionary<string, Row> Table = new Dictionary<string, Row>
    {
        ["C1_exact_match"] = new(
            "worked and distributed are the same value — the ordinary balanced day",
            ManualHours: 7.4000m, Intervals: null,
            Entries: new[] { new Entry(7.4000m, "NORMAL", NamesProject: true) },
            ExpectedWorked: 7.40m, ExpectedDistributed: 7.40m,
            RawWorked: 7.4000m, RawDistributed: 7.4000m,
            ExpectedBalanced: true, ExpectedDirection: null),

        ["C2_one_oere_short"] = new(
            "one hundredth of an hour LESS distributed than worked — the smallest real mismatch",
            ManualHours: 7.4000m, Intervals: null,
            Entries: new[] { new Entry(7.3900m, "NORMAL", NamesProject: true) },
            ExpectedWorked: 7.40m, ExpectedDistributed: 7.39m,
            RawWorked: 7.4000m, RawDistributed: 7.3900m,
            ExpectedBalanced: false, ExpectedDirection: "under"),

        ["C3_one_oere_over"] = new(
            "one hundredth MORE distributed than worked — the same magnitude, other direction",
            ManualHours: 7.4000m, Intervals: null,
            Entries: new[] { new Entry(7.4100m, "NORMAL", NamesProject: true) },
            ExpectedWorked: 7.40m, ExpectedDistributed: 7.41m,
            RawWorked: 7.4000m, RawDistributed: 7.4100m,
            ExpectedBalanced: false, ExpectedDirection: "over"),

        ["C4_interval_7point4_vs_7point40"] = new(
            "same value, different route and different decimal scale: 26 640 s ÷ 3600 = 7.4 worked "
            + "against a NUMERIC(8,4) 7.4000 distributed",
            ManualHours: null, Intervals: """[{"start":"08:00","end":"15:24"}]""",
            Entries: new[] { new Entry(7.4000m, "NORMAL", NamesProject: true) },
            ExpectedWorked: 7.40m, ExpectedDistributed: 7.40m,
            RawWorked: 7.4m, RawDistributed: 7.4000m,
            ExpectedBalanced: true, ExpectedDirection: null),

        ["C5_sub_oere_noise"] = new(
            "raw values 0.0098 apart — nearly TWICE the tolerance — but both round to 7.42, so the "
            + "day is balanced. This row fails if the rounding stops preceding the comparison",
            ManualHours: 7.4151m, Intervals: null,
            Entries: new[] { new Entry(7.4249m, "NORMAL", NamesProject: true) },
            ExpectedWorked: 7.42m, ExpectedDistributed: 7.42m,
            RawWorked: 7.4151m, RawDistributed: 7.4249m,
            ExpectedBalanced: true, ExpectedDirection: null),

        ["C6_no_project_named"] = new(
            "an ordinary registration that names NO project is not distributed hours — it satisfies "
            + "workday coverage and contributes nothing (the reachable 'distributed nothing' case)",
            ManualHours: 7.4000m, Intervals: null,
            Entries: new[] { new Entry(7.4000m, "NORMAL", NamesProject: false) },
            ExpectedWorked: 7.40m, ExpectedDistributed: 0.00m,
            RawWorked: 7.4000m, RawDistributed: 0m,
            ExpectedBalanced: false, ExpectedDirection: "under"),

        ["C7_distributed_without_work"] = new(
            "project hours on a day with no recorded work time at all — worked is 0 and the day "
            + "enters the comparison through the distributed side",
            ManualHours: null, Intervals: null,
            Entries: new[] { new Entry(5.0000m, "NORMAL", NamesProject: true) },
            ExpectedWorked: 0.00m, ExpectedDistributed: 5.00m,
            RawWorked: 0m, RawDistributed: 5.0000m,
            ExpectedBalanced: false, ExpectedDirection: "over"),

        ["C8_absence_row_not_distributed"] = new(
            "an absence-type registration is not distributed hours even when it names a project — "
            + "only the 3.00 NORMAL row counts against 7.40 worked",
            ManualHours: 7.4000m, Intervals: null,
            Entries: new[]
            {
                new Entry(3.0000m, "NORMAL", NamesProject: true),
                new Entry(4.4000m, "VACATION", NamesProject: true),
            },
            ExpectedWorked: 7.40m, ExpectedDistributed: 3.00m,
            RawWorked: 7.4000m, RawDistributed: 3.0000m,
            ExpectedBalanced: false, ExpectedDirection: "under"),
    };

    public static IEnumerable<object[]> CaseIds => Table.Keys.Select(k => new object[] { k });

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  The baseline
    // ════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Drives one table row through all THREE encodings. The two read surfaces are queried BEFORE the
    /// gate is posted, because a passing gate transitions the period to <c>EMPLOYEE_APPROVED</c>.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseIds))]
    public async Task Characterizes_AllThreeEncodings(string caseId)
    {
        var row = Table[caseId];
        var ids = await SeedCaseAsync(caseId, row);

        // ── Encoding 2: team-overview hasWarning (ApprovalEndpoints.cs:1109) ──
        var hasWarning = await ReadHasWarningAsync(ids.Manager, ids.Employee);
        Assert.Equal(!row.ExpectedBalanced, hasWarning);

        // ── Encoding 3: allocation-breakdown hasAllocationImbalance (:1284) ──
        var breakdown = await ReadBreakdownAsync(ids.Manager, ids.Employee);
        Assert.Equal(!row.ExpectedBalanced, breakdown.GetProperty("hasAllocationImbalance").GetBoolean());

        // The month totals corroborate that the fixture really did produce the intended inputs —
        // if a seed row silently failed to land, these go red before the verdict does, so a green
        // verdict can never be the accident of an empty month. These are the RAW sums: the breakdown
        // rounds only inside its per-day comparison, which is visible on C5 and nowhere else.
        Assert.Equal(row.RawWorked, breakdown.GetProperty("worked").GetDecimal());
        Assert.Equal(row.RawDistributed, breakdown.GetProperty("allocated").GetDecimal());

        // ── Encoding 1: the employee-approve gate (:1488) ──
        var gate = await PostEmployeeApproveAsync(ids.Employee, ids.PeriodId);
        var raw = await gate.Content.ReadAsStringAsync();

        if (row.ExpectedBalanced)
        {
            // A 422 here would most likely be the COVERAGE arm above the gate, not the gate itself,
            // so surface the body rather than a bare status mismatch.
            Assert.True(gate.StatusCode == HttpStatusCode.OK,
                $"expected 200 for balanced case {caseId}, got {(int)gate.StatusCode}: {raw}");
            var okBody = JsonDocument.Parse(raw).RootElement;
            Assert.Equal("EMPLOYEE_APPROVED", okBody.GetProperty("status").GetString());
        }
        else
        {
            Assert.True(gate.StatusCode == HttpStatusCode.UnprocessableEntity,
                $"expected 422 for imbalanced case {caseId}, got {(int)gate.StatusCode}: {raw}");
            var gateBody = JsonDocument.Parse(raw).RootElement;
            Assert.Equal("allocation", gateBody.GetProperty("kind").GetString());

            // Assert the EXACT reported day set, not merely that the status was 422. The fixture
            // fills the rest of the month with absences precisely so that the case day is the ONLY
            // day the gate may report; asserting only the status would stay green under an inverted
            // gate that rejected the filler days instead of the case day.
            var days = gateBody.GetProperty("unbalancedDays").EnumerateArray().ToList();
            var day = Assert.Single(days);
            Assert.Equal(CaseDay.ToString("yyyy-MM-dd"), day.GetProperty("date").GetString());
            Assert.Equal(row.ExpectedWorked, day.GetProperty("worked").GetDecimal());
            Assert.Equal(row.ExpectedDistributed, day.GetProperty("allocated").GetDecimal());
            Assert.Equal(row.ExpectedDirection, day.GetProperty("direction").GetString());
        }
    }

    /// <summary>
    /// Pins the REACHABILITY precondition the whole table depends on: the allocation gate sits BELOW
    /// the workday-coverage check, so a day carrying worked hours and no registration at all never
    /// reaches the allocation arm — it is refused by coverage first.
    ///
    /// <para>This is why the value table has no "distributed nothing at all" row and uses C6 (an
    /// ordinary entry naming no project) as the reachable form instead. Stating that in a comment
    /// would leave the table's shape resting on an unchecked claim; this asserts it. It also pins an
    /// ordering TASK-12705 must not disturb: were the collapse to hoist the shared predicate above
    /// coverage, this case would start returning <c>kind:"allocation"</c> and go red.</para>
    /// </summary>
    [Fact]
    public async Task GateIsBelowCoverage_WorkedHoursWithNoRegistrationAtAll_IsRefusedByCoverageNotAllocation()
    {
        // Worked hours on the case day, and NOTHING else on it — no time entry, no absence. The rest
        // of the month is covered exactly as every table row covers it.
        var row = new Row(
            "worked hours, no registration of any kind on the case day",
            ManualHours: 7.4000m, Intervals: null,
            Entries: Array.Empty<Entry>(),
            ExpectedWorked: 7.40m, ExpectedDistributed: 0.00m,
            RawWorked: 7.4000m, RawDistributed: 0m,
            ExpectedBalanced: false, ExpectedDirection: "under");
        var ids = await SeedCaseAsync("X_uncovered_day", row);

        var rsp = await PostEmployeeApproveAsync(ids.Employee, ids.PeriodId);
        var raw = await rsp.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rsp.StatusCode);

        var body = JsonDocument.Parse(raw).RootElement;
        // The COVERAGE shape, not the allocation shape — and the day named is the case day.
        Assert.False(body.TryGetProperty("kind", out _),
            $"expected the coverage refusal, got an allocation refusal: {raw}");
        Assert.Equal("Ikke alle arbejdsdage er dækket", body.GetProperty("error").GetString());
        var missing = body.GetProperty("missingDays").EnumerateArray().Select(d => d.GetString()).ToList();
        Assert.Equal(new[] { CaseDay.ToString("yyyy-MM-dd") }, missing);
    }

    /// <summary>
    /// The three encodings agree with each other on every row of the table — the property TASK-12705's
    /// collapse must preserve, stated directly rather than inferred from the per-row assertions above.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseIds))]
    public async Task ThreeEncodings_AgreeWithEachOther(string caseId)
    {
        var row = Table[caseId];
        var ids = await SeedCaseAsync(caseId + "_agree", row);

        var hasWarning = await ReadHasWarningAsync(ids.Manager, ids.Employee);
        var hasImbalance = (await ReadBreakdownAsync(ids.Manager, ids.Employee))
            .GetProperty("hasAllocationImbalance").GetBoolean();
        var gateBlocked = (await PostEmployeeApproveAsync(ids.Employee, ids.PeriodId)).StatusCode
            == HttpStatusCode.UnprocessableEntity;

        Assert.Equal(hasWarning, hasImbalance);
        Assert.Equal(hasWarning, gateBlocked);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  Fixture
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private sealed record CaseIdentities(string Employee, string Manager, Guid PeriodId);

    private async Task<CaseIdentities> SeedCaseAsync(string caseId, Row row)
    {
        // Case-unique ids: the container is shared across the class, so isolation comes from the
        // identifiers rather than from cleanup.
        var slug = "t12700_" + caseId.ToLowerInvariant();
        var emp = slug + "_e";
        var mgr = slug + "_m";
        var projectCode = ("PRJ_" + caseId).ToUpperInvariant();

        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();

        // The two facts the fixture depends on, ASSERTED rather than assumed: the case day must be a
        // weekday (so it is an expected workday the gate insists on covering) and must not be a
        // Danish public holiday (which would remove it from expected workdays entirely).
        Assert.False(CaseDay.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        await using (var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM danish_public_holidays WHERE holiday_date = @d", conn))
        {
            cmd.Parameters.AddWithValue("d", CaseDay);
            Assert.Equal(0L, (long)(await cmd.ExecuteScalarAsync())!);
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO users (user_id, username, password_hash, display_name, email,
                               primary_org_id, agreement_code, ok_version, is_active)
            VALUES (@emp, @emp, '$2a$11$fake', 'T12700 Emp', @empMail, @org, 'HK', 'OK24', TRUE),
                   (@mgr, @mgr, '$2a$11$fake', 'T12700 Mgr', @mgrMail, @org, 'HK', 'OK24', TRUE)
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", emp);
            cmd.Parameters.AddWithValue("mgr", mgr);
            cmd.Parameters.AddWithValue("empMail", emp + "@test.dk");
            cmd.Parameters.AddWithValue("mgrMail", mgr + "@test.dk");
            cmd.Parameters.AddWithValue("org", Org);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO role_assignments (user_id, role_id, org_id, scope_type, assigned_by)
            VALUES (@emp, 'EMPLOYEE',     @org, 'ORG_ONLY', 'TEST'),
                   (@mgr, 'LOCAL_LEADER', @org, 'ORG_ONLY', 'TEST')
            ON CONFLICT DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("emp", emp);
            cmd.Parameters.AddWithValue("mgr", mgr);
            cmd.Parameters.AddWithValue("org", Org);
            await cmd.ExecuteNonQueryAsync();
        }

        // The designated edge: the leader's team-overview roster and the breakdown's authorization
        // both derive from it.
        await new ReportingLineRepository(_fx.Db).AssignAsync(null, new ReportingLineModel
        {
            ReportingLineId = Guid.Empty,
            EmployeeId = emp,
            ManagerId = mgr,
            OrganisationId = Org,
            Relationship = "PRIMARY",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            Source = "MANUAL",
            Version = 0,
            CreatedBy = "TEST",
        });

        // This case's OWN fixture project — a distributed entry must name one. Deliberately
        // independent of TASK-12701a's seeding.
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO projects (org_id, project_code, project_name, created_by)
            VALUES (@org, @code, @name, 'TEST')
            ON CONFLICT (org_id, project_code) DO NOTHING
            """, conn))
        {
            cmd.Parameters.AddWithValue("org", Org);
            cmd.Parameters.AddWithValue("code", projectCode);
            cmd.Parameters.AddWithValue("name", "TASK-12700 baseline " + caseId);
            await cmd.ExecuteNonQueryAsync();
        }

        // SUBMITTED, because it is the one status that is simultaneously (a) accepted by the gate and
        // (b) "sent to the manager", which is what un-withholds hasWarning on the team-overview row.
        var periodId = Guid.NewGuid();
        await using (var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status,
                 agreement_code, ok_version, submitted_at, submitted_by)
            VALUES (@pid, @emp, @org, @start, @end, 'MONTHLY', 'SUBMITTED', 'HK', 'OK24', NOW(), @emp)
            """, conn))
        {
            cmd.Parameters.AddWithValue("pid", periodId);
            cmd.Parameters.AddWithValue("emp", emp);
            cmd.Parameters.AddWithValue("org", Org);
            cmd.Parameters.AddWithValue("start", MonthStart);
            cmd.Parameters.AddWithValue("end", MonthEnd);
            await cmd.ExecuteNonQueryAsync();
        }

        // Workday coverage for every weekday EXCEPT the case day, as full-day absences. Absences are
        // read from a table that neither the worked map nor the distributed map touches, so these days
        // satisfy coverage while contributing NOTHING to any of the three encodings — leaving the case
        // day as the only day compared by all three.
        for (var d = MonthStart; d <= MonthEnd; d = d.AddDays(1))
        {
            if (d == CaseDay || d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO absences_projection
                    (event_id, employee_id, date, absence_type, hours, feriedage,
                     agreement_code, ok_version, occurred_at, outbox_id)
                VALUES (gen_random_uuid(), @emp, @date, 'VACATION', 7.4, 1.0, 'HK', 'OK24', NOW(), @seq)
                """, conn);
            cmd.Parameters.AddWithValue("emp", emp);
            cmd.Parameters.AddWithValue("date", d);
            cmd.Parameters.AddWithValue("seq", NextOutboxId());
            await cmd.ExecuteNonQueryAsync();
        }

        // The case day itself.
        if (row.ManualHours is not null || row.Intervals is not null)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO work_time_projection
                    (employee_id, date, intervals, manual_hours, occurred_at, outbox_id)
                VALUES (@emp, @date, @intervals::jsonb, @manual, NOW(), @seq)
                """, conn);
            cmd.Parameters.AddWithValue("emp", emp);
            cmd.Parameters.AddWithValue("date", CaseDay);
            cmd.Parameters.AddWithValue("intervals", row.Intervals ?? "[]");
            cmd.Parameters.AddWithValue("manual", row.ManualHours ?? 0m);
            cmd.Parameters.AddWithValue("seq", NextOutboxId());
            await cmd.ExecuteNonQueryAsync();
        }

        foreach (var entry in row.Entries)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO time_entries_projection
                    (event_id, employee_id, date, hours, task_id, activity_type,
                     agreement_code, ok_version, voluntary_unsocial_hours, occurred_at, outbox_id)
                VALUES (gen_random_uuid(), @emp, @date, @hours, @task, @activity,
                        'HK', 'OK24', FALSE, NOW(), @seq)
                """, conn);
            cmd.Parameters.AddWithValue("emp", emp);
            cmd.Parameters.AddWithValue("date", CaseDay);
            cmd.Parameters.AddWithValue("hours", entry.Hours);
            cmd.Parameters.AddWithValue("task", entry.NamesProject ? projectCode : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("activity", entry.ActivityType);
            cmd.Parameters.AddWithValue("seq", NextOutboxId());
            await cmd.ExecuteNonQueryAsync();
        }

        return new CaseIdentities(emp, mgr, periodId);
    }

    private static int _outboxSeq;
    private static long NextOutboxId() => Interlocked.Increment(ref _outboxSeq);

    // ════════════════════════════════════════════════════════════════════════════════════════════
    //  HTTP surfaces
    // ════════════════════════════════════════════════════════════════════════════════════════════

    private async Task<bool> ReadHasWarningAsync(string managerId, string employeeId)
    {
        var client = ClientFor(managerId, StatsTidRoles.LocalLeader);
        var rsp = await client.GetAsync(
            $"/api/approval/team-overview?year={MonthStart.Year}&month={MonthStart.Month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        var body = await rsp.Content.ReadFromJsonAsync<JsonElement>();
        var row = body.GetProperty("employees").EnumerateArray()
            .Single(r => r.GetProperty("employeeId").GetString() == employeeId);
        // Null would mean the row was withheld as not-yet-sent, which the SUBMITTED fixture rules
        // out; a null here is a broken fixture, not a verdict.
        var warning = row.GetProperty("hasWarning");
        Assert.NotEqual(JsonValueKind.Null, warning.ValueKind);
        return warning.GetBoolean();
    }

    private async Task<JsonElement> ReadBreakdownAsync(string managerId, string employeeId)
    {
        var client = ClientFor(managerId, StatsTidRoles.LocalLeader);
        var rsp = await client.GetAsync(
            $"/api/approval/{employeeId}/allocation-breakdown"
            + $"?year={MonthStart.Year}&month={MonthStart.Month}");
        Assert.Equal(HttpStatusCode.OK, rsp.StatusCode);
        return await rsp.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpResponseMessage> PostEmployeeApproveAsync(string employeeId, Guid periodId)
    {
        var client = ClientFor(employeeId, StatsTidRoles.Employee);
        return await client.PostAsync($"/api/approval/{periodId}/employee-approve", null);
    }

    private HttpClient ClientFor(string userId, string role)
    {
        var client = _fx.Factory.CreateClient();
        var tokenService = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = DevFallbackSigningKey,
            ExpirationMinutes = 60,
        });
        var token = tokenService.GenerateToken(
            employeeId: userId, name: userId, role: role,
            agreementCode: "HK", orgId: Org,
            scopes: new[] { new RoleScope(role, Org, "ORG_ONLY") });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>
/// S127 / TASK-12700 — mechanizes the STATED LIMIT of
/// <see cref="AllocationPredicateCharacterizationTests"/> so that it cannot quietly become false.
///
/// <para>The gate spells the predicate <c>|Δ| &lt; tol ⇒ balanced</c>; the two read surfaces spell it
/// <c>|Δ| &gt; tol ⇒ warn</c>. Those disagree at exactly one input, <c>|Δ| == 0.005</c>. The
/// characterization baseline cannot reach that input and therefore says nothing about the
/// difference — <b>not</b> because the difference is harmless, but because both operands are
/// <c>Math.Round(…, 2)</c> before the comparison, so each is a whole number of hundredths and so is
/// their difference. Half a hundredth cannot be produced.</para>
///
/// <para>These cases are pairs of RAW values chosen to sit exactly on the 0.005 boundary — the only
/// inputs from which the unreachable delta could ever arise. Each asserts, in order: the raw pair IS
/// on the boundary (so the case is not vacuous), and that after rounding to hundredths the delta is
/// never 0.005 but always either 0 or at least a whole hundredth.</para>
///
/// <para><b>Be honest about what this is and is not.</b> It does not call the production predicate,
/// so it is NOT a tripwire that fires when someone removes the rounding from
/// <c>ApprovalEndpoints.cs</c> — nothing here would notice. What it is: the unreachability argument
/// written down as arithmetic rather than as prose, with its own non-vacuity guard, so a future
/// reader can check the limit instead of taking the comment's word for it. The claim it makes is
/// conditional — <i>given</i> both operands are rounded to hundredths, half a hundredth cannot
/// arise — and the <i>given</i> is what TASK-12705 must not quietly drop. Case C5 of the
/// characterization baseline is the assertion that actually watches the production code for that.
/// No database, no HTTP.</para>
/// </summary>
public sealed class AllocationPredicateRoundingLimitTests
{
    [Theory]
    [InlineData("7.4150", "7.4200")]    // rounds to 7.42 / 7.42 → Δ 0.00
    [InlineData("7.4149", "7.4199")]    // rounds to 7.41 / 7.42 → Δ 0.01
    [InlineData("0.0050", "0.0000")]    // the boundary at the origin
    [InlineData("12.3450", "12.3400")]
    [InlineData("23.9950", "24.0000")]  // the boundary at the top of a legal day
    public void HalfCentDelta_IsUnreachable_OnceBothOperandsAreRoundedToHundredths(
        string workedRaw, string distributedRaw)
    {
        var worked = decimal.Parse(workedRaw, CultureInfo.InvariantCulture);
        var distributed = decimal.Parse(distributedRaw, CultureInfo.InvariantCulture);

        // Not vacuous: the RAW pair is exactly on the boundary the two spellings disagree at.
        Assert.Equal(0.005m, Math.Abs(worked - distributed));

        // ... and rounding to hundredths always moves it off, in one direction or the other.
        var delta = Math.Abs(Math.Round(worked, 2) - Math.Round(distributed, 2));
        Assert.NotEqual(0.005m, delta);
        Assert.True(delta == 0m || delta >= 0.01m,
            $"rounded delta {delta} is neither zero nor a whole hundredth");
    }
}
