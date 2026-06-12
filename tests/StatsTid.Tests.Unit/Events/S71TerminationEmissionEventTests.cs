using System.Reflection;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Events;

/// <summary>
/// S71 / ADR-033 slice 3b (TASK-7101, SPRINT-71 R2/R6/R10) — EventSerializer round-trip tests for
/// the two new termination-emission events (<see cref="TerminationPayoutRequested"/>,
/// <see cref="TerminationClaimWaived"/>) and the R10-EXTENDED <see cref="SettlementReversed"/>
/// payload (define-only since S68; extended before first emission). Registration coverage itself
/// is guarded generically by <see cref="EventSerializerCoverageTests"/> (DEP-003); these tests pin
/// FIELD preservation, mirroring <see cref="S70TerminationFoundationEventTests"/>, plus an
/// explicit registration-presence pin for the three S71 discriminators.
/// The §7 deduct-in-full <c>TerminationModregningApplied</c> event is PARKED behind the
/// SLS-dialogue task (slice Step-0 gate (i)) — deliberately NOT defined, registered, or tested.
/// </summary>
public class S71TerminationEmissionEventTests
{
    // ── Registration presence (the three S71 discriminators) ───────────────────────

    [Fact]
    public void EventSerializer_Registers_S71TerminationEmissionEvents()
    {
        // Same private-map reflection route as EventSerializerCoverageTests — pins that the
        // exact S71 discriminators map to the exact types (the generic coverage test would
        // flag a MISSING registration, but not which sprint's contract it broke).
        var mapField = typeof(EventSerializer).GetField(
            "EventTypeMap",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(mapField);
        var map = (IReadOnlyDictionary<string, Type>)mapField!.GetValue(null)!;

        Assert.True(map.TryGetValue("TerminationPayoutRequested", out var requested));
        Assert.Equal(typeof(TerminationPayoutRequested), requested);
        Assert.True(map.TryGetValue("TerminationClaimWaived", out var waived));
        Assert.Equal(typeof(TerminationClaimWaived), waived);
        Assert.True(map.TryGetValue("SettlementReversed", out var reversed));
        Assert.Equal(typeof(SettlementReversed), reversed);
    }

    // ── TerminationPayoutRequested (§26 anmodning, R6) ──────────────────────────────

    [Fact]
    public void EventSerializer_RoundTrip_TerminationPayoutRequested_PreservesAllFields()
    {
        var correlationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 9, 14, 9, 15, 0, DateTimeKind.Utc);

        var original = new TerminationPayoutRequested
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "HR001",
            ActorRole = "HR",
            CorrelationId = correlationId,
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            SettlementSequence = 1,
            RequestDate = new DateOnly(2026, 9, 10),
            EvidenceNote = "Skriftlig anmodning modtaget pr. brev 2026-09-10.",
            CrystallizedDays = 11.25m,
            SettlementBoundaryDate = new DateOnly(2026, 7, 31),
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("TerminationPayoutRequested", json);

        var result = Assert.IsType<TerminationPayoutRequested>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("TerminationPayoutRequested", result.EventType);
        Assert.Equal("HR001", result.ActorId);
        Assert.Equal("HR", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal("EMP042", result.EmployeeId);
        Assert.Equal("VACATION", result.EntitlementType);
        Assert.Equal(2025, result.EntitlementYear);
        Assert.Equal(1, result.SettlementSequence);
        Assert.Equal(new DateOnly(2026, 9, 10), result.RequestDate);
        Assert.Equal("Skriftlig anmodning modtaget pr. brev 2026-09-10.", result.EvidenceNote);
        Assert.Equal(11.25m, result.CrystallizedDays);
        Assert.Equal(new DateOnly(2026, 7, 31), result.SettlementBoundaryDate);
    }

    [Fact]
    public void EventSerializer_RoundTrip_TerminationPayoutRequested_PreservesNullEvidenceNote()
    {
        // EvidenceNote is the one optional field (non-required nullable): the omitted-under-
        // WhenWritingNull JSON must deserialize back to null (S66 e0d1dc3 round-trippability lesson).
        var original = new TerminationPayoutRequested
        {
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            SettlementSequence = 3,
            RequestDate = new DateOnly(2026, 9, 10),
            EvidenceNote = null,
            CrystallizedDays = 2.08m,
            SettlementBoundaryDate = new DateOnly(2026, 7, 31),
        };

        var json = EventSerializer.Serialize(original);
        var result = Assert.IsType<TerminationPayoutRequested>(
            EventSerializer.Deserialize("TerminationPayoutRequested", json));

        Assert.Null(result.EvidenceNote);
        Assert.Equal(3, result.SettlementSequence);
        Assert.Equal(2.08m, result.CrystallizedDays);
    }

    // ── TerminationClaimWaived (§7 waive-in-full, R5/D-C) ───────────────────────────

    [Fact]
    public void EventSerializer_RoundTrip_TerminationClaimWaived_PreservesAllFields()
    {
        var correlationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 9, 14, 10, 0, 0, DateTimeKind.Utc);

        var original = new TerminationClaimWaived
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "HR001",
            ActorRole = "HR",
            CorrelationId = correlationId,
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            SettlementSequence = 1,
            WaivedDays = 3.5m,
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("TerminationClaimWaived", json);

        var result = Assert.IsType<TerminationClaimWaived>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("TerminationClaimWaived", result.EventType);
        Assert.Equal("HR001", result.ActorId);
        Assert.Equal("HR", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal("EMP042", result.EmployeeId);
        Assert.Equal("VACATION", result.EntitlementType);
        Assert.Equal(2025, result.EntitlementYear);
        Assert.Equal(1, result.SettlementSequence);
        Assert.Equal(3.5m, result.WaivedDays);
    }

    // ── SettlementReversed (R10 payload extension) ──────────────────────────────────

    [Fact]
    public void EventSerializer_RoundTrip_SettlementReversed_Superseded_PreservesAllFields()
    {
        // The SUPERSEDED variant carries the full optional surface: a successor sequence (R1's
        // next-generation 2g−1), the preserved D3 snapshot, and the nullable TERMINATION/claim
        // quantities — every field must survive the round trip.
        var correlationId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var occurredAt = new DateTime(2026, 10, 2, 14, 45, 0, DateTimeKind.Utc);

        var original = new SettlementReversed
        {
            EventId = eventId,
            OccurredAt = occurredAt,
            Version = 1,
            ActorId = "HR001",
            ActorRole = "HR",
            CorrelationId = correlationId,
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            SettlementSequence = 1,
            ReversalKind = SettlementReversed.ReversalKindSuperseded,
            SuccessorSequence = 3,
            Trigger = "TERMINATION",
            Snapshot = new VacationSettlementSnapshot
            {
                Earned = 18.75m,
                Used = 10m,
                CarryoverIn = 2.5m,
                OkVersion = "OK24",
                CrystallizedDays = 11.25m,
            },
            TransferDays = 0m,
            PayoutDays = 0m,
            ForfeitDays = 0m,
            CrystallizedDays = 11.25m,
            ClaimDispositionDays = 3.5m,
        };

        var json = EventSerializer.Serialize(original);
        var deserialized = EventSerializer.Deserialize("SettlementReversed", json);

        var result = Assert.IsType<SettlementReversed>(deserialized);
        Assert.Equal(eventId, result.EventId);
        Assert.Equal(occurredAt, result.OccurredAt);
        Assert.Equal("SettlementReversed", result.EventType);
        Assert.Equal("HR001", result.ActorId);
        Assert.Equal("HR", result.ActorRole);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.Equal("EMP042", result.EmployeeId);
        Assert.Equal("VACATION", result.EntitlementType);
        Assert.Equal(2025, result.EntitlementYear);
        Assert.Equal(1, result.SettlementSequence);
        Assert.Equal("SUPERSEDED", result.ReversalKind);
        Assert.Equal(3, result.SuccessorSequence);
        Assert.Equal("TERMINATION", result.Trigger);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(18.75m, result.Snapshot!.Earned);
        Assert.Equal(10m, result.Snapshot.Used);
        Assert.Equal(2.5m, result.Snapshot.CarryoverIn);
        Assert.Equal("OK24", result.Snapshot.OkVersion);
        Assert.Equal(11.25m, result.Snapshot.CrystallizedDays);
        Assert.Equal(0m, result.TransferDays);
        Assert.Equal(0m, result.PayoutDays);
        Assert.Equal(0m, result.ForfeitDays);
        Assert.Equal(11.25m, result.CrystallizedDays);
        Assert.Equal(3.5m, result.ClaimDispositionDays);
    }

    [Fact]
    public void EventSerializer_RoundTrip_SettlementReversed_Bare_PreservesNullOptionals()
    {
        // The BARE variant (no successor — the tuple parks behind the R3 marker): every nullable
        // optional (SuccessorSequence / Snapshot / CrystallizedDays / ClaimDispositionDays) is
        // omitted under WhenWritingNull and must deserialize back to null (S66 e0d1dc3 lesson).
        var original = new SettlementReversed
        {
            EmployeeId = "EMP042",
            EntitlementType = "VACATION",
            EntitlementYear = 2025,
            SettlementSequence = 1,
            ReversalKind = SettlementReversed.ReversalKindBare,
            SuccessorSequence = null,
            Trigger = "YEAR_END",
            Snapshot = null,
            TransferDays = 0m,
            PayoutDays = 4.17m,
            ForfeitDays = 0m,
            CrystallizedDays = null,
            ClaimDispositionDays = null,
        };

        var json = EventSerializer.Serialize(original);
        var result = Assert.IsType<SettlementReversed>(
            EventSerializer.Deserialize("SettlementReversed", json));

        Assert.Equal("BARE", result.ReversalKind);
        Assert.Null(result.SuccessorSequence);
        Assert.Equal("YEAR_END", result.Trigger);
        Assert.Null(result.Snapshot);
        Assert.Equal(0m, result.TransferDays);
        Assert.Equal(4.17m, result.PayoutDays);
        Assert.Equal(0m, result.ForfeitDays);
        Assert.Null(result.CrystallizedDays);
        Assert.Null(result.ClaimDispositionDays);
    }
}
