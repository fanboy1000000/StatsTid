using System.Text.Json;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.Events;

/// <summary>
/// S148 / TASK-14815-T — DEP-003 protocol step 3: each of the five config-stage events is written with every
/// member filled, read back through the REAL <see cref="EventSerializer"/>, and compared member by member. It also
/// asserts the literal camelCase name each member is stored under, because stored events are replayed by those
/// names (a renamed or ignored member silently breaks the audit trail).
///
/// <para><b>Kind N</b> for every fact (each names an event this sprint creates). Red evidence is the mutation M-37,
/// not a revert: <c>[JsonIgnore]</c> on <c>UserOrganisationTransferred.PredecessorAssignmentId</c>,
/// <c>EmployeeEmploymentStartDateSet.NewStartDate</c>, <c>CustomaryDayChanged.PartFromTime</c> and
/// <c>EmployeeHrFactsChanged.OvertimeRegime</c> (each trips that member's equality and wire-name assertion), and
/// <c>[JsonPropertyName("organisationId")]</c> on <c>UserOrganisationSeeded.OrgId</c> (trips its wire-name
/// assertion). Red conditions: the member is lost or stored under another name. The expected values are the
/// ones written below and the camelCase names of the members, never read back from the code.</para>
/// </summary>
public class S148ConfigStageEventRoundTripTests
{
    private static readonly Guid EventId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly DateTime Occurred = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

