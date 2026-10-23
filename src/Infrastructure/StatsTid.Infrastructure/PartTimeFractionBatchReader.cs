using Npgsql;

namespace StatsTid.Infrastructure;

/// <summary>
/// S146 / TASK-14602 (QUAL-198) — the SQL implementation of
/// <see cref="IPartTimeFractionBatchReader"/>: ONE set-based query over <c>employee_profiles</c>
/// for the whole roster, never one query per employee.
///
/// <para>
/// <b>The predicate.</b> <c>effective_from &lt;= asOfDate AND (effective_to IS NULL OR
/// effective_to &gt; asOfDate)</c> — end-exclusive (ADR-018 D9), identical to
/// <see cref="EmploymentProfileResolver.GetByEmployeeIdAtAsync"/>, so a zero-width (retired) row
/// never matches and a row whose end equals the date does not cover it. Served by the
/// <c>idx_employee_profiles_history (employee_id, effective_from)</c> index.
/// </para>
///
/// <para>
/// <b>One row per employee, deterministically.</b> A valid timeline has at most one row covering a
/// date, but non-overlap is a write-router invariant (<c>TimelineSnapshot.Build</c>), not a
/// database constraint. <c>DISTINCT ON (employee_id) … ORDER BY employee_id, effective_from DESC</c>
/// therefore picks the latest-starting covering row if the invariant were ever broken, so two
/// identical requests can never return different fractions.
/// </para>
///
/// <para>
/// <b>Self-managed read-only connection</b>, like <see cref="EmploymentProfileResolver"/>: a pure
/// read, so the ADR-018 D5 <c>(conn, tx)</c> overload rule does not apply. Stateless over the
/// connection factory ⇒ singleton-safe.
/// </para>
///
/// <para>
/// <b>What it deliberately does NOT do.</b> It reads only the fraction. It does not join
/// <c>user_agreement_codes</c>, so unlike <see cref="EmploymentProfileResolver"/> it never throws
/// <c>EmployeeProfileNotFoundException</c> for an employee whose profile row exists but whose dated
/// agreement-code row is missing (that data-integrity condition is the resolver's to report).
/// </para>
/// </summary>
public sealed class PartTimeFractionBatchReader : IPartTimeFractionBatchReader
{
    private const string Sql =
        """
        SELECT DISTINCT ON (employee_id) employee_id, part_time_fraction
        FROM employee_profiles
        WHERE employee_id = ANY(@ids)
          AND effective_from <= @asOfDate
          AND (effective_to IS NULL OR effective_to > @asOfDate)
        ORDER BY employee_id, effective_from DESC
        """;

    private readonly DbConnectionFactory _dbFactory;

    public PartTimeFractionBatchReader(DbConnectionFactory dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, decimal>> GetFractionsAtAsync(
        IReadOnlyCollection<string> employeeIds, DateOnly asOfDate, CancellationToken ct = default)
    {
        var fractions = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (employeeIds.Count == 0)
            return fractions;

        await using var conn = _dbFactory.Create();
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("ids", employeeIds.ToArray());
        cmd.Parameters.AddWithValue("asOfDate", asOfDate);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            fractions[reader.GetString(0)] = reader.GetDecimal(1);

        return fractions;
    }
}
