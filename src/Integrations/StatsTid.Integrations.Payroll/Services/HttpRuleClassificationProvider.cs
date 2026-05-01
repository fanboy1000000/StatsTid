using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using StatsTid.Auth;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// HTTP-backed implementation of <see cref="IRuleClassificationProvider"/> that calls the
/// Rule Engine's <c>GET /api/rules/classifications</c> endpoint (TASK-2010 / S20). The
/// fetched <see cref="RuleClassification"/> set is cached for the lifetime of the process —
/// rule registry contents are immutable per process startup (a service restart is the only
/// way for the wire shape to change), so a single fetch on first call is sufficient.
///
/// <para>
/// <strong>Sync interface trade-off</strong>: <see cref="IRuleClassificationProvider.GetClassifications"/>
/// is synchronous (the planner uses the result as a process-level constant, not as a
/// per-request value), so this implementation issues a blocking
/// <see cref="HttpClient.Send(HttpRequestMessage, CancellationToken)"/> on first call. The
/// blocking call only happens once per process — subsequent calls return the cached list
/// without I/O. Switching the interface to async would touch
/// <see cref="PeriodCalculationService"/> internals (out of scope for TASK-2010); the sync
/// path is the lighter footprint.
/// </para>
///
/// <para>
/// <strong>JWT propagation</strong>: <see cref="GetClassifications"/> is invoked from inside
/// <see cref="PeriodCalculationService.CalculateWithOutcomeAsync"/> with no in-flight
/// <see cref="HttpContext"/> (it's resolved as process-level metadata before per-segment
/// evaluation). To satisfy the Rule Engine's <c>RequireAuthorization("Authenticated")</c>
/// policy on <c>GET /api/rules/classifications</c>, the provider mints a service-to-service
/// JWT using <see cref="JwtTokenService"/> on first call and caches it alongside the result.
/// The signing key already lives in DI (registered by
/// <see cref="JwtValidationSetup.AddStatsTidJwtAuth"/>), so the system token is signed with
/// the same key used to validate inbound tokens — no new secret material is introduced.
/// </para>
///
/// <para>
/// <strong>Failure mode</strong>: if the first fetch fails (Rule Engine unreachable, 5xx,
/// JSON parse error), the provider logs a warning and returns
/// <see cref="Array.Empty{RuleClassification}"/> WITHOUT caching the empty result —
/// subsequent calls retry the fetch. This matches the documented contract: returning empty
/// silences D9 invariants and routes per-rule MergeStrategy lookup through the "default to
/// Concatenate with warning" fallback in PCS, which is degraded-but-correct rather than
/// fatally aborted.
/// </para>
///
/// <para>
/// <strong>Custom converter for <see cref="MergeStrategy"/></strong>: <see cref="MergeStrategy"/>
/// has a private constructor (it's a closed set of named factory members on the type), so
/// <see cref="JsonSerializer"/> cannot construct one from <c>{"kind": &lt;n&gt;}</c> without
/// help. <see cref="MergeStrategyJsonConverter"/> below maps the wire shape onto the
/// static members. This works against either an integer-valued <c>kind</c> (default
/// ASP.NET Web serialization for enums) or a string-valued <c>kind</c> (if a future
/// <c>JsonStringEnumConverter</c> is added on the Rule Engine side).
/// </para>
/// </summary>
public sealed class HttpRuleClassificationProvider : IRuleClassificationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = BuildJsonOptions();

    private readonly HttpClient _httpClient;
    private readonly JwtTokenService _tokenService;
    private readonly ILogger<HttpRuleClassificationProvider> _logger;

    // Lock guards transitions of _cached from null to a non-null list. Once set, reads
    // are lock-free (the field write happens-before the publish via the lock release).
    private readonly object _cacheLock = new();
    private IReadOnlyList<RuleClassification>? _cached;

    public HttpRuleClassificationProvider(
        HttpClient httpClient,
        JwtTokenService tokenService,
        ILogger<HttpRuleClassificationProvider> logger)
    {
        _httpClient = httpClient;
        _tokenService = tokenService;
        _logger = logger;
    }

    public IReadOnlyList<RuleClassification> GetClassifications()
    {
        // Fast path: cache hit, lock-free.
        var snapshot = _cached;
        if (snapshot is not null)
            return snapshot;

        lock (_cacheLock)
        {
            // Re-check after acquiring the lock — another thread may have populated the cache.
            if (_cached is not null)
                return _cached;

            var fetched = TryFetchClassifications();
            if (fetched is not null)
            {
                _cached = fetched;
                return fetched;
            }

            // Fetch failed: return an empty list WITHOUT caching it. The empty result silences
            // D9 invariants in PCS (warning-logged at the merge step) but the next call will
            // retry the fetch, so transient Rule Engine outages auto-recover.
            return Array.Empty<RuleClassification>();
        }
    }

    private IReadOnlyList<RuleClassification>? TryFetchClassifications()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/rules/classifications");

            // Service-to-service token: minted with the system signing key already in DI.
            // Role "GlobalAdmin" satisfies the "Authenticated" policy on the Rule Engine.
            var token = _tokenService.GenerateToken(
                employeeId: "system:payroll-classification-provider",
                name: "Payroll Classification Provider",
                role: "GlobalAdmin",
                agreementCode: "system");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // HttpClient.Send is the synchronous variant — appropriate here because the
            // sync IRuleClassificationProvider interface is resolved at calculation start
            // and called once per process, not per request.
            using var response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                _logger.LogWarning(
                    "Rule Engine returned {StatusCode} for GET /api/rules/classifications: {Body}",
                    (int)response.StatusCode, body);
                return null;
            }

            using var stream = response.Content.ReadAsStream();
            var list = JsonSerializer.Deserialize<List<RuleClassification>>(stream, JsonOptions);
            if (list is null)
            {
                _logger.LogWarning(
                    "Rule Engine returned null body for GET /api/rules/classifications");
                return null;
            }

            _logger.LogInformation(
                "Fetched {Count} rule classification(s) from Rule Engine", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch rule classifications from Rule Engine. " +
                "D9 rule-side invariants will be silenced until the next call recovers.");
            return null;
        }
    }

    private static JsonSerializerOptions BuildJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new MergeStrategyJsonConverter());
        return options;
    }
}

