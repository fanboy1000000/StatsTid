using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Tests.Regression.Segmentation;

/// <summary>
/// S134 TASK-13404 (QUAL-003) — the PAYROLL-BOUNDARY regression assertion.
///
/// <para>
/// <b>Why (for a product manager):</b> QUAL-003 wired audit logging into the payroll/calc host so
/// that a calculate-and-export call finally leaves an <c>audit_log</c> trail linked to the
/// calculation's "manifest" (the immutable record of how the period was segmented). The payroll
/// boundary is an inviolable invariant: adding that audit trail must NOT change a single byte of what
/// we send to the payroll system. This test is the standing proof that it does not.
/// </para>
///
/// <para>
/// <b>How the byte-identity is guaranteed BY CONSTRUCTION.</b> The endpoint used to call the legacy
/// planless <c>CalculateAsync(profile, …)</c> shim and return its <c>PeriodCalculationResult</c>. It
/// now calls the new planless <c>CalculateWithOutcomeAsync(profile, …)</c> and returns
/// <c>outcome.Result</c>. Both overloads build the plan through the SAME
/// <c>BuildPlanForLegacyCallersAsync</c> and run the SAME sole logic entry point
/// (<c>CalculateWithOutcomeAsync(plan, …)</c>) — indeed the legacy <c>CalculateAsync</c> IS literally
/// <c>CalculateWithOutcomeAsync(plan, …).Result</c>. So the export produced by the two paths is
/// identical except for the per-call, freshly-minted manifest id — which was already
/// non-deterministic per call BEFORE QUAL-003 (every calc mints a new manifest). This test drives BOTH
/// overloads with identical inputs and asserts the SLS export text is byte-for-byte identical once that
/// per-run manifest id is normalized out, and that the manifest STAMPING itself is intact on both paths.
/// </para>
///
/// <para>
/// Uses the shared Sprint-20 Postgres testcontainer + stubbed Rule Engine (see <see cref="TestFixtures"/>).
/// The audit middleware and the endpoint plumbing are NOT exercised here — this test isolates the ONE
/// thing that could perturb the export: the service-level switch from <c>CalculateAsync</c> to
/// <c>CalculateWithOutcomeAsync</c>. The composed-stack smoke probe proves the audit ROW is written.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class AuditWiringExportParityTests : IAsyncLifetime
{
    private TestFixtures.DockerHarness _harness = null!;

    public async Task InitializeAsync() => _harness = await TestFixtures.DockerHarness.StartAsync();
    public async Task DisposeAsync() => await _harness.DisposeAsync();

    /// <summary>
    /// Single-OK-version (no interior boundary → single-segment) March-2026 period so the legacy
    /// planless path plans cleanly under the full rule set. Drives the OLD (<c>CalculateAsync</c>) and
    /// NEW (<c>CalculateWithOutcomeAsync</c>) planless overloads with identical inputs and asserts the
    /// SLS export is byte-identical modulo the per-run manifest id, plus that manifest stamping is
    /// present and internally consistent on the new path.
    /// </summary>
    [Fact]
    public async Task PlanlessCalculateWithOutcome_ProducesByteIdenticalExport_ToLegacyCalculateAsync()
    {
        var profile = TestFixtures.Profile("EMP-QUAL003-PARITY");
        // All dates < 2026-04-01 ⇒ one OK24 segment (WeekdayEntriesForPeriod stamps OK24 for these).
        var periodStart = new DateOnly(2026, 3, 2);
        var periodEnd = new DateOnly(2026, 3, 6);
        var entries = TestFixtures.WeekdayEntriesForPeriod(profile.EmployeeId, periodStart, periodEnd);
        var absences = Array.Empty<AbsenceEntry>();

        await TestFixtures.SeedWageTypeMappingsAsync(_harness.Factory);
        var pcs = TestFixtures.BuildPcs(_harness.Factory, _harness.EventStore);

#pragma warning disable CS0618 // Both planless overloads are [Obsolete] by design (legacy shim path).
        // OLD endpoint behaviour (pre-QUAL-003): the planless CalculateAsync, returning just the result.
        var resultOld = await pcs.CalculateAsync(
            profile, entries, absences, periodStart, periodEnd, previousFlexBalance: 0m);
        // NEW endpoint behaviour (post-QUAL-003): the planless CalculateWithOutcomeAsync, returning the
        // full outcome (result + AuditState + ManifestId). outcome.Result is what the endpoint returns.
        var outcomeNew = await pcs.CalculateWithOutcomeAsync(
            profile, entries, absences, periodStart, periodEnd, previousFlexBalance: 0m);
#pragma warning restore CS0618

        // Both must succeed and produce export lines (NORM_CHECK emits one NORMAL_HOURS line per entry).
        Assert.True(resultOld.Success);
        Assert.True(outcomeNew.Result.Success);
        Assert.NotEmpty(resultOld.ExportLines);
        Assert.NotEmpty(outcomeNew.Result.ExportLines);

        // ── Headline: the SLS export is BYTE-IDENTICAL once the per-run manifest id is normalized ──
        // Zero out the (per-call, non-deterministic-by-design) manifest id on BOTH sides and render with
        // a fixed export id + timestamp + empty header manifest, so the comparison isolates the EXPORT
        // CONTENT: employee, wage type, hours, amount, period dates, OK-version, and the trailer checksum.
        var fixedTs = new DateTime(2026, 3, 6, 12, 0, 0, DateTimeKind.Utc);
        var slsOld = SlsExportFormatter.Format(
            resultOld.ExportLines.Select(ZeroManifest).ToList(), "FIXED-EXPORT-ID", fixedTs);
        var slsNew = SlsExportFormatter.Format(
            outcomeNew.Result.ExportLines.Select(ZeroManifest).ToList(), "FIXED-EXPORT-ID", fixedTs);
        Assert.Equal(slsOld, slsNew);

        // Field-level equivalence (excluding the manifest id) as a second, structured lens on the same
        // guarantee — line count + every payroll-relevant field per line.
        Assert.Equal(resultOld.ExportLines.Count, outcomeNew.Result.ExportLines.Count);
        for (int i = 0; i < resultOld.ExportLines.Count; i++)
        {
            var a = resultOld.ExportLines[i];
            var b = outcomeNew.Result.ExportLines[i];
            Assert.Equal(a.EmployeeId, b.EmployeeId);
            Assert.Equal(a.WageType, b.WageType);
            Assert.Equal(a.Hours, b.Hours);
            Assert.Equal(a.Amount, b.Amount);
            Assert.Equal(a.PeriodStart, b.PeriodStart);
            Assert.Equal(a.PeriodEnd, b.PeriodEnd);
            Assert.Equal(a.OkVersion, b.OkVersion);
            Assert.Equal(a.SourceRuleId, b.SourceRuleId);
            Assert.Equal(a.SourceTimeType, b.SourceTimeType);
        }

        // The result envelope (everything the endpoint returns except the manifest-carrying lines) matches.
        Assert.Equal(resultOld.EmployeeId, outcomeNew.Result.EmployeeId);
        Assert.Equal(resultOld.PeriodStart, outcomeNew.Result.PeriodStart);
        Assert.Equal(resultOld.PeriodEnd, outcomeNew.Result.PeriodEnd);
        Assert.Equal(resultOld.AgreementCode, outcomeNew.Result.AgreementCode);
        Assert.Equal(resultOld.OkVersion, outcomeNew.Result.OkVersion);
        Assert.Equal(resultOld.RuleResults.Count, outcomeNew.Result.RuleResults.Count);

        // ── Manifest stamping is INTACT and internally consistent on the new path ──
        // The new overload's added value is exposing the manifest id; every export line must carry it,
        // and it is the ONLY thing that differs run-to-run (each calc mints a fresh manifest).
        Assert.NotEqual(Guid.Empty, outcomeNew.ManifestId);
        Assert.All(outcomeNew.Result.ExportLines, l => Assert.Equal(outcomeNew.ManifestId, l.ManifestId));
        Assert.All(resultOld.ExportLines, l => Assert.NotEqual(Guid.Empty, l.ManifestId));
        // Distinct manifest per invocation — confirms the byte-difference we normalized away is exactly
        // (and only) the per-run manifest id, not any export content.
        Assert.NotEqual(outcomeNew.ManifestId, resultOld.ExportLines[0].ManifestId);
    }

    /// <summary>
    /// Copies a <see cref="PayrollExportLine"/> with its <see cref="PayrollExportLine.ManifestId"/>
    /// zeroed. <see cref="PayrollExportLine"/> is a sealed class (not a record), so a <c>with</c>
    /// expression is unavailable — this explicit copy is the normalization used to isolate export
    /// content from the per-run manifest id.
    /// </summary>
    private static PayrollExportLine ZeroManifest(PayrollExportLine l) => new()
    {
        EmployeeId = l.EmployeeId,
        WageType = l.WageType,
        Hours = l.Hours,
        Amount = l.Amount,
        PeriodStart = l.PeriodStart,
        PeriodEnd = l.PeriodEnd,
        OkVersion = l.OkVersion,
        SourceRuleId = l.SourceRuleId,
        SourceTimeType = l.SourceTimeType,
        ManifestId = Guid.Empty,
    };
}
