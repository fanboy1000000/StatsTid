using System.Text.Json;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.AuditMappers;

/// <summary>
/// S70 / ADR-033 slice 3a (TASK-7001, SPRINT-70 R10) — projection-shape tests for the three new
/// ADR-026 audit mappers (the two leaver-lifecycle events + <see cref="TerminationSettled"/>,
/// which S70 begins emitting). Each mapper is pinned on:
/// <list type="bullet">
///   <item>TENANT_TARGETED scope with target_org_id = <c>context.ResolvedTargetOrgId</c>
///         (employee → users.primary_org_id, resolved at the dispatch site per ADR-026 D2)
///         and target_resource_id = employee_id;</item>
///   <item>the details-JSON field set;</item>
///   <item>NULL-TOLERANCE (S66 e0d1dc3 lesson): <c>Map</c> must not NRE on an
///         <c>Activator.CreateInstance</c>-built event whose <c>required</c> reference
///         members were bypassed — the catalog-driven visibility test constructs events
///         exactly that way.</item>
/// </list>
/// </summary>
public class S70TerminationFoundationAuditMapperTests
{
    private static readonly AuditProjectionContext Context = new(
        ActorId: "ADMIN001",
        ActorPrimaryOrgId: "ORG_A",
        CorrelationId: Guid.NewGuid(),
        OccurredAt: DateTimeOffset.UtcNow,
        ResolvedTargetOrgId: "ORG_EMP");

    // ── EmployeeEmploymentEndDateSet ────────────────────────────────────────────────

