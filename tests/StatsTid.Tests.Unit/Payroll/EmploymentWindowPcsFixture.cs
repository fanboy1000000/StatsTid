using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S137 / TASK-13702 — shared, DB-free harness for the ADR-040 D5 payroll pins
/// (<see cref="EmploymentWindowSegmentSkipTests"/>, <see cref="EmploymentWindowHydrationTests"/>).
///
/// <para>
/// Plain-language: these tests need a real <see cref="PeriodCalculationService"/> ("PCS" —
/// the service that turns one employee's month into rule-engine calls and payroll lines)
/// but NO database and NO Docker. So every I/O edge is replaced by a recording fake: the
/// rule engine is an <see cref="HttpMessageHandler"/> that records what PCS asked it and
/// answers with configurable success/failure; the event store is an in-memory list (so the
/// emitted segment manifest can be inspected); the employment-window and profile resolvers
/// are fakes that record which dates PCS asked about — which is exactly the ADR-040 D10
/// question ("did PCS ask for a profile on a pre-hire date?").
/// </para>
///
/// <para>
/// Two harness choices are deliberate and load-bearing for what these tests CAN pin:
/// <list type="bullet">
///   <item>The rule-engine stub returns ZERO line items, so <c>PayrollMappingService</c>
///     (sealed, DB-backed) is never dereferenced and is passed as <c>null!</c> — the same
///     idiom as <c>RuleEngineFailureLoggingTests</c>. Consequence: flex-carry pass-through
///     is pinned STRUCTURALLY here (the carry reaching the third segment equals the carry
///     the first received; the skipped segment issues no flex call) with a ZERO delta; the
///     NON-ZERO-delta variant needs seeded wage-type mappings and lives in the Docker-gated
///     <c>EmploymentWindowPayrollTests</c>.</item>
///   <item>The projection insert (<c>segment_manifests</c>) runs inside PCS's own swallowing
///     try/catch by design (a degraded audit chain is reported as <c>AuditState</c>, never
///     thrown), so a <c>null!</c> connection factory degrades the outcome to
///     <c>AuditState.EventOnly</c> — the event-store half is what these tests read.</item>
/// </list>
/// </para>
/// </summary>
internal static class EmploymentWindowPcsFixture
{
    // Segment-safe classifications for the five stubbed rules — an interior boundary must be
    // PLANNABLE here (the production AlignedWindow OVERTIME_CALC would trip ADR-016 D4 on any
    // interior boundary with PlannerOptions.Default; that contract is pinned elsewhere).
    // FLEX_BALANCE keeps its Custom merge so MergePerSegmentRuleResults' fallback path runs.
    public static readonly RuleClassification[] RuleSet =
    {
        new("SUPPLEMENT_CALC", Span.Entry,       SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategy.Concatenate, SnapshotContract: null),
        new("ON_CALL_DUTY",    Span.Entry,       SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategy.Concatenate, SnapshotContract: null),
        new("OVERTIME_CALC",   Span.Entry,       SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategy.Concatenate, SnapshotContract: null),
        new("NORM_CHECK_37H",  Span.Period,      SplitBehavior.SegmentSafe, Family.Calculation, MergeStrategy.Concatenate, SnapshotContract: null),
        new("FLEX_BALANCE",    Span.CrossPeriod, SplitBehavior.Mergeable,   Family.Calculation, MergeStrategy.Custom,      SnapshotContract: null),
    };

    /// <summary>
    /// S137 Step-5a (Reviewer WARNING 2): the same five rules, but OVERTIME_CALC carries the
    /// PRODUCTION classification shape from <c>RuleRegistry</c> — <c>Span.Window /
    /// SplitBehavior.AlignedWindow / MergeStrategy.RejectIfMultipleSegments</c>. Lets the
    /// DB-free suite pin the MERGE half of the 2026-09-02 truncation ruling: a whole-window
    /// rule evaluated in exactly ONE EMPLOYED segment reaches the merger as a single segment
    /// and passes; one that reaches it with TWO EMPLOYED segments gets the merger's failure
    /// row (the backstop the ruling assumes). See <c>EmploymentWindowAlignedWindowMergeTests</c>.
    /// </summary>
    public static readonly RuleClassification[] AlignedWindowRuleSet =
    {
        new("SUPPLEMENT_CALC", Span.Entry,       SplitBehavior.SegmentSafe,   Family.Calculation, MergeStrategy.Concatenate,              SnapshotContract: null),
        new("ON_CALL_DUTY",    Span.Entry,       SplitBehavior.SegmentSafe,   Family.Calculation, MergeStrategy.Concatenate,              SnapshotContract: null),
        new("OVERTIME_CALC",   Span.Window,      SplitBehavior.AlignedWindow, Family.Calculation, MergeStrategy.RejectIfMultipleSegments, SnapshotContract: null),
        new("NORM_CHECK_37H",  Span.Period,      SplitBehavior.SegmentSafe,   Family.Calculation, MergeStrategy.Concatenate,              SnapshotContract: null),
        new("FLEX_BALANCE",    Span.CrossPeriod, SplitBehavior.Mergeable,     Family.Calculation, MergeStrategy.Custom,                   SnapshotContract: null),
    };