    private static readonly Guid A1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid A2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static HashSet<string> WireNames(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void UserOrganisationSeeded_RoundTrips_EveryMember()
    {
        var original = new UserOrganisationSeeded
        {
            EventId = EventId, OccurredAt = Occurred, ActorId = "HR001", ActorRole = "LocalHR",
            UserId = "EMP001", AssignmentId = A1, OrgId = "STY02",
            EffectiveFrom = new DateOnly(2026, 6, 15), RowVersion = 3,
        };

        var json = EventSerializer.Serialize(original);
        var rt = Assert.IsType<UserOrganisationSeeded>(EventSerializer.Deserialize(original.EventType, json));
        var names = WireNames(json);

        Assert.Multiple(
            () => Assert.Equal("UserOrganisationSeeded", rt.EventType),
            () => Assert.Equal(EventId, rt.EventId),
            () => Assert.Equal(Occurred, rt.OccurredAt),
            () => Assert.Equal("HR001", rt.ActorId),
            () => Assert.Equal("LocalHR", rt.ActorRole),
            () => Assert.Equal("EMP001", rt.UserId),
            () => Assert.Equal(A1, rt.AssignmentId),
            () => Assert.Equal("STY02", rt.OrgId),
            () => Assert.Equal(new DateOnly(2026, 6, 15), rt.EffectiveFrom),
            () => Assert.Equal(3L, rt.RowVersion),
            () => Assert.True(names.IsSupersetOf(new[] { "userId", "assignmentId", "orgId", "effectiveFrom", "rowVersion" }),
                "wire names: " + string.Join(",", names)));
    }

    [Fact]
    public void UserOrganisationTransferred_RoundTrips_BothShapes()
    {
        var closeAndOpen = new UserOrganisationTransferred
        {
            EventId = EventId, OccurredAt = Occurred, ActorId = "HR001", ActorRole = "LocalHR",
            UserId = "EMP001", AssignmentId = A2, PredecessorAssignmentId = A1,
            OldOrgId = "STY02", NewOrgId = "STY03",
            EffectiveFrom = new DateOnly(2026, 6, 15), InPlace = false, VersionBefore = 1, VersionAfter = 2,
        };
        var inPlace = new UserOrganisationTransferred
        {
            EventId = EventId, OccurredAt = Occurred, ActorId = "HR001", ActorRole = "LocalHR",
            UserId = "EMP001", AssignmentId = A1, PredecessorAssignmentId = null,
            OldOrgId = "STY02", NewOrgId = "STY03",
            EffectiveFrom = new DateOnly(2026, 6, 15), InPlace = true, VersionBefore = 1, VersionAfter = 2,
        };

        var json1 = EventSerializer.Serialize(closeAndOpen);
        var json2 = EventSerializer.Serialize(inPlace);
        var rt1 = Assert.IsType<UserOrganisationTransferred>(EventSerializer.Deserialize(closeAndOpen.EventType, json1));
        var rt2 = Assert.IsType<UserOrganisationTransferred>(EventSerializer.Deserialize(inPlace.EventType, json2));
        var n1 = WireNames(json1);
        var n2 = WireNames(json2);

        Assert.Multiple(
            () => Assert.Equal("UserOrganisationTransferred", rt1.EventType),
            () => Assert.Equal("EMP001", rt1.UserId),
            () => Assert.Equal(A2, rt1.AssignmentId),
            () => Assert.Equal(A1, rt1.PredecessorAssignmentId),
            () => Assert.Equal("STY02", rt1.OldOrgId),
            () => Assert.Equal("STY03", rt1.NewOrgId),
            () => Assert.Equal(new DateOnly(2026, 6, 15), rt1.EffectiveFrom),
            () => Assert.False(rt1.InPlace),
            () => Assert.Equal(1L, rt1.VersionBefore),
            () => Assert.Equal(2L, rt1.VersionAfter),
            () => Assert.Equal(EventId, rt1.EventId),
            () => Assert.Equal("HR001", rt1.ActorId),
            () => Assert.True(n1.IsSupersetOf(new[]
                { "userId", "assignmentId", "predecessorAssignmentId", "oldOrgId", "newOrgId", "effectiveFrom", "inPlace", "versionBefore", "versionAfter" }),
                "wire names: " + string.Join(",", n1)),
            () => Assert.Equal(A1, rt2.AssignmentId),
            () => Assert.Null(rt2.PredecessorAssignmentId),
            () => Assert.True(rt2.InPlace),
            () => Assert.DoesNotContain("predecessorAssignmentId", n2));
    }

    [Fact]
    public void EmployeeEmploymentStartDateSet_RoundTrips_SetClearAndCorrect()
    {
        EmployeeEmploymentStartDateSet Make(DateOnly? oldD, DateOnly? newD, long before, long after) => new()
        {
            EventId = EventId, OccurredAt = Occurred, ActorId = "HR001", ActorRole = "LocalHR",
            EmployeeId = "EMP001", OldStartDate = oldD, NewStartDate = newD, VersionBefore = before, VersionAfter = after,
        };

        // set (null -> Monday 15 June 2026), clear (Monday 2 March 2026 -> null), correct (Sunday 15 March -> Monday 2 March)
        var set = Make(null, new DateOnly(2026, 6, 15), 4, 5);
        var clear = Make(new DateOnly(2026, 3, 2), null, 5, 6);
        var correct = Make(new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 2), 6, 7);

        var jSet = EventSerializer.Serialize(set);
        var jClear = EventSerializer.Serialize(clear);
        var jCorrect = EventSerializer.Serialize(correct);
        var rSet = Assert.IsType<EmployeeEmploymentStartDateSet>(EventSerializer.Deserialize(set.EventType, jSet));
        var rClear = Assert.IsType<EmployeeEmploymentStartDateSet>(EventSerializer.Deserialize(clear.EventType, jClear));
        var rCorrect = Assert.IsType<EmployeeEmploymentStartDateSet>(EventSerializer.Deserialize(correct.EventType, jCorrect));

        Assert.Multiple(
            () => Assert.Equal("EmployeeEmploymentStartDateSet", rSet.EventType),
            () => Assert.Equal("EMP001", rSet.EmployeeId),
            () => Assert.Null(rSet.OldStartDate),
            () => Assert.Equal(new DateOnly(2026, 6, 15), rSet.NewStartDate),
            () => Assert.Equal(4L, rSet.VersionBefore),
            () => Assert.Equal(5L, rSet.VersionAfter),
            () => Assert.Equal(new DateOnly(2026, 3, 2), rClear.OldStartDate),
            () => Assert.Null(rClear.NewStartDate),
            () => Assert.Equal(new DateOnly(2026, 3, 15), rCorrect.OldStartDate),
            () => Assert.Equal(new DateOnly(2026, 3, 2), rCorrect.NewStartDate),
            () => Assert.Equal(7L, rCorrect.VersionAfter),
            () => Assert.Equal(EventId, rCorrect.EventId),
            () => Assert.Equal("LocalHR", rCorrect.ActorRole),
            () => Assert.True(WireNames(jCorrect).IsSupersetOf(new[]
                { "employeeId", "oldStartDate", "newStartDate", "versionBefore", "versionAfter" })),
            () => Assert.Contains("newStartDate", WireNames(jSet)),
            () => Assert.DoesNotContain("oldStartDate", WireNames(jSet)),
            () => Assert.Contains("oldStartDate", WireNames(jClear)),
            () => Assert.DoesNotContain("newStartDate", WireNames(jClear)));
    }

