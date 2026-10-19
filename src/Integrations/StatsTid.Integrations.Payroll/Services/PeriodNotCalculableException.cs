using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S145 / TASK-14512 (ruling R40; Q5 (c)) — a month whose own data cannot be calculated or exported,
/// found by a check that runs after planning. It has two causes, told apart by <see cref="Reason"/>:
/// <list type="bullet">
/// <item><see cref="AgreementChangeMidWeek"/> (Q5 (c), every payroll route): an agreement change dated
/// inside a Monday–Sunday week the month settles, on a day other than its Monday.</item>
/// <item><see cref="SplitMonthSingleProfileRoute"/> (Q6 (a), S145 Stage B / TASK-14522, ruling R-76; the raw
/// export routes only): the month has two or more EMPLOYED parts, and the route maps every line under one
/// profile.</item>
/// </list>
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
/// <b>Why the single-profile cause refuses (plain language).</b> Since S145 Stage B the calculating routes
/// pay a month with a mid-month change in hours, position or agreement part by part, each part under its
/// own wage-type codes. The raw <c>/api/payroll/export</c> and <c>/api/payroll/export-period</c> routes
/// cannot: they take lines the caller already calculated and map every one of them under the request's
/// single profile, so the lines of the second part would be exported under the first part's codes. They
/// refuse such a month by this separate name and point the caller to the calculating routes. Only this
/// cause carries <see cref="EmployedSegmentCount"/> and <see cref="InteriorBoundaryCauses"/>.
/// </para>
///
/// <para>
/// <b>What leaves the service.</b> The host maps this exception to a 422 variant through
/// <see cref="PayrollPlanRefusalProblem.TryCreate(PeriodNotCalculableException)"/>, built from
/// <see cref="Reason"/>, the structured members and a fixed sentence only. <see cref="Exception.Message"/>
/// is free text for diagnosis (it may name the employee, the period and the change date) and never
/// reaches a response. Nothing has been calculated or written when it is thrown.
/// </para>
/// </summary>
public sealed class PeriodNotCalculableException : Exception
{
    /// <summary>The <see cref="Reason"/> for an agreement change inside a settled week (Q5 (c)).</summary>
    public const string AgreementChangeMidWeek = "agreement-change-mid-week";

    /// <summary>
    /// The <see cref="Reason"/> for a month with two or more EMPLOYED parts met on a raw export route, which
    /// maps every line under a single profile (Q6 (a), S145 Stage B, ruling R-76).
    /// </summary>
    public const string SplitMonthSingleProfileRoute = "split-month-single-profile-route";

    /// <summary>
    /// Creates the refusal. <paramref name="employedSegmentCount"/> and
    /// <paramref name="interiorBoundaryCauses"/> are given for <see cref="SplitMonthSingleProfileRoute"/>
    /// only; for <see cref="AgreementChangeMidWeek"/> they are left out (null and empty), and its body is
    /// unchanged since S145 / TASK-14512.
    /// </summary>
    /// <param name="reason">The machine-readable cause (one of the reason constants).</param>
    /// <param name="message">Free-text diagnostic; never reaches a response.</param>
    /// <param name="employedSegmentCount">How many EMPLOYED parts the period has (the single-profile cause).</param>
    /// <param name="interiorBoundaryCauses">The plan's distinct interior boundary causes, in date order
    /// (the single-profile cause); never their dates.</param>
    public PeriodNotCalculableException(
        string reason,
        string message,
        int? employedSegmentCount = null,
        IReadOnlyList<BoundaryCause>? interiorBoundaryCauses = null)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Reason = reason;
        EmployedSegmentCount = employedSegmentCount;
        InteriorBoundaryCauses = interiorBoundaryCauses is null
            ? Array.Empty<BoundaryCause>()
            : interiorBoundaryCauses.ToArray();
    }

    /// <summary>
    /// The machine-readable cause, carried verbatim into the 422 body's <c>reason</c> member (for
    /// example <see cref="AgreementChangeMidWeek"/>). Clients tell the 422 variants apart by it.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// How many EMPLOYED parts the period has (S145 Stage B, R-76). Set for
    /// <see cref="SplitMonthSingleProfileRoute"/>; <c>null</c> for <see cref="AgreementChangeMidWeek"/>.
    /// </summary>
    public int? EmployedSegmentCount { get; }

    /// <summary>
    /// The plan's distinct interior boundary causes, in date order, as the planner reports them (S145
    /// Stage B, R-76): causes only, never a date (ADR-040 D7). Set for
    /// <see cref="SplitMonthSingleProfileRoute"/>; empty for <see cref="AgreementChangeMidWeek"/>.
    /// </summary>
    public IReadOnlyList<BoundaryCause> InteriorBoundaryCauses { get; }
}
