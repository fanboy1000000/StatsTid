using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.Events;

/// <summary>
/// S148 / TASK-14816-T — DEP-003 and ADR-017 D7: an event stored before a member was removed must still read.
/// The flex carryover maximum was retired from the three role-override events (ADR-043 D6). Each JSON below is a
/// payload as the serializer wrote it before the retirement (camelCase, every required member present, the retired
/// member among them, hard-coded). It goes through the real <see cref="EventSerializer.Deserialize"/>.
///
/// <para><b>Kind R</b> (a regression pin: the assertions hold before the retirement, because the member then maps,
/// and after it, because the member is skipped). Discriminating assertion: every remaining member equals the JSON's
/// value. Red conditions: the fact fails if a later change makes an unknown member an error (for example
/// <c>UnmappedMemberHandling.Disallow</c> in <c>EventSerializer</c>'s options) or marks a member <c>required</c> that
/// old payloads lack.</para>
/// </summary>
public class RoleConfigOverrideRetiredMemberTests
{
    private const string CreatedJson = """
        {
          "overrideId": "11111111-1111-1111-1111-111111111111",
          "employmentCategory": "Konsulent",
          "agreementCode": "HK",
          "okVersion": "OK24",
          "effectiveFrom": "2026-06-15",
          "merarbejdeCompensationRight": "EMPLOYEE_CHOICE",
          "hasMerarbejde": true,
          "hasOvertime": false,
          "hasEveningSupplement": true,
          "hasNightSupplement": false,
          "hasWeekendSupplement": true,
          "hasHolidaySupplement": false,
          "maxFlexBalance": 120.5,
          "flexCarryoverMax": 40.25,
          "normPeriodWeeks": 4,
          "weeklyNormHours": 37.5,
          "eventId": "aaaaaaaa-0000-0000-0000-000000000001",
          "occurredAt": "2026-06-15T08:00:00Z",
          "eventType": "RoleConfigOverrideCreated",
          "version": 1,
          "actorId": "ADM001",
          "actorRole": "GlobalAdmin"
        }
        """;

    private const string UpdatedJson = """
        {
          "overrideId": "11111111-1111-1111-1111-111111111111",
          "versionBefore": 3,
          "versionAfter": 4,
          "merarbejdeCompensationRight": "PAYOUT",
          "hasMerarbejde": false,
          "hasOvertime": true,
          "hasEveningSupplement": false,
          "hasNightSupplement": true,
          "hasWeekendSupplement": false,
          "hasHolidaySupplement": true,
          "maxFlexBalance": 120.5,
          "flexCarryoverMax": 40.25,
          "normPeriodWeeks": 8,
          "weeklyNormHours": 36.25,
          "eventId": "aaaaaaaa-0000-0000-0000-000000000002",
          "occurredAt": "2026-06-15T08:00:00Z",
          "eventType": "RoleConfigOverrideUpdated",
          "version": 1,
          "actorId": "ADM001",
          "actorRole": "GlobalAdmin"
        }
        """;

    private const string SupersededJson = """
        {
          "predecessorOverrideId": "11111111-1111-1111-1111-111111111111",
          "successorOverrideId": "22222222-2222-2222-2222-222222222222",
          "effectiveFrom": "2026-07-01",
          "employmentCategory": "Konsulent",
          "agreementCode": "HK",
          "okVersion": "OK24",
          "merarbejdeCompensationRight": "TIME_OFF",
          "hasMerarbejde": true,
          "hasOvertime": true,
          "hasEveningSupplement": true,
          "hasNightSupplement": true,
          "hasWeekendSupplement": true,
          "hasHolidaySupplement": true,
          "maxFlexBalance": 120.5,
          "flexCarryoverMax": 40.25,
          "normPeriodWeeks": 12,
          "weeklyNormHours": 35.5,
          "eventId": "aaaaaaaa-0000-0000-0000-000000000003",
          "occurredAt": "2026-06-15T08:00:00Z",
          "eventType": "RoleConfigOverrideSuperseded",
          "version": 1,
          "actorId": "ADM001",
          "actorRole": "GlobalAdmin"
        }
        """;

