using System.Text.Json;
using StatsTid.Backend.Api.AuditMappers;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.AuditMappers;

/// <summary>
/// S73 / TASK-7301 (M3) — the audit-projection mappers for the entitlement-config events must
/// project the D-A full-day-only legal flag into their details JSON so a CARE_DAY/SENIOR_DAY
/// full-day-only configuration change is visible in the operational audit projection. The flag
/// is additive + null-tolerant (the payload field is <c>bool?</c>; pre-S73 events carry null,
/// which the shared <c>WhenWritingNull</c> options omit).
/// </summary>
public class S73EntitlementConfigFullDayOnlyAuditMapperTests
{
    private static readonly AuditProjectionContext Context = new(
        ActorId: "ADMIN001",
        ActorPrimaryOrgId: "ORG_A",
        CorrelationId: Guid.NewGuid(),
        OccurredAt: DateTimeOffset.UtcNow);

    // ── EntitlementConfigCreated ─────────────────────────────────────────────────────

    [Fact]
    public void Created_Mapper_ProjectsFullDayOnly_True()
    {
        var mapper = new EntitlementConfigCreatedAuditMapper();
        var @event = new EntitlementConfigCreated
        {
            ConfigId = Guid.NewGuid(),
            EntitlementType = "CARE_DAY",
            AgreementCode = "AC",
            OkVersion = "OK24",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            RowVersion = 1,
            AnnualQuota = 5m,
            AccrualModel = "IMMEDIATE",
            ResetMonth = 1,
            CarryoverMax = 0m,
            ProRateByPartTime = false,
            IsPerEpisode = false,
            FullDayOnly = true,
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.GlobalTenantVisible, row.VisibilityScope);
        using var details = JsonDocument.Parse(row.DetailsJson);
        Assert.True(details.RootElement.GetProperty("fullDayOnly").GetBoolean());
    }

    [Fact]
    public void Created_Mapper_NullFlag_OmittedFromDetails()
    {
        var mapper = new EntitlementConfigCreatedAuditMapper();
        var @event = new EntitlementConfigCreated
        {
            ConfigId = Guid.NewGuid(),
            EntitlementType = "CHILD_SICK",
            AgreementCode = "AC",
            OkVersion = "OK24",
            EffectiveFrom = new DateOnly(2026, 1, 1),
            RowVersion = 1,
            AnnualQuota = 1m,
            AccrualModel = "IMMEDIATE",
            ResetMonth = 1,
            CarryoverMax = 0m,
            ProRateByPartTime = false,
            IsPerEpisode = false,
            FullDayOnly = null, // pre-S73 / non-full-day events
        };

        var row = mapper.Map(@event, Context);

        using var details = JsonDocument.Parse(row.DetailsJson); // valid JSON
        Assert.False(details.RootElement.TryGetProperty("fullDayOnly", out _)); // WhenWritingNull
    }

    [Fact]
    public void Created_Mapper_NullTolerant_OnActivatorInstance()
    {
        var mapper = new EntitlementConfigCreatedAuditMapper();
        var @event = (EntitlementConfigCreated)Activator.CreateInstance(typeof(EntitlementConfigCreated))!;

        var row = mapper.Map(@event, Context); // must not NRE

        using var details = JsonDocument.Parse(row.DetailsJson); // valid JSON
        Assert.False(details.RootElement.TryGetProperty("fullDayOnly", out _));
    }

    // ── EntitlementConfigSuperseded ──────────────────────────────────────────────────

    [Fact]
    public void Superseded_Mapper_ProjectsFullDayOnly_True()
    {
        var mapper = new EntitlementConfigSupersededAuditMapper();
        var @event = new EntitlementConfigSuperseded
        {
            ConfigId = Guid.NewGuid(),
            EntitlementType = "SENIOR_DAY",
            AgreementCode = "AC",
            OkVersion = "OK24",
            EffectiveFrom = new DateOnly(2025, 1, 1),
            EffectiveTo = new DateOnly(2026, 1, 1),
            RowVersion = 2,
            SupersededByConfigId = Guid.NewGuid(),
            FullDayOnly = true,
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.GlobalTenantVisible, row.VisibilityScope);
        using var details = JsonDocument.Parse(row.DetailsJson);
        Assert.True(details.RootElement.GetProperty("fullDayOnly").GetBoolean());
    }

    [Fact]
    public void Superseded_Mapper_NullTolerant_OnActivatorInstance()
    {
        var mapper = new EntitlementConfigSupersededAuditMapper();
        var @event = (EntitlementConfigSuperseded)Activator.CreateInstance(typeof(EntitlementConfigSuperseded))!;

        var row = mapper.Map(@event, Context); // must not NRE

        using var details = JsonDocument.Parse(row.DetailsJson); // valid JSON
        Assert.False(details.RootElement.TryGetProperty("fullDayOnly", out _));
    }
}
