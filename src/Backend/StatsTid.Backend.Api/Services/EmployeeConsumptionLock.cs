using Npgsql;

namespace StatsTid.Backend.Api.Services;

/// <summary>
/// S66 / TASK-6603 — the shared per-employee serialization primitive for the vacation-consumption
/// write paths (ADR-032 D4). Acquires a Postgres transaction-scoped advisory lock keyed on the
/// employee so two concurrent mutations of the same employee's consumption-bearing state — a Skema
/// absence save (TASK-6603) and a profile-PUT revaluation (TASK-6604) — cannot interleave between
/// each other's read-derive-write window.
///
/// <para>
/// <b>Why advisory and not row locks (ADR-032 D4).</b> The hazard is a TOCTOU across DIFFERENT
/// connections/queries: the Skema save re-derives <c>fullDayHours</c> from the dated employment
/// profile (the profile resolver opens its OWN connection), then writes the balance; a profile PUT
/// committing in between would make the just-read norm stale. A <c>FOR UPDATE</c> on
/// <c>entitlement_balances</c> alone does not cover the profile read on a separate connection. The
/// advisory lock is acquired FIRST, on the SAME transaction connection, and HELD TO COMMIT
/// (<c>pg_advisory_xact_lock</c>, transaction-scoped — auto-released on commit/rollback, no manual
/// unlock, no leak on exception). It therefore serializes the two writers REGARDLESS of which rows
/// each touches. Any subsequent <c>FOR UPDATE</c> in the same tx is taken AFTER this lock.
/// </para>
///
/// <para>
/// <b>Key derivation.</b> <c>pg_advisory_xact_lock(hashtext('employee-' || @employeeId))</c> — the
/// employee id namespaced with the <c>employee-</c> stream prefix (ADR-018 D6) so the hash space is
/// shared with nothing else. The hashing is done server-side by <c>hashtext</c> so .NET and Postgres
/// agree on the bigint key. The separate-connection profile resolver (which the in-lock re-derive
/// uses) is intentionally NOT enrolled in this transaction: the advisory lock serializes the
/// WRITERS, so the resolver's own snapshot is fine — it observes whatever committed before the lock
/// was acquired (ADR-032 D4 separate-connection-resolver rationale).
/// </para>
/// </summary>
public static class EmployeeConsumptionLock
{
    /// <summary>
    /// Acquires the transaction-scoped advisory lock for <paramref name="employeeId"/> on
    /// <paramref name="conn"/>/<paramref name="tx"/>. MUST be the first statement inside the
    /// consumption transaction (ADR-032 D4: acquired first, held to commit, precedes any
    /// <c>FOR UPDATE</c>). Blocks until the lock is available; released automatically at
    /// commit/rollback.
    /// </summary>
    public static async Task AcquireAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId, CancellationToken ct = default)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext('employee-' || @employeeId))", conn, tx);
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await cmd.ExecuteScalarAsync(ct);
    }
}
