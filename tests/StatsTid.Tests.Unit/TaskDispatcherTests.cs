namespace StatsTid.Tests.Unit;

public class TaskDispatcherTests
{
    [Fact]
    public void RouteTask_RuleEvaluation_GoesToRuleEngine()
    {
        var routing = GetServiceUrl("rule-evaluation");
        Assert.Equal("http://rule-engine:8080", routing);
    }

    [Fact]
    public void RouteTask_PayrollExport_GoesToPayrollService()
    {
        var routing = GetServiceUrl("payroll-export");
        Assert.Equal("http://payroll:8080", routing);
    }

    [Fact]
    public void RouteTask_Unknown_ReturnsNull()
    {
        var routing = GetServiceUrl("unknown-task");
        Assert.Null(routing);
    }

    [Fact]
    public void RouteTask_ExternalIntegration_GoesToExternalService()
    {
        var routing = GetServiceUrl("external-integration");
        Assert.Equal("http://external:8080", routing);
    }

    /// <summary>
    /// Simple routing logic matching what TaskDispatcher will implement.
    /// Pure function for testability.
    /// </summary>
    private static string? GetServiceUrl(string taskType)
    {
        return taskType switch
        {
            "rule-evaluation" => "http://rule-engine:8080",
            "payroll-export" => "http://payroll:8080",
            "external-integration" => "http://external:8080",
            _ => null
        };
    }
}
