using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Segmentation;

/// <summary>
/// S145 / TASK-14513 (adjudicator R-18) — a stored <see cref="PlanInputs"/> that is missing a required member is
/// a damaged record and must fail to decode, instead of quietly reading the missing member as a default.
///
/// <para>
/// Plain language: the frozen inputs of a calculation are stored as JSON. A replay trusts them completely. If the
/// opening flex balance (a number) were missing from the stored JSON and the reader treated "missing" as 0, the
/// replay would re-run a DIFFERENT calculation than the one that was paid, and nothing would say so. R-18 marks
/// every member that must always be there as required, so a missing one throws a <see cref="JsonException"/>
/// (which the replay turns into the named refusal <c>replay-manifest-without-frozen-inputs</c>). Two members are
/// allowed to be absent: <see cref="PlanInputs.SettlementCutoff"/> (null for a stayer; R41) and
/// <see cref="FrozenPiece.Position"/> (null when no position applies); they are written as an omitted key and must
/// still decode.
/// </para>
///
/// <para>
/// <b>Serializer options.</b> The record is written the way <see cref="EventSerializer"/> writes the event that
/// carries it (camelCase names, string enums, <c>DefaultIgnoreCondition = WhenWritingNull</c>; the test goes through
/// <c>EventSerializer.Serialize</c> itself, so the JSON is the real one) and read with the options the projection
/// reader uses (<c>PeriodCalculationService.JsonOptions</c>: camelCase, case-insensitive, string enum converter
/// accepting integers). <c>[JsonRequired]</c> does not depend on options, so the event path
/// (<c>EventSerializer.Deserialize</c>) is pinned too.
/// </para>
///
/// <para>
/// <b>Red conditions.</b> The two <c>Throws</c> facts fail today (<c>b25bf24</c>: no required markers, so a missing
/// member deserializes to its default and <c>Assert.Throws</c> reports "No exception was thrown"); after the
/// fix-up they fail under the mutation "drop <c>[property: JsonRequired]</c> from
/// <c>PlanInputs.OpeningFlexBalance</c>" / "from <c>FrozenPiece.PartTimeFraction</c>". The two guards are green
/// before and after; they fail under the mutation "mark <c>SettlementCutoff</c> or <c>Position</c> required" and
/// "drop the object check (accept <c>[]</c>)" respectively.
/// </para>
/// </summary>
public sealed class PlanInputsRequiredMembersTests
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true) },
    };

    private static PlanInputs Complete() => new(
        Pieces: new[]
        {
            new FrozenPiece(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 0.8m, "AC", "DEPARTMENT_HEAD"),
        },
        SettlementCutoff: new DateOnly(2026, 3, 31),
        OpeningFlexBalance: 12.5m,
        EntryEventIds: new[] { Guid.Parse("00000000-0000-0000-0000-000000000001") },
        AbsenceEventIds: new[] { Guid.Parse("00000000-0000-0000-0000-000000000002") },
        AsOfOutboxId: 42,
        EntriesDigest: "ab12",
        AbsencesDigest: "cd34",
        ConfigFingerprints: new[] { "fp-1" },
        EvaluationModel: "weekly-timeline-v1");

    /// <summary>The event carrying <paramref name="inputs"/>, serialized by the real <see cref="EventSerializer"/>.</summary>
    private static JsonObject SerializedEvent(PlanInputs inputs)
    {
        var evt = new SegmentManifestCreated
        {
            ManifestId = Guid.Parse("00000000-0000-0000-0000-0000000000aa"),
            EmployeeId = "EMP-R18",
            PeriodStart = new DateOnly(2026, 3, 1),
            PeriodEnd = new DateOnly(2026, 3, 31),
            CalculationKind = "forward-calc",
            CreatedAt = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            Segments = Array.Empty<PlannedSegment>(),
            PlanInputs = inputs,
        };
        return JsonNode.Parse(EventSerializer.Serialize(evt))!.AsObject();
    }

    private static JsonObject PlanInputsNode(PlanInputs inputs) =>
        SerializedEvent(inputs)["planInputs"]!.AsObject();

    private static PlanInputs Read(JsonNode node) =>
        JsonSerializer.Deserialize<PlanInputs>(node.ToJsonString(), ReadOptions)!;

    [Fact]
    public void Deserialize_CompleteRecord_RoundTripsOpeningBalance()
    {
        // S145 R-18: the control. Without it a green Throws could be a broken fixture.
        var read = Read(PlanInputsNode(Complete()));

        Assert.Equal(12.5m, read.OpeningFlexBalance);
        Assert.Equal(new DateOnly(2026, 3, 31), read.SettlementCutoff);
        var piece = Assert.Single(read.Pieces);
        Assert.Equal(0.8m, piece.PartTimeFraction);
        Assert.Equal("AC", piece.AgreementCode);
        Assert.Equal("DEPARTMENT_HEAD", piece.Position);
    }

    [Fact]
    public void Deserialize_MissingOpeningFlexBalance_Throws()
    {
        // S145 R-18:
        var node = PlanInputsNode(Complete());
        Assert.True(node.Remove("openingFlexBalance"));

        Assert.Throws<JsonException>(() => Read(node));
    }

    [Fact]
    public void Deserialize_PieceMissingPartTimeFraction_Throws()
    {
        // S145 R-18:
        var node = PlanInputsNode(Complete());
        var piece = node["pieces"]!.AsArray()[0]!.AsObject();
        Assert.True(piece.Remove("partTimeFraction"));

        Assert.Throws<JsonException>(() => Read(node));
    }

    [Fact]
    public void Deserialize_MissingOpeningFlexBalance_ThroughTheEventSerializer_Throws()
    {
        // S145 R-18: the event is the record of truth, so its read path is pinned too.
        var evt = SerializedEvent(Complete());
        Assert.True(evt["planInputs"]!.AsObject().Remove("openingFlexBalance"));

        Assert.Throws<JsonException>(
            () => EventSerializer.Deserialize("SegmentManifestCreated", evt.ToJsonString()));
    }

    [Fact]
    public void Deserialize_OmittedSettlementCutoffAndPiecePosition_DeserializesWithNull()
    {
        // S145 R-18 (guard): a null cutoff (R41) and a null position are written as omitted keys and are legitimate.
        var withNulls = Complete() with
        {
            SettlementCutoff = null,
            Pieces = new[] { new FrozenPiece(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 1m, "HK", null) },
        };
        var node = PlanInputsNode(withNulls);
        // The omission is real, not assumed.
        Assert.False(node.ContainsKey("settlementCutoff"));
        Assert.False(node["pieces"]!.AsArray()[0]!.AsObject().ContainsKey("position"));

        var read = Read(node);

        Assert.Null(read.SettlementCutoff);
        Assert.Null(Assert.Single(read.Pieces).Position);
        Assert.Equal(12.5m, read.OpeningFlexBalance);
    }

    [Fact]
    public void Deserialize_WrongShape_Throws()
    {
        // S145 R-18 (guard): an array where the object belongs is not frozen inputs.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PlanInputs>("[]", ReadOptions));
    }
}
