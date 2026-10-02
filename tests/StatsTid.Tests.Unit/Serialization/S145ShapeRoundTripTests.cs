using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Serialization;

/// <summary>
/// S145 / TASK-14506 — the new optional members S145 adds to four persisted shapes must round-trip
/// through the event serializer, and must read back as <b>null</b> from a payload written before they
/// existed (TASK-14500 Do 3 and Do 5; refinement A0 constraint 1, A7 "Where it lives", Q7 (b), R41).
///
/// <para>
/// <b>Plain language.</b> The system never edits an event once written (event sourcing: the log is the
/// record of truth, and the database tables are rebuilt from it). S145 needs four new facts recorded in
/// that log: <i>where an hours row came from</i> (<c>Source</c>: <c>SKEMA</c> for a Skema save, absent
/// for an additive time registration, which is also what every old event means); <i>what a calculation
/// used</i> (<c>PlanInputs</c> on the segment-manifest event: the frozen pieces, the opening flex
/// balance, the ids of the hours rows, their digests, the config fingerprints, the arithmetic version,
/// and the settlement cutoff); and <i>which pay period a recalculation also changed</i>
/// (<c>NextPeriodToRecalculateStart/End</c> on the retroactive-correction event). If a serializer
/// attribute ever swallowed one of those members, the system would write the event happily and the fact
/// would be gone at replay time — so each fact below writes a value, reads it back, and compares it.
/// Every new member is optional, so old payloads must still deserialize, to null.
/// </para>
///
/// <para>
/// <b>R41 in particular:</b> <c>PlanInputs.SettlementCutoff</c> is the frozen settlement cutoff (the
/// employment window's inclusive end when it is on or before the period end, otherwise null). It cannot
/// be recovered from the plan, so losing it silently would make a leaver's replay settle the closing
/// week in the wrong month. It is pinned both set and null.
/// </para>
///
/// <para>
/// <b>Red conditions</b> (RED by MUTATION at the A.1 gate — these pins name members TASK-14500 creates):
/// MU-22 puts <c>[JsonIgnore]</c> on <c>Source</c> (both events) and trips the two <c>Source</c> facts;
/// MU-23 puts it on <c>PlanInputs</c> (which also drops <c>SettlementCutoff</c>) and trips the manifest
/// fact; MU-24 puts it on the two next-period fields and trips the correction fact.
/// </para>
/// </summary>
public sealed class S145ShapeRoundTripTests
{
    // -------------------------------------------------------------------------------------------
    // TimeEntryRegistered.Source / AbsenceRegistered.Source
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void TimeEntryRegistered_Source_RoundTrips_AndIsNullWhenAbsent()
    {
        TimeEntryRegistered Event(string? source) => new()
        {
            EmployeeId = "EMP-1",
            Date = new DateOnly(2026, 9, 14),
            Hours = 7.40m,
            TaskId = "TASK-1",
            AgreementCode = "HK",
            OkVersion = "OK26",
            Source = source,
        };

        // A Skema save: SKEMA survives the round trip.
        var skema = Assert.IsType<TimeEntryRegistered>(
            EventSerializer.Deserialize("TimeEntryRegistered", EventSerializer.Serialize(Event("SKEMA"))));
        Assert.Equal("SKEMA", skema.Source);

        // An explicit TIME_POST survives too (the second legal value).
        var timePost = Assert.IsType<TimeEntryRegistered>(
            EventSerializer.Deserialize("TimeEntryRegistered", EventSerializer.Serialize(Event("TIME_POST"))));
        Assert.Equal("TIME_POST", timePost.Source);

        // Absent: written without a Source, it reads back null (= additive TIME_POST).
        var absent = Assert.IsType<TimeEntryRegistered>(
            EventSerializer.Deserialize("TimeEntryRegistered", EventSerializer.Serialize(Event(null))));
        Assert.Null(absent.Source);

        // A genuine PRE-S145 payload (no `source` key at all) deserializes, to null.
        const string preS145 =
            """{"eventId":"11111111-1111-1111-1111-111111111111","occurredAt":"2026-09-14T08:00:00Z","eventType":"TimeEntryRegistered","version":1,"employeeId":"EMP-1","date":"2026-09-14","hours":7.4,"taskId":"TASK-1","agreementCode":"HK","okVersion":"OK26","voluntaryUnsocialHours":false}""";
        var legacy = Assert.IsType<TimeEntryRegistered>(EventSerializer.Deserialize("TimeEntryRegistered", preS145));
        Assert.Null(legacy.Source);
        Assert.Equal(7.40m, legacy.Hours);
    }

