using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Auth;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Regression.Security;
using StatsTid.Tests.Regression.TestSupport;
using StatsTid.Tests.Shared;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14828-T (AC-C1e, payroll side; ADR-043 D12, ADR-016 D10; rulings 15 and 19) - payroll's real
/// serializer meets the real Rule Engine, and a per-segment request that reaches the engine without its
/// configuration is refused by the engine's fixed sentence, the month is refused and nothing is written.
///
/// <para>
/// <b>In plain language.</b> The Rule Engine no longer carries agreement rules of its own: it computes only with
/// the configuration a request hands it, and answers a request without one with a fixed sentence. Payroll is the
/// biggest caller. These two facts run payroll's calculation against the REAL engine host (no database, no
/// Docker): the first lets payroll send everything it builds, and the month is calculated, which proves payroll's
/// serialization really binds in the real engine (this is the only check of that before CI); the second removes
/// the configuration from payroll's per-segment <c>evaluate</c> requests on their way to the engine, and the
/// engine's own refusal comes back, payroll refuses the month, and its manifest store records no write.
/// </para>
///
/// <para>
/// <b>Harness.</b> <see cref="PeriodCalculationService"/> built by hand (a recording manifest store, a fake
/// lookup answering the compiled configuration of the key it is asked, an empty hours reader, the live
/// classification set), its HTTP client routed to <see cref="RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory"/>
/// through a delegating handler that records each exchange and, in the refusal arm only, drops the
/// <c>config</c> member of the per-segment <c>evaluate</c> bodies. June 2026 opens on a Monday, so there are no
/// context days and no previous month to approve. The employee has no hours: the month then yields no line items,
/// which keeps the database-backed mapping service out of the path; the norm figure is what the engine computes
/// from the configuration alone.
/// </para>
///
/// <para>
/// <b>Kind: N (Regression, non-Docker; mutation M-50).</b> It names members the sprint creates (the lookup
/// parameter of the service, the engine's <c>config</c> member), so it cannot compile on master and is not an F
/// fact. <b>Red conditions</b> (M-50: the <c>evaluate</c> route answers a request without a configuration from
/// the compiled table instead of refusing it): in the refusal arm the engine answers 200 to the stripped request,
/// so the S1 assertion on the recorded engine reply fails (expected 400 and the S1 sentence), the month is
/// calculated and the manifest store records one write. The control arm stays green under M-50 (it never strips).
/// </para>
/// </summary>
public sealed class PayrollSegmentRequestWithoutConfigTests : IDisposable
{
    /// <summary>S1, taken from TASK-14826's brief (item 3), never from the implementation.</summary>
    private const string SentenceS1 = "An agreement configuration is required.";

