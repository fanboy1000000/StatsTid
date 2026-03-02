using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using System.Text.Json;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// The "glue" service that connects Rule Engine output to Payroll Export.
/// Calls the Rule Engine via HTTP (respecting service boundaries), maps results
/// to wage types via PayrollMappingService, and produces PayrollExportLines
/// with full traceability (SourceRuleId, SourceTimeType).
/// </summary>
public sealed class PeriodCalculationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PayrollMappingService _mappingService;
    private readonly IEventStore _eventStore;
    private readonly ILogger<PeriodCalculationService> _logger;
    private readonly string _ruleEngineUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public PeriodCalculationService(
        IHttpClientFactory httpClientFactory,
        PayrollMappingService mappingService,
        IEventStore eventStore,
        IConfiguration configuration,
        ILogger<PeriodCalculationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _mappingService = mappingService;
        _eventStore = eventStore;
        _logger = logger;
        _ruleEngineUrl = configuration["ServiceUrls:RuleEngine"] ?? "http://rule-engine:8080";
    }

    public async Task<PeriodCalculationResult> CalculateAsync(
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousFlexBalance,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        var ruleResults = new List<CalculationResult>();
        var allLineItems = new List<(string RuleId, CalculationLineItem Item)>();
        var failureCount = 0;

        var client = _httpClientFactory.CreateClient();
        if (authorizationHeader is not null)
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);
        if (correlationId.HasValue)
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", correlationId.Value.ToString());

        // ---------------------------------------------------------------
        // 1. Call Rule Engine for each rule via HTTP POST
        // ---------------------------------------------------------------

        // Parallelize independent rules: 3 time rules + absence
        var timeRuleTasks = new[] { "NORM_CHECK_37H", "SUPPLEMENT_CALC", "OVERTIME_CALC" }
            .Select(ruleId => CallTimeRuleAsync(client, ruleId, profile, entries, periodStart, periodEnd, ct))
            .ToList();
        var absenceTask = CallAbsenceRuleAsync(client, profile, absences, periodStart, periodEnd, ct);

        var allTasks = timeRuleTasks.Append(absenceTask).ToArray();
        await Task.WhenAll(allTasks);

        // Collect results from parallel calls
        foreach (var task in timeRuleTasks)
        {
            var result = await task; // Already completed
            if (result is not null)
            {
                ruleResults.Add(result);
                if (result.Success)
                {
                    foreach (var item in result.LineItems)
                        allLineItems.Add((result.RuleId, item));
                }
            }
            else
            {
                failureCount++;
            }
        }

        // Absence result
        var absenceResult = await absenceTask; // Already completed
        if (absenceResult is not null)
        {
            ruleResults.Add(absenceResult);
            if (absenceResult.Success)
            {
                foreach (var item in absenceResult.LineItems)
                    allLineItems.Add((absenceResult.RuleId, item));
            }
        }
        else
        {
            failureCount++;
        }

        // Flex balance rule — sequential (depends on absence results for norm credit)
        var flexResult = await CallFlexRuleAsync(client, profile, entries, absences, periodStart, periodEnd, previousFlexBalance, ct);
        if (flexResult is not null)
        {
            ruleResults.Add(flexResult);
            if (flexResult.Success)
            {
                foreach (var item in flexResult.LineItems)
                    allLineItems.Add((flexResult.RuleId, item));
            }
        }
        else
        {
            failureCount++;
        }

        // If ALL rules failed, report overall failure
        var totalRules = 5; // 3 time rules + absence + flex
        if (failureCount >= totalRules)
        {
            _logger.LogError(
                "All {TotalRules} rule evaluations failed for employee {EmployeeId} period {PeriodStart}-{PeriodEnd}",
                totalRules, profile.EmployeeId, periodStart, periodEnd);

            return new PeriodCalculationResult
            {
                EmployeeId = profile.EmployeeId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                AgreementCode = profile.AgreementCode,
                OkVersion = profile.OkVersion,
                RuleResults = ruleResults,
                ExportLines = [],
                Success = false,
                ErrorMessage = "All rule evaluations failed"
            };
        }

        // ---------------------------------------------------------------
        // 2. Map all line items to wage types via PayrollMappingService
        // ---------------------------------------------------------------
        var exportLines = new List<PayrollExportLine>();

        foreach (var (ruleId, lineItem) in allLineItems)
        {
            var mapping = await _mappingService.GetMappingAsync(
                lineItem.TimeType, profile.OkVersion, profile.AgreementCode, ct);

            if (mapping is null)
            {
                _logger.LogWarning(
                    "No wage type mapping for {TimeType}/{OkVersion}/{Agreement} — skipping line item from rule {RuleId}",
                    lineItem.TimeType, profile.OkVersion, profile.AgreementCode, ruleId);
                continue;
            }

            exportLines.Add(new PayrollExportLine
            {
                EmployeeId = profile.EmployeeId,
                WageType = mapping.WageType,
                Hours = lineItem.Hours,
                Amount = lineItem.Hours * lineItem.Rate,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                OkVersion = profile.OkVersion,
                SourceRuleId = ruleId,
                SourceTimeType = lineItem.TimeType
            });
        }

        _logger.LogInformation(
            "Period calculation complete for {EmployeeId}: {RuleCount} rules evaluated, {LineCount} export lines produced",
            profile.EmployeeId, ruleResults.Count, exportLines.Count);

        // ---------------------------------------------------------------
        // 3. Emit PeriodCalculationCompleted event
        // ---------------------------------------------------------------
        try
        {
            var calcEvent = new PeriodCalculationCompleted
            {
                EmployeeId = profile.EmployeeId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                AgreementCode = profile.AgreementCode,
                OkVersion = profile.OkVersion,
                RuleCount = ruleResults.Count,
                ExportLineCount = exportLines.Count,
                TotalHours = exportLines.Sum(l => l.Hours),
                CorrelationId = correlationId
            };

            await _eventStore.AppendAsync(
                $"period-calc-{profile.EmployeeId}-{periodStart:yyyy-MM-dd}",
                calcEvent, ct);
        }
        catch (Exception ex)
        {
            // Event emission failure should not fail the calculation
            _logger.LogWarning(ex, "Failed to emit PeriodCalculationCompleted event for {EmployeeId}", profile.EmployeeId);
        }

        return new PeriodCalculationResult
        {
            EmployeeId = profile.EmployeeId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            AgreementCode = profile.AgreementCode,
            OkVersion = profile.OkVersion,
            RuleResults = ruleResults,
            ExportLines = exportLines,
            Success = true
        };
    }

    // ---------------------------------------------------------------
    // Private helpers — HTTP calls to Rule Engine
    // ---------------------------------------------------------------

    private async Task<CalculationResult?> CallTimeRuleAsync(
        HttpClient client,
        string ruleId,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                ruleId,
                profile,
                entries,
                periodStart,
                periodEnd
            };

            var response = await client.PostAsJsonAsync(
                $"{_ruleEngineUrl}/api/rules/evaluate", payload, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Rule engine returned {StatusCode} for {RuleId}: {Body}",
                    (int)response.StatusCode, ruleId, body);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<CalculationResult>(json, JsonOptions);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call rule engine for {RuleId}", ruleId);
            return null;
        }
    }

    private async Task<CalculationResult?> CallAbsenceRuleAsync(
        HttpClient client,
        EmploymentProfile profile,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                profile,
                absences,
                periodStart,
                periodEnd
            };

            var response = await client.PostAsJsonAsync(
                $"{_ruleEngineUrl}/api/rules/evaluate-absence", payload, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Rule engine returned {StatusCode} for absence evaluation: {Body}",
                    (int)response.StatusCode, body);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<CalculationResult>(json, JsonOptions);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call rule engine for absence evaluation");
            return null;
        }
    }

    private async Task<CalculationResult?> CallFlexRuleAsync(
        HttpClient client,
        EmploymentProfile profile,
        IReadOnlyList<TimeEntry> entries,
        IReadOnlyList<AbsenceEntry> absences,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal previousBalance,
        CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                profile,
                entries,
                absences,
                periodStart,
                periodEnd,
                previousBalance
            };

            var response = await client.PostAsJsonAsync(
                $"{_ruleEngineUrl}/api/rules/evaluate-flex", payload, JsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Rule engine returned {StatusCode} for flex evaluation: {Body}",
                    (int)response.StatusCode, body);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var result = JsonSerializer.Deserialize<CalculationResult>(json, JsonOptions);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call rule engine for flex evaluation");
            return null;
        }
    }
}