    [Fact]
    public void CustomaryDayChanged_RoundTrips_RecurringAndSingleDate()
    {
        var custom1 = Guid.Parse("c0c0c0c0-0000-0000-0000-000000000001");
        var custom2 = Guid.Parse("c0c0c0c0-0000-0000-0000-000000000002");

        // a paid day off on 5 June, recurring, from 12:00, given to all, valid from Monday 5 January 2026
        var recurring = new CustomaryDayChanged
        {
            EventId = EventId, OccurredAt = Occurred, ActorId = "LA001", ActorRole = "LocalAdmin",
            CustomId = custom1, OrgId = "STY02", Action = "CREATED", AgreementCode = "AC",
            RecurringMonth = 6, RecurringDay = 5, SingleDate = null,
            Kind = "PAID_DAY_OFF", Part = "FROM_TIME", PartFromTime = new TimeOnly(12, 0), PartHours = null,
            PaidMode = "GIVEN_TO_ALL", ValidFrom = new DateOnly(2026, 1, 5), ValidTo = null,
            PrecedingCustomId = null, VersionBefore = null, VersionAfter = 1,
        };
        // a closure day on the single date Friday 15 May 2026, whole day, withdrawn from Monday 1 June 2026
        var single = new CustomaryDayChanged
        {
            EventId = EventId, OccurredAt = Occurred, ActorId = "LA001", ActorRole = "LocalAdmin",
            CustomId = custom2, OrgId = "STY02", Action = "WITHDRAWN", AgreementCode = null,
            RecurringMonth = null, RecurringDay = null, SingleDate = new DateOnly(2026, 5, 15),
            // WHOLE_DAY with PartHours is refused by `customary_days_part_hours_check`; filled here only to pin every details key. Not an example row.
            Kind = "CLOSURE_DAY", Part = "WHOLE_DAY", PartFromTime = null, PartHours = 7.4m,
            PaidMode = null, ValidFrom = new DateOnly(2026, 1, 5), ValidTo = new DateOnly(2026, 6, 1),
            PrecedingCustomId = custom1, VersionBefore = 1, VersionAfter = 2,
        };

        var j1 = EventSerializer.Serialize(recurring);
        var j2 = EventSerializer.Serialize(single);
        var r1 = Assert.IsType<CustomaryDayChanged>(EventSerializer.Deserialize(recurring.EventType, j1));
        var r2 = Assert.IsType<CustomaryDayChanged>(EventSerializer.Deserialize(single.EventType, j2));

        Assert.Multiple(
            () => Assert.Equal("CustomaryDayChanged", r1.EventType),
            () => Assert.Equal(custom1, r1.CustomId),
            () => Assert.Equal("STY02", r1.OrgId),
            () => Assert.Equal("CREATED", r1.Action),
            () => Assert.Equal("AC", r1.AgreementCode),
            () => Assert.Equal(6, r1.RecurringMonth),
            () => Assert.Equal(5, r1.RecurringDay),
            () => Assert.Null(r1.SingleDate),
            () => Assert.Equal("PAID_DAY_OFF", r1.Kind),
            () => Assert.Equal("FROM_TIME", r1.Part),
            () => Assert.Equal(new TimeOnly(12, 0), r1.PartFromTime),
            () => Assert.Null(r1.PartHours),
            () => Assert.Equal("GIVEN_TO_ALL", r1.PaidMode),
            () => Assert.Equal(new DateOnly(2026, 1, 5), r1.ValidFrom),
            () => Assert.Null(r1.ValidTo),
            () => Assert.Null(r1.PrecedingCustomId),
            () => Assert.Null(r1.VersionBefore),
            () => Assert.Equal(1L, r1.VersionAfter),
            () => Assert.Equal("LocalAdmin", r1.ActorRole),
            () => Assert.True(WireNames(j1).IsSupersetOf(new[]
                { "customId", "orgId", "action", "agreementCode", "recurringMonth", "recurringDay", "kind", "part",
                  "partFromTime", "paidMode", "validFrom", "versionAfter" }), "wire names: " + string.Join(",", WireNames(j1))),
            () => Assert.Equal(custom2, r2.CustomId),
            () => Assert.Equal("WITHDRAWN", r2.Action),
            () => Assert.Null(r2.AgreementCode),
            () => Assert.Null(r2.RecurringMonth),
            () => Assert.Null(r2.RecurringDay),
            () => Assert.Equal(new DateOnly(2026, 5, 15), r2.SingleDate),
            () => Assert.Equal("CLOSURE_DAY", r2.Kind),
            () => Assert.Equal("WHOLE_DAY", r2.Part),
            () => Assert.Null(r2.PartFromTime),
            () => Assert.Equal(7.4m, r2.PartHours),
            () => Assert.Null(r2.PaidMode),
            () => Assert.Equal(new DateOnly(2026, 6, 1), r2.ValidTo),
            () => Assert.Equal(custom1, r2.PrecedingCustomId),
            () => Assert.Equal(1L, r2.VersionBefore),
            () => Assert.Equal(2L, r2.VersionAfter),
            () => Assert.True(WireNames(j2).IsSupersetOf(new[]
                { "singleDate", "partHours", "validTo", "precedingCustomId", "versionBefore" }), "wire names: " + string.Join(",", WireNames(j2))));
    }

