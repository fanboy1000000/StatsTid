using System.Net;
using System.Text.Json;
using static StatsTid.Tests.Regression.Payroll.FlexLedgerHostFixture;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S147 / TASK-14700-T (QUAL-229; AC-229-1, -2, -3, -6; rulings R-2 with R-11 .. R-14, R-26) — the HOST-level facts of the
/// stale-chain check: what an operator sees through the real Payroll host when an early month's balance was corrected and the
/// months built on it were not.
///
/// <para>
/// <b>Plain language.</b> Each paid month records the flex balance it opened and closed at, and the next month opens at the
/// previous month's recorded closing. When an administrator corrects an early month (January) its closing changes, and every
/// month built on it (February, March, ...) keeps the old, now wrong, figure until it is recalculated, one by one, in order.
/// Until this sprint payroll looked only at the month directly before the one it was about to pay, so it paid March or April
/// on a figure built on a stale February. Now payroll refuses (409 <c>payroll-prior-month-stale</c>) and names the EARLIEST
/// month to recalculate first; the administrator recalculates it, the next attempt names the next, and so on until the month
/// exports on the corrected chain. A refusal writes nothing: no record, no event, no calculation. These facts drive that
/// through HTTP against a real Postgres.
/// </para>
///
/// <para>
/// <b>Harness and values.</b> <see cref="FlexLedgerHostFixture"/> with <see cref="FlexLedgerStubEngine"/> behind the host.
/// Months are exported with NO registrations (zero-line months still write a record and balances, ADR-034 D3), so every fact
/// asserts on the ledger, not on lines. The stub closes each month at <c>opening + delta</c> (the opening payroll SENT it), so
/// a month recalculated on a new opening closes at a new figure: January closes at 10 (corrected to 12), February +5, March
/// +1, April +2, May +3. The expected figures below are therefore spec arithmetic, never read off the code: February
/// 10 to 15, March 15 to 16, April 16 to 18; after January is corrected to 12, February 12 to 17, March 17 to 18, April 18 to
/// 20. Every month from December 2025 to May 2026 is APPROVED for the employee, so the previous-month approval gate never
/// decides an outcome.
/// </para>
///
/// <para>
/// <b>Status.</b> Docker-gated: <b>CI-pending</b>, never locally green. Each docstring names its kind (F fails on the code
/// before this sprint, B boundary pin that holds today) and argues its RED, because the mutation proof for a Docker fact is
/// argued, not run (docs/AGENTS.md § "RED evidence"). The F facts all fail on the pre-S147 code for one reason: its
/// derivation reads only the previous month's record (<c>FlexLedgerOpening.Derive</c> case 4a), so a stale month further back
/// is never seen and the month exports with 200.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FlexLedgerStaleChainHostTests : IClassFixture<FlexLedgerHostFixture>
{
    private static readonly DateOnly Dec25 = new(2025, 12, 1);
    private static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly DateOnly Feb = new(2026, 2, 1);
    private static readonly DateOnly Mar = new(2026, 3, 1);
    private static readonly DateOnly Apr = new(2026, 4, 1);
    private static readonly DateOnly May = new(2026, 5, 1);

    private readonly FlexLedgerHostFixture _fx;

    public FlexLedgerStaleChainHostTests(FlexLedgerHostFixture fixture) => _fx = fixture;

    // ═════════════════════════════════════════════════════════════════════
    // helpers
    // ═════════════════════════════════════════════════════════════════════

    private static void AssertStatus(HttpStatusCode expected, (HttpStatusCode Status, string Json) response) =>
        Assert.True(response.Status == expected, $"expected {(int)expected}, got {(int)response.Status}: {response.Json}");

    /// <summary>A 409 stale refusal: the kind, the named month, success false, and a fixed sentence with no employee id and no
    /// digit (no date, ADR-040 D7).</summary>
    private static void AssertStaleRefusal((HttpStatusCode Status, string Json) response, string employeeId, string month)
    {
        AssertStatus(HttpStatusCode.Conflict, response);
        using var doc = JsonDocument.Parse(response.Json);
        var root = doc.RootElement;
        Assert.Equal("payroll-prior-month-stale", Str(root, "kind"));
        Assert.Equal(month, Str(root, "month"));
        Assert.False(root.GetProperty("success").GetBoolean());
        var error = Str(root, "error");
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.DoesNotContain(employeeId, error!);
        Assert.DoesNotContain(error!, char.IsDigit);
    }

    private static void AssertTriple(ExportRecordRow record, decimal opening, decimal closing, string provenance)
    {
        Assert.Equal(opening, record.Opening);
        Assert.Equal(closing, record.Closing);
        Assert.Equal(provenance, record.Provenance);
    }

    /// <summary>Everything a refusal must leave alone: the records (by value), the event, audit, manifest and correction
    /// counts, and the number of rule-engine calls the employee caused.</summary>
    private sealed record Snapshot(
        IReadOnlyList<ExportRecordRow?> Records, int Exports, int Corrections, int Audit, int Manifests, int EngineCalls);

    private async Task<Snapshot> SnapshotAsync(string employeeId, params DateOnly[] months)
    {
        var records = new List<ExportRecordRow?>();
        foreach (var month in months)
            records.Add(await _fx.ReadRecordAsync(employeeId, month));
        return new Snapshot(
            records,
            await _fx.CountExportEventsAsync(employeeId),
            await _fx.CountCorrectionEventsAsync(employeeId),
            await _fx.CountAuditRowsAsync(employeeId, "PayrollExportGenerated"),
            await _fx.CountManifestsAsync(employeeId),
            _fx.Engine.CallCountFor(employeeId));
    }

    private static void AssertSameSnapshot(Snapshot before, Snapshot after)
    {
        Assert.Equal(before.Records.Count, after.Records.Count);
        for (var i = 0; i < before.Records.Count; i++)
            Assert.Equal(before.Records[i], after.Records[i]);
        Assert.Equal(before.Exports, after.Exports);
        Assert.Equal(before.Corrections, after.Corrections);
        Assert.Equal(before.Audit, after.Audit);
        Assert.Equal(before.Manifests, after.Manifests);
        Assert.Equal(before.EngineCalls, after.EngineCalls);
    }

    /// <summary>A new employee, employed throughout, every month from December 2025 to May 2026 APPROVED, and the stub's
    /// figures configured (January closes at 10; February +5, March +1, April +2, May +3 on whatever opening they are sent).</summary>
    private async Task<string> NewEmployeeAsync(string tag)
    {
        var employeeId = $"EMP-S147-STALE-{tag}";
        await _fx.SeedEmployeeAsync(employeeId);
        foreach (var month in new[] { Dec25, Jan, Feb, Mar, Apr, May })
            await _fx.SeedApprovedMonthAsync(employeeId, month);
        _fx.Engine.Configure(employeeId, Jan, closing: 10m);
        _fx.Engine.ConfigureOpeningDependent(employeeId, Feb, closingDelta: 5m);
        _fx.Engine.ConfigureOpeningDependent(employeeId, Mar, closingDelta: 1m);
        _fx.Engine.ConfigureOpeningDependent(employeeId, Apr, closingDelta: 2m);
        _fx.Engine.ConfigureOpeningDependent(employeeId, May, closingDelta: 3m);
        return employeeId;
    }

    private async Task ExportAsync(string employeeId, params DateOnly[] months)
    {
        foreach (var month in months)
            AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, month));
    }

    /// <summary>January is corrected: its closing moves 10 to 12 (the balance-only correction of S146 AC-C6), which leaves
    /// every month recorded after it out of date. The correction names February (HRP-003), the setup's own sanity check.</summary>
    private async Task CorrectJanuaryAsync(string employeeId)
    {
        _fx.Engine.Configure(employeeId, Jan, closing: 12m);
        var response = await _fx.RecalculateMonthAsync(employeeId, Jan);
        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadCorrection(response.Json);
        Assert.Equal<(decimal?, decimal?)>((10m, 12m), (result.ClosingFlexBalanceBefore, result.ClosingFlexBalanceAfter));
        Assert.Equal(Feb, result.NextPeriodToRecalculateStart);
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jan), 0m, 12m, "LEDGER_START");
    }

    private async Task RecalculateAsync(string employeeId, DateOnly month, decimal expectedOpening, decimal expectedClosing, string expectedProvenance)
    {
        AssertStatus(HttpStatusCode.OK, await _fx.RecalculateMonthAsync(employeeId, month));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, month), expectedOpening, expectedClosing, expectedProvenance);
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-229-1 — March after a corrected January
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-229-1 (kind F, Docker). January (0 to 10) and February (CHAINED 10 to 15) are exported, then January is corrected to
    /// close at 12, so February's recorded opening, 10, is stale (January now hands on 12). March's first export answers 409
    /// <c>payroll-prior-month-stale</c> naming <c>2026-02</c>, with the fixed date-free sentence, and NOTHING is written: no March
    /// record, no new event, audit row, manifest or correction event, January's and February's records unchanged, and the rule
    /// engine was not called for the employee.
    ///
    /// <para><b>Red conditions (argued).</b> The pre-S147 derivation reads only February's record (case 4a: February is employed
    /// and recorded, so March is <c>CHAINED 15</c> from February's closing), so March exports with 200 and writes a record
    /// built on a figure that depends on the stale February: <c>AssertStaleRefusal</c>'s status assertion (409) fails first.</para>
    /// </summary>
    [Fact]
    public async Task MarchFirstExport_AfterJanuaryCorrected_Refuses409_NamingFebruary_NothingWritten()
    {
        var employeeId = await NewEmployeeAsync("AC1");
        await ExportAsync(employeeId, Jan, Feb);
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Feb), 10m, 15m, "CHAINED");
        await CorrectJanuaryAsync(employeeId);
        var before = await SnapshotAsync(employeeId, Jan, Feb, Mar);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Mar);

        AssertStaleRefusal(response, employeeId, "2026-02");
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Mar));
        AssertSameSnapshot(before, await SnapshotAsync(employeeId, Jan, Feb, Mar));
    }

    /// <summary>
    /// Contract 7 — a CORRECTION of a later month is refused the same way (kind F, Docker). With the setup of AC-229-1 plus a
    /// recorded March (exported BEFORE January was corrected), a recalculation of March answers 409
    /// <c>payroll-prior-month-stale</c> naming <c>2026-02</c> and writes nothing; February's own recalculation, the named
    /// month, is not refused (200, CHAINED 12 to 17).
    ///
    /// <para><b>Red conditions (argued).</b> The pre-S147 admission of a correction reads only February's record, which is
    /// consistent with March's, so the correction answers 200 and rewrites March: the 409 assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_LaterMonth_WhileAnEarlierMonthIsStale_Refuses409_NamingTheEarliest_NothingWritten()
    {
        var employeeId = await NewEmployeeAsync("CORR");
        await ExportAsync(employeeId, Jan, Feb, Mar);
        await CorrectJanuaryAsync(employeeId);
        var before = await SnapshotAsync(employeeId, Jan, Feb, Mar);

        var refused = await _fx.RecalculateMonthAsync(employeeId, Mar);

        AssertStaleRefusal(refused, employeeId, "2026-02");
        AssertSameSnapshot(before, await SnapshotAsync(employeeId, Jan, Feb, Mar));

        await RecalculateAsync(employeeId, Feb, 12m, 17m, "CHAINED");
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-229-2 — the prefix shape (R-26)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>The prefix-shape setup: January (S), February, March and April exported with no registrations (zero-line
    /// months still write records), then January corrected to close at 12 (so February's recorded opening is stale and, because
    /// its employment is about to go, can never be repaired), then the employment start moved to 1 April (no registration is
    /// stranded, so the edit is allowed). February and March now hold no employed day.</summary>
    private async Task<string> PrefixShapeAsync(string tag)
    {
        var employeeId = await NewEmployeeAsync(tag);
        await ExportAsync(employeeId, Jan, Feb, Mar, Apr);
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Apr), 16m, 18m, "CHAINED");
        await CorrectJanuaryAsync(employeeId);
        await _fx.SetEmploymentWindowAsync(employeeId, Apr, null);
        return employeeId;
    }

    /// <summary>
    /// AC-229-2, the check (kind F, Docker; R-26). After the prefix-shape setup May's first export answers 409
    /// <c>payroll-prior-month-stale</c> naming <c>2026-04</c>, not February: April, recorded CHAINED 16, now follows a month with
    /// no employed day and so re-derives <c>REHIRE 0</c>; February (stale, and unrepairable because its correction derives
    /// nothing) and March are SKIPPED, and January is not re-derived (it is the ledger start). Nothing is written.
    ///
    /// <para><b>Red conditions (argued).</b> The pre-S147 derivation reads only April's record (employed, recorded), so May is
    /// <c>CHAINED 18</c> and exports with 200: the 409 assertion fails. (A walk WITHOUT the skip would name February: the month
    /// assertion fails.)</para>
    /// </summary>
    [Fact]
    public async Task PrefixShape_StartMovedToApril_MayNamesApril_FebruaryAndMarchSkipped()
    {
        var employeeId = await PrefixShapeAsync("AC2");
        var feb = await _fx.RequireRecordAsync(employeeId, Feb);
        Assert.Equal(10m, feb.Opening); // February is stale: January now closes at 12
        var before = await SnapshotAsync(employeeId, Jan, Feb, Mar, Apr, May);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, May);

        AssertStaleRefusal(response, employeeId, "2026-04");
        Assert.Null(await _fx.ReadRecordAsync(employeeId, May));
        AssertSameSnapshot(before, await SnapshotAsync(employeeId, Jan, Feb, Mar, Apr, May));
    }

    /// <summary>
    /// AC-229-2, the repair (kind B, Docker — boundary pin, holds today). Recalculating April, the named month, is not refused:
    /// it re-derives from March, which holds no employed day, and records <c>REHIRE 0</c> (closing 0 + 2 = 2); May then exports
    /// <c>CHAINED 2</c> (closing 5), on the repaired chain, with February still stale and skipped.
    ///
    /// <para><b>Kind and Red conditions.</b> B, not F: the pre-S147 code also repairs April and exports May. The discriminating
    /// assertions are the exact figures (April 0 to 2 REHIRE; May opening 2, not April's old closing 18), which a repair that
    /// kept April's old triple, or a May that opened from the stale chain, would fail.</para>
    /// </summary>
    [Fact]
    public async Task PrefixShape_AprilRecalculated_RecordsRehire_MayExportsChained()
    {
        var employeeId = await PrefixShapeAsync("AC2R");

        await RecalculateAsync(employeeId, Apr, expectedOpening: 0m, expectedClosing: 2m, expectedProvenance: "REHIRE");
        var response = await _fx.CalculateAndExportMonthAsync(employeeId, May);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Equal<(decimal?, decimal?, string?)>((2m, 5m, "CHAINED"), (result.OpeningFlexBalance, result.ClosingFlexBalance, result.FlexProvenance));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, May), 2m, 5m, "CHAINED");
        Assert.Equal(2m, _fx.Engine.TimelineCalls.Last(c => c.EmployeeId == employeeId && c.PeriodStart == May).PreviousFlexBalance);
        Assert.Equal(10m, (await _fx.RequireRecordAsync(employeeId, Feb)).Opening); // February is untouched: still stale, still skipped
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-229-3 — the repair of a plain chain
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-229-3 (kind B, Docker — holds today). With January corrected to 12 and February recalculated (now CHAINED 12 to 17),
    /// March's first export succeeds and opens <c>CHAINED 17</c>, February's CORRECTED closing, not January's 12, not February's
    /// old 15 and not February's stale opening 10; it closes at 18.
    ///
    /// <para><b>Kind and Red conditions.</b> B: the pre-S147 code also chains March from February's record, so no assertion
    /// fails there; the fact discriminates on the exact opening (17) and on the engine having been SENT 17, which a March that
    /// opened from a stale figure (15) fails.</para>
    /// </summary>
    [Fact]
    public async Task FebruaryRecalculated_MarchExportsChained_FromCorrectedClosing()
    {
        var employeeId = await NewEmployeeAsync("AC3");
        await ExportAsync(employeeId, Jan, Feb);
        await CorrectJanuaryAsync(employeeId);
        await RecalculateAsync(employeeId, Feb, expectedOpening: 12m, expectedClosing: 17m, expectedProvenance: "CHAINED");

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Mar);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Equal<(decimal?, decimal?, string?)>((17m, 18m, "CHAINED"), (result.OpeningFlexBalance, result.ClosingFlexBalance, result.FlexProvenance));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Mar), 17m, 18m, "CHAINED");
        Assert.Equal(17m, _fx.Engine.TimelineCalls.Last(c => c.EmployeeId == employeeId && c.PeriodStart == Mar).PreviousFlexBalance);
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-229-6 — the deep chain
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-229-6 (kind F, Docker). January, February and March are exported; January is corrected to 12. April's first export
    /// answers 409 naming <c>2026-02</c> (the EARLIEST stale month, not March, the previous month, whose record is consistent
    /// with February's stale closing), and writes nothing. The repair runs in order: February recalculated (CHAINED 12 to 17), the
    /// next attempt names <c>2026-03</c>; March recalculated (CHAINED 17 to 18), and April then exports <c>CHAINED 18</c>
    /// (closing 20).
    ///
    /// <para><b>Red conditions (argued).</b> The pre-S147 derivation reads only March's record, so April's first export answers
    /// 200 (<c>CHAINED 16</c> from March's stale closing): the first 409 assertion fails. A walk that stops at P names nothing
    /// and fails the same assertion; one that names the LATEST stale month names March and fails the month assertion.</para>
    /// </summary>
    [Fact]
    public async Task AprilFirstExport_AfterJanuaryCorrected_NamesFebruary_ThenRepairChains()
    {
        var employeeId = await NewEmployeeAsync("AC6");
        await ExportAsync(employeeId, Jan, Feb, Mar);
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Mar), 15m, 16m, "CHAINED");
        await CorrectJanuaryAsync(employeeId);
        var before = await SnapshotAsync(employeeId, Jan, Feb, Mar, Apr);

        AssertStaleRefusal(await _fx.CalculateAndExportMonthAsync(employeeId, Apr), employeeId, "2026-02");
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Apr));
        AssertSameSnapshot(before, await SnapshotAsync(employeeId, Jan, Feb, Mar, Apr));

        await RecalculateAsync(employeeId, Feb, expectedOpening: 12m, expectedClosing: 17m, expectedProvenance: "CHAINED");
        AssertStaleRefusal(await _fx.CalculateAndExportMonthAsync(employeeId, Apr), employeeId, "2026-03");
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Apr));

        await RecalculateAsync(employeeId, Mar, expectedOpening: 17m, expectedClosing: 18m, expectedProvenance: "CHAINED");
        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Apr);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Equal<(decimal?, decimal?, string?)>((18m, 20m, "CHAINED"), (result.OpeningFlexBalance, result.ClosingFlexBalance, result.FlexProvenance));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Apr), 18m, 20m, "CHAINED");
        Assert.Equal(18m, _fx.Engine.TimelineCalls.Last(c => c.EmployeeId == employeeId && c.PeriodStart == Apr).PreviousFlexBalance);
    }
}
