using System.Text.Json;
using StatsTid.Backend.Api.AuditMappers;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.Worklist;

/// <summary>
/// S138 / TASK-13803 — projection-shape pins for the two ADR-026 audit mappers of the HR backdate
/// worklist. Each is pinned on: TENANT_TARGETED with target_org_id from the CONTEXT (the employee's
/// home Organisation, resolved in-tx by the repository) and target_resource_id = employee_id; the
/// details field set incl. the <c>action</c> discriminator; and NULL-TOLERANCE (the S66 e0d1dc3
/// lesson — the catalog-driven visibility test builds events via <c>Activator.CreateInstance</c>,
/// bypassing <c>required</c>).
/// </summary>
public sealed class BackdateWorklistAuditMapperTests
{
    private static readonly AuditProjectionContext Context = new(
        ActorId: "hr01",
        ActorPrimaryOrgId: null,
        CorrelationId: Guid.NewGuid(),
        OccurredAt: DateTimeOffset.UtcNow,
        ResolvedTargetOrgId: "STY_TARGET");

    [Fact]
    public void RowCreated_NewRow_ProjectsTenantTargeted_EmployeeResource_CreatedAction()
    {
        var mapper = new BackdateWorklistRowCreatedAuditMapper();
        var worklistId = Guid.NewGuid();
        var exportId = Guid.NewGuid();
        var triggerEventId = Guid.NewGuid();
        var @event = new BackdateWorklistRowCreated
        {
            WorklistId = worklistId,
            EmployeeId = "emp1",
            Kind = "EXPORTED_MONTH",
            Year = 2026,
            Month = 3,
            ExportId = exportId,
            TriggerKind = "PROFILE_CHANGE",
            TriggerEventId = triggerEventId,
            TriggerEffectiveFrom = new DateOnly(2026, 3, 15),
            BaselineContentHash = "h1",
            Appended = false,
            RowVersion = 1,
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal("STY_TARGET", row.TargetOrgId);
        Assert.Equal("emp1", row.TargetResourceId);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("WORKLIST_ROW_CREATED", root.GetProperty("action").GetString());
        Assert.Equal(worklistId, root.GetProperty("worklistId").GetGuid());
        Assert.Equal("emp1", root.GetProperty("employeeId").GetString());
        Assert.Equal("EXPORTED_MONTH", root.GetProperty("rowKind").GetString());
        Assert.Equal(2026, root.GetProperty("year").GetInt32());
        Assert.Equal(3, root.GetProperty("month").GetInt32());
        Assert.Equal(exportId, root.GetProperty("exportId").GetGuid());
        Assert.Equal("PROFILE_CHANGE", root.GetProperty("triggerKind").GetString());
        Assert.Equal(triggerEventId, root.GetProperty("triggerEventId").GetGuid());
        Assert.Equal("h1", root.GetProperty("baselineContentHash").GetString());
        Assert.False(root.GetProperty("appended").GetBoolean());
        Assert.Equal(1L, root.GetProperty("rowVersion").GetInt64());
    }

    [Fact]
    public void RowCreated_AppendedTrigger_SettledYear_AppendedAction_SettlementBaseline()
    {
        var mapper = new BackdateWorklistRowCreatedAuditMapper();
        var @event = new BackdateWorklistRowCreated
        {
            WorklistId = Guid.NewGuid(),
            EmployeeId = "emp1",
            Kind = "SETTLED_YEAR",
            EntitlementType = "SPECIAL_HOLIDAY",
            EntitlementYear = 2024,
            TriggerKind = "AGREEMENT_CODE_CHANGE",
            TriggerEventId = Guid.NewGuid(),
            TriggerEffectiveFrom = new DateOnly(2026, 2, 1),
            BaselineSettlementSequence = 2,
            BaselineSettlementState = "SETTLED",
            Appended = true,
            RowVersion = 2,
        };

        var row = mapper.Map(@event, Context);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("WORKLIST_TRIGGER_APPENDED", root.GetProperty("action").GetString());
        Assert.Equal("SETTLED_YEAR", root.GetProperty("rowKind").GetString());
        Assert.Equal("SPECIAL_HOLIDAY", root.GetProperty("entitlementType").GetString());
        Assert.Equal(2024, root.GetProperty("entitlementYear").GetInt32());
        Assert.Equal(2, root.GetProperty("baselineSettlementSequence").GetInt32());
        Assert.Equal("SETTLED", root.GetProperty("baselineSettlementState").GetString());
        Assert.True(root.GetProperty("appended").GetBoolean());
        Assert.Equal(2L, root.GetProperty("rowVersion").GetInt64());
    }

    [Fact]
    public void RowResolved_ProjectsTenantTargeted_EmployeeResource_ResolvedActionWithVersionTransition()
    {
        var mapper = new BackdateWorklistRowResolvedAuditMapper();
        var worklistId = Guid.NewGuid();
        var @event = new BackdateWorklistRowResolved
        {
            WorklistId = worklistId,
            EmployeeId = "emp1",
            Kind = "EXPORTED_MONTH",
            Year = 2026,
            Month = 3,
            ExportId = Guid.NewGuid(),
            Resolution = "RECALCULATED",
            Reason = "Re-planned",
            TriggerCount = 2,
            VersionBefore = 2,
            VersionAfter = 3,
        };

        var row = mapper.Map(@event, Context);

        Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
        Assert.Equal("STY_TARGET", row.TargetOrgId);
        Assert.Equal("emp1", row.TargetResourceId);

        using var details = JsonDocument.Parse(row.DetailsJson);
        var root = details.RootElement;
        Assert.Equal("WORKLIST_ROW_RESOLVED", root.GetProperty("action").GetString());
        Assert.Equal(worklistId, root.GetProperty("worklistId").GetGuid());
        Assert.Equal("RECALCULATED", root.GetProperty("resolution").GetString());
        Assert.Equal("Re-planned", root.GetProperty("reason").GetString());
        Assert.Equal(2, root.GetProperty("triggerCount").GetInt32());
        Assert.Equal(2L, root.GetProperty("versionBefore").GetInt64());
        Assert.Equal(3L, root.GetProperty("versionAfter").GetInt64());
    }

    [Fact]
    public void BothMappers_NullTolerant_OnActivatorBuiltEvents()
    {
        var created = (BackdateWorklistRowCreated)Activator.CreateInstance(typeof(BackdateWorklistRowCreated))!;
        var resolved = (BackdateWorklistRowResolved)Activator.CreateInstance(typeof(BackdateWorklistRowResolved))!;

        var createdRow = new BackdateWorklistRowCreatedAuditMapper().Map(created, Context);
        var resolvedRow = new BackdateWorklistRowResolvedAuditMapper().Map(resolved, Context);

        Assert.Equal(AuditVisibilityScope.TenantTargeted, createdRow.VisibilityScope);
        Assert.Equal(AuditVisibilityScope.TenantTargeted, resolvedRow.VisibilityScope);
        Assert.Null(createdRow.TargetResourceId);
        Assert.Null(resolvedRow.TargetResourceId);
        Assert.Equal("STY_TARGET", createdRow.TargetOrgId);
        Assert.Equal("STY_TARGET", resolvedRow.TargetOrgId);
        using var createdDetails = JsonDocument.Parse(createdRow.DetailsJson);
        using var resolvedDetails = JsonDocument.Parse(resolvedRow.DetailsJson);
        Assert.Equal(JsonValueKind.Object, createdDetails.RootElement.ValueKind);
        Assert.Equal(JsonValueKind.Object, resolvedDetails.RootElement.ValueKind);
    }
}
