using System.Text.Json;

namespace StatsTid.Integrations.External.Services;

public sealed class ExternalApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExternalApiClient> _logger;
    private readonly string _mockExternalUrl;

    public ExternalApiClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ExternalApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _mockExternalUrl = configuration["ServiceUrls:MockExternal"] ?? "http://mock-external:8080";
    }

    public async Task<ExternalSendResult> SendAsync(object payload, Guid? correlationId = null, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient();
        if (correlationId.HasValue)
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", correlationId.Value.ToString());

        try
        {
            var response = await client.PostAsJsonAsync($"{_mockExternalUrl}/api/external/receive", payload, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("External API response: {StatusCode}", response.StatusCode);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<JsonElement>(body);
                return new ExternalSendResult
                {
                    Success = true,
                    MessageId = result.TryGetProperty("messageId", out var mid) ? Guid.Parse(mid.GetString()!) : Guid.NewGuid()
                };
            }

            return new ExternalSendResult { Success = false, ErrorMessage = body };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send to external API");
            return new ExternalSendResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}

public sealed class ExternalSendResult
{
    public required bool Success { get; init; }
    public Guid MessageId { get; init; }
    public string? ErrorMessage { get; init; }
}
