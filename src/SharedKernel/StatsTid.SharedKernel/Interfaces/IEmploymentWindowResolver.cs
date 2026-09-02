namespace StatsTid.SharedKernel.Interfaces;

using StatsTid.SharedKernel.Models;

/// <summary>
/// S136 / TASK-13602 (ADR-040 D1) — the ONE read surface for the employment window.
/// Given (employee, date), answers the domain question "was this person employed on that
/// date?" All consumers — registration gates, the approval send-gate, payroll planning
/// (D5), accrual capping (D9) — read the window through this resolver, never the
/// <c>users</c> date columns directly, so growing to a spells table later changes storage
/// + resolver only, never the consumers.
///
/// <para>
/// <b>Semantics (binding on every implementation):</b> the window is
/// <c>[employment_start_date, employment_end_date]</c> with the end date INCLUSIVE — the
/// last day employed (ADR-040 D1, pinning the ADR-033 / S70 R1 semantics):
/// <c>date == start</c> ⇒ EMPLOYED and <c>date == end</c> ⇒ EMPLOYED. A NULL date is
/// unbounded on that side (D2): NULL start = employed since the beginning of time, NULL
/// end = open-ended; both-NULL employees are employed on every date, which is why
/// enforcement needs no data backfill. The window is a fact about DATES only — it is
/// NEVER filtered by <c>is_active</c>, which governs login/session for the actor (D3).
/// </para>
///
/// <para>
/// <b>Fail-loud on missing subject:</b> throws <see cref="InvalidOperationException"/>
/// when no <c>users</c> row exists for <paramref name="employeeId"/>. Every caller
/// resolves the subject before asking about dates, so a missing row here is a bug (or a
/// data-integrity fault), not a domain state — deliberately unlike
/// <see cref="IEmploymentProfileResolver"/>'s null-on-no-covering-row contract (ADR-040
/// D10 keeps that contract unchanged; callers ask "employed?" FIRST, then resolve a
/// profile only for in-window dates).
/// </para>
///
/// <para>
/// <b>Self-managed only — the in-transaction sibling lives in Infrastructure.</b> This
/// interface deliberately mirrors <see cref="IEmploymentProfileResolver"/>: no
/// <c>(NpgsqlConnection, NpgsqlTransaction)</c> overload here, because those parameter
/// types would force an Npgsql package reference onto <c>StatsTid.SharedKernel</c>, which
/// transitively reaches <c>StatsTid.RuleEngine.Api</c> and regresses the post-S19
/// <c>b4fc670</c> assembly-graph cleanup that keeps the rule engine Npgsql-free — the
/// same split-interface rationale as <c>IOutboxEnqueue</c> (ADR-018 D3). Callers that
/// must read INSIDE an advisory-locked transaction (S136's strand/re-hire guards) take
/// <c>StatsTid.Infrastructure.IEmploymentWindowResolverInTx</c> instead; the single
/// sealed <c>EmploymentWindowResolver</c> implements both.
/// </para>
/// </summary>
public interface IEmploymentWindowResolver
{
    /// <summary>
    /// Returns the employment-window fact for the given employee on the given date,
    /// opening (and disposing) its own connection. Use for reads that do not need to
    /// see uncommitted state — display paths, validation OUTSIDE an advisory-locked
    /// write transaction (in-lock reads take the Infrastructure in-tx sibling; see
    /// the class doc).
    /// </summary>
    Task<EmploymentWindowStatus> GetStatusAsync(
        string employeeId, DateOnly date, CancellationToken ct = default);

    /// <summary>
    /// S137 / ADR-040 D5 — the RANGE-scoped window read: every employment spell of
    /// <paramref name="employeeId"/> that overlaps <c>[from, to]</c> (both INCLUSIVE),
    /// as raw <see cref="EmploymentWindow"/> date pairs. Exists because segmentation
    /// consumers (the payroll planner) need TRANSITION DATES to place boundaries, not a
    /// per-date EMPLOYED/NOT_EMPLOYED verdict — a per-date loop over
    /// <see cref="GetStatusAsync(string, DateOnly, CancellationToken)"/> would be O(days)
    /// round-trips and still not name the edges.
    ///
    /// <para>
    /// <b>Deliberately LIST-shaped (spells-proof):</b> today this returns 0-or-1 entries
    /// — the single <c>users</c>-row window, when it overlaps <c>[from, to]</c> (a
    /// both-NULL window is unbounded per D2 and so always counts as ONE unbounded
    /// entry). When the deferred spells increment (re-hire, ADR-040 D1 tail) lands, it
    /// returns the genuine list — so "spells = storage + resolver only" holds for this
    /// method too: no consumer signature changes. An EMPTY list means "the window is
    /// known and no employed day falls in <c>[from, to]</c>" — callers must treat it as
    /// fully NOT_EMPLOYED, never as "no information".
    /// </para>
    ///
    /// <para>
    /// Same contract as <see cref="GetStatusAsync(string, DateOnly, CancellationToken)"/>
    /// otherwise: self-managed connection, NO <c>is_active</c> filtering (the window is a
    /// date fact — D3), and fail-loud <see cref="InvalidOperationException"/> when no
    /// <c>users</c> row exists for the employee.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<EmploymentWindow>> GetWindowsAsync(
        string employeeId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