    [Fact]
    public void AbsenceRegistered_Source_RoundTrips_AndIsNullWhenAbsent()
    {
        AbsenceRegistered Event(string? source) => new()
        {
            EmployeeId = "EMP-1",
            Date = new DateOnly(2026, 9, 9),
            AbsenceType = AbsenceTypes.Vacation,
            Hours = 7.40m,
            AgreementCode = "HK",
            OkVersion = "OK26",
            Feriedage = 1.0m,
            Source = source,
        };

        var skema = Assert.IsType<AbsenceRegistered>(
            EventSerializer.Deserialize("AbsenceRegistered", EventSerializer.Serialize(Event("SKEMA"))));
        Assert.Equal("SKEMA", skema.Source);
        Assert.Equal(1.0m, skema.Feriedage); // the neighbouring optional member is undisturbed

        var timePost = Assert.IsType<AbsenceRegistered>(
            EventSerializer.Deserialize("AbsenceRegistered", EventSerializer.Serialize(Event("TIME_POST"))));
        Assert.Equal("TIME_POST", timePost.Source);

        var absent = Assert.IsType<AbsenceRegistered>(
            EventSerializer.Deserialize("AbsenceRegistered", EventSerializer.Serialize(Event(null))));
        Assert.Null(absent.Source);

        const string preS145 =
            """{"eventId":"22222222-2222-2222-2222-222222222222","occurredAt":"2026-09-09T08:00:00Z","eventType":"AbsenceRegistered","version":1,"employeeId":"EMP-1","date":"2026-09-09","absenceType":"VACATION","hours":7.4,"agreementCode":"HK","okVersion":"OK26","feriedage":1.0}""";
        var legacy = Assert.IsType<AbsenceRegistered>(EventSerializer.Deserialize("AbsenceRegistered", preS145));
        Assert.Null(legacy.Source);
        Assert.Equal(AbsenceTypes.Vacation, legacy.AbsenceType);
    }

    [Fact]
    public void HoursSource_Constants_AndResolve_NullIsTimePost()
    {
        // TASK-14500 Do 3: the two legal values, and "null means TIME_POST (additive)".
        Assert.Equal("SKEMA", HoursSource.Skema);
        Assert.Equal("TIME_POST", HoursSource.TimePost);
        Assert.Equal("TIME_POST", HoursSource.Resolve(null));
        Assert.Equal("SKEMA", HoursSource.Resolve("SKEMA"));
        Assert.Equal("TIME_POST", HoursSource.Resolve("TIME_POST"));
    }

    // -------------------------------------------------------------------------------------------
    // SegmentManifestCreated.PlanInputs (every member, SettlementCutoff set and null)
    // -------------------------------------------------------------------------------------------

