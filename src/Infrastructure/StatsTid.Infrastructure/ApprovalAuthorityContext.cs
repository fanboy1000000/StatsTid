namespace StatsTid.Infrastructure;

/// <summary>
/// S125 / TASK-12501 step 3 — a per-call memo for the approval-authority predicate.
///
/// <para><b>The problem it solves.</b> <c>GetPeriodStatusProjectionForTreeAsync</c> asks the same
/// questions over and over within a single projection: it resolves an employee's designated approver,
/// then the authority gate re-resolves that SAME employee once per candidate; the role floor —
/// a fact about the CANDIDATE alone — is asked once per (candidate, employee) pair and twice for a
/// unit-leader candidate; the same-Organisation check is asked once per leg. Measured at 27 SQL
/// statements per pending employee, of which roughly 16–17 are re-derivations.</para>
///
/// <para><b>Why this is not a second implementation of anything.</b> Every value here is produced BY
/// the authorizer's own code path and merely remembered — the caller cannot hand in an answer it
/// computed some other way. That is the whole design: a cache in front of one implementation, never a
/// parallel one. It is why this does not carry the drift risk that a bulk/SQL rewrite of the rule
/// would, and why ADR-027/038's one-encoding requirement still holds.</para>
///
/// <para><b>Why caching is SOUND rather than merely convenient.</b> Only inside the projection's
/// REPEATABLE READ snapshot (step 2). There, the underlying rows cannot change for the transaction's
/// duration, so "ask once and remember" and "ask every time" are the SAME answer by construction —
/// not an accepted staleness trade. Without the snapshot this class would be a behaviour change
/// requiring a ruling; with it, it is an optimisation. That ordering is the point of steps 2 and 3
/// landing together.</para>
///
/// <para><b>Lifetime is ONE projection call. Never promote this to a field or a DI singleton.</b>
/// Outside a snapshot the memo would serve answers from before a mid-request role revocation, edge
/// reassignment, deactivation or transfer — i.e. it would authorize against state that no longer
/// exists. It is deliberately not thread-safe: the tally loop is sequential, and making it concurrent
/// would need more thought than a lock.</para>
/// </summary>
public sealed class ApprovalAuthorityContext
{
    /// <summary>The single authority date for the whole projection (invariant 7 — computed once, so a
    /// projection cannot straddle a date boundary mid-loop). Memo keys do NOT include it precisely
    /// because it cannot vary within one context.</summary>
    public DateOnly AsOf { get; }

    private readonly Dictionary<string, (string? ManagerId, string? Method, int Depth)> _edges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _roleFloor = new(StringComparer.Ordinal);
    private readonly Dictionary<(string EmployeeId, string ActorId), bool> _sameOrg = new();

    public ApprovalAuthorityContext(DateOnly asOf) => AsOf = asOf;

    /// <summary>Statements SAVED by this memo, for the perf assertions and for logging. Counts cache
    /// HITS, so a projection can report how much re-derivation it avoided.</summary>
    public int MemoHits { get; private set; }

    internal async Task<(string? ManagerId, string? Method, int Depth)> ResolveEdgeAsync(
        string employeeId, Func<Task<(string? ManagerId, string? Method, int Depth)>> resolve)
    {
        if (_edges.TryGetValue(employeeId, out var cached))
        {
            MemoHits++;
            return cached;
        }
        var value = await resolve();
        _edges[employeeId] = value;
        return value;
    }

    internal async Task<bool> RoleFloorAsync(string userId, Func<Task<bool>> query)
    {
        if (_roleFloor.TryGetValue(userId, out var cached))
        {
            MemoHits++;
            return cached;
        }
        var value = await query();
        _roleFloor[userId] = value;
        return value;
    }

    /// <summary>
    /// Memoizes the same-Organisation VERDICT for an (employee, actor) pair.
    ///
    /// <para>Deliberately keyed on the PAIR rather than caching each user's home Organisation. A
    /// per-user org map would be the bigger win, but reproducing "deny when the user is missing, or
    /// inactive, or their home Organisation is inactive" outside the query would be a second encoding
    /// of the fail-closed rule — the drift this design exists to avoid. The pair verdict is whatever
    /// the one implementation returned, cached; nothing is re-derived.</para>
    /// </summary>
    internal async Task<bool> SameOrganisationAsync(
        string employeeId, string actorId, Func<Task<bool>> check)
    {
        var key = (employeeId, actorId);
        if (_sameOrg.TryGetValue(key, out var cached))
        {
            MemoHits++;
            return cached;
        }
        var value = await check();
        _sameOrg[key] = value;
        return value;
    }
}
