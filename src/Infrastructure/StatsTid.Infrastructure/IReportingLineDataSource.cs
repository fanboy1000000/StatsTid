using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S125 / TASK-12501 step 3b — the four facts
/// <see cref="ReportingLineRepository.ResolveDesignatedApproverAsync(IReportingLineDataSource, string, DateOnly, CancellationToken)"/>
/// needs, behind an interface so the SAME resolution ALGORITHM can run against either live SQL or a
/// prefetched snapshot of the whole pending set.
///
/// <para><b>Why an interface over the DATA rather than a batched rewrite of the RULE.</b> The R3
/// precedence (admin-ACTING → vikar → PRIMARY → inactive-manager escalation), the FAIL-004
/// self-exclusion invariant and the depth-10 ceiling are the authorization rule. Re-expressing that
/// in SQL — or writing a second in-memory copy of it — would fork the thing this task must not touch.
/// Splitting out only the four LOOKUPS leaves the algorithm written exactly once, executing the same
/// branches in the same order; all that changes is where each fact comes from.</para>
///
/// <para>This is the difference between "prefetch the resolver's INPUTS" and "batch the DECISION".
/// The former is what makes the projection flat in the pending count; the latter is what the
/// refinement rejected outright as a P7 regression risk.</para>
///
/// <para><b>Equivalence is not assumed — it is tested.</b> The two implementations are driven over the
/// same fixture matrix and asserted to return identical verdicts pair-by-pair (not merely identical
/// totals, which can agree by luck). See the differential test in the regression suite.</para>
/// </summary>
public interface IReportingLineDataSource
{
    /// <summary>The single active line of the given relationship (<c>ACTING</c> / <c>PRIMARY</c>) for
    /// this employee, or null. Active means <c>effective_to IS NULL</c>.</summary>
    Task<ReportingLine?> GetActiveLineAsync(string employeeId, string relationship, CancellationToken ct);

    /// <summary>The approver's active vikar covering <paramref name="asOf"/>, or null. Coverage is
    /// <c>effective_to IS NULL AND until_date &gt;= asOf</c> — INCLUSIVE ("til og med").</summary>
    Task<ManagerVikar?> GetActiveVikarByApproverAsync(string approverId, DateOnly asOf, CancellationToken ct);

    /// <summary><c>users.is_active</c> for this user. False for an unknown user — the resolver treats
    /// "not active" and "not present" identically, which is the fail-closed behaviour.</summary>
    Task<bool> IsUserActiveAsync(string userId, CancellationToken ct);
}

/// <summary>
/// The live-SQL implementation — one round-trip per question, i.e. exactly the behaviour every caller
/// had before step 3b. It is the reference the prefetched source is differentially tested against.
/// </summary>
public sealed class SqlReportingLineDataSource : IReportingLineDataSource
{
    private readonly NpgsqlConnection _conn;
    private readonly NpgsqlTransaction? _tx;
    private readonly ManagerVikarRepository _vikarRepo;

    public SqlReportingLineDataSource(
        NpgsqlConnection conn, NpgsqlTransaction? tx, ManagerVikarRepository vikarRepo)
    {
        _conn = conn;
        _tx = tx;
        _vikarRepo = vikarRepo;
    }

    public Task<ReportingLine?> GetActiveLineAsync(string employeeId, string relationship, CancellationToken ct)
        => ReportingLineRepository.QueryActiveLineAsync(_conn, _tx, employeeId, relationship, ct);

    public Task<ManagerVikar?> GetActiveVikarByApproverAsync(string approverId, DateOnly asOf, CancellationToken ct)
        => _vikarRepo.GetActiveByApproverAsync(_conn, approverId, asOf, _tx, ct);

    public Task<bool> IsUserActiveAsync(string userId, CancellationToken ct)
        => ReportingLineRepository.QueryUserActiveAsync(_conn, _tx, userId, ct);
}
