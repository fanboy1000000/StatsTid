using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.Integrations.Payroll.Services;

namespace StatsTid.Tests.Unit;

/// <summary>
/// Tests for Sprint 5 models: RetroactiveCorrectionRequested event, CorrectionExportLine,
/// SlsExportFormatter, and correction diff logic.
/// </summary>
public class Sprint5ModelTests
{
    // ---------------------------------------------------------------
    // RetroactiveCorrectionRequested event tests
    // ---------------------------------------------------------------

    [Fact]
    public void RetroactiveCorrectionRequested_EventType_IsCorrect()
    {
        var evt = new RetroactiveCorrectionRequested
        {
            EmployeeId = "EMP001",
            OriginalPeriodStart = new DateOnly(2024, 4, 8),
            OriginalPeriodEnd = new DateOnly(2024, 4, 14),
            AgreementCode = "HK",
            OkVersion = "OK24",
            Reason = "Late time entry correction"
        };

        Assert.Equal("RetroactiveCorrectionRequested", evt.EventType);
    }

    [Fact]
    public void RetroactiveCorrectionRequested_ExtendsBase_HasActorTracking()
    {
        var correlationId = Guid.NewGuid();
        var evt = new RetroactiveCorrectionRequested
        {
            EmployeeId = "EMP001",
            OriginalPeriodStart = new DateOnly(2024, 4, 8),
            OriginalPeriodEnd = new DateOnly(2024, 4, 14),
            AgreementCode = "HK",
            OkVersion = "OK24",
            Reason = "Late time entry correction",
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
    public void EventSerializer_RetroactiveCorrectionRequested_RoundTrips()
    {
        var originalEventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var occurredAt = new DateTime(2024, 4, 10, 14, 30, 0, DateTimeKind.Utc);

        var original = new RetroactiveCorrectionRequested
        {
            EventId = originalEventId,
            OccurredAt = occurredAt,
            CorrelationId = correlationId,
            ActorId = "mgr01",
            ActorRole = "Manager",
            EmployeeId = "EMP_RT_002",
            OriginalPeriodStart = new DateOnly(2024, 4, 8),
            OriginalPeriodEnd = new DateOnly(2024, 4, 14),
            AgreementCode = "AC",
            OkVersion = "OK24",
            Reason = "Forgotten overtime entry"
        };

        // Serialize
        var json = EventSerializer.Serialize(original);

        // Deserialize
        var deserialized = EventSerializer.Deserialize("RetroactiveCorrectionRequested", json);

        Assert.IsType<RetroactiveCorrectionRequested>(deserialized);
        var roundTripped = (RetroactiveCorrectionRequested)deserialized;

        Assert.Equal(original.EventId, roundTripped.EventId);
        Assert.Equal(original.EmployeeId, roundTripped.EmployeeId);
        Assert.Equal(original.OriginalPeriodStart, roundTripped.OriginalPeriodStart);
        Assert.Equal(original.OriginalPeriodEnd, roundTripped.OriginalPeriodEnd);
        Assert.Equal(original.AgreementCode, roundTripped.AgreementCode);
        Assert.Equal(original.OkVersion, roundTripped.OkVersion);
        Assert.Equal(original.Reason, roundTripped.Reason);
        Assert.Equal(original.ActorId, roundTripped.ActorId);
        Assert.Equal(original.ActorRole, roundTripped.ActorRole);
        Assert.Equal(original.CorrelationId, roundTripped.CorrelationId);
    }

    // ---------------------------------------------------------------
    // CorrectionExportLine model tests
    // ---------------------------------------------------------------

    [Fact]
    public void CorrectionExportLine_AllRequiredFieldsSetCorrectly()
    {
        var line = new CorrectionExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1020",
            OriginalHours = 3.0m,
            CorrectedHours = 5.0m,
            DifferenceHours = 2.0m,
            OriginalAmount = 450.0m,
            CorrectedAmount = 750.0m,
            DifferenceAmount = 300.0m,
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24"
        };

        Assert.Equal("EMP001", line.EmployeeId);
        Assert.Equal("1020", line.WageType);
        Assert.Equal(3.0m, line.OriginalHours);
        Assert.Equal(5.0m, line.CorrectedHours);
        Assert.Equal(2.0m, line.DifferenceHours);
        Assert.Equal(450.0m, line.OriginalAmount);
        Assert.Equal(750.0m, line.CorrectedAmount);
        Assert.Equal(300.0m, line.DifferenceAmount);
    }

    [Fact]
    public void CorrectionExportLine_TraceabilityFields_AreOptional()
    {
        var line = new CorrectionExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1010",
            OriginalHours = 37.0m,
            CorrectedHours = 37.0m,
            DifferenceHours = 0m,
            OriginalAmount = 0m,
            CorrectedAmount = 0m,
            DifferenceAmount = 0m,
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24"
        };

        Assert.Null(line.SourceRuleId);
        Assert.Null(line.SourceTimeType);
    }

