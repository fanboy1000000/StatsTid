using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S137 / TASK-13702 — the OQ-1 core of ADR-040 D5 at the payroll boundary, end-to-end
/// against a real Postgres: the payroll host's planner reads the employee's employment
/// window and profile history from the DB (server-side, D7), splits the month into typed
/// EMPLOYED / NOT_EMPLOYED segments, evaluates and exports ONLY the employed spans, and
/// persists the whole typed story in the <c>segment_manifests</c> projection.
///
/// <para>
/// Plain-language, per fact:
/// <list type="bullet">
///   <item><b>Mid-month leaver</b> (last day 2026-04-15): the persisted manifest carries an
///     EmploymentEnded-caused NOT_EMPLOYED segment starting 04-16 — and <c>segments_jsonb</c>
///     carries the <c>employmentStatus</c> key for THAT segment only (the employed one stays
///     byte-shaped exactly as before S137). Zero rule-engine calls and zero export lines for
///     the not-employed span, while the LAST employed day (04-15) DOES pay — the ADR-040 D1
///     end-inclusive fencepost.</item>
///   <item><b>Mid-month starter</b> (first day 2026-04-13): mirror image — day <c>start</c>
///     pays, the pre-hire prefix is NOT_EMPLOYED and silent.</item>
///   <item><b>Mid-month part_time_fraction change</b> (1.0 → 0.8 from 04-16): the
///     ADR-016 D5b-reserved EmployeeProfileChange cause is now live — the month splits and
///     each span resolves its own dated profile, so norm-credit hours differ per span. The
///     ADR-031 negative AC is pinned at this boundary: the fraction changes ONLY the hours
///     (norm/pay) on a vacation line, never HOW MANY vacation days flow through (day-count
///     is fraction-flat — one absence day in, one line out, on both sides of the split).</item>
///   <item><b>Flex carry through a NOT_EMPLOYED middle segment</b> with a NON-ZERO delta and
///     a real (seeded) wage-type mapping: the third segment's flex call receives
///     <c>opening + segment-1 delta</c>, untouched by the skipped span. This is the variant
///     the DB-free unit pin (<c>EmploymentWindowSegmentSkipTests</c>) cannot run.</item>
/// </list>
/// </para>
///
/// <para>
/// The rule engine is stubbed (the contracts under test belong to PCS + the DB reads); the
/// stub MODELS the two rule behaviours these pins depend on — <c>NORM_CHECK_37H</c> emits one
/// NORMAL_HOURS line per entry, and the absence rule emits one VACATION line per absence day
/// with <c>hours = 7.4 × part_time_fraction</c> (the real <c>AbsenceRule</c> formula) — and
/// RECORDS what PCS asked, so "zero calls for the not-employed span" is a direct assertion.
/// The classification provider uses <see cref="TestFixtures.StraddleSafeRuleSet"/> because
/// an interior boundary must be plannable (the AlignedWindow OVERTIME_CALC in
/// <see cref="TestFixtures.RuleSet"/> trips ADR-016 D4 on any split — a contract pinned
/// elsewhere).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentWindowPayrollTests : IAsyncLifetime
{
    private const string OrgId = "STY_S137_PAY";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";

    // April 2026 sits entirely on the OK26 side of the OK24→OK26 transition (2026-04-01),
    // and because the transition date IS the period start it is not an INTERIOR boundary.
    // Every split asserted below therefore comes from THIS task's new boundary sources —
    // employment window edges and profile effective-dates — never from the OK calendar.
    private static readonly DateOnly Apr01 = new(2026, 4, 1);
    private static readonly DateOnly Apr30 = new(2026, 4, 30);

    private const decimal DailyHours = 7.4m;

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
    // OQ-1 core — mid-month leaver
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MidMonthLeaver_NotEmployedSuffixInManifest_NoCallsNoLinesAfterEnd_LastEmployedDayPays()
    {
        const string employeeId = "EMP-S137-LEAVER";
        var lastDay = new DateOnly(2026, 4, 15); // a Wednesday — has a 7.4h entry
        await SeedEmployeeAsync(employeeId, partTimeFraction: 1.0m);
        await SetEmploymentWindowAsync(employeeId, start: null, end: lastDay);

        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);

        var outcome = await RunShimAsync(pcs, employeeId, entries, Array.Empty<AbsenceEntry>());

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.Equal(PeriodCalculationService.AuditState.Complete, outcome.AuditState);

        // Zero rule-engine calls for the not-employed span: every call covers [04-01 .. 04-15].
        Assert.NotEmpty(engine.Calls);
        Assert.All(engine.Calls, c =>
        {
            Assert.Equal(Apr01, c.PeriodStart);
            Assert.Equal(lastDay, c.PeriodEnd);
        });

        // Export: one NORMAL_HOURS line per employed weekday entry — INCLUDING 04-15, the last
        // employed day. A boundary hydrated at End (not End + 1) would drop that day's 7.4h.
        var normalLines = outcome.Result.ExportLines.Where(l => l.SourceTimeType == "NORMAL_HOURS").ToList();
        var employedEntries = entries.Where(e => e.Date <= lastDay).ToList();
        Assert.Contains(employedEntries, e => e.Date == lastDay);
        Assert.Equal(employedEntries.Count, normalLines.Count);
        Assert.Equal(employedEntries.Count * DailyHours, normalLines.Sum(l => l.Hours));
        Assert.All(outcome.Result.ExportLines, l => Assert.True(l.PeriodEnd <= lastDay));

        // The persisted manifest: 2 segments; the NOT_EMPLOYED suffix starts at End + 1 and is
        // the ONLY segment whose JSON carries the employmentStatus key.
        var manifest = await LoadManifestAsync(outcome.ManifestId);
        Assert.Contains("EmploymentEnded", manifest.BoundaryCauseSummary);
        Assert.Equal(2, manifest.Segments.Count);

        Assert.Equal("2026-04-01", manifest.Segments[0].GetProperty("startDate").GetString());
        Assert.Equal("2026-04-15", manifest.Segments[0].GetProperty("endDate").GetString());
        Assert.False(manifest.Segments[0].TryGetProperty("employmentStatus", out _),
            "the EMPLOYED segment must serialize WITHOUT the employmentStatus key (byte-parity with pre-S137)");

        Assert.Equal("2026-04-16", manifest.Segments[1].GetProperty("startDate").GetString());
        Assert.Equal("2026-04-30", manifest.Segments[1].GetProperty("endDate").GetString());
        Assert.Equal("NOT_EMPLOYED", manifest.Segments[1].GetProperty("employmentStatus").GetString());
        Assert.Equal("EmploymentEnded", manifest.Segments[1].GetProperty("boundaryCause").GetString());
    }

    // ═════════════════════════════════════════════════════════════════════
    // OQ-1 mirror — mid-month starter
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MidMonthStarter_NotEmployedPrefixInManifest_NoCallsNoLinesBeforeStart_DayStartPays()
    {
        const string employeeId = "EMP-S137-STARTER";
        var hire = new DateOnly(2026, 4, 13); // a Monday — has a 7.4h entry
        // Profile (and agreement-code) rows anchored AT the hire date — the post-S136 shape of a
        // real hire. The dated resolver therefore has NO covering row for the pre-hire span and
        // would return null there: if PCS asked, the fail-closed path would throw. It must not
        // ask (ADR-040 D10) — this makes that claim falsifiable end-to-end.
        await SeedEmployeeAsync(employeeId, partTimeFraction: 1.0m, effectiveFrom: hire);
        await SetEmploymentWindowAsync(employeeId, start: hire, end: null);

        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);

        var outcome = await RunShimAsync(pcs, employeeId, entries, Array.Empty<AbsenceEntry>());

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);

        // Every rule-engine call covers [04-13 .. 04-30] — nothing for the pre-hire prefix.
        Assert.NotEmpty(engine.Calls);
        Assert.All(engine.Calls, c =>
        {
            Assert.Equal(hire, c.PeriodStart);
            Assert.Equal(Apr30, c.PeriodEnd);
        });

        // Day `start` pays: the 04-13 entry is in the export; nothing before it is.
        var normalLines = outcome.Result.ExportLines.Where(l => l.SourceTimeType == "NORMAL_HOURS").ToList();
        var employedEntries = entries.Where(e => e.Date >= hire).ToList();
        Assert.Contains(employedEntries, e => e.Date == hire);
        Assert.Equal(employedEntries.Count, normalLines.Count);
        Assert.Equal(employedEntries.Count * DailyHours, normalLines.Sum(l => l.Hours));
        Assert.All(outcome.Result.ExportLines, l => Assert.True(l.PeriodStart >= hire));

        var manifest = await LoadManifestAsync(outcome.ManifestId);
        Assert.Contains("EmploymentStarted", manifest.BoundaryCauseSummary);
        Assert.Equal(2, manifest.Segments.Count);

        Assert.Equal("2026-04-12", manifest.Segments[0].GetProperty("endDate").GetString()); // start − 1
        Assert.Equal("NOT_EMPLOYED", manifest.Segments[0].GetProperty("employmentStatus").GetString());

        Assert.Equal("2026-04-13", manifest.Segments[1].GetProperty("startDate").GetString());
        Assert.Equal("EmploymentStarted", manifest.Segments[1].GetProperty("boundaryCause").GetString());
        Assert.False(manifest.Segments[1].TryGetProperty("employmentStatus", out _));
    }

    // ═════════════════════════════════════════════════════════════════════
    // EmployeeProfileChange activation + the ADR-031 negative AC
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task MidMonthFractionChange_EmployeeProfileChangeBoundary_NormScalesPerSpan_VacationDayCountFlat()
    {
        const string employeeId = "EMP-S137-FRACTION";
        var changeDate = new DateOnly(2026, 4, 16);
        await SeedEmployeeAsync(employeeId, partTimeFraction: 1.0m);
        await SupersedeFractionAsync(employeeId, newFraction: 0.800m, effectiveFrom: changeDate);
        // Window stays unbounded (both NULL) — every segment EMPLOYED; the split is the
        // profile change alone.

        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);
        // One vacation day per span, Hours = 0 so the stub applies the AbsenceRule formula
        // (7.4 × part_time_fraction) exactly as the real rule would.
        var absences = new List<AbsenceEntry>
        {
            VacationDay(employeeId, new DateOnly(2026, 4, 8)),
            VacationDay(employeeId, new DateOnly(2026, 4, 22)),
        };

        var outcome = await RunShimAsync(pcs, employeeId, entries, absences);

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);

        // The month split at the profile change; both spans EMPLOYED (no employmentStatus key).
        var manifest = await LoadManifestAsync(outcome.ManifestId);
        Assert.Contains("EmployeeProfileChange", manifest.BoundaryCauseSummary);
        Assert.Equal(2, manifest.Segments.Count);
        Assert.Equal("2026-04-16", manifest.Segments[1].GetProperty("startDate").GetString());
        Assert.Equal("EmployeeProfileChange", manifest.Segments[1].GetProperty("boundaryCause").GetString());
        Assert.All(manifest.Segments, s => Assert.False(s.TryGetProperty("employmentStatus", out _)));

        // Each span resolved ITS OWN dated profile (D10 ordering, per-segment resolver read):
        // the rule engine saw fraction 1.0 for [04-01..04-15] and 0.8 for [04-16..04-30].
        Assert.All(engine.Calls.Where(c => c.PeriodStart == Apr01), c => Assert.Equal(1.0m, c.ProfilePartTimeFraction));
        Assert.All(engine.Calls.Where(c => c.PeriodStart == changeDate), c => Assert.Equal(0.8m, c.ProfilePartTimeFraction));
        Assert.Contains(engine.Calls, c => c.PeriodStart == Apr01);
        Assert.Contains(engine.Calls, c => c.PeriodStart == changeDate);

        // ADR-031 negative AC at the payroll boundary: the fraction changes ONLY the hours
        // (norm/pay) on the vacation line — never the DAY-COUNT. One absence day in, one
        // VACATION line out, on BOTH sides of the split.
        var vacationLines = outcome.Result.ExportLines
            .Where(l => l.SourceTimeType == "VACATION")
            .OrderBy(l => l.PeriodStart)
            .ToList();
        Assert.Equal(2, vacationLines.Count);
        Assert.Equal(Apr01, vacationLines[0].PeriodStart);
        Assert.Equal(DailyHours * 1.0m, vacationLines[0].Hours);
        Assert.Equal(changeDate, vacationLines[1].PeriodStart);
        Assert.Equal(DailyHours * 0.8m, vacationLines[1].Hours);

        // Norm lines are per-entry and unaffected in COUNT by the fraction (entry hours are
        // entry hours) — the split moved nothing out of the export.
        var normalLines = outcome.Result.ExportLines.Where(l => l.SourceTimeType == "NORMAL_HOURS").ToList();
        Assert.Equal(entries.Count, normalLines.Count);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Flex carry through a NOT_EMPLOYED middle segment — NON-ZERO delta
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Plan-first (the planner is list-shaped/spells-proof, so two windows give
    /// EMPLOYED / NOT_EMPLOYED / EMPLOYED even though storage holds one spell today). The
    /// flex stub returns a +2.5h NORMAL_HOURS line for the FIRST segment only; the seeded
    /// mapping lets it export, and <c>ExtractFlexDelta</c> carries it. Segment 3's flex call
    /// must receive 12.5 + 2.5 = 15.0 — the skipped span neither resets nor perturbs it.</summary>
    [Fact]
    public async Task TwoSpells_FlexDeltaCarriesThroughNotEmployedMiddleSegment()
    {
        const string employeeId = "EMP-S137-FLEX";
        await SeedEmployeeAsync(employeeId, partTimeFraction: 1.0m);

        var firstSpellEnd = new DateOnly(2026, 4, 10);
        var secondSpellStart = new DateOnly(2026, 4, 20);
        var windows = new[]
        {
            new EmploymentWindow(null, firstSpellEnd),
            new EmploymentWindow(secondSpellStart, null),
        };
        var plan = PeriodPlanner.Plan(
            employeeId: employeeId,
            periodStart: Apr01,
            periodEnd: Apr30,
            calculationKind: "forward-calc",
            ruleSet: TestFixtures.StraddleSafeRuleSet,
            sources: new BoundarySources(
                OkTransitions: Array.Empty<(DateOnly, string, string)>(),
                AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
                PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
                EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
                NonDatedSourceValues: new Dictionary<string, object?>(),
                LocalProfileActivations: null,
                EmploymentStartedDates: new[] { secondSpellStart },
                EmploymentEndedDates: new[] { firstSpellEnd.AddDays(1) }),
            options: PlannerOptions.Default,
            enrollment: TestFixtures.StraddleEnrollment(),
            profile: Profile(employeeId),
            employmentWindows: windows);
        Assert.Equal(3, plan.Segments.Count);
        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, plan.Segments[1].EmploymentStatus);

        const decimal openingBalance = 12.5m;
        const decimal firstSegmentDelta = 2.5m;
        var engine = new RecordingRuleEngine(flexDeltaFor: call => call.PeriodStart == Apr01 ? firstSegmentDelta : 0m);
        var pcs = BuildPcs(engine);

        var outcome = await pcs.CalculateWithOutcomeAsync(
            plan, Profile(employeeId), TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30),
            Array.Empty<AbsenceEntry>(), previousFlexBalance: openingBalance);

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);

        var flexCalls = engine.Calls.Where(c => c.Path.EndsWith("/evaluate-flex", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, flexCalls.Count);
        Assert.Equal(Apr01, flexCalls[0].PeriodStart);
        Assert.Equal(openingBalance, flexCalls[0].PreviousBalance);
        Assert.Equal(secondSpellStart, flexCalls[1].PeriodStart);
        Assert.Equal(openingBalance + firstSegmentDelta, flexCalls[1].PreviousBalance);

        // Nothing touched the not-employed middle span.
        Assert.DoesNotContain(engine.Calls, c => c.PeriodStart > firstSpellEnd && c.PeriodStart < secondSpellStart);

        // The manifest records all three typed segments; only the middle one carries the key.
        var manifest = await LoadManifestAsync(outcome.ManifestId);
        Assert.Equal(3, manifest.Segments.Count);
        Assert.False(manifest.Segments[0].TryGetProperty("employmentStatus", out _));
        Assert.Equal("NOT_EMPLOYED", manifest.Segments[1].GetProperty("employmentStatus").GetString());
        Assert.False(manifest.Segments[2].TryGetProperty("employmentStatus", out _));
    }

    // ─── Seeding ─────────────────────────────────────────────────────────

    private async Task SeedEmployeeAsync(string employeeId, decimal partTimeFraction, DateOnly? effectiveFrom = null)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        // users + employee_profiles + user_agreement_codes — the three rows the dated profile
        // resolver needs; the profile/agreement rows start at effectiveFrom (default
        // '0001-01-01' = history-covering; the starter fact passes the hire date). The users
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
    /// and inserts the successor — the Case C shape, written directly so the test controls
    /// the dates (the repository's supersede path stamps today-relative dates).</summary>
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

    private PeriodCalculationService BuildPcs(RecordingRuleEngine engine)
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

        // The three server-side reads the Payroll host registers (Program.cs): the dated
        // profile resolver (ADR-023), the employment-window resolver (self-managed surface,
        // ADR-040 D7) and the profile-history read feeding EmployeeProfileChange (D5).
        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            _harness.EventStore,
            _harness.Factory,
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryRuleClassificationProvider(TestFixtures.StraddleSafeRuleSet),
            localAgreementProfileRepo: null,
            profileResolver: new EmploymentProfileResolver(_harness.Factory, new UserAgreementCodeRepository(_harness.Factory)),
            employmentWindowResolver: new EmploymentWindowResolver(_harness.Factory),
            employeeProfileRepo: new EmployeeProfileRepository(_harness.Factory));
    }

    private static async Task<PeriodCalculationService.PeriodCalculationOutcome> RunShimAsync(
        PeriodCalculationService pcs,
        string employeeId,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences)
    {
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        return await pcs.CalculateWithOutcomeAsync(
            Profile(employeeId), entries, absences, Apr01, Apr30, previousFlexBalance: 0m);
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

    private static AbsenceEntry VacationDay(string employeeId, DateOnly date) => new()
    {
        EmployeeId = employeeId,
        Date = date,
        AbsenceType = AbsenceTypes.Vacation,
        Hours = 0m, // 0 ⇒ the rule (and the stub modelling it) applies 7.4 × part_time_fraction
        AgreementCode = AgreementCode,
        OkVersion = OkVersion,
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

        // Structural (not textual) read: JSONB normalizes key order, so key PRESENCE is the
        // contract asserted here — the per-writer byte pins live in SegmentSerializationParityTests.
        using var doc = JsonDocument.Parse(reader.GetString(0));
        var segments = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        var summary = ((string[])reader.GetValue(1)).ToList();
        return new PersistedManifest(segments, summary);
    }

    // ─── Recording, rule-modelling stub ──────────────────────────────────

    /// <summary>
    /// Records every rule-engine call (endpoint, segment range, the flex previousBalance, the
    /// resolved profile's part_time_fraction) and answers with rule-shaped payloads:
    /// NORM_CHECK_37H → one NORMAL_HOURS line per entry; absence → one line per absence day
    /// with the real AbsenceRule formula; flex → an optional NORMAL_HOURS delta line.
    /// </summary>
    private sealed class RecordingRuleEngine : HttpMessageHandler
    {
        public sealed record Call(string Path, DateOnly PeriodStart, DateOnly PeriodEnd, decimal? PreviousBalance, decimal ProfilePartTimeFraction);

        private readonly List<Call> _calls = new();
        private readonly Func<Call, decimal> _flexDeltaFor;

        public RecordingRuleEngine(Func<Call, decimal>? flexDeltaFor = null)
            => _flexDeltaFor = flexDeltaFor ?? (_ => 0m);

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

            var fraction = root.TryGetProperty("profile", out var prof)
                && prof.TryGetProperty("partTimeFraction", out var ptf)
                && ptf.ValueKind == JsonValueKind.Number
                ? ptf.GetDecimal()
                : 1.0m;
            var employeeId = prof.ValueKind == JsonValueKind.Object && prof.TryGetProperty("employeeId", out var eid)
                ? eid.GetString() ?? "EMP"
                : "EMP";

            var call = new Call(
                path,
                DateOnly.Parse(root.GetProperty("periodStart").GetString()!),
                DateOnly.Parse(root.GetProperty("periodEnd").GetString()!),
                root.TryGetProperty("previousBalance", out var pb) ? pb.GetDecimal() : null,
                fraction);
            lock (_calls) _calls.Add(call);

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
            {
                // Models AbsenceRule.Evaluate: hours = absence.Hours > 0 ? absence.Hours : 7.4 × fraction.
                var lineItems = root.GetProperty("absences").EnumerateArray().Select(a =>
                {
                    var given = a.GetProperty("hours").GetDecimal();
                    return (object)new
                    {
                        timeType = a.GetProperty("absenceType").GetString(),
                        hours = given > 0 ? given : DailyHours * fraction,
                        rate = 1.0m,
                        date = a.GetProperty("date").GetString(),
                    };
                }).ToList();
                return Json(new { ruleId = "ABSENCE", employeeId, success = true, lineItems });
            }

            if (path.EndsWith("/api/rules/evaluate-flex", StringComparison.Ordinal))
            {
                var delta = _flexDeltaFor(call);
                var lineItems = delta == 0m
                    ? new List<object>()
                    : new List<object>
                    {
                        new { timeType = "NORMAL_HOURS", hours = delta, rate = 1.0m, date = call.PeriodStart.ToString("yyyy-MM-dd") },
                    };
                return Json(new { ruleId = "FLEX_BALANCE", employeeId, success = true, lineItems });
            }

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

    private sealed class InMemoryRuleClassificationProvider : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public InMemoryRuleClassificationProvider(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }
}
