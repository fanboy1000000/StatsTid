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
            ["payroll-export"] = configuration["ServiceUrls:Payroll"] ?? "http://payroll:8080",
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
            "payroll-export" => "/api/payroll/export",
            "external-integration" => "/api/external/send",
            _ => throw new InvalidOperationException($"Unknown task type: {taskType}")
        };

        var client = _httpClientFactory.CreateClient();
        if (authorizationHeader is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);
        if (correlationId.HasValue)
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", correlationId.Value.ToString());

        _logger.LogInformation("Dispatching {TaskType} to {Url}{Endpoint}", taskType, url, endpoint);

        return await client.PostAsJsonAsync($"{url}{endpoint}", payload, ct);
    }
}
