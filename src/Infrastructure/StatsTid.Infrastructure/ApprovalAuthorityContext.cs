using Npgsql;

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
public sealed class ApprovalAuthorityContext : IDisposable
{
    // ── S126 Step-7a BLOCKER — the SINGLE-USE LATCH ─────────────────────────────────────────────
    // The first guard bound to the NpgsqlTransaction by reference; Npgsql recycles that instance per
    // connection, so it silently passed the reuse case. The second bound to the CONNECTION, which
    // catches a cross-connection hoist but still lets two SEQUENTIAL snapshots on ONE connection share
    // the memo — the exact hazard, left uncovered.
    //
    // Both attempts were trying to INFER the lifetime from an ADO.NET object. The rule is not about
    // connections or transactions: it is "lifetime is ONE projection call". So enforce that directly —
    // the owner disposes the context when the call ends and the context is permanently spent. This is
    // independent of Npgsql's object reuse, needs no round-trip to txid_current(), and cannot be
    // defeated by pooling.
    private bool _spent;

    /// <summary>Ends this context's life. After this every memo access throws, so a context hoisted
    /// beyond its projection call fails loudly instead of serving pre-snapshot authority facts.</summary>
    public void Dispose() => _spent = true;

    private void ThrowIfSpent()
    {
        if (_spent)
            throw new InvalidOperationException(
                "ApprovalAuthorityContext was used after its projection call ended. Its memoized " +
                "authority answers are equivalent to re-querying ONLY inside the single snapshot they " +
                "were taken in; reused later they can authorize against state that no longer exists " +
                "(a role revocation, edge reassignment, deactivation or transfer since). Construct one " +
                "context per projection call.");
    }

    /// <summary>The single authority date for the whole projection (invariant 7 — computed once, so a
    /// projection cannot straddle a date boundary mid-loop). Memo keys do NOT include it precisely
    /// because it cannot vary within one context.</summary>
    public DateOnly AsOf { get; }

    private readonly Dictionary<string, (string? ManagerId, string? Method, int Depth)> _edges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _roleFloor = new(StringComparer.Ordinal);
    private readonly Dictionary<(string EmployeeId, string ActorId), bool> _sameOrg = new();

    public ApprovalAuthorityContext(DateOnly asOf) => AsOf = asOf;

    /// <summary>The connection this context was first used on. Set on first use rather than in the
    /// constructor so the public ctor keeps its signature (tests construct one without a live tx).</summary>
    private NpgsqlConnection? _boundConn;

    /// <summary>
    /// S126 / W3 — makes the "lifetime is ONE projection call" rule above partially ENFORCEABLE
    /// instead of merely documented. The prose said "never promote this to a field or a DI
    /// singleton"; nothing failed if someone did.
    ///
    /// <para>The hazard is not hypothetical bookkeeping: outside one snapshot the memo serves answers
    /// from before a mid-request role revocation, edge reassignment, deactivation or transfer — i.e.
    /// it authorizes against state that no longer exists.</para>
    ///
    /// <para><b>What this catches, and what it CANNOT — measured, not assumed.</b> The first design
    /// bound to the <see cref="NpgsqlTransaction"/> and compared by reference. That does not work:
    /// <b>Npgsql RECYCLES the transaction instance per connection</b>, so two sequential
    /// <c>BeginTransactionAsync</c> calls on one connection hand back the SAME object and compare
    /// equal. The guard silently passed the exact case it was written for, and the test that proved
    /// it is the reason this comment exists rather than a false claim of coverage.</para>
    ///
    /// <para><b>Superseded as the primary mechanism (S126 Step-7a).</b> Connection binding catches a
    /// cross-connection hoist but NOT two sequential snapshots on one connection — so it was never
    /// sufficient on its own. The single-use latch at the top of this class enforces the actual rule
    /// ("one projection call") directly and is what closes that gap. This binding is retained as
    /// defence in depth: it catches a hoist that never reaches Dispose.</para>
    /// </summary>
    internal void BindTo(NpgsqlConnection conn)
    {
        if (_boundConn is null)
        {
            _boundConn = conn;
            return;
        }
        if (!ReferenceEquals(_boundConn, conn))
            throw new InvalidOperationException(
                "ApprovalAuthorityContext was reused across two connections. Its memoized authority " +
                "answers are only equivalent to re-querying INSIDE the single snapshot they were taken " +
                "in; outside it they can authorize against state that no longer exists. " +
                "Construct one context per projection call.");
    }

    internal async Task<(string? ManagerId, string? Method, int Depth)> ResolveEdgeAsync(
        string employeeId, Func<Task<(string? ManagerId, string? Method, int Depth)>> resolve)
    {
        ThrowIfSpent();
        if (_edges.TryGetValue(employeeId, out var cached))
            return cached;
        var value = await resolve();
        _edges[employeeId] = value;
        return value;
    }

    internal async Task<bool> RoleFloorAsync(string userId, Func<Task<bool>> query)
    {
        ThrowIfSpent();
        if (_roleFloor.TryGetValue(userId, out var cached))
            return cached;
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
        ThrowIfSpent();
        var key = (employeeId, actorId);
        if (_sameOrg.TryGetValue(key, out var cached))
            return cached;
        var value = await check();
        _sameOrg[key] = value;
        return value;
    }
}
