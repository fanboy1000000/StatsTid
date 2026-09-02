using System.Reflection;
using System.Text.Json;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;
using StatsTid.Tests.Unit.Payroll;

namespace StatsTid.Tests.Unit.Segmentation;

/// <summary>
/// S137 / TASK-13705 — the manifest byte-parity pins for ADR-040 D5's typed segments,
/// locally runnable (no DB, no Docker).
///
/// <para>
/// Plain-language: <c>PlannedSegment</c> gained an <c>EmploymentStatus</c> field. The
/// safety claim of the whole increment is BY-CONSTRUCTION byte-parity — for a windowless
/// employee (every employee today), the serialized segment JSON must be IDENTICAL to
/// what the system wrote before the field existed, because
/// <c>[property: JsonIgnore(Condition = WhenWritingDefault)]</c> omits the key whenever
/// the value is EMPLOYED (pinned <c>== 0 == default</c>). These tests make that claim
/// falsifiable by freezing the PRE-CHANGE literals and asserting the post-change writers
/// still produce them byte-for-byte.
/// </para>
///
/// <para>
/// <b>Two writers, two literals — never one shared literal.</b> The manifest has two
/// JSON writers that legitimately differ on null-snapshot bytes: the PCS shared options
/// write <c>"snapshot":null</c> (no default-ignore condition), while
/// <c>EventSerializer</c> uses <c>WhenWritingNull</c> and OMITS the key — that byte
/// delta IS the QUAL-146 residual and must not be papered over by asserting a single
/// shared shape.
/// </para>
///
/// <para>
/// <b>The <c>[property:]</c> trap this suite exists to catch:</b> on a positional
/// record, a bare <c>[JsonIgnore]</c> attribute lands on the constructor PARAMETER and
/// does nothing — System.Text.Json only honors it on the generated property. If someone
/// "simplifies" the attribute target away, the EMPLOYED literals below grow an
/// <c>employmentStatus</c> key and these pins go red. Do not chase that red by updating
/// the literals — restore the <c>[property:]</c> target.
/// </para>
/// </summary>
public sealed class SegmentSerializationParityTests
{
    // ---------------------------------------------------------------------
    // Writer options
    // ---------------------------------------------------------------------

    // S137 Step-5a (Reviewer WARNING 3): THE REAL PeriodCalculationService.JsonOptions — the
    // production segments_jsonb writer — bound by reflection through the shared
    // PcsJsonOptions accessor. No replica, no "KEEP IN SYNC" promise: the PCS-writer literals
    // below are asserted against the bytes production actually writes, so a drift in the real
    // options (e.g. an added DefaultIgnoreCondition) turns these pins red instead of passing
    // silently against a stale copy. (The EventSerializer pins already used the real writer.)
    private static JsonSerializerOptions PcsWriter => PcsJsonOptions.Real;

