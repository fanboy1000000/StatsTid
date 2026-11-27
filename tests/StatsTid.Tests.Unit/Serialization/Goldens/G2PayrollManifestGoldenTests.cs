using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Unit.Serialization.Goldens;

/// <summary>
/// G-2 (TASK-14900, ADR-045; ADR-034 D1) — the payroll export manifest's stored text and its content hash.
///
/// <para>
/// <b>Plain language.</b> <c>payroll_export_records.content_hash</c> is the SHA-256 of the manifest JSON. Payroll
/// treats "same key and same hash" as a repeated export (nothing to do) and "same key and a different hash" as
/// a conflicting one (refused). So the JSON bytes must never move: a moved byte would make an unchanged
/// export look like a conflict. The literals in <c>G2-manifest.golden</c> are the order the lines are sorted
/// into, the JSON, its hash (64 uppercase hex), and the same two for the empty list. The lines are given in
/// unsorted order; two differ only in <c>Amount</c>; one amount is negative; hours are <c>7.40</c> and
/// <c>0.5</c>; a rule id carries Danish letters and escaped characters; one optional text is null.
/// </para>
///
/// <para>
/// <b>Kind: R.</b> Red conditions: any change to the JSON options (indentation, naming, number or date form), the
/// sort key or the hash encoding fails the matching literal. Proved by the mutation M-2 (<c>WriteIndented =
/// true</c>) at the wave gate.
/// </para>
/// </summary>
public sealed class G2PayrollManifestGoldenTests
{
    private const string File = "G2-manifest.golden";

    private static PayrollExportLine Line(string wage, int day, decimal hours, decimal amount, string? rule, string? timeType, Guid manifest) => new()
    {
        EmployeeId = "EMP-0001",
        WageType = wage,
        Hours = hours,
        Amount = amount,
        PeriodStart = new DateOnly(2026, 3, 1),
        PeriodEnd = new DateOnly(2026, 3, day),
        OkVersion = "OK24",
        SourceRuleId = rule,
        SourceTimeType = timeType,
        ManifestId = manifest,
    };

    private static readonly Guid ManifestId = Guid.Parse("0a0b0c0d-1111-4222-8333-444455556666");

    // Deliberately not in sorted order.
    private static readonly PayrollExportLine[] Input =
    {
        Line("SLS_0420", 31, 7.40m, 1234.50m, "Ærø Å/ø <&'\"+>", "NORMAL", ManifestId),
        Line("SLS_0110", 31, 0.5m, -0.25m, "OVERTIME-ÆØÅ", null, ManifestId),
        Line("SLS_0420", 31, 7.40m, 99.00m, "Ærø Å/ø <&'\"+>", "NORMAL", ManifestId),
        Line("SLS_0110", 15, 37.00m, 8000.00m, "NORM", "NORMAL", ManifestId),
    };

    private static IReadOnlyDictionary<string, string> Actual()
    {
        var ordered = PayrollExportManifest.OrderLines(Input);
        var order = string.Join(",", ordered.Select(l => Array.IndexOf(Input, l)));
        return new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["1.order"] = order,
            ["2.json"] = PayrollExportManifest.Serialize(ordered),
            ["3.hash"] = PayrollExportManifest.ComputeContentHash(ordered),
            ["4.empty.json"] = PayrollExportManifest.Serialize(Array.Empty<PayrollExportLine>()),
            ["5.empty.hash"] = PayrollExportManifest.ComputeContentHash(Array.Empty<PayrollExportLine>()),
        };
    }

    [Fact]
    public void Order_json_and_hash_of_the_fixed_lines_equal_their_literals()
    {
        GoldenFile.AssertMatches(File, Actual());
    }

    /// <summary>
    /// The content hash on its own: the production <c>ComputeContentHash</c> over the ordered fixed lines, and over
    /// the empty list, equals the hash literals (<c>payroll_export_records.content_hash</c>, ADR-034 D1). Kind: R.
    /// Red conditions: a different hash encoding (case, length) or a different hashed text. Proved by M-2
    /// (indented JSON) and M-2b (lowercase hex).
    /// </summary>
    [Fact]
    public void The_content_hash_of_the_fixed_lines_and_of_the_empty_list_equal_their_literals()
    {
        var golden = GoldenFile.Load(File);
        Assert.Equal(golden["3.hash"], PayrollExportManifest.ComputeContentHash(PayrollExportManifest.OrderLines(Input)));
        Assert.Equal(golden["5.empty.hash"], PayrollExportManifest.ComputeContentHash(Array.Empty<PayrollExportLine>()));
    }

    /// <summary>
    /// Capture sanity: reads only the data file, so it is not a pin. The hash literals are 64 uppercase hex
    /// characters and each is the SHA-256 of its JSON literal, so a capture cannot pair a hash with the wrong text.
    /// </summary>
    [Fact]
    public void Capture_sanity_each_hash_literal_is_the_uppercase_sha256_of_its_json_literal()
    {
        var golden = GoldenFile.Load(File);
        Assert.Matches("^[0-9A-F]{64}$", golden["3.hash"]);
        Assert.Matches("^[0-9A-F]{64}$", golden["5.empty.hash"]);
        Assert.Equal("[]", golden["4.empty.json"]);
        Assert.Equal(golden["3.hash"], Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(golden["2.json"]))));
        Assert.Equal(golden["5.empty.hash"], Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(golden["4.empty.json"]))));
    }

    [Fact]
    public void Reading_the_literal_gives_the_lines_back_and_writing_them_gives_the_literal()
    {
        var golden = GoldenFile.Load(File);
        var read = PayrollExportManifest.Deserialize(golden["2.json"]);
        var order = golden["1.order"].Split(',').Select(int.Parse).ToArray();
        Assert.Equal(Input.Length, read.Count);
        for (var i = 0; i < read.Count; i++)
        {
            var want = Input[order[i]];
            Assert.Equal(want.EmployeeId, read[i].EmployeeId);
            Assert.Equal(want.WageType, read[i].WageType);
            Assert.Equal(want.Hours, read[i].Hours);
            Assert.Equal(want.Amount, read[i].Amount);
            Assert.Equal(want.PeriodStart, read[i].PeriodStart);
            Assert.Equal(want.PeriodEnd, read[i].PeriodEnd);
            Assert.Equal(want.OkVersion, read[i].OkVersion);
            Assert.Equal(want.SourceRuleId, read[i].SourceRuleId);
            Assert.Equal(want.SourceTimeType, read[i].SourceTimeType);
            Assert.Equal(want.ManifestId, read[i].ManifestId);
        }
        Assert.Equal(golden["2.json"], PayrollExportManifest.Serialize(read));
        Assert.Empty(PayrollExportManifest.Deserialize(golden["4.empty.json"]));
    }
}
