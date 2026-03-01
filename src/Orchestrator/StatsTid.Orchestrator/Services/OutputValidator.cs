using System.Text.Json;

namespace StatsTid.Orchestrator.Services;

public sealed class OutputValidator
{
    private readonly ILogger<OutputValidator> _logger;

    public OutputValidator(ILogger<OutputValidator> logger)
    {
        _logger = logger;
    }

    public bool Validate(string taskType, string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            _logger.LogWarning("Empty response body for task type: {TaskType}", taskType);
            return false;
        }

        try
        {
            var doc = JsonDocument.Parse(responseBody);

            return taskType switch
            {
                "rule-evaluation" => ValidateRuleEvaluationOutput(doc),
                "weekly-calculation" => ValidateWeeklyCalculationOutput(doc),
                "payroll-export" => ValidatePayrollExportOutput(doc),
                "external-integration" => ValidateExternalIntegrationOutput(doc),
                _ => true
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON response for task type: {TaskType}", taskType);
            return false;
        }
    }

    private bool ValidateRuleEvaluationOutput(JsonDocument doc)
    {
        return doc.RootElement.TryGetProperty("ruleId", out _)
            && doc.RootElement.TryGetProperty("success", out _);
    }

    private bool ValidatePayrollExportOutput(JsonDocument doc)
    {
        return doc.RootElement.TryGetProperty("exportId", out _)
            || doc.RootElement.TryGetProperty("success", out _);
    }

    private bool ValidateWeeklyCalculationOutput(JsonDocument doc)
    {
        return doc.RootElement.TryGetProperty("employeeId", out _)
            && doc.RootElement.TryGetProperty("success", out _);
    }

    private bool ValidateExternalIntegrationOutput(JsonDocument doc)
    {
        return doc.RootElement.TryGetProperty("messageId", out _)
            || doc.RootElement.TryGetProperty("status", out _);
    }
}
