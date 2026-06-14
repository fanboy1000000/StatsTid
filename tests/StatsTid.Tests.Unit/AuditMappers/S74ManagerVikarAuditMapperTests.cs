using System.Text.Json;
using StatsTid.Infrastructure.AuditMappers;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.AuditMappers;

/// <summary>
/// S74 / ADR-027 Phase 5 (TASK-7401, SPRINT-74 R4) — projection-shape tests for the two
/// new manager_vikar ADR-026 audit mappers (<see cref="ManagerVikarCreated"/> /
/// <see cref="ManagerVikarEnded"/>). Each mapper is pinned on:
/// <list type="bullet">
///   <item>TENANT_TARGETED scope with target_org_id = the event's <c>tree_root_org_id</c>
///         (carried on the event — mirrors the S48 ReportingLineAssigned mapper, no
///         context lookup needed) and target_resource_id = vikar_id;</item>
///   <item>the details-JSON field set;</item>
///   <item>NULL-TOLERANCE (S66 e0d1dc3 lesson): <c>Map</c> must not NRE on an
///         <c>Activator.CreateInstance</c>-built event whose <c>required</c> reference
///         members were bypassed — the catalog-driven visibility test constructs events
///         exactly that way.</item>
/// </list>
/// </summary>
public class S74ManagerVikarAuditMapperTests
{
    private static readonly AuditProjectionContext Context = new(
        ActorId: "mgr01",
        ActorPrimaryOrgId: "ORG_A",
        CorrelationId: Guid.NewGuid(),
        OccurredAt: DateTimeOffset.UtcNow,
        ResolvedTargetOrgId: "ORG_FALLBACK");

    // ── ManagerVikarCreated ─────────────────────────────────────────────────────────

    [Fact]
    public void ManagerVikarCreated_Mapper_ProjectsTenantTargetedRow_FromEventTreeRoot()
    {
        var mapper = new ManagerVikarCreatedAuditMapper();
        var vikarId = Guid.NewGuid();
        var @event = new ManagerVikarCreated
        {
            VikarId = vikarId,
            AbsentApproverId = "mgr01",
            VikarUserId = "mgr02",
            UntilDate = new DateOnly(2026, 7, 1),
            Reason = "ANDET",
            TreeRootOrgId = "STY02",
            RowVersion = 1,
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal("STY02", row.TargetOrgId); // from the event, NOT the context fallback
        Assert.Equal(vikarId.ToString(), row.TargetResourceId);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("ManagerVikarCreated", root.GetProperty("kind").GetString());
        Assert.Equal("mgr01", root.GetProperty("absentApproverId").GetString());
        Assert.Equal("mgr02", root.GetProperty("vikarUserId").GetString());
        Assert.Equal("2026-07-01", root.GetProperty("untilDate").GetString());
        Assert.Equal("ANDET", root.GetProperty("reason").GetString());
        Assert.Equal("STY02", root.GetProperty("treeRootOrgId").GetString());
    }

    [Fact]
    public void ManagerVikarCreated_Mapper_NullTolerant_OnActivatorInstance()
    {
        var mapper = new ManagerVikarCreatedAuditMapper();
        var @event = (ManagerVikarCreated)Activator.CreateInstance(typeof(ManagerVikarCreated))!;

        var row = mapper.Map(@event, Context); // must not NRE

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        // Null event tree-root falls back to the context-resolved org.
        Assert.Equal("ORG_FALLBACK", row.TargetOrgId);
        using var details = JsonDocument.Parse(row.DetailsJson); // valid JSON
        Assert.Equal("ManagerVikarCreated", details.RootElement.GetProperty("kind").GetString());
    }

    // ── ManagerVikarEnded ───────────────────────────────────────────────────────────

    [Fact]
    public void ManagerVikarEnded_Mapper_ProjectsTenantTargetedRow_WithEndReason()
    {
        var mapper = new ManagerVikarEndedAuditMapper();
        var vikarId = Guid.NewGuid();
        var @event = new ManagerVikarEnded
        {
            VikarId = vikarId,
            AbsentApproverId = "mgr01",
            VikarUserId = "mgr02",
            UntilDate = new DateOnly(2026, 7, 1),
            Reason = "ANDET",
            TreeRootOrgId = "STY02",
            EffectiveTo = new DateOnly(2026, 7, 2),
            EndReason = "REVOKED",
            RowVersion = 2,
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal("STY02", row.TargetOrgId);
        Assert.Equal(vikarId.ToString(), row.TargetResourceId);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("ManagerVikarEnded", root.GetProperty("kind").GetString());
        Assert.Equal("2026-07-02", root.GetProperty("effectiveTo").GetString());
        Assert.Equal("REVOKED", root.GetProperty("endReason").GetString());
    }

    [Fact]
    public void ManagerVikarEnded_Mapper_NullTolerant_OnActivatorInstance()
    {
        var mapper = new ManagerVikarEndedAuditMapper();
        var @event = (ManagerVikarEnded)Activator.CreateInstance(typeof(ManagerVikarEnded))!;

        var row = mapper.Map(@event, Context); // must not NRE

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal("ORG_FALLBACK", row.TargetOrgId);
        using var details = JsonDocument.Parse(row.DetailsJson);
        Assert.Equal("ManagerVikarEnded", details.RootElement.GetProperty("kind").GetString());
    }
}
