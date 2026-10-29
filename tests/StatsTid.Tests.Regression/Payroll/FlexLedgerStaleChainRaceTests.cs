using System.Net;
using System.Text.Json;
using Npgsql;
using static StatsTid.Tests.Regression.Payroll.FlexLedgerHostFixture;

namespace StatsTid.Tests.Regression.Payroll;

/// <summary>
/// S147 / TASK-14700-T (QUAL-229; AC-229-4; rulings R-2, R-8) — the race between a correction of an EARLY month and the
/// export of a LATE month, pinned deterministically against a real Postgres with its ordering FORCED.
///
/// <para>
/// <b>Plain language.</b> A month's opening balance is worked out BEFORE the per-employee ledger lock is taken, from what is
/// recorded at that moment, and the same derivation runs again under the lock before anything is written. S147 widens that
/// derivation from "the month before" to "the whole recorded chain". So an export of April that starts while January,
/// February and March are consistent, but whose turn at the lock comes after a correction of January has committed, finds a
/// stale February under the lock and must not write: it answers 409 <c>payroll-ledger-changed</c> ("retry"). The retry then
/// sees the stale chain at admission and answers 409 <c>payroll-prior-month-stale</c>, naming February, the month to
/// recalculate first. Nothing is ever written on a stale chain.
/// </para>
///
/// <para>
/// <b>How the order is forced (no sleep decides an outcome).</b> The machinery of <see cref="FlexLedgerRaceTests"/>, copied
/// here because it is private there: connection A holds <c>pg_advisory_xact_lock(hashtext('payroll-ledger-' || E))</c>; the
/// first request is started and OBSERVED waiting on it (<c>pg_stat_activity</c>, a bounded condition wait), only then the
/// second; once both are waiting A commits, and PostgreSQL grants the lock in queue order. The correction is queued first.
/// </para>
///
/// <para>
/// <b>Status.</b> Docker-gated: <b>CI-pending</b>, never locally green.
/// </para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class FlexLedgerStaleChainRaceTests : IClassFixture<FlexLedgerHostFixture>
{
    private static readonly DateOnly Dec25 = new(2025, 12, 1);
    private static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly DateOnly Feb = new(2026, 2, 1);
    private static readonly DateOnly Mar = new(2026, 3, 1);
    private static readonly DateOnly Apr = new(2026, 4, 1);

    private readonly FlexLedgerHostFixture _fx;

    public FlexLedgerStaleChainRaceTests(FlexLedgerHostFixture fixture) => _fx = fixture;

    // ─── the forced-ordering machinery (copied from FlexLedgerRaceTests, where it is private) ────────

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

    // ─── AC-229-4 ────────────────────────────────────────────────────────

    /// <summary>
    /// AC-229-4 (kind F, Docker; R-2, R-8). January (0 to 10), February (CHAINED 10 to 15) and March (CHAINED 15 to 16) are
    /// exported: a consistent chain. A correction of January (closing 10 to 12) and the first export of April are both started
    /// while the ledger lock is held, the CORRECTION queued first. April's admission saw a consistent chain
    /// (<c>CHAINED 16</c>); once the correction has committed, April's under-lock recheck walks the chain, finds February stale
    /// (opening 10, January now hands on 12) and refuses: April answers 409 <c>payroll-ledger-changed</c> naming
    /// <c>2026-04</c> and writes nothing. The correction commits (January now 12; it names February). The RETRY of April
    /// answers 409 <c>payroll-prior-month-stale</c> naming <c>2026-02</c>, the month to recalculate first, and still writes
    /// nothing.
    ///
    /// <para><b>Red conditions (argued).</b> The pre-S147 recheck derives April's opening from March's record alone: it is
    /// <c>CHAINED 16</c> both at admission and under the lock, so April exports with 200 (and writes a record on the stale
    /// chain): the <c>ledger-changed</c> assertion on the first response fails; the retry, today, also answers 200. A recheck
    /// that did not walk, or a walk that ran at admission only, fails the same first assertion.</para>
    /// </summary>
    [Fact]
    public async Task CorrectionBetweenAdmissionAndRecheck_LedgerChanged_ThenRetryNamesStaleMonth()
    {
        const string employeeId = "EMP-S147-RACE-STALE";
        await _fx.SeedEmployeeAsync(employeeId);
        foreach (var month in new[] { Dec25, Jan, Feb, Mar, Apr })
            await _fx.SeedApprovedMonthAsync(employeeId, month);
        _fx.Engine.Configure(employeeId, Jan, closing: 10m);
        _fx.Engine.ConfigureOpeningDependent(employeeId, Feb, closingDelta: 5m);
        _fx.Engine.ConfigureOpeningDependent(employeeId, Mar, closingDelta: 1m);
        _fx.Engine.ConfigureOpeningDependent(employeeId, Apr, closingDelta: 2m);
        foreach (var month in new[] { Jan, Feb, Mar })
        {
            var seed = await _fx.CalculateAndExportMonthAsync(employeeId, month);
            Assert.True(seed.Status == HttpStatusCode.OK, $"seed export of {month:yyyy-MM} failed: {seed.Json}");
        }
        _fx.Engine.Configure(employeeId, Jan, closing: 12m); // the correction will move January's closing 10 to 12
        var recordsBefore = await _fx.CountRecordsAsync(employeeId);
        var exportsBefore = await _fx.CountExportEventsAsync(employeeId);
        Assert.Equal(3, recordsBefore);

        var (correction, export) = await RaceAsync(
            employeeId,
            () => _fx.RecalculateMonthAsync(employeeId, Jan), "the correction of January",
            () => _fx.CalculateAndExportMonthAsync(employeeId, Apr), "the first export of April");

        Assert.True(correction.Status == HttpStatusCode.OK, $"correction: {correction.Json}");
        var corrected = ReadCorrection(correction.Json);
        Assert.Equal<(decimal?, decimal?)>((10m, 12m), (corrected.ClosingFlexBalanceBefore, corrected.ClosingFlexBalanceAfter));
        Assert.Equal(Feb, corrected.NextPeriodToRecalculateStart);
        Assert.True(export.Status == HttpStatusCode.Conflict, $"export of April: {export.Status} {export.Json}");
        using (var doc = JsonDocument.Parse(export.Json))
        {
            Assert.Equal("payroll-ledger-changed", Str(doc.RootElement, "kind"));
            Assert.Equal("2026-04", Str(doc.RootElement, "month"));
        }
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Apr));
        Assert.Equal(recordsBefore, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(exportsBefore, await _fx.CountExportEventsAsync(employeeId));
        var january = await _fx.RequireRecordAsync(employeeId, Jan);
        Assert.Equal<(decimal?, decimal?, string?)>((0m, 12m, "LEDGER_START"), (january.Opening, january.Closing, january.Provenance));
        var february = await _fx.RequireRecordAsync(employeeId, Feb);
        Assert.Equal<(decimal?, decimal?, string?)>((10m, 15m, "CHAINED"), (february.Opening, february.Closing, february.Provenance));

        var retry = await _fx.CalculateAndExportMonthAsync(employeeId, Apr);

        Assert.True(retry.Status == HttpStatusCode.Conflict, $"retry of April: {retry.Status} {retry.Json}");
        using (var doc = JsonDocument.Parse(retry.Json))
        {
            Assert.Equal("payroll-prior-month-stale", Str(doc.RootElement, "kind"));
            Assert.Equal("2026-02", Str(doc.RootElement, "month"));
            Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        }
        Assert.Null(await _fx.ReadRecordAsync(employeeId, Apr));
        Assert.Equal(recordsBefore, await _fx.CountRecordsAsync(employeeId));
        Assert.Equal(exportsBefore, await _fx.CountExportEventsAsync(employeeId));
    }
}
