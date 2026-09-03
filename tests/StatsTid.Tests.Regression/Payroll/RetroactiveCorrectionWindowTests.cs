using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Hosting;
using StatsTid.Tests.Regression.Segmentation;
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S138 / TASK-13806 — the retroactive-correction seam INHERITS the employment window
/// (ADR-040 D5) through the planless shim, pinned end-to-end against a real Postgres.
/// Routed here from the S137 Step-7a Reviewer NOTE: "the retroactive-correction seam inherits
/// window typing through the shim (a positive: corrections of a month whose end date was
/// recorded after export will claw back post-end days) but is UNPINNED."
///
/// <para>
/// Plain-language, the story this test tells: a month is calculated and sent to payroll while
/// the employee's record has NO end date (a "windowless" month — every day paid). Afterwards
/// HR records that the employee's LAST day was actually the 15th. A payroll correction of that
/// month must then (1) re-run the calculation over ONLY the employed span [1st .. 15th] — the
/// 15th itself still pays (ADR-040 D1: the end date is the last EMPLOYED day); (2) produce
/// correction lines whose deltas are NEGATIVE and equal exactly to the post-end days' hours —
/// the pre-end days are unchanged, so nothing is clawed back for them; (3) advance the diff
/// baseline to the truncated month (S90 / B3, so a second correction does not claw back twice);
/// and (4) persist a manifest whose NOT_EMPLOYED suffix starts on the 16th with the
/// <c>EmploymentEnded</c> cause — the audit trail says WHY the month got shorter.
/// </para>
///
/// <para>
/// <b>Granularity note (load-bearing for reading the assertions).</b> A correction line is the
/// wire shape of <c>RetroactiveCorrectionService.ProduceCorrectionLines</c>: ONE line per wage
/// type over the WHOLE period, carrying <c>OriginalHours</c> / <c>CorrectedHours</c> /
/// <c>DifferenceHours</c> as period sums. "Negative only for post-end days, pre-end days
/// unchanged" is therefore asserted as sums — <c>CorrectedHours == Σ(pre-end entries)</c> (pre-end
/// untouched, the 15th included) and <c>DifferenceHours == −Σ(post-end entries)</c> (exactly the
/// post-end days, nothing else) — plus the per-day shape underneath: every rule-engine call of
/// the re-run covers [1st .. 15th] and every corrected export line ends on or before the 15th.
/// </para>
///
/// <para>
/// <b>Why this would have been RED before S137.</b> Pre-S137 the shim planned the whole month
/// EMPLOYED regardless of <c>users.employment_end_date</c>, so the re-run reproduced the original
/// lines byte-for-byte and the correction produced ZERO lines — <c>Assert.Single</c> below fails
/// on an empty list. It is GREEN on the current tree by construction (the seam was never given a
/// separate code path); this pin keeps it that way when the [Obsolete] shim is retired
/// (TASK-2010) and the correction service constructs its plan explicitly.
/// </para>
///
/// <para>
/// Harness: the S137 payroll harness (<see cref="TestFixtures.DockerHarness"/> + full init.sql,
/// <see cref="RegressionSeed.SeedEmployeeAsync"/>, the seeded wage-type mappings, the
/// straddle-safe classification set, a recording rule-engine stub) plus the S90 correction
/// wiring (<see cref="RetroactiveCorrectionService"/> over the SAME window-hydrated PCS). The
/// "export" step persists the first-export record with the production manifest serializer, the
/// established S90 idiom (<c>RetroactiveCorrectionManifestTests.SeedExportRecordAsync</c>) — the
/// export SERVICE's own approval-period / delivery gates are pinned elsewhere and add nothing to
/// the window question.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RetroactiveCorrectionWindowTests : IAsyncLifetime
{
    private const string OrgId = "STY_S138_RETRO";
    private const string AgreementCode = "HK";
    private const string OkVersion = "OK24";

    // April 2026: the OK24→OK26 transition (2026-04-01) IS the period start, so it is not an
    // interior boundary — the only split below comes from the recorded end date.
    private static readonly DateOnly Apr01 = new(2026, 4, 1);
    private static readonly DateOnly Apr30 = new(2026, 4, 30);
    private const int Year = 2026;
    private const int Month = 4;

    private TestFixtures.DockerHarness _harness = null!;
    private PostgresEventStore _eventStore = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        // Full init.sql: users (employment dates), employee_profiles, user_agreement_codes,
        // organizations, payroll_export_records, outbox_events, audit_projection — the
        // segmentation DockerHarness DDL is the 4-table subset only.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
        // The payroll host's outbox context — the same store feeds PCS (manifest events) and the
        // correction service (the RetroactiveCorrectionRequested enqueue).
        _eventStore = new PostgresEventStore(_harness.Factory, new OutboxServiceContext("payroll"));
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    [Fact]
    public async Task EndDateRecordedAfterExport_CorrectionClawsBackOnlyPostEndDays_ManifestCarriesEmploymentEndedSuffix()
    {
        const string employeeId = "EMP-S138-RETRO-LEAVER";
        var lastDay = new DateOnly(2026, 4, 15); // a Wednesday — has a 7.4h entry
        await SeedEmployeeAsync(employeeId); // users row window both-NULL (unbounded, ADR-040 D2)

        var entries = TestFixtures.WeekdayEntriesForPeriod(employeeId, Apr01, Apr30);
        var preEndEntries = entries.Where(e => e.Date <= lastDay).ToList();
        var postEndEntries = entries.Where(e => e.Date > lastDay).ToList();
        Assert.Contains(preEndEntries, e => e.Date == lastDay); // the D1 fencepost is exercised
        Assert.NotEmpty(postEndEntries);

        // ── 1. The ORIGINAL export — a WINDOWLESS month: every weekday pays ──────────────
        var exportEngine = new RecordingRuleEngine();
        var exportOutcome = await RunShimAsync(BuildPcs(exportEngine), employeeId, entries);
        Assert.True(exportOutcome.Result.Success, exportOutcome.Result.ErrorMessage);
        Assert.Equal(PeriodCalculationService.AuditState.Complete, exportOutcome.AuditState);

        var originalLines = exportOutcome.Result.ExportLines;
        var originalNormalHours = originalLines.Where(l => l.SourceTimeType == "NORMAL_HOURS").Sum(l => l.Hours);
        Assert.Equal(entries.Sum(e => e.Hours), originalNormalHours);
        Assert.All(exportEngine.Calls, c =>
        {
            Assert.Equal(Apr01, c.PeriodStart);
            Assert.Equal(Apr30, c.PeriodEnd);
        });

        // The windowless manifest: one segment, no employment cause (the byte-identity shape).
        var exportManifest = await LoadManifestAsync(exportOutcome.ManifestId);
        Assert.DoesNotContain("EmploymentEnded", exportManifest.BoundaryCauseSummary);
        Assert.Single(exportManifest.Segments);
        Assert.False(exportManifest.Segments[0].TryGetProperty("employmentStatus", out _));

        await PersistFirstExportRecordAsync(employeeId, originalLines);

        // ── 2. HR records the end date AFTER the export ─────────────────────────────────
        await SetEmploymentWindowAsync(employeeId, start: null, end: lastDay);

        // ── 3. The correction — driven exactly as /api/payroll/recalculate drives it ─────
        var correctionEngine = new RecordingRuleEngine();
        var service = BuildCorrectionService(BuildPcs(correctionEngine));

        var result = await service.RecalculateAsync(
            Profile(employeeId),
            entries,
            Array.Empty<AbsenceEntry>(),
            periodStart: Apr01,
            periodEnd: Apr30,
            previousFlexBalance: 0m,
            reason: "S138 window pin — end date recorded after export",
            actorId: "hr-s138");

        Assert.True(result.Success, result.ErrorMessage);

        // (1) The re-run evaluated ONLY the employed span: every rule-engine call covers
        //     [04-01 .. 04-15] — none for the post-end days, and the 15th is inside.
        Assert.NotEmpty(correctionEngine.Calls);
        Assert.All(correctionEngine.Calls, c =>
        {
            Assert.Equal(Apr01, c.PeriodStart);
            Assert.Equal(lastDay, c.PeriodEnd);
        });

        // (2) Deltas are NEGATIVE and equal exactly the post-end days; the pre-end days are
        //     unchanged (CorrectedHours == the pre-end sum, the last employed day INCLUDED).
        var line = Assert.Single(result.CorrectionLines);
        Assert.Equal("NORMAL_HOURS", line.SourceTimeType);
        Assert.Equal(originalNormalHours, line.OriginalHours);
        Assert.Equal(preEndEntries.Sum(e => e.Hours), line.CorrectedHours);
        Assert.Equal(-postEndEntries.Sum(e => e.Hours), line.DifferenceHours);
        Assert.True(line.DifferenceHours < 0m, "the correction must claw back, never add");
        Assert.True(line.DifferenceAmount <= 0m);
        Assert.Equal(Apr01, line.PeriodStart);
        Assert.Equal(Apr30, line.PeriodEnd); // the correction line spans the corrected MONTH (wire shape)

        // (3) The diff baseline advanced to the truncated month (S90 / B3): a second correction
        //     would diff against the shortened month — no double claw-back. Every effective line
        //     now ends on or before the last employed day.
        var baseline = await new PayrollExportRecordRepository(_harness.Factory)
            .TryReadCurrentEffectiveLinesAsync(employeeId, Year, Month);
        Assert.NotNull(baseline);
        Assert.Equal(preEndEntries.Sum(e => e.Hours),
            baseline!.Where(l => l.SourceTimeType == "NORMAL_HOURS").Sum(l => l.Hours));
        Assert.All(baseline, l => Assert.True(l.PeriodEnd <= lastDay));
        var (originalJson, currentJson) = await ReadRecordRowAsync(employeeId);
        Assert.NotEqual(originalJson, currentJson); // original_lines immutable, current advanced

        // (4) The correction's manifest carries the EmploymentEnded-caused NOT_EMPLOYED suffix
        //     from End + 1; the employed prefix serializes WITHOUT the employmentStatus key.
        var correctionManifest = await LoadOtherManifestAsync(employeeId, exceptManifestId: exportOutcome.ManifestId);
        Assert.Contains("EmploymentEnded", correctionManifest.BoundaryCauseSummary);
        Assert.Equal(2, correctionManifest.Segments.Count);

        Assert.Equal("2026-04-01", correctionManifest.Segments[0].GetProperty("startDate").GetString());
        Assert.Equal("2026-04-15", correctionManifest.Segments[0].GetProperty("endDate").GetString());
        Assert.False(correctionManifest.Segments[0].TryGetProperty("employmentStatus", out _),
            "the EMPLOYED segment must serialize WITHOUT the employmentStatus key (byte-parity)");

        Assert.Equal("2026-04-16", correctionManifest.Segments[1].GetProperty("startDate").GetString());
        Assert.Equal("2026-04-30", correctionManifest.Segments[1].GetProperty("endDate").GetString());
        Assert.Equal("NOT_EMPLOYED", correctionManifest.Segments[1].GetProperty("employmentStatus").GetString());
        Assert.Equal("EmploymentEnded", correctionManifest.Segments[1].GetProperty("boundaryCause").GetString());
    }

    // ─── Seeding ─────────────────────────────────────────────────────────

    private async Task SeedEmployeeAsync(string employeeId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        // users + employee_profiles + user_agreement_codes (history-covering '0001-01-01' rows)
        // + the organizations FK parent. The users row's window is both-NULL until
        // SetEmploymentWindowAsync narrows it.
        await RegressionSeed.SeedEmployeeAsync(
            conn, employeeId, OrgId, AgreementCode, OkVersion, partTimeFraction: 1.0m);
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

    /// <summary>
    /// Persists the FIRST-export record (<c>original_lines == current_effective_lines</c>, the
    /// first-export invariant) with the production manifest serializer — the S90 idiom
    /// (<c>RetroactiveCorrectionManifestTests.SeedExportRecordAsync</c>). This is the row the
    /// correction reads its diff baseline from and advances.
    /// </summary>
    private async Task PersistFirstExportRecordAsync(string employeeId, IReadOnlyList<PayrollExportLine> lines)
    {
        var ordered = PayrollExportManifest.OrderLines(lines);
        var json = PayrollExportManifest.Serialize(ordered);
        var hash = PayrollExportManifest.ComputeContentHash(ordered);

        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO payroll_export_records (
                export_id, period_id, employee_id, year, month, exported_at,
                original_lines, current_effective_lines, content_hash, source
            ) VALUES (
                @id, NULL, @emp, @year, @month, NOW(),
                @lines::jsonb, @lines::jsonb, @hash, 'CALCULATE_AND_EXPORT'
            )
            """, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("year", Year);
        cmd.Parameters.AddWithValue("month", Month);
        cmd.Parameters.Add(new NpgsqlParameter("lines", NpgsqlDbType.Jsonb) { Value = json });
        cmd.Parameters.AddWithValue("hash", hash);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private async Task<(string Original, string Current)> ReadRecordRowAsync(string employeeId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT original_lines::text, current_effective_lines::text
            FROM payroll_export_records
            WHERE employee_id = @e AND year = @y AND month = @m
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("y", Year);
        cmd.Parameters.AddWithValue("m", Month);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "no payroll_export_records row");
        return (reader.GetString(0), reader.GetString(1));
    }

    // ─── Wiring (the production DI shapes, hand-built) ───────────────────

    /// <summary>The S137 payroll-host PCS shape: the dated profile resolver (ADR-023), the
    /// employment-window resolver (self-managed surface, ADR-040 D7) and the profile-history
    /// read (D5) — over the straddle-safe classification set so any split is plannable.</summary>
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

        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            _eventStore,
            _harness.Factory,
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryRuleClassificationProvider(TestFixtures.StraddleSafeRuleSet),
            localAgreementProfileRepo: null,
            profileResolver: new EmploymentProfileResolver(_harness.Factory, new UserAgreementCodeRepository(_harness.Factory)),
            employmentWindowResolver: new EmploymentWindowResolver(_harness.Factory),
            employeeProfileRepo: new EmployeeProfileRepository(_harness.Factory));
    }

    /// <summary>The S90 correction-service shape over the supplied (window-hydrated) PCS.</summary>
    private RetroactiveCorrectionService BuildCorrectionService(PeriodCalculationService pcs)
        => new(
            pcs,
            _harness.Factory,
            _eventStore,
            new AuditProjectionRepository(_harness.Factory),
            new RetroactiveCorrectionRequestedAuditMapper(),
            new PayrollExportRecordRepository(_harness.Factory),
            NullLogger<RetroactiveCorrectionService>.Instance);

    private static async Task<PeriodCalculationService.PeriodCalculationOutcome> RunShimAsync(
        PeriodCalculationService pcs, string employeeId, IReadOnlyList<TimeEntry> entries)
    {
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export and /recalculate).
        return await pcs.CalculateWithOutcomeAsync(
            Profile(employeeId), entries, Array.Empty<AbsenceEntry>(), Apr01, Apr30, previousFlexBalance: 0m);
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

    // ─── Manifest projection reads ───────────────────────────────────────

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
        return ReadManifest(reader, segmentsOrdinal: 0, summaryOrdinal: 1);
    }

    /// <summary>
    /// The correction's manifest. <c>RetroactiveCorrectionResult</c> does not expose the manifest
    /// id (it travels on the <c>RetroactiveCorrectionRequested</c> event), so the projection is read
    /// by employee, excluding the original export's manifest — exactly one other row must exist.
    /// </summary>
    private async Task<PersistedManifest> LoadOtherManifestAsync(string employeeId, Guid exceptManifestId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT segments_jsonb::text, boundary_cause_summary
            FROM segment_manifests
            WHERE employee_id = @employeeId AND manifest_id <> @except
            """, conn);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        cmd.Parameters.AddWithValue("except", exceptManifestId);
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "the correction persisted no segment_manifests row");
        var manifest = ReadManifest(reader, segmentsOrdinal: 0, summaryOrdinal: 1);
        Assert.False(await reader.ReadAsync(), "expected exactly ONE correction manifest for the employee");
        return manifest;
    }

    private static PersistedManifest ReadManifest(NpgsqlDataReader reader, int segmentsOrdinal, int summaryOrdinal)
    {
        // Structural (not textual) read: JSONB normalizes key order, so key PRESENCE is the
        // contract asserted here — the per-writer byte pins live in SegmentSerializationParityTests.
        using var doc = JsonDocument.Parse(reader.GetString(segmentsOrdinal));
        var segments = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        var summary = ((string[])reader.GetValue(summaryOrdinal)).ToList();
        return new PersistedManifest(segments, summary);
    }

    // ─── Recording rule-engine stub ──────────────────────────────────────

    /// <summary>
    /// Records every rule-engine call's (endpoint, segment range) so "no call after the end
    /// date" is a direct assertion, then answers with the suite's shared default stub
    /// (<see cref="TestFixtures.DefaultRuleEngineHandler"/>: NORM_CHECK_37H → one NORMAL_HOURS
    /// line per entry; every other rule → a line-less success). The request body is buffered by
    /// <see cref="HttpContent"/> on first read, so the delegate's own read sees the same bytes.
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
            using (var doc = JsonDocument.Parse(body))
            {
                var root = doc.RootElement;
                var call = new Call(
                    request.RequestUri?.AbsolutePath ?? string.Empty,
                    DateOnly.Parse(root.GetProperty("periodStart").GetString()!, CultureInfo.InvariantCulture),
                    DateOnly.Parse(root.GetProperty("periodEnd").GetString()!, CultureInfo.InvariantCulture));
                lock (_calls) _calls.Add(call);
            }

            return TestFixtures.DefaultRuleEngineHandler(request);
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
