namespace StatsTid.Orchestrator.Services;

public sealed class TaskDispatcher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TaskDispatcher> _logger;
    private readonly Dictionary<string, string> _serviceRoutes;

    public TaskDispatcher(
        IHttpClientFactory httpClientFactory,
        ILogger<TaskDispatcher> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _serviceRoutes = new Dictionary<string, string>
        {
            ["rule-evaluation"] = configuration["ServiceUrls:RuleEngine"] ?? "http://rule-engine:8080",
            ["external-integration"] = configuration["ServiceUrls:External"] ?? "http://external:8080",
        };
    }

    public string? GetServiceUrl(string taskType)
    {
        return _serviceRoutes.GetValueOrDefault(taskType);
    }

    public async Task<HttpResponseMessage?> DispatchAsync(
        string taskType,
        object payload,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        var url = GetServiceUrl(taskType);
        if (url is null)
        {
            _logger.LogWarning("No route found for task type: {TaskType}", taskType);
            return null;
        }

        var endpoint = taskType switch
        {
            "rule-evaluation" => "/api/rules/evaluate",
            "external-integration" => "/api/external/send",
            _ => throw new InvalidOperationException($"Unknown task type: {taskType}")
        };

        var client = _httpClientFactory.CreateClient();
        if (authorizationHeader is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);
        if (correlationId.HasValue)
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", correlationId.Value.ToString());

        _logger.LogInformation("Dispatching {TaskType} to {Url}{Endpoint}", taskType, url, endpoint);

        var body = taskType == "rule-evaluation" ? RuleEvaluationBody(payload) : payload;
        return await client.PostAsJsonAsync($"{url}{endpoint}", body, ct);
    }

    /// <summary>
    /// The <c>rule-evaluation</c> body: the caller's parameters, with <c>config</c> set to the compiled
    /// agreement config of the forwarded profile's agreement code, OK version and position, the config the
    /// engine resolved for it before it stopped reading configuration of its own (ADR-043 D12, R-8). Any
    /// <c>config</c> member the caller sent is removed first, whatever its case, so no caller can hand the
    /// engine rules nobody configured (BC-6 (a)). The profile's members are read as the engine binds them
    /// (names case-insensitive, the last of a repeated name). A profile naming no compiled pair gets no
    /// config, so the engine refuses it and the task fails, as the engine's failure did; so does a body that is
    /// not a JSON object, which is forwarded as it came.
    /// </summary>
    private static object RuleEvaluationBody(object payload)
    {
        if (System.Text.Json.JsonSerializer.SerializeToNode(payload) is not System.Text.Json.Nodes.JsonObject body)
            return payload;
        foreach (var name in body.Select(member => member.Key)
                     .Where(key => string.Equals(key, "config", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            body.Remove(name);
        }

        var profile = Member(body, "profile") as System.Text.Json.Nodes.JsonObject;
        var agreementCode = StringMember(profile, "agreementCode");
        var okVersion = StringMember(profile, "okVersion");
        var position = StringMember(profile, "position");
        // shortcut(QUAL-262): the Orchestrator's rule-evaluation task evaluates with the compiled agreement config, not the dated configured one, so a published, position or local value does not reach it (R-8); upgrade when the Orchestrator's rule-evaluation path retires (stage 2, S1-4)
        var config = agreementCode is not null && okVersion is not null && StatsTid.SharedKernel.Config.CentralAgreementConfigs.HasConfig(agreementCode, okVersion) ? StatsTid.SharedKernel.Config.CentralAgreementConfigs.GetConfig(agreementCode, okVersion, position) : null;
        if (config is not null)
        {
            body["config"] = System.Text.Json.JsonSerializer.SerializeToNode(
                config, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        }

        return body;

        static System.Text.Json.Nodes.JsonNode? Member(System.Text.Json.Nodes.JsonObject? obj, string name) =>
            obj?.LastOrDefault(member => string.Equals(member.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

        static string? StringMember(System.Text.Json.Nodes.JsonObject? obj, string name) =>
            Member(obj, name) is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var text)
                ? text
                : null;
    }
}
