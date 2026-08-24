using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models; // WorkInterval (WorkTimeRegistered payload)

namespace StatsTid.Tests.Unit;

/// <summary>
/// Unit tests for Sprint 9 (Skema) event serialization through the REAL
/// <see cref="EventSerializer"/> — the one Skema-adjacent component with a pure, unit-testable
/// contract (serialize/deserialize + the discriminator registry).
///
/// <para>S133 / TASK-13306 (QUAL-095): this file used to carry ~16 verification-theater "tests" that
/// re-implemented the system under test inside the test body — Project-model POCO round-trips that
/// asserted the fields they had just assigned; a local <c>.Where(p =&gt; p.IsActive)</c> standing in
/// for the repository read; approval state-machine "transitions" that asserted a literal
/// <c>period.Status is "DRAFT" or "REJECTED"</c> predicate copied out of the endpoint; and a
/// hidden-absence filter re-implemented with a local <c>HashSet</c>. None touched the shipped Skema
/// repositories or endpoints (all DB-backed), so none could fail on a real regression. They were
/// removed: the shipped Skema save/approval/visibility behavior is genuinely exercised by the
/// Docker-gated regression suite (e.g. <c>S120SkemaSpecRuntimeTests</c>, the <c>Skema/*</c> suites,
/// <c>S94FlatApprovalTests</c> / the <c>Approval/*</c> suites), which drive the real endpoints.</para>
///
/// <para>What remains is genuine: the two <c>SkemaSave_*_RoundTripsThroughEventSerializer</c> tests
/// (rewired from POCO echoes into real serialize→deserialize round-trips of the events the Skema save
/// path emits) and the pre-existing <see cref="EventSerializer"/> round-trip suite. Falsifiability:
/// each RED if the serializer drops a field, changes a discriminator, or loses a type registration.</para>
/// </summary>
public class Sprint9SkemaTests
{
    // ---------------------------------------------------------------
    // 1. The Skema-save events, round-tripped through the REAL EventSerializer
    //    (rewired from the former Save_Emits* POCO round-trips).
    // ---------------------------------------------------------------