/// <summary>
/// Bridges the wire shape of <see cref="MergeStrategy"/> (a record with a private
/// constructor and a single <see cref="MergeStrategy.Kind"/> property) onto the static
/// factory members on the type. Accepts either an integer-valued or a string-valued
/// <c>kind</c> property so this remains compatible with whichever enum-serialization
/// convention the Rule Engine ends up emitting (default integer, or string with
/// <c>JsonStringEnumConverter</c>).
/// </summary>
internal sealed class MergeStrategyJsonConverter : JsonConverter<MergeStrategy>
{
    public override MergeStrategy? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException(
                $"Expected StartObject for MergeStrategy, got {reader.TokenType}.");

        MergeStrategyKind? kind = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            var propertyName = reader.GetString();
            reader.Read();

            if (string.Equals(propertyName, "kind", StringComparison.OrdinalIgnoreCase))
            {
                kind = ReadKind(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        if (kind is null)
            throw new JsonException("MergeStrategy JSON is missing the 'kind' property.");

        return kind.Value switch
        {
            MergeStrategyKind.Concatenate => MergeStrategy.Concatenate,
            MergeStrategyKind.RejectIfMultipleSegments => MergeStrategy.RejectIfMultipleSegments,
            MergeStrategyKind.UnionDedupe => MergeStrategy.UnionDedupe,
            MergeStrategyKind.Custom => MergeStrategy.Custom,
            _ => throw new JsonException($"Unknown MergeStrategyKind value: {kind.Value}.")
        };
    }

    public override void Write(Utf8JsonWriter writer, MergeStrategy value, JsonSerializerOptions options)
    {
        // Round-trip: serialize as { "kind": <int> } matching the default ASP.NET Web shape.
        writer.WriteStartObject();
        writer.WriteNumber("kind", (int)value.Kind);
        writer.WriteEndObject();
    }

    private static MergeStrategyKind ReadKind(ref Utf8JsonReader reader)
    {
        // Accept both integer and string forms — the Rule Engine currently emits integers
        // (default Web defaults), but a JsonStringEnumConverter on either side would flip
        // it to strings without breaking us.
        if (reader.TokenType == JsonTokenType.Number)
        {
            var n = reader.GetInt32();
            if (Enum.IsDefined(typeof(MergeStrategyKind), n))
                return (MergeStrategyKind)n;
            throw new JsonException($"Unknown MergeStrategyKind integer value: {n}.");
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (Enum.TryParse<MergeStrategyKind>(s, ignoreCase: true, out var parsed))
                return parsed;
            throw new JsonException($"Unknown MergeStrategyKind string value: '{s}'.");
        }

        throw new JsonException(
            $"Expected Number or String for MergeStrategyKind, got {reader.TokenType}.");
    }
}
