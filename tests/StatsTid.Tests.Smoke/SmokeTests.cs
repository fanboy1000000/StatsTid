using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace StatsTid.Tests.Smoke;

/// <summary>
/// Smoke tests run against Docker Compose services.
/// Requires: docker compose up (from docker/ directory)
/// </summary>
public class SmokeTests
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const string BackendUrl = "http://localhost:5100";
    private const string RuleEngineUrl = "http://localhost:5200";
    private const string OrchestratorUrl = "http://localhost:5300";
    private const string PayrollUrl = "http://localhost:5400";
    private const string ExternalUrl = "http://localhost:5500";
    private const string MockPayrollUrl = "http://localhost:5600";
    private const string MockExternalUrl = "http://localhost:5700";

    [Fact]
    public async Task AllServices_HealthCheck_ReturnsHealthy()
    {
        var urls = new[] { BackendUrl, RuleEngineUrl, OrchestratorUrl, PayrollUrl, ExternalUrl, MockPayrollUrl, MockExternalUrl };

        foreach (var url in urls)
        {
            var response = await _client.GetAsync($"{url}/health");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("healthy", body.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task RuleEngine_EvaluateNormCheck_ReturnsResult()
    {
        var request = new
        {
            ruleId = "NORM_CHECK_37H",
            profile = new
            {
                employeeId = "SMOKE001",
                agreementCode = "AC",
                okVersion = "OK24",
                weeklyNormHours = 37.0m,
                employmentCategory = "Standard",
                partTimeFraction = 1.0m
            },
            entries = new[]
            {
                new { employeeId = "SMOKE001", date = "2024-04-01", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = "SMOKE001", date = "2024-04-02", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = "SMOKE001", date = "2024-04-03", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = "SMOKE001", date = "2024-04-04", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
                new { employeeId = "SMOKE001", date = "2024-04-05", hours = 7.4m, agreementCode = "AC", okVersion = "OK24" },
            },
            periodStart = "2024-04-01",
            periodEnd = "2024-04-07"
        };

        var response = await _client.PostAsJsonAsync($"{RuleEngineUrl}/api/rules/evaluate", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("NORM_CHECK_37H", result.GetProperty("ruleId").GetString());
    }

    [Fact]
    public async Task Backend_RegisterAndRetrieveTimeEntry()
    {
        var registerRequest = new
        {
            employeeId = "SMOKE002",
            date = "2024-04-01",
            hours = 7.4m,
            agreementCode = "AC",
            okVersion = "OK24"
        };

        var registerResponse = await _client.PostAsJsonAsync($"{BackendUrl}/api/time-entries", registerRequest);
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var getResponse = await _client.GetAsync($"{BackendUrl}/api/time-entries/SMOKE002");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var entries = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(entries.GetArrayLength() > 0);
    }

    [Fact]
    public async Task MockPayroll_ReceivesExport()
    {
        var payload = new { employeeId = "SMOKE003", wageType = "SLS_0110", hours = 37.0m };
        var response = await _client.PostAsJsonAsync($"{MockPayrollUrl}/api/payroll/receive", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("success").GetBoolean());
    }
}
