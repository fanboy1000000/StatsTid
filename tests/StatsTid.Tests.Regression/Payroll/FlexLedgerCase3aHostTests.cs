using System.Net;
using System.Text.Json;
using Npgsql;
using static StatsTid.Tests.Regression.Payroll.FlexLedgerHostFixture;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S147 / TASK-14715-T (SEC-054 step 1; owner OQ-3 remedy (i); AC-054-1 (a) and (b); rulings R-16, R-17, D1) — the HOST-level
/// facts of "case 3a": a first export of a month that comes BEFORE the employee's first recorded flex month (the ledger start).
///
/// <para>
/// <b>Plain language.</b> Flex is the running balance of hours above or below the norm. An employee's "ledger" starts at their
/// first paid month. Until S147 a month BEFORE that start could not be paid the normal way (the system answered "out of order");
/// the only way in was two raw payroll routes that take pay lines typed by a caller with no approved month and no second
/// person, which are being retired. The owner chose to give such a month a proper path: it is paid through the ordinary
/// approved-month route, and its flex is marked "not tracked" (no balance is invented or carried forward, and no flex payout is
/// paid). If the month has pay lines, a record is written and the month is locked (changes only by correction). If it has no pay
/// lines, nothing is written at all, so the month can still be reopened and edited. These facts drive that through HTTP against
/// a real Postgres.
/// </para>
///
/// <para>
/// <b>Harness.</b> <see cref="FlexLedgerHostFixture"/> (the real Payroll host, the configurable stub engine). Every fact seeds
/// its own employee; the ledger starts in June (a seeded ledger record), and the month under test is April, which has no record.
/// Expected values come from the refinement's T6 and AC-054-1 and from ADR-034 D3, never from reading the code.
/// </para>
///
/// <para>
/// <b>Status.</b> Docker-gated: <b>CI-pending</b> (CI-W4); never locally green. Kinds: F fails on the pre-S147 code (<c>1185624f</c>,
/// where a case-3a month answers 409 <c>payroll-export-out-of-order</c>); R regression pin (an existing behaviour kept). The RED of a
/// Docker fact is argued here, not run.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FlexLedgerCase3aHostTests : IClassFixture<FlexLedgerHostFixture>
{
    private static readonly DateOnly Mar = new(2026, 3, 1);
    private static readonly DateOnly Apr = new(2026, 4, 1);
    private static readonly DateOnly Jun = new(2026, 6, 1);

    private const string UntrackedNoteText = "flex not tracked: the month precedes the ledger start 2026-06";

    private readonly FlexLedgerHostFixture _fx;

    public FlexLedgerCase3aHostTests(FlexLedgerHostFixture fixture) => _fx = fixture;

    // ═════════════════════════════════════════════════════════════════════
    // helpers
    // ═════════════════════════════════════════════════════════════════════

    private static void AssertStatus(HttpStatusCode expected, (HttpStatusCode Status, string Json) response) =>
        Assert.True(response.Status == expected, $"expected {(int)expected}, got {(int)response.Status}: {response.Json}");

    /// <summary>The ledger starts in June (a seeded ledger record) and April has an approved month, with March approved too
    /// (April opens in a week that holds 30-31 March, so March must be APPROVED for April to be calculable).</summary>
    private async Task<string> SeedCase3aEmployeeAsync(string tag)
    {
        var employeeId = $"EMP-S147-C3A-{tag}";
        await _fx.SeedEmployeeAsync(employeeId);
        await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 1m);
        await _fx.SeedApprovedMonthAsync(employeeId, Mar);
        await _fx.SeedApprovedMonthAsync(employeeId, Apr);
        return employeeId;
    }

    private static void AssertUntrackedBody(string json, bool expectLines)
    {
        var result = ReadResult(json);
        Assert.Equal("UNTRACKED", result.FlexProvenance);
        Assert.Null(result.OpeningFlexBalance);
        Assert.Null(result.ClosingFlexBalance);
        if (expectLines)
            Assert.NotEmpty(result.ExportLines);
        else
            Assert.Empty(result.ExportLines);
        Assert.DoesNotContain(result.ExportLines, l => l.SourceTimeType == "FLEX_PAYOUT");
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(UntrackedNoteText, Str(doc.RootElement, "flexNote"));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-054-1 (a) — a case-3a month WITH lines
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-054-1 (a), the export (kind F, Docker; R-16). The ledger starts in June; April is approved, has no record and holds
    /// recorded weekday hours, and the stub engine would pay 2 h of flex out and close the month at 5. The export answers 200
    /// with the "flex not tracked" note naming 2026-06 and provenance UNTRACKED with null opening and closing; April's record
    /// holds the month's pay lines, has NULL balances and no provenance, source CALCULATE_AND_EXPORT and the approval period
    /// attached, and carries NO <c>FLEX_PAYOUT</c> line (the stub's payout is dropped under UNTRACKED). Exactly one
    /// <c>PayrollExportGenerated</c> event and audit row were written; June's record is untouched.
    ///
    /// <para><b>Red conditions.</b> On <c>1185624f</c> case 3a refuses: the status assertion fails (409
    /// <c>payroll-export-out-of-order</c>, nothing written). A fix that opened the month at 0 as LEDGER_START or stored the
    /// stub's closing 5 trips the null-balance assertions; one that kept the FLEX_PAYOUT line trips the line assertion; one
    /// that skipped the record trips the single-record read.</para>
    /// </summary>
    [Fact]
    public async Task Case3a_MonthWithLines_Returns200Untracked_WritesNullBalanceRecord_NoFlexPayout()
    {
        var employeeId = await SeedCase3aEmployeeAsync("LINES");
        await _fx.SeedWeekdayHoursAsync(employeeId, Apr, new DateOnly(2026, 4, 30));
        _fx.Engine.Configure(employeeId, Apr, closing: 5m, payoutHours: 2m);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Apr);

        AssertStatus(HttpStatusCode.OK, response);
        AssertUntrackedBody(response.Json, expectLines: true);
        Assert.Equal(2, await _fx.CountRecordsAsync(employeeId));
        var record = await _fx.RequireRecordAsync(employeeId, Apr);
        Assert.Null(record.Opening);
        Assert.Null(record.Closing);
        Assert.Null(record.Provenance);
        Assert.Equal("CALCULATE_AND_EXPORT", record.Source);
        Assert.NotEmpty(record.CurrentLines);
        Assert.DoesNotContain(record.CurrentLines, l => l.SourceTimeType == "FLEX_PAYOUT");
        Assert.Equal(1, await _fx.CountAsync(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e AND year = 2026 AND month = 4 AND period_id IS NOT NULL",
            ("e", employeeId)));
        Assert.Equal(1, await _fx.CountExportEventsAsync(employeeId));
        Assert.Equal(1, await _fx.CountAuditRowsAsync(employeeId, "PayrollExportGenerated"));
        var june = await _fx.RequireRecordAsync(employeeId, Jun);
        Assert.Equal<(decimal?, decimal?, string?)>((0m, 1m, "LEDGER_START"), (june.Opening, june.Closing, june.Provenance));
    }

    /// <summary>
    /// AC-054-1 (a), the re-run (kind F, Docker). After the export above, the same request again is a case-3b re-run of a
    /// recorded month: it is NOT a silent 200 and NOT a second record. Its calculation carries a new manifest id, so the lines
    /// differ from the recorded ones and the answer is 409 <c>payroll-export-conflict</c> with a sentence telling the operator
    /// to use a correction (ADR-034 D3: same key and different content means "use a correction"). The record is byte-for-byte
    /// unchanged and no second event was written.
    ///
    /// <para><b>Red conditions.</b> On <c>1185624f</c> the first export already 409s out-of-order, so the setup assertion
    /// (200) fails first. After the fix, a re-run that overwrote the record, or answered 200, trips the 409 and equality
    /// assertions.</para>
    /// </summary>
    [Fact]
    public async Task Case3a_MonthWithLines_ReRun_Returns409UseACorrection_RecordUnchanged()
    {
        var employeeId = await SeedCase3aEmployeeAsync("RERUN");
        await _fx.SeedWeekdayHoursAsync(employeeId, Apr, new DateOnly(2026, 4, 30));
        AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, Apr));
        var before = await _fx.RequireRecordAsync(employeeId, Apr);

        var rerun = await _fx.CalculateAndExportMonthAsync(employeeId, Apr);

        AssertStatus(HttpStatusCode.Conflict, rerun);
        using (var doc = JsonDocument.Parse(rerun.Json))
        {
            Assert.Equal("payroll-export-conflict", Str(doc.RootElement, "kind"));
            Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains("correction", Str(doc.RootElement, "error")!, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(before, await _fx.RequireRecordAsync(employeeId, Apr));
        Assert.Equal(2, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(1, await _fx.CountExportEventsAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-054-1 (b) — a case-3a month with ZERO lines writes nothing
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-054-1 (b), the export (kind F, Docker; R-17; ADR-034 D3 "nothing handed off, no record"). April is approved, has no
    /// record and no recorded hours, so the calculation has no lines (the stub is told to close it at 5, which must not be
    /// stored). The export answers 200 with the note naming 2026-06, UNTRACKED, null balances and EMPTY lines, and writes
    /// NOTHING: no April record (June's remains the only one), no <c>PayrollExportGenerated</c> event, no audit row.
    ///
    /// <para>R pin (fix-up 1, R-F2; "holds today"): the calculation's proof is still stored. Exactly one
    /// <c>segment_manifests</c> row exists and the engine saw exactly one timeline call, because the calculation's manifest is
    /// persisted BEFORE the export is attempted (<c>Program.cs</c>, ADR-016 D10's QUAL-205 footnote), whatever the export then
    /// writes. A change that skipped the calculation, or lost its manifest, trips these.</para>
    ///
    /// <para><b>Red conditions.</b> On <c>1185624f</c> the month answers 409 out-of-order: the status assertion fails. A fix
    /// that still adds the empty group (writing an empty April record with null balances) trips the "no record" and the record
    /// count assertions.</para>
    /// </summary>
    [Fact]
    public async Task Case3a_ZeroLines_Returns200Untracked_WritesNoRecordEventOrAudit()
    {
        var employeeId = await SeedCase3aEmployeeAsync("ZERO");
        _fx.Engine.Configure(employeeId, Apr, closing: 5m);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Apr);

        AssertStatus(HttpStatusCode.OK, response);
        AssertUntrackedBody(response.Json, expectLines: false);
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Apr));
        Assert.Equal(1, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));
        Assert.Equal(0, await _fx.CountAuditRowsAsync(employeeId, "PayrollExportGenerated"));
        Assert.Equal(1, await _fx.CountManifestsAsync(employeeId));
        Assert.Single(_fx.Engine.TimelineCalls, c => c.EmployeeId == employeeId);
    }

    /// <summary>
    /// AC-054-1 (b), "stays reopenable; a re-export writes nothing; /recalculate refuses it" (kind F, Docker). After the zero-line
    /// export: (1) a second export is again 200 with the same note and writes nothing; (2) the month has no
    /// <c>payroll_export_records</c> row, which is exactly what the Backend's reopen guard and its "payrollExported" flag probe
    /// (<c>ApprovalEndpoints.cs</c>, "a payroll_export_records row exists for the period's month"), so the month is reopenable
    /// (the Backend host is not in this fixture, so the reopen route itself is not driven); (3) <c>/recalculate</c> refuses the
    /// never-exported month with 422 <c>payroll-not-exported</c> (the route's own kind, <c>Payroll/Program.cs</c>), nothing
    /// written.
    ///
    /// <para>R pin (fix-up 1, R-F2): each export request is its own calculation with its own manifest id, so a re-export adds
    /// a SECOND <c>segment_manifests</c> row (the count is the number of calculations, not of records) while the record count
    /// stays 0.</para>
    ///
    /// <para><b>Red conditions.</b> On <c>1185624f</c> the first export 409s (setup assertion). After the fix, a path that wrote
    /// an empty record on the second call would trip the "no record" assertion and lock the month; a /recalculate that treated
    /// the month as exported would answer 200 and trip the 422 assertion.</para>
    /// </summary>
    [Fact]
    public async Task Case3a_ZeroLines_ReExportWritesNothing_MonthStaysReopenable_RecalculateRefuses()
    {
        var employeeId = await SeedCase3aEmployeeAsync("ZERO-AFTER");
        AssertStatus(HttpStatusCode.OK, await _fx.CalculateAndExportMonthAsync(employeeId, Apr));

        var again = await _fx.CalculateAndExportMonthAsync(employeeId, Apr);

        AssertStatus(HttpStatusCode.OK, again);
        AssertUntrackedBody(again.Json, expectLines: false);
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Apr));
        Assert.Equal(0, await _fx.CountAsync(
            "SELECT COUNT(*) FROM payroll_export_records WHERE employee_id = @e AND year = 2026 AND month = 4",
            ("e", employeeId)));
        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));
        Assert.Equal(2, await _fx.CountManifestsAsync(employeeId));

        var recalc = await _fx.RecalculateMonthAsync(employeeId, Apr);

        AssertStatus(HttpStatusCode.UnprocessableEntity, recalc);
        using (var doc = JsonDocument.Parse(recalc.Json))
            Assert.Equal("payroll-not-exported", Str(doc.RootElement, "kind"));
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Apr));
        Assert.Equal(0, await _fx.CountCorrectionEventsAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // Four-eyes — the record-less zero-line month runs the same in-transaction checks (fix-up 1, R-F1)
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Seeds an APPROVED month whose approver is the same person who sent it (TASK-14704's seeding, as in
    /// <c>PayrollSenderSeparationHostTests</c>: submitted_by, employee_approved_by and approved_by all the same actor).</summary>
    private async Task SeedSelfApprovedMonthAsync(string employeeId, DateOnly month)
    {
        await using var conn = _fx.Db.Create();
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO approval_periods
                (period_id, employee_id, org_id, period_start, period_end, period_type, status, agreement_code, ok_version,
                 submitted_by, employee_approved_by, approved_by, approved_at)
            VALUES
                (gen_random_uuid(), @emp, @org, @start, @end, 'MONTHLY', 'APPROVED', @agreement, @ok,
                 'hr-self', 'hr-self', 'hr-self', NOW())
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", FlexLedgerHostFixture.OrgId);
        cmd.Parameters.AddWithValue("start", FlexLedgerHostFixture.MonthStart(month));
        cmd.Parameters.AddWithValue("end", FlexLedgerHostFixture.MonthEnd(month));
        cmd.Parameters.AddWithValue("agreement", FlexLedgerHostFixture.Agreement);
        cmd.Parameters.AddWithValue("ok", FlexLedgerHostFixture.Ok);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }

    private static string FourEyesError(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("payroll-export-conflict", Str(doc.RootElement, "kind"));
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        var error = Str(doc.RootElement, "error")!;
        Assert.Contains("approved by the person who sent it (four-eyes)", error);
        return error;
    }

    /// <summary>
    /// Four-eyes on a record-less pre-ledger zero-line month (kind F, Docker; TASK-14715 fix-up 1, R-F1, Fable 5a WARNING).
    /// April has no record, no hours (zero lines) and comes before the June ledger start, but its approval was stamped by the
    /// same person who sent it. It must meet the same refusal every other month meets: 409 <c>payroll-export-conflict</c>
    /// whose error says the month "was approved by the person who sent it (four-eyes)", and nothing is written (no record,
    /// no event). A tracked month in the same situation (June with no ledger yet) answers the same status, kind and
    /// four-eyes sentence, shown by the mirror fact below; only the period id inside the sentence differs.
    ///
    /// <para><b>Red conditions.</b> At <c>2ffbd482</c> the record-less zero-line month returned before the transaction, so it
    /// skipped the four-eyes checkpoint and answered 200: the 409 status assertion fails (<c>expected 409, got 200</c>). A
    /// fix that ran the checkpoint but still wrote an empty record would trip the no-record assertion.</para>
    /// </summary>
    [Fact]
    public async Task Case3a_ZeroLines_SelfApprovedMonth_Returns409FourEyes_NothingWritten()
    {
        var employeeId = "EMP-S147-C3A-SELF";
        await _fx.SeedEmployeeAsync(employeeId);
        await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 1m);
        await _fx.SeedApprovedMonthAsync(employeeId, Mar);
        await SeedSelfApprovedMonthAsync(employeeId, Apr);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Apr);

        AssertStatus(HttpStatusCode.Conflict, response);
        FourEyesError(response.Json);
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Apr));
        Assert.Equal(1, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));
        Assert.Equal(0, await _fx.CountAuditRowsAsync(employeeId, "PayrollExportGenerated"));
    }

    /// <summary>
    /// Four-eyes mirror on a normal tracked month (kind R, "regression pin — holds today"). June with NO ledger yet (so it is
    /// the ledger start, tracked), self-approved: the same 409, kind and four-eyes sentence as the pre-ledger zero-line
    /// month, and nothing written. It shows that the record-less zero-line month is refused exactly as a normal month is.
    ///
    /// <para><b>Red conditions.</b> Holds today and must keep holding: a calculating export that skipped the four-eyes
    /// check would answer 200 and fail the status assertion.</para>
    /// </summary>
    [Fact]
    public async Task TrackedMonth_SelfApproved_Returns409FourEyes_NothingWritten()
    {
        var employeeId = "EMP-S147-C3A-SELF-TRACKED";
        await _fx.SeedEmployeeAsync(employeeId);
        await SeedSelfApprovedMonthAsync(employeeId, Jun);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertStatus(HttpStatusCode.Conflict, response);
        FourEyesError(response.Json);
        Assert.Equal(0, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));
    }

    // ═════════════════════════════════════════════════════════════════════
    // D1 — the zero-line skip is guarded by "no record"
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Case 3b, a zero-line re-run of a month that already HAS a record with different lines (kind R, "regression pin — holds
    /// today"; Orchestrator ruling D1 on TASK-14715). April holds a raw record with one line; the ledger starts in June; the
    /// month's calculation now has no lines. The zero-line skip applies only to a month with NO record, so this month still
    /// meets the content compare: 409 <c>payroll-export-conflict</c> telling the operator to use a correction, and the record is
    /// unchanged. (The same-content no-op is already pinned by <c>FlexLedgerHostTests.ReRun_UntrackedRawMonth_IsNoOp</c>; this
    /// is the DIFFERENT-content case that one cannot see.)
    ///
    /// <para><b>Red conditions.</b> It holds today and must keep holding. A skip guarded by UNTRACKED alone would return a
    /// silent 200 here (the recorded line never compared): the 409 assertion fails, and the record-unchanged assertion guards an
    /// implementation that overwrote it.</para>
    /// </summary>
    [Fact]
    public async Task Case3b_ZeroLineReRun_OfARecordedMonthWithDifferentLines_StillReturns409()
    {
        var employeeId = await SeedCase3aEmployeeAsync("3B-ZERO");
        await _fx.SeedRawRecordAsync(employeeId, Apr, new[] { PayoutLine(employeeId, new DateOnly(2026, 4, 30), 4m) });
        var before = await _fx.RequireRecordAsync(employeeId, Apr);
        Assert.NotEmpty(before.CurrentLines);

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Apr);

        AssertStatus(HttpStatusCode.Conflict, response);
        using (var doc = JsonDocument.Parse(response.Json))
        {
            Assert.Equal("payroll-export-conflict", Str(doc.RootElement, "kind"));
            Assert.Contains("correction", Str(doc.RootElement, "error")!, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(before, await _fx.RequireRecordAsync(employeeId, Apr));
        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));
    }
}
