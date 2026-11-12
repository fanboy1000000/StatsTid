using StatsTid.Backend.Api.AuditMappers;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.AuditMappers;

/// <summary>
/// S148 / TASK-14840-T — AC-CL-26, the audit mappers of the three cell-clear events (<see cref="TimeEntryCleared"/>,
/// <see cref="AbsenceCleared"/>, <see cref="WorkTimeCleared"/>). In plain terms: each removal writes one audit row, and
/// the row's details are what HR reads to see who removed what and which registration it replaced. The details' bytes are
/// pinned against a golden string per event, in the catalog's shape and order
/// (<c>{employeeId, date, taskId | absenceType, clearedEventId, clearedHours[, clearedFeriedage]}</c>), written from the
/// addendum's catalog and the audit serializer's known format (camelCase, compact, <c>DateOnly</c> as <c>yyyy-MM-dd</c>,
/// a lowercase hyphenated <c>Guid</c>, a decimal without forced trailing zeros, a null member omitted): never copied from
/// the mapper's output.
///
/// <para><b>Kind N</b> (the mappers are new in S148). Red evidence is the mutation M-17, not a revert: in
/// <c>AbsenceClearedAuditMapper</c> the <c>clearedEventId</c> member is dropped from the details; the first golden
/// comparison (the absence's) then fails on the missing member. The sibling check that the three events round-trip through
/// the serializer is <c>EventSerializerCoverageTests.EventSerializer_RegistersAllDomainEventBaseDescendants</c>, kept.</para>
/// </summary>
public class SkemaCellClearedAuditMapperTests
{
    private static readonly AuditProjectionContext Ctx = new(
        ActorId: "HR001",
        ActorPrimaryOrgId: "ORG_HR",
        CorrelationId: Guid.Empty,
        OccurredAt: DateTimeOffset.UnixEpoch,
        ResolvedTargetOrgId: "STY01");

    private static readonly Guid Registration = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void EachMapper_DetailsShape_IsByteStable()
    {
        var absence = new AbsenceClearedAuditMapper().Map(new AbsenceCleared
        {
            EmployeeId = "EMP1",
            Date = new DateOnly(2026, 6, 12),
            AbsenceType = "VACATION",
            ClearedEventId = Registration,
            ClearedHours = 7.4m,
            ClearedFeriedage = 0.5m,
        }, Ctx);
        var absenceNoFeriedage = new AbsenceClearedAuditMapper().Map(new AbsenceCleared
        {
            EmployeeId = "EMP1",
            Date = new DateOnly(2026, 6, 8),
            AbsenceType = "SICK_DAY",
            ClearedEventId = Registration,
            ClearedHours = 7.4m,
        }, Ctx);
        var entry = new TimeEntryClearedAuditMapper().Map(new TimeEntryCleared
        {
            EmployeeId = "EMP1",
            Date = new DateOnly(2026, 6, 9),
            TaskId = "PRJ-1",
            ClearedEventId = Registration,
            ClearedHours = 3.5m,
        }, Ctx);
        var work = new WorkTimeClearedAuditMapper().Map(new WorkTimeCleared
        {
            EmployeeId = "EMP1",
            Date = new DateOnly(2026, 6, 10),
            ClearedEventId = Registration,
            ClearedHours = 7.4m,
        }, Ctx);

        Assert.Multiple(
            () => Assert.Equal(
                "{\"employeeId\":\"EMP1\",\"date\":\"2026-06-12\",\"absenceType\":\"VACATION\",\"clearedEventId\":\"11111111-2222-3333-4444-555555555555\",\"clearedHours\":7.4,\"clearedFeriedage\":0.5}",
                absence.DetailsJson),
            () => Assert.Equal(
                "{\"employeeId\":\"EMP1\",\"date\":\"2026-06-08\",\"absenceType\":\"SICK_DAY\",\"clearedEventId\":\"11111111-2222-3333-4444-555555555555\",\"clearedHours\":7.4}",
                absenceNoFeriedage.DetailsJson),
            () => Assert.Equal(
                "{\"employeeId\":\"EMP1\",\"date\":\"2026-06-09\",\"taskId\":\"PRJ-1\",\"clearedEventId\":\"11111111-2222-3333-4444-555555555555\",\"clearedHours\":3.5}",
                entry.DetailsJson),
            () => Assert.Equal(
                "{\"employeeId\":\"EMP1\",\"date\":\"2026-06-10\",\"clearedEventId\":\"11111111-2222-3333-4444-555555555555\",\"clearedHours\":7.4}",
                work.DetailsJson));

        // The row's scope and targets (catalog: TENANT_TARGETED, the employee's org, resource = the employee id).
        foreach (var row in new[] { absence, absenceNoFeriedage, entry, work })
        {
            Assert.Equal(AuditVisibilityScope.TenantTargeted, row.VisibilityScope);
            Assert.Equal("STY01", row.TargetOrgId);
            Assert.Equal("EMP1", row.TargetResourceId);
        }
    }
}