    [Fact]
    public void EmployeeHrFactsChanged_RoundTrips_WithAndWithoutValues()
    {
        var facts1 = Guid.Parse("f0f0f0f0-0000-0000-0000-000000000001");
        var facts2 = Guid.Parse("f0f0f0f0-0000-0000-0000-000000000002");

        var withValues = new EmployeeHrFactsChanged
        {
            EventId = EventId, OccurredAt = Occurred, ActorId = "HR001", ActorRole = "LocalHR",
            EmployeeId = "EMP001", FactsId = facts2, Action = "SUPERSEDED",
            OvertimeRegime = "OVERTIME", OnFlexScheme = true,
            EffectiveFrom = new DateOnly(1, 1, 1), EffectiveTo = new DateOnly(2026, 7, 1),
            PrecedingFactsId = facts1, VersionBefore = 1, VersionAfter = 2,
        };
        var withoutValues = new EmployeeHrFactsChanged
        {
            EventId = EventId, OccurredAt = Occurred, ActorId = "HR001", ActorRole = "LocalHR",
            EmployeeId = "EMP001", FactsId = facts1, Action = "CREATED",
            OvertimeRegime = null, OnFlexScheme = null,
            EffectiveFrom = new DateOnly(1, 1, 1), EffectiveTo = null,
            PrecedingFactsId = null, VersionBefore = null, VersionAfter = 1,
        };

        var j1 = EventSerializer.Serialize(withValues);
        var j2 = EventSerializer.Serialize(withoutValues);
        var r1 = Assert.IsType<EmployeeHrFactsChanged>(EventSerializer.Deserialize(withValues.EventType, j1));
        var r2 = Assert.IsType<EmployeeHrFactsChanged>(EventSerializer.Deserialize(withoutValues.EventType, j2));

        Assert.Multiple(
            () => Assert.Equal("EmployeeHrFactsChanged", r1.EventType),
            () => Assert.Equal("EMP001", r1.EmployeeId),
            () => Assert.Equal(facts2, r1.FactsId),
            () => Assert.Equal("SUPERSEDED", r1.Action),
            () => Assert.Equal("OVERTIME", r1.OvertimeRegime),
            () => Assert.Equal(true, r1.OnFlexScheme),
            () => Assert.Equal(new DateOnly(1, 1, 1), r1.EffectiveFrom),
            () => Assert.Equal(new DateOnly(2026, 7, 1), r1.EffectiveTo),
            () => Assert.Equal(facts1, r1.PrecedingFactsId),
            () => Assert.Equal(1L, r1.VersionBefore),
            () => Assert.Equal(2L, r1.VersionAfter),
            () => Assert.Equal("HR001", r1.ActorId),
            () => Assert.True(WireNames(j1).IsSupersetOf(new[]
                { "employeeId", "factsId", "action", "overtimeRegime", "onFlexScheme", "effectiveFrom", "effectiveTo",
                  "precedingFactsId", "versionBefore", "versionAfter" }), "wire names: " + string.Join(",", WireNames(j1))),
            () => Assert.Null(r2.OvertimeRegime),
            () => Assert.Null(r2.OnFlexScheme),
            () => Assert.Null(r2.EffectiveTo),
            () => Assert.Null(r2.PrecedingFactsId),
            () => Assert.Null(r2.VersionBefore),
            () => Assert.Equal(1L, r2.VersionAfter),
            () => Assert.DoesNotContain("overtimeRegime", WireNames(j2)));
    }
}