    /// <summary>The binding itself is pinned: the options the PCS-writer literals are asserted
    /// against ARE the live private field — reference-identical, not a copy.</summary>
    [Fact]
    public void PcsWriterOptions_AreTheRealPeriodCalculationServiceJsonOptions_NotAReplica()
    {
        var realField = typeof(PeriodCalculationService)
            .GetField("JsonOptions", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(realField);
        Assert.Same(realField!.GetValue(null), PcsWriter);
    }

    // ---------------------------------------------------------------------
    // Frozen PRE-CHANGE literals (the exact shapes hand-written in
    // BoundaryCauseEncodingTests:103/:127 before S137 — do NOT regenerate
    // these from the code under test; their value is that they are frozen).
    // ---------------------------------------------------------------------

    // PCS writer: explicit "snapshot":null (no default-ignore condition on its options).
    private const string PcsEmployedLiteral =
        "{\"startDate\":\"2026-01-01\",\"endDate\":\"2026-01-31\",\"boundaryCause\":\"LocalProfileActivation\",\"snapshot\":null}";

    // EventSerializer writer: WhenWritingNull omits the null snapshot key entirely.
    private const string EventSerializerEmployedLiteral =
        "{\"startDate\":\"2026-01-01\",\"endDate\":\"2026-01-31\",\"boundaryCause\":\"LocalProfileActivation\"}";

    // The NOT_EMPLOYED shapes: same per-writer bytes plus the employmentStatus key
    // (trailing — declaration order on the positional record).
    private const string PcsNotEmployedLiteral =
        "{\"startDate\":\"2026-01-01\",\"endDate\":\"2026-01-31\",\"boundaryCause\":\"LocalProfileActivation\",\"snapshot\":null,\"employmentStatus\":\"NOT_EMPLOYED\"}";

    private const string EventSerializerNotEmployedLiteral =
        "{\"startDate\":\"2026-01-01\",\"endDate\":\"2026-01-31\",\"boundaryCause\":\"LocalProfileActivation\",\"employmentStatus\":\"NOT_EMPLOYED\"}";

    private static readonly DateOnly Jan01 = new(2026, 1, 1);
    private static readonly DateOnly Jan31 = new(2026, 1, 31);

    private static PlannedSegment Segment(EmploymentWindowStatus status) => new(
        Jan01, Jan31, BoundaryCause.LocalProfileActivation, Snapshot: null,
        EmploymentStatus: status);

    // ═════════════════════════════════════════════════════════════════════
    // Pin (i): an EMPLOYED segment serializes to the PRE-CHANGE bytes under
    // EACH writer — the by-construction parity proof (no employment key).
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void PcsWriter_EmployedSegment_ByteIdenticalToPreChangeLiteral()
    {
        var json = JsonSerializer.Serialize(
            Segment(EmploymentWindowStatus.EMPLOYED), PcsWriter);

        Assert.Equal(PcsEmployedLiteral, json);
    }

    [Fact]
    public void EventSerializerWriter_EmployedSegment_ByteIdenticalToPreChangeLiteral()
    {
        // The REAL production writer, not a replica: serialize a SegmentManifestCreated
        // event through EventSerializer and extract the segment element's raw bytes
        // (GetRawText returns the original input slice verbatim).
        var json = EventSerializer.Serialize(
            ManifestEvent(Segment(EmploymentWindowStatus.EMPLOYED)));

        using var doc = JsonDocument.Parse(json);
        var segmentJson = doc.RootElement.GetProperty("segments")[0].GetRawText();

        Assert.Equal(EventSerializerEmployedLiteral, segmentJson);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Pin (ii): a NOT_EMPLOYED segment DOES serialize the key under each
    // writer — the field is falsifiable, not decorative.
    // ═════════════════════════════════════════════════════════════════════

    [Fact]
    public void PcsWriter_NotEmployedSegment_CarriesEmploymentStatusKey()
    {
        var json = JsonSerializer.Serialize(
            Segment(EmploymentWindowStatus.NOT_EMPLOYED), PcsWriter);

        Assert.Equal(PcsNotEmployedLiteral, json);
    }

    [Fact]
    public void EventSerializerWriter_NotEmployedSegment_CarriesEmploymentStatusKey()
    {
        var json = EventSerializer.Serialize(
            ManifestEvent(Segment(EmploymentWindowStatus.NOT_EMPLOYED)));

        using var doc = JsonDocument.Parse(json);
        var segmentJson = doc.RootElement.GetProperty("segments")[0].GetRawText();

        Assert.Equal(EventSerializerNotEmployedLiteral, segmentJson);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Pin (iii), local half: pre-D5 JSON (no employmentStatus key) replays
    // to EMPLOYED under both readers — the ADR-040 D5 mandated replay
    // default. (The Docker half extends the legacy-row legs in
    // BoundaryCauseEncodingTests through the real projection read path.)
    // ═════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(PcsEmployedLiteral)] // string-encoded cause (post-QUAL-002 unified shape)
    [InlineData("{\"startDate\":\"2026-01-01\",\"endDate\":\"2026-01-31\",\"boundaryCause\":2,\"snapshot\":null}")] // legacy NUMERIC row (allowIntegerValues tolerance)
    public void PcsReader_PreD5Segment_DefaultsToEmployed(string preD5Json)
    {
        var segment = JsonSerializer.Deserialize<PlannedSegment>(preD5Json, PcsWriter)!;

        Assert.Equal(EmploymentWindowStatus.EMPLOYED, segment.EmploymentStatus);
        Assert.Equal(BoundaryCause.LocalProfileActivation, segment.BoundaryCause);
        Assert.Null(segment.Snapshot);
    }

    [Fact]
    public void EventSerializerReader_PreD5ManifestEvent_DefaultsToEmployed()
    {
        // A hand-written pre-D5 SegmentManifestCreated payload: the segment carries no
        // employmentStatus key (and no snapshot key — the EventSerializer shape).
        const string preD5EventJson =
            "{\"manifestId\":\"7e04a3c2-40cc-4c39-9a3e-111111111111\"," +
            "\"employeeId\":\"EMP-PRE-D5\"," +
            "\"periodStart\":\"2026-01-01\",\"periodEnd\":\"2026-01-31\"," +
            "\"calculationKind\":\"forward-calc\"," +
            "\"boundaryCauseSummary\":[\"LocalProfileActivation\"]," +
            "\"createdAt\":\"2026-01-31T12:00:00+00:00\"," +
            "\"segments\":[{\"startDate\":\"2026-01-01\",\"endDate\":\"2026-01-31\",\"boundaryCause\":\"LocalProfileActivation\"}]}";

        var evt = Assert.IsType<SegmentManifestCreated>(
            EventSerializer.Deserialize("SegmentManifestCreated", preD5EventJson));

        var segment = Assert.Single(evt.Segments);
        Assert.Equal(EmploymentWindowStatus.EMPLOYED, segment.EmploymentStatus);
        Assert.Null(segment.Snapshot);
    }

    /// <summary>The read side of falsifiability: a NOT_EMPLOYED key round-trips (it is
    /// not silently coerced back to the default).</summary>
    [Fact]
    public void PcsReader_NotEmployedSegment_RoundTrips()
    {
        var segment = JsonSerializer.Deserialize<PlannedSegment>(
            PcsNotEmployedLiteral, PcsWriter)!;

        Assert.Equal(EmploymentWindowStatus.NOT_EMPLOYED, segment.EmploymentStatus);
    }

    private static SegmentManifestCreated ManifestEvent(PlannedSegment segment) => new()
    {
        ManifestId = Guid.NewGuid(),
        EmployeeId = "EMP-PARITY-PIN",
        PeriodStart = Jan01,
        PeriodEnd = Jan31,
        CalculationKind = "forward-calc",
        BoundaryCauseSummary = new[] { segment.BoundaryCause.ToString() },
        CreatedAt = DateTimeOffset.UtcNow,
        Segments = new[] { segment },
    };
}