    [Fact]
    public void EmployeeEmploymentEndDateSet_Mapper_ProjectsTenantTargetedRow()
    {
        var mapper = new EmployeeEmploymentEndDateSetAuditMapper();
        var @event = new EmployeeEmploymentEndDateSet
        {
            EmployeeId = "EMP042",
            OldEndDate = null,
            NewEndDate = new DateOnly(2026, 7, 31),
            OldIsActive = true,
            NewIsActive = true,
            VersionBefore = 4,
            VersionAfter = 5,
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal("ORG_EMP", row.TargetOrgId);
        Assert.Equal("EMP042", row.TargetResourceId);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("EmployeeEmploymentEndDateSet", root.GetProperty("kind").GetString());
        Assert.Equal("EMP042", root.GetProperty("employeeId").GetString());
        // SET transition: oldEndDate null ⇒ omitted under WhenWritingNull; newEndDate present.
        Assert.False(root.TryGetProperty("oldEndDate", out _));
        Assert.Equal("2026-07-31", root.GetProperty("newEndDate").GetString());
        Assert.True(root.GetProperty("oldIsActive").GetBoolean());
        Assert.True(root.GetProperty("newIsActive").GetBoolean());
        Assert.Equal(4, root.GetProperty("versionBefore").GetInt64());
        Assert.Equal(5, root.GetProperty("versionAfter").GetInt64());
    }

    [Fact]
    public void EmployeeEmploymentEndDateSet_Mapper_NullTolerant_OnActivatorInstance()
    {
        var mapper = new EmployeeEmploymentEndDateSetAuditMapper();
        var @event = (EmployeeEmploymentEndDateSet)Activator.CreateInstance(typeof(EmployeeEmploymentEndDateSet))!;

        var row = mapper.Map(@event, Context); // must not NRE

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal(string.Empty, row.TargetResourceId);
        using var details = JsonDocument.Parse(row.DetailsJson); // valid JSON
        Assert.Equal("EmployeeEmploymentEndDateSet", details.RootElement.GetProperty("kind").GetString());
    }

    // ── EmployeeEndDateDeactivationApplied ──────────────────────────────────────────

    [Fact]
    public void EmployeeEndDateDeactivationApplied_Mapper_ProjectsTenantTargetedRow()
    {
        var mapper = new EmployeeEndDateDeactivationAppliedAuditMapper();
        var @event = new EmployeeEndDateDeactivationApplied
        {
            EmployeeId = "EMP042",
            EndDate = new DateOnly(2026, 7, 31),
            OldIsActive = true,
            NewIsActive = false,
            VersionBefore = 5,
            VersionAfter = 6,
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal("ORG_EMP", row.TargetOrgId);
        Assert.Equal("EMP042", row.TargetResourceId);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("EmployeeEndDateDeactivationApplied", root.GetProperty("kind").GetString());
        Assert.Equal("EMP042", root.GetProperty("employeeId").GetString());
        Assert.Equal("2026-07-31", root.GetProperty("endDate").GetString());
        Assert.True(root.GetProperty("oldIsActive").GetBoolean());
        Assert.False(root.GetProperty("newIsActive").GetBoolean());
        Assert.Equal(5, root.GetProperty("versionBefore").GetInt64());
        Assert.Equal(6, root.GetProperty("versionAfter").GetInt64());
    }

    [Fact]
    public void EmployeeEndDateDeactivationApplied_Mapper_NullTolerant_OnActivatorInstance()
    {
        var mapper = new EmployeeEndDateDeactivationAppliedAuditMapper();
        var @event = (EmployeeEndDateDeactivationApplied)Activator.CreateInstance(typeof(EmployeeEndDateDeactivationApplied))!;

        var row = mapper.Map(@event, Context); // must not NRE

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal(string.Empty, row.TargetResourceId);
        using var details = JsonDocument.Parse(row.DetailsJson); // valid JSON
        Assert.Equal("EmployeeEndDateDeactivationApplied", details.RootElement.GetProperty("kind").GetString());
    }

    // ── TerminationSettled ──────────────────────────────────────────────────────────

    [Fact]
    public void TerminationSettled_Mapper_ProjectsTenantTargetedRow()
    {
        var mapper = new TerminationSettledAuditMapper();
        var @event = new TerminationSettled
        {
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            Sequence = 1,
            Snapshot = new VacationSettlementSnapshot
            {
                Earned = 18.75m,
                Used = 10m,
                CarryoverIn = 2.5m,
                OkVersion = "OK24",
            },
            PayoutDays = 11.25m,
            ModregningDays = 0m,
            UnearnedAdvanceDays = 0m,
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal("ORG_EMP", row.TargetOrgId);
        Assert.Equal("EMP042", row.TargetResourceId);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("TerminationSettled", root.GetProperty("kind").GetString());
        Assert.Equal("§26+§7", root.GetProperty("paragraph").GetString());
        Assert.Equal("EMP042", root.GetProperty("employeeId").GetString());
        Assert.Equal("VACATION", root.GetProperty("entitlementType").GetString());
        Assert.Equal(2025, root.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(1, root.GetProperty("sequence").GetInt32());
        Assert.Equal(11.25m, root.GetProperty("payoutDays").GetDecimal());
        Assert.Equal(0m, root.GetProperty("modregningDays").GetDecimal());
        Assert.Equal(0m, root.GetProperty("unearnedAdvanceDays").GetDecimal());
        Assert.Equal(2.5m, root.GetProperty("carryoverIn").GetDecimal());
        Assert.Equal("OK24", root.GetProperty("okVersion").GetString());
    }

    [Fact]
    public void TerminationSettled_Mapper_NullTolerant_OnActivatorInstance()
    {
        var mapper = new TerminationSettledAuditMapper();
        var @event = (TerminationSettled)Activator.CreateInstance(typeof(TerminationSettled))!;

        var row = mapper.Map(@event, Context); // must not NRE (Snapshot null + required refs bypassed)

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal(string.Empty, row.TargetResourceId);
        using var details = JsonDocument.Parse(row.DetailsJson); // valid JSON
        var root = details.RootElement;
        Assert.Equal("TerminationSettled", root.GetProperty("kind").GetString());
        // Snapshot-derived operands are omitted (null) — never thrown on.
        Assert.False(root.TryGetProperty("okVersion", out _));
    }
}
