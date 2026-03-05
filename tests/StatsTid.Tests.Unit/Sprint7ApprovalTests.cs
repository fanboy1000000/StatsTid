using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit;

/// <summary>
/// Tests for Sprint 7: Approval period state machine (DRAFT/SUBMITTED/APPROVED/REJECTED),
/// and event serialization roundtrips for PeriodSubmitted, PeriodApproved, PeriodRejected,
/// and LocalConfigurationChanged events.
/// </summary>
public class Sprint7ApprovalTests
{
    // ---------------------------------------------------------------
    // 1. ApprovalPeriod state machine tests
    // ---------------------------------------------------------------

    [Fact]
    public void ApprovalPeriod_DraftState_HasNoSubmittedOrApprovedTimestamps()
    {
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2024, 6, 3),
            PeriodEnd = new DateOnly(2024, 6, 9),
            PeriodType = "WEEKLY",
            Status = "DRAFT",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.Equal("DRAFT", period.Status);
        Assert.Null(period.SubmittedAt);
        Assert.Null(period.SubmittedBy);
        Assert.Null(period.ApprovedBy);
        Assert.Null(period.ApprovedAt);
        Assert.Null(period.RejectionReason);
    }

    [Fact]
    public void ApprovalPeriod_SubmittedState_HasSubmittedByAndTimestamp()
    {
        var submittedAt = new DateTime(2024, 6, 9, 18, 0, 0, DateTimeKind.Utc);
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2024, 6, 3),
            PeriodEnd = new DateOnly(2024, 6, 9),
            PeriodType = "WEEKLY",
            Status = "SUBMITTED",
            SubmittedAt = submittedAt,
            SubmittedBy = "EMP001",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.Equal("SUBMITTED", period.Status);
        Assert.Equal(submittedAt, period.SubmittedAt);
        Assert.Equal("EMP001", period.SubmittedBy);
        Assert.Null(period.ApprovedBy);
        Assert.Null(period.ApprovedAt);
        Assert.Null(period.RejectionReason);
    }

    [Fact]
    public void ApprovalPeriod_ApprovedState_HasApprovedByAndTimestamp()
    {
        var submittedAt = new DateTime(2024, 6, 9, 18, 0, 0, DateTimeKind.Utc);
        var approvedAt = new DateTime(2024, 6, 10, 9, 0, 0, DateTimeKind.Utc);
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2024, 6, 3),
            PeriodEnd = new DateOnly(2024, 6, 9),
            PeriodType = "WEEKLY",
            Status = "APPROVED",
            SubmittedAt = submittedAt,
            SubmittedBy = "EMP001",
            ApprovedBy = "leader01",
            ApprovedAt = approvedAt,
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        Assert.Equal("APPROVED", period.Status);
        Assert.Equal("leader01", period.ApprovedBy);
        Assert.Equal(approvedAt, period.ApprovedAt);
        Assert.Null(period.RejectionReason);
    }

    [Fact]
    public void ApprovalPeriod_RejectedState_HasRejectionReason()
    {
        var submittedAt = new DateTime(2024, 6, 9, 18, 0, 0, DateTimeKind.Utc);
        var period = new ApprovalPeriod
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2024, 6, 3),
            PeriodEnd = new DateOnly(2024, 6, 9),
            PeriodType = "WEEKLY",
            Status = "REJECTED",
            SubmittedAt = submittedAt,
            SubmittedBy = "EMP001",
            AgreementCode = "HK",
            OkVersion = "OK24",
            RejectionReason = "Missing time entries for Wednesday"
        };

        Assert.Equal("REJECTED", period.Status);
        Assert.Equal("Missing time entries for Wednesday", period.RejectionReason);
        Assert.Null(period.ApprovedBy);
        Assert.Null(period.ApprovedAt);
    }

    // ---------------------------------------------------------------
    // 2. Event serialization roundtrip tests
    // ---------------------------------------------------------------

    [Fact]
    public void PeriodSubmitted_Event_SerializationRoundtrip()
    {
        var periodId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAt = new DateTime(2024, 6, 9, 18, 0, 0, DateTimeKind.Utc);

        var original = new PeriodSubmitted
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            ActorId = "EMP001",
            ActorRole = "Employee",
            PeriodId = periodId,
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2024, 6, 3),
            PeriodEnd = new DateOnly(2024, 6, 9),
            PeriodType = "WEEKLY"
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("PeriodSubmitted", json);

        Assert.IsType<PeriodSubmitted>(deserialized);
        var roundTripped = (PeriodSubmitted)deserialized;

        Assert.Equal(eventId, roundTripped.EventId);
        Assert.Equal(periodId, roundTripped.PeriodId);
        Assert.Equal("EMP001", roundTripped.EmployeeId);
        Assert.Equal("AFD01", roundTripped.OrgId);
        Assert.Equal(new DateOnly(2024, 6, 3), roundTripped.PeriodStart);
        Assert.Equal(new DateOnly(2024, 6, 9), roundTripped.PeriodEnd);
        Assert.Equal("WEEKLY", roundTripped.PeriodType);
        Assert.Equal("EMP001", roundTripped.ActorId);
        Assert.Equal("Employee", roundTripped.ActorRole);
    }

    [Fact]
    public void PeriodApproved_Event_SerializationRoundtrip()
    {
        var periodId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAt = new DateTime(2024, 6, 10, 9, 0, 0, DateTimeKind.Utc);

        var original = new PeriodApproved
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            ActorId = "leader01",
            ActorRole = "LocalLeader",
            PeriodId = periodId,
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2024, 6, 3),
            PeriodEnd = new DateOnly(2024, 6, 9),
            ApprovedBy = "leader01"
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("PeriodApproved", json);

        Assert.IsType<PeriodApproved>(deserialized);
        var roundTripped = (PeriodApproved)deserialized;

        Assert.Equal(eventId, roundTripped.EventId);
        Assert.Equal(periodId, roundTripped.PeriodId);
        Assert.Equal("EMP001", roundTripped.EmployeeId);
        Assert.Equal("AFD01", roundTripped.OrgId);
        Assert.Equal(new DateOnly(2024, 6, 3), roundTripped.PeriodStart);
        Assert.Equal(new DateOnly(2024, 6, 9), roundTripped.PeriodEnd);
        Assert.Equal("leader01", roundTripped.ApprovedBy);
    }

    [Fact]
    public void PeriodRejected_Event_SerializationRoundtrip()
    {
        var periodId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAt = new DateTime(2024, 6, 10, 10, 30, 0, DateTimeKind.Utc);

        var original = new PeriodRejected
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            ActorId = "leader01",
            ActorRole = "LocalLeader",
            PeriodId = periodId,
            EmployeeId = "EMP001",
            OrgId = "AFD01",
            PeriodStart = new DateOnly(2024, 6, 3),
            PeriodEnd = new DateOnly(2024, 6, 9),
            RejectedBy = "leader01",
            RejectionReason = "Incomplete entries for Thursday"
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("PeriodRejected", json);

        Assert.IsType<PeriodRejected>(deserialized);
        var roundTripped = (PeriodRejected)deserialized;

        Assert.Equal(eventId, roundTripped.EventId);
        Assert.Equal(periodId, roundTripped.PeriodId);
        Assert.Equal("EMP001", roundTripped.EmployeeId);
        Assert.Equal("AFD01", roundTripped.OrgId);
        Assert.Equal(new DateOnly(2024, 6, 3), roundTripped.PeriodStart);
        Assert.Equal(new DateOnly(2024, 6, 9), roundTripped.PeriodEnd);
        Assert.Equal("leader01", roundTripped.RejectedBy);
        Assert.Equal("Incomplete entries for Thursday", roundTripped.RejectionReason);
    }

    [Fact]
    public void LocalConfigurationChanged_Event_IncludesAllFields()
    {
        var configId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAt = new DateTime(2024, 6, 11, 14, 0, 0, DateTimeKind.Utc);

        var original = new LocalConfigurationChanged
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            ActorId = "ladm01",
            ActorRole = "LocalAdmin",
            ConfigId = configId,
            OrgId = "STY02",
            ConfigArea = "FLEX_RULES",
            ConfigKey = "MaxFlexBalance",
            ConfigValue = "80",
            PreviousValue = "100",
            AgreementCode = "HK",
            OkVersion = "OK24"
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("LocalConfigurationChanged", json);

        Assert.IsType<LocalConfigurationChanged>(deserialized);
        var roundTripped = (LocalConfigurationChanged)deserialized;

        Assert.Equal(eventId, roundTripped.EventId);
        Assert.Equal(configId, roundTripped.ConfigId);
        Assert.Equal("STY02", roundTripped.OrgId);
        Assert.Equal("FLEX_RULES", roundTripped.ConfigArea);
        Assert.Equal("MaxFlexBalance", roundTripped.ConfigKey);
        Assert.Equal("80", roundTripped.ConfigValue);
        Assert.Equal("100", roundTripped.PreviousValue);
        Assert.Equal("HK", roundTripped.AgreementCode);
        Assert.Equal("OK24", roundTripped.OkVersion);
        Assert.Equal("ladm01", roundTripped.ActorId);
        Assert.Equal("LocalAdmin", roundTripped.ActorRole);
    }
}
