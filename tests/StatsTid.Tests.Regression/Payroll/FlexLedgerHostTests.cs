using System.Net;
using System.Text.Json;
using StatsTid.Infrastructure;
using StatsTid.Integrations.Payroll.Services;
using static StatsTid.Tests.Regression.Payroll.FlexLedgerHostFixture;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S146 / TASK-14606 (QUAL-188; AC-C1 .. AC-C3, AC-C5, AC-C6; rulings R-14, R-16, R-47, R-48) — the HOST-level facts of
/// the flex ledger: what an operator sees through the real Payroll host.
///
/// <para>
/// <b>Plain language.</b> Flex is the running balance of hours worked above or below the norm. Until S146 the closing
/// balance the Rule Engine computed for a month was checked and thrown away, and the next month opened at whatever number
/// the caller typed in. Now every paid month's export record stores its opening balance, its closing balance and where
/// the opening came from (the first paid month starts the ledger at 0; later months open at the previous month's recorded
/// closing; a month after a gap starts at 0 again), the caller can no longer supply an opening, a month that cannot
/// be chained is refused by name, and a correction both updates the month's balances and names the next month when the
/// closing it handed on has changed. These facts drive that through HTTP against a real Postgres.
/// </para>
///
/// <para>
/// <b>Harness.</b> <see cref="FlexLedgerHostFixture"/>: the real Payroll host; behind it <see cref="FlexLedgerStubEngine"/>
/// (a CONFIGURED, NON-ZERO closing per employee and month; the shared stub returns 0) or, for the facts marked
/// <c>_RealEngine</c>, the real in-process Rule Engine. Every fact seeds its own employee. Expected values come from the
/// refinement's R-14 table and the acceptance criteria, never from reading the code.
/// </para>
///
/// <para>
/// <b>Status.</b> Docker-gated: <b>CI-pending</b> (first seen green at CI-W2, R-39); never locally green. Each docstring
/// names its kind (F fails on the pre-S146 code, B boundary pin that holds today, R regression pin, N names a type or
/// member this sprint creates) and argues its RED, because the mutation proof for a Docker fact is argued, not run
/// (docs/AGENTS.md § "RED evidence").
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FlexLedgerHostTests : IClassFixture<FlexLedgerHostFixture>
{
    private static readonly DateOnly Mar = new(2026, 3, 1);
    private static readonly DateOnly Apr = new(2026, 4, 1);
    private static readonly DateOnly May = new(2026, 5, 1);
    private static readonly DateOnly Jun = new(2026, 6, 1);
    private static readonly DateOnly Jul = new(2026, 7, 1);

    private readonly FlexLedgerHostFixture _fx;

    public FlexLedgerHostTests(FlexLedgerHostFixture fixture) => _fx = fixture;

    // ═════════════════════════════════════════════════════════════════════
    // helpers
    // ═════════════════════════════════════════════════════════════════════

    private async Task<string> NewEmployeeAsync(string tag, DateOnly? start = null, DateOnly? end = null)
    {
        var employeeId = $"EMP-S146-{tag}";
        await _fx.SeedEmployeeAsync(employeeId, start, end);
        return employeeId;
    }

    private static void AssertStatus(HttpStatusCode expected, (HttpStatusCode Status, string Json) response) =>
        Assert.True(response.Status == expected, $"expected {(int)expected}, got {(int)response.Status}: {response.Json}");

    /// <summary>A refusal body: status, the discriminating kind, the named month (when given), success false, and a
    /// fixed sentence that never carries the employee id.</summary>
    private static void AssertRefusal(
        (HttpStatusCode Status, string Json) response, HttpStatusCode status, string kind, string employeeId, string? month = null)
    {
        AssertStatus(status, response);
        using var doc = JsonDocument.Parse(response.Json);
        var root = doc.RootElement;
        Assert.Equal(kind, Str(root, "kind"));
        Assert.False(root.GetProperty("success").GetBoolean());
        if (month is not null)
            Assert.Equal(month, Str(root, "month"));
        var error = Str(root, "error");
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.DoesNotContain(employeeId, error!);
    }

    private async Task AssertExportEventAsync(string employeeId, decimal opening, decimal closing, string provenance)
    {
        using var evt = await _fx.LatestEventAsync(employeeId, "PayrollExportGenerated");
        Assert.Equal(opening, Dec(evt.RootElement, "openingFlexBalance"));
        Assert.Equal(closing, Dec(evt.RootElement, "closingFlexBalance"));
        Assert.Equal(provenance, Str(evt.RootElement, "flexProvenance"));
        using var audit = await _fx.LatestAuditDetailsAsync(employeeId, "PayrollExportGenerated");
        Assert.Equal(opening, Dec(audit.RootElement, "openingFlexBalance"));
        Assert.Equal(closing, Dec(audit.RootElement, "closingFlexBalance"));
        Assert.Equal(provenance, Str(audit.RootElement, "flexProvenance"));
    }

    private static void AssertTriple(FlexLedgerHostFixture.ExportRecordRow record, decimal? opening, decimal? closing, string? provenance)
    {
        Assert.Equal(opening, record.Opening);
        Assert.Equal(closing, record.Closing);
        Assert.Equal(provenance, record.Provenance);
    }

    private static string DateText(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // ═════════════════════════════════════════════════════════════════════
    // R-36 — the two window reads agree
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// R-36 (kind N, Docker). One employee whose employment spell ENDS INSIDE the previous month P (10 May; M = June): the
    /// self-managed <c>GetWindowsAsync</c> and the new in-transaction overload <c>GetWindowsAsync(conn, tx, ...)</c>, both over
    /// <c>[P.start, M.end]</c> (<see cref="FlexLedgerOpening.WindowsReadRange"/>), return the SAME windows, and
    /// <c>EmployedIn(P)</c> is true on both (while <c>EmployedIn(M)</c> is false), so the admission read and the under-lock
    /// recheck cannot disagree about the chain.
    ///
    /// <para><b>Red conditions (argued).</b> The mutation "the in-tx overload reads <c>[M.start, M.end]</c>" (ignores its
    /// <c>from</c> argument) makes the single-spell filter drop the window that ended in May: the in-tx list is empty while
    /// the self-managed one holds the spell, so <c>Assert.Equal(selfManaged, inTx)</c> fails. Without the overload the fact does
    /// not compile (N); the mutation is the RED, not a compile error.</para>
    /// </summary>
    [Fact]
    public async Task EmployedIn_InTxAndSelfManaged_Agree()
    {
        var employeeId = await NewEmployeeAsync("WINDOWS", end: new DateOnly(2026, 5, 10));
        var (from, to) = FlexLedgerOpening.WindowsReadRange(Jun);
        var resolver = new EmploymentWindowResolver(_fx.Db);

        var selfManaged = await resolver.GetWindowsAsync(employeeId, from, to);
        IReadOnlyList<StatsTid.SharedKernel.Models.EmploymentWindow> inTx;
        await using (var conn = _fx.Db.Create())
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            inTx = await resolver.GetWindowsAsync(conn, tx, employeeId, from, to);
        }

        Assert.Single(selfManaged);
        Assert.Equal(selfManaged, inTx);
        Assert.True(FlexLedgerOpening.EmployedIn(May, selfManaged));
        Assert.True(FlexLedgerOpening.EmployedIn(May, inTx));
        Assert.False(FlexLedgerOpening.EmployedIn(Jun, selfManaged));
        Assert.False(FlexLedgerOpening.EmployedIn(Jun, inTx));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C1 — an exported employed month records its triple
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-C1, month WITH a payout (kind F, Docker). June opens at the ledger start (0) and the stub engine closes it at the
    /// cap, 100 h, paying 6.5 h out as <c>FLEX_PAYOUT</c>: the record, the response, the <c>PayrollExportGenerated</c> event and
    /// its audit row all say opening 0, closing 100, <c>LEDGER_START</c>; the record's lines hold the FLEX_PAYOUT line; the
    /// calculation ran on opening 0 (the stub saw <c>previousFlexBalance 0</c>).
    ///
    /// <para><b>Red conditions.</b> On the pre-S146 code the record has no balance columns (the SQL read of
    /// <c>opening_balance</c> fails with 42703 undefined_column) and the response carries no closing; the assertion
    /// <c>closing == 100</c> is the discriminating one (a zero-stub could never pass it).</para>
    /// </summary>
    [Fact]
    public async Task Export_MonthWithPayout_RecordsOpeningClosingProvenance_ClosingEqualsCap()
    {
        var employeeId = await NewEmployeeAsync("C1-PAYOUT");
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedWeekdayHoursAsync(employeeId, Jun, new DateOnly(2026, 6, 30));
        _fx.Engine.Configure(employeeId, Jun, closing: 100m, payoutHours: 6.5m);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Equal(0m, result.OpeningFlexBalance);
        Assert.Equal(100m, result.ClosingFlexBalance);
        Assert.Equal("LEDGER_START", result.FlexProvenance);
        var record = await _fx.RequireRecordAsync(employeeId, Jun);
        AssertTriple(record, 0m, 100m, "LEDGER_START");
        var payout = Assert.Single(record.CurrentLines, l => l.SourceTimeType == "FLEX_PAYOUT");
        Assert.Equal(6.5m, payout.Hours);
        await AssertExportEventAsync(employeeId, 0m, 100m, "LEDGER_START");
        Assert.Equal(0m, Assert.Single(_fx.Engine.TimelineCalls, c => c.EmployeeId == employeeId).PreviousFlexBalance);
    }

    /// <summary>
    /// AC-C1, month WITHOUT a payout (kind F, Docker). The stub closes June at a NEGATIVE, fractional -3.25 h with no payout:
    /// the record stores exactly that (no clamping to 0, no sign loss), there is no FLEX_PAYOUT line, and the response, event
    /// and audit row agree.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 there is no closing on the record at all (42703); the assertion
    /// <c>closing == -3.25</c> is what a "store 0" or "store the absolute value" implementation fails.</para>
    /// </summary>
    [Fact]
    public async Task Export_MonthWithoutPayout_RecordsClosingExactly()
    {
        var employeeId = await NewEmployeeAsync("C1-NOPAYOUT");
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedWeekdayHoursAsync(employeeId, Jun, new DateOnly(2026, 6, 30));
        _fx.Engine.Configure(employeeId, Jun, closing: -3.25m);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Equal(0m, result.OpeningFlexBalance);
        Assert.Equal(-3.25m, result.ClosingFlexBalance);
        var record = await _fx.RequireRecordAsync(employeeId, Jun);
        AssertTriple(record, 0m, -3.25m, "LEDGER_START");
        Assert.DoesNotContain(record.CurrentLines, l => l.SourceTimeType == "FLEX_PAYOUT");
        Assert.NotEmpty(record.CurrentLines);
        await AssertExportEventAsync(employeeId, 0m, -3.25m, "LEDGER_START");
    }

    /// <summary>
    /// AC-C1, an EMPLOYED ZERO-LINE month (kind F, Docker; R-3, R-50). June holds employed days but no recorded hours, so the
    /// calculation has no lines: the record is STILL written (lines empty, balances set: opening 0, closing -148), its content
    /// hash is the hash of the empty line set (<c>PayrollExportManifest.ComputeContentHash</c> over an empty list), and the
    /// event and audit row are emitted in the same transaction. The response reports no export lines.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 the route skipped the export when the line list was empty, so no record (and no
    /// event) existed: <c>RequireRecordAsync</c> fails ("expected a payroll_export_records row").</para>
    /// </summary>
    [Fact]
    public async Task Export_EmployedZeroLineMonth_WritesRecord_LinesEmpty_BalancesSet()
    {
        var employeeId = await NewEmployeeAsync("C1-ZEROLINE");
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        _fx.Engine.Configure(employeeId, Jun, closing: -148m);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.Empty(ReadResult(response.Json).ExportLines);
        var record = await _fx.RequireRecordAsync(employeeId, Jun);
        Assert.Empty(record.CurrentLines);
        Assert.Empty(record.OriginalLines);
        Assert.Equal(EmptyLinesHash, record.ContentHash);
        AssertTriple(record, 0m, -148m, "LEDGER_START");
        Assert.Equal(1, await _fx.CountExportEventsAsync(employeeId));
        await AssertExportEventAsync(employeeId, 0m, -148m, "LEDGER_START");
    }

    /// <summary>
    /// AC-C1, the REAL engine (kind F, Docker). A full-time HK employee works 7.40 h on every June 2026 weekday except
    /// Wednesday 10 June (21 of the 22 weekdays; June has no public holiday): the engine's own arithmetic is
    /// <c>closing = min(opening + worked - norm, cap)</c> = 0 + 155.40 - 162.80 = -7.40 (the cap is not reached), so the record
    /// and the response both hold opening 0, closing -7.40, <c>LEDGER_START</c>. Nothing here is the stub's number.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 the closing is not stored (42703) and not returned; a host that stored the stub's
    /// 0 or the opening instead of the engine's <c>NewBalance</c> fails <c>closing == -7.40</c>.</para>
    /// </summary>
    [Fact]
    public async Task Export_ClosingEqualsEnginesNewBalance_RealEngine()
    {
        var employeeId = await NewEmployeeAsync("C1-REAL");
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedWeekdayHoursAsync(employeeId, Jun, new DateOnly(2026, 6, 9));
        await _fx.SeedWeekdayHoursAsync(employeeId, new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 30));

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun, realEngine: true);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Equal(0m, result.OpeningFlexBalance);
        Assert.Equal(-7.40m, result.ClosingFlexBalance);
        Assert.Equal("LEDGER_START", result.FlexProvenance);
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), 0m, -7.40m, "LEDGER_START");
    }

    /// <summary>
    /// AC-C1, a month with NO employed day writes NO record (kind B, "boundary pin — holds today"; ADR-034 D3). The employee's
    /// employment starts on 15 July, so June holds no EMPLOYED day: the route answers 200, no export record exists, no
    /// <c>PayrollExportGenerated</c> event was emitted, and the result carries no flex members (nothing was settled).
    ///
    /// <para><b>Red conditions.</b> It holds today and must keep holding (the zero-line record of R-3 must not widen to a
    /// month with no employed day): a host that writes a record for it fails <c>CountRecords == 0</c>. Not a fail-first
    /// fact.</para>
    /// </summary>
    [Fact]
    public async Task NoEmployedDay_WritesNoRecord()
    {
        var employeeId = await NewEmployeeAsync("C1-NOEMPLOYED", start: new DateOnly(2026, 7, 15));
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Null(result.OpeningFlexBalance);
        Assert.Null(result.ClosingFlexBalance);
        Assert.Null(result.FlexProvenance);
        Assert.Equal(0, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C2 — an opening balance in the body is refused
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-C2, /calculate-and-export (kind F, Docker). A body carrying <c>previousFlexBalance</c> (0 AND 12.5: zero is a value)
    /// answers 400 <c>payroll-opening-balance-not-accepted</c> with a fixed sentence. The employee has NO approval row, so a
    /// handler that moved the refusal after the approval lookup would answer 403: the 400 proves the refusal precedes the
    /// approval lookup's answer. The rule engine, the record table and the manifest table are untouched. The same request
    /// without the member answers 403 (the period is not approved), the positive control. That NO read at all happens before
    /// the refusal is verified by code reading (<c>Program.cs:364-370</c>), not by this fact.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 the member was a required <c>decimal</c> and accepted: the response is 403 (or a
    /// calculation), never 400, so <c>AssertRefusal</c> fails at the status.</para>
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_BodyCarriesOpeningBalance_Returns400()
    {
        var employeeId = await NewEmployeeAsync("C2-EXPORT");

        var zero = await _fx.CalculateAndExportAsync(employeeId, Jun, new DateOnly(2026, 6, 30), previousFlexBalance: 0m);
        var some = await _fx.CalculateAndExportAsync(employeeId, Jun, new DateOnly(2026, 6, 30), previousFlexBalance: 12.5m);
        var absent = await _fx.CalculateAndExportAsync(employeeId, Jun, new DateOnly(2026, 6, 30));

        AssertRefusal(zero, HttpStatusCode.BadRequest, "payroll-opening-balance-not-accepted", employeeId);
        AssertRefusal(some, HttpStatusCode.BadRequest, "payroll-opening-balance-not-accepted", employeeId);
        AssertStatus(HttpStatusCode.Forbidden, absent);
        Assert.Equal(0, _fx.Engine.CallCountFor(employeeId));
        Assert.Equal(0, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(0, await _fx.CountManifestsAsync(employeeId));
    }

    /// <summary>
    /// AC-C2, /recalculate (kind F, Docker). A body carrying <c>previousFlexBalance</c> (0 included) answers 400
    /// <c>payroll-opening-balance-not-accepted</c>. The request carries a token that is ALREADY DELIVERED (pre-marked in
    /// <c>outbox_messages</c> as the idempotency guard reads it), so a handler that moved the refusal after the idempotency
    /// lookup would answer 200 "Correction already processed", not 400: the 400 proves the refusal precedes the idempotency
    /// lookup's answer. The month's record (a ledger record closing at 10), the correction events, the audit rows and the marker
    /// (still exactly the one pre-mark) are untouched, and the rule engine was never called. That NO read at all happens before
    /// the refusal is verified by code reading (<c>Program.cs:613-619</c>), not by this fact.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 the member was accepted and ignored: the correction ran (status 200, a correction
    /// event written), so the status assertion fails first.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_BodyCarriesOpeningBalance_Returns400()
    {
        var employeeId = await NewEmployeeAsync("C2-RECALC");
        await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 10m);
        _fx.Engine.Configure(employeeId, Jun, closing: 99m);
        var token = Guid.NewGuid();
        await _fx.PreMarkTokenDeliveredAsync(token);
        var before = await _fx.RequireRecordAsync(employeeId, Jun);

        var response = await _fx.RecalculateAsync(
            employeeId, Jun, new DateOnly(2026, 6, 30), token, previousFlexBalance: 0m);

        AssertRefusal(response, HttpStatusCode.BadRequest, "payroll-opening-balance-not-accepted", employeeId);
        var after = await _fx.RequireRecordAsync(employeeId, Jun);
        Assert.Equal(before, after);
        AssertTriple(after, 0m, 10m, "LEDGER_START");
        Assert.Equal(0, await _fx.CountCorrectionEventsAsync(employeeId));
        Assert.Equal(0, await _fx.CountAuditRowsAsync(employeeId, "RetroactiveCorrectionRequested"));
        Assert.Equal(1, await _fx.CountAsync(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t", ("t", token))); // the pre-mark only
        Assert.Equal(0, _fx.Engine.CallCountFor(employeeId));

        // 5a c2 positive control: the SAME pre-marked token on a VALID whole-month request is short-circuited by the
        // idempotency guard (200 "Correction already processed"), so the pre-mark is live and the 400 above cannot be vacuous.
        var control = await _fx.RecalculateMonthAsync(employeeId, Jun, token);
        AssertStatus(HttpStatusCode.OK, control);
        Assert.Contains("Correction already processed", control.Json);
        Assert.Equal(1, await _fx.CountAsync(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t", ("t", token)));
        Assert.Equal(0, _fx.Engine.CallCountFor(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C3 (a) — the first calculating-route month starts the ledger at 0
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-C3 (a), the export route with raw history (kind F, Docker). The employee has RAW records for March and April (what
    /// the raw routes wrote; no balances) and June is unrecorded: raw records do not count, so June is the first month the
    /// ledger pays and is <c>LEDGER_START 0</c> (never refused, never chained from a raw month), on the record, in the response
    /// and in the event; the engine was sent opening 0.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 there is no provenance and no stored opening (42703); a derivation that counted
    /// raw months as ledger records would refuse (409 prior-month-not-recorded) or chain, tripping the status or the
    /// provenance assertion.</para>
    /// </summary>
    [Fact]
    public async Task Export_FirstCalculatingMonth_WithRawHistory_IsLedgerStartZero_OnRecordResultAndEvent()
    {
        var employeeId = await NewEmployeeAsync("C3A-EXPORT");
        await _fx.SeedRawRecordAsync(employeeId, Mar);
        await _fx.SeedRawRecordAsync(employeeId, Apr);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedWeekdayHoursAsync(employeeId, Jun, new DateOnly(2026, 6, 30));
        _fx.Engine.Configure(employeeId, Jun, closing: 4.5m);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Equal<(decimal?, decimal?, string?)>((0m, 4.5m, "LEDGER_START"), (result.OpeningFlexBalance, result.ClosingFlexBalance, result.FlexProvenance));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), 0m, 4.5m, "LEDGER_START");
        await AssertExportEventAsync(employeeId, 0m, 4.5m, "LEDGER_START");
        Assert.Equal(0m, Assert.Single(_fx.Engine.TimelineCalls, c => c.EmployeeId == employeeId).PreviousFlexBalance);
    }

    /// <summary>
    /// AC-C3 (a), the correction route with raw history (kind F, Docker; R-14 case 2 on the correction route). June has a RAW
    /// record and the employee has no ledger record at all: /recalculate derives <c>LEDGER_START 0</c>, fills the record's
    /// nulls (opening 0, closing 6, <c>LEDGER_START</c>) and its event carries opening used 0, no closing before and closing
    /// after 6.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 the correction event carried no flex member and the record had no balance
    /// columns; <c>OpeningFlexBalanceUsed == 0</c> and the filled record are the discriminating assertions.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_RawRecordedMonth_FirstCalculatingMonth_IsLedgerStartZero()
    {
        var employeeId = await NewEmployeeAsync("C3A-RECALC");
        await _fx.SeedRawRecordAsync(employeeId, Jun);
        _fx.Engine.Configure(employeeId, Jun, closing: 6m);

        var response = await _fx.RecalculateMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadCorrection(response.Json);
        Assert.Equal(0m, result.OpeningFlexBalanceUsed);
        Assert.Null(result.ClosingFlexBalanceBefore);
        Assert.Equal(6m, result.ClosingFlexBalanceAfter);
        Assert.Equal("LEDGER_START", result.FlexProvenance);
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), 0m, 6m, "LEDGER_START");
        using var evt = await _fx.LatestEventAsync(employeeId, "RetroactiveCorrectionRequested");
        Assert.Equal(0m, Dec(evt.RootElement, "openingFlexBalanceUsed"));
        Assert.False(Has(evt.RootElement, "closingFlexBalanceBefore"));
        Assert.Equal(6m, Dec(evt.RootElement, "closingFlexBalanceAfter"));
        Assert.Equal("LEDGER_START", Str(evt.RootElement, "flexProvenance"));
    }

    /// <summary>
    /// AC-C3 (a), the export route against a raw-recorded month (kind F, Docker; R-16, reviewer c3 N3). June already has a RAW
    /// record whose lines (none) equal what the calculation produces, so the CONTENT hash is the same; but the calculating route
    /// would record <c>LEDGER_START 0 -> closing</c> and the raw record holds no balances: the triple differs, so the answer
    /// is 409 <c>payroll-export-conflict</c> ("the balance changed; use a correction"), and the raw record is left as it was.
    ///
    /// <para><b>Red conditions.</b> Without the triple compare (the pre-S146 same-hash no-op) the response is 200 and the
    /// raw record is untouched: the status assertion fails. This is the fact the brief names as expecting the 409.</para>
    /// </summary>
    [Fact]
    public async Task Export_RawRecordedMonth_Returns409_ExportConflict()
    {
        var employeeId = await NewEmployeeAsync("C3A-CONFLICT");
        await _fx.SeedRawRecordAsync(employeeId, Jun);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        _fx.Engine.Configure(employeeId, Jun, closing: 2m);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.Conflict, response);
        using (var doc = JsonDocument.Parse(response.Json))
            Assert.Equal("payroll-export-conflict", Str(doc.RootElement, "kind"));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), null, null, null);
        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C3 (b) — a first export before the ledger start is refused
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-C3 (b) (kind F, Docker). The ledger starts in June; an export of the UNRECORDED month April is refused 409
    /// <c>payroll-export-out-of-order</c> naming the ledger start (<c>2026-06</c>), with a fixed sentence (no employee id), and
    /// nothing was calculated, written or exported: one record only (June's), no manifest, no rule-engine call.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 April simply exported (200, a second record): the status assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task Export_UnrecordedMonthBeforeLedgerStart_Returns409_OutOfOrder_NamingTheStart()
    {
        var employeeId = await NewEmployeeAsync("C3B");
        await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 1m);
        await _fx.SeedApprovedMonthAsync(employeeId, Mar);
        await _fx.SeedApprovedMonthAsync(employeeId, Apr);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Apr);

        AssertRefusal(response, HttpStatusCode.Conflict, "payroll-export-out-of-order", employeeId, month: "2026-06");
        Assert.Equal(1, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(0, await _fx.CountManifestsAsync(employeeId));
        Assert.Equal(0, _fx.Engine.CallCountFor(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C3 (c) — a re-run and a correction keep the ledger start's opening
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-C3 (c) (kind F, Docker). June is the ledger start with a recorded opening of 7.5 (a start is never re-derived) and
    /// closing 10. (1) A same-lines RE-RUN with the engine again closing at 10 is a no-op that keeps opening 7.5 (200, no new
    /// event, the record unchanged); (2) a CORRECTION with the engine now closing at 12 keeps opening 7.5, reports opening used
    /// 7.5, closing before 10 and after 12, and stores 7.5 -> 12. Every timeline call was sent opening 7.5.
    ///
    /// <para><b>Red conditions.</b> A derivation that re-derives a start (0 instead of the recorded 7.5) sends opening 0 to the
    /// engine and, on the re-run, 409s on the triple; on the correction it reports opening used 0 and stores 0 -> 12: the
    /// assertions on 7.5 fail. Pre-S146 there is no stored opening at all (42703).</para>
    /// </summary>
    [Fact]
    public async Task LedgerStartMonth_ReRunAndCorrection_BothKeepItsOpening()
    {
        var employeeId = await NewEmployeeAsync("C3C");
        await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 10m, opening: 7.5m);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        _fx.Engine.Configure(employeeId, Jun, closing: 10m);

        var rerun = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, rerun);
        Assert.Equal(7.5m, ReadResult(rerun.Json).OpeningFlexBalance);
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), 7.5m, 10m, "LEDGER_START");
        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));

        _fx.Engine.Configure(employeeId, Jun, closing: 12m);
        var correction = await _fx.RecalculateMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, correction);
        var result = ReadCorrection(correction.Json);
        Assert.Equal(7.5m, result.OpeningFlexBalanceUsed);
        Assert.Equal(10m, result.ClosingFlexBalanceBefore);
        Assert.Equal(12m, result.ClosingFlexBalanceAfter);
        Assert.Equal("LEDGER_START", result.FlexProvenance);
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), 7.5m, 12m, "LEDGER_START");
        Assert.All(_fx.Engine.TimelineCalls.Where(c => c.EmployeeId == employeeId), c => Assert.Equal(7.5m, c.PreviousFlexBalance));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C3 (d) — M > S with P employed
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-C3 (d), P is a ledger record (kind F, Docker). The ledger starts in June (closing 8.25); July is exported: it opens
    /// at June's recorded CLOSING, <c>CHAINED 8.25</c> (the engine was sent 8.25), and closes at the stub's 9.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 the opening was the caller's; a chain that opened July at 0, or at June's OPENING
    /// (0), fails <c>opening == 8.25</c>.</para>
    /// </summary>
    [Fact]
    public async Task Export_PreviousMonthEmployedAndRecorded_ChainsFromItsClosing()
    {
        var employeeId = await NewEmployeeAsync("C3D-CHAIN");
        await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 8.25m);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedApprovedMonthAsync(employeeId, Jul);
        _fx.Engine.Configure(employeeId, Jul, closing: 9m);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jul);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Equal<(decimal?, decimal?, string?)>((8.25m, 9m, "CHAINED"), (result.OpeningFlexBalance, result.ClosingFlexBalance, result.FlexProvenance));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jul), 8.25m, 9m, "CHAINED");
        await AssertExportEventAsync(employeeId, 8.25m, 9m, "CHAINED");
        Assert.Equal(8.25m, Assert.Single(_fx.Engine.TimelineCalls, c => c.EmployeeId == employeeId).PreviousFlexBalance);
    }

    /// <summary>
    /// AC-C3 (d), P is absent (kind F, Docker). The ledger starts in March; June's previous month, May, is employed but has
    /// NO export record: 409 <c>payroll-prior-month-not-recorded</c> naming <c>2026-05</c> with the "export it first" message,
    /// and nothing was calculated or written.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 June exported with a caller-supplied opening (200): the status fails. A derivation
    /// that treated the gap as REHIRE would answer 200 with opening 0.</para>
    /// </summary>
    [Fact]
    public async Task Export_PreviousMonthEmployedButUnrecorded_Returns409_NamesP_ExportFirst()
    {
        var employeeId = await NewEmployeeAsync("C3D-ABSENT");
        await _fx.SeedLedgerRecordAsync(employeeId, Mar, closing: 3m);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertRefusal(response, HttpStatusCode.Conflict, "payroll-prior-month-not-recorded", employeeId, month: "2026-05");
        using (var doc = JsonDocument.Parse(response.Json))
        {
            var error = Str(doc.RootElement, "error")!;
            Assert.Contains("Export that month first", error);
            Assert.DoesNotContain("Correct that month first", error);
        }
        Assert.Equal(1, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(0, _fx.Engine.CallCountFor(employeeId));
    }

    /// <summary>
    /// AC-C3 (d), P is a RAW record (kind F, Docker). As above but May holds a raw record: the same 409 naming
    /// <c>2026-05</c>, with the "correct it first" message (a raw month is filled by a correction, not by an export).
    ///
    /// <para><b>Red conditions.</b> A core that gives one message for both causes fails the <c>Contains</c>/<c>DoesNotContain</c>
    /// pair; pre-S146 the month exported (200).</para>
    /// </summary>
    [Fact]
    public async Task Export_PreviousMonthEmployedButRaw_Returns409_NamesP_CorrectFirst()
    {
        var employeeId = await NewEmployeeAsync("C3D-RAW");
        await _fx.SeedLedgerRecordAsync(employeeId, Mar, closing: 3m);
        await _fx.SeedRawRecordAsync(employeeId, May);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertRefusal(response, HttpStatusCode.Conflict, "payroll-prior-month-not-recorded", employeeId, month: "2026-05");
        using (var doc = JsonDocument.Parse(response.Json))
        {
            var error = Str(doc.RootElement, "error")!;
            Assert.Contains("Correct that month first", error);
            Assert.DoesNotContain("Export that month first", error);
        }
        Assert.Equal(0, _fx.Engine.CallCountFor(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C3 (e) — REHIRE, and the Monday-start month
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-C3 (e), a Monday-start month under continuous employment is CHAINED (kind F, Docker; R-28). June 2026 opens on a
    /// MONDAY, so the previous-month "context days" machinery finds no May day in its opening week; but the employee has been
    /// employed throughout, so May IS employed and recorded (closing 4): June is <c>CHAINED 4</c>, never <c>REHIRE 0</c>.
    ///
    /// <para><b>Red conditions.</b> A derivation that read employment through the opening-week context days would take May as
    /// "not employed" and answer REHIRE 0: the assertion <c>opening == 4</c> and <c>CHAINED</c> fails (and the engine would be
    /// sent 0). Pre-S146 there is no provenance.</para>
    /// </summary>
    [Fact]
    public async Task Export_MondayStartMonth_UnderContinuousEmployment_IsChained_NeverRehire()
    {
        var employeeId = await NewEmployeeAsync("C3E-MONDAY");
        Assert.Equal(DayOfWeek.Monday, Jun.DayOfWeek);
        await _fx.SeedLedgerRecordAsync(employeeId, May, closing: 4m);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        _fx.Engine.Configure(employeeId, Jun, closing: 5m);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Equal<(decimal?, decimal?, string?)>((4m, 5m, "CHAINED"), (result.OpeningFlexBalance, result.ClosingFlexBalance, result.FlexProvenance));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), 4m, 5m, "CHAINED");
        Assert.Equal(4m, Assert.Single(_fx.Engine.TimelineCalls, c => c.EmployeeId == employeeId).PreviousFlexBalance);
    }

    /// <summary>
    /// AC-C3 (e), a previous month with no employed day is REHIRE 0 (kind F, Docker). The ledger started in March (closing 5),
    /// but the employee's employment now starts on 10 June (a hire after a gap): May holds no employed day, so June opens at
    /// 0, <c>REHIRE</c>, not at March's balance and not refused.
    ///
    /// <para><b>Red conditions.</b> A chain that ignores employment refuses (409 prior-month-not-recorded, May unrecorded) or
    /// chains; the status and provenance assertions fail. Pre-S146 there is no provenance.</para>
    /// </summary>
    [Fact]
    public async Task Export_PreviousMonthNotEmployed_IsRehireZero()
    {
        var employeeId = await NewEmployeeAsync("C3E-REHIRE", start: new DateOnly(2026, 6, 10));
        await _fx.SeedLedgerRecordAsync(employeeId, Mar, closing: 5m);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedWeekdayHoursAsync(employeeId, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 30));
        _fx.Engine.Configure(employeeId, Jun, closing: -2m);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Equal<(decimal?, decimal?, string?)>((0m, -2m, "REHIRE"), (result.OpeningFlexBalance, result.ClosingFlexBalance, result.FlexProvenance));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), 0m, -2m, "REHIRE");
        Assert.Equal(0m, Assert.Single(_fx.Engine.TimelineCalls, c => c.EmployeeId == employeeId).PreviousFlexBalance);
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C3 (f) — raw months on the correction route
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-C3 (f), a raw month AFTER the ledger start (kind F, Docker). May is the ledger start (closing 3) and June holds a
    /// RAW record: /recalculate fills June's nulls from its chain, opening used 3 (<c>CHAINED</c>), closing after 5, and the
    /// record becomes (3, 5, CHAINED).
    ///
    /// <para><b>Red conditions.</b> Pre-S146 a correction stored no balances: the record stays (null, null, null), tripping
    /// <c>AssertTriple</c>.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_RawMonthAfterLedgerStart_FillsItsNulls()
    {
        var employeeId = await NewEmployeeAsync("C3F-FILL");
        await _fx.SeedLedgerRecordAsync(employeeId, May, closing: 3m);
        await _fx.SeedRawRecordAsync(employeeId, Jun);
        _fx.Engine.Configure(employeeId, Jun, closing: 5m);

        var response = await _fx.RecalculateMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadCorrection(response.Json);
        Assert.Equal(3m, result.OpeningFlexBalanceUsed);
        Assert.Null(result.ClosingFlexBalanceBefore);
        Assert.Equal(5m, result.ClosingFlexBalanceAfter);
        Assert.Equal("CHAINED", result.FlexProvenance);
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), 3m, 5m, "CHAINED");
    }

    /// <summary>
    /// AC-C3 (f), a raw month BEFORE the ledger start runs UNTRACKED (kind F, Docker; R-14 case 3b, reviewer c3 W4). The ledger
    /// starts in June. May holds a RAW record whose baseline carries a 4 h <c>FLEX_PAYOUT</c> line, and the engine now closes
    /// May at 50 with a 6 h payout. Under UNTRACKED the payout is excluded from BOTH sides of the correction diff: no
    /// correction line is emitted (the baseline payout is not clawed back, the new one is not paid), the balances stay null,
    /// the baseline payout line stays in the record, the response and the event say UNTRACKED (the response also carries the
    /// plain-words note and no balance), and June (exported) is not named as stale.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 the diff included FLEX_PAYOUT: a correction line (-4 h reversing the baseline
    /// payout, or +6 h) is emitted, so <c>CorrectionLines</c> is not empty; a tracked treatment would also store balances.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_RawMonthBeforeLedgerStart_RunsUntracked_NoFlexCorrectionLine()
    {
        var employeeId = await NewEmployeeAsync("C3F-UNTRACKED");
        await _fx.SeedApprovedMonthAsync(employeeId, Apr);
        await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 1m);
        await _fx.SeedRawRecordAsync(employeeId, May, new[] { PayoutLine(employeeId, new DateOnly(2026, 5, 31), 4m) });
        _fx.Engine.Configure(employeeId, May, closing: 50m, payoutHours: 6m);

        var response = await _fx.RecalculateMonthAsync(employeeId, May);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadCorrection(response.Json);
        Assert.Empty(result.CorrectionLines);
        Assert.Null(result.OpeningFlexBalanceUsed);
        Assert.Null(result.ClosingFlexBalanceBefore);
        Assert.Null(result.ClosingFlexBalanceAfter);
        Assert.Equal("UNTRACKED", result.FlexProvenance);
        Assert.Equal("flex not tracked: the month precedes the ledger start 2026-06", result.FlexNote);
        Assert.Null(result.NextPeriodToRecalculateStart);

        var record = await _fx.RequireRecordAsync(employeeId, May);
        AssertTriple(record, null, null, null);
        var kept = Assert.Single(record.CurrentLines, l => l.SourceTimeType == "FLEX_PAYOUT");
        Assert.Equal(4m, kept.Hours);
        using var evt = await _fx.LatestEventAsync(employeeId, "RetroactiveCorrectionRequested");
        Assert.Equal("UNTRACKED", Str(evt.RootElement, "flexProvenance"));
        Assert.False(Has(evt.RootElement, "openingFlexBalanceUsed"));
        Assert.False(Has(evt.RootElement, "closingFlexBalanceAfter"));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C5 — a correction names the next month when the closing changed
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-C5 (kind F, Docker; R-10). May 2026 ENDS ON A SUNDAY (31 May) and June is exported (CHAINED from May's closing 10).
    /// A correction of May that moves the closing 10 -> 15 names June (<c>nextPeriodToRecalculateStart 2026-06-01</c>, end
    /// <c>2026-06-30</c>) even though the Sunday test alone would not; its result and event carry the opening used (0), the
    /// closing before (10) and after (15); May's record is updated to 15 and June's record is left alone (ADR-013: nothing
    /// cascades). A second correction that leaves the closing at 15 names nothing (Sunday end, closing unchanged).
    ///
    /// <para><b>Red conditions.</b> Pre-S146 the next month was named only by the Sunday test (May ends on a Sunday: never):
    /// the first <c>nextPeriodToRecalculateStart</c> assertion fails. A hint that always names M+1 fails the second
    /// correction's null assertion.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_ClosingChanged_SundayEndingMay2026_NamesJune()
    {
        var employeeId = await NewEmployeeAsync("C5");
        Assert.Equal(DayOfWeek.Sunday, new DateOnly(2026, 5, 31).DayOfWeek);
        await _fx.SeedApprovedMonthAsync(employeeId, Apr);
        await _fx.SeedApprovedMonthAsync(employeeId, May);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        _fx.Engine.Configure(employeeId, May, closing: 10m);
        _fx.Engine.Configure(employeeId, Jun, closing: 20m);
        AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, May));
        AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, Jun));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), 10m, 20m, "CHAINED");

        _fx.Engine.Configure(employeeId, May, closing: 15m);
        var changed = await _fx.RecalculateMonthAsync(employeeId, May);

        AssertStatus(HttpStatusCode.OK, changed);
        var result = ReadCorrection(changed.Json);
        Assert.Equal(Jun, result.NextPeriodToRecalculateStart);
        Assert.Equal(new DateOnly(2026, 6, 30), result.NextPeriodToRecalculateEnd);
        Assert.Equal<(decimal?, decimal?, decimal?)>((0m, 10m, 15m), (result.OpeningFlexBalanceUsed, result.ClosingFlexBalanceBefore, result.ClosingFlexBalanceAfter));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, May), 0m, 15m, "LEDGER_START");
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), 10m, 20m, "CHAINED");
        using (var evt = await _fx.LatestEventAsync(employeeId, "RetroactiveCorrectionRequested"))
        {
            Assert.Equal(0m, Dec(evt.RootElement, "openingFlexBalanceUsed"));
            Assert.Equal(10m, Dec(evt.RootElement, "closingFlexBalanceBefore"));
            Assert.Equal(15m, Dec(evt.RootElement, "closingFlexBalanceAfter"));
            Assert.Equal("2026-06-01", Str(evt.RootElement, "nextPeriodToRecalculateStart"));
            Assert.Equal("2026-06-30", Str(evt.RootElement, "nextPeriodToRecalculateEnd"));
        }

        var unchanged = await _fx.RecalculateMonthAsync(employeeId, May);

        AssertStatus(HttpStatusCode.OK, unchanged);
        var second = ReadCorrection(unchanged.Json);
        Assert.Equal<(decimal?, decimal?)>((15m, 15m), (second.ClosingFlexBalanceBefore, second.ClosingFlexBalanceAfter));
        Assert.Null(second.NextPeriodToRecalculateStart);
        Assert.Null(second.NextPeriodToRecalculateEnd);
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-C6 — a re-run compares the whole triple
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-C6, same lines, opening changed (kind F, Docker; R-16). June (closing 10) and July (CHAINED 10, closing 20) are
    /// exported. A same-lines re-run of July is first a no-op (200, no new event: the positive control). Then June is
    /// CORRECTED to close at 15, so July's derived opening is now 15 while its record holds 10: the same re-run answers 409
    /// <c>payroll-export-conflict</c> and July's record is unchanged.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 the re-run compared the content hash only: both re-runs answer 200 (a silent
    /// no-op over a stale balance), so the 409 assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task ReRun_SameLines_OpeningChanged_Returns409()
    {
        var employeeId = await NewEmployeeAsync("C6-OPENING");
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedApprovedMonthAsync(employeeId, Jul);
        _fx.Engine.Configure(employeeId, Jun, closing: 10m);
        _fx.Engine.Configure(employeeId, Jul, closing: 20m);
        AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, Jun));
        AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, Jul));
        var events = await _fx.CountExportEventsAsync(employeeId);
        Assert.Equal(2, events);

        AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, Jul));
        Assert.Equal(events, await _fx.CountExportEventsAsync(employeeId));

        _fx.Engine.Configure(employeeId, Jun, closing: 15m);
        AssertStatus(HttpStatusCode.OK, await _fx.RecalculateMonthAsync(employeeId, Jun));
        var rerun = await _fx.CalculateAndExportMonthAsync(employeeId, Jul);

        AssertStatus(HttpStatusCode.Conflict, rerun);
        using (var doc = JsonDocument.Parse(rerun.Json))
            Assert.Equal("payroll-export-conflict", Str(doc.RootElement, "kind"));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jul), 10m, 20m, "CHAINED");
        Assert.Equal(events, await _fx.CountExportEventsAsync(employeeId));
    }

    /// <summary>
    /// AC-C6, an employed ZERO-LINE month re-run after a part-time fraction change (kind F, Docker; real engine). June has
    /// no recorded hours, so its record has no lines and closes at minus its norm (22 weekdays x 7.40 = 162.80 at fraction 1.0, so
    /// below zero; the exact figure is the engine's, not this fact's claim).
    /// After the employee's fraction drops to 0.5 the same request has the same (empty) lines but a different closing:
    /// 409 <c>payroll-export-conflict</c>, and the record is unchanged.
    ///
    /// <para><b>Red conditions.</b> Pre-S146 the fact fails EARLIER than the 409: the first export wrote no record for a
    /// zero-line month, so <c>RequireRecordAsync</c> ("expected a payroll_export_records row") fails, and the record has no
    /// balance columns. After S146 the 409 assertion is what a triple compare that skipped zero-line months (a hash-only no-op)
    /// fails: it would answer 200.</para>
    /// </summary>
    [Fact]
    public async Task ReRun_ZeroLineMonth_AfterFractionChange_Returns409()
    {
        var employeeId = await NewEmployeeAsync("C6-FRACTION");
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, Jun, realEngine: true));
        var before = await _fx.RequireRecordAsync(employeeId, Jun);
        Assert.Empty(before.CurrentLines);
        Assert.Equal(0m, before.Opening);
        Assert.Equal("LEDGER_START", before.Provenance);
        Assert.True(before.Closing < 0m, $"a month with no recorded hours closes below zero (minus its norm); got {before.Closing}");

        await _fx.SetPartTimeFractionAsync(employeeId, 0.5m);
        var rerun = await _fx.CalculateAndExportMonthAsync(employeeId, Jun, realEngine: true);

        AssertStatus(HttpStatusCode.Conflict, rerun);
        using (var doc = JsonDocument.Parse(rerun.Json))
            Assert.Equal("payroll-export-conflict", Str(doc.RootElement, "kind"));
        Assert.Equal(before, await _fx.RequireRecordAsync(employeeId, Jun));
        Assert.Equal(1, await _fx.CountExportEventsAsync(employeeId));
    }

    /// <summary>
    /// AC-C6, an UNTRACKED raw month re-run (kind B, "boundary pin — holds today"; R-26). The ledger starts in June; May holds a
    /// raw record with no lines and the calculation again produces none, so the SAME-hash raw re-run is already a no-op. The
    /// discriminating assertion is what the response says: <c>flexProvenance == "UNTRACKED"</c> with no balances and the
    /// plain-words note, while the record's balances stay null and no event was emitted.
    ///
    /// <para><b>Red conditions.</b> It holds today (the no-op) and must keep holding; a triple compare that took UNTRACKED's
    /// derived 0 and the computed closing as different from the all-null record would answer 409 and trip the status
    /// assertion, and a response without the provenance trips the discriminating one.</para>
    /// </summary>
    [Fact]
    public async Task ReRun_UntrackedRawMonth_IsNoOp()
    {
        var employeeId = await NewEmployeeAsync("C6-UNTRACKED");
        await _fx.SeedApprovedMonthAsync(employeeId, Apr);
        await _fx.SeedApprovedMonthAsync(employeeId, May);
        await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 1m);
        await _fx.SeedRawRecordAsync(employeeId, May);
        _fx.Engine.Configure(employeeId, May, closing: 33m);
        var before = await _fx.RequireRecordAsync(employeeId, May);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, May);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadResult(response.Json);
        Assert.Equal("UNTRACKED", result.FlexProvenance);
        Assert.Null(result.OpeningFlexBalance);
        Assert.Null(result.ClosingFlexBalance);
        using (var doc = JsonDocument.Parse(response.Json))
            Assert.Equal("flex not tracked: the month precedes the ledger start 2026-06", Str(doc.RootElement, "flexNote"));
        var after = await _fx.RequireRecordAsync(employeeId, May);
        AssertTriple(after, null, null, null);
        Assert.Equal(before, after);
        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));
    }

    /// <summary>
    /// AC-C6, a correction that changes only the balance (kind F, Docker). June (lines none) closed at 10; the engine now
    /// closes it at 13. There is no correction LINE, but the record is updated to 13, one correction event is emitted
    /// (opening used 0, closing before 10, after 13) with its audit row, and no next month is named (June does not end on a
    /// Sunday and July is not exported).
    ///
    /// <para><b>Red conditions.</b> Pre-S146 a correction with no line difference stored nothing about the balance (the record
    /// keeps 10, no flex member on the event): the record assertion fails.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_BalanceOnly_RecordsAndEmits()
    {
        var employeeId = await NewEmployeeAsync("C6-BALANCEONLY");
        await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 10m);
        _fx.Engine.Configure(employeeId, Jun, closing: 13m);

        var response = await _fx.RecalculateMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadCorrection(response.Json);
        Assert.Empty(result.CorrectionLines);
        Assert.Equal<(decimal?, decimal?, decimal?)>((0m, 10m, 13m), (result.OpeningFlexBalanceUsed, result.ClosingFlexBalanceBefore, result.ClosingFlexBalanceAfter));
        Assert.Null(result.NextPeriodToRecalculateStart);
        AssertTriple(await _fx.RequireRecordAsync(employeeId, Jun), 0m, 13m, "LEDGER_START");
        Assert.Equal(1, await _fx.CountCorrectionEventsAsync(employeeId));
        Assert.Equal(1, await _fx.CountAuditRowsAsync(employeeId, "RetroactiveCorrectionRequested"));
        using var evt = await _fx.LatestEventAsync(employeeId, "RetroactiveCorrectionRequested");
        Assert.Equal(10m, Dec(evt.RootElement, "closingFlexBalanceBefore"));
        Assert.Equal(13m, Dec(evt.RootElement, "closingFlexBalanceAfter"));
    }

    // ═════════════════════════════════════════════════════════════════════
    // R-47 — one whole calendar month
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// R-47, /calculate-and-export (kind F, Docker). A WEEKLY APPROVED approval row exists for 1-7 June; a request for exactly
    /// that week answers 400 <c>payroll-period-not-a-month</c> and writes nothing (no record, no manifest, no rule-engine call).
    /// The weekly row IS approved, so a refusal moved after the approval lookup would find it and proceed (not 400): the 400
    /// proves the refusal precedes the approval lookup's answer. "No read at all" is verified by code reading
    /// (<c>Program.cs:364-370</c>), not by this fact.
    ///
    /// <para><b>Red conditions.</b> Without the guard the legacy weekly row matches the approval lookup and the week is
    /// exported: the status is 200, and June's record would hold a week's closing balance for the next month to chain from.</para>
    /// </summary>
    [Fact]
    public async Task CalculateAndExport_PartialPeriod_Returns400PeriodNotAMonth()
    {
        var employeeId = await NewEmployeeAsync("R47-EXPORT");
        await _fx.SeedApprovalAsync(employeeId, Jun, new DateOnly(2026, 6, 7), "WEEKLY");

        var response = await _fx.CalculateAndExportAsync(employeeId, Jun, new DateOnly(2026, 6, 7));

        AssertRefusal(response, HttpStatusCode.BadRequest, "payroll-period-not-a-month", employeeId);
        Assert.Equal(0, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(0, await _fx.CountManifestsAsync(employeeId));
        Assert.Equal(0, _fx.Engine.CallCountFor(employeeId));

        // 5a c2: a partial period for an employee with NO approval row at all. A refusal moved to just after the approval
        // read would answer 403 here; the 400 proves it precedes the approval lookup's answer. (The approved-week case above
        // pins that the guard exists at all.)
        var noApproval = await NewEmployeeAsync("R47-EXPORT-NOAPPROVAL");
        var absent = await _fx.CalculateAndExportAsync(noApproval, Jun, new DateOnly(2026, 6, 7));
        AssertRefusal(absent, HttpStatusCode.BadRequest, "payroll-period-not-a-month", noApproval);
        Assert.Equal(0, await _fx.CountRecordsAsync(noApproval));
        Assert.Equal(0, _fx.Engine.CallCountFor(noApproval));
    }

    /// <summary>
    /// R-47, /recalculate (kind F, Docker). After a whole-month export (June, closing 10), a correction request for 1-15 June
    /// answers 400 <c>payroll-period-not-a-month</c> with the lines, the triple, the events and the marker unchanged. The
    /// request carries a token that is ALREADY DELIVERED (pre-marked), so a refusal moved after the idempotency lookup would
    /// answer 200 "Correction already processed": the 400 proves the refusal precedes the idempotency lookup's answer. The
    /// marker stays exactly the one pre-mark. "No read at all" is verified by code reading (<c>Program.cs:613-619</c>).
    ///
    /// <para><b>Red conditions.</b> Without the guard the partial period replaces the month's baseline lines and stores a
    /// part-month closing: the status is 200 (or the record changes), so the status and the unchanged-record assertions fail.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_PartialPeriod_Returns400PeriodNotAMonth()
    {
        var employeeId = await NewEmployeeAsync("R47-RECALC");
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedWeekdayHoursAsync(employeeId, Jun, new DateOnly(2026, 6, 30));
        _fx.Engine.Configure(employeeId, Jun, closing: 10m);
        AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, Jun));
        var before = await _fx.RequireRecordAsync(employeeId, Jun);
        var callsBefore = _fx.Engine.CallCountFor(employeeId);
        var eventsBefore = await _fx.CountExportEventsAsync(employeeId);
        var token = Guid.NewGuid();
        await _fx.PreMarkTokenDeliveredAsync(token);

        var response = await _fx.RecalculateAsync(employeeId, Jun, new DateOnly(2026, 6, 15), token);

        AssertRefusal(response, HttpStatusCode.BadRequest, "payroll-period-not-a-month", employeeId);
        Assert.Equal(before, await _fx.RequireRecordAsync(employeeId, Jun));
        Assert.Equal(eventsBefore, await _fx.CountExportEventsAsync(employeeId));
        Assert.Equal(0, await _fx.CountCorrectionEventsAsync(employeeId));
        Assert.Equal(1, await _fx.CountAsync(
            "SELECT COUNT(*) FROM outbox_messages WHERE idempotency_token = @t", ("t", token))); // the pre-mark only
        Assert.Equal(callsBefore, _fx.Engine.CallCountFor(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // R-48 — a month that lost every employed day
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// R-48, M holds a triple (kind F, Docker). May (ends on a Sunday) holds (0, 10, LEDGER_START) and June is exported
    /// (CHAINED from 10). The employee's employment is then edited to start on 10 June, so May has NO employed day. A correction
    /// of May succeeds, keeps May's triple exactly as it was, says "flex not settled", and NAMES June (June may have chained from
    /// that closing and would now derive REHIRE 0). Its event carries no opening used and no provenance, and equal closings
    /// before and after (10 and 10).
    ///
    /// <para><b>Red conditions.</b> Without R-48's widening the hint rests on the Sunday test and the closing change, neither of
    /// which holds (May ends on a Sunday; the closings are equal): <c>nextPeriodToRecalculateStart</c> is null, so the first
    /// assertion fails. A correction that overwrote May's triple with nulls fails the unchanged-triple assertion.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_MonthLostEveryEmployedDay_KeepsTripleAndNamesNextMonth()
    {
        var employeeId = await NewEmployeeAsync("R48-TRIPLE");
        await _fx.SeedApprovedMonthAsync(employeeId, Apr);
        await _fx.SeedApprovedMonthAsync(employeeId, May);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        _fx.Engine.Configure(employeeId, May, closing: 10m);
        _fx.Engine.Configure(employeeId, Jun, closing: 20m);
        AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, May));
        AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, Jun));
        await _fx.SetEmploymentWindowAsync(employeeId, new DateOnly(2026, 6, 10), null);

        var response = await _fx.RecalculateMonthAsync(employeeId, May);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadCorrection(response.Json);
        Assert.Equal(Jun, result.NextPeriodToRecalculateStart);
        Assert.Equal(RetroactiveCorrectionService.NoEmployedDayFlexNote, result.FlexNote);
        Assert.Null(result.OpeningFlexBalanceUsed);
        Assert.Null(result.FlexProvenance);
        Assert.Equal<(decimal?, decimal?)>((10m, 10m), (result.ClosingFlexBalanceBefore, result.ClosingFlexBalanceAfter));
        AssertTriple(await _fx.RequireRecordAsync(employeeId, May), 0m, 10m, "LEDGER_START");
        using var evt = await _fx.LatestEventAsync(employeeId, "RetroactiveCorrectionRequested");
        Assert.False(Has(evt.RootElement, "flexProvenance"));
        Assert.False(Has(evt.RootElement, "openingFlexBalanceUsed"));
        Assert.Equal(10m, Dec(evt.RootElement, "closingFlexBalanceBefore"));
        Assert.Equal(10m, Dec(evt.RootElement, "closingFlexBalanceAfter"));
        Assert.Equal("2026-06-01", Str(evt.RootElement, "nextPeriodToRecalculateStart"));
    }

    /// <summary>
    /// R-48, M is a RAW record (kind N/B with a mutation argument, not F: the pre-S146 code also names nothing here, so no
    /// assertion fails there). May holds a raw record (no triple), June is the ledger start, and May has no
    /// employed day (employment starts 10 June). A correction of May does NOT name June: June cannot have chained from a raw
    /// month (case 4b refuses that), May ends on a Sunday, and both closings are null. No flex provenance, no balance members.
    ///
    /// <para><b>Red conditions.</b> A widening that named M+1 for every month without an employed day (not only one holding a
    /// triple) names June here: <c>nextPeriodToRecalculateStart</c> is not null, tripping the assertion.</para>
    /// </summary>
    [Fact]
    public async Task Recalculate_MonthLostEveryEmployedDay_RawRecord_DoesNotNameNextMonth()
    {
        var employeeId = await NewEmployeeAsync("R48-RAW");
        await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 1m);
        await _fx.SeedRawRecordAsync(employeeId, May);
        await _fx.SetEmploymentWindowAsync(employeeId, new DateOnly(2026, 6, 10), null);

        var response = await _fx.RecalculateMonthAsync(employeeId, May);

        AssertStatus(HttpStatusCode.OK, response);
        var result = ReadCorrection(response.Json);
        Assert.Null(result.NextPeriodToRecalculateStart);
        Assert.Equal(RetroactiveCorrectionService.NoEmployedDayFlexNote, result.FlexNote);
        Assert.Null(result.ClosingFlexBalanceBefore);
        Assert.Null(result.ClosingFlexBalanceAfter);
        AssertTriple(await _fx.RequireRecordAsync(employeeId, May), null, null, null);
        using var evt = await _fx.LatestEventAsync(employeeId, "RetroactiveCorrectionRequested");
        Assert.False(Has(evt.RootElement, "flexProvenance"));
        Assert.False(Has(evt.RootElement, "closingFlexBalanceBefore"));
        Assert.False(Has(evt.RootElement, "nextPeriodToRecalculateStart"));
    }
}