    /// <summary>PCS attempts 4 time rules + absence + flex per EMPLOYED segment.</summary>
    public const int RuleCallsPerEmployedSegment = 6;

    public static readonly DateOnly Mar01 = new(2026, 3, 1);
    public static readonly DateOnly Mar31 = new(2026, 3, 31);

    public static EmploymentProfile Profile(string employeeId) => new()
    {
        EmployeeId = employeeId,
        AgreementCode = "HK",
        OkVersion = "OK24",
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
    };

    /// <summary>
    /// The WtmNaturalKey snapshot enrollment PCS's export mapper REQUIRES on every EMPLOYED
    /// segment (ADR-020 D1) — mirrors <c>BuildPlanForLegacyCallersAsync</c>. Plans built for
    /// the plan-first entry point must register it or PCS throws at export time.
    /// </summary>
    public static IPlannerEnrollment WtmEnrollment()
    {
        var enrollment = new PlannerEnrollment();
        enrollment.RegisterSnapshotContract("WtmNaturalKey", p => new WtmNaturalKey(
            OkVersion: p.OkVersion,
            AgreementCode: p.AgreementCode,
            Position: p.Position ?? ""));
        return enrollment;
    }

    /// <summary>
    /// Builds a plan the way the payroll host does — the SAME windows feed both the
    /// employment boundary dates (Start as-is; End + 1, the ADR-040 D1 fencepost) and the
    /// segment typing. <paramref name="windows"/> semantics follow the planner: null =
    /// windowless (all EMPLOYED); empty = no employed day in the period (all NOT_EMPLOYED).
    /// <paramref name="ruleSet"/> / <paramref name="options"/> default to <see cref="RuleSet"/> /
    /// <see cref="PlannerOptions.Default"/>; <paramref name="profileChangeDates"/> feeds the
    /// EmployeeProfileChange boundary (a split between two EMPLOYED segments).
    /// </summary>
    public static PlannedCalculation Plan(
        string employeeId,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<EmploymentWindow>? windows,
        IReadOnlyList<RuleClassification>? ruleSet = null,
        PlannerOptions? options = null,
        IReadOnlyList<DateOnly>? profileChangeDates = null)
    {
        List<DateOnly>? started = null;
        List<DateOnly>? ended = null;
        if (windows is not null)
        {
            started = new List<DateOnly>();
            ended = new List<DateOnly>();
            foreach (var w in windows)
            {
                if (w.Start is { } s) started.Add(s);
                if (w.End is { } e) ended.Add(e.AddDays(1));
            }
        }

        var sources = new BoundarySources(
            OkTransitions: Array.Empty<(DateOnly, string, string)>(),
            AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
            PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
            EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
            NonDatedSourceValues: new Dictionary<string, object?>(),
            LocalProfileActivations: null,
            EmploymentStartedDates: started,
            EmploymentEndedDates: ended,
            EmployeeProfileEffectiveDates: profileChangeDates);

        return PeriodPlanner.Plan(
            employeeId: employeeId,
            periodStart: periodStart,
            periodEnd: periodEnd,
            calculationKind: "forward-calc",
            ruleSet: ruleSet ?? RuleSet,
            sources: sources,
            options: options ?? PlannerOptions.Default,
            enrollment: WtmEnrollment(),
            profile: Profile(employeeId),
            employmentWindows: windows);
    }

