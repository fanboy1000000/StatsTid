using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

public class PayrollMappingTests
{
    [Fact]
    public void NormalHours_MapsTo_SLS0110()
    {
        var mapping = new WageTypeMapping
        {
            TimeType = "NORMAL_HOURS",
            WageType = "SLS_0110",
            OkVersion = "OK24",
            AgreementCode = "AC",
            Description = "Normal working hours"
        };

        Assert.Equal("SLS_0110", mapping.WageType);
        Assert.Equal("NORMAL_HOURS", mapping.TimeType);
    }

    [Fact]
    public void ExportLine_ContainsAllRequiredFields()
    {
        var line = new PayrollExportLine
        {
            EmployeeId = "EMP001",
            WageType = "SLS_0110",
            Hours = 37.0m,
            Amount = 37.0m,
            PeriodStart = new DateOnly(2024, 4, 1),
            PeriodEnd = new DateOnly(2024, 4, 7),
            OkVersion = "OK24"
        };

        Assert.Equal("EMP001", line.EmployeeId);
        Assert.Equal("SLS_0110", line.WageType);
        Assert.Equal(37.0m, line.Hours);
        Assert.Equal("OK24", line.OkVersion);
    }

    [Fact]
    public void MappingLookup_ByTimeTypeAndVersion()
    {
        var mappings = new List<WageTypeMapping>
        {
            new() { TimeType = "NORMAL_HOURS", WageType = "SLS_0110", OkVersion = "OK24", AgreementCode = "AC" },
            new() { TimeType = "OVERTIME_50", WageType = "SLS_0210", OkVersion = "OK24", AgreementCode = "HK" },
            new() { TimeType = "MERARBEJDE", WageType = "SLS_0310", OkVersion = "OK24", AgreementCode = "AC" },
        };

        var result = mappings.FirstOrDefault(m =>
            m.TimeType == "NORMAL_HOURS" && m.OkVersion == "OK24" && m.AgreementCode == "AC");

        Assert.NotNull(result);
        Assert.Equal("SLS_0110", result.WageType);
    }
}