    private const string EmployeeId = "EMP-S148-NOCONFIG";
    private static readonly DateOnly Jun01 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);

    private readonly RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory _engine = new();

    public void Dispose() => _engine.Dispose();

    /// <summary>
    /// The control arm: payroll sends the configuration as it builds it. The month is calculated (the outcome
    /// succeeds, the manifest store records one write), every engine exchange answered 200, each timeline piece
    /// and each per-segment <c>evaluate</c> body carried a <c>config</c>, and the norm figure is 162.80 h (22
    /// weekdays at HK's 37 h), which only a configuration the engine bound can produce.
    /// </summary>
    [Fact]
    public async Task Control_ConfigSent_TheMonthIsCalculated()
    {
        var recorder = new RecordingHandler(_engine.Server.CreateHandler(), stripSegmentConfig: false);
        var store = new RecordingManifestStore();
        var pcs = BuildPcs(recorder, store);

#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var outcome = await pcs.CalculateWithOutcomeAsync(Profile(), Jun01, Jun30, MintBearer());
#pragma warning restore CS0618

        Assert.True(outcome.Result.Success, outcome.Result.ErrorMessage);
        Assert.Equal(1, store.PersistCalls);
        Assert.All(recorder.Exchanges, e => Assert.Equal(HttpStatusCode.OK, e.Status));

        var timeline = Assert.Single(recorder.Exchanges, e => e.Path == "/api/rules/evaluate-timeline");
        var pieces = JsonNode.Parse(timeline.RequestBody)!["pieces"]!.AsArray();
        Assert.NotEmpty(pieces);
        Assert.All(pieces, p => Assert.NotNull(p!["config"]));

        var segmentCalls = recorder.Exchanges.Where(e => e.Path == "/api/rules/evaluate").ToList();
        Assert.NotEmpty(segmentCalls);
        Assert.All(segmentCalls, e => Assert.NotNull(JsonNode.Parse(e.RequestBody)!["config"]));

        var norm = Assert.Single(outcome.Result.RuleResults, r => r.RuleId == "NORM_CHECK_37H");
        Assert.Equal(162.80m, norm.NormHoursTotal);
    }

    /// <summary>
    /// The refusal arm: the <c>config</c> member is removed from each per-segment <c>evaluate</c> body on its way
    /// to the engine. The engine answers 400 with exactly <c>{ success: false, error: S1 }</c> (S1 from
    /// TASK-14826's brief), the month is refused (R-6: the outcome does not succeed), and the manifest store
    /// records no write.
    /// </summary>
    [Fact]
    public async Task RefusedByTheFixedSentence_NothingWritten()
    {
        var recorder = new RecordingHandler(_engine.Server.CreateHandler(), stripSegmentConfig: true);
        var store = new RecordingManifestStore();
        var pcs = BuildPcs(recorder, store);

#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var outcome = await pcs.CalculateWithOutcomeAsync(Profile(), Jun01, Jun30, MintBearer());
#pragma warning restore CS0618

        var refused = recorder.Exchanges.Where(e => e.Path == "/api/rules/evaluate").ToList();
        Assert.NotEmpty(refused);
        Assert.All(refused, e =>
        {
            Assert.Equal(HttpStatusCode.BadRequest, e.Status);
            var body = JsonNode.Parse(e.ResponseBody)!.AsObject();
            Assert.Equal(new[] { "error", "success" }, body.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal));
            Assert.False(body["success"]!.GetValue<bool>());
            Assert.Equal(SentenceS1, body["error"]!.GetValue<string>());
        });

        Assert.False(outcome.Result.Success);
        Assert.Equal(0, store.PersistCalls);
    }

    private PeriodCalculationService BuildPcs(HttpMessageHandler handler, RecordingManifestStore store) => new(
        new SingleClientFactory(handler),
        // Never dereferenced: with no hours the engine returns no line items, so no mapping lookup happens.
        mappingService: null!,
        store,
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ServiceUrls:RuleEngine"] = "http://rule-engine.test" })
            .Build(),
        NullLogger<PeriodCalculationService>.Instance,
        classificationProvider: new LiveClassifications(),
        hoursReader: new InMemoryRecordedHoursReader(),
        // June 2026 opens on a Monday: there is no previous month to approve; the fake answers APPROVED regardless.
        priorPeriodApprovalReader: new AlwaysApproved(),
        // Not about transfers: no transfer dates (the service refuses a plan without an organisation reader).
        organisationHistoryReader: new NoTransferDates(),
        flexLedgerReader: new InMemoryFlexLedgerReader(),
        // Not about configurations: the fake answers the compiled configuration of the key it is asked (the helper's).
        configLookup: new EngineConfigRequests.CompiledLookup());

    private static EmploymentProfile Profile() => new()
    {
        EmployeeId = EmployeeId,
        AgreementCode = "HK",
        OkVersion = "OK26",
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
        OrgId = "STY01",
    };

    private static string MintBearer()
    {
        var tokens = new JwtTokenService(new JwtSettings
        {
            Issuer = "statstid",
            Audience = "statstid",
            SigningKey = RuleClassificationsLeastPrivilegeAcceptTests.RuleEngineHostFactory.DevSigningKey,
            ExpirationMinutes = 60,
        });
        return "Bearer " + tokens.GenerateToken(
            employeeId: "ROOT-S148", name: "ROOT-S148", role: StatsTidRoles.GlobalAdmin,
            agreementCode: "AC", orgId: null,
            scopes: new[] { new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL") });
    }

    // ── fakes ─────────────────────────────────────────────────────────────────────────────────

    private sealed record Exchange(string Path, HttpStatusCode Status, string RequestBody, string ResponseBody);

    /// <summary>Records every engine exchange and, when asked, removes <c>config</c> from the per-segment <c>evaluate</c> bodies.</summary>
    private sealed class RecordingHandler : DelegatingHandler
    {
        private readonly bool _strip;
        public List<Exchange> Exchanges { get; } = new();

        public RecordingHandler(HttpMessageHandler engine, bool stripSegmentConfig) : base(engine) => _strip = stripSegmentConfig;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            if (_strip && path == "/api/rules/evaluate")
            {
                var node = JsonNode.Parse(body)!.AsObject();
                node.Remove("config");
                body = node.ToJsonString();
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            var response = await base.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            Exchanges.Add(new Exchange(path, response.StatusCode, body, responseBody));
            return response;
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class LiveClassifications : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set = new RuleRegistry().GetAll();
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }

    private sealed class AlwaysApproved : IPriorPeriodApprovalReader
    {
        public Task<string?> GetApprovalStatusAsync(
            string employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct) =>
            Task.FromResult<string?>("APPROVED");
    }

    private sealed class NoTransferDates : IOrganisationHistoryReader
    {
        public Task<IReadOnlyList<DateOnly>> GetEffectiveFromDatesAsync(
            string employeeId, DateOnly afterExclusive, DateOnly toInclusive, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DateOnly>>(Array.Empty<DateOnly>());
    }

    /// <summary>Counts persists; keeps nothing (the facts read only the count).</summary>
    private sealed class RecordingManifestStore : ISegmentManifestStore
    {
        public int PersistCalls { get; private set; }

        public Task PersistAsync(
            SegmentManifestCreated manifestEvent, SegmentManifest manifest, PeriodCalculationCompleted legacyEvent,
            CancellationToken ct = default)
        {
            PersistCalls++;
            return Task.CompletedTask;
        }

        public Task<SegmentManifest?> TryLoadAsync(Guid manifestId, CancellationToken ct = default) =>
            Task.FromResult<SegmentManifest?>(null);
    }
}
