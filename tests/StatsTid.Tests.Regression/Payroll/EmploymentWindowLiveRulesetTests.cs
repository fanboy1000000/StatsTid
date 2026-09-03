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
/// (<c>new RuleRegistry().GetAll()</c> — four AlignedWindow rules, OVERTIME_CALC first).
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
/// The third pin records the LIMITATION the ruling leaves in place, by name:
/// <b>"AlignedWindow norm/overtime rules refuse a profile-change split until reclassified —
/// ADR-016 D4 follow-up"</b> (QUAL-149, the S64 F4-1(b) gap with its true scope). A mid-month
/// part-time-fraction change while employed is two EMPLOYED segments — a genuine split — and
/// the live set refuses it with <see cref="PlannerInvariantViolation"/>. When the norm/overtime
/// rules are reclassified with a pro-rating merger, this pin turns RED and the flip is made
/// consciously.
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
        var pcs = BuildPcs(engine);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);

        // Before the ruling this call threw PlannerInvariantViolation naming OVERTIME_CALC.
        var outcome = await RunShimAsync(pcs, employeeId, entries, Array.Empty<AbsenceEntry>());

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.Equal(PeriodCalculationService.AuditState.Complete, outcome.AuditState);

        // The AlignedWindow rule was evaluated in exactly one EMPLOYED segment and reached
        // RejectIfMultipleSegments with ONE segment — so its merged result is a success, not
        // the "produced N segments but its merge strategy requires exactly one" failure row.
        var overtime = Assert.Single(outcome.Result.RuleResults, r => r.RuleId == OvertimeRule.RuleId);
        Assert.True(overtime.Success, overtime.ErrorMessage);
        Assert.All(outcome.Result.RuleResults, r => Assert.True(r.Success, r.ErrorMessage));

        // No rule-engine call after the end date: every call covers [04-01 .. 04-15].
        Assert.NotEmpty(engine.Calls);
        Assert.All(engine.Calls, c =>
        {
            Assert.Equal(Apr01, c.PeriodStart);
            Assert.Equal(lastDay, c.PeriodEnd);
        });

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
        var pcs = BuildPcs(engine);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);

        var outcome = await RunShimAsync(pcs, employeeId, entries, Array.Empty<AbsenceEntry>());

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.All(outcome.Result.RuleResults, r => Assert.True(r.Success, r.ErrorMessage));

        // Every rule-engine call covers [04-13 .. 04-30] — nothing for the pre-hire prefix.
        Assert.NotEmpty(engine.Calls);
        Assert.All(engine.Calls, c =>
        {
            Assert.Equal(hire, c.PeriodStart);
            Assert.Equal(Apr30, c.PeriodEnd);
        });

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
    // (c) The registered limitation — a profile change WHILE EMPLOYED still
    //     refuses under the live set
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <b>Limitation pinned: "AlignedWindow norm/overtime rules refuse a profile-change split
    /// until reclassified — ADR-016 D4 follow-up" (QUAL-149).</b> A part-time-fraction change
    /// on 04-16 while employed all month is EMPLOYED [04-01..04-15] + EMPLOYED [04-16..04-30]:
    /// two EMPLOYED segments, so OVERTIME_CALC would be evaluated twice and its halves cannot
    /// be merged — the planner refuses before any rule runs. The SAME input calculates under
    /// <see cref="TestFixtures.StraddleSafeRuleSet"/>
    /// (<see cref="EmploymentWindowPayrollTests.MidMonthFractionChange_EmployeeProfileChangeBoundary_NormScalesPerSpan_VacationDayCountFlat"/>),
    /// which is what proves the per-span mechanics work once the rules are reclassified.
    /// If this test turns RED because the month now calculates, the registry was reclassified:
    /// close QUAL-149 consciously and retire this pin.
    /// </summary>
    [Fact]
    public async Task MidMonthFractionChange_LiveRuleSet_RefusesAsRegisteredLimitation()
    {
        const string employeeId = "EMP-S137-LIVE-FRACTION";
        var changeDate = new DateOnly(2026, 4, 16);
        await SeedEmployeeAsync(employeeId, partTimeFraction: 1.0m);
        await SupersedeFractionAsync(employeeId, newFraction: 0.800m, effectiveFrom: changeDate);
        // Window stays unbounded (both NULL) — every segment EMPLOYED; the split is the
        // profile change alone.

        var engine = new RecordingRuleEngine();
        var pcs = BuildPcs(engine);
        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);

        var ex = await Assert.ThrowsAsync<PlannerInvariantViolation>(() =>
            RunShimAsync(pcs, employeeId, entries, Array.Empty<AbsenceEntry>()));

        // The refusal happened at plan time — nothing was evaluated, nothing exported.
        Assert.Empty(engine.Calls);

        // Message contract: rule id, count, cause — and the rule a reader learns from it.
        Assert.Contains($"'{OvertimeRule.RuleId}'", ex.Message);
        Assert.Contains("AlignedWindow", ex.Message);
        Assert.Contains("2 EMPLOYED segments", ex.Message);
        Assert.Contains("EmployeeProfileChange", ex.Message);
        Assert.Contains("employment edge alone", ex.Message);

        // ADR-040 D7: no segment date in the message (the change date would reveal the
        // profile row's effective date; only the period — the caller's own input — appears).
        Assert.DoesNotContain(changeDate.ToString(), ex.Message);
        Assert.DoesNotContain(changeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ex.Message);
        Assert.DoesNotContain(changeDate.AddDays(-1).ToString(), ex.Message);
        Assert.DoesNotContain(changeDate.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ex.Message);
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

        // Identical to EmploymentWindowPayrollTests.BuildPcs except the classification
        // provider: the LIVE registry, not the straddle-safe test set.
        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            _harness.EventStore,
            _harness.Factory,
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new LiveRuleClassificationProvider(),
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
        public sealed record Call(string Path, DateOnly PeriodStart, DateOnly PeriodEnd);

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

            var call = new Call(
                path,
                DateOnly.Parse(root.GetProperty("periodStart").GetString()!, CultureInfo.InvariantCulture),
                DateOnly.Parse(root.GetProperty("periodEnd").GetString()!, CultureInfo.InvariantCulture));
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
