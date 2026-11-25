using Npgsql;

namespace StatsTid.Infrastructure;

/// <summary>
/// The "pay inputs" lock (S148 PIL; refinement A-3, B-2, B-7, B-8; the plan's Block C): one
/// transaction-scoped PostgreSQL advisory lock for the whole system, so a change dated back into a
/// month and payroll's recording of that month take turns. Whichever commits first, the other sees
/// it: a recording that committed first is found by the change, which lists the month on HR's
/// worklist; a change that committed first is found by payroll's recheck, which refuses.
///
/// <para><b>The key.</b> <c>(148, 1)</c> in PostgreSQL's TWO-integer key space. Every other advisory
/// lock in the system is a single <c>hashtext</c> key (<c>employee-</c>, <c>payroll-ledger-</c>,
/// <c>reporting-org-</c>, <c>unit-org-</c>, <c>position-override-</c>), and PostgreSQL keeps the two
/// spaces apart, so this key can never equal one of them. In <c>pg_locks</c> it shows as locktype
/// <c>advisory</c>, classid 148, objid 1, objsubid 2. The key appears in this file only.</para>
///
/// <para><b>Who takes it.</b> EXCLUSIVE in every writer that dates a pay input into the past, or
/// changes one from a day a recording may still cover; SHARED in the payroll export and correction.
/// Always as the FIRST statement of a transaction that runs at READ COMMITTED, stated explicitly
/// (PAT-015: under REPEATABLE READ the snapshot would be taken before the wait, hiding the commit
/// the waiter waited for), at a route's own transaction head or at the head of a service that owns
/// its transaction; never inside a repository helper or a shared writer, and never on a separate
/// connection's read: a request that holds it exclusive and reads on a second connection would
/// otherwise wait for itself.</para>
///
/// <para><b>Why it cannot deadlock.</b> It comes first in every transaction that takes it, so a
/// transaction waiting for it holds nothing yet, and a request waiting for it holds no lock on any
/// other connection. The export and correction take it shared BEFORE ADR-034's per-employee
/// <c>payroll-ledger-</c> lock; the writers take it exclusive and never take <c>payroll-ledger-</c>.
/// So no transaction waits for this lock while holding the ledger lock, and none waits for the
/// ledger lock while holding this one exclusive.</para>
/// </summary>
public static class PayInputsLock
{
    /// <summary>
    /// Takes the pay-inputs lock EXCLUSIVE on <paramref name="conn"/>/<paramref name="tx"/>: the
    /// first statement of a writer's READ COMMITTED transaction. Waits for the lock within Npgsql's
    /// default 30-second command timeout (no <c>CommandTimeout</c> is set), so a writer queued behind a
    /// longer holder fails, writing nothing, rather than waiting on (F9); released at commit or rollback.
    /// </summary>
    public static async Task AcquireExclusiveAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(148, 1)", conn, tx);
        await cmd.ExecuteScalarAsync(ct);
    }

    /// <summary>
    /// Takes the pay-inputs lock SHARED: the first statement of the payroll export's and the
    /// correction's READ COMMITTED transactions, so recordings run side by side and wait only for a
    /// writer. Its two callers are <c>PayrollExportService.ExportAsync</c> and
    /// <c>RetroactiveCorrectionService.RecalculateAsync</c> (payroll), each followed by the
    /// <c>payroll-ledger-</c> lock and then payroll's rechecks.
    /// </summary>
    public static async Task AcquireSharedAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock_shared(148, 1)", conn, tx);
        await cmd.ExecuteScalarAsync(ct);
    }
}
