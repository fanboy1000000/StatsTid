using System.Text.Json;

namespace StatsTid.Orchestrator.Services;

/// <summary>
/// Orchestrates a composite weekly calculation:
/// 1. Call Rule Engine: NORM_CHECK_37H
/// 2. Call Rule Engine: SUPPLEMENT_CALC
/// 3. Call Rule Engine: OVERTIME_CALC
/// 4. Call Rule Engine: ABSENCE_CALC
/// 5. Call Rule Engine: FLEX_BALANCE (evaluate-flex)
/// Aggregates all results and returns composite output.
/// </summary>
public sealed class WeeklyCalculationPipeline
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WeeklyCalculationPipeline> _logger;
    private readonly string _ruleEngineUrl;
    private readonly string _backendUrl;

    public WeeklyCalculationPipeline(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WeeklyCalculationPipeline> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _ruleEngineUrl = configuration["ServiceUrls:RuleEngine"] ?? "http://rule-engine:8080";
        _backendUrl = configuration["ServiceUrls:Backend"] ?? "http://backend-api:8080";
    }

    public async Task<WeeklyCalculationResult> ExecuteAsync(
        Dictionary<string, object> parameters,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        var employeeId = parameters["employeeId"].ToString()!;
        var agreementCode = parameters["agreementCode"].ToString()!;
        var okVersion = parameters["okVersion"].ToString()!;
        var periodStart = parameters["periodStart"].ToString()!;
        var periodEnd = parameters["periodEnd"].ToString()!;
        var weeklyNormHours = Convert.ToDecimal(parameters["weeklyNormHours"]);
        var partTimeFraction = Convert.ToDecimal(parameters["partTimeFraction"]);
        var previousFlexBalance = Convert.ToDecimal(parameters["previousFlexBalance"]);

        var client = _httpClientFactory.CreateClient();
        if (authorizationHeader is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);
        if (correlationId.HasValue)
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", correlationId.Value.ToString());

        var allLineItems = new List<object>();
        object? flexResult = null;

        var profile = new
        {
            employeeId,
            agreementCode,
            okVersion,
            weeklyNormHours,
            employmentCategory = "Standard",
            isPartTime = partTimeFraction < 1.0m,
            partTimeFraction
        };

        // 1. Fetch time entries from Backend
        var entriesResponse = await client.GetAsync($"{_backendUrl}/api/time-entries/{employeeId}", ct);
        var entriesJson = await entriesResponse.Content.ReadAsStringAsync(ct);
        var entries = JsonSerializer.Deserialize<JsonElement>(entriesJson);

        // 2. Fetch absences from Backend
        var absencesResponse = await client.GetAsync($"{_backendUrl}/api/absences/{employeeId}", ct);
        var absencesJson = await absencesResponse.Content.ReadAsStringAsync(ct);
        var absences = JsonSerializer.Deserialize<JsonElement>(absencesJson);

        // 3. NORM_CHECK_37H
        var normResult = await CallRuleEvaluateAsync(client, new
        {
            ruleId = "NORM_CHECK_37H",
            profile,
            entries,
            periodStart,
            periodEnd
        }, ct);
        if (normResult is not null) allLineItems.Add(normResult);

        // 4. SUPPLEMENT_CALC
        var supplementResult = await CallRuleEvaluateAsync(client, new
        {
            ruleId = "SUPPLEMENT_CALC",
            profile,
            entries,
            periodStart,
            periodEnd
        }, ct);
        if (supplementResult is not null) allLineItems.Add(supplementResult);

        // 5. OVERTIME_CALC
        var overtimeResult = await CallRuleEvaluateAsync(client, new
        {
            ruleId = "OVERTIME_CALC",
            profile,
            entries,
            periodStart,
            periodEnd
        }, ct);
        if (overtimeResult is not null) allLineItems.Add(overtimeResult);

        // 6. ABSENCE_CALC
        var absenceResult = await CallAbsenceEvaluateAsync(client, new
        {
            profile,
            absences,
            periodStart,
            periodEnd
        }, ct);
        if (absenceResult is not null) allLineItems.Add(absenceResult);

        // 7. FLEX_BALANCE
        flexResult = await CallFlexEvaluateAsync(client, new
        {
            profile,
            entries,
            absences,
            periodStart,
            periodEnd,
            previousBalance = previousFlexBalance
        }, ct);

        return new WeeklyCalculationResult
        {
            EmployeeId = employeeId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            AgreementCode = agreementCode,
            OkVersion = okVersion,
            RuleResults = allLineItems,
            FlexBalance = flexResult,
            Success = true
        };
    }

    private async Task<object?> CallRuleEvaluateAsync(HttpClient client, object payload, CancellationToken ct)
    {
        try
        {
            var response = await client.PostAsJsonAsync($"{_ruleEngineUrl}/api/rules/evaluate", payload, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return response.IsSuccessStatusCode ? JsonSerializer.Deserialize<object>(body) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Rule evaluate call failed");
            return null;
        }
    }

    private async Task<object?> CallAbsenceEvaluateAsync(HttpClient client, object payload, CancellationToken ct)
    {
        try
        {
            var response = await client.PostAsJsonAsync($"{_ruleEngineUrl}/api/rules/evaluate-absence", payload, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return response.IsSuccessStatusCode ? JsonSerializer.Deserialize<object>(body) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Absence evaluate call failed");
            return null;
        }
    }

    private async Task<object?> CallFlexEvaluateAsync(HttpClient client, object payload, CancellationToken ct)
    {
        try
        {
            var response = await client.PostAsJsonAsync($"{_ruleEngineUrl}/api/rules/evaluate-flex", payload, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return response.IsSuccessStatusCode ? JsonSerializer.Deserialize<object>(body) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flex evaluate call failed");
            return null;
        }
    }
}

public sealed class WeeklyCalculationResult
{
    public required string EmployeeId { get; init; }
    public required string PeriodStart { get; init; }
    public required string PeriodEnd { get; init; }
    public required string AgreementCode { get; init; }
    public required string OkVersion { get; init; }
    public required List<object> RuleResults { get; init; }
    public object? FlexBalance { get; init; }
    public required bool Success { get; init; }
}
