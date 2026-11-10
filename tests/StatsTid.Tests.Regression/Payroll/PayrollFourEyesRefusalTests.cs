using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using static StatsTid.Tests.Regression.Payroll.FlexLedgerHostFixture;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S148 / TASK-14801-T (QUAL-246, tidy 12b, P-5 option (a); AC-12b-a, AC-12b-b) — payroll's own four-eyes refusal, over HTTP
/// against a real Postgres.
///
/// <para>
/// <b>Plain language.</b> Payroll will not pay a month that one person signed twice: the approver must be neither the person
/// who sent the month nor the employee whose month it is. Payroll now refuses at admission, its first look at the month and
/// before calculating anything, with a 409 of its own kind, <c>payroll-four-eyes</c>, so no calculation proof (manifest) is
/// left behind for a month that cannot be paid. The check inside the export transaction stays as the guard for an approval
/// row that changes after admission, and answers the same 409 and kind.
/// </para>
///
/// <para>
/// <b>Harness.</b> <see cref="FlexLedgerHostFixture"/> (the real Payroll host, the configurable stub engine). Every row uses
/// a fresh employee. Expected values come from the refinement (Tidy 12b, AC-12b-a/b) and ruling A3, never from reading the
/// code: 409; kind <c>payroll-four-eyes</c>; a body with no date (ADR-040 D7); nothing exported.
/// </para>
///
/// <para>
/// <b>Status.</b> Docker-gated: <b>CI-pending</b> (CI-W1); never locally green. Both facts are kind F: they fail on the base
/// (the parent of TASK-14801), where admission reads only the status and the export-time check answers
/// <c>payroll-export-conflict</c>. The RED of a Docker fact is argued, not run.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class PayrollFourEyesRefusalTests : IClassFixture<FlexLedgerHostFixture>
{
    private static readonly DateOnly Mar = new(2026, 3, 1);
    private static readonly DateOnly Apr = new(2026, 4, 1);
    private static readonly DateOnly Jun = new(2026, 6, 1);

    private const string Kind = "payroll-four-eyes";

    private readonly FlexLedgerHostFixture _fx;

    public PayrollFourEyesRefusalTests(FlexLedgerHostFixture fixture) => _fx = fixture;

    // ═════════════════════════════════════════════════════════════════════
    // AC-12b-a — refused at admission, before calculating
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-12b-a (kind F, Docker). June 2026 (a Monday-opening month, so no previous month must be approved) is APPROVED by
    /// SQL in a shape the endpoints refuse to produce. Row "sender": the legacy shape, <c>submitted_by</c> = X,
    /// <c>employee_approved_by</c> NULL, <c>approved_by</c> = X (X is not the employee). Row "employee": the approver is the
    /// employee, the sender someone else. Each answers 409 with kind <c>payroll-four-eyes</c> and the same fixed body: no
    /// <c>periodStart</c>/<c>periodEnd</c> member, no date in any string, no employee id. The refusal comes before the
    /// calculation: the rule engine was never called for the employee, no manifest exists, and nothing was exported (no
    /// record, no <c>PayrollExportGenerated</c> event, no audit row).
    ///
    /// <para><b>Red conditions.</b> On the base, admission reads only the status, so the month is calculated: the engine is
    /// called and a manifest is written, and the export-time check then answers 409 <c>payroll-export-conflict</c>. The kind
    /// assertion fails first; the engine-call and manifest-count assertions would fail next.</para>
    /// </summary>
    [Theory]
    [InlineData("sender")]
    [InlineData("employee")]
    public async Task SelfApprovedLegacyMonth_RefusedAtAdmission_NoManifest(string row)
    {
        var employeeId = $"EMP-S148-FE-A-{row.ToUpperInvariant()}";
        await _fx.SeedEmployeeAsync(employeeId);
        await _fx.SeedWeekdayHoursAsync(employeeId, Jun, new DateOnly(2026, 6, 30));
        _fx.Engine.Configure(employeeId, Jun, closing: 5m);
        switch (row)
        {
            case "sender":
                await SeedApprovedMonthWithStampsAsync(
                    employeeId, Jun, submittedBy: "hr-legacy-x", employeeApprovedBy: null, approvedBy: "hr-legacy-x");
                break;
            case "employee":
                await SeedApprovedMonthWithStampsAsync(
                    employeeId, Jun, submittedBy: "hr-sender-y", employeeApprovedBy: null, approvedBy: employeeId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(row), row, null);
        }

        var response = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        AssertFourEyesRefusal(response, employeeId);
        Assert.Equal(0, _fx.Engine.CallCountFor(employeeId));
        Assert.Equal(0, await _fx.CountManifestsAsync(employeeId));
        Assert.Equal(0, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));
        Assert.Equal(0, await _fx.CountAuditRowsAsync(employeeId, "PayrollExportGenerated"));
    }

    // ═════════════════════════════════════════════════════════════════════
    // AC-12b-b — the approval row changes after admission; the transaction refuses with the same kind
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC-12b-b (kind F, Docker; ruling A3). The month passes admission (<c>submitted_by</c> = the employee, <c>approved_by</c>
    /// another person), then its approval row is changed before the export commits. Connection A holds the row
    /// <c>FOR UPDATE</c>; <c>/calculate-and-export</c> starts on a task (admission's read is a plain SELECT, which the lock does
    /// not block); the test waits, on a condition and with a bound, until the export's own <c>FOR UPDATE</c> read of an
    /// approval row is blocked on that lock (never on a duration); A then sets <c>approved_by</c> to the sender (the employee
    /// here) and commits. The export reads the committed row and answers 409 <c>payroll-four-eyes</c>, the same body as
    /// admission's. Nothing is exported (no record, no event, no audit row).
    ///
    /// <para>Row "tracked-june": June 2026, an ordinary month with hours; the calculation's manifest stays (the export-time
    /// recheck runs after the calculation, so <c>CountManifestsAsync</c> = 1). Row "recordless-zero-line-april": the
    /// record-less zero-line month of <c>FlexLedgerCase3aHostTests</c> (April 2026, no hours; March approved; a June ledger
    /// record): the month TASK-14715 fix-up 1 (R-F1) showed must reach the in-transaction four-eyes check although it writes
    /// nothing. The admission refusal now answers a seeded self-approved April before the transaction, so only this race
    /// reaches that check for such a month; the row keeps R-F1's path pinned. Expected: no April record (the June seed stays
    /// the employee's only record) and no <c>PayrollExportGenerated</c>.</para>
    ///
    /// <para><b>Red conditions.</b> On the base the in-transaction refusal answers 409 <c>payroll-export-conflict</c>: the kind
    /// assertion fails in both rows. If the export never blocks (the check does not take the row lock, or the record-less
    /// month returns before it), the bounded wait fails loudly, naming the condition.</para>
    /// </summary>
    [Theory]
    [InlineData("tracked-june")]
    [InlineData("recordless-zero-line-april")]
    public async Task ApprovalFlippedAfterAdmission_InTransactionRefusal_SameKind(string row)
    {
        var tracked = row == "tracked-june";
        var month = tracked ? Jun : Apr;
        var employeeId = tracked ? "EMP-S148-FE-B-JUN" : "EMP-S148-FE-B-APR";
        await _fx.SeedEmployeeAsync(employeeId);
        if (tracked)
        {
            await _fx.SeedApprovedMonthAsync(employeeId, Jun);
            await _fx.SeedWeekdayHoursAsync(employeeId, Jun, new DateOnly(2026, 6, 30));
            _fx.Engine.Configure(employeeId, Jun, closing: 5m);
        }
        else
        {
            // As Case3a_ZeroLines_SelfApprovedMonth seeds it, except that April's approval passes admission.
            await _fx.SeedLedgerRecordAsync(employeeId, Jun, closing: 1m);
            await _fx.SeedApprovedMonthAsync(employeeId, Mar);
            await _fx.SeedApprovedMonthAsync(employeeId, Apr);
            _fx.Engine.Configure(employeeId, Apr, closing: 5m);
        }
        var start = MonthStart(month);
        var end = MonthEnd(month);

        // Connection A plays the writer that changes the approval after admission: it holds the row FOR UPDATE.
        await using var holder = new NpgsqlConnection(_fx.ConnectionString);
        await holder.OpenAsync();
        await using var holderTx = await holder.BeginTransactionAsync();
        Task<(HttpStatusCode Status, string Json)>? response = null;
        var committed = false;
        try
        {
            await using (var lockCmd = new NpgsqlCommand(
                """
                SELECT status FROM approval_periods
                WHERE employee_id = @e AND period_start = @s AND period_end = @p
                FOR UPDATE
                """, holder, holderTx))
            {
                lockCmd.Parameters.AddWithValue("e", employeeId);
                lockCmd.Parameters.AddWithValue("s", start);
                lockCmd.Parameters.AddWithValue("p", end);
                Assert.Equal("APPROVED", (string?)await lockCmd.ExecuteScalarAsync());
            }

            response = Task.Run(() => _fx.CalculateAndExportMonthAsync(employeeId, month));

            var blocked = await PriorMonthExportRaceTests.WaitUntilAsync(
                () => IsExportBlockedOnApprovalForUpdateAsync(_fx.ConnectionString),
                TimeSpan.FromSeconds(30), () => response.IsCompleted);
            var early = string.Empty;
            if (response.IsCompletedSuccessfully)
            {
                var (earlyStatus, earlyJson) = await response;
                early = $" The request already answered {(int)earlyStatus}: {earlyJson}";
            }
            Assert.True(blocked,
                $"the export never blocked on the {row} approval row's FOR UPDATE: it does not reach the in-transaction " +
                $"four-eyes re-check for this month, or the re-check takes no row lock (request task status: " +
                $"{response.Status}).{early}{PriorMonthExportRaceTests.DescribeFault(response)}");
            Assert.False(response.IsCompleted, "the request finished while the writer still held the approval row.");

            // The change: the approver becomes the sender (submitted_by = the employee), committed. Only now can the
            // export's locking read proceed, and it reads the committed row.
            await using (var flipCmd = new NpgsqlCommand(
                """
                UPDATE approval_periods SET approved_by = submitted_by
                WHERE employee_id = @e AND period_start = @s AND period_end = @p
                """, holder, holderTx))
            {
                flipCmd.Parameters.AddWithValue("e", employeeId);
                flipCmd.Parameters.AddWithValue("s", start);
                flipCmd.Parameters.AddWithValue("p", end);
                Assert.Equal(1, await flipCmd.ExecuteNonQueryAsync());
            }
            await holderTx.CommitAsync();
            committed = true;

            AssertFourEyesRefusal(await response, employeeId);
        }
        finally
        {
            if (!committed)
                await holderTx.RollbackAsync();
            if (response is not null)
            {
                try { await response; } catch { /* the assertions above report the outcome */ }
            }
        }

        Assert.Equal(0, await _fx.CountExportEventsAsync(employeeId));
        Assert.Equal(0, await _fx.CountAuditRowsAsync(employeeId, "PayrollExportGenerated"));
        Assert.Null(await _fx.ReadRecordAsync(employeeId, month));
        if (tracked)
        {
            Assert.Equal(0, await _fx.CountRecordsAsync(employeeId));
            // The calculation ran before the export, so its manifest stays, as with every export-time recheck.
            Assert.Equal(1, await _fx.CountManifestsAsync(employeeId));
        }
        else
        {
            Assert.Equal(1, await _fx.CountRecordsAsync(employeeId)); // the June ledger seed only
        }
    }

    // ═════════════════════════════════════════════════════════════════════
    // helpers
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>409, kind <c>payroll-four-eyes</c>, <c>success:false</c>, a non-empty sentence, and a body that carries no
    /// date, no <c>periodStart</c>/<c>periodEnd</c> member and not the employee's id (ADR-040 D7).</summary>
    private static void AssertFourEyesRefusal((HttpStatusCode Status, string Json) response, string employeeId)
    {
        Assert.True(response.Status == HttpStatusCode.Conflict, $"expected 409, got {(int)response.Status}: {response.Json}");
        using var doc = JsonDocument.Parse(response.Json);
        var root = doc.RootElement;
        Assert.Equal(Kind, Str(root, "kind"));
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(Str(root, "error")));
        Assert.False(root.TryGetProperty("periodStart", out _));
        Assert.False(root.TryGetProperty("periodEnd", out _));
        Assert.DoesNotMatch(new Regex(@"\d{4}-\d{2}-\d{2}"), response.Json);
        Assert.DoesNotContain(employeeId, response.Json, StringComparison.Ordinal);
    }

    /// <summary>True when some backend is waiting on a lock while running the export's <c>FOR UPDATE</c> read of an approval
    /// row (the re-assert; the previous month's read is <c>FOR SHARE</c> and admission's is a plain SELECT).</summary>
    private static async Task<bool> IsExportBlockedOnApprovalForUpdateAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_stat_activity
            WHERE datname = current_database()
              AND pid <> pg_backend_pid()
              AND wait_event_type = 'Lock'
              AND query LIKE '%approval_periods%'
              AND query LIKE '%FOR UPDATE%'
            """, conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) >= 1;
    }

    /// <summary>An APPROVED MONTHLY month with explicit stamps (the forbidden shapes the endpoints refuse to produce).</summary>
    private async Task SeedApprovedMonthWithStampsAsync(
        string employeeId, DateOnly month, string? submittedBy, string? employeeApprovedBy, string approvedBy)
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
                 @submittedBy, @employeeApprovedBy, @approvedBy, NOW())
            """, conn);
        cmd.Parameters.AddWithValue("emp", employeeId);
        cmd.Parameters.AddWithValue("org", FlexLedgerHostFixture.OrgId);
        cmd.Parameters.AddWithValue("start", MonthStart(month));
        cmd.Parameters.AddWithValue("end", MonthEnd(month));
        cmd.Parameters.AddWithValue("agreement", FlexLedgerHostFixture.Agreement);
        cmd.Parameters.AddWithValue("ok", FlexLedgerHostFixture.Ok);
        cmd.Parameters.AddWithValue("submittedBy", (object?)submittedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("employeeApprovedBy", (object?)employeeApprovedBy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("approvedBy", approvedBy);
        Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
    }
}
