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
/// S145 / TASK-14514 (R50: a NEW file) — what payroll SENDS the week-by-week engine for an employee who starts or
/// leaves inside the month: which days the timeline covers, and the settlement cutoff. The engine is a FAKE that
/// records the request (the house idiom: <c>EmploymentWindowLiveRulesetTests</c>), so these facts pin the
/// request PCS builds, not any arithmetic.
///
/// <para>
/// <b>Plain language.</b> A starter hired on Thursday 3 September has no norm for 31 August - 2 September: the
/// timeline must cover 3 September onward only. A leaver whose last day is Tuesday 29 September has a last week
/// that September itself settles, on 29 September (R41): the request must carry that last day as its
/// <b>settlement cutoff</b>, so the engine settles the week there and October evaluates nothing for it.
/// </para>
///
/// <para>
/// <b>Fixture conditions.</b> C-E1: EE1 asserts the pieces only; EE2 asserts the pieces and the cutoff; neither
/// asserts the entries (the hours reader is an empty fake). These fixtures carry NO hire-dated agreement row (the
/// profile and agreement-code history cover from '0001-01-01', and PCS has no agreement-code repository), so
/// MD-46 cannot reach them. Per R26 the fixture is not about the prior-month check, so M-1 is answered APPROVED
/// by a fake. The pieces are asserted as the SET OF DAYS they cover (their union), so the facts hold whether PCS
/// emits one piece per run of days or one per segment plus one for the context days.
/// </para>
///
/// Docker-gated (Postgres for the employment-window resolver): CI-verified, never locally green.
/// </summary>
[Trait("Category", "Docker")]
public sealed class EmploymentEdgesTimelineTests : IAsyncLifetime
{
    private const string OrgId = "STY_S145_EDGES";

