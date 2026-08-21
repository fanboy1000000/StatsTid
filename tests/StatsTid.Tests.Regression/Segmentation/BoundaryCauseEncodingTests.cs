using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// S132 TASK-132-2b (QUAL-002) — regression guard for the <c>boundaryCause</c> serialization
/// unification across the <c>segment_manifests</c> projection WRITER and READER.
///
/// <para>
/// Plain-language: a payroll calculation records a "segment manifest" (a JSON snapshot of the
/// calculation's temporal segments) into the <c>segment_manifests</c> projection. Each segment
/// carries a <see cref="BoundaryCause"/> — WHY the calculation was split there (an OK-version
/// change, a local-agreement activation, ...). That enum used to be written TWO different ways:
/// <see cref="StatsTid.Integrations.Payroll.Services.PeriodCalculationService"/>'s live write
/// emitted it as a NUMBER (<c>"boundaryCause": 2</c>) because its JSON options carried no string
/// enum converter, while the immutable <c>SegmentManifestCreated</c> event — and therefore any
/// projection row rebuilt from events (<see cref="SegmentManifestProjectionRebuilder"/> copies
/// <c>data-&gt;'segments'</c> verbatim) — emitted it as a STRING (<c>"boundaryCause":
/// "LocalProfileActivation"</c>). A reader wired to one encoding silently misread the other; after
/// a projection rebuild the converter-less reader could not parse the string form at all. That is
/// an auditability/correctness defect (ADR-016 D10 manifest⋈audit join; ADR-018 projections MUST
/// be rebuildable from the immutable event stream).
/// </para>
///
/// <para>
/// The fix adds a tolerant <c>JsonStringEnumConverter</c> to the PCS options so writes are now
/// STRING-encoded (enum-equivalent to the event/rebuild path) AND reads accept BOTH encodings. The
/// enforced contract is DESERIALIZED EQUIVALENCE — a live-written and a rebuilt manifest deserialize
/// to the SAME <see cref="PlannedSegment"/> set — NOT byte-identity (a null <c>Snapshot</c> is
/// written as <c>"snapshot":null</c> by PCS but OMITTED by EventSerializer, a benign residual
/// documented on <c>PeriodCalculationService.JsonOptions</c>). These tests pin all the legs:
/// </para>
/// <list type="bullet">
///   <item><b>(a) legacy tolerance</b> — a lingering LEGACY NUMERIC projection row (as the old
///         converter-less writer produced), with NO backing event so the read cannot be rescued by
///         the event-replay fallback, still replays. Green before AND after the fix (belt-and-braces;
///         no data loss).</item>
///   <item><b>(b-reader) string tolerance</b> — a STRING projection row, again with NO backing
///         event, replays. This is the RED-on-old leg: the converter-less reader throws on the
///         string, the event-replay fallback finds no event, the load returns null and replay
///         raises "not found".</item>
///   <item><b>(b-writer/rebuild) unification</b> — a live forward-calc write now encodes
///         <c>boundaryCause</c> as a STRING carrying the correct enum names (RED-on-old: baseline
///         wrote numbers), a truncate+rebuild yields a deserialized-EQUIVALENT manifest, and the
///         reader replays the rebuilt row.</item>
///   <item><b>null-snapshot handling</b> — the event→rebuild path OMITS the null <c>Snapshot</c> key
///         (<c>WhenWritingNull</c>), yet the omitted-key row still deserializes to
///         <c>Snapshot == null</c> and the reader replays it.</item>
/// </list>
///
/// <para>
/// The reader legs exercise the REAL projection read path through the public
/// <c>PeriodCalculationService.ReplayAsync</c> (which calls the private <c>LoadManifestAsync</c>),
/// and deliberately insert PROJECTION-ONLY rows (no <c>SegmentManifestCreated</c> event) so a
/// projection-parse failure surfaces as a failed replay instead of being masked by the
/// event-replay fallback inside <c>LoadManifestAsync</c>. The correct enum VALUE is asserted on the
/// writer leg by parsing the persisted <c>segments_jsonb</c> (the public replay result does not
/// surface segment-level <c>BoundaryCause</c>, and the Web-SDK Payroll assembly cannot expose its
/// internals to this test project without a <c>Program</c>-type clash against Backend.Api).
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class BoundaryCauseEncodingTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TestFixtures.DockerHarness.StartAsync();
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    // A boundaryCause value that is NOT the enum default (OkTransition == 0), so a successful
    // numeric parse is provably distinct from a "defaulted-because-unparsed" 0.
    private const BoundaryCause ExpectedCause = BoundaryCause.LocalProfileActivation; // ordinal 2

    // Matches "boundaryCause": <quoted string> (the unified encoding).
    private static readonly Regex StringEncoded =
        new("\"boundaryCause\"\\s*:\\s*\"[^\"]+\"", RegexOptions.Compiled);
    // Matches "boundaryCause": <bare number> (the legacy converter-less encoding).
    private static readonly Regex NumericEncoded =
        new("\"boundaryCause\"\\s*:\\s*-?\\d", RegexOptions.Compiled);

    // -------------------------------------------------------------------
    // (a) Legacy NUMERIC projection row, projection-only → tolerant read.
    //     Green before AND after the fix (regression guard — no data loss).
    // -------------------------------------------------------------------
    [Fact]
    public async Task Replay_LegacyNumericBoundaryCauseRow_ProjectionOnly_StillReplays()
    {
        var pcs = TestFixtures.BuildPcs(_harness.Factory, _harness.EventStore);
        var manifestId = Guid.NewGuid();

        // Exactly what the OLD converter-less PCS writer produced: boundaryCause as a NUMBER.
        // One full-period segment so FromManifest's geometric invariants are satisfied.
        await InsertProjectionRowAsync(
            manifestId,
            segmentsJson:
                $"[{{\"startDate\":\"2026-01-01\",\"endDate\":\"2026-01-31\",\"boundaryCause\":{(int)ExpectedCause},\"snapshot\":null}}]");

        var result = await pcs.ReplayAsync(manifestId);

        Assert.True(result.Success);
        Assert.Equal(manifestId, result.ManifestId);
    }

    // -------------------------------------------------------------------
    // (b-reader) STRING projection row, projection-only → tolerant read.
    //     RED on baseline: the converter-less reader throws on the string, the event-replay
    //     fallback finds no event, LoadManifestAsync returns null, and ReplayAsync raises
    //     "not found". GREEN after the fix.
    // -------------------------------------------------------------------
    [Fact]
    public async Task Replay_StringBoundaryCauseRow_ProjectionOnly_StillReplays()
    {
        var pcs = TestFixtures.BuildPcs(_harness.Factory, _harness.EventStore);
        var manifestId = Guid.NewGuid();

        // The unified encoding (also what a rebuild-from-events produces): boundaryCause as a STRING.
        await InsertProjectionRowAsync(
            manifestId,
            segmentsJson:
                $"[{{\"startDate\":\"2026-01-01\",\"endDate\":\"2026-01-31\",\"boundaryCause\":\"{ExpectedCause}\",\"snapshot\":null}}]");

        // Baseline: this throws (null load → "Manifest ... not found ... Cannot replay") → RED.
        var result = await pcs.ReplayAsync(manifestId);

        Assert.True(result.Success);
        Assert.Equal(manifestId, result.ManifestId);
    }

    // -------------------------------------------------------------------
    // (b-writer/rebuild) Live forward-calc write + rebuild-from-events both STRING-encode the
    //     correct enum names, are byte-identical, and the reader replays the rebuilt row.
    //     RED on baseline: the live-written row encodes boundaryCause NUMERICALLY.
    // -------------------------------------------------------------------
    [Fact]
    public async Task ForwardCalcWrite_And_Rebuild_BothStringEncode_CorrectBoundaryCause()
    {
        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
        var pcs = TestFixtures.BuildPcs(_harness.Factory, _harness.EventStore);

        var profile = TestFixtures.Profile("EMP-BC-ENCODING-1");
        var entries = TestFixtures.WeekdayEntriesForPeriod(
            profile.EmployeeId, new DateOnly(2026, 3, 25), new DateOnly(2026, 4, 7));

        // Straddles the OK24→OK26 boundary (2026-04-01), so the plan has a real BoundaryCause
        // beyond the default — see the existing Manifest{Replay,ProjectionRebuild}Tests.
        var plan = PeriodPlanner.Plan(
            employeeId: profile.EmployeeId,
            periodStart: new DateOnly(2026, 3, 25),
            periodEnd: new DateOnly(2026, 4, 7),
            calculationKind: "forward-calc",
            ruleSet: TestFixtures.StraddleSafeRuleSet,
            sources: TestFixtures.OkStraddleSources(),
            options: PlannerOptions.Default,
            enrollment: TestFixtures.StraddleEnrollment(),
            profile: profile);

        await pcs.CalculateAsync(plan, profile, entries, Array.Empty<AbsenceEntry>(), 0m);

        var expectedCauses = plan.Segments.Select(s => s.BoundaryCause.ToString()).ToList();

        // The LIVE-written projection row must now string-encode boundaryCause (RED on baseline)
        // AND carry the correct enum names.
        var liveSegmentsJson = await ReadSegmentsJsonAsync(plan.ManifestId);
        AssertStringEncoded(liveSegmentsJson, "live forward-calc write");
        Assert.Equal(expectedCauses, ParseBoundaryCauseNames(liveSegmentsJson));

        // Truncate then rebuild from the event store (the ops drift-recovery path).
        await using (var conn = new NpgsqlConnection(_harness.ConnectionString))
        {
            await conn.OpenAsync();
            await using var trunc = new NpgsqlCommand("TRUNCATE TABLE segment_manifests", conn);
            await trunc.ExecuteNonQueryAsync();
        }
        var rebuilt = await SegmentManifestProjectionRebuilder.RebuildAsync(
            _harness.Factory, NullLogger.Instance);
        Assert.True(rebuilt >= 1);

        // The rebuilt row string-encodes boundaryCause (it always did — copied verbatim from the
        // string-encoded event) and carries the same enum names.
        var rebuiltSegmentsJson = await ReadSegmentsJsonAsync(plan.ManifestId);
        AssertStringEncoded(rebuiltSegmentsJson, "rebuilt-from-events");
        Assert.Equal(expectedCauses, ParseBoundaryCauseNames(rebuiltSegmentsJson));

        // DESERIALIZED EQUIVALENCE (not byte-identity): the live-written and rebuilt rows deserialize
        // to the SAME PlannedSegment set — date ranges, BoundaryCause values, and snapshots all match.
        // (These straddle segments carry non-null snapshots so they also happen to be byte-identical,
        // but the null-snapshot residual is covered separately by the null-snapshot test below.)
        TestFixtures.AssertSegmentsDeserializeEquivalent(liveSegmentsJson, rebuiltSegmentsJson);

        // The reader replays the rebuilt (string-encoded) row end-to-end.
        var replay = await pcs.ReplayAsync(plan.ManifestId);
        Assert.True(replay.Success);
        Assert.Equal(plan.ManifestId, replay.ManifestId);
    }

    // -------------------------------------------------------------------
    // NULL-SNAPSHOT handling on the event→rebuild path. PlannedSegment.Snapshot == null is the
    //     COMMON case, and EventSerializer OMITS the key (WhenWritingNull), so the rebuilt projection
    //     row carries NO "snapshot" key at all. This pins that (a) the rebuild genuinely omits the
    //     key, (b) that omitted-key encoding still deserializes to Snapshot == null (absent key ≡
    //     explicit null on read — the deserialized-equivalence half of the contract), and (c) the
    //     real reader replays such a row end-to-end.
    //
    //     Scope note: this does NOT assert the PCS live-write's explicit "snapshot":null byte-shape.
    //     That shape cannot be produced through the real calc path (a null-snapshot segment makes
    //     MapSegmentToExportLinesAsync throw before the projection is ever written), and asserting a
    //     hand-fabricated PCS string would be a claim not backed by real code — it would keep passing
    //     even if PCS later gained WhenWritingNull. The PCS-side explicit-null behavior is documented
    //     (not asserted) in PeriodCalculationService.JsonOptions. Not a RED-on-old leg.
    // -------------------------------------------------------------------
    [Fact]
    public async Task NullSnapshot_Rebuild_OmitsKey_YetDeserializesToNull_AndReplays()
    {
        var pcs = TestFixtures.BuildPcs(_harness.Factory, _harness.EventStore);
        var manifestId = Guid.NewGuid();

        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 1, 31);
        // A single, whole-period segment with a NULL snapshot (the common shape).
        var nullSnapshotSegment = new PlannedSegment(start, end, ExpectedCause, Snapshot: null);

        // Produce the rebuilt row via the REAL event → rebuild path: append a SegmentManifestCreated
        // event (EventSerializer omits the null snapshot key), then rebuild (copies data->'segments'
        // verbatim). Stream-id pattern mirrors PeriodCalculationService.ManifestStreamId (internal).
        var evt = new SegmentManifestCreated
        {
            ManifestId = manifestId,
            EmployeeId = "EMP-BC-NULLSNAP",
            PeriodStart = start,
            PeriodEnd = end,
            CalculationKind = "forward-calc",
            BoundaryCauseSummary = new[] { ExpectedCause.ToString() },
            CreatedAt = DateTimeOffset.UtcNow,
            Segments = new[] { nullSnapshotSegment },
        };
        await _harness.EventStore.AppendAsync($"segment-manifest-{manifestId}", evt);
        await SegmentManifestProjectionRebuilder.RebuildAsync(_harness.Factory, NullLogger.Instance);

        // (a) The rebuild GENUINELY omits the snapshot key (driven by EventSerializer's WhenWritingNull).
        var rebuiltJson = await ReadSegmentsJsonAsync(manifestId);
        Assert.DoesNotContain("snapshot", rebuiltJson);

        // (b) The omitted-key encoding deserializes back to a segment with Snapshot == null (absent
        //     key ≡ explicit null on read), preserving the correct BoundaryCause.
        var seg = Assert.Single(TestFixtures.DeserializeSegments(rebuiltJson));
        Assert.Null(seg.Snapshot);
        Assert.Equal(ExpectedCause, seg.BoundaryCause);

        // (c) The real reader replays the rebuilt (omitted-key, null-snapshot) row end-to-end.
        var replay = await pcs.ReplayAsync(manifestId);
        Assert.True(replay.Success);
        Assert.Equal(manifestId, replay.ManifestId);
    }

    private static void AssertStringEncoded(string segmentsJson, string which)
    {
        Assert.True(
            StringEncoded.IsMatch(segmentsJson),
            $"Expected string-encoded boundaryCause in the {which} row, got: {segmentsJson}");
        Assert.False(
            NumericEncoded.IsMatch(segmentsJson),
            $"Found a NUMERIC boundaryCause in the {which} row (encoding not unified): {segmentsJson}");
    }

    /// <summary>Reads each segment's <c>boundaryCause</c> (a JSON string post-fix) in array order.</summary>
    private static List<string> ParseBoundaryCauseNames(string segmentsJson)
    {
        using var doc = JsonDocument.Parse(segmentsJson);
        return doc.RootElement.EnumerateArray()
            .Select(seg => seg.GetProperty("boundaryCause").GetString()!)
            .ToList();
    }

    private async Task InsertProjectionRowAsync(Guid manifestId, string segmentsJson)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO segment_manifests
                (manifest_id, period_start, period_end, employee_id, calculation_kind,
                 boundary_cause_summary, created_at, segments_jsonb)
            VALUES
                (@manifestId, @periodStart, @periodEnd, @employeeId, @calculationKind,
                 @boundaryCauseSummary, @createdAt, @segmentsJson::jsonb)
            """, conn);
        cmd.Parameters.AddWithValue("manifestId", manifestId);
        cmd.Parameters.AddWithValue("periodStart", new DateTime(2026, 1, 1));
        cmd.Parameters.AddWithValue("periodEnd", new DateTime(2026, 1, 31));
        cmd.Parameters.AddWithValue("employeeId", "EMP-BC-LEGACY");
        cmd.Parameters.AddWithValue("calculationKind", "forward-calc");
        cmd.Parameters.AddWithValue("boundaryCauseSummary", new[] { ExpectedCause.ToString() });
        cmd.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow);
        cmd.Parameters.AddWithValue("segmentsJson", NpgsqlTypes.NpgsqlDbType.Text, segmentsJson);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<string> ReadSegmentsJsonAsync(Guid manifestId)
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT segments_jsonb::text FROM segment_manifests WHERE manifest_id = @id", conn);
        cmd.Parameters.AddWithValue("id", manifestId);
        var result = await cmd.ExecuteScalarAsync();
        Assert.NotNull(result);
        return (string)result!;
    }
}
