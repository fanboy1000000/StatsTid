using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.Tests.Regression.Payroll;
using static StatsTid.Tests.Regression.Payroll.FlexLedgerHostFixture;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// S146 / TASK-14606 (QUAL-188; AC-C7; rulings R-33 item 2, R42) — a replay reproduces the flex balance a month was paid on.
///
/// <para>
/// <b>Plain language.</b> A replay is the proof, after the fact, that a payroll figure is what the rules give for the inputs
/// it had. The month's opening flex balance is one of those inputs: it is DERIVED from the recorded ledger when the month is
/// calculated, and then FROZEN in the manifest (with where it came from). A replay must run on that frozen opening, never ask
/// today's ledger, and give back the same closing balance the export record stored. If it asked today's ledger, a later
/// correction of the previous month would change what the replay of this month shows.
/// </para>
///
/// <para>
/// <b>Harness.</b> <see cref="FlexLedgerHostFixture"/>: the month is calculated and exported through the real Payroll host with
/// the configured-closing stub engine; the replay then runs on a SEPARATE <see cref="PeriodCalculationService"/> built over the
/// same database with NO ledger reader and NO approval reader (a replay never reads either, R42), with the same stub engine.
/// Docker-gated: <b>CI-pending</b> (first green at CI-W2, R-39).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FlexLedgerReplayTests : IClassFixture<FlexLedgerHostFixture>
{
    private static readonly DateOnly May = new(2026, 5, 1);
    private static readonly DateOnly Jun = new(2026, 6, 1);

    private readonly FlexLedgerHostFixture _fx;

    public FlexLedgerReplayTests(FlexLedgerHostFixture fixture) => _fx = fixture;

    /// <summary>A PCS over the fixture's database for replay only: no ledger reader, no approval reader (R42).</summary>
    private PeriodCalculationService BuildReplayPcs()
    {
        var factory = _fx.Db;
        var eventStore = new PostgresEventStore(factory, new OutboxServiceContext("payroll"));
        var mapping = new PayrollMappingService(
            factory, NullLogger<PayrollMappingService>.Instance, new WageTypeMappingRepository(factory));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceUrls:RuleEngine"] = "http://rule-engine.test" })
            .Build();
        return new PeriodCalculationService(
            new SingleClientFactory(new TestFixtures.StubHandler(_fx.Engine.Handle)),
            mapping,
            new PostgresSegmentManifestStore(factory, eventStore, eventStore),
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryClassifications(new RuleRegistry().GetAll()),
            hoursReader: new ProjectionHoursReader(
                factory, new TimeEntryProjectionRepository(factory), new AbsenceProjectionRepository(factory)),
            priorPeriodApprovalReader: null,
            flexLedgerReader: null);
    }

    private async Task<Guid> ManifestIdAsync(string employeeId, string calculationKind)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            """
            SELECT manifest_id FROM segment_manifests
            WHERE employee_id = @e AND calculation_kind = @k
            ORDER BY created_at DESC LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("e", employeeId);
        cmd.Parameters.AddWithValue("k", calculationKind);
        var id = await cmd.ExecuteScalarAsync();
        Assert.True(id is Guid, $"no {calculationKind} manifest for {employeeId}");
        return (Guid)id!;
    }

    /// <summary>
    /// AC-C7 (kind F, Docker; R-33 item 2, R42). June is calculated through the host with a DERIVED opening: May is the ledger
    /// start with a recorded closing of 12.5, so June is <c>CHAINED 12.5</c>, and the engine closes it at 4 (a value distinct
    /// from the opening). The export record holds (12.5, 4, CHAINED). A replay of June's manifest, on a service with no ledger
    /// reader, returns that same closing 4, the frozen opening 12.5 and the frozen provenance <c>CHAINED</c>.
    ///
    /// <para><b>Red conditions (5a fix-up: the closing is now opening-dependent).</b> The stub closes the month at the opening
    /// it is sent minus 8.5, so 12.5 gives the recorded 4. A replay that sends the engine anything but the frozen 12.5 (0, or a
    /// number read from today's ledger) answers a DIFFERENT closing (<c>replay.ClosingFlexBalance != record.Closing</c>) and
    /// fails the assertion that the replay's own timeline call carried <c>previousFlexBalance == 12.5</c>. A replay that read the
    /// ledger would throw (this service has no ledger reader, R42). A manifest that did not freeze the provenance leaves
    /// <c>replay.FlexProvenance</c> null. Pre-S146 <c>record.Closing</c> does not exist.</para>
    /// </summary>
    [Fact]
    public async Task Replay_DerivedOpening_ReproducesRecordedClosing()
    {
        const string employeeId = "EMP-S146-REPLAY-CHAIN";
        await _fx.SeedEmployeeAsync(employeeId);
        await _fx.SeedLedgerRecordAsync(employeeId, May, closing: 12.5m);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedWeekdayHoursAsync(employeeId, Jun, new DateOnly(2026, 6, 30));
        // OPENING-DEPENDENT closing (5a fix-up): closing = the opening the engine is sent - 8.5, i.e. 4 at 12.5.
        _fx.Engine.ConfigureOpeningDependent(employeeId, Jun, closingDelta: -8.5m);
        var exported = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);
        Assert.True(exported.Status == HttpStatusCode.OK, $"forward export: {exported.Json}");
        var record = await _fx.RequireRecordAsync(employeeId, Jun);
        Assert.Equal<(decimal?, decimal?, string?)>((12.5m, 4m, "CHAINED"), (record.Opening, record.Closing, record.Provenance));
        var manifestId = await ManifestIdAsync(employeeId, "forward-calc");

        var replay = await BuildReplayPcs().ReplayAsync(manifestId);

        Assert.True(replay.Success, replay.ErrorMessage);
        Assert.Equal(record.Closing, replay.ClosingFlexBalance);
        Assert.Equal(record.Opening, replay.OpeningFlexBalance);
        Assert.Equal("CHAINED", replay.FlexProvenance);
        // The replay's OWN timeline call (the second for this employee and month) was sent the frozen 12.5.
        var calls = _fx.Engine.TimelineCalls.Where(c => c.EmployeeId == employeeId).ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal(12.5m, calls[0].PreviousFlexBalance);
        Assert.Equal(12.5m, calls[1].PreviousFlexBalance);
    }

    /// <summary>
    /// R-33 item 2 (kind F, Docker). A month calculated UNTRACKED (a raw-recorded month before the ledger start, here May with
    /// June the start) pays no flex: the correction's calculation dropped the engine's 6 h <c>FLEX_PAYOUT</c> from its export lines,
    /// and a replay of that manifest drops it the same way: no <c>FLEX_PAYOUT</c> export line, provenance <c>UNTRACKED</c>, no
    /// balances, while the rule report still shows what the engine computed (so the absence is not just "the engine paid none").
    ///
    /// <para><b>Red conditions.</b> A replay that did not know the frozen provenance would map the engine's payout into an export
    /// line (an export line with <c>SourceTimeType == "FLEX_PAYOUT"</c> appears), tripping the first assertion; a manifest that
    /// froze no provenance leaves <c>replay.FlexProvenance</c> null.</para>
    /// </summary>
    [Fact]
    public async Task Replay_UntrackedManifest_DropsFlexPayoutAsTheForwardRunDid()
    {
        const string employeeId = "EMP-S146-REPLAY-UNTRACKED";
        await _fx.SeedEmployeeAsync(employeeId);
        await _fx.SeedApprovedMonthAsync(employeeId, new DateOnly(2026, 4, 1));
        await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 1m);
        await _fx.SeedRawRecordAsync(employeeId, May);
        _fx.Engine.Configure(employeeId, May, closing: 50m, payoutHours: 6m);
        var corrected = await _fx.RecalculateMonthAsync(employeeId, May);
        Assert.True(corrected.Status == HttpStatusCode.OK, $"forward correction: {corrected.Json}");
        Assert.Equal("UNTRACKED", ReadCorrection(corrected.Json).FlexProvenance);
        var manifestId = await ManifestIdAsync(employeeId, "forward-calc");

        var replay = await BuildReplayPcs().ReplayAsync(manifestId);

        Assert.True(replay.Success, replay.ErrorMessage);
        Assert.DoesNotContain(replay.ExportLines, l => l.SourceTimeType == "FLEX_PAYOUT");
        Assert.Contains(replay.RuleResults.SelectMany(r => r.LineItems), li => li.TimeType == "FLEX_PAYOUT");
        Assert.Equal("UNTRACKED", replay.FlexProvenance);
        Assert.Null(replay.OpeningFlexBalance);
        Assert.Null(replay.ClosingFlexBalance);
        // The replay really called the engine, and with the frozen UNTRACKED opening 0 (the forward call's twin).
        var calls = _fx.Engine.TimelineCalls.Where(c => c.EmployeeId == employeeId).ToList();
        Assert.Equal(2, calls.Count);
        Assert.Equal(calls[0].PreviousFlexBalance, calls[1].PreviousFlexBalance);
        Assert.Equal(0m, calls[1].PreviousFlexBalance);
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class InMemoryClassifications : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<StatsTid.SharedKernel.Segmentation.RuleClassification> _set;
        public InMemoryClassifications(IReadOnlyList<StatsTid.SharedKernel.Segmentation.RuleClassification> set) => _set = set;
        public IReadOnlyList<StatsTid.SharedKernel.Segmentation.RuleClassification> GetClassifications() => _set;
    }
}
