using System.Net;
using System.Text.Json;
using Npgsql;
using StatsTid.Integrations.Payroll.Services;
using static StatsTid.Tests.Regression.Payroll.FlexLedgerHostFixture;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S146 / TASK-14606 (QUAL-188; AC-C4 (i)-(iv); rulings R-14, R-15) — the flex ledger's RACES, pinned deterministically
/// against a real Postgres, each with its ordering FORCED.
///
/// <para>
/// <b>Plain language.</b> Every writer of an employee's export records (the three export routes and the correction) takes
/// one per-employee lock as the FIRST statement of its transaction, so two writers for the same employee are processed
/// one after the other. A month's opening balance is calculated BEFORE that lock, from what is recorded at that moment, so
/// by the time a request gets the lock the ledger may have moved. Under the lock the same derivation runs again; if the
/// answer changed the request is refused 409 <c>payroll-ledger-changed</c> ("retry"), and its retry sees the new ledger.
/// These facts hold the lock on a separate connection, start the requests, wait until each one is OBSERVED waiting on it,
/// and only then release, so the order in which they get the lock is decided by the test, never by timing.
/// </para>
///
/// <para>
/// <b>How the order is forced (no sleep decides an outcome).</b> Connection A runs
/// <c>pg_advisory_xact_lock(hashtext('payroll-ledger-' || E))</c> and keeps its transaction open. The first request is
/// started; the test waits on the CONDITION "a backend is waiting on a lock while running the ledger-lock statement"
/// (<c>pg_stat_activity.wait_event_type = 'Lock'</c>; the bounded condition wait of
/// <c>PriorMonthExportRaceTests</c>), and only then starts the second; once both are waiting, A commits. PostgreSQL grants a
/// contended lock to its waiters in the order they queued, so the first request to queue is the first to run. That queue
/// order is the one assumption beyond the lock itself; it is the lock manager's documented behaviour, and the
/// observed-waiting condition is what makes the arrival order deterministic.
/// </para>
///
/// <para>
/// <b>Status.</b> Docker-gated: <b>CI-pending</b> (first seen green at CI-W2, R-39). Each docstring argues its RED, because
/// the mutation proof for a Docker fact is argued, not run locally.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FlexLedgerRaceTests : IClassFixture<FlexLedgerHostFixture>
{
    private static readonly DateOnly Jun = new(2026, 6, 1);
    private static readonly DateOnly Jul = new(2026, 7, 1);

    private readonly FlexLedgerHostFixture _fx;

    public FlexLedgerRaceTests(FlexLedgerHostFixture fixture) => _fx = fixture;

    // ─── the forced-ordering machinery ───────────────────────────────────

    /// <summary>The held lock: a connection and its open transaction, which owns the employee's ledger key.</summary>
    private sealed class HeldLock : IAsyncDisposable
    {
        private readonly NpgsqlConnection _conn;
        private readonly NpgsqlTransaction _tx;
        private bool _released;

        private HeldLock(NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            _conn = conn;
            _tx = tx;
        }

        public static async Task<HeldLock> AcquireAsync(string connectionString, string employeeId)
        {
            var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync();
            var tx = await conn.BeginTransactionAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(hashtext('payroll-ledger-' || @e))", conn, tx);
            cmd.Parameters.AddWithValue("e", employeeId);
            await cmd.ExecuteNonQueryAsync();
            return new HeldLock(conn, tx);
        }

        /// <summary>Commits the holding transaction: the lock is released and the waiters proceed in queue order.</summary>
        public async Task ReleaseAsync()
        {
            if (_released)
                return;
            _released = true;
            await _tx.CommitAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (!_released)
            {
                _released = true;
                await _tx.RollbackAsync(); // never leave the lock held (a failed assertion must not hang teardown)
            }
            await _tx.DisposeAsync();
            await _conn.DisposeAsync();
        }
    }

    /// <summary>True when at least <paramref name="count"/> backends are waiting on a lock while running the ledger-lock
    /// statement (the first statement of every writer of an employee's export records).</summary>
    private async Task<bool> LedgerWaitersAtLeastAsync(int count)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM pg_stat_activity
            WHERE datname = current_database()
              AND pid <> pg_backend_pid()
              AND wait_event_type = 'Lock'
              AND query LIKE '%pg_advisory_xact_lock%'
              AND query LIKE '%payroll-ledger-%'
            """, conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) >= count;
    }

    /// <summary>Waits (bounded; fails loudly) until the request has been observed waiting on the ledger key.</summary>
    private async Task AssertWaitingAsync(int waiters, Task<(HttpStatusCode Status, string Json)> request, string who)
    {
        var observed = await PriorMonthExportRaceTests.WaitUntilAsync(
            () => LedgerWaitersAtLeastAsync(waiters), TimeSpan.FromSeconds(30), () => request.IsCompleted);
        var early = request.IsCompletedSuccessfully
            ? $" The request already answered {(int)request.Result.Status}: {request.Result.Json}"
            : string.Empty;
        Assert.True(observed,
            $"{who} was never observed waiting on the ledger lock (waiters expected: {waiters}; request status: " +
            $"{request.Status}). The ledger lock is not taken by this writer, or the request " +
            $"ended before it.{early}{PriorMonthExportRaceTests.DescribeFault(request)}");
        Assert.False(request.IsCompleted, $"{who} finished while the ledger lock was still held.");
    }

    /// <summary>Starts <paramref name="first"/>, waits until it is waiting, starts <paramref name="second"/>, waits until
    /// both are waiting, then releases the lock and returns both responses (first, second).</summary>
    private async Task<((HttpStatusCode Status, string Json) First, (HttpStatusCode Status, string Json) Second)> RaceAsync(
        string employeeId,
        Func<Task<(HttpStatusCode, string)>> first, string firstName,
        Func<Task<(HttpStatusCode, string)>> second, string secondName)
    {
        await using var held = await HeldLock.AcquireAsync(_fx.ConnectionString, employeeId);
        Task<(HttpStatusCode Status, string Json)>? firstTask = null;
        Task<(HttpStatusCode Status, string Json)>? secondTask = null;
        try
        {
            firstTask = Task.Run(async () => await first());
            await AssertWaitingAsync(1, firstTask, firstName);
            secondTask = Task.Run(async () => await second());
            await AssertWaitingAsync(2, secondTask, secondName);

            await held.ReleaseAsync();
            return (await firstTask, await secondTask);
        }
        finally
        {
            await held.ReleaseAsync(); // idempotent: a failed assertion above must not strand the lock
            foreach (var task in new[] { firstTask, secondTask })
            {
                if (task is null)
                    continue;
                try { await task; } catch { /* the assertions above report the outcome */ }
            }
        }
    }

    // ─── (i) a correction of M against an export of M+1 ──────────────────

    private async Task<string> SeedJunExportedAsync(string tag)
    {
        var employeeId = $"EMP-S146-RACE-{tag}";
        await _fx.SeedEmployeeAsync(employeeId);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedApprovedMonthAsync(employeeId, Jul);
        _fx.Engine.Configure(employeeId, Jun, closing: 10m);
        var first = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);
        Assert.True(first.Status == HttpStatusCode.OK, $"seed export of June failed: {first.Json}");
        // The correction will move June's closing to 15; July closes at 20.
        _fx.Engine.Configure(employeeId, Jun, closing: 15m);
        _fx.Engine.Configure(employeeId, Jul, closing: 20m);
        return employeeId;
    }

    /// <summary>
    /// AC-C4 (i), the export SECOND (kind F, Docker; R-14, R-15). June is exported (closing 10). A correction of June (which
    /// moves its closing to 15) and an export of July both calculate while the ledger lock is held, the CORRECTION queued first.
    /// July's admission already derived <c>CHAINED 10</c> from the old closing; once the correction has committed, July's
    /// under-lock recheck derives <c>CHAINED 15</c>, a different opening: July answers 409
    /// <c>payroll-ledger-changed</c> (naming <c>2026-07</c>) and writes nothing. The correction commits (June now 15; July
    /// was not yet exported, so no next month is named), and July's RETRY chains from 15.
    ///
    /// <para><b>Red conditions (argued).</b> Pre-S146 there is no ledger lock and no recheck: July exports on the stale 10 (200),
    /// so the 409 assertion fails. Mutation "the export skips the under-lock recheck" gives the same 200; mutation "the
    /// correction takes no ledger lock" trips <c>AssertWaitingAsync</c> (the correction is never observed waiting).</para>
    /// </summary>
    [Fact]
    public async Task CorrectionOfM_VsExportOfNextMonth_ExportSecond_LedgerChanged_RetryChains()
    {
        var employeeId = await SeedJunExportedAsync("I-A");

        var (correction, export) = await RaceAsync(
            employeeId,
            () => _fx.RecalculateMonthAsync(employeeId, Jun), "the correction of June",
            () => _fx.CalculateAndExportMonthAsync(employeeId, Jul), "the export of July");

        Assert.True(correction.Status == HttpStatusCode.OK, $"correction: {correction.Json}");
        var corrected = ReadCorrection(correction.Json);
        Assert.Equal<(decimal?, decimal?)>((10m, 15m), (corrected.ClosingFlexBalanceBefore, corrected.ClosingFlexBalanceAfter));
        Assert.Null(corrected.NextPeriodToRecalculateStart); // July was not exported when the correction committed
        Assert.True(export.Status == HttpStatusCode.Conflict, $"export of July: {export.Status} {export.Json}");
        using (var doc = JsonDocument.Parse(export.Json))
        {
            Assert.Equal("payroll-ledger-changed", Str(doc.RootElement, "kind"));
            Assert.Equal("2026-07", Str(doc.RootElement, "month"));
        }
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Jul));
        var june = await _fx.RequireRecordAsync(employeeId, Jun);
        Assert.Equal<(decimal?, decimal?, string?)>((0m, 15m, "LEDGER_START"), (june.Opening, june.Closing, june.Provenance));

        var retry = await _fx.CalculateAndExportMonthAsync(employeeId, Jul);

        Assert.True(retry.Status == HttpStatusCode.OK, $"retry: {retry.Json}");
        var july = await _fx.RequireRecordAsync(employeeId, Jul);
        Assert.Equal<(decimal?, decimal?, string?)>((15m, 20m, "CHAINED"), (july.Opening, july.Closing, july.Provenance));
    }

    /// <summary>
    /// AC-C4 (i), the export FIRST (kind F, Docker). The same two requests, but the EXPORT of July is queued first: it wins the
    /// lock and records July (<c>CHAINED 10 -> 20</c>); the correction then runs, finds July exported and, because June's closing
    /// moved 10 -> 15, NAMES July as the month to recalculate; June's record holds 15 and July's is untouched (ADR-013: nothing cascades).
    ///
    /// <para><b>Kinds and Red conditions (argued; 5a fix-up).</b> The NAMING of July is kind R, not F: 30 June 2026 is a Tuesday,
    /// so the pre-S146 F1 (Sunday) trigger already names July whenever July is exported; AC-C5 pins R-10 (closing changed, Sunday
    /// end) without a race. The F parts are the lock ORDERING and the two triples: with no ledger lock the two writers would not
    /// queue (the queue-wait assertion trips), and pre-S146 neither record has a triple (42703).</para>
    /// </summary>
    [Fact]
    public async Task CorrectionOfM_VsExportOfNextMonth_ExportFirst_CorrectionNamesNextMonth()
    {
        var employeeId = await SeedJunExportedAsync("I-B");

        var (export, correction) = await RaceAsync(
            employeeId,
            () => _fx.CalculateAndExportMonthAsync(employeeId, Jul), "the export of July",
            () => _fx.RecalculateMonthAsync(employeeId, Jun), "the correction of June");

        Assert.True(export.Status == HttpStatusCode.OK, $"export of July: {export.Json}");
        Assert.True(correction.Status == HttpStatusCode.OK, $"correction: {correction.Json}");
        var corrected = ReadCorrection(correction.Json);
        Assert.Equal(Jul, corrected.NextPeriodToRecalculateStart);
        Assert.Equal(new DateOnly(2026, 7, 31), corrected.NextPeriodToRecalculateEnd);
        var june = await _fx.RequireRecordAsync(employeeId, Jun);
        Assert.Equal<(decimal?, decimal?, string?)>((0m, 15m, "LEDGER_START"), (june.Opening, june.Closing, june.Provenance));
        var july = await _fx.RequireRecordAsync(employeeId, Jul);
        Assert.Equal<(decimal?, decimal?, string?)>((10m, 20m, "CHAINED"), (july.Opening, july.Closing, july.Provenance));
    }

    // ─── (ii) two first exports of different months ──────────────────────

    /// <summary>
    /// AC-C4 (ii), the EARLIER month first (kind F, Docker). Neither June nor July is recorded; both requests calculate on
    /// <c>LEDGER_START 0</c> while the lock is held; June is queued first. June wins and starts the ledger with closing 0
    /// (0 -> 0). July's under-lock recheck now derives <c>CHAINED 0</c>: the balance is the SAME 0 as the <c>LEDGER_START 0</c>
    /// it ran on, so the pair differs ONLY by provenance. July is 409 <c>payroll-ledger-changed</c> (<c>2026-07</c>) and writes
    /// nothing; its RETRY records <c>(0, 20, CHAINED)</c>.
    ///
    /// <para><b>Red conditions (argued; 5a fix-up).</b> A recheck that compared the balance alone sees 0 == 0, lets July through
    /// and records it as a second <c>LEDGER_START</c>: the 409 assertion fails. That is exactly the case R-14's provenance
    /// compare exists for (the unit fact <c>SamePairAs_SameBalanceDifferentProvenance_IsFalse</c> pins the predicate). Pre-S146
    /// (no lock, no recheck) both commit, so the 409 also fails.</para>
    /// </summary>
    [Fact]
    public async Task TwoFirstExports_EarlierMonthFirst_LaterGetsLedgerChanged_RetryChains()
    {
        const string employeeId = "EMP-S146-RACE-II-A";
        await _fx.SeedEmployeeAsync(employeeId);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedApprovedMonthAsync(employeeId, Jul);
        _fx.Engine.Configure(employeeId, Jun, closing: 0m); // June closes at 0: July differs ONLY by provenance
        _fx.Engine.Configure(employeeId, Jul, closing: 20m);

        var (june, july) = await RaceAsync(
            employeeId,
            () => _fx.CalculateAndExportMonthAsync(employeeId, Jun), "the export of June",
            () => _fx.CalculateAndExportMonthAsync(employeeId, Jul), "the export of July");

        Assert.True(june.Status == HttpStatusCode.OK, $"export of June: {june.Json}");
        Assert.True(july.Status == HttpStatusCode.Conflict, $"export of July: {july.Status} {july.Json}");
        using (var doc = JsonDocument.Parse(july.Json))
        {
            Assert.Equal("payroll-ledger-changed", Str(doc.RootElement, "kind"));
            Assert.Equal("2026-07", Str(doc.RootElement, "month"));
        }
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Jul));

        var retry = await _fx.CalculateAndExportMonthAsync(employeeId, Jul);

        Assert.True(retry.Status == HttpStatusCode.OK, $"retry: {retry.Json}");
        var recordJune = await _fx.RequireRecordAsync(employeeId, Jun);
        var recordJuly = await _fx.RequireRecordAsync(employeeId, Jul);
        Assert.Equal<(decimal?, decimal?, string?)>((0m, 0m, "LEDGER_START"), (recordJune.Opening, recordJune.Closing, recordJune.Provenance));
        Assert.Equal<(decimal?, decimal?, string?)>((0m, 20m, "CHAINED"), (recordJuly.Opening, recordJuly.Closing, recordJuly.Provenance));
    }

    /// <summary>
    /// AC-C4 (ii), the LATER month first (kind F, Docker; R-14's ruled consequence, stated, not a defect). The same two requests,
    /// July queued first: July wins and becomes <c>LEDGER_START</c> (0 -> 20). June's recheck now finds an unrecorded month BEFORE
    /// the ledger start, a refusal rather than an opening: June is 409 <c>payroll-ledger-changed</c> (<c>2026-06</c>). On RETRY June
    /// is refused for good, 409 <c>payroll-export-out-of-order</c> naming the ledger start (<c>2026-07</c>): an earlier month can
    /// enter only as a raw export (and then run UNTRACKED). The ruling is that the system never re-opens the ledger backwards.
    ///
    /// <para><b>Red conditions (argued).</b> Pre-S146 June exports after July without complaint (both 200): the first 409
    /// assertion fails. A recheck that ignores refusals (compares only openings it can compute) would let June through: same.</para>
    /// </summary>
    [Fact]
    public async Task TwoFirstExports_LaterMonthFirst_EarlierGetsLedgerChanged_ThenOutOfOrderOnRetry()
    {
        const string employeeId = "EMP-S146-RACE-II-B";
        await _fx.SeedEmployeeAsync(employeeId);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        await _fx.SeedApprovedMonthAsync(employeeId, Jul);
        _fx.Engine.Configure(employeeId, Jun, closing: 10m);
        _fx.Engine.Configure(employeeId, Jul, closing: 20m);

        var (july, june) = await RaceAsync(
            employeeId,
            () => _fx.CalculateAndExportMonthAsync(employeeId, Jul), "the export of July",
            () => _fx.CalculateAndExportMonthAsync(employeeId, Jun), "the export of June");

        Assert.True(july.Status == HttpStatusCode.OK, $"export of July: {july.Json}");
        Assert.True(june.Status == HttpStatusCode.Conflict, $"export of June: {june.Status} {june.Json}");
        using (var doc = JsonDocument.Parse(june.Json))
        {
            Assert.Equal("payroll-ledger-changed", Str(doc.RootElement, "kind"));
            Assert.Equal("2026-06", Str(doc.RootElement, "month"));
        }
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Jun));
        var recordJuly = await _fx.RequireRecordAsync(employeeId, Jul);
        Assert.Equal<(decimal?, decimal?, string?)>((0m, 20m, "LEDGER_START"), (recordJuly.Opening, recordJuly.Closing, recordJuly.Provenance));

        var retry = await _fx.CalculateAndExportMonthAsync(employeeId, Jun);

        Assert.True(retry.Status == HttpStatusCode.Conflict, $"retry of June: {retry.Status} {retry.Json}");
        using (var doc = JsonDocument.Parse(retry.Json))
        {
            Assert.Equal("payroll-export-out-of-order", Str(doc.RootElement, "kind"));
            Assert.Equal("2026-07", Str(doc.RootElement, "month"));
        }
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Jun));
    }

    // ─── (iii) the same month concurrently ────────────────────────────────

    /// <summary>
    /// AC-C4 (iii), the SAME month exported twice at once (kind R, "regression pin — holds today through the approval-row lock
    /// and ON CONFLICT"; under R-16 it pins the triple). Both requests calculate <c>LEDGER_START 0 -> 7</c>; one writes June, the
    /// other finds the record, sees the same lines AND the same (opening, provenance, closing) and takes the same-hash no-op path.
    /// Both answer 200, exactly one record exists and exactly one <c>PayrollExportGenerated</c> event and audit row were written.
    ///
    /// <para><b>Red conditions.</b> Holds today for the lines; the triple half is the S146 addition: a same-hash path that
    /// compared the lines only would still pass, so the discriminating assertions are the single record, single event, and that
    /// the loser did NOT 409 (a triple compare that took the derived opening as changed under the lock, e.g. a case-1 recheck
    /// that re-derived 0 from a null ledger, would refuse the loser).</para>
    /// </summary>
    [Fact]
    public async Task SameMonthConcurrently_OneRecord_OtherTakesTheNoOpPath()
    {
        const string employeeId = "EMP-S146-RACE-III";
        await _fx.SeedEmployeeAsync(employeeId);
        await _fx.SeedApprovedMonthAsync(employeeId, Jun);
        _fx.Engine.Configure(employeeId, Jun, closing: 7m);

        var (a, b) = await RaceAsync(
            employeeId,
            () => _fx.CalculateAndExportMonthAsync(employeeId, Jun), "the first export of June",
            () => _fx.CalculateAndExportMonthAsync(employeeId, Jun), "the second export of June");

        Assert.True(a.Status == HttpStatusCode.OK, $"first export: {a.Json}");
        Assert.True(b.Status == HttpStatusCode.OK, $"second export: {b.Json}");
        Assert.Equal(1, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(1, await _fx.CountExportEventsAsync(employeeId));
        Assert.Equal(1, await _fx.CountAuditRowsAsync(employeeId, "PayrollExportGenerated"));
        var record = await _fx.RequireRecordAsync(employeeId, Jun);
        Assert.Equal<(decimal?, decimal?, string?)>((0m, 7m, "LEDGER_START"), (record.Opening, record.Closing, record.Provenance));
    }

    // ─── (iv) a raw /export-period waits on the ledger key ────────────────

    /// <summary>
    /// AC-C4 (iv) (kind F, Docker; R-15, "the raw routes take it too"). A raw <c>/export-period</c> of the same employee is
    /// OBSERVED waiting on the ledger key while connection A holds it, and completes (200) once A releases, writing a RAW record
    /// (no balances, source <c>EXPORT_PERIOD</c>). The raw routes are writers of <c>payroll_export_records</c> too, so a raw
    /// export racing a calculating export of the same employee waits behind it; the same-month UNIQUE key would serialize the pair
    /// even without the lock, which is why the observation, not the outcome, is what is pinned.
    ///
    /// <para><b>Red conditions (argued; 5a fix-up).</b> The RED is exactly: "the raw route takes the ledger lock and waits on
    /// it". The mutation "the raw routes skip the ledger lock" leaves the request unqueued, so <c>AssertWaitingAsync</c> fails
    /// with "never observed waiting". This fact does NOT pin that the lock is the FIRST statement (a lock taken after an
    /// uncontended row lock would still be observed waiting); the ordering claim rests on code reading.</para>
    /// </summary>
    [Fact]
    public async Task RawExportPeriod_IsObservedWaitingOnTheLedgerKey()
    {
        const string employeeId = "EMP-S146-RACE-IV";
        await _fx.SeedEmployeeAsync(employeeId);
        var body = new
        {
            profile = Profile(employeeId),
            calculationResults = new[]
            {
                new
                {
                    ruleId = "NORM_CHECK_37H",
                    employeeId,
                    success = true,
                    lineItems = new[]
                    {
                        new { timeType = "NORMAL_HOURS", hours = 7.4m, rate = 1.0m, date = new DateOnly(2026, 6, 10) },
                    },
                },
            },
        };

        await using var held = await HeldLock.AcquireAsync(_fx.ConnectionString, employeeId);
        Task<(HttpStatusCode Status, string Json)>? raw = null;
        try
        {
            raw = Task.Run(() => _fx.PostAsync("/api/payroll/export-period", body));
            await AssertWaitingAsync(1, raw, "the raw /export-period");
            Assert.Equal(0, await _fx.CountRecordsAsync(employeeId));

            await held.ReleaseAsync();
            var response = await raw;

            Assert.True(response.Status == HttpStatusCode.OK, $"raw export: {response.Status} {response.Json}");
            var record = await _fx.RequireRecordAsync(employeeId, Jun);
            Assert.Equal("EXPORT_PERIOD", record.Source);
            Assert.Null(record.Opening);
            Assert.Null(record.Closing);
            Assert.Null(record.Provenance);
        }
        finally
        {
            await held.ReleaseAsync();
            if (raw is not null)
            {
                try { await raw; } catch { /* the assertions above report the outcome */ }
            }
        }
    }
}
