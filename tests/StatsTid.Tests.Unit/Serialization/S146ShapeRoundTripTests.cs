using System.Text.Json;
using System.Text.Json.Nodes;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Serialization;

/// <summary>
/// S146 / TASK-14606 (AC-C7; C1, R-9, R-33 item 2) — the optional members the flex ledger adds to four persisted or
/// serialized shapes must read back as <b>null</b> from a payload written before they existed, and must round-trip
/// exactly when set.
///
/// <para>
/// <b>Plain language.</b> The system never edits an event once written (event sourcing: the log is the record of truth
/// and the tables are rebuilt from it). S146 records, for every paid month, the flex balance it opened at, the balance
/// it closed at and where the opening came from. Those facts ride on the export event, the correction event, the
/// calculation result and (for the provenance) the frozen plan inputs a replay reads. Every new member is optional, so
/// an event written last year must still be read, with the new members empty. If a serializer attribute ever swallowed a
/// member, the event would be written happily and the fact would be gone at audit or replay time, so each fact below
/// writes a value, reads it back and compares it; and, for the old shape, it takes a serialized payload and REMOVES the
/// new keys before reading (a genuine "payload without the members", not merely a payload where they were null).
/// </para>
///
/// <para>
/// <b>Kind: R/N.</b> The with-members round trips and the old-payload reads name members this sprint creates, so they are
/// N (RED by MUTATION at the wave gate: a <c>[JsonIgnore]</c> on each new member trips the matching fact; the table is in
/// the TASK-14606 report). The old-payload reads are also R (the existing "optional, never required" behaviour is kept):
/// they would trip if a new member were made <c>[JsonRequired]</c> (the mutation named in the table).
/// </para>
/// </summary>
public sealed class S146ShapeRoundTripTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string RemoveKeys(string json, params string[] keys) => RemoveKeysAt(json, null, keys);

    /// <summary>Removes <paramref name="keys"/> from the object at <paramref name="path"/> (null = the root).</summary>
    private static string RemoveKeysAt(string json, string? path, params string[] keys)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var target = path is null ? root : root[path]!.AsObject();
        foreach (var key in keys)
            Assert.True(target.Remove(key), $"the serialized payload must carry '{key}' before it is removed");
        return root.ToJsonString();
    }

    // -------------------------------------------------------------------------------------------
    // PayrollExportGenerated: openingFlexBalance, closingFlexBalance, flexProvenance
    // -------------------------------------------------------------------------------------------

    private static PayrollExportGenerated Export(decimal? opening, decimal? closing, string? provenance, Guid? manifestId = null) => new()
    {
        EmployeeId = "EMP-1",
        Year = 2026,
        Month = 6,
        ExportId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ContentHash = "abc123",
        ExportedAt = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero),
        OpeningFlexBalance = opening,
        ClosingFlexBalance = closing,
        FlexProvenance = provenance,
        ManifestId = manifestId,
    };

    /// <summary>
    /// The export event carries the month's recorded opening, closing and provenance and reads them back exactly (a
    /// negative closing and a fractional opening, so a sign or scale loss shows). Kind N. Red conditions: all three equal
    /// after the round trip; a <c>[JsonIgnore]</c> on any of the three makes it read null.
    /// </summary>
    [Fact]
    public void PayrollExportGenerated_FlexTriple_RoundTrips()
    {
        var back = Assert.IsType<PayrollExportGenerated>(
            EventSerializer.Deserialize("PayrollExportGenerated", EventSerializer.Serialize(Export(12.25m, -3.5m, "CHAINED"))));

        Assert.Equal(12.25m, back.OpeningFlexBalance);
        Assert.Equal(-3.5m, back.ClosingFlexBalance);
        Assert.Equal("CHAINED", back.FlexProvenance);
        Assert.Equal("abc123", back.ContentHash); // an unrelated member is undisturbed
    }

    /// <summary>
    /// A pre-S146 export event (the three keys absent) deserializes, with the three members null; a raw export (nulls) too.
    /// Kind R/N. Red conditions: no exception and three nulls; a member made required throws.
    /// </summary>
    [Fact]
    public void PayrollExportGenerated_WithoutFlexTriple_ReadsNull()
    {
        var oldPayload = RemoveKeys(
            EventSerializer.Serialize(Export(12.25m, -3.5m, "CHAINED", Guid.Parse("55555555-5555-5555-5555-555555555555"))),
            "openingFlexBalance", "closingFlexBalance", "flexProvenance", "manifestId");

        var back = Assert.IsType<PayrollExportGenerated>(EventSerializer.Deserialize("PayrollExportGenerated", oldPayload));

        Assert.Null(back.OpeningFlexBalance);
        Assert.Null(back.ClosingFlexBalance);
        Assert.Null(back.FlexProvenance);
        Assert.Null(back.ManifestId); // R-54: a pre-fix event names no manifest
        Assert.Equal("EMP-1", back.EmployeeId);
    }

    /// <summary>
    /// S146 Step 7a (R-54, TASK-14610-T) — the export event names its manifest and reads it back exactly. Kind N
    /// (names <c>PayrollExportGenerated.ManifestId</c>). Red conditions: equals the Guid after the round trip.
    /// MUTATION: a <c>[JsonIgnore]</c> on the member reads null, failing the equality.
    /// </summary>
    [Fact]
    public void PayrollExportGenerated_ManifestId_RoundTrips()
    {
        var manifest = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var back = Assert.IsType<PayrollExportGenerated>(EventSerializer.Deserialize(
            "PayrollExportGenerated", EventSerializer.Serialize(Export(12.25m, -3.5m, "CHAINED", manifest))));

        Assert.Equal(manifest, back.ManifestId);
    }

    // -------------------------------------------------------------------------------------------
    // RetroactiveCorrectionRequested: openingFlexBalanceUsed, closingFlexBalanceBefore/After, flexProvenance
    // -------------------------------------------------------------------------------------------

    private static RetroactiveCorrectionRequested Correction(
        decimal? openingUsed, decimal? before, decimal? after, string? provenance) => new()
    {
        EmployeeId = "EMP-1",
        OriginalPeriodStart = new DateOnly(2026, 6, 1),
        OriginalPeriodEnd = new DateOnly(2026, 6, 30),
        AgreementCode = "HK",
        OkVersion = "OK26",
        Reason = "S146 round-trip",
        CorrectedByActorId = "ADMIN-1",
        CorrectionLineCount = 2,
        TotalDifferenceHours = 1.80m,
        OpeningFlexBalanceUsed = openingUsed,
        ClosingFlexBalanceBefore = before,
        ClosingFlexBalanceAfter = after,
        FlexProvenance = provenance,
    };

    /// <summary>
    /// The correction event carries the opening used, the closing before and after, and the provenance (UNTRACKED is
    /// allowed here), and reads them back exactly. Kind N. Red conditions: all four equal; a <c>[JsonIgnore]</c> on any
    /// reads null.
    /// </summary>
    [Fact]
    public void RetroactiveCorrectionRequested_FlexMembers_RoundTrip()
    {
        var back = Assert.IsType<RetroactiveCorrectionRequested>(
            EventSerializer.Deserialize("RetroactiveCorrectionRequested", EventSerializer.Serialize(Correction(4.5m, 10m, 12.75m, "REHIRE"))));

        Assert.Equal(4.5m, back.OpeningFlexBalanceUsed);
        Assert.Equal(10m, back.ClosingFlexBalanceBefore);
        Assert.Equal(12.75m, back.ClosingFlexBalanceAfter);
        Assert.Equal("REHIRE", back.FlexProvenance);
        Assert.Equal(1.80m, back.TotalDifferenceHours);

        var untracked = Assert.IsType<RetroactiveCorrectionRequested>(
            EventSerializer.Deserialize("RetroactiveCorrectionRequested", EventSerializer.Serialize(Correction(null, null, null, "UNTRACKED"))));
        Assert.Equal("UNTRACKED", untracked.FlexProvenance);
        Assert.Null(untracked.ClosingFlexBalanceAfter);
    }

    /// <summary>
    /// A pre-S146 correction event (the four keys absent) deserializes, with the four members null. Kind R/N. Red
    /// conditions: no exception and four nulls.
    /// </summary>
    [Fact]
    public void RetroactiveCorrectionRequested_WithoutFlexMembers_ReadsNull()
    {
        var oldPayload = RemoveKeys(
            EventSerializer.Serialize(Correction(4.5m, 10m, 12.75m, "REHIRE")),
            "openingFlexBalanceUsed", "closingFlexBalanceBefore", "closingFlexBalanceAfter", "flexProvenance");

        var back = Assert.IsType<RetroactiveCorrectionRequested>(EventSerializer.Deserialize("RetroactiveCorrectionRequested", oldPayload));

        Assert.Null(back.OpeningFlexBalanceUsed);
        Assert.Null(back.ClosingFlexBalanceBefore);
        Assert.Null(back.ClosingFlexBalanceAfter);
        Assert.Null(back.FlexProvenance);
        Assert.Equal("S146 round-trip", back.Reason);
    }

    // -------------------------------------------------------------------------------------------
    // PeriodCalculationResult: OpeningFlexBalance, ClosingFlexBalance, FlexProvenance
    // -------------------------------------------------------------------------------------------

    private static PeriodCalculationResult Result(decimal? opening, decimal? closing, string? provenance) => new()
    {
        EmployeeId = "EMP-1",
        PeriodStart = new DateOnly(2026, 6, 1),
        PeriodEnd = new DateOnly(2026, 6, 30),
        AgreementCode = "HK",
        OkVersion = "OK26",
        RuleResults = Array.Empty<CalculationResult>(),
        ExportLines = Array.Empty<PayrollExportLine>(),
        Success = true,
        OpeningFlexBalance = opening,
        ClosingFlexBalance = closing,
        FlexProvenance = provenance,
    };

    /// <summary>
    /// The calculation result's three flex members round-trip through the JSON the host writes. Kind N. Red conditions:
    /// the three equal after the round trip.
    /// </summary>
    [Fact]
    public void PeriodCalculationResult_FlexMembers_RoundTrip()
    {
        var json = JsonSerializer.Serialize(Result(0m, 37.5m, "LEDGER_START"), Web);

        var back = JsonSerializer.Deserialize<PeriodCalculationResult>(json, Web)!;

        Assert.Equal(0m, back.OpeningFlexBalance);
        Assert.Equal(37.5m, back.ClosingFlexBalance);
        Assert.Equal("LEDGER_START", back.FlexProvenance);
    }

    /// <summary>
    /// A result payload without the three keys deserializes with them null (a zero opening is NOT confused with absent).
    /// Kind R/N. Red conditions: no exception, three nulls; and, on the same shape written with a zero opening, the opening
    /// is 0 and not null.
    /// </summary>
    [Fact]
    public void PeriodCalculationResult_WithoutFlexMembers_ReadsNull()
    {
        var withZero = JsonSerializer.Serialize(Result(0m, 0m, "LEDGER_START"), Web);
        var oldPayload = RemoveKeys(withZero, "openingFlexBalance", "closingFlexBalance", "flexProvenance");

        var back = JsonSerializer.Deserialize<PeriodCalculationResult>(oldPayload, Web)!;

        Assert.Null(back.OpeningFlexBalance);
        Assert.Null(back.ClosingFlexBalance);
        Assert.Null(back.FlexProvenance);
        Assert.True(back.Success);
        Assert.Equal(0m, JsonSerializer.Deserialize<PeriodCalculationResult>(withZero, Web)!.OpeningFlexBalance);
    }

    // -------------------------------------------------------------------------------------------
    // PlanInputs.OpeningFlexProvenance (frozen on the manifest event; not in IsComplete)
    // -------------------------------------------------------------------------------------------

    private static PlanInputs SamplePlanInputs(string? provenance) => new(
        new[] { new FrozenPiece(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), 0.811m, "AC", "DEPARTMENT_HEAD") },
        null,
        95.25m,
        new[] { Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001") },
        Array.Empty<Guid>(),
        4_000_000_001L,
        new string('a', 64),
        new string('b', 64),
        new[] { new string('c', 64) },
        "weekly-timeline-v1")
    {
        OpeningFlexProvenance = provenance,
    };

    private static SegmentManifestCreated Manifest(PlanInputs? planInputs) => new()
    {
        ManifestId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        EmployeeId = "EMP-1",
        PeriodStart = new DateOnly(2026, 6, 1),
        PeriodEnd = new DateOnly(2026, 6, 30),
        CalculationKind = "forward-calc",
        CreatedAt = new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero),
        PlanInputs = planInputs,
    };

    /// <summary>
    /// The frozen provenance rides the manifest event's plan inputs and reads back exactly, for all four values the
    /// frozen record may hold (UNTRACKED included: a replay drops the payout as the forward run did), without disturbing
    /// the existing frozen opening balance. Kind N. Red conditions: provenance equal for each value and the balance
    /// still 95.25; a <c>[JsonIgnore]</c> on the member reads null.
    /// </summary>
    [Theory]
    [InlineData("LEDGER_START")]
    [InlineData("CHAINED")]
    [InlineData("REHIRE")]
    [InlineData("UNTRACKED")]
    public void PlanInputs_OpeningFlexProvenance_RoundTrips(string provenance)
    {
        var back = Assert.IsType<SegmentManifestCreated>(
            EventSerializer.Deserialize("SegmentManifestCreated", EventSerializer.Serialize(Manifest(SamplePlanInputs(provenance)))));

        Assert.Equal(provenance, back.PlanInputs!.OpeningFlexProvenance);
        Assert.Equal(95.25m, back.PlanInputs.OpeningFlexBalance);
    }

    /// <summary>
    /// A plan-inputs record stored before S146 (no <c>openingFlexProvenance</c> key) still decodes, with the provenance
    /// null and every other member intact (the member is optional, R-33 item 2, so no stored record becomes "damaged";
    /// the completeness check itself is private to the calculation service and is exercised by the replay facts). Kind
    /// R/N. Red conditions: no exception and provenance null; a member made <c>[JsonRequired]</c> throws.
    /// </summary>
    [Fact]
    public void PlanInputs_WithoutOpeningFlexProvenance_ReadsNull()
    {
        var oldPayload = RemoveKeysAt(
            EventSerializer.Serialize(Manifest(SamplePlanInputs("CHAINED"))), "planInputs", "openingFlexProvenance");

        var back = Assert.IsType<SegmentManifestCreated>(EventSerializer.Deserialize("SegmentManifestCreated", oldPayload));

        Assert.NotNull(back.PlanInputs);
        Assert.Null(back.PlanInputs!.OpeningFlexProvenance);
        Assert.Equal(95.25m, back.PlanInputs.OpeningFlexBalance);
    }
}