    private static readonly Guid EntryEventA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid EntryEventB = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid AbsenceEventA = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static PlanInputs SamplePlanInputs(DateOnly? settlementCutoff) => new(
        // Pieces: a context day (fraction 1.0, no position) and the month (0.811, a position).
        new[]
        {
            new FrozenPiece(new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), 1.0m, "HK", null),
            new FrozenPiece(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 0.811m, "AC", "DEPARTMENT_HEAD"),
        },
        settlementCutoff,
        95.25m,                                            // OpeningFlexBalance
        new[] { EntryEventA, EntryEventB },                // EntryEventIds
        new[] { AbsenceEventA },                           // AbsenceEventIds
        4_000_000_001L,                                    // AsOfOutboxId (beyond Int32, so a narrowing shows)
        new string('a', 64),                               // EntriesDigest
        new string('b', 64),                               // AbsencesDigest
        new[] { new string('c', 64), new string('d', 64) },// ConfigFingerprints
        "weekly-timeline-v1");                             // EvaluationModel

    private static SegmentManifestCreated Manifest(PlanInputs? planInputs) => new()
    {
        ManifestId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        EmployeeId = "EMP-1",
        PeriodStart = new DateOnly(2026, 9, 1),
        PeriodEnd = new DateOnly(2026, 9, 30),
        CalculationKind = "forward-calc",
        CreatedAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero),
        PlanInputs = planInputs,
    };

    private static void AssertPlanInputsEqual(PlanInputs expected, PlanInputs? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected.Pieces, actual!.Pieces);                     // FrozenPiece is a record: element equality
        Assert.Equal(expected.SettlementCutoff, actual.SettlementCutoff);
        Assert.Equal(expected.OpeningFlexBalance, actual.OpeningFlexBalance);
        Assert.Equal(expected.EntryEventIds, actual.EntryEventIds);
        Assert.Equal(expected.AbsenceEventIds, actual.AbsenceEventIds);
        Assert.Equal(expected.AsOfOutboxId, actual.AsOfOutboxId);
        Assert.Equal(expected.EntriesDigest, actual.EntriesDigest);
        Assert.Equal(expected.AbsencesDigest, actual.AbsencesDigest);
        Assert.Equal(expected.ConfigFingerprints, actual.ConfigFingerprints);
        Assert.Equal(expected.EvaluationModel, actual.EvaluationModel);
    }

    [Fact]
    public void SegmentManifestCreated_PlanInputs_RoundTrips_AndIsNullWhenAbsent()
    {
        // Every member round-trips, with the settlement cutoff SET (a leaver's last day, R41)…
        var withCutoff = SamplePlanInputs(new DateOnly(2026, 9, 29));
        var back = Assert.IsType<SegmentManifestCreated>(
            EventSerializer.Deserialize("SegmentManifestCreated", EventSerializer.Serialize(Manifest(withCutoff))));
        AssertPlanInputsEqual(withCutoff, back.PlanInputs);
        Assert.Equal(new DateOnly(2026, 9, 29), back.PlanInputs!.SettlementCutoff);

        // …and with it NULL (a stayer): the rest of the record still round-trips, the cutoff stays null.
        var withoutCutoff = SamplePlanInputs(null);
        var backNull = Assert.IsType<SegmentManifestCreated>(
            EventSerializer.Deserialize("SegmentManifestCreated", EventSerializer.Serialize(Manifest(withoutCutoff))));
        AssertPlanInputsEqual(withoutCutoff, backNull.PlanInputs);
        Assert.Null(backNull.PlanInputs!.SettlementCutoff);

        // Absent: an event written without PlanInputs reads back null (and is not required)…
        var absent = Assert.IsType<SegmentManifestCreated>(
            EventSerializer.Deserialize("SegmentManifestCreated", EventSerializer.Serialize(Manifest(null))));
        Assert.Null(absent.PlanInputs);
        Assert.Equal("EMP-1", absent.EmployeeId);

        // …including a genuine PRE-S145 payload with no `planInputs` key at all.
        const string preS145 =
            """{"eventId":"44444444-4444-4444-4444-444444444444","occurredAt":"2026-09-30T12:00:00Z","eventType":"SegmentManifestCreated","version":1,"manifestId":"33333333-3333-3333-3333-333333333333","employeeId":"EMP-1","periodStart":"2026-09-01","periodEnd":"2026-09-30","calculationKind":"forward-calc","boundaryCauseSummary":[],"createdAt":"2026-09-30T12:00:00+00:00","segments":[]}""";
        var legacy = Assert.IsType<SegmentManifestCreated>(EventSerializer.Deserialize("SegmentManifestCreated", preS145));
        Assert.Null(legacy.PlanInputs);
        Assert.Equal("forward-calc", legacy.CalculationKind);
    }

    [Fact]
    public void SegmentManifest_PlanInputs_DefaultsToNull_SoExistingConstructionSitesCompileUnchanged()
    {
        // TASK-14500 Do 5: SegmentManifest gains a TRAILING OPTIONAL positional PlanInputs, so the eight-argument
        // construction sites (PeriodCalculationService, EmploymentTruncationAlignmentTests) are unchanged.
        var eightArguments = new SegmentManifest(
            Guid.NewGuid(),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30),
            "EMP-1",
            "forward-calc",
            Array.Empty<string>(),
            new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero),
            Array.Empty<PlannedSegment>());
        Assert.Null(eightArguments.PlanInputs);

        var planInputs = SamplePlanInputs(new DateOnly(2026, 9, 29));
        var nineArguments = eightArguments with { PlanInputs = planInputs };
        AssertPlanInputsEqual(planInputs, nineArguments.PlanInputs);
    }

    // -------------------------------------------------------------------------------------------
    // RetroactiveCorrectionRequested.NextPeriodToRecalculateStart / …End
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void RetroactiveCorrectionRequested_NextPeriod_RoundTrips_AndIsNullWhenAbsent()
    {
        RetroactiveCorrectionRequested Event(DateOnly? nextStart, DateOnly? nextEnd) => new()
        {
            EmployeeId = "EMP-1",
            OriginalPeriodStart = new DateOnly(2026, 9, 1),
            OriginalPeriodEnd = new DateOnly(2026, 9, 30),
            AgreementCode = "HK",
            OkVersion = "OK26",
            Reason = "S145 round-trip",
            CorrectedByActorId = "ADMIN-1",
            CorrectionLineCount = 2,
            TotalDifferenceHours = 1.80m,
            NextPeriodToRecalculateStart = nextStart,
            NextPeriodToRecalculateEnd = nextEnd,
        };

        // Q7 (b): the next PAY period whose settled week this recalculation changed (pay-period dates).
        var set = Assert.IsType<RetroactiveCorrectionRequested>(
            EventSerializer.Deserialize(
                "RetroactiveCorrectionRequested",
                EventSerializer.Serialize(Event(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)))));
        Assert.Equal(new DateOnly(2026, 10, 1), set.NextPeriodToRecalculateStart);
        Assert.Equal(new DateOnly(2026, 10, 31), set.NextPeriodToRecalculateEnd);
        Assert.Equal(1.80m, set.TotalDifferenceHours); // an unrelated member is undisturbed

        // Absent: both read back null.
        var absent = Assert.IsType<RetroactiveCorrectionRequested>(
            EventSerializer.Deserialize("RetroactiveCorrectionRequested", EventSerializer.Serialize(Event(null, null))));
        Assert.Null(absent.NextPeriodToRecalculateStart);
        Assert.Null(absent.NextPeriodToRecalculateEnd);

        // A genuine PRE-S145 payload (neither key present) deserializes, to null.
        const string preS145 =
            """{"eventId":"55555555-5555-5555-5555-555555555555","occurredAt":"2026-09-30T12:00:00Z","eventType":"RetroactiveCorrectionRequested","version":1,"employeeId":"EMP-1","originalPeriodStart":"2026-09-01","originalPeriodEnd":"2026-09-30","agreementCode":"HK","okVersion":"OK26","reason":"legacy","correctedByActorId":"ADMIN-1","correctionLineCount":2,"totalDifferenceHours":1.8}""";
        var legacy = Assert.IsType<RetroactiveCorrectionRequested>(
            EventSerializer.Deserialize("RetroactiveCorrectionRequested", preS145));
        Assert.Null(legacy.NextPeriodToRecalculateStart);
        Assert.Null(legacy.NextPeriodToRecalculateEnd);
        Assert.Equal("legacy", legacy.Reason);
    }
}
