using Npgsql;
using NpgsqlTypes;

namespace StatsTid.Infrastructure;

/// <summary>
/// S136 Step-5a fix-forward (Codex BLOCKER 2 + Reviewer WARNING 1 + owner ruling 2026-08-26) —
/// the ONE ADR-040 D3 strand-check query, shared by EVERY employment-window writer so the
/// predicate cannot drift between surfaces:
/// <list type="bullet">
///   <item><description>the two admin employment-date PUTs
///   (<c>EmploymentDateEndpoints.CheckStrandedRegistrationsAsync</c>, which owns the HTTP 409
///   shaping — this class was LIFTED out of that helper);</description></item>
///   <item><description>the settlement reversal's subsumed end-date correction
///   (<c>SettlementReversalService</c>), which pre-S136-Step-5a wrote end dates through the
///   lifecycle writer with NO strand check at all — the second end-date writer Codex BLOCKER 2
///   found. The reversal runs this check NARROWING-ONLY (see the call site's
///   predicate).</description></item>
/// </list>
///
/// <para><b>What "stranded" means (ADR-040 D3).</b> A registration row dated OUTSIDE the
/// proposed employment window [<paramref name="proposedStart"/>, <paramref name="proposedEnd"/>]
/// (end INCLUSIVE per D1). The WHOLE proposed window is checked, not just the edited side —
/// fail-closed: a window edit never commits while ANY registered data sits outside it. ALL
/// THREE registration families the write side gates are queried — <c>time_entries_projection</c>,
/// <c>absences_projection</c> AND <c>work_time_projection</c> (the third arm is the S136 Step-5a
/// Reviewer-W1 fix: the write-side window gate covers all three arrays, so the edit side must
/// see all three or a window edit could orphan a work-time day the save could never have
/// written). NULL bounds are unbounded (D2); both-NULL skips the query entirely.</para>
///
/// <para><b>Why the caller's (conn, tx).</b> Every caller holds the ADR-032 D4 employee
/// advisory lock on that tx, and the registration writers acquire the SAME key — a self-managed
/// connection would read OUTSIDE the lock and miss a registration committed while the edit was
/// blocked on it (the S136 lock regime / the TimeEndpoints race class). Never "simplify" this
/// to a pooled read.</para>
/// </summary>
public static class EmploymentWindowStrandCheck
{
    /// <summary>One stranded month (ascending <c>YYYY-MM</c>) with its per-family counts —
    /// the pointer-contract row every caller surfaces (the endpoints as the structured
    /// <c>strandedMonths</c> 409 array; the reversal service in its fail-closed reason).</summary>
    public sealed record StrandedMonth(
        string Month, long TimeEntryCount, long AbsenceCount, long WorkTimeCount);

    /// <summary>
    /// The in-tx strand query: every month holding at least one registration row dated outside
    /// the proposed window, with per-family counts. Empty ⇒ the edit may proceed.
    /// </summary>
    public static async Task<IReadOnlyList<StrandedMonth>> QueryStrandedMonthsAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx, string employeeId,
        DateOnly? proposedStart, DateOnly? proposedEnd, CancellationToken ct = default)
    {
        if (proposedStart is null && proposedEnd is null)
            return []; // D2: fully unbounded window — nothing can be outside it.

        var strandedMonths = new List<StrandedMonth>();
        await using var cmd = new NpgsqlCommand(
            """
            SELECT to_char(stranded.date, 'YYYY-MM') AS month,
                   COUNT(*) FILTER (WHERE stranded.kind = 'timeEntry') AS time_entry_count,
                   COUNT(*) FILTER (WHERE stranded.kind = 'absence') AS absence_count,
                   COUNT(*) FILTER (WHERE stranded.kind = 'workTime') AS work_time_count
            FROM (
                SELECT date, 'timeEntry' AS kind
                FROM time_entries_projection
                WHERE employee_id = @employeeId
                UNION ALL
                SELECT date, 'absence' AS kind
                FROM absences_projection
                WHERE employee_id = @employeeId
                UNION ALL
                SELECT date, 'workTime' AS kind
                FROM work_time_projection
                WHERE employee_id = @employeeId
            ) stranded
            WHERE (@proposedStart IS NOT NULL AND stranded.date < @proposedStart)
               OR (@proposedEnd IS NOT NULL AND stranded.date > @proposedEnd)
            GROUP BY 1
            ORDER BY 1
            """, conn, tx);
        // Explicitly typed parameters: the IS NOT NULL branches need a typed NULL —
        // AddWithValue(DBNull) cannot infer `date` in this WHERE shape.
        cmd.Parameters.Add(new NpgsqlParameter("employeeId", NpgsqlDbType.Text) { Value = employeeId });
        cmd.Parameters.Add(new NpgsqlParameter("proposedStart", NpgsqlDbType.Date) { Value = (object?)proposedStart ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("proposedEnd", NpgsqlDbType.Date) { Value = (object?)proposedEnd ?? DBNull.Value });
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            strandedMonths.Add(new StrandedMonth(
                reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3)));
        }
        return strandedMonths;
    }
}