    /// <summary>One 7.4h entry per weekday in <c>[start, end]</c>.</summary>
    public static List<TimeEntry> WeekdayEntries(string employeeId, DateOnly start, DateOnly end)
    {
        var entries = new List<TimeEntry>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            entries.Add(new TimeEntry
            {
                EmployeeId = employeeId,
                Date = d,
                Hours = 7.4m,
                AgreementCode = "HK",
                OkVersion = d < new DateOnly(2026, 4, 1) ? "OK24" : "OK26",
            });
        }
        return entries;
    }

    public static PeriodCalculationService BuildPcs(
        RecordingRuleEngine engine,
        IEventStore? eventStore = null,
        IEmploymentProfileResolver? profileResolver = null,
        IEmploymentWindowResolver? windowResolver = null,
        IReadOnlyList<RuleClassification>? ruleSet = null)
    {
        // S137 Step-5a (Reviewer WARNING 1): PCS REFUSES a profile resolver without a window
        // resolver — that pairing is the dropped-DI shape that would silently pay a leaver all
        // month. Plan-first tests that only need a counting profile resolver get an unbounded
        // fake window here (every segment EMPLOYED — the shape of every pre-S136 employee) so
        // the fixture never trips the guard by accident. Tests ABOUT the guard construct PCS
        // directly (PcsConstructionCouplingTests).
        if (profileResolver is not null && windowResolver is null)
            windowResolver = new FakeWindowResolver(new EmploymentWindow(null, null));

        return new PeriodCalculationService(
            new SingleClientHttpFactory(engine),
            // Never dereferenced: the stub rule engine returns no line items, so the
            // export mapper's per-line mapping lookup is never reached (see class doc).
            mappingService: null!,
            eventStore ?? new InMemoryEventStore(),
            // The projection insert is inside PCS's swallowing try/catch by design;
            // the outcome degrades to AuditState.EventOnly (see class doc).
            connectionFactory: null!,
            Configuration(),
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryRuleClassificationProvider(ruleSet ?? RuleSet),
            localAgreementProfileRepo: null,
            profileResolver: profileResolver,
            employmentWindowResolver: windowResolver,
            employeeProfileRepo: null);
    }

    /// <summary>The in-memory configuration every PCS in this suite is built with.</summary>
    public static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
        })
        .Build();

    // ---------------------------------------------------------------------
    // Recording rule-engine stub
    // ---------------------------------------------------------------------

    /// <summary>
    /// Records every rule-engine call PCS makes (endpoint, rule id, the segment range PCS
    /// asked about, and the flex <c>previousBalance</c> it sent) and answers via a
    /// configurable responder. Time-rule calls within a segment run concurrently
    /// (<c>Task.WhenAll</c>), hence the lock; segments themselves are evaluated in order,
    /// so per-endpoint call order reflects segment order.
    /// </summary>
    public sealed class RecordingRuleEngine : HttpMessageHandler
    {
        public sealed record Call(
            string Path,
            string? RuleId,
            DateOnly PeriodStart,
            DateOnly PeriodEnd,
            decimal? PreviousBalance,
            decimal? ProfilePartTimeFraction);

        private readonly List<Call> _calls = new();
        private readonly Func<Call, HttpResponseMessage> _responder;

        public RecordingRuleEngine(Func<Call, HttpResponseMessage>? responder = null)
            => _responder = responder ?? AllSucceedWithNoLineItems;

        public IReadOnlyList<Call> Calls
        {
            get { lock (_calls) return _calls.ToList(); }
        }

        public IReadOnlyList<Call> FlexCalls =>
            Calls.Where(c => c.Path.EndsWith("/evaluate-flex", StringComparison.Ordinal)).ToList();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? "{}"
                : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var call = new Call(
                Path: request.RequestUri?.AbsolutePath ?? string.Empty,
                RuleId: root.TryGetProperty("ruleId", out var rid) ? rid.GetString() : null,
                PeriodStart: DateOnly.Parse(root.GetProperty("periodStart").GetString()!),
                PeriodEnd: DateOnly.Parse(root.GetProperty("periodEnd").GetString()!),
                PreviousBalance: root.TryGetProperty("previousBalance", out var pb) ? pb.GetDecimal() : null,
                ProfilePartTimeFraction: root.TryGetProperty("profile", out var prof)
                    && prof.TryGetProperty("partTimeFraction", out var ptf)
                    && ptf.ValueKind == JsonValueKind.Number
                    ? ptf.GetDecimal()
                    : null);

            lock (_calls) _calls.Add(call);
            return _responder(call);
        }

        /// <summary>Every endpoint succeeds with an EMPTY line-item list (flex delta = 0).</summary>
        public static HttpResponseMessage AllSucceedWithNoLineItems(Call call) =>
            Json(new
            {
                ruleId = RuleIdFor(call),
                employeeId = "EMP",
                success = true,
                lineItems = Array.Empty<object>(),
            });

        /// <summary>Every endpoint fails with HTTP 500 — the "rule engine down" shape.</summary>
        public static HttpResponseMessage AllFail(Call call) =>
            new(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("{\"error\":\"rule engine unavailable\"}", Encoding.UTF8, "application/json"),
            };

        private static string RuleIdFor(Call call) =>
            call.Path.EndsWith("/evaluate-flex", StringComparison.Ordinal) ? "FLEX_BALANCE"
            : call.Path.EndsWith("/evaluate-absence", StringComparison.Ordinal) ? "ABSENCE"
            : call.RuleId ?? "UNKNOWN";

        private static HttpResponseMessage Json(object payload)
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    // ---------------------------------------------------------------------
    // Recording fakes for the two resolvers + the event store
    // ---------------------------------------------------------------------

    /// <summary>In-memory <see cref="IEventStore"/> — captures the emitted
    /// <see cref="SegmentManifestCreated"/> so the manifest's typed segments can be read.</summary>
    public sealed class InMemoryEventStore : IEventStore
    {
        private readonly Dictionary<string, List<IDomainEvent>> _streams = new(StringComparer.Ordinal);
        private readonly List<IDomainEvent> _all = new();

        public IReadOnlyList<IDomainEvent> All => _all;

        public SegmentManifestCreated? Manifest => _all.OfType<SegmentManifestCreated>().SingleOrDefault();

        public Task AppendAsync(string streamId, IDomainEvent @event, CancellationToken ct = default)
        {
            if (!_streams.TryGetValue(streamId, out var list))
                _streams[streamId] = list = new List<IDomainEvent>();
            list.Add(@event);
            _all.Add(@event);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<IDomainEvent>> ReadStreamAsync(string streamId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IDomainEvent>>(
                _streams.TryGetValue(streamId, out var list) ? list.ToList() : Array.Empty<IDomainEvent>());

        public Task<IReadOnlyList<IDomainEvent>> ReadAllAsync(int fromPosition = 0, int maxCount = 1000, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IDomainEvent>>(_all.Skip(fromPosition).Take(maxCount).ToList());

        public Task<T?> ReadLatestOfTypeAsync<T>(string streamId, CancellationToken ct = default)
            where T : class, IDomainEvent
            => Task.FromResult(_streams.TryGetValue(streamId, out var list) ? list.OfType<T>().LastOrDefault() : null);
    }

    /// <summary>
    /// <see cref="IEmploymentProfileResolver"/> fake that RECORDS every as-of date PCS asks
    /// about and always answers with a covering profile. The recorded dates are the ADR-040
    /// D10 assertion surface: a NOT_EMPLOYED segment's start date must never appear.
    /// </summary>
    public sealed class CountingProfileResolver : IEmploymentProfileResolver
    {
        private readonly List<DateOnly> _asOfDates = new();
        public IReadOnlyList<DateOnly> AsOfDates => _asOfDates;

        public Task<EmploymentProfile?> GetByEmployeeIdAtAsync(
            string employeeId, DateOnly asOfDate, CancellationToken ct = default)
        {
            _asOfDates.Add(asOfDate);
            return Task.FromResult<EmploymentProfile?>(Profile(employeeId));
        }
    }

    /// <summary>
    /// <see cref="IEmploymentWindowResolver"/> fake returning fixed windows and recording the
    /// (employee, from, to) range PCS asked for. <c>GetStatusAsync</c> is deliberately
    /// unsupported: planning must use the range-scoped read, never a per-date loop.
    /// </summary>
    public sealed class FakeWindowResolver : IEmploymentWindowResolver
    {
        private readonly IReadOnlyList<EmploymentWindow> _windows;
        private readonly List<(string EmployeeId, DateOnly From, DateOnly To)> _calls = new();

        public FakeWindowResolver(params EmploymentWindow[] windows) => _windows = windows;

        public IReadOnlyList<(string EmployeeId, DateOnly From, DateOnly To)> Calls => _calls;

        public Task<EmploymentWindowStatus> GetStatusAsync(string employeeId, DateOnly date, CancellationToken ct = default)
            => throw new NotSupportedException("Planning must use GetWindowsAsync (range-scoped), not a per-date status loop.");

        public Task<IReadOnlyList<EmploymentWindow>> GetWindowsAsync(string employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            _calls.Add((employeeId, from, to));
            return Task.FromResult(_windows);
        }
    }

    public sealed class SingleClientHttpFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientHttpFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class InMemoryRuleClassificationProvider : IRuleClassificationProvider
    {
        private readonly IReadOnlyList<RuleClassification> _set;
        public InMemoryRuleClassificationProvider(IReadOnlyList<RuleClassification> set) => _set = set;
        public IReadOnlyList<RuleClassification> GetClassifications() => _set;
    }
}
