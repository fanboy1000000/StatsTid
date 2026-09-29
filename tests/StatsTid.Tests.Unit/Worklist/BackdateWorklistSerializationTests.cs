using System.Text.Json;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Unit.Worklist;

/// <summary>
/// S138 / TASK-13803 — the two serialization contracts of the worklist, pinned field-by-field:
/// the <c>triggers</c> JSONB element codec (<see cref="WorklistTriggerJson"/> — camelCase keys the
/// refinement names, nulls OMITTED so an exported-month element carries no settlement keys and
/// vice versa) and the two events through the production <see cref="EventSerializer"/> (DEP-003 —
/// registration is covered by <c>EventSerializerCoverageTests</c>; this pins that the PAYLOAD
/// survives, incl. DateOnly + nullable members).
/// </summary>
public sealed class BackdateWorklistSerializationTests
{
    [Fact]
    public void TriggerJson_ExportedMonthElement_CamelCaseKeys_SettlementKeysOmitted_RoundTrips()
    {
        var eventId = Guid.NewGuid();
        var appendedAt = new DateTimeOffset(2026, 9, 3, 8, 15, 0, TimeSpan.Zero);
        var trigger = new StoredWorklistTrigger("PROFILE_CHANGE", eventId, new DateOnly(2026, 3, 15), appendedAt, "hr01", "h1", null, null);

        var json = WorklistTriggerJson.Serialize(new[] { trigger });

        using var doc = JsonDocument.Parse(json);
        var el = Assert.Single(doc.RootElement.EnumerateArray());
        Assert.Equal("PROFILE_CHANGE", el.GetProperty("kind").GetString());
        Assert.Equal(eventId, el.GetProperty("eventId").GetGuid());
        Assert.Equal("2026-03-15", el.GetProperty("effectiveFrom").GetString());
        Assert.Equal(appendedAt, el.GetProperty("appendedAt").GetDateTimeOffset());
        Assert.Equal("hr01", el.GetProperty("actorId").GetString());
        Assert.Equal("h1", el.GetProperty("baselineContentHash").GetString());
        Assert.False(el.TryGetProperty("baselineSettlementSequence", out _));
        Assert.False(el.TryGetProperty("baselineSettlementState", out _));

        var back = Assert.Single(WorklistTriggerJson.Deserialize(json));
        Assert.Equal(trigger, back);
    }

