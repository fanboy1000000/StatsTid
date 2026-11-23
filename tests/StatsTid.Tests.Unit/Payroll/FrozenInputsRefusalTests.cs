using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Shared;
using static StatsTid.Tests.Unit.Payroll.EmploymentWindowPcsFixture;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S148 / TASK-14830 FU-T (Step 5a cycle 1, rulings V1, V3, V6; ADR-043 D12, ADR-016 D10, S148 ruling R-5) — a month's
/// stored proof holds the agreement rules the month was paid on, and a replay (a) refuses a damaged proof by name
/// before it asks the Rule Engine anything and (b) never looks the rules up again.
///
/// <para>
/// <b>In plain language.</b> June 2026 is calculated, which stores its proof. A replay re-runs the month from that
/// proof alone. Three things must hold. A proof whose stored rules do not fit the month's stretches (none at all, or a
/// set that says "AC" under a "HK" label) is refused with <c>replay-manifest-without-frozen-inputs</c> at once: the
/// Rule Engine is not called, not even for its rule list, so an engine outage can never be mistaken for a damaged
/// proof. A proof that names no organisation is refused the same way. And a replay needs no access to the
/// configuration database at all: it is given a service with no lookup, and it still succeeds and returns the same
/// answer as the original, because it sends the rules the month was paid with.
/// </para>
///
/// <para>
/// <b>Harness.</b> DB-free (no Docker): the real <see cref="PeriodCalculationService"/> over the fixture's in-memory
/// manifest store, hours reader and recording rule-engine stub (<see cref="EmploymentWindowPcsFixture"/>). June 2026
/// opens on a Monday and has one piece, so no previous month is needed. A damaged manifest is served from a small
/// fixed store (the in-memory store cannot hold a hand-made manifest). The replay service is built directly because the
/// fixture's classification provider cannot count: a counting provider records how often payroll asked for the rule
/// list, the only way payroll reaches <c>/api/rules/classifications</c>.
/// </para>
/// </summary>
public sealed class FrozenInputsRefusalTests
{
    private const string Emp = "EMP-S148-FROZENU";

    // 2026: 1 June is a Monday, 30 June a Tuesday.
    private static readonly DateOnly Jun1 = new(2026, 6, 1);
    private static readonly DateOnly Jun30 = new(2026, 6, 30);

    // ─────────────────────────────────────────────────────────────────────────
    // V1 — a damaged proof is refused by name before any engine call
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Kind F</b> (Unit; ruling V1, Codex W1), shape (a): one piece and an empty <c>ConfigRuns</c> list. Refused
    /// <c>replay-manifest-without-frozen-inputs</c>, with <b>zero</b> classification calls and zero engine requests.
    /// The classification provider throws <see cref="RuleClassificationsUnavailableException"/> when asked (the
    /// shape of an engine outage) and counts the asking.
    ///
    /// <para>
    /// <b>Red conditions.</b> At <c>52ac6683</c> (before the check moved to the first step of a replay) the count was
    /// checked after payroll had asked for the rule list: the replay throws
    /// <see cref="RuleClassificationsUnavailableException"/> instead of the refusal, so <c>ThrowsAsync</c> for the
    /// refusal fails, and the count is 1. A check that sits after the classification fetch, or after the hours read,
    /// fails the same way.
    /// </para>
    /// </summary>
    [Fact]
    public async Task EmptyConfigRuns_RefusedByName_NoClassificationCall_NoEngineRequest()
    {
        var run = await ReplayDamagedAsync(pi => pi with { ConfigRuns = Array.Empty<FrozenConfigRun>() },
            classificationsThrow: true);

        AssertRefusedByNameBeforeTheEngine(run);
    }

    /// <summary>
    /// <b>Kind F</b> (Unit; ruling V1, Codex W1), shape (b): a run keyed HK/OK26 whose nested configuration says AC.
    /// Refused by the same name with zero classification calls and zero engine requests.
    ///
    /// <para>
    /// <b>Red conditions.</b> At <c>52ac6683</c> the one-for-one check did not compare the nested configuration's
    /// agreement code and OK version with the run's own key, so the replay is not refused: it succeeds and makes four
    /// engine requests (the live engine would refuse the AC configuration under HK, but the stub does not), so
    /// the refusal assertion fails ("no exception was thrown"). Dropping the agreement-code comparison alone turns
    /// this fact red; the OK-version comparison is pinned by the sibling arm below.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ConfigNamingAnotherAgreementThanItsKey_RefusedByName_NoClassificationCall_NoEngineRequest()
    {
        var run = await ReplayDamagedAsync(pi => pi with
        {
            ConfigRuns = pi.ConfigRuns!.Select(r => r with { Config = EngineConfigRequests.Config("AC", r.OkVersion, r.Position) }).ToList(),
        });

        AssertRefusedByNameBeforeTheEngine(run);
    }

