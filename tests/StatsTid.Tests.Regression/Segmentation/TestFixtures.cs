using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// Shared test fixtures for the Sprint 20 segmentation regression suite. Centralises
/// the rule-engine HTTP stubbing pattern, profile/entry builders, wage-type-mapping
/// seed data, and the "build a real <see cref="PeriodCalculationService"/> wired to a
/// mocked <see cref="IHttpClientFactory"/>" helper.
///
/// <para>
/// The Rule Engine is stubbed (not containerised) because the manifest-emission and
/// replay-determinism contracts under test are properties of <see cref="PeriodCalculationService"/>
/// itself — the rule outputs only need to be plausible enough that <c>CalculateAsync</c>
/// proceeds through to the export-line + manifest stage. A second container would
/// double the cold-start cost without exercising any contract this suite cares about.
/// </para>
/// </summary>
internal static class TestFixtures
{
    public static EmploymentProfile Profile(string employeeId, string okVersion = "OK24") => new()
    {
        EmployeeId = employeeId,
        AgreementCode = "HK",
        OkVersion = okVersion,
        WeeklyNormHours = 37.0m,
        EmploymentCategory = "Standard",
        PartTimeFraction = 1.0m,
    };

    /// <summary>
    /// Five 7.4-hour weekday entries plus one weekend entry across a straddling period
    /// — enough volume that NormCheckRule produces a NORMAL_HOURS line item per day,
    /// which exercises wage-type mapping AND per-line manifest-id stamping.
    /// </summary>
    public static List<TimeEntry> WeekdayEntriesForPeriod(
        string employeeId, DateOnly periodStart, DateOnly periodEnd)
    {
        var entries = new List<TimeEntry>();
        for (var d = periodStart; d <= periodEnd; d = d.AddDays(1))
        {
            // Skip weekends to keep the entry stream lean — we don't need full coverage.
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
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

    /// <summary>
    /// Boundary sources containing the OK24 -> OK26 transition on 2026-04-01 and nothing else.
    /// </summary>
    public static BoundarySources OkStraddleSources() => new(
        OkTransitions: new List<(DateOnly, string, string)>
        {
            (new DateOnly(2026, 4, 1), "OK24", "OK26")
        },
        AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
        PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
        EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
        NonDatedSourceValues: new Dictionary<string, object?>());

    /// <summary>
    /// The five canonical cell-test rule shapes, registered with the merge strategies
    /// that <see cref="PeriodCalculationService"/> uses to dispatch per-rule merges.
    /// Matches the production registry's coverage of the 5 populated cells.
    /// </summary>
    public static readonly RuleClassification[] RuleSet =
    {
        new("SUPPLEMENT_CALC",  Span.Entry,       SplitBehavior.SegmentSafe,    Family.Calculation, MergeStrategy.Concatenate,             SnapshotContract: null),
        new("ON_CALL_DUTY",     Span.Entry,       SplitBehavior.SegmentSafe,    Family.Calculation, MergeStrategy.Concatenate,             SnapshotContract: null),
        new("OVERTIME_CALC",    Span.Window,      SplitBehavior.AlignedWindow,  Family.Calculation, MergeStrategy.RejectIfMultipleSegments, SnapshotContract: null),
        new("NORM_CHECK_37H",   Span.Period,      SplitBehavior.SegmentSafe,    Family.Calculation, MergeStrategy.Concatenate,             SnapshotContract: null),
        new("FLEX_BALANCE",     Span.CrossPeriod, SplitBehavior.Mergeable,      Family.Calculation, MergeStrategy.Custom,                  SnapshotContract: null),
    };

    public static async Task SeedWageTypeMappingsAsync(DbConnectionFactory factory)
    {
        var rows = new (string TimeType, string WageType, string Ok)[]
        {
            ("NORMAL_HOURS", "SLS_0110", "OK24"),
            ("NORMAL_HOURS", "SLS_0110", "OK26"),
            ("OVERTIME_50",  "SLS_0210", "OK24"),
            ("OVERTIME_50",  "SLS_0210", "OK26"),
            ("OVERTIME_100", "SLS_0220", "OK24"),
            ("OVERTIME_100", "SLS_0220", "OK26"),
            ("VACATION",     "SLS_0310", "OK24"),
            ("VACATION",     "SLS_0310", "OK26"),
        };
        await using var conn = factory.Create();
        await conn.OpenAsync();
        foreach (var r in rows)
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO wage_type_mappings (time_type, wage_type, ok_version, agreement_code, position, description)
                VALUES (@t, @w, @ok, 'HK', '', NULL)
                ON CONFLICT DO NOTHING
                """, conn);
            cmd.Parameters.AddWithValue("t", r.TimeType);
            cmd.Parameters.AddWithValue("w", r.WageType);
            cmd.Parameters.AddWithValue("ok", r.Ok);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Builds a <see cref="PeriodCalculationService"/> wired to a stub HTTP handler that
    /// returns a deterministic NORMAL_HOURS line item per matched entry — enough to
    /// produce export lines and exercise per-line manifest stamping. The default handler
    /// shape returns a fresh <see cref="CalculationResult"/> for every rule call.
    /// </summary>
    public static PeriodCalculationService BuildPcs(
        DbConnectionFactory factory,
        PostgresEventStore eventStore,
        Func<HttpRequestMessage, HttpResponseMessage>? handler = null)
    {
        var stubHandler = new StubHandler(handler ?? DefaultRuleEngineHandler);
        var httpFactory = new SingleClientFactory(stubHandler);

        var mappingService = new PayrollMappingService(factory, NullLogger<PayrollMappingService>.Instance);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceUrls:RuleEngine"] = "http://rule-engine.test",
            })
            .Build();

        return new PeriodCalculationService(
            httpFactory,
            mappingService,
            eventStore,
            factory,
            configuration,
            NullLogger<PeriodCalculationService>.Instance,
            classificationProvider: new InMemoryRuleClassificationProvider(RuleSet));
    }

    /// <summary>
    /// Default Rule Engine stub: every endpoint returns a NORMAL_HOURS line item per
    /// matched entry. The flex endpoint returns zero delta. The absence endpoint returns
    /// an empty success. This is the minimum shape PCS needs to walk the full code path
    /// (per-segment evaluation -> per-rule merge -> export-line mapping -> manifest emit).
    /// </summary>
    public static HttpResponseMessage DefaultRuleEngineHandler(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.EndsWith("/api/rules/evaluate", StringComparison.Ordinal))
        {
            return EvaluateTimeRuleResponse(request);
        }
        if (path.EndsWith("/api/rules/evaluate-absence", StringComparison.Ordinal))
        {
            return SimpleSuccessResponse("ABSENCE");
        }
        if (path.EndsWith("/api/rules/evaluate-flex", StringComparison.Ordinal))
        {
            return SimpleSuccessResponse("FLEX_BALANCE");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("unknown rule endpoint"),
        };
    }

    private static HttpResponseMessage EvaluateTimeRuleResponse(HttpRequestMessage request)
    {
        // Read the request body to get the rule id and entries — produces a NORMAL_HOURS
        // line item per entry so export-line counts vary with the input.
        string body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "{}";
        using var doc = JsonDocument.Parse(body);
        var ruleId = doc.RootElement.TryGetProperty("ruleId", out var rid) ? rid.GetString() ?? "UNKNOWN" : "UNKNOWN";
        var entries = doc.RootElement.TryGetProperty("entries", out var ents)
            ? ents.EnumerateArray().ToList()
            : new List<JsonElement>();
        var employeeId = doc.RootElement.TryGetProperty("profile", out var prof) &&
                          prof.TryGetProperty("employeeId", out var eid)
            ? eid.GetString() ?? "EMP"
            : "EMP";

        // Only NORM_CHECK_37H emits a line item per entry — others succeed with no items
        // so they don't multiply the export-line count.
        var lineItems = ruleId == "NORM_CHECK_37H"
            ? entries.Select(e =>
                {
                    var date = e.TryGetProperty("date", out var d) ? d.GetString() : null;
                    var hours = e.TryGetProperty("hours", out var h) ? h.GetDecimal() : 0m;
                    return new
                    {
                        timeType = "NORMAL_HOURS",
                        hours,
                        rate = 1.0m,
                        date,
                    };
                }).ToList<object>()
            : new List<object>();

        var payload = new
        {
            ruleId,
            employeeId,
            success = true,
            lineItems,
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private static HttpResponseMessage SimpleSuccessResponse(string ruleId)
    {
        var payload = new
        {
            ruleId,
            employeeId = "EMP",
            success = true,
            lineItems = Array.Empty<object>(),
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    public sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request);
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