    [Fact]
    public void TriggerJson_SettledYearElement_HashOmitted_RoundTrips_AndArrayOrderPreserved()
    {
        var a = new StoredWorklistTrigger("AGREEMENT_CODE_CHANGE", Guid.NewGuid(), new DateOnly(2025, 3, 1),
            new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero), "hr01", null, 1, "SETTLED");
        var b = new StoredWorklistTrigger("EMPLOYMENT_CATEGORY_CHANGE", Guid.NewGuid(), new DateOnly(2025, 3, 5),
            new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero), "hr02", null, 2, "PENDING_REVIEW");

        var json = WorklistTriggerJson.Serialize(new[] { a, b });

        using var doc = JsonDocument.Parse(json);
        var elements = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(2, elements.Count);
        Assert.False(elements[0].TryGetProperty("baselineContentHash", out _));
        Assert.Equal(1, elements[0].GetProperty("baselineSettlementSequence").GetInt32());
        Assert.Equal("PENDING_REVIEW", elements[1].GetProperty("baselineSettlementState").GetString());

        Assert.Equal(new[] { a, b }, WorklistTriggerJson.Deserialize(json));
    }

    [Fact]
    public void TriggerJson_DeserializesTheHandWrittenShape_TheSpecNames()
    {
        // The exact element shape the refinement names — must read back regardless of key order.
        const string json =
            """[{"appendedAt":"2026-09-03T08:00:00+00:00","baselineContentHash":"h1","effectiveFrom":"2026-03-10","eventId":"11111111-1111-1111-1111-111111111111","kind":"PROFILE_CHANGE","actorId":"hr01"}]""";

        var t = Assert.Single(WorklistTriggerJson.Deserialize(json));
        Assert.Equal("PROFILE_CHANGE", t.Kind);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), t.EventId);
        Assert.Equal(new DateOnly(2026, 3, 10), t.EffectiveFrom);
        Assert.Equal("h1", t.BaselineContentHash);
        Assert.Null(t.BaselineSettlementSequence);
    }

    [Fact]
    public void EventSerializer_RoundTrip_BackdateWorklistRowCreated_PreservesAllFields()
    {
        var original = new BackdateWorklistRowCreated
        {
            EventId = Guid.NewGuid(),
            OccurredAt = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc),
            ActorId = "hr01",
            WorklistId = Guid.NewGuid(),
            EmployeeId = "emp1",
            Kind = "EXPORTED_MONTH",
            Year = 2026,
            Month = 3,
            ExportId = Guid.NewGuid(),
            TriggerKind = "PROFILE_CHANGE",
            TriggerEventId = Guid.NewGuid(),
            TriggerEffectiveFrom = new DateOnly(2026, 3, 15),
            BaselineContentHash = "h1",
            Appended = true,
            RowVersion = 2,
        };

        var json = EventSerializer.Serialize(original);
        var result = Assert.IsType<BackdateWorklistRowCreated>(EventSerializer.Deserialize("BackdateWorklistRowCreated", json));

        Assert.Equal(original.EventId, result.EventId);
        Assert.Equal(original.OccurredAt, result.OccurredAt);
        Assert.Equal("BackdateWorklistRowCreated", result.EventType);
        Assert.Equal("hr01", result.ActorId);
        Assert.Equal(original.WorklistId, result.WorklistId);
        Assert.Equal("emp1", result.EmployeeId);
        Assert.Equal("EXPORTED_MONTH", result.Kind);
        Assert.Equal(2026, result.Year);
        Assert.Equal(3, result.Month);
        Assert.Equal(original.ExportId, result.ExportId);
        Assert.Null(result.EntitlementType);
        Assert.Null(result.EntitlementYear);
        Assert.Equal("PROFILE_CHANGE", result.TriggerKind);
        Assert.Equal(original.TriggerEventId, result.TriggerEventId);
        Assert.Equal(new DateOnly(2026, 3, 15), result.TriggerEffectiveFrom);
        Assert.Equal("h1", result.BaselineContentHash);
        Assert.Null(result.BaselineSettlementSequence);
        Assert.True(result.Appended);
        Assert.Equal(2L, result.RowVersion);
    }

    [Fact]
    public void EventSerializer_RoundTrip_BackdateWorklistRowResolved_PreservesAllFields()
    {
        var original = new BackdateWorklistRowResolved
        {
            EventId = Guid.NewGuid(),
            ActorId = "hr01",
            ActorRole = "LocalHR",
            CorrelationId = Guid.NewGuid(),
            WorklistId = Guid.NewGuid(),
            EmployeeId = "emp1",
            Kind = "SETTLED_YEAR",
            EntitlementType = "VACATION",
            EntitlementYear = 2024,
            Resolution = "DISMISSED",
            Reason = "Not payroll-relevant",
            TriggerCount = 3,
            VersionBefore = 1,
            VersionAfter = 2,
        };

        var json = EventSerializer.Serialize(original);
        var result = Assert.IsType<BackdateWorklistRowResolved>(EventSerializer.Deserialize("BackdateWorklistRowResolved", json));

        Assert.Equal(original.EventId, result.EventId);
        Assert.Equal("BackdateWorklistRowResolved", result.EventType);
        Assert.Equal("LocalHR", result.ActorRole);
        Assert.Equal(original.CorrelationId, result.CorrelationId);
        Assert.Equal(original.WorklistId, result.WorklistId);
        Assert.Equal("SETTLED_YEAR", result.Kind);
        Assert.Null(result.Year);
        Assert.Null(result.ExportId);
        Assert.Equal("VACATION", result.EntitlementType);
        Assert.Equal(2024, result.EntitlementYear);
        Assert.Equal("DISMISSED", result.Resolution);
        Assert.Equal("Not payroll-relevant", result.Reason);
        Assert.Equal(3, result.TriggerCount);
        Assert.Equal(1L, result.VersionBefore);
        Assert.Equal(2L, result.VersionAfter);
    }

    // ── S144 / TASK-14405 — BlockedBy on BackdateWorklistRowResolved ─────────────────────────
    //
    // Three states are distinct and must survive the wire: null (a PRE-S144 event, never stamped),
    // [] (resolved with nothing blocked), and a non-empty set. Collapsing null and [] would make
    // history say "nothing was blocked" about events that never recorded it.

    private static BackdateWorklistRowResolved ResolvedEvent(IReadOnlyList<string>? blockedBy) => new()
    {
        EventId = Guid.NewGuid(),
        ActorId = "hr01",
        WorklistId = Guid.NewGuid(),
        EmployeeId = "emp1",
        Kind = "EXPORTED_MONTH",
        Year = 2026,
        Month = 8,
        Resolution = "HANDLED_MANUALLY",
        Reason = "Paid by hand",
        TriggerCount = 1,
        VersionBefore = 1,
        VersionAfter = 2,
        BlockedBy = blockedBy,
    };

    /// <summary>
    /// <c>BlockedBy</c> round-trips through the production <see cref="EventSerializer"/> as
    /// <c>null</c>, <c>[]</c> and <c>["QUAL-149","QUAL-150"]</c> (order preserved).
    ///
    /// Red conditions: mutation M-5 — <c>[JsonIgnore]</c> on
    /// <c>BackdateWorklistRowResolved.BlockedBy</c>. The two non-null cases then come back
    /// <c>null</c>, tripping <c>Assert.Equal(new[] { "QUAL-149", "QUAL-150" }, …)</c> (the set
    /// case runs first, so this is the assertion that fails).
    /// </summary>
    [Fact]
    public void EventSerializer_RoundTrip_BackdateWorklistRowResolved_BlockedBy_SetEmptyAndNull()
    {
        var set = Assert.IsType<BackdateWorklistRowResolved>(EventSerializer.Deserialize(
            "BackdateWorklistRowResolved", EventSerializer.Serialize(ResolvedEvent(new[] { "QUAL-149", "QUAL-150" }))));
        Assert.NotNull(set.BlockedBy);
        Assert.Equal(new[] { "QUAL-149", "QUAL-150" }, set.BlockedBy);

        var empty = Assert.IsType<BackdateWorklistRowResolved>(EventSerializer.Deserialize(
            "BackdateWorklistRowResolved", EventSerializer.Serialize(ResolvedEvent(Array.Empty<string>()))));
        Assert.NotNull(empty.BlockedBy);
        Assert.Empty(empty.BlockedBy!);

        var none = Assert.IsType<BackdateWorklistRowResolved>(EventSerializer.Deserialize(
            "BackdateWorklistRowResolved", EventSerializer.Serialize(ResolvedEvent(null))));
        Assert.Null(none.BlockedBy);
    }

    /// <summary>
    /// A PRE-S144 payload — the event JSON with no <c>blockedBy</c> key at all — deserializes to
    /// <c>null</c> (not <c>[]</c>, not an error). Built by serializing a stamped event and
    /// removing the key, so every other required member is real.
    ///
    /// Red conditions: no named mutation (a compatibility pin: it holds while <c>BlockedBy</c> is a
    /// nullable, non-required member). Goes red if the member becomes <c>required</c> (the
    /// deserialize throws) or defaults to an empty list (<c>Assert.Null</c> trips).
    /// </summary>
    [Fact]
    public void EventSerializer_Deserialize_BackdateWorklistRowResolved_JsonWithoutBlockedBy_YieldsNull()
    {
        var json = EventSerializer.Serialize(ResolvedEvent(new[] { "QUAL-149" }));
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        var key = node.Select(kv => kv.Key).Single(k => string.Equals(k, "blockedBy", StringComparison.OrdinalIgnoreCase));
        node.Remove(key);
        Assert.DoesNotContain("blockedBy", node.ToJsonString(), StringComparison.OrdinalIgnoreCase);

        var result = Assert.IsType<BackdateWorklistRowResolved>(
            EventSerializer.Deserialize("BackdateWorklistRowResolved", node.ToJsonString()));

        Assert.Null(result.BlockedBy);
        Assert.Equal("HANDLED_MANUALLY", result.Resolution);
    }
}
