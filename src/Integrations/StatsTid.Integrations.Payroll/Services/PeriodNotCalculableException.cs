namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S145 / TASK-14512 (ruling R40; Q5 (c)) — a month whose own data cannot be calculated or exported,
/// found by a check that runs after planning. Its one cause, carried in <see cref="Reason"/>:
/// <see cref="AgreementChangeMidWeek"/> (Q5 (c), both calculating routes): an agreement change dated
/// inside a Monday–Sunday week the month settles, on a day other than its Monday. (S145 Stage B added a
/// second cause for the raw export routes, a month with two or more EMPLOYED parts; it was deleted with
/// those routes in S147 TASK-14716, together with the two members only it carried.)
///
/// <para>
/// <b>Why the week cause refuses (plain language).</b> Payroll judges overtime one whole week at a time. HK
/// pays tiered overtime, AC pays merarbejde; a week worked partly under each has no rule that says how to
/// judge it, so the month is refused rather than paid under a guess. The planner cannot see these cases
/// itself: a change ON the 1st is not inside the period, and a change in the previous month's last days
/// is before it, yet both fall inside the month's first week. A hire is not a change: the caller drops
/// an employee's first, hire-dated agreement row before asking (R43).
/// </para>
///
/// <para>
/// <b>What leaves the service.</b> The host maps this exception to a 422 variant through
/// <see cref="PayrollPlanRefusalProblem.TryCreate(PeriodNotCalculableException)"/>, built from
/// <see cref="Reason"/> and a fixed sentence only. <see cref="Exception.Message"/>
/// is free text for diagnosis (it may name the employee, the period and the change date) and never
/// reaches a response. Nothing has been calculated or written when it is thrown.
/// </para>
/// </summary>
public sealed class PeriodNotCalculableException : Exception
{
    /// <summary>The <see cref="Reason"/> for an agreement change inside a settled week (Q5 (c)).</summary>
    public const string AgreementChangeMidWeek = "agreement-change-mid-week";

    /// <summary>Creates the refusal; its body is unchanged since S145 / TASK-14512.</summary>
    /// <param name="reason">The machine-readable cause (<see cref="AgreementChangeMidWeek"/>).</param>
    /// <param name="message">Free-text diagnostic; never reaches a response.</param>
    public PeriodNotCalculableException(string reason, string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
    }

    /// <summary>
    /// The machine-readable cause, carried verbatim into the 422 body's <c>reason</c> member (for
    /// example <see cref="AgreementChangeMidWeek"/>). Clients tell the 422 variants apart by it.
    /// </summary>
    public string Reason { get; }
}