    /// <summary>
    /// <b>Kind F</b> (Unit; ruling V1), the OK-version arm of shape (b): a run keyed HK/OK26 whose nested configuration
    /// says HK/OK24. Refused by the same name before any engine call.
    ///
    /// <para>
    /// <b>Red conditions.</b> At <c>52ac6683</c> the nested OK version was not compared with the run's key: the replay
    /// succeeds with engine requests, so <c>ThrowsAsync</c> fails. Dropping the
    /// <c>run.Config.OkVersion == run.OkVersion</c> comparison turns this fact red and not the agreement-code one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ConfigNamingAnotherOkVersionThanItsKey_RefusedByName_NoClassificationCall_NoEngineRequest()
    {
        var run = await ReplayDamagedAsync(pi => pi with
        {
            ConfigRuns = pi.ConfigRuns!.Select(r => r with { Config = EngineConfigRequests.Config(r.AgreementCode, "OK24", r.Position) }).ToList(),
        });

        AssertRefusedByNameBeforeTheEngine(run);
    }

    /// <summary>
    /// <b>Kind F</b> (Unit; ruling V1, the fifth check FU-1 moved to the first step), shape (c): piece coverage. Both
    /// the piece and its configuration entry start one day late, so each fits the other and the employed segment's
    /// first day (1 June) is covered by no piece. Refused by the same name before any engine call.
    ///
    /// <para>
    /// <b>Red conditions.</b> At <c>52ac6683</c> the coverage check ran after payroll had fetched the rule list: the
    /// replay throws <see cref="RuleClassificationsUnavailableException"/> instead of the refusal (the provider
    /// throws), and the count is 1.
    /// </para>
    /// </summary>
    [Fact]
    public async Task PieceNotCoveringTheFirstEmployedDay_RefusedByName_NoClassificationCall_NoEngineRequest()
    {
        var run = await ReplayDamagedAsync(
            pi => pi with
            {
                Pieces = pi.Pieces.Select(p => p with { From = p.From.AddDays(1) }).ToList(),
                ConfigRuns = pi.ConfigRuns!.Select(r => r with { From = r.From.AddDays(1) }).ToList(),
            },
            classificationsThrow: true);

        AssertRefusedByNameBeforeTheEngine(run);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // V3 — the organisation rule of IsComplete
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Kind F</b> (Unit, because a stub engine counts requests and the check is pure and needs no database;
    /// ruling V3, Fable W1). A forward manifest with <c>orgId</c> removed from its piece <b>and</b> from that piece's
    /// configuration entry, <c>ConfigRuns</c> kept, is refused <c>replay-manifest-without-frozen-inputs</c> with no
    /// engine request. Everything else fits (one entry for one piece, equal keys), so only the organisation rule can
    /// refuse it: a piece without an organisation cannot select the institution's local profile, and a replay must not
    /// look one up.
    ///
    /// <para>
    /// <b>Red conditions.</b> Dropping <c>!string.IsNullOrEmpty(piece.OrgId)</c> from <c>IsComplete</c> (mutation
    /// <b>M-ORG</b>): the one-for-one check passes (both ids are null and equal), the replay succeeds and sends the
    /// engine four requests, so <c>ThrowsAsync</c> fails with "no exception was thrown". This is the only fact that
    /// reaches the organisation rule alone: the pre-S148 facts of <c>FrozenConfigReplayTests</c> also lack the
    /// configurations, so the configuration rule refuses them whether or not the organisation rule exists.
    /// </para>
    /// </summary>
    [Fact]
    public async Task OrganisationRule_PieceAndEntryWithoutOrgId_RefusedByName()
    {
        var run = await ReplayDamagedAsync(pi => pi with
        {
            Pieces = pi.Pieces.Select(p => p with { OrgId = null }).ToList(),
            ConfigRuns = pi.ConfigRuns!.Select(r => r with { OrgId = null }).ToList(),
        });

        AssertRefusedByNameBeforeTheEngine(run);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // V6 — a replay never consults the configuration lookup
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Kind F</b> (Unit; ruling V6, Fable W4; named in <b>M-13</b>'s frozen list for CI-M5 beside
    /// <c>FrozenConfigReplayTests.PublishChangedConfig_ReplayReproducesOriginal</c>). June is calculated forward on a
    /// service with a configuration lookup (the fixture's, answering the compiled HK/OK26 rules). It is then replayed
    /// on a second service built with <b>no</b> lookup at all. The replay succeeds, its result equals the forward
    /// result as JSON text, and each piece's raw <c>config</c> object in its timeline request equals the forward
    /// request's, byte for byte.
    ///
    /// <para>
    /// <b>Red conditions.</b> At <c>f328d00d</c> the replay re-resolved each piece's configuration through the lookup,
    /// and a service without one refused it (<c>RequireConfigLookup</c>), so the replay throws
    /// <c>InvalidOperationException</c> before the success assertion. <b>M-13</b> (in <c>FrozenEvaluationInputs</c>,
    /// resolve each piece's configuration through the lookup instead of using the stored one) fails it the same way. Non-vacuity: the forward request carried one piece with the HK
    /// 37-hour week, and the replay's engine received the timeline request.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ReplayWithNoConfigLookup_Succeeds_ByteIdenticalToForward()
    {
        var hours = WeekdayHours(Emp, Jun1, Jun30);
        var store = new InMemorySegmentManifestStore();
        var forwardEngine = new RecordingRuleEngine();
        var forwardBodies = new BodyCapture(forwardEngine);
        var forwardPcs = BuildPcsOver(forwardBodies, store, hours, new FakeConfigLookup(), new CountingClassifications(throws: false));

#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var forward = await forwardPcs.CalculateWithOutcomeAsync(Profile(Emp), Jun1, Jun30);
#pragma warning restore CS0618
        Assert.True(forward.Result.Success, forward.Result.ErrorMessage);
        Assert.Equal(new[] { 37m }, PieceWeeklyNorms(forwardBodies)); // non-vacuity: the original was sent the HK 37-hour week

        var replayEngine = new RecordingRuleEngine();
        var replayBodies = new BodyCapture(replayEngine);
        var replayPcs = BuildPcsOver(replayBodies, store, hours, configLookup: null, new CountingClassifications(throws: false));

        var replay = await replayPcs.ReplayAsync(forward.ManifestId);

        Assert.True(replay.Success, replay.ErrorMessage);
        Assert.Contains(replayEngine.Calls, c => c.Path.EndsWith(RecordingRuleEngine.TimelinePath, StringComparison.Ordinal)); // non-vacuity
        Assert.Equal(JsonSerializer.Serialize(forward.Result), JsonSerializer.Serialize(replay));
        Assert.Equal(PieceConfigs(forwardBodies), PieceConfigs(replayBodies));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // helpers
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record ReplayRun(Exception? Thrown, bool Succeeded, int ClassificationCalls, int EngineRequests);

    /// <summary>
    /// Forward-calculates June 2026 (one piece), damages the stored <see cref="PlanInputs"/> with
    /// <paramref name="damage"/>, serves it from a fixed store and replays it on a service whose classification
    /// provider counts (and, with <paramref name="classificationsThrow"/>, throws as an engine outage would).
    /// </summary>
    private static async Task<ReplayRun> ReplayDamagedAsync(
        Func<PlanInputs, PlanInputs> damage, bool classificationsThrow = false)
    {
        var hours = WeekdayHours(Emp, Jun1, Jun30);
        var forwardStore = new InMemorySegmentManifestStore();
        var forwardPcs = BuildPcs(new RecordingRuleEngine(), forwardStore, hoursReader: hours);
#pragma warning disable CS0618 // The planless shim IS the surviving production path (/calculate-and-export).
        var forward = await forwardPcs.CalculateWithOutcomeAsync(Profile(Emp), Jun1, Jun30);
#pragma warning restore CS0618
        Assert.True(forward.Result.Success, forward.Result.ErrorMessage);

        var manifest = forwardStore.Manifests[forward.ManifestId];
        var frozen = manifest.PlanInputs!;
        Assert.Single(frozen.Pieces);               // non-vacuity: one piece, one entry
        Assert.Single(frozen.ConfigRuns!);
        Assert.False(string.IsNullOrEmpty(frozen.Pieces[0].OrgId)); // non-vacuity: the forward piece names its organisation
        var damaged = manifest with { PlanInputs = damage(frozen) };

        var engine = new RecordingRuleEngine();
        var classifications = new CountingClassifications(classificationsThrow);
        var replayPcs = BuildPcsOver(engine, new FixedManifestStore(damaged), hours, new FakeConfigLookup(), classifications);

        try
        {
            var replay = await replayPcs.ReplayAsync(forward.ManifestId);
            return new ReplayRun(null, replay.Success, classifications.Calls, engine.Calls.Count);
        }
        catch (Exception ex)
        {
            return new ReplayRun(ex, false, classifications.Calls, engine.Calls.Count);
        }
    }

    private static void AssertRefusedByNameBeforeTheEngine(ReplayRun run)
    {
        var refused = Assert.IsType<ReplayRefusedException>(run.Thrown);
        Assert.Equal(ReplayRefusedException.ManifestWithoutFrozenInputs, refused.Reason);
        Assert.Equal("replay-manifest-without-frozen-inputs", refused.Reason);
        Assert.Equal(0, run.ClassificationCalls);
        Assert.Equal(0, run.EngineRequests);
    }

    /// <summary>A replay service built directly: the fixture's classification provider cannot count.</summary>
    private static PeriodCalculationService BuildPcsOver(
        HttpMessageHandler engine, ISegmentManifestStore store, IRecordedHoursReader hours,
        IAgreementConfigLookup? configLookup, IRuleClassificationProvider classifications) =>
        new(
            new SingleClientHttpFactory(engine),
            mappingService: null!, // never dereferenced: the stub answers no line items
            store,
            Configuration(),
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: classifications,
            hoursReader: hours,
            priorPeriodApprovalReader: new AlwaysApprovedPriorPeriods(),
            flexLedgerReader: new InMemoryFlexLedgerReader(),
            organisationHistoryReader: new NoTransferDates(),
            configLookup: configLookup);

    private static IReadOnlyList<decimal> PieceWeeklyNorms(BodyCapture engine) =>
        TimelinePieces(engine).Select(p => p.GetProperty("config").GetProperty("weeklyNormHours").GetDecimal()).ToList();

    private static IReadOnlyList<string> PieceConfigs(BodyCapture engine) =>
        TimelinePieces(engine).Select(p => p.GetProperty("config").GetRawText()).ToList();

    private static IReadOnlyList<JsonElement> TimelinePieces(BodyCapture engine)
    {
        var body = engine.Bodies.Last(b => b.Path.EndsWith(RecordingRuleEngine.TimelinePath, StringComparison.Ordinal)).Body;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("pieces").EnumerateArray().Select(p => p.Clone()).ToList();
    }

    /// <summary>Counts how often payroll asks for the rule list; with <c>throws</c> it answers like an engine outage.</summary>
    private sealed class CountingClassifications : IRuleClassificationProvider
    {
        private readonly bool _throws;
        public CountingClassifications(bool throws) => _throws = throws;
        public int Calls { get; private set; }

        public IReadOnlyList<RuleClassification> GetClassifications()
        {
            Calls++;
            return _throws ? throw new RuleClassificationsUnavailableException() : RuleSet;
        }
    }

    /// <summary>Serves one hand-made manifest; a replay never writes, so a write is a failure.</summary>
    private sealed class FixedManifestStore : ISegmentManifestStore
    {
        private readonly SegmentManifest _manifest;
        public FixedManifestStore(SegmentManifest manifest) => _manifest = manifest;

        public Task PersistAsync(
            StatsTid.SharedKernel.Events.SegmentManifestCreated manifestEvent, SegmentManifest manifest,
            StatsTid.SharedKernel.Events.PeriodCalculationCompleted legacyEvent, CancellationToken ct = default)
            => throw new InvalidOperationException("a replay never persists");

        public Task<SegmentManifest?> TryLoadAsync(Guid manifestId, CancellationToken ct = default)
            => Task.FromResult<SegmentManifest?>(manifestId == _manifest.ManifestId ? _manifest : null);
    }

    /// <summary>Records each request's path and body, then hands it to the recording stub.</summary>
    private sealed class BodyCapture : DelegatingHandler
    {
        private readonly List<(string Path, string Body)> _bodies = new();

        public BodyCapture(RecordingRuleEngine inner) : base(inner) { }

        public IReadOnlyList<(string Path, string Body)> Bodies
        {
            get { lock (_bodies) return _bodies.ToList(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(ct);
            lock (_bodies) _bodies.Add((request.RequestUri?.AbsolutePath ?? string.Empty, body));
            return await base.SendAsync(request, ct);
        }
    }
}
