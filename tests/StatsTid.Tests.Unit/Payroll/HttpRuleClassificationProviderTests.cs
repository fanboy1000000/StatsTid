using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using StatsTid.Auth;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Security;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// Wire-format and caching tests for <see cref="HttpRuleClassificationProvider"/> (TASK-2010).
/// These exercise the JSON deserialization shape independently of any live Rule Engine —
/// the integration test that hits the actual <c>GET /api/rules/classifications</c> endpoint
/// is owned by TASK-2012's manifest-replay sweep.
/// </summary>
public sealed class HttpRuleClassificationProviderTests
{
    private static readonly JwtSettings TestJwtSettings = new()
    {
        Issuer = "statstid",
        Audience = "statstid",
        SigningKey = "StatsTid_Test_SigningKey_MustBeAtLeast32BytesLong!",
        ExpirationMinutes = 5
    };

    [Fact]
    public void GetClassifications_ParsesIntegerKindShape()
    {
        // Default ASP.NET Core Web serialization emits enums as integers; this verifies the
        // converter handles that wire format cleanly.
        var json = """
        [
          {
            "ruleId": "NORM_CHECK_37H",
            "span": 2,
            "splitBehavior": 0,
            "family": 0,
            "mergeStrategy": { "kind": 0 },
            "snapshotContract": null
          }
        ]
        """;

        var provider = BuildProviderWith(json);
        var result = provider.GetClassifications();

        Assert.Single(result);
        var c = result[0];
        Assert.Equal("NORM_CHECK_37H", c.RuleId);
        Assert.Equal(Span.Period, c.Span);
        Assert.Equal(SplitBehavior.SegmentSafe, c.SplitBehavior);
        Assert.Equal(Family.Calculation, c.Family);
        Assert.Equal(MergeStrategyKind.Concatenate, c.MergeStrategy.Kind);
        Assert.Same(MergeStrategy.Concatenate, c.MergeStrategy);
        Assert.Null(c.SnapshotContract);
    }

    [Fact]
    public void GetClassifications_ParsesStringKindOnMergeStrategy()
    {
        // Forward-compat: if a JsonStringEnumConverter is added on the wire side later,
        // MergeStrategyJsonConverter still parses the string form of the kind discriminator.
        // The outer enums (Span/SplitBehavior/Family) keep integer encoding because that's
        // the actual production wire shape today.
        var json = """
        [
          {
            "ruleId": "FLEX_BALANCE",
            "span": 3,
            "splitBehavior": 2,
            "family": 0,
            "mergeStrategy": { "kind": "Custom" },
            "snapshotContract": {
              "ruleId": "FLEX_BALANCE",
              "nonDatedSourceFields": ["EmployeeProfile.PartTimeFraction"]
            }
          }
        ]
        """;

        var provider = BuildProviderWith(json);
        var result = provider.GetClassifications();

        Assert.Single(result);
        var c = result[0];
        Assert.Equal("FLEX_BALANCE", c.RuleId);
        Assert.Equal(Span.CrossPeriod, c.Span);
        Assert.Equal(SplitBehavior.Mergeable, c.SplitBehavior);
        Assert.Equal(MergeStrategyKind.Custom, c.MergeStrategy.Kind);
        Assert.Same(MergeStrategy.Custom, c.MergeStrategy);
        Assert.NotNull(c.SnapshotContract);
        Assert.Equal("FLEX_BALANCE", c.SnapshotContract!.RuleId);
        Assert.Single(c.SnapshotContract.NonDatedSourceFields);
    }

    [Fact]
    public void GetClassifications_ReturnsEmpty_OnHttpFailure()
    {
        // Rule Engine 5xx -> empty list (D9 silenced, warning logged).
        var provider = BuildProviderWithHandler(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("oops")
            }));

        var result = provider.GetClassifications();

        Assert.Empty(result);
    }

    [Fact]
    public void GetClassifications_DoesNotCacheEmptyFallback()
    {
        // Failure path: empty result should NOT be cached so a transient outage auto-recovers.
        var callCount = 0;
        var handler = new StubHandler(req =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("retry me")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"ruleId":"R1","span":0,"splitBehavior":0,"family":0,"mergeStrategy":{"kind":0},"snapshotContract":null}]""",
                    Encoding.UTF8, "application/json")
            };
        });

        var provider = BuildProviderWithHandler(handler);

        var first = provider.GetClassifications();
        var second = provider.GetClassifications();

        Assert.Empty(first);
        Assert.Single(second);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public void GetClassifications_CachesSuccessfulFetch()
    {
        // Successful fetch should be cached — no second HTTP call.
        var callCount = 0;
        var handler = new StubHandler(req =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"ruleId":"R1","span":0,"splitBehavior":0,"family":0,"mergeStrategy":{"kind":0},"snapshotContract":null}]""",
                    Encoding.UTF8, "application/json")
            };
        });

        var provider = BuildProviderWithHandler(handler);

        var first = provider.GetClassifications();
        var second = provider.GetClassifications();

        Assert.Single(first);
        Assert.Same(first, second);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void GetClassifications_AttachesBearerToken()
    {
        // Verifies the provider mints a Bearer JWT and attaches it to outbound requests.
        string? observedAuth = null;
        var handler = new StubHandler(req =>
        {
            observedAuth = req.Headers.Authorization?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });

        var provider = BuildProviderWithHandler(handler);
        provider.GetClassifications();

        Assert.NotNull(observedAuth);
        Assert.StartsWith("Bearer ", observedAuth);
        // Token should be a non-empty JWT (three dot-separated base64 segments).
        var token = observedAuth!["Bearer ".Length..];
        Assert.Equal(3, token.Split('.').Length);
    }

    // -----------------------------------------------------------------------
    // Test helpers
    // -----------------------------------------------------------------------

    private static HttpRuleClassificationProvider BuildProviderWith(string responseJson)
    {
        return BuildProviderWithHandler(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            }));
    }

    private static HttpRuleClassificationProvider BuildProviderWithHandler(StubHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://rule-engine.test") };
        var tokenService = new JwtTokenService(TestJwtSettings);
        return new HttpRuleClassificationProvider(
            httpClient,
            tokenService,
            NullLogger<HttpRuleClassificationProvider>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _responder(request);
        }
    }
}