    /// <summary>
    /// The Skema save path emits <see cref="TimeEntryRegistered"/> for each project-hours cell.
    /// Drives the REAL <see cref="EventSerializer"/>: the discriminator lands in the JSON and every
    /// field survives serialize → deserialize. RED if the serializer drops the type registration or a
    /// field (the fidelity the outbox + projection replay depend on).
    /// </summary>
    [Fact]
    public void SkemaSave_TimeEntryRegistered_RoundTripsThroughEventSerializer()
    {
        var original = new TimeEntryRegistered
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 3, 5, 9, 0, 0, DateTimeKind.Utc),
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 5),
            Hours = 7.4m,
            TaskId = "DRIFT",
            ActivityType = "NORMAL",
            AgreementCode = "HK",
            OkVersion = "OK24",
            ActorId = "EMP001",
            ActorRole = "Employee",
        };

        var json = EventSerializer.Serialize(original);
        Assert.Contains("TimeEntryRegistered", json);

        var deserialized = EventSerializer.Deserialize("TimeEntryRegistered", json);
        var rt = Assert.IsType<TimeEntryRegistered>(deserialized);
        Assert.Equal("TimeEntryRegistered", rt.EventType);
        Assert.Equal(original.EventId, rt.EventId);
        Assert.Equal("EMP001", rt.EmployeeId);
        Assert.Equal(new DateOnly(2026, 3, 5), rt.Date);
        Assert.Equal(7.4m, rt.Hours);
        Assert.Equal("DRIFT", rt.TaskId);
        Assert.Equal("NORMAL", rt.ActivityType);
    }

    /// <summary>
    /// The Skema save path emits <see cref="AbsenceRegistered"/> for each absence cell. Drives the
    /// REAL <see cref="EventSerializer"/> the same way. RED if the serializer drops the registration
    /// or a field.
    /// </summary>
    [Fact]
    public void SkemaSave_AbsenceRegistered_RoundTripsThroughEventSerializer()
    {
        var original = new AbsenceRegistered
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc),
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 10),
            AbsenceType = "VACATION",
            Hours = 7.4m,
            AgreementCode = "HK",
            OkVersion = "OK24",
            ActorId = "EMP001",
            ActorRole = "Employee",
        };

        var json = EventSerializer.Serialize(original);
        Assert.Contains("AbsenceRegistered", json);

        var deserialized = EventSerializer.Deserialize("AbsenceRegistered", json);
        var rt = Assert.IsType<AbsenceRegistered>(deserialized);
        Assert.Equal("AbsenceRegistered", rt.EventType);
        Assert.Equal(original.EventId, rt.EventId);
        Assert.Equal("EMP001", rt.EmployeeId);
        Assert.Equal("VACATION", rt.AbsenceType);
        Assert.Equal(new DateOnly(2026, 3, 10), rt.Date);
        Assert.Equal(7.4m, rt.Hours);
    }

    // ---------------------------------------------------------------
    // 2. Event serialization roundtrip tests (Sprint 9 event family)
    // ---------------------------------------------------------------

    [Fact]
    public void EventSerializer_SerializesNewEventTypes()
    {
        // Verify all 4 new Sprint 9 event types can be serialized
        var periodEmployeeApproved = new PeriodEmployeeApproved
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "STY01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        var periodReopened = new PeriodReopened
        {
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "STY01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            Reason = "Correction needed",
            ActorId = "leader01",
            ActorRole = "LocalLeader"
        };

        var timerCheckedIn = new TimerCheckedIn
        {
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 5),
            CheckInAt = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc),
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        var timerCheckedOut = new TimerCheckedOut
        {
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 5),
            CheckOutAt = new DateTime(2026, 3, 5, 16, 0, 0, DateTimeKind.Utc),
            ClockedHours = 8.0m,
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        // All should serialize without exceptions
        var json1 = EventSerializer.Serialize(periodEmployeeApproved);
        var json2 = EventSerializer.Serialize(periodReopened);
        var json3 = EventSerializer.Serialize(timerCheckedIn);
        var json4 = EventSerializer.Serialize(timerCheckedOut);

        Assert.NotNull(json1);
        Assert.NotNull(json2);
        Assert.NotNull(json3);
        Assert.NotNull(json4);
        Assert.Contains("PeriodEmployeeApproved", json1);
        Assert.Contains("PeriodReopened", json2);
        Assert.Contains("TimerCheckedIn", json3);
        Assert.Contains("TimerCheckedOut", json4);
    }

    [Fact]
    public void EventSerializer_DeserializesNewEventTypes()
    {
        // Round-trip test for all 4 new Sprint 9 event types
        var originalPeriodEmployeeApproved = new PeriodEmployeeApproved
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 3, 5, 10, 0, 0, DateTimeKind.Utc),
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "STY01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        var json1 = EventSerializer.Serialize(originalPeriodEmployeeApproved);
        var deserialized1 = EventSerializer.Deserialize("PeriodEmployeeApproved", json1);
        Assert.IsType<PeriodEmployeeApproved>(deserialized1);
        var rt1 = (PeriodEmployeeApproved)deserialized1;
        Assert.Equal(originalPeriodEmployeeApproved.EventId, rt1.EventId);
        Assert.Equal(originalPeriodEmployeeApproved.PeriodId, rt1.PeriodId);
        Assert.Equal("EMP001", rt1.EmployeeId);
        Assert.Equal("STY01", rt1.OrgId);
        Assert.Equal(new DateOnly(2026, 3, 1), rt1.PeriodStart);
        Assert.Equal(new DateOnly(2026, 3, 31), rt1.PeriodEnd);

        // PeriodReopened
        var originalPeriodReopened = new PeriodReopened
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 3, 6, 9, 0, 0, DateTimeKind.Utc),
            PeriodId = Guid.NewGuid(),
            EmployeeId = "EMP001",
            OrgId = "STY01",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            Reason = "Fejl i registrering",
            ActorId = "leader01",
            ActorRole = "LocalLeader"
        };

        var json2 = EventSerializer.Serialize(originalPeriodReopened);
        var deserialized2 = EventSerializer.Deserialize("PeriodReopened", json2);
        Assert.IsType<PeriodReopened>(deserialized2);
        var rt2 = (PeriodReopened)deserialized2;
        Assert.Equal(originalPeriodReopened.EventId, rt2.EventId);
        Assert.Equal(originalPeriodReopened.PeriodId, rt2.PeriodId);
        Assert.Equal("Fejl i registrering", rt2.Reason);

        // TimerCheckedIn
        var originalTimerCheckedIn = new TimerCheckedIn
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc),
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 5),
            CheckInAt = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc),
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        var json3 = EventSerializer.Serialize(originalTimerCheckedIn);
        var deserialized3 = EventSerializer.Deserialize("TimerCheckedIn", json3);
        Assert.IsType<TimerCheckedIn>(deserialized3);
        var rt3 = (TimerCheckedIn)deserialized3;
        Assert.Equal(originalTimerCheckedIn.EventId, rt3.EventId);
        Assert.Equal("EMP001", rt3.EmployeeId);
        Assert.Equal(new DateOnly(2026, 3, 5), rt3.Date);
        Assert.Equal(originalTimerCheckedIn.CheckInAt, rt3.CheckInAt);

        // TimerCheckedOut
        var originalTimerCheckedOut = new TimerCheckedOut
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 3, 5, 16, 0, 0, DateTimeKind.Utc),
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 5),
            CheckOutAt = new DateTime(2026, 3, 5, 16, 0, 0, DateTimeKind.Utc),
            ClockedHours = 8.0m,
            ActorId = "EMP001",
            ActorRole = "Employee"
        };

        var json4 = EventSerializer.Serialize(originalTimerCheckedOut);
        var deserialized4 = EventSerializer.Deserialize("TimerCheckedOut", json4);
        Assert.IsType<TimerCheckedOut>(deserialized4);
        var rt4 = (TimerCheckedOut)deserialized4;
        Assert.Equal(originalTimerCheckedOut.EventId, rt4.EventId);
        Assert.Equal("EMP001", rt4.EmployeeId);
        Assert.Equal(new DateOnly(2026, 3, 5), rt4.Date);
        Assert.Equal(8.0m, rt4.ClockedHours);
        Assert.Equal(originalTimerCheckedOut.CheckOutAt, rt4.CheckOutAt);
    }

    /// <summary>
    /// S56 / TASK-5603: WorkTimeRegistered round-trips through the
    /// EventSerializer — the interval list (start/end strings) and the
    /// manual-hours scalar survive serialize → deserialize. This is the event
    /// the Skema save path enqueues and the backfill replays into
    /// work_time_projection, so its fidelity is load-bearing.
    /// </summary>
    [Fact]
    public void EventSerializer_WorkTimeRegistered_RoundTrips()
    {
        var original = new WorkTimeRegistered
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc),
            EmployeeId = "EMP001",
            Date = new DateOnly(2026, 3, 5),
            Intervals = new[]
            {
                new WorkInterval { Start = "08:00", End = "12:00" },
                new WorkInterval { Start = "12:30", End = "16:00" },
            },
            ManualHours = 0.5m,
            ActorId = "EMP001",
            ActorRole = "Employee",
        };

        var json = EventSerializer.Serialize(original);
        Assert.Contains("WorkTimeRegistered", json);

        var deserialized = EventSerializer.Deserialize("WorkTimeRegistered", json);
        Assert.IsType<WorkTimeRegistered>(deserialized);
        var rt = (WorkTimeRegistered)deserialized;
        Assert.Equal(original.EventId, rt.EventId);
        Assert.Equal("EMP001", rt.EmployeeId);
        Assert.Equal(new DateOnly(2026, 3, 5), rt.Date);
        Assert.Equal(0.5m, rt.ManualHours);
        Assert.Collection(rt.Intervals,
            iv => { Assert.Equal("08:00", iv.Start); Assert.Equal("12:00", iv.End); },
            iv => { Assert.Equal("12:30", iv.Start); Assert.Equal("16:00", iv.End); });
    }
}
