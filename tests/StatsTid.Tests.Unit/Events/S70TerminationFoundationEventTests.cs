using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.Events;

/// <summary>
/// S70 / ADR-033 slice 3a (TASK-7001, SPRINT-70 R10) — EventSerializer round-trip tests for the
/// two new leaver-lifecycle events. Registration coverage itself is guarded generically by
/// <see cref="EventSerializerCoverageTests"/> (DEP-003); these tests pin FIELD preservation,
/// mirroring <c>EventSerializer_RoundTrip_UserUpdated_PreservesAllFields</c>.
/// </summary>
public class S70TerminationFoundationEventTests
{
    [Fact]
    public void EventSerializer_RoundTrip_EmployeeEmploymentEndDateSet_PreservesAllFields()
    {
        var correlationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 6, 10, 8, 30, 0, DateTimeKind.Utc);

        var original = new EmployeeEmploymentEndDateSet
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "ADMIN001",
            ActorRole = "HR",
            CorrelationId = correlationId,
            EmployeeId = "EMP042",
            OldEndDate = new DateOnly(2026, 6, 30),
            NewEndDate = new DateOnly(2026, 7, 31),
            OldIsActive = true,
            NewIsActive = true,
            VersionBefore = 4,
            VersionAfter = 5,
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("EmployeeEmploymentEndDateSet", json);

        var result = Assert.IsType<EmployeeEmploymentEndDateSet>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("EmployeeEmploymentEndDateSet", result.EventType);
        Assert.Equal("ADMIN001", result.ActorId);
        Assert.Equal("HR", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal("EMP042", result.EmployeeId);
        Assert.Equal(new DateOnly(2026, 6, 30), result.OldEndDate);
        Assert.Equal(new DateOnly(2026, 7, 31), result.NewEndDate);
        Assert.True(result.OldIsActive);
        Assert.True(result.NewIsActive);
        Assert.Equal(4, result.VersionBefore);
        Assert.Equal(5, result.VersionAfter);
    }

    [Fact]
    public void EventSerializer_RoundTrip_EmployeeEmploymentEndDateSet_ClearTransition_PreservesNullNewEndDate()
    {
        // The CLEAR transition (date → null) must survive the WhenWritingNull omission:
        // NewEndDate is non-required nullable precisely so the omitted field deserializes
        // back to null (S66 e0d1dc3 round-trippability lesson).
        var original = new EmployeeEmploymentEndDateSet
        {
            EmployeeId = "EMP042",
            OldEndDate = new DateOnly(2026, 6, 30),
            NewEndDate = null,
            OldIsActive = false,
            NewIsActive = true,
            VersionBefore = 7,
            VersionAfter = 8,
        };

        var json = EventSerializer.Serialize(original);
        var result = Assert.IsType<EmployeeEmploymentEndDateSet>(
            EventSerializer.Deserialize("EmployeeEmploymentEndDateSet", json));

        Assert.Equal(new DateOnly(2026, 6, 30), result.OldEndDate);
        Assert.Null(result.NewEndDate);
        Assert.False(result.OldIsActive);
        Assert.True(result.NewIsActive);
    }

    [Fact]
    public void EventSerializer_RoundTrip_EmployeeEndDateDeactivationApplied_PreservesAllFields()
    {
        var correlationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 8, 1, 2, 0, 0, DateTimeKind.Utc);

        var original = new EmployeeEndDateDeactivationApplied
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            // System actor per the settlement actor convention (SPRINT-70 R2).
            ActorId = "system:settlement-close:STEP_A",
            ActorRole = "System",
            CorrelationId = correlationId,
            EmployeeId = "EMP042",
            EndDate = new DateOnly(2026, 7, 31),
            OldIsActive = true,
            NewIsActive = false,
            VersionBefore = 5,
            VersionAfter = 6,
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("EmployeeEndDateDeactivationApplied", json);

        var result = Assert.IsType<EmployeeEndDateDeactivationApplied>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("EmployeeEndDateDeactivationApplied", result.EventType);
        Assert.Equal("system:settlement-close:STEP_A", result.ActorId);
        Assert.Equal("System", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal("EMP042", result.EmployeeId);
        Assert.Equal(new DateOnly(2026, 7, 31), result.EndDate);
        Assert.True(result.OldIsActive);
        Assert.False(result.NewIsActive);
        Assert.Equal(5, result.VersionBefore);
        Assert.Equal(6, result.VersionAfter);
    }
}
