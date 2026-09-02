using Npgsql;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S136 / TASK-13602 — concrete <see cref="IEmploymentWindowResolver"/> (ADR-040 D1/D2/D3).
/// Reads the single stored employment spell (<c>users.employment_start_date</c> /
/// <c>employment_end_date</c>) and answers EMPLOYED / NOT_EMPLOYED for a date:
/// end date INCLUSIVE — the last day employed (D1: <c>date == end</c> ⇒ EMPLOYED,
/// <c>date == start</c> ⇒ EMPLOYED); NULL = unbounded on that side (D2), so both-NULL
/// employees are employed on every date and enforcement needs no backfill.
/// S137 (ADR-040 D5) adds <see cref="GetWindowsAsync"/> — the range-scoped, list-shaped
/// (spells-proof) window read segmentation consumers use to place employment boundaries;
/// same D1/D2/D3 semantics, same fail-loud missing-user contract.
///
/// <para>
/// <b>Deliberately NO <c>is_active</c> / <c>end_date_deactivated</c> filter (ADR-040 D3).</b>
/// The employment window is a fact about DATES regardless of login state — <c>is_active</c>
/// governs login/session for the ACTOR only. Every sibling repository read filters
/// <c>is_active = TRUE</c>, so an unguarded copy-paste of that predicate here would
/// silently break the leaver-correction flow: HR correcting a deactivated leaver's final
/// in-window month must still resolve EMPLOYED for those dates (the S70
/// <c>IncludingTerminated</c> role gate governs WHO may write; this resolver governs
/// WHAT DATES are inside the spell).
/// </para>
///
/// <para>
/// <b>Fail-loud on missing user row.</b> Throws <see cref="InvalidOperationException"/> —
/// a data assertion, not a domain state: every caller resolves the subject before asking
/// about dates, so no <c>users</c> row here means a caller bug. Deliberately unlike the
/// <see cref="EmploymentProfileResolver"/> twin's null-on-no-covering-row contract, which
/// ADR-040 D10 keeps unchanged.
/// </para>
///
/// <para>
/// <b>Two surfaces, one implementation (the IOutboxEnqueue split-interface pattern).</b>
/// The SharedKernel <see cref="IEmploymentWindowResolver"/> is the self-managed read
/// (own pooled connection — fine outside a lock); the Infrastructure
/// <see cref="IEmploymentWindowResolverInTx"/> rides the caller's advisory-locked
/// transaction — required by S136's strand/re-hire guards, where a self-managed read
/// would sit outside the lock and miss a concurrently-committed write (the TimeEndpoints
/// PAT-015 race). The full do-not-harmonize warning lives on
/// <see cref="IEmploymentWindowResolverInTx"/>. DI registers this concrete once and
/// exposes it under both contracts.
/// </para>
/// </summary>
public sealed class EmploymentWindowResolver : IEmploymentWindowResolver, IEmploymentWindowResolverInTx
{
    // ADR-040 D3: no is_active predicate — see class doc before "fixing" this to match
    // sibling repositories.
    private const string Sql =
        """
        SELECT employment_start_date, employment_end_date
        FROM users
        WHERE user_id = @employeeId
        """;

    private readonly DbConnectionFactory _dbFactory;

    public EmploymentWindowResolver(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <inheritdoc />
    public async Task<EmploymentWindowStatus> GetStatusAsync(
        string employeeId, DateOnly date, CancellationToken ct = default)
    {
        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Sql, conn);
        return await ExecuteAsync(cmd, employeeId, date, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmploymentWindow>> GetWindowsAsync(
        string employeeId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Fail-loud on an inverted range: an empty result here would read as "no employed
        // days in the range" (a legitimate domain answer) and silently mask the caller's
        // date arithmetic bug — the same fail-loud stance as the missing-user case.
        if (to < from)
        {
            throw new ArgumentException(
                $"Employment-window range query is inverted: to ({to:yyyy-MM-dd}) is before " +
                $"from ({from:yyyy-MM-dd}). EmployeeId='{employeeId}'.",
                nameof(to));
        }

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Sql, conn);
        var (start, end) = await ReadWindowRowAsync(cmd, employeeId, ct);

        // Overlap test for [start, end] vs [from, to], both end-INCLUSIVE (ADR-040 D1);
        // a NULL side is unbounded (D2), so a both-NULL window overlaps every range —
        // that is why it counts as ONE unbounded entry, never an empty list.
        var overlaps = (!start.HasValue || start.Value <= to)
                    && (!end.HasValue || end.Value >= from);

        // 0-or-1 entries while storage is the single users-row spell (ADR-040 D1); the
        // deferred spells increment changes THIS body (query a spells table), never the
        // list-shaped contract — the "spells = storage + resolver only" promise.
        return overlaps
            ? new[] { new EmploymentWindow(start, end) }
            : Array.Empty<EmploymentWindow>();
    }

    /// <inheritdoc />
    public async Task<EmploymentWindowStatus> GetStatusAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, DateOnly date, CancellationToken ct = default)
    {
        // Caller-supplied conn + tx; this method never commits, rolls back, or closes them
        // (same contract as ApprovalPeriodRepository's in-tx sibling).
        await using var cmd = new NpgsqlCommand(Sql, conn, tx);
        return await ExecuteAsync(cmd, employeeId, date, ct);
    }

    private static async Task<EmploymentWindowStatus> ExecuteAsync(
        NpgsqlCommand cmd, string employeeId, DateOnly date, CancellationToken ct)
    {
        var (start, end) = await ReadWindowRowAsync(cmd, employeeId, ct);
        return Evaluate(date, start, end);
    }

    /// <summary>
    /// Executes the shared single-spell SELECT and returns the raw window dates —
    /// the one row-reading path both <c>GetStatusAsync</c> and <c>GetWindowsAsync</c>
    /// funnel through, so the fail-loud missing-user contract cannot drift between them.
    /// </summary>
    private static async Task<(DateOnly? Start, DateOnly? End)> ReadWindowRowAsync(
        NpgsqlCommand cmd, string employeeId, CancellationToken ct)
    {
        cmd.Parameters.AddWithValue("employeeId", employeeId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
        {
            // Fail-loud data assertion (ADR-040): callers resolve the subject before asking
            // about dates — a missing users row is a bug, not a domain state.
            throw new InvalidOperationException(
                $"Employment-window lookup failed: no users row exists for employee " +
                $"'{employeeId}'. Callers must resolve the subject before consulting the " +
                "employment window (ADR-040 D1) — a missing row here is a caller bug or a " +
                "data-integrity fault, never a NOT_EMPLOYED answer.");
        }

        DateOnly? start = reader.IsDBNull(0) ? null : reader.GetFieldValue<DateOnly>(0);
        DateOnly? end = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateOnly>(1);
        return (start, end);
    }

    private static EmploymentWindowStatus Evaluate(DateOnly date, DateOnly? start, DateOnly? end)
    {
        // D2: NULL = unbounded on that side (guards pass through).
        // D1: the spell is [start, end] with end INCLUSIVE — the last day employed.
        if (start.HasValue && date < start.Value)
        {
            return EmploymentWindowStatus.NOT_EMPLOYED;
        }
        if (end.HasValue && date > end.Value)
        {
            return EmploymentWindowStatus.NOT_EMPLOYED;
        }
        return EmploymentWindowStatus.EMPLOYED;
    }
}
