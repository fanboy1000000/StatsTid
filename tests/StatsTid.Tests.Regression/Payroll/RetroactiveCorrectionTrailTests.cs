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
using StatsTid.Tests.Regression.TestSupport;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S145 / TASK-14513 (adjudicator R-11) — the retroactive-correction event records what ACTUALLY RAN.
///
/// <para>
/// Plain language: when HR corrects an already-exported month, the system recalculates it and writes a
/// <c>RetroactiveCorrectionRequested</c> event, the audit record of "this correction was made, under this
/// agreement and position". The recalculation takes the employee's agreement and position from the employee's
/// DATED record for the month (the dated row decides, as in R-10). But the event used to copy the agreement
/// and position out of the request body the caller sent, so a body that disagreed with the record produced an
/// audit trail describing a calculation that never ran. R-11 makes the event take both fields from the
/// calculation's frozen inputs, the same place the engine got them from.
/// </para>
///
/// <para>
/// <b>Docker-gated: CI-verified, not locally green.</b> It needs a real Postgres for the dated employee
/// rows, the export record, and the outbox.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class RetroactiveCorrectionTrailTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;

    private const string OrgId = "ORG_R11";
    private const string EmployeeId = "EMP_R11_TRAIL";
    private const int Year = 2026;
    private const int Month = 7;

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        // Full init.sql: users + employee_profiles + user_agreement_codes + outbox + audit + export records.
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    /// <summary>
    /// <b>R-11.</b> The employee's dated rows for July 2026 say agreement AC and position DEPARTMENT_HEAD, and
    /// the month has been exported. <c>RecalculateAsync</c> is called with a body profile saying HK and
    /// RESEARCHER. The captured <c>RetroactiveCorrectionRequested</c> must say AC and DEPARTMENT_HEAD: what the
    /// calculation ran under, not what the request carried.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker, so argued, not run locally). On <c>9984173</c>
    /// <c>RetroactiveCorrectionService.RecalculateAsync</c> builds the event with
    /// <c>AgreementCode = profile.AgreementCode</c> and <c>Position = profile.Position</c> (the request
    /// body's), so the stored payload reads <c>"HK"</c> and <c>"RESEARCHER"</c> and the first assertion fails
    /// with Expected "AC", Actual "HK". The production mutation that reproduces it after the fix-up is
    /// "take the event's two fields from the caller's profile again instead of
    /// <c>FirstEmployedSegmentPiece(outcome.PlanInputs.Pieces, periodStart)</c>"; it trips the same
    /// assertion (and the position assertion).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Recalculate_RequestProfileDiffersFromDatedRecord_EventRecordsDatedAgreementAndPosition()
    {
        // S145 R-11: the dated record says (AC, DEPARTMENT_HEAD) for the whole month.
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await RegressionSeed.SeedEmployeeAsync(
                conn,
                employeeId: EmployeeId,
                orgId: OrgId,
                agreementCode: "AC",
                okVersion: "OK26",
                position: "DEPARTMENT_HEAD");
        }

        var periodStart = new DateOnly(Year, Month, 1);
        var periodEnd = new DateOnly(Year, Month, DateTime.DaysInMonth(Year, Month));

        // The month is exported (a correction needs an export record to diff against).
        await SeedExportRecordAsync(new PayrollExportLine
        {
            EmployeeId = EmployeeId,
            WageType = "SLS_0110",
            Hours = 10m,
            Amount = 1000m,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            OkVersion = "OK26",
            SourceTimeType = "NORMAL_HOURS",
        });

        var service = BuildCorrectionService(
            Segmentation.TestFixtures.WeekdayEntriesForPeriod(EmployeeId, periodStart, periodEnd));

        // The BODY profile disagrees with the dated record on both fields.
        var body = new EmploymentProfile
        {
            EmployeeId = EmployeeId,
            AgreementCode = "HK",
            OkVersion = "OK26",
            EmploymentCategory = "FULL_TIME",
            Position = "RESEARCHER",
            OrgId = OrgId,
        };

        var result = await service.RecalculateAsync(
            body, periodStart, periodEnd,
            reason: "R-11: the event records what ran",
            actorId: "admin-r11");
        Assert.True(result.Success, result.ErrorMessage);

        var payload = await ReadCorrectionEventPayloadAsync();
        using var doc = JsonDocument.Parse(payload);
        Assert.Equal("AC", doc.RootElement.GetProperty("agreementCode").GetString());
        Assert.Equal("DEPARTMENT_HEAD", doc.RootElement.GetProperty("position").GetString());
    }

    /// <summary>
    /// <b>R-19.</b> A correction for a month in which the employee has NO employed day (the employment window
    /// ended before the month) still writes a manifest, and the <c>RetroactiveCorrectionRequested</c> event must
    /// LINK it: its <c>manifestId</c> is the id of the <c>segment_manifests</c> row written for that employee and
    /// period, never the empty GUID.
    ///
    /// <para>
    /// <b>Red conditions</b> (Docker, so argued, not run locally). On <c>b25bf24</c>
    /// <c>RetroactiveCorrectionService.RecalculateAsync</c> derives the event's manifest id from
    /// <c>result.RuleResults.FirstOrDefault()?.ManifestId</c>; a month with no employed segment produces no rule
    /// results, so the event is written with <c>Guid.Empty</c> and the assertion
    /// <c>Assert.NotEqual(Guid.Empty, linked)</c> fails (Expected: not 00000000-0000-0000-0000-000000000000).
    /// The production mutation that reproduces it after the fix-up is "derive the event's manifest id from the
    /// first rule result again instead of <c>outcome.ManifestId</c>"; it trips the same assertion.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Recalculate_NoEmployedSegment_EventLinksPersistedManifest()
    {
        // S145 R-19: the employee's window ENDED before July (last employed day 30 June).
        await using (var conn = _harness.Factory.Create())
        {
            await conn.OpenAsync();
            await RegressionSeed.SeedEmployeeAsync(
                conn,
                employeeId: EmployeeId,
                orgId: OrgId,
                agreementCode: "AC",
                okVersion: "OK26",
                position: "DEPARTMENT_HEAD");
            await using var window = new NpgsqlCommand(
                "UPDATE users SET employment_start_date = NULL, employment_end_date = @e WHERE user_id = @id", conn);
            window.Parameters.AddWithValue("e", new DateTime(Year, Month - 1, 30));
            window.Parameters.AddWithValue("id", EmployeeId);
            Assert.Equal(1, await window.ExecuteNonQueryAsync());
        }

        var periodStart = new DateOnly(Year, Month, 1);
        var periodEnd = new DateOnly(Year, Month, DateTime.DaysInMonth(Year, Month));

        await SeedExportRecordAsync(new PayrollExportLine
        {
            EmployeeId = EmployeeId,
            WageType = "SLS_0110",
            Hours = 10m,
            Amount = 1000m,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            OkVersion = "OK26",
            SourceTimeType = "NORMAL_HOURS",
        });

        var service = BuildCorrectionService(
            Segmentation.TestFixtures.WeekdayEntriesForPeriod(EmployeeId, periodStart, periodEnd));

        var body = new EmploymentProfile
        {
            EmployeeId = EmployeeId,
            AgreementCode = "AC",
            OkVersion = "OK26",
            EmploymentCategory = "FULL_TIME",
            Position = "DEPARTMENT_HEAD",
            OrgId = OrgId,
        };

        var result = await service.RecalculateAsync(
            body, periodStart, periodEnd,
            reason: "R-19: the event links the manifest it wrote",
            actorId: "admin-r19");
        Assert.True(result.Success, result.ErrorMessage);

        var payload = await ReadCorrectionEventPayloadAsync();
        using var doc = JsonDocument.Parse(payload);
        var linked = doc.RootElement.GetProperty("manifestId").GetGuid();
        Assert.NotEqual(Guid.Empty, linked);

        await using var read = new NpgsqlConnection(_harness.ConnectionString);
        await read.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT manifest_id FROM segment_manifests
            WHERE employee_id = @emp AND period_start = @ps AND period_end = @pe
            """, read);
        cmd.Parameters.AddWithValue("emp", EmployeeId);
        cmd.Parameters.AddWithValue("ps", new DateTime(Year, Month, 1));
        cmd.Parameters.AddWithValue("pe", new DateTime(Year, Month, DateTime.DaysInMonth(Year, Month)));
        var persisted = Assert.IsType<Guid>(await cmd.ExecuteScalarAsync());
        Assert.Equal(persisted, linked);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A correction service over a PCS that resolves the employee's DATED profile and employment window from
    /// the database (the production wiring), a stub rule engine and an in-memory hours reader holding the
    /// month's weekday entries.
    /// </summary>
    private RetroactiveCorrectionService BuildCorrectionService(IEnumerable<TimeEntry> entries)
    {
        var factory = _harness.Factory;
        var eventStore = new PostgresEventStore(factory, new OutboxServiceContext("payroll"));
        var calc = new PeriodCalculationService(
            new SingleClientFactory(new Segmentation.TestFixtures.StubHandler(
                Segmentation.TestFixtures.DefaultRuleEngineHandler)),
            new PayrollMappingService(
                factory, NullLogger<PayrollMappingService>.Instance, new WageTypeMappingRepository(factory)),
            new PostgresSegmentManifestStore(factory, eventStore, eventStore),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
            }).Build(),
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryRuleClassificationProvider(Segmentation.TestFixtures.RuleSet),
            localAgreementProfileRepo: null,
            profileResolver: new EmploymentProfileResolver(factory, new UserAgreementCodeRepository(factory)),
            employmentWindowResolver: new EmploymentWindowResolver(factory),
            hoursReader: new InMemoryRecordedHoursReader(entries),
            priorPeriodApprovalReader: new Segmentation.TestFixtures.AlwaysApprovedPriorPeriods(),
            // S146 R-14: a correction/ledger fact over the database: the production reader over payroll_export_records.
            flexLedgerReader: new PostgresFlexLedgerReader(factory));

        return new RetroactiveCorrectionService(
            calc,
            factory,
            eventStore,
            new AuditProjectionRepository(factory),
            new RetroactiveCorrectionRequestedAuditMapper(),
            new PayrollExportRecordRepository(factory),
            NullLogger<RetroactiveCorrectionService>.Instance);
    }

    /// <summary>Seeds a first-export record (original == current), as the manifest tests do.</summary>
    private async Task SeedExportRecordAsync(PayrollExportLine line)
    {
        var ordered = PayrollExportManifest.OrderLines(new[] { line });
        var json = PayrollExportManifest.Serialize(ordered);
        var hash = PayrollExportManifest.ComputeContentHash(ordered);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
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
        cmd.Parameters.AddWithValue("emp", EmployeeId);
        cmd.Parameters.AddWithValue("year", Year);
        cmd.Parameters.AddWithValue("month", Month);
        cmd.Parameters.Add(new NpgsqlParameter("lines", NpgsqlDbType.Jsonb) { Value = json });
        cmd.Parameters.AddWithValue("hash", hash);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string> ReadCorrectionEventPayloadAsync()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT event_payload::text FROM outbox_events
            WHERE event_type = 'RetroactiveCorrectionRequested' AND stream_id LIKE @s
            ORDER BY outbox_id DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("s", $"retro-correction-{EmployeeId}-%");
        var payload = await cmd.ExecuteScalarAsync() as string;
        Assert.NotNull(payload);
        return payload!;
    }

    private sealed class InMemoryRuleClassificationProvider : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public InMemoryRuleClassificationProvider(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }
}
