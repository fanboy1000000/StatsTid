using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// Tests for EntitlementBalance model (Sprint 15).
/// Verifies the Remaining computed property under various scenarios.
/// </summary>
public class EntitlementBalanceTests
{
    private static EntitlementBalance CreateBalance(
        decimal totalQuota = 25m,
        decimal used = 0m,
        decimal planned = 0m,
        decimal carryoverIn = 0m) => new()
    {
        BalanceId = Guid.NewGuid(),
        EmployeeId = "EMP001",
        EntitlementType = "VACATION",
        EntitlementYear = 2025,
        TotalQuota = totalQuota,
        Used = used,
        Planned = planned,
        CarryoverIn = carryoverIn,
    };

    [Fact]
    public void Remaining_CalculatedCorrectly()
    {
        var balance = CreateBalance(totalQuota: 25m, carryoverIn: 3m, used: 10m, planned: 5m);

        Assert.Equal(13m, balance.Remaining); // 25 + 3 - 10 - 5 = 13
    }

    [Fact]
    public void Remaining_CanBeNegative()
    {
        var balance = CreateBalance(totalQuota: 25m, used: 30m);

        Assert.Equal(-5m, balance.Remaining); // 25 + 0 - 30 - 0 = -5
    }

    [Fact]
    public void Remaining_AllZeros_ReturnsZero()
    {
        var balance = CreateBalance(totalQuota: 0m, used: 0m, planned: 0m, carryoverIn: 0m);

        Assert.Equal(0m, balance.Remaining);
    }

    [Fact]
    public void Remaining_CarryoverAddsToAvailable()
    {
        var balance = CreateBalance(totalQuota: 25m, carryoverIn: 5m, used: 0m);

        Assert.Equal(30m, balance.Remaining); // 25 + 5 - 0 - 0 = 30
    }

    [Fact]
    public void Remaining_DefaultUsedAndPlanned_EqualsQuota()
    {
        var balance = CreateBalance(totalQuota: 25m);

        Assert.Equal(25m, balance.Remaining); // 25 + 0 - 0 - 0 = 25
    }

    [Fact]
    public void Remaining_PlannedReducesAvailable()
    {
        var balance = CreateBalance(totalQuota: 25m, planned: 10m);

        Assert.Equal(15m, balance.Remaining); // 25 + 0 - 0 - 10 = 15
    }

    [Fact]
    public void Remaining_UsedReducesAvailable()
    {
        var balance = CreateBalance(totalQuota: 25m, used: 15m);

        Assert.Equal(10m, balance.Remaining); // 25 + 0 - 15 - 0 = 10
    }
}
