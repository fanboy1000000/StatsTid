using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S137 / TASK-13707 — ADR-016 D4 after the owner ruling of 2026-09-02 ("hire and leave
/// edges are truncations, not splits"), proven END-TO-END through the payroll host against a
/// real Postgres and with the <b>LIVE rule-classification set</b>
/// (<c>new RuleRegistry().GetAll()</c> — three AlignedWindow rules, NORM_CHECK_WEEKLY first since S145 B1;
/// since S145 B2 payroll hands the planner only the six rules it evaluates).
///
/// <para>
/// Why a second file next to <see cref="EmploymentWindowPayrollTests"/>: that suite proves
/// the D5 typed-segment mechanics under <see cref="TestFixtures.StraddleSafeRuleSet"/>, a
/// deliberately split-tolerant rule set. This suite asks the production question — with the
/// rules the payroll host really reads, does a mid-month leaver's or starter's month CALCULATE?
/// Before the ruling it did not: any interior boundary refused the month. Now it does, because
/// the NOT_EMPLOYED side evaluates nothing, so OVERTIME_CALC runs exactly once and reaches
/// <see cref="MergeStrategy.RejectIfMultipleSegments"/> as a single segment.
/// </para>
///
/// <para>
/// The third pin used to record the LIMITATION the ruling left in place (QUAL-149): a mid-month
/// part-time-fraction change while employed is two EMPLOYED segments — a genuine split — and the
/// live set refused it with <see cref="PlannerInvariantViolation"/>. S145 Stage B lifted it:
/// payroll now hands the planner only the six rules it evaluates (<c>NORM_CHECK_37H</c>,
/// <c>OVERTIME_CALC</c> and <c>FLEX_BALANCE</c> are <c>TimelineAware</c>; the registry's three
/// AlignedWindow rules — NORM_CHECK_WEEKLY and the two rest-period rules — are rules payroll never
/// runs and never reach planning), so the split month calculates and the third pin now asserts
/// that (<c>MidMonthFractionChange_LiveRuleSet_Calculates_TimelineSpansBothSegments</c>).
/// </para>
///
/// <para>
/// Harness: mirrors <see cref="EmploymentWindowPayrollTests"/> (recording rule-engine stub,
/// full-schema Postgres, the production PCS DI shape) with ONE difference — the
/// <see cref="IRuleClassificationProvider"/> serves the live registry.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentWindowLiveRulesetTests : IAsyncLifetime
{
    private const string OrgId = "STY_S137_LIVE";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";

    // April 2026 sits entirely on the OK26 side of the OK24→OK26 transition (2026-04-01);
    // the transition date IS the period start, so it is not an interior boundary. Every
    // split below comes from the employment window or the profile history.
    private static readonly DateOnly Apr01 = new(2026, 4, 1);
    private static readonly DateOnly Apr30 = new(2026, 4, 30);

    private const decimal DailyHours = 7.4m;

    /// <summary>The production registry's classification set — what
    /// <c>HttpRuleClassificationProvider</c> serves the payroll host.</summary>
    private static readonly IReadOnlyList<RuleClassification> LiveSet = new RuleRegistry().GetAll();

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        // Full init.sql: users (employment dates), employee_profiles, user_agreement_codes,
        // organizations — the segmentation DockerHarness DDL is the 4-table subset only.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // (a) Mid-month leaver — the month CALCULATES under the live set
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MidMonthLeaver_LiveRuleSet_CalculationSucceeds_LastEmployedDayPays_NoCallsAfterEnd()
    {
        const string employeeId = "EMP-S137-LIVE-LEAVER";
        var lastDay = new DateOnly(2026, 4, 15); // a Wednesday — has a 7.4h entry
        await SeedEmployeeAsync(employeeId, partTimeFraction: 1.0m);
        await SetEmploymentWindowAsync(employeeId, start: null, end: lastDay);

        var engine = new RecordingRuleEngine();
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);
        // S145 R14: payroll reads its own hours — the same entries reach PCS through an
        // in-memory IRecordedHoursReader instead of through the call.
        var pcs = BuildPcs(engine, new InMemoryRecordedHoursReader(entries));

        // Before the ruling this call threw PlannerInvariantViolation naming OVERTIME_CALC.
        var outcome = await RunShimAsync(pcs, employeeId);

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.Equal(PeriodCalculationService.AuditState.Complete, outcome.AuditState);

        // The AlignedWindow rule was evaluated in exactly one EMPLOYED segment and reached
        // RejectIfMultipleSegments with ONE segment — so its merged result is a success, not
        // the "produced N segments but its merge strategy requires exactly one" failure row.
        var overtime = Assert.Single(outcome.Result.RuleResults, r => r.RuleId == OvertimeRule.RuleId);
        Assert.True(overtime.Success, overtime.ErrorMessage);
        Assert.All(outcome.Result.RuleResults, r => Assert.True(r.Success, r.ErrorMessage));

        // No rule-engine call after the end date: every per-segment call covers [04-01 .. 04-15].
        // S145 F1: the plan's one timeline call carries the period's range (04-01 .. 04-30); what must
        // end on the last employed day is its PIECES (so nothing after it is judged), and no entry
        // after it is sent. Its pieces start on April's opening Monday, 30 Mar (the week
        // holding Sunday 5 Apr is April's); the leaver is employed on those context days. The request's
        // SettlementCutoff is deliberately not asserted here (C-T1; pinned at the endpoint's own tests).
        Assert.NotEmpty(engine.PerSegmentCalls);
        Assert.All(engine.PerSegmentCalls, c =>
        {
            Assert.Equal(Apr01, c.PeriodStart);
            Assert.Equal(lastDay, c.PeriodEnd);
        });
        var timeline = Assert.Single(engine.TimelineCalls);
        Assert.Equal(Apr01, timeline.PeriodStart);
        Assert.Equal(Apr30, timeline.PeriodEnd);
        Assert.Equal(new DateOnly(2026, 3, 30), timeline.Pieces![0].From);
        Assert.Equal(lastDay, timeline.Pieces[^1].To);
        Assert.All(timeline.EntryDates!, d => Assert.True(d <= lastDay, $"{d} is after the last employed day"));

        // The last employed day's hours export (ADR-040 D1 end-inclusive fencepost).
        var normalLines = outcome.Result.ExportLines.Where(l => l.SourceTimeType == "NORMAL_HOURS").ToList();
        var employedEntries = entries.Where(e => e.Date <= lastDay).ToList();
        Assert.Contains(employedEntries, e => e.Date == lastDay);
        Assert.Equal(employedEntries.Count, normalLines.Count);
        Assert.Equal(employedEntries.Count * DailyHours, normalLines.Sum(l => l.Hours));
        Assert.All(outcome.Result.ExportLines, l => Assert.True(l.PeriodEnd <= lastDay));

        // The persisted manifest carries the NOT_EMPLOYED suffix from End + 1.
        var manifest = await LoadManifestAsync(outcome.ManifestId);
        Assert.Contains("EmploymentEnded", manifest.BoundaryCauseSummary);
        Assert.Equal(2, manifest.Segments.Count);
        Assert.Equal("2026-04-15", manifest.Segments[0].GetProperty("endDate").GetString());
        Assert.False(manifest.Segments[0].TryGetProperty("employmentStatus", out _));
        Assert.Equal("2026-04-16", manifest.Segments[1].GetProperty("startDate").GetString());
        Assert.Equal("NOT_EMPLOYED", manifest.Segments[1].GetProperty("employmentStatus").GetString());
    }

    // ═════════════════════════════════════════════════════════════════════
    // (b) Mid-month starter — the mirror image
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MidMonthStarter_LiveRuleSet_CalculationSucceeds_DayStartPays_NoCallsBeforeStart()
    {
        const string employeeId = "EMP-S137-LIVE-STARTER";
        var hire = new DateOnly(2026, 4, 13); // a Monday — has a 7.4h entry
        // Profile (and agreement-code) rows anchored AT the hire date — the post-S136 shape of a
        // real hire, so the dated resolver has NO covering row for the pre-hire span (ADR-040 D10
        // end-to-end: PCS must never ask for it).
        await SeedEmployeeAsync(employeeId, partTimeFraction: 1.0m, effectiveFrom: hire);
        await SetEmploymentWindowAsync(employeeId, start: hire, end: null);

        var engine = new RecordingRuleEngine();
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);
        // S145 R14: payroll reads its own hours — the same entries reach PCS through an
        // in-memory IRecordedHoursReader instead of through the call.
        var pcs = BuildPcs(engine, new InMemoryRecordedHoursReader(entries));

        var outcome = await RunShimAsync(pcs, employeeId);

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.All(outcome.Result.RuleResults, r => Assert.True(r.Success, r.ErrorMessage));

        // Every per-segment rule-engine call covers [04-13 .. 04-30] — nothing for the pre-hire prefix.
        // S145 F1: the timeline call carries the period's range (04-01 .. 04-30); its PIECES start on
        // the hire date (the pre-hire days, context days included, are not employed, so no piece covers
        // them) and end on 04-30, and no entry before the hire is sent.
        Assert.NotEmpty(engine.PerSegmentCalls);
        Assert.All(engine.PerSegmentCalls, c =>
        {
            Assert.Equal(hire, c.PeriodStart);
            Assert.Equal(Apr30, c.PeriodEnd);
        });
        var timeline = Assert.Single(engine.TimelineCalls);
        Assert.Equal(Apr01, timeline.PeriodStart);
        Assert.Equal(Apr30, timeline.PeriodEnd);
        Assert.Equal(hire, timeline.Pieces![0].From);
        Assert.Equal(Apr30, timeline.Pieces[^1].To);
        Assert.All(timeline.EntryDates!, d => Assert.True(d >= hire, $"{d} is before the hire date"));

        // Day `start` pays; nothing before it does.
        var normalLines = outcome.Result.ExportLines.Where(l => l.SourceTimeType == "NORMAL_HOURS").ToList();
        var employedEntries = entries.Where(e => e.Date >= hire).ToList();
        Assert.Contains(employedEntries, e => e.Date == hire);
        Assert.Equal(employedEntries.Count, normalLines.Count);
        Assert.All(outcome.Result.ExportLines, l => Assert.True(l.PeriodStart >= hire));

        var manifest = await LoadManifestAsync(outcome.ManifestId);
        Assert.Contains("EmploymentStarted", manifest.BoundaryCauseSummary);
        Assert.Equal(2, manifest.Segments.Count);
        Assert.Equal("NOT_EMPLOYED", manifest.Segments[0].GetProperty("employmentStatus").GetString());
        Assert.Equal("2026-04-13", manifest.Segments[1].GetProperty("startDate").GetString());
        Assert.False(manifest.Segments[1].TryGetProperty("employmentStatus", out _));
    }

    // ═════════════════════════════════════════════════════════════════════
    // (c) A profile change WHILE EMPLOYED — the month CALCULATES under the live
    //     set (S145 Stage B lifted the registered limitation, QUAL-149)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>S145 B2: was <c>MidMonthFractionChange_LiveRuleSet_RefusesAsRegisteredLimitation</c>
    /// (QUAL-149, "AlignedWindow norm/overtime rules refuse a profile-change split until
    /// reclassified"). Stage B reclassified them (B1) and made payroll hand the planner only the
    /// rules it evaluates (B2), so the pin now asserts the opposite.</b> A part-time-fraction change
    /// 1.0 → 0.800 on Thursday 16 Apr 2026 while employed all month is EMPLOYED [04-01..04-15] +
    /// EMPLOYED [04-16..04-30]: two EMPLOYED segments. The month CALCULATES: <c>Success</c>; ONE
    /// timeline call whose pieces end on 15 Apr and start on 16 Apr (the timeline spans both
    /// segments); two EMPLOYED segments and <c>EmployeeProfileChange</c> in the persisted manifest;
    /// the three timeline rules (norm, overtime, flex) are evaluated by that one call and NOT per
    /// segment (the per-segment lane makes supplement, on-call and absence calls only). The merged
    /// rows are the timeline's, one per rule, each a success. March is approved through the suite's
    /// R26 fake (<see cref="TestFixtures.AlwaysApprovedPriorPeriods"/>, injected in
    /// <see cref="BuildPcs"/>).
    ///
    /// <para>
    /// <b>Red conditions</b> (docs/AGENTS.md § "RED evidence"). MB-7 (TASK-14528: the evaluated-set
    /// filter disabled, so the planner sees NORM_CHECK_WEEKLY as AlignedWindow again): the month is
    /// refused with <see cref="PlannerInvariantViolation"/> and the call throws before any assertion.
    /// MB-3 (the Unit mutation of the per-plan lane covering only the first EMPLOYED segment,
    /// <c>PeriodCalculationService.DeriveEvaluationInputsAsync</c>'s pieces): no piece starts on
    /// 16 Apr, so the piece assertions trip.
    /// </para>
    /// </summary>
    [Fact]
    public async Task MidMonthFractionChange_LiveRuleSet_Calculates_TimelineSpansBothSegments() // S145 B2: was ..._RefusesAsRegisteredLimitation
    {
        const string employeeId = "EMP-S137-LIVE-FRACTION";
        var changeDate = new DateOnly(2026, 4, 16);
        var lastOfFirstPart = new DateOnly(2026, 4, 15);
        await SeedEmployeeAsync(employeeId, partTimeFraction: 1.0m);
        await SupersedeFractionAsync(employeeId, newFraction: 0.800m, effectiveFrom: changeDate);
        // Window stays unbounded (both NULL) — every segment EMPLOYED; the split is the
        // profile change alone.

        var engine = new RecordingRuleEngine();
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);
        // S145 R14: payroll reads its own hours — the same entries reach PCS through an
        // in-memory IRecordedHoursReader instead of through the call.
        var pcs = BuildPcs(engine, new InMemoryRecordedHoursReader(entries));

        // S145 B2: this call used to throw PlannerInvariantViolation naming NORM_CHECK_WEEKLY (R-61).
        var outcome = await RunShimAsync(pcs, employeeId);

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage); // S145 B2
        Assert.Equal(PeriodCalculationService.AuditState.Complete, outcome.AuditState); // S145 B2
        Assert.All(outcome.Result.RuleResults, r => Assert.True(r.Success, r.ErrorMessage)); // S145 B2

        // S145 B2: ONE timeline call spans both segments — its pieces end on 15 Apr and start on 16 Apr,
        // and the last piece is the second part, ending with the period.
        var timeline = Assert.Single(engine.TimelineCalls);
        Assert.Equal(Apr01, timeline.PeriodStart);
        Assert.Equal(Apr30, timeline.PeriodEnd);
        Assert.Contains(timeline.Pieces!, p => p.To == lastOfFirstPart);
        Assert.Contains(timeline.Pieces!, p => p.From == changeDate);
        Assert.Equal(changeDate, timeline.Pieces![^1].From);
        Assert.Equal(Apr30, timeline.Pieces[^1].To);

        // S145 B2: no per-segment call for the three timeline rules — they run only in the timeline call.
        Assert.DoesNotContain(engine.PerSegmentCalls, c => c.RuleId == OvertimeRule.RuleId);
        Assert.DoesNotContain(engine.PerSegmentCalls, c => c.RuleId == "NORM_CHECK_37H");
        Assert.DoesNotContain(engine.PerSegmentCalls, c => c.RuleId == "FLEX_BALANCE");

        // S145 B2: the merged rows are the timeline's one row each (a SinglePlanEvaluation rule), all successes.
        var overtime = Assert.Single(outcome.Result.RuleResults, r => r.RuleId == OvertimeRule.RuleId);
        Assert.True(overtime.Success, overtime.ErrorMessage);
        Assert.Single(outcome.Result.RuleResults, r => r.RuleId == "NORM_CHECK_37H");
        Assert.Single(outcome.Result.RuleResults, r => r.RuleId == "FLEX_BALANCE");

        // S145 B2: every weekday's hours export once (the timeline's lines, split by date into the two parts).
        var normalLines = outcome.Result.ExportLines.Where(l => l.SourceTimeType == "NORMAL_HOURS").ToList();
        Assert.Equal(entries.Count, normalLines.Count);

        // S145 B2: the persisted manifest holds two EMPLOYED segments and names the profile change.
        var manifest = await LoadManifestAsync(outcome.ManifestId);
        Assert.Contains("EmployeeProfileChange", manifest.BoundaryCauseSummary);
        Assert.Equal(2, manifest.Segments.Count);
        Assert.All(manifest.Segments, seg => Assert.False(seg.TryGetProperty("employmentStatus", out _)));
        Assert.Equal("2026-04-15", manifest.Segments[0].GetProperty("endDate").GetString());
        Assert.Equal("2026-04-16", manifest.Segments[1].GetProperty("startDate").GetString());
    }

    // ─── Seeding ─────────────────────────────────────────────────────────

    private async Task SeedEmployeeAsync(string employeeId, decimal partTimeFraction, DateOnly? effectiveFrom = null)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        // users + employee_profiles + user_agreement_codes; the profile/agreement rows start at
        // effectiveFrom (default '0001-01-01'; the starter fact passes the hire date). The users
        // row's window is both-NULL (unbounded, ADR-040 D2) until SetEmploymentWindowAsync
        // narrows it.
        await RegressionSeed.SeedEmployeeAsync(
            conn, employeeId, OrgId, AgreementCode, OkVersion, partTimeFraction, effectiveFrom);
    }

    private async Task SetEmploymentWindowAsync(string employeeId, DateOnly? start, DateOnly? end)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users
            SET employment_start_date = @start, employment_end_date = @end
            WHERE user_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        cmd.Parameters.AddWithValue("start", (object?)start ?? DBNull.Value);
        cmd.Parameters.AddWithValue("end", (object?)end ?? DBNull.Value);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    /// <summary>Closes the live profile row at <paramref name="effectiveFrom"/> (end-exclusive)
    /// and inserts the successor — written directly so the test controls the dates.</summary>
    private async Task SupersedeFractionAsync(string employeeId, decimal newFraction, DateOnly effectiveFrom)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var close = new NpgsqlCommand(
            """
            UPDATE employee_profiles SET effective_to = @from
            WHERE employee_id = @id AND effective_to IS NULL
            """, conn, tx))
        {
            close.Parameters.AddWithValue("id", employeeId);
            close.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await close.ExecuteNonQueryAsync());
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO employee_profiles (
                profile_id, employee_id, part_time_fraction, position,
                effective_from, effective_to, version, employment_category)
            VALUES (gen_random_uuid(), @id, @fraction, NULL, @from, NULL, 1,
                    (SELECT u.employment_category FROM users u WHERE u.user_id = @id))
            """, conn, tx))
        {
            insert.Parameters.AddWithValue("id", employeeId);
            insert.Parameters.AddWithValue("fraction", newFraction);
            insert.Parameters.AddWithValue("from", effectiveFrom);
            Assert.Equal(1, await insert.ExecuteNonQueryAsync());
        }

        await tx.CommitAsync();
    }

    // ─── PCS wiring (the production DI shape, hand-built) ────────────────

    private PeriodCalculationService BuildPcs(RecordingRuleEngine engine, IRecordedHoursReader hoursReader)
    {
        var httpFactory = new SingleClientFactory(engine);
        var wtmRepo = new WageTypeMappingRepository(_harness.Factory);
        var mappingService = new PayrollMappingService(
            _harness.Factory, NullLogger<PayrollMappingService>.Instance, wtmRepo);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
            })
            .Build();

        // Identical to EmploymentWindowPayrollTests.BuildPcs except the classification
        // provider: the LIVE registry, not the straddle-safe test set.
        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            new PostgresSegmentManifestStore(_harness.Factory, _harness.EventStore, _harness.EventStore),
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new LiveRuleClassificationProvider(),
            localAgreementProfileRepo: null,
            profileResolver: new EmploymentProfileResolver(_harness.Factory, new UserAgreementCodeRepository(_harness.Factory)),
            employmentWindowResolver: new EmploymentWindowResolver(_harness.Factory),
            employeeProfileRepo: new EmployeeProfileRepository(_harness.Factory),
            // S145 R14: the in-memory hours reader (payroll reads its own hours).
            hoursReader: hoursReader,
            // S145 R26: this suite is not about the prior-month check; M−1 is treated as APPROVED
            priorPeriodApprovalReader: new TestFixtures.AlwaysApprovedPriorPeriods());
    }

    private static async Task<PeriodCalculationService.PeriodCalculationOutcome> RunShimAsync(
        PeriodCalculationService pcs,
        string employeeId)
    {
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        return await pcs.CalculateWithOutcomeAsync(
            Profile(employeeId), Apr01, Apr30, previousFlexBalance: 0m);
#pragma warning restore CS0618
    }

    private static EmploymentProfile Profile(string employeeId) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = AgreementCode,
        OkVersion = OkVersion,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = OrgId,
    };

    // ─── Manifest projection read ────────────────────────────────────────

    private sealed record PersistedManifest(IReadOnlyList<JsonElement> Segments, IReadOnlyList<string> BoundaryCauseSummary);

    private async Task<PersistedManifest> LoadManifestAsync(Guid manifestId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT segments_jsonb::text, boundary_cause_summary
            FROM segment_manifests
            WHERE manifest_id = @id
            """, conn);
        cmd.Parameters.AddWithValue("id", manifestId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), $"no segment_manifests row for {manifestId}");

        using var doc = JsonDocument.Parse(reader.GetString(0));
        var segments = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        var summary = ((string[])reader.GetValue(1)).ToList();
        return new PersistedManifest(segments, summary);
    }

    // ─── Recording rule-engine stub ──────────────────────────────────────

    /// <summary>
    /// Records every rule-engine call (endpoint, segment range) and answers with rule-shaped
    /// success payloads: NORM_CHECK_37H → one NORMAL_HOURS line per entry; every other time
    /// rule (OVERTIME_CALC included) → a successful, line-less result, so the AlignedWindow
    /// rule's result exists and must survive the merge step; absence and flex → empty success.
    /// </summary>
    private sealed class RecordingRuleEngine : HttpMessageHandler
    {
        // S145 F1: the timeline call also records its pieces (inclusive ranges) and the dates of the
        // entries it carried; both are null on the per-segment calls.
        public sealed record Call(
            string Path,
            DateOnly PeriodStart,
            DateOnly PeriodEnd,
            IReadOnlyList<(DateOnly From, DateOnly To)>? Pieces = null,
            IReadOnlyList<DateOnly>? EntryDates = null,
            string? RuleId = null); // S145 B2: the per-segment calls record their rule id (LB1)

        public IReadOnlyList<Call> TimelineCalls =>
            Calls.Where(c => c.Path.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal)).ToList();

        public IReadOnlyList<Call> PerSegmentCalls =>
            Calls.Where(c => !c.Path.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal)).ToList();

        private readonly List<Call> _calls = new();

        public IReadOnlyList<Call> Calls
        {
            get { lock (_calls) return _calls.ToList(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            var employeeId = root.TryGetProperty("profile", out var prof)
                && prof.ValueKind == JsonValueKind.Object
                && prof.TryGetProperty("employeeId", out var eid)
                ? eid.GetString() ?? "EMP"
                : "EMP";

            var isTimeline = path.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal);
            var call = new Call(
                path,
                DateOnly.Parse(root.GetProperty("periodStart").GetString()!, CultureInfo.InvariantCulture),
                DateOnly.Parse(root.GetProperty("periodEnd").GetString()!, CultureInfo.InvariantCulture),
                Pieces: isTimeline
                    ? root.GetProperty("pieces").EnumerateArray().Select(p => (
                        DateOnly.Parse(p.GetProperty("from").GetString()!, CultureInfo.InvariantCulture),
                        DateOnly.Parse(p.GetProperty("to").GetString()!, CultureInfo.InvariantCulture))).ToList()
                    : null,
                EntryDates: isTimeline
                    ? root.GetProperty("entries").EnumerateArray()
                        .Select(e => DateOnly.Parse(e.GetProperty("date").GetString()!, CultureInfo.InvariantCulture)).ToList()
                    : null,
                RuleId: root.TryGetProperty("ruleId", out var callRuleId) ? callRuleId.GetString() : null);
            lock (_calls) _calls.Add(call);

            // S145 F1: the plan's one timeline call is answered with a COMPLETE timeline response (the
            // three results, the flex block, fingerprints and model marker; NORMAL_HOURS lines for the
            // in-month entries only). PCS refuses anything less as a failed call.
            if (isTimeline)
                return TestFixtures.EvaluateTimelineResponse(request);

            if (path.EndsWith("/api/rules/evaluate", StringComparison.Ordinal))
            {
                var ruleId = root.TryGetProperty("ruleId", out var rid) ? rid.GetString() ?? "UNKNOWN" : "UNKNOWN";
                var lineItems = ruleId == "NORM_CHECK_37H"
                    ? root.GetProperty("entries").EnumerateArray().Select(e => (object)new
                    {
                        timeType = "NORMAL_HOURS",
                        hours = e.GetProperty("hours").GetDecimal(),
                        rate = 1.0m,
                        date = e.GetProperty("date").GetString(),
                    }).ToList()
                    : new List<object>();
                return Json(new { ruleId, employeeId, success = true, lineItems });
            }

            if (path.EndsWith("/api/rules/evaluate-absence", StringComparison.Ordinal))
                return Json(new { ruleId = "ABSENCE", employeeId, success = true, lineItems = new List<object>() });

            if (path.EndsWith("/api/rules/evaluate-flex", StringComparison.Ordinal))
                return Json(new { ruleId = "FLEX_BALANCE", employeeId, success = true, lineItems = new List<object>() });

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("unknown rule endpoint") };
        }

        private static HttpResponseMessage Json(object payload)
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    /// <summary>Serves the production registry's classification set — the in-process twin of
    /// <c>HttpRuleClassificationProvider</c>, which reads the same set over HTTP.</summary>
    private sealed class LiveRuleClassificationProvider : IRuleClassificationProvider
    {
        public IReadOnlyList<RuleClassification> GetClassifications() => LiveSet;
    }
}
