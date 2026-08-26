using Npgsql;
using StatsTid.SharedKernel.Models;

namespace StatsTid.Infrastructure;

/// <summary>
/// S136 / TASK-13602 (ADR-040 D1) — in-transaction employment-window read surface for
/// advisory-locked write paths (the D3 strand/re-hire guards, the registration gates'
/// in-lock re-reads).
///
/// <para>
/// Lives in <c>StatsTid.Infrastructure</c> rather than alongside
/// <see cref="StatsTid.SharedKernel.Interfaces.IEmploymentWindowResolver"/> because the
/// parameter list exposes <see cref="NpgsqlConnection"/> / <see cref="NpgsqlTransaction"/>.
/// Putting those on a SharedKernel interface would force an Npgsql package reference onto
/// <c>StatsTid.SharedKernel</c>, which would transitively reach
/// <c>StatsTid.RuleEngine.Api</c> and regress the post-S19 <c>b4fc670</c> assembly-graph
/// cleanup that keeps the rule engine Npgsql-free — the same split-interface design as
/// <see cref="Outbox.IOutboxEnqueue"/> (ADR-018 D3 rationale).
/// </para>
///
/// <para>
/// The single concrete implementation <see cref="EmploymentWindowResolver"/> implements
/// both the SharedKernel self-managed surface and this in-tx surface with IDENTICAL
/// window semantics (D1 inclusive end, D2 NULL-unbounded, D3 no <c>is_active</c> filter,
/// fail-loud on missing user). DI registers the concrete once and exposes it under both
/// contracts.
/// </para>
///
/// <para>
/// <b>Why this surface exists — do NOT "harmonize" it away.</b> The
/// <c>IEmploymentProfileResolver</c> twin is self-managed-only because it is a pure
/// hot-path read (its ADR-023 rationale). The window resolver is DIFFERENT: S136's
/// strand and re-hire guards (ADR-040 D1/D3) validate the window INSIDE an
/// advisory-locked transaction (<c>EmployeeConsumptionLock</c>,
/// <c>pg_advisory_xact_lock</c>). A self-managed read there opens a private connection
/// that sits OUTSIDE the caller's transaction and therefore outside the lock — it would
/// miss a concurrent write that committed while the caller blocked on the lock acquire.
/// That is the exact race documented at <c>TimeEndpoints.cs</c> ("Placement (PAT-015)":
/// the approval-status in-lock read) and pinned by
/// <c>ApprovalPeriodRepository.GetByEmployeeAndPeriodAsync(conn, tx, …)</c>. A future
/// cleanup that removes this interface "for consistency with the profile resolver"
/// reintroduces that race silently.
/// </para>
///
/// <para>
/// Isolation prerequisite (same as the ApprovalPeriodRepository sibling): seeing a
/// concurrently-committed row requires the caller's transaction to be
/// <c>ReadCommitted</c> — under <c>RepeatableRead</c> the snapshot is pinned before the
/// advisory lock is granted.
/// </para>
/// </summary>
public interface IEmploymentWindowResolverInTx
{
    /// <summary>
    /// Returns the employment-window fact for the given employee on the given date,
    /// riding the caller-supplied <paramref name="conn"/> + <paramref name="tx"/>;
    /// never commits, rolls back, or closes them.
    /// </summary>
    Task<EmploymentWindowStatus> GetStatusAsync(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string employeeId, DateOnly date, CancellationToken ct = default);
}
