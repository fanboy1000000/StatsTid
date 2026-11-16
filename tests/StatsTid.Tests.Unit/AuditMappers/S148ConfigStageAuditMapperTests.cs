using System.Text.Json;
using StatsTid.Backend.Api.AuditMappers;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.AuditMappers;

/// <summary>
/// S148 / TASK-14815-T (ruling A6) — the five audit mappers of the config-stage events. In plain terms: each new
/// event becomes one audit-screen row, and the row names the organisation whose administrators may see it; a wrong
/// organisation would show the row to the wrong institution. One fact per mapper asserts scope, target organisation,
/// resource id and the details keys (ADR-026 D2). The context's organisation is STY09, one no payload member and no
/// actor carries; the customary-day fact sets the payload's (STY03) differently and expects the payload's: a customary
/// day is keyed by organisation, not by the acting user.
///
/// <para><b>Kind N</b> for every fact (the mappers are new in S148). Red evidence is the mutations M-38 and M-38b, not
/// a revert: every new mapper returns a null target organisation (M-38), or reads it from the wrong place (M-38b); each
/// fact's target-organisation assertion then fails.
/// Red conditions: the target organisation is not the context's resolved one (the payload's for customary days); the
/// target organisation is read from the payload or from the actor's organisation instead of the context (M-38b).</para>
/// </summary>
public class S148ConfigStageAuditMapperTests
{
    private static readonly AuditProjectionContext Ctx = new(
        ActorId: "HR001",
        ActorPrimaryOrgId: "ORG_HR",
        CorrelationId: Guid.Empty,
        OccurredAt: DateTimeOffset.UnixEpoch,
        ResolvedTargetOrgId: "STY09");

    private static readonly Guid A1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid A2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static string[] Keys(string detailsJson)
    {
        using var doc = JsonDocument.Parse(detailsJson);
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
    }

    [Fact]
    public void UserOrganisationSeeded_Mapper_TenantTargeted_ContextOrg_UserResource()
    {
        var row = new UserOrganisationSeededAuditMapper().Map(new UserOrganisationSeeded
        {
            UserId = "EMP001", AssignmentId = A1, OrgId = "STY02",
            EffectiveFrom = new DateOnly(2026, 6, 15), RowVersion = 1,
        }, Ctx);

        Assert.Multiple(
            () => Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope),
            () => Assert.Equal("STY09", row.TargetOrgId),
            () => Assert.Equal("EMP001", row.TargetResourceId),
            () => Assert.Equal(new[] { "userId", "assignmentId", "orgId", "effectiveFrom", "rowVersion" }, Keys(row.DetailsJson)));
    }

    [Fact]
    public void UserOrganisationTransferred_Mapper_TenantTargeted_ContextOrg_UserResource()
    {
        var row = new UserOrganisationTransferredAuditMapper().Map(new UserOrganisationTransferred
        {
            UserId = "EMP001", AssignmentId = A2, PredecessorAssignmentId = A1,
            OldOrgId = "STY02", NewOrgId = "STY03",
            EffectiveFrom = new DateOnly(2026, 6, 15), InPlace = false, VersionBefore = 1, VersionAfter = 2,
        }, Ctx);

        Assert.Multiple(
            () => Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope),
            () => Assert.Equal("STY09", row.TargetOrgId),
            () => Assert.Equal("EMP001", row.TargetResourceId),
            () => Assert.Equal(new[]
                { "userId", "assignmentId", "predecessorAssignmentId", "oldOrgId", "newOrgId", "effectiveFrom", "inPlace", "versionBefore", "versionAfter" },
                Keys(row.DetailsJson)));
    }

    [Fact]
    public void EmployeeEmploymentStartDateSet_Mapper_TenantTargeted_ContextOrg_EmployeeResource()
    {
        var row = new EmployeeEmploymentStartDateSetAuditMapper().Map(new EmployeeEmploymentStartDateSet
        {
            EmployeeId = "EMP001", OldStartDate = new DateOnly(2026, 3, 15), NewStartDate = new DateOnly(2026, 3, 2),
            VersionBefore = 6, VersionAfter = 7,
        }, Ctx);

        Assert.Multiple(
            () => Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope),
            () => Assert.Equal("STY09", row.TargetOrgId),
            () => Assert.Equal("EMP001", row.TargetResourceId),
            () => Assert.Equal(new[] { "employeeId", "oldStartDate", "newStartDate", "versionBefore", "versionAfter" }, Keys(row.DetailsJson)));
    }

    [Fact]
    public void CustomaryDayChanged_Mapper_TenantTargeted_PayloadOrgNotContextOrg_CustomResource()
    {
        var row = new CustomaryDayChangedAuditMapper().Map(new CustomaryDayChanged
        {
            CustomId = Guid.Parse("c0c0c0c0-0000-0000-0000-000000000001"), OrgId = "STY03", Action = "CREATED",
            AgreementCode = "AC", RecurringMonth = 6, RecurringDay = 5,
            // FROM_TIME with PartHours is refused by `customary_days_part_hours_check`; filled here only to pin every details key. Not an example row.
            Kind = "PAID_DAY_OFF", Part = "FROM_TIME", PartFromTime = new TimeOnly(12, 0), PartHours = 3.5m,
            PaidMode = "GIVEN_TO_ALL", ValidFrom = new DateOnly(2026, 1, 5),
            VersionBefore = 1, VersionAfter = 2,
        }, Ctx); // the context says STY09; the payload says STY03 and must win

        Assert.Multiple(
            () => Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope),
            () => Assert.Equal("STY03", row.TargetOrgId),
            () => Assert.Equal("c0c0c0c0-0000-0000-0000-000000000001", row.TargetResourceId),
            () => Assert.Equal(new[]
                { "customId", "orgId", "action", "agreementCode", "recurringMonth", "recurringDay", "kind", "part",
                  "partFromTime", "partHours", "paidMode", "validFrom", "versionBefore", "versionAfter" },
                Keys(row.DetailsJson)));
    }

    [Fact]
    public void EmployeeHrFactsChanged_Mapper_TenantTargeted_ContextOrg_EmployeeResource()
    {
        var row = new EmployeeHrFactsChangedAuditMapper().Map(new EmployeeHrFactsChanged
        {
            EmployeeId = "EMP001", FactsId = Guid.Parse("f0f0f0f0-0000-0000-0000-000000000001"), Action = "CREATED",
            OvertimeRegime = "OVERTIME", OnFlexScheme = true,
            EffectiveFrom = new DateOnly(1, 1, 1), EffectiveTo = new DateOnly(2026, 7, 1),
            PrecedingFactsId = A1, VersionBefore = 1, VersionAfter = 2,
        }, Ctx);

        Assert.Multiple(
            () => Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope),
            () => Assert.Equal("STY09", row.TargetOrgId),
            () => Assert.Equal("EMP001", row.TargetResourceId),
            () => Assert.Equal(new[]
                { "employeeId", "factsId", "action", "overtimeRegime", "onFlexScheme", "effectiveFrom", "effectiveTo",
                  "precedingFactsId", "versionBefore", "versionAfter" }, Keys(row.DetailsJson)));
    }
}