    [Fact]
    public void CorrectionExportLine_DifferenceCalculation_Correct()
    {
        // Simulate: original was 3h overtime at 1.5x, corrected to 5h overtime at 1.5x
        var originalHours = 3.0m;
        var correctedHours = 5.0m;
        var rate = 1.5m;
        var originalAmount = originalHours * rate;
        var correctedAmount = correctedHours * rate;

        var line = new CorrectionExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1020",
            OriginalHours = originalHours,
            CorrectedHours = correctedHours,
            DifferenceHours = correctedHours - originalHours,
            OriginalAmount = originalAmount,
            CorrectedAmount = correctedAmount,
            DifferenceAmount = correctedAmount - originalAmount,
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24",
            SourceRuleId = "OVERTIME_CALC",
            SourceTimeType = "OVERTIME_50"
        };

        Assert.Equal(2.0m, line.DifferenceHours);
        Assert.Equal(3.0m, line.DifferenceAmount);
        Assert.Equal("OVERTIME_CALC", line.SourceRuleId);
        Assert.Equal("OVERTIME_50", line.SourceTimeType);
    }

    // ---------------------------------------------------------------
    // SlsExportFormatter tests
    // ---------------------------------------------------------------

    [Fact]
    public void SlsExportFormatter_SingleLine_FormatsCorrectly()
    {
        var lines = new List<PayrollExportLine>
        {
            new()
            {
                EmployeeId = "EMP001",
                WageType = "1010",
                Hours = 37.00m,
                Amount = 250.00m,
                PeriodStart = new DateOnly(2024, 4, 8),
                PeriodEnd = new DateOnly(2024, 4, 14),
                OkVersion = "OK24"
            }
        };

        var exportId = "EXP-001";
        var timestamp = new DateTime(2024, 4, 15, 10, 0, 0, DateTimeKind.Utc);

        var output = SlsExportFormatter.Format(lines, exportId, timestamp);
        var outputLines = output.Split(Environment.NewLine);

        // Header: H|exportId|timestamp|count
        Assert.StartsWith("H|EXP-001|2024-04-15 10:00:00|1", outputLines[0]);
        // Data: D|employeeId|wageType|hours|amount|periodStart|periodEnd|okVersion
        Assert.StartsWith("D|EMP001|1010|37.00|250.00|20240408|20240414|OK24", outputLines[1]);
        // Trailer: T|count|totalHours|totalAmount|checksum
        Assert.StartsWith("T|1|37.00|250.00|", outputLines[2]);
    }

    [Fact]
    public void SlsExportFormatter_MultipleLines_CorrectTotals()
    {
        var lines = new List<PayrollExportLine>
        {
            new()
            {
                EmployeeId = "EMP001",
                WageType = "1010",
                Hours = 37.00m,
                Amount = 250.00m,
                PeriodStart = new DateOnly(2024, 4, 8),
                PeriodEnd = new DateOnly(2024, 4, 14),
                OkVersion = "OK24"
            },
            new()
            {
                EmployeeId = "EMP001",
                WageType = "1020",
                Hours = 3.00m,
                Amount = 450.00m,
                PeriodStart = new DateOnly(2024, 4, 8),
                PeriodEnd = new DateOnly(2024, 4, 14),
                OkVersion = "OK24"
            }
        };

        var exportId = "EXP-002";
        var timestamp = new DateTime(2024, 4, 15, 12, 0, 0, DateTimeKind.Utc);

        var output = SlsExportFormatter.Format(lines, exportId, timestamp);
        var outputLines = output.Split(Environment.NewLine);

        // Header should have count = 2
        Assert.StartsWith("H|EXP-002|2024-04-15 12:00:00|2", outputLines[0]);
        // Two D records
        Assert.StartsWith("D|EMP001|1010|", outputLines[1]);
        Assert.StartsWith("D|EMP001|1020|", outputLines[2]);
        // Trailer: total hours = 40, total amount = 700
        Assert.StartsWith("T|2|40.00|700.00|", outputLines[3]);
    }

    [Fact]
    public void SlsExportFormatter_EmptyLines_HeaderAndTrailerOnly()
    {
        var lines = new List<PayrollExportLine>();

        var exportId = "EXP-003";
        var timestamp = new DateTime(2024, 4, 15, 14, 0, 0, DateTimeKind.Utc);

        var output = SlsExportFormatter.Format(lines, exportId, timestamp);
        var outputLines = output.Split(Environment.NewLine);

        // Header: count = 0
        Assert.StartsWith("H|EXP-003|2024-04-15 14:00:00|0", outputLines[0]);
        // Trailer: no D records, zeros
        Assert.StartsWith("T|0|0.00|0.00|0", outputLines[1]);
    }

    [Fact]
    public void SlsExportFormatter_Checksum_IsDeterministic()
    {
        var lines = new List<PayrollExportLine>
        {
            new()
            {
                EmployeeId = "EMP001",
                WageType = "1010",
                Hours = 37.00m,
                Amount = 250.00m,
                PeriodStart = new DateOnly(2024, 4, 8),
                PeriodEnd = new DateOnly(2024, 4, 14),
                OkVersion = "OK24"
            },
            new()
            {
                EmployeeId = "EMP002",
                WageType = "1020",
                Hours = 5.00m,
                Amount = 750.00m,
                PeriodStart = new DateOnly(2024, 4, 8),
                PeriodEnd = new DateOnly(2024, 4, 14),
                OkVersion = "OK24"
            }
        };

        var exportId = "EXP-DET";
        var timestamp = new DateTime(2024, 4, 15, 10, 0, 0, DateTimeKind.Utc);

        var output1 = SlsExportFormatter.Format(lines, exportId, timestamp);
        var output2 = SlsExportFormatter.Format(lines, exportId, timestamp);

        Assert.Equal(output1, output2);
    }

    [Fact]
    public void SlsExportFormatter_HeaderContainsTimestampAndExportId()
    {
        var lines = new List<PayrollExportLine>
        {
            new()
            {
                EmployeeId = "EMP001",
                WageType = "1010",
                Hours = 10.00m,
                Amount = 100.00m,
                PeriodStart = new DateOnly(2024, 5, 1),
                PeriodEnd = new DateOnly(2024, 5, 7),
                OkVersion = "OK24"
            }
        };

        var exportId = "MY-EXPORT-ID";
        var timestamp = new DateTime(2024, 5, 8, 9, 30, 45, DateTimeKind.Utc);

        var output = SlsExportFormatter.Format(lines, exportId, timestamp);
        var headerLine = output.Split(Environment.NewLine)[0];

        Assert.Contains("MY-EXPORT-ID", headerLine);
        Assert.Contains("2024-05-08 09:30:45", headerLine);
    }

    // ---------------------------------------------------------------
    // Correction diff logic tests
    // ---------------------------------------------------------------

    [Fact]
    public void CorrectionExportLine_ZeroDiff_NoCorrectionNeeded()
    {
        // Simulate a scenario where original and corrected are identical
        var originalLine = new PayrollExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1010",
            Hours = 37.0m,
            Amount = 250.0m,
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24",
            SourceRuleId = "NORM_CHECK_37H",
            SourceTimeType = "NORMAL_HOURS"
        };

        var correctedLine = new PayrollExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1010",
            Hours = 37.0m,
            Amount = 250.0m,
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24",
            SourceRuleId = "NORM_CHECK_37H",
            SourceTimeType = "NORMAL_HOURS"
        };

        var diffHours = correctedLine.Hours - originalLine.Hours;
        var diffAmount = correctedLine.Amount - originalLine.Amount;

        Assert.Equal(0m, diffHours);
        Assert.Equal(0m, diffAmount);
    }

    [Fact]
    public void CorrectionExportLine_PositiveDiff_AdditionalHours()
    {
        // Simulate: employee had originally 3h overtime, corrected to 5h
        var originalLine = new PayrollExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1020",
            Hours = 3.0m,
            Amount = 4.5m, // 3 * 1.5
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24",
            SourceRuleId = "OVERTIME_CALC",
            SourceTimeType = "OVERTIME_50"
        };

        var correctedLine = new PayrollExportLine
        {
            EmployeeId = "EMP001",
            WageType = "1020",
            Hours = 5.0m,
            Amount = 7.5m, // 5 * 1.5
            PeriodStart = new DateOnly(2024, 4, 8),
            PeriodEnd = new DateOnly(2024, 4, 14),
            OkVersion = "OK24",
            SourceRuleId = "OVERTIME_CALC",
            SourceTimeType = "OVERTIME_50"
        };

        // Build CorrectionExportLine from the diff
        var correction = new CorrectionExportLine
        {
            EmployeeId = originalLine.EmployeeId,
            WageType = originalLine.WageType,
            OriginalHours = originalLine.Hours,
            CorrectedHours = correctedLine.Hours,
            DifferenceHours = correctedLine.Hours - originalLine.Hours,
            OriginalAmount = originalLine.Amount,
            CorrectedAmount = correctedLine.Amount,
            DifferenceAmount = correctedLine.Amount - originalLine.Amount,
            PeriodStart = originalLine.PeriodStart,
            PeriodEnd = originalLine.PeriodEnd,
            OkVersion = originalLine.OkVersion,
            SourceRuleId = originalLine.SourceRuleId,
            SourceTimeType = originalLine.SourceTimeType
        };

        Assert.Equal(2.0m, correction.DifferenceHours);
        Assert.Equal(3.0m, correction.DifferenceAmount);
        Assert.Equal("OVERTIME_CALC", correction.SourceRuleId);
        Assert.Equal("OVERTIME_50", correction.SourceTimeType);
    }
}