    private static readonly Guid Id1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Id2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Occurred = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string, string> StoredPayloads => new()
    {
        { "RoleConfigOverrideCreated", CreatedJson },
        { "RoleConfigOverrideUpdated", UpdatedJson },
        { "RoleConfigOverrideSuperseded", SupersededJson },
    };

    [Theory]
    [MemberData(nameof(StoredPayloads))]
    public void StoredPayloadWithFlexCarryoverMax_StillDeserialises(string eventType, string json)
    {
        var evt = EventSerializer.Deserialize(eventType, json);

        Assert.Equal(eventType, evt.EventType);
        Assert.Equal(Occurred, evt.OccurredAt);
        Assert.Equal(1, evt.Version);
        Assert.Equal("ADM001", evt.ActorId);
        Assert.Equal("GlobalAdmin", evt.ActorRole);

        switch (evt)
        {
            case RoleConfigOverrideCreated c:
                Assert.Equal(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), c.EventId);
                Assert.Equal(Id1, c.OverrideId);
                Assert.Equal("Konsulent", c.EmploymentCategory);
                Assert.Equal("HK", c.AgreementCode);
                Assert.Equal("OK24", c.OkVersion);
                Assert.Equal(new DateOnly(2026, 6, 15), c.EffectiveFrom);
                Assert.Equal("EMPLOYEE_CHOICE", c.MerarbejdeCompensationRight);
                Assert.True(c.HasMerarbejde);
                Assert.False(c.HasOvertime);
                Assert.True(c.HasEveningSupplement);
                Assert.False(c.HasNightSupplement);
                Assert.True(c.HasWeekendSupplement);
                Assert.False(c.HasHolidaySupplement);
                Assert.Equal(120.5m, c.MaxFlexBalance);
                Assert.Equal(4, c.NormPeriodWeeks);
                Assert.Equal(37.5m, c.WeeklyNormHours);
                break;
            case RoleConfigOverrideUpdated u:
                Assert.Equal(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), u.EventId);
                Assert.Equal(Id1, u.OverrideId);
                Assert.Equal(3L, u.VersionBefore);
                Assert.Equal(4L, u.VersionAfter);
                Assert.Equal("PAYOUT", u.MerarbejdeCompensationRight);
                Assert.False(u.HasMerarbejde);
                Assert.True(u.HasOvertime);
                Assert.False(u.HasEveningSupplement);
                Assert.True(u.HasNightSupplement);
                Assert.False(u.HasWeekendSupplement);
                Assert.True(u.HasHolidaySupplement);
                Assert.Equal(120.5m, u.MaxFlexBalance);
                Assert.Equal(8, u.NormPeriodWeeks);
                Assert.Equal(36.25m, u.WeeklyNormHours);
                break;
            case RoleConfigOverrideSuperseded s:
                Assert.Equal(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), s.EventId);
                Assert.Equal(Id1, s.PredecessorOverrideId);
                Assert.Equal(Id2, s.SuccessorOverrideId);
                Assert.Equal(new DateOnly(2026, 7, 1), s.EffectiveFrom);
                Assert.Equal("Konsulent", s.EmploymentCategory);
                Assert.Equal("HK", s.AgreementCode);
                Assert.Equal("OK24", s.OkVersion);
                Assert.Equal("TIME_OFF", s.MerarbejdeCompensationRight);
                Assert.True(s.HasMerarbejde);
                Assert.True(s.HasOvertime);
                Assert.True(s.HasEveningSupplement);
                Assert.True(s.HasNightSupplement);
                Assert.True(s.HasWeekendSupplement);
                Assert.True(s.HasHolidaySupplement);
                Assert.Equal(120.5m, s.MaxFlexBalance);
                Assert.Equal(12, s.NormPeriodWeeks);
                Assert.Equal(35.5m, s.WeeklyNormHours);
                break;
            default:
                Assert.Fail($"Unexpected concrete type {evt.GetType().Name} for {eventType}.");
                break;
        }
    }
}
