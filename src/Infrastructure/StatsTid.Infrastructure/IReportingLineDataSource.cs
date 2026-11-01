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

    /// <summary>
    /// Whether this user is an <b>acting user</b> at <paramref name="asOf"/>: <c>users.is_active</c>
    /// AND not past their recorded last day of employment, i.e. NOT
    /// <see cref="StatsTid.SharedKernel.Security.DepartedSubject.IsDeparted(bool, DateOnly?, DateOnly)"/>.
    /// False for an unknown user — the resolver treats "not acting" and "not present" identically,
    /// which is the fail-closed behaviour.
    ///
    /// <para><b>S147 / TASK-14712 (QUAL-230 T5c; NR-10).</b> Was <c>IsUserActiveAsync</c>, which read
    /// <c>is_active</c> alone. Under the owner's 5-day grace (OQ-2) a leaver stays <c>is_active</c>
    /// until D+6 so that THEIR manager can still sign their final month; the grace is not meant to keep
    /// the leaver in charge of anyone else. Every resolver leg that asks this question is a HOLDER of
    /// authority over someone (an ACTING manager, a stand-in, a PRIMARY manager), so from the day after
    /// their last day the departed holder is skipped exactly as a deactivated one always was, and
    /// routing escalates. <paramref name="asOf"/> is the resolver's own <c>effectiveAsOf</c>, never a
    /// second clock read (PAT-028).</para>
    /// </summary>
    Task<bool> IsActingUserAsync(string userId, DateOnly asOf, CancellationToken ct);
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

    public Task<bool> IsActingUserAsync(string userId, DateOnly asOf, CancellationToken ct)
        => ReportingLineRepository.QueryUserActingAsync(_conn, _tx, userId, asOf, ct);
}