    private static readonly DateOnly Aug31 = new(2026, 8, 31);
    private static readonly DateOnly Sep01 = new(2026, 9, 1);
    private static readonly DateOnly Sep03 = new(2026, 9, 3);
    private static readonly DateOnly Sep29 = new(2026, 9, 29);
    private static readonly DateOnly Sep30 = new(2026, 9, 30);
    private static readonly DateOnly Oct01 = new(2026, 10, 1);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    private static readonly IReadOnlyList<RuleClassification> LiveSet = new RuleRegistry().GetAll();

    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync()
    {
        _harness = await TestFixtures.DockerHarness.StartAsync();
        await StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    // ═════════════════════════════════════════════════════════════════════
    // EE1 — starter
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// EE1 (refinement "Employment edges", :598). A starter hired <b>Thursday 3 September</b>: the week 31 August -
    /// 6 September counts norm only for 3-4 September (14.80 h). The timeline PCS sends for September covers
    /// exactly the EMPLOYED days 3-30 September: no piece covers 31 August, 1 or 2 September, and every piece
    /// carries the employee's HK agreement at fraction 1.0. Pieces only (C-E1).
    ///
    /// Red conditions: <b>its mutation evidence is MU-40 at Unit level</b> (R54, fact 3), not a Docker run: any
    /// PCS-level form of "the piece builder includes NOT_EMPLOYED days" reddens 14511-T's Unit piece assertions
    /// (<c>EmploymentWindowSegmentSkipTests</c>), which would stop an evidence run at its Unit step. Under that
    /// mutation this fact's covered-day set would start on 31 August, so the "starts on 3 September" assertion
    /// would trip here too. EE1 carries no MD id and is on no evidence list ("covered by MU-40").
    /// </summary>
    [Fact]
    public async Task EmploymentEdges_Starter3Sep_TimelineCoversEmployedDaysOnly()
    {
        const string emp = "EMP-S145-EE1";
        await SeedEmployeeAsync(emp);
        await SetEmploymentWindowAsync(emp, start: Sep03, end: null);

        var engine = new RecordingTimelineEngine();
        var outcome = await RunAsync(engine, emp, Sep01, Sep30);

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        var call = Assert.Single(engine.TimelineCalls);
        var covered = call.CoveredDays();
        var expected = DaysBetween(Sep03, Sep30);

        Assert.Multiple(
            () => Assert.Equal(expected, covered),
            () => Assert.Equal(Sep03, covered.First()),
            () => Assert.DoesNotContain(Aug31, covered),
            () => Assert.All(call.Pieces, p => Assert.Equal(1.0m, p.PartTimeFraction)),
            () => Assert.All(call.Pieces, p => Assert.Equal("HK", p.AgreementCode)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // EE2 — leaver (R41, MD-52)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// EE2 (refinement "Employment edges", :599; ruling R41). A leaver whose last day is <b>Tuesday 29
    /// September</b>.
    /// <list type="bullet">
    /// <item><b>September settles the week.</b> The September timeline covers exactly the EMPLOYED days 31
    /// August - 29 September (31 August is the opening-week context day; nothing for 30 September) and its
    /// <c>SettlementCutoff</c> is <b>2026-09-29</b>, the window's inclusive last day, which is not the period's
    /// last day (30 September): the cutoff is what lets the engine settle the leaver's last week on 29
    /// September.</item>
    /// <item><b>October evaluates nothing.</b> Every October timeline call (PCS may make none, because the plan
    /// has no EMPLOYED October segment) carries the same cutoff, 29 September, which precedes October, and covers
    /// no October day, so no week can be settled by October for this employee, and the calculation succeeds.</item>
    /// </list>
    /// Pieces and cutoff only; the entries are not asserted (C-E1).
    ///
    /// Red conditions (Docker-gated, CI-verified): mutation MD-52 (the cutoff derivation is always null, even for a
    /// leaver): the September request carries no cutoff and the "2026-09-29" assertion trips. MD-43 does not reach
    /// this fact (for a leaver the last piece day is the window end).
    /// </summary>
    [Fact]
    public async Task EmploymentEdges_LeaverTue29Sep_SeptemberSettlesWeek_OctoberEvaluatesNothing()
    {
        const string emp = "EMP-S145-EE2";
        await SeedEmployeeAsync(emp);
        await SetEmploymentWindowAsync(emp, start: null, end: Sep29);

        var septemberEngine = new RecordingTimelineEngine();
        var september = await RunAsync(septemberEngine, emp, Sep01, Sep30);
        var octoberEngine = new RecordingTimelineEngine();
        var october = await RunAsync(octoberEngine, emp, Oct01, Oct31);

        Assert.True(september.Result.Success, september.Result.ErrorMessage);
        Assert.True(october.Result.Success, october.Result.ErrorMessage);
        var septemberCall = Assert.Single(septemberEngine.TimelineCalls);

        Assert.Multiple(
            () => Assert.Equal(DaysBetween(Aug31, Sep29), septemberCall.CoveredDays()),
            () => Assert.Equal(Sep29, septemberCall.SettlementCutoff),
            () => Assert.All(octoberEngine.TimelineCalls, c => Assert.Equal(Sep29, c.SettlementCutoff)),
            () => Assert.All(octoberEngine.TimelineCalls, c => Assert.DoesNotContain(c.CoveredDays(), d => d >= Oct01)));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Harness
    // ═════════════════════════════════════════════════════════════════════

    private static List<DateOnly> DaysBetween(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
            days.Add(d);
        return days;
    }

    private async Task<PeriodCalculationService.PeriodCalculationOutcome> RunAsync(
        RecordingTimelineEngine engine, string employeeId, DateOnly periodStart, DateOnly periodEnd)
    {
        var pcs = BuildPcs(engine);
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        return await pcs.CalculateWithOutcomeAsync(
            Profile(employeeId), periodStart, periodEnd, previousFlexBalance: 0m);
#pragma warning restore CS0618
    }

    /// <summary>The production DI shape, hand-built (as <c>EmploymentWindowLiveRulesetTests</c>), plus the S145
    /// readers: an EMPTY hours reader (the entries are not asserted here, C-E1) and the R26 fake.</summary>
    private PeriodCalculationService BuildPcs(RecordingTimelineEngine engine)
    {
        var wtmRepo = new WageTypeMappingRepository(_harness.Factory);
        var mappingService = new PayrollMappingService(
            _harness.Factory, NullLogger<PayrollMappingService>.Instance, wtmRepo);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceUrls:RuleEngine"] = "http://rule-engine.test" })
            .Build();

        return new PeriodCalculationService(
            new SingleClientFactory(engine),
            mappingService,
            _harness.EventStore,
            _harness.Factory,
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new LiveClassifications(),
            localAgreementProfileRepo: null,
            profileResolver: new EmploymentProfileResolver(_harness.Factory, new UserAgreementCodeRepository(_harness.Factory)),
            employmentWindowResolver: new EmploymentWindowResolver(_harness.Factory),
            employeeProfileRepo: new EmployeeProfileRepository(_harness.Factory),
            recordedHoursReader: new EmptyHours(),
            priorPeriodApprovalReader: new AlwaysApprovedPriorPeriods());
    }

    private static EmploymentProfile Profile(string employeeId) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = "HK",
        OkVersion = "OK26",
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = OrgId,
    };

    private async Task SeedEmployeeAsync(string employeeId)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        // The history-covering '0001-01-01' rows: no hire-dated agreement row (MD-46 cannot reach this suite).
        await RegressionSeed.SeedEmployeeAsync(conn, employeeId, OrgId, "HK", "OK26", partTimeFraction: 1.0m);
    }

    private async Task SetEmploymentWindowAsync(string employeeId, DateOnly? start, DateOnly? end)
    {
        await using var conn = _harness.Factory.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "UPDATE users SET employment_start_date = @s, employment_end_date = @e WHERE user_id = @id", conn);
        cmd.Parameters.AddWithValue("id", employeeId);
        cmd.Parameters.AddWithValue("s", (object?)start ?? DBNull.Value);
        cmd.Parameters.AddWithValue("e", (object?)end ?? DBNull.Value);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    // ═════════════════════════════════════════════════════════════════════
    // Fakes (private copies: the house idiom for fixtures)
    // ═════════════════════════════════════════════════════════════════════

    // S145 R26: this suite is not about the prior-month check; M−1 is treated as APPROVED
    private sealed class AlwaysApprovedPriorPeriods : IPriorPeriodApprovalReader
    {
        public Task<string?> GetApprovalStatusAsync(
            string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct) =>
            Task.FromResult<string?>("APPROVED");
    }

    /// <summary>No recorded hours at all: the entries are not what these facts pin (C-E1).</summary>
    private sealed class EmptyHours : IRecordedHoursReader
    {
        private static readonly RecordedHours None =
            new(Array.Empty<TimeEntryProjectionRow>(), Array.Empty<AbsenceProjectionRow>());

        public Task<RecordedHours> ReadCurrentAsync(
            string employeeId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct) =>
            Task.FromResult(None);

        public Task<RecordedHours> ReadByEventIdsAsync(
            string employeeId, IReadOnlyList<Guid> entryEventIds, IReadOnlyList<Guid> absenceEventIds, CancellationToken ct) =>
            Task.FromResult(None);
    }

    private sealed record TimelinePiece(DateOnly From, DateOnly To, decimal PartTimeFraction, string AgreementCode);

    private sealed record TimelineCall(IReadOnlyList<TimelinePiece> Pieces, DateOnly? SettlementCutoff)
    {
        /// <summary>The union of the days the pieces cover, in order.</summary>
        public IReadOnlyList<DateOnly> CoveredDays() =>
            Pieces.SelectMany(p => DaysBetween(p.From, p.To)).Distinct().OrderBy(d => d).ToList();
    }

    /// <summary>
    /// A fake engine that answers every endpoint with an empty success and records each
    /// <c>/api/rules/evaluate-timeline</c> request (pieces and cutoff). The timeline answer has the response's
    /// full shape (three result rows, the flex block, a stub fingerprint and the marker).
    /// </summary>
    private sealed class RecordingTimelineEngine : HttpMessageHandler
    {
        private readonly List<TimelineCall> _timeline = new();

        public IReadOnlyList<TimelineCall> TimelineCalls
        {
            get { lock (_timeline) return _timeline.ToList(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var employeeId = root.TryGetProperty("employeeId", out var eid) ? eid.GetString() ?? "EMP"
                : root.TryGetProperty("profile", out var prof) && prof.TryGetProperty("employeeId", out var pid) ? pid.GetString() ?? "EMP"
                : "EMP";

            if (path.EndsWith("/api/rules/evaluate-timeline", StringComparison.Ordinal))
            {
                var pieces = root.GetProperty("pieces").EnumerateArray()
                    .Select(p => new TimelinePiece(
                        DateOnly.Parse(p.GetProperty("from").GetString()!, CultureInfo.InvariantCulture),
                        DateOnly.Parse(p.GetProperty("to").GetString()!, CultureInfo.InvariantCulture),
                        p.GetProperty("partTimeFraction").GetDecimal(),
                        p.GetProperty("agreementCode").GetString()!))
                    .ToList();
                DateOnly? cutoff = root.TryGetProperty("settlementCutoff", out var c) && c.ValueKind == JsonValueKind.String
                    ? DateOnly.Parse(c.GetString()!, CultureInfo.InvariantCulture)
                    : null;
                lock (_timeline) _timeline.Add(new TimelineCall(pieces, cutoff));

                return Json(new
                {
                    results = new object[]
                    {
                        Empty("OVERTIME_CALC", employeeId),
                        Empty("NORM_CHECK_37H", employeeId),
                        Empty("FLEX_BALANCE", employeeId),
                    },
                    flex = new
                    {
                        ruleId = "FLEX_BALANCE", employeeId, success = true, lineItems = Array.Empty<object>(),
                        previousBalance = 0m, newBalance = 0m, delta = 0m, workedHours = 0m,
                        absenceNormCredits = 0m, effectiveNorm = 0m, excessForPayout = 0m,
                    },
                    configFingerprints = new[] { "stub" },
                    evaluationModel = WeeklyTimelineCore.EvaluationModel,
                });
            }

            var ruleId = path.EndsWith("/evaluate-flex", StringComparison.Ordinal) ? "FLEX_BALANCE"
                : path.EndsWith("/evaluate-absence", StringComparison.Ordinal) ? "ABSENCE"
                : root.TryGetProperty("ruleId", out var rid) ? rid.GetString() ?? "UNKNOWN" : "UNKNOWN";
            return Json(Empty(ruleId, employeeId));
        }

        private static object Empty(string ruleId, string employeeId) =>
            new { ruleId, employeeId, success = true, lineItems = Array.Empty<object>() };

        private static HttpResponseMessage Json(object payload) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                Encoding.UTF8, "application/json"),
        };
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class LiveClassifications : IRuleClassificationProvider
    {
        public IReadOnlyList<RuleClassification> GetClassifications() => LiveSet;
    }
}
