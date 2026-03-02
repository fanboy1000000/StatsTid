using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// Tests for Sprint 4 models: PeriodCalculationCompleted event, PeriodCalculationResult,
/// PayrollExportLine traceability fields, and EventSerializer compliance.
/// </summary>
public class Sprint4ModelTests
{
    [Fact]
    public void PeriodCalculationCompleted_EventType_IsCorrect()
    {
        var evt = new PeriodCalculationCompleted
        {
            EmployeeId = "EMP001",
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            AgreementCode = "HK",
            OkVersion = "OK24",
            RuleCount = 5,
            ExportLineCount = 3,
            TotalHours = 42.5m
        };

        Assert.Equal("PeriodCalculationCompleted", evt.EventType);
    }

    [Fact]
    public void PeriodCalculationCompleted_ExtendsBase_HasActorTracking()
    {
        var correlationId = Guid.NewGuid();
        var evt = new PeriodCalculationCompleted
        {
            EmployeeId = "EMP001",
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            AgreementCode = "HK",
            OkVersion = "OK24",
            RuleCount = 5,
            ExportLineCount = 3,
            TotalHours = 42.5m,
            ActorId = "admin01",
            ActorRole = "Admin",
            CorrelationId = correlationId
        };

        Assert.Equal("admin01", evt.ActorId);
        Assert.Equal("Admin", evt.ActorRole);
        Assert.Equal(correlationId, evt.CorrelationId);
        Assert.NotEqual(Guid.Empty, evt.EventId);
        Assert.Equal(1, evt.Version);
    }

    [Fact]
    public void EventSerializer_PeriodCalculationCompleted_RoundTrips()
    {
        var originalEventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var occurredAt = new DateTime(2024, 4, 8, 12, 0, 0, DateTimeKind.Utc);

        var original = new PeriodCalculationCompleted
        {
            EventId = originalEventId,
            OccurredAt = occurredAt,
            CorrelationId = correlationId,
            ActorId = "system",
            ActorRole = "Service",
            EmployeeId = "EMP_RT_001",
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            AgreementCode = "AC",
            OkVersion = "OK24",
            RuleCount = 4,
            ExportLineCount = 7,
            TotalHours = 38.5m
        };

        // Serialize
        var json = EventSerializer.Serialize(original);

        // Deserialize
        var deserialized = EventSerializer.Deserialize("PeriodCalculationCompleted", json);

        Assert.IsType<PeriodCalculationCompleted>(deserialized);
        var roundTripped = (PeriodCalculationCompleted)deserialized;

        Assert.Equal(original.EventId, roundTripped.EventId);
        Assert.Equal(original.EmployeeId, roundTripped.EmployeeId);
        Assert.Equal(original.PeriodStart, roundTripped.PeriodStart);
        Assert.Equal(original.PeriodEnd, roundTripped.PeriodEnd);
        Assert.Equal(original.AgreementCode, roundTripped.AgreementCode);
        Assert.Equal(original.OkVersion, roundTripped.OkVersion);
        Assert.Equal(original.RuleCount, roundTripped.RuleCount);
        Assert.Equal(original.ExportLineCount, roundTripped.ExportLineCount);
        Assert.Equal(original.TotalHours, roundTripped.TotalHours);
        Assert.Equal(original.ActorId, roundTripped.ActorId);
        Assert.Equal(original.ActorRole, roundTripped.ActorRole);
        Assert.Equal(original.CorrelationId, roundTripped.CorrelationId);
    }

    [Fact]
    public void PayrollExportLine_TraceabilityFields_AreOptional()
    {
        var line = new PayrollExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1010",
            Hours = 7.4m,
            Amount = 250.0m,
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24"
        };

        Assert.Null(line.SourceRuleId);
        Assert.Null(line.SourceTimeType);
    }

    [Fact]
    public void PayrollExportLine_TraceabilityFields_WhenSet()
    {
        var line = new PayrollExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1020",
            Hours = 3.0m,
            Amount = 450.0m,
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24",
            SourceRuleId = "OVERTIME_CALC",
            SourceTimeType = "OVERTIME_50"
        };

        Assert.Equal("OVERTIME_CALC", line.SourceRuleId);
        Assert.Equal("OVERTIME_50", line.SourceTimeType);
    }

    [Fact]
    public void PeriodCalculationResult_HoldsRuleResultsAndExportLines()
    {
        var ruleResults = new List<CalculationResult>
        {
            new()
            {
                RuleId = "NORM_CHECK_37H",
                EmployeeId = "EMP001",
                Success = true,
                LineItems = new List<CalculationLineItem>()
            }
        };

        var exportLines = new List<PayrollExportLine>
        {
            new()
            {
                EmployeeId = "EMP001",
                WageType = "1010",
                Hours = 37m,
                Amount = 0m,
                PeriodStart = new DateOnly(2024, 4, 8),
                PeriodEnd = new DateOnly(2024, 4, 14),
                OkVersion = "OK24",
                SourceRuleId = "NORM_CHECK_37H",
                SourceTimeType = "NORMAL_HOURS"
            }
        };

        var result = new PeriodCalculationResult
        {
            EmployeeId = "EMP001",
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            AgreementCode = "HK",
            OkVersion = "OK24",
            RuleResults = ruleResults,
            ExportLines = exportLines,
            Success = true
        };

        Assert.True(result.Success);
        Assert.Single(result.RuleResults);
        Assert.Single(result.ExportLines);
        Assert.Equal("NORM_CHECK_37H", result.ExportLines[0].SourceRuleId);
    }
}
