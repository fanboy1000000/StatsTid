using System.Text.Json.Serialization;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S144 / TASK-14403 — the HTTP problem body the payroll host returns (as a 422) when a month cannot be
/// calculated or exported automatically. Since S145 it has three variants, all with
/// <c>kind = payroll-recalc-blocked</c>:
/// <list type="bullet">
/// <item><b>The planner's split refusal</b> (S144, unchanged): a whole-period rule would have to be
/// evaluated in two or more pieces (the ADR-016 D4 split refusal). It names the rule (<c>ruleId</c>) and
/// the EMPLOYED segment count, and carries no <c>reason</c>. Since S145 Stage B payroll hands the planner
/// only the classifications of the rules it evaluates, none of which refuses a split, so this variant
/// answers only for a rule set that still holds a Reject or AlignedWindow rule (a test fixture set,
/// today).</item>
/// <item><b>The week check's refusal</b> (S145 / TASK-14512, Q5 (c), every payroll route): an agreement
/// change inside a Monday–Sunday week the month settles, on a day other than its Monday
/// (<see cref="PeriodNotCalculableException.AgreementChangeMidWeek"/>). It carries
/// <c>reason = agreement-change-mid-week</c> and no <c>ruleId</c> or segment count: it is not a rule's
/// refusal.</item>
/// <item><b>The single-profile route's refusal</b> (S145 Stage B / TASK-14522, Q6 (a), ruling R-76; the raw
/// <c>/api/payroll/export</c> and <c>/api/payroll/export-period</c> routes only): the month has two or
/// more EMPLOYED parts, and the route maps every line under one profile
/// (<see cref="PeriodNotCalculableException.SplitMonthSingleProfileRoute"/>). It carries
/// <c>reason = split-month-single-profile-route</c>, the EMPLOYED segment count and the interior boundary
/// causes, and no <c>ruleId</c>: it is not a rule's refusal either.</item>
/// </list>
/// Consumers tell the variants apart by <c>reason</c> (absent from the planner's variant), never by which
/// other members are present.
///
/// <para>
/// <b>Why it exists (plain language).</b> A month can contain a change that splits it into
/// segments — since S144 a mid-month change of the employee's agreement code, since S137 a
/// mid-month profile change. Before S144 a refused month escaped the endpoints as a bare 500: the
/// operator learned nothing and could not tell it from a crash. Since S144 the payroll routes turn the
/// refusal into a 422 saying, in effect, "this month cannot be handled here, and this is why". Since
/// S145 Stage B the calculating routes (<c>/api/payroll/recalculate</c> and
/// <c>/api/payroll/calculate-and-export</c>) pay a split month part by part, so two refusals remain in
/// production: an agreement change inside a week (a week cannot be judged under two agreements; the
/// month is handled manually, on every payroll route), and a split month sent to a raw export route
/// (which must go through a calculating route instead).
/// </para>
///
/// <para>
/// <b>What it deliberately leaves out.</b> The exception messages are free text for
/// diagnosis (logs, tests); they name the employee id and dates. A response body is a
/// client contract, and a stable one needs a fixed shape rather than forwarded free text, so this
/// body is built ONLY from the exception's structured members plus a fixed sentence — never from
/// <see cref="Exception.Message"/>. (The period and employee id are the caller's own input, but
/// the message is not a contract; ADR-040 D7 separately keeps employment and change dates out of
/// client bodies.) The shape is QUAL-149's register sub-item: counts and causes, no dates.
/// </para>
///
/// <para>
/// <b>Bugs stay bugs.</b> Only a genuine split refusal (<see cref="PlannerInvariantViolation.IsSplitRefusal"/>)
/// or a known reason carrying the members its variant needs maps to a problem. Every other planner
/// violation (a gap, an overlap, a missing snapshot, a merger contract breach) is a defect in the system,
/// not a month that needs manual handling; both <c>TryCreate</c> overloads return <c>null</c> for it and
/// the handlers let it propagate as a 500.
/// </para>
///
/// <para>
/// Wire shapes (web-default JSON, camelCase; absent members are omitted, not written as null). The
/// planner variant, byte for byte as in S144:
/// <c>{ "success": false, "error": "&lt;fixed sentence&gt;", "kind": "payroll-recalc-blocked",
/// "employedSegmentCount": 2, "interiorBoundaryCauses": ["AgreementCodeChange"], "ruleId": "OVERTIME_CALC" }</c>.
/// The week variant:
/// <c>{ "success": false, "error": "&lt;fixed sentence&gt;", "kind": "payroll-recalc-blocked",
/// "reason": "agreement-change-mid-week", "interiorBoundaryCauses": ["AgreementCodeChange"] }</c>.
/// The single-profile route variant:
/// <c>{ "success": false, "error": "&lt;fixed sentence&gt;", "kind": "payroll-recalc-blocked",
/// "reason": "split-month-single-profile-route", "employedSegmentCount": 2,
/// "interiorBoundaryCauses": ["EmployeeProfileChange"] }</c>.
/// </para>
/// </summary>
public sealed class PayrollPlanRefusalProblem
{
    /// <summary>The discriminator a client branches on (sibling of <c>payroll-not-exported</c>).</summary>
    public const string KindValue = "payroll-recalc-blocked";

    /// <summary>
    /// The planner variant's fixed, plain-language sentence. It contains no date, no id and no digit, so
    /// it can never leak one; the specifics travel in the structured fields.
    /// </summary>
    public const string ErrorSentence =
        "This period cannot be calculated automatically: a change inside the period splits it into " +
        "segments, and a whole-period rule cannot be evaluated in separate segments. The period " +
        "must be handled manually.";

    /// <summary>
    /// The week variant's fixed, plain-language sentence (S145, Q5 (c)). Like <see cref="ErrorSentence"/>
    /// it contains no date, no id and no digit.
    /// </summary>
    public const string MidWeekAgreementChangeSentence =
        "This period cannot be calculated automatically: the employee's agreement changes in the middle " +
        "of a week this period settles, and a week cannot be judged under two agreements. The period " +
        "must be handled manually.";

    /// <summary>
    /// The single-profile route variant's fixed, plain-language sentence (S145 Stage B, Q6 (a), ruling R-76,
    /// verbatim). Like <see cref="ErrorSentence"/> it contains no date, no id and no digit.
    /// </summary>
    public const string SplitMonthSingleProfileRouteSentence =
        "This period cannot be exported through this route: a change inside the period splits it into " +
        "parts with different profiles, and this route maps every line under a single profile. Use the " +
        "calculating routes (recalculate, or calculate-and-export) to pay the period.";

    private PayrollPlanRefusalProblem(
        string error,
        string? reason,
        int? employedSegmentCount,
        IReadOnlyList<string> interiorBoundaryCauses,
        string? ruleId)
    {
        Error = error;
        Reason = reason;
        EmployedSegmentCount = employedSegmentCount;
        InteriorBoundaryCauses = interiorBoundaryCauses;
        RuleId = ruleId;
    }

    /// <summary>Always <c>false</c> — matches the host's other failure bodies.</summary>
    public bool Success => false;

    /// <summary>The variant's fixed sentence — never an exception message.</summary>
    public string Error { get; }

    /// <summary>Always <see cref="KindValue"/>.</summary>
    public string Kind => KindValue;

    /// <summary>
    /// The cause of a <see cref="PeriodNotCalculableException"/> variant
    /// (<see cref="PeriodNotCalculableException.AgreementChangeMidWeek"/> or
    /// <see cref="PeriodNotCalculableException.SplitMonthSingleProfileRoute"/>). Omitted from the planner
    /// variant, whose S144 body it must not change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; }

    /// <summary>
    /// How many EMPLOYED segments the period has (the planner variant and the single-profile route variant;
    /// omitted from the week variant).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? EmployedSegmentCount { get; }

    /// <summary>The distinct interior boundary cause NAMES, in date order (e.g. <c>AgreementCodeChange</c>).</summary>
    public IReadOnlyList<string> InteriorBoundaryCauses { get; }

    /// <summary>The whole-period rule that refused the split (planner variant only).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleId { get; }

    /// <summary>
    /// Maps a planner violation to the problem body, or <c>null</c> when the violation is not a
    /// split refusal (a bug — let it propagate as a 500). Reads only the structured members.
    /// </summary>
    public static PayrollPlanRefusalProblem? TryCreate(PlannerInvariantViolation ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (!ex.IsSplitRefusal || ex.EmployedSegmentCount is not { } segmentCount || ex.SplitRefusalRuleId is null)
            return null;

        return new PayrollPlanRefusalProblem(
            ErrorSentence,
            reason: null,
            segmentCount,
            ex.InteriorBoundaryCauses.Select(cause => cause.ToString()).ToArray(),
            ex.SplitRefusalRuleId);
    }

    /// <summary>
    /// Maps a <see cref="PeriodNotCalculableException"/> to its 422 variant, or <c>null</c> when it cannot
    /// be mapped (a bug — let it propagate as a 500, like a non-split planner violation):
    /// <list type="bullet">
    /// <item><see cref="PeriodNotCalculableException.AgreementChangeMidWeek"/> → the week variant (S145 /
    /// TASK-14512), unchanged: the reason and the fixed cause <c>AgreementCodeChange</c>, no segment
    /// count;</item>
    /// <item><see cref="PeriodNotCalculableException.SplitMonthSingleProfileRoute"/> → the single-profile
    /// route variant (S145 Stage B / TASK-14522, R-76): the reason, the EMPLOYED segment count and the
    /// interior boundary cause names in date order. Without a segment count it maps to <c>null</c>: the raw
    /// routes' guard always sets one, so a missing count is a bug;</item>
    /// <item>any other reason → <c>null</c>.</item>
    /// </list>
    /// Reads only <see cref="PeriodNotCalculableException.Reason"/> and the structured members — never
    /// <see cref="Exception.Message"/> — so no date and no employee id can reach the body.
    /// </summary>
    public static PayrollPlanRefusalProblem? TryCreate(PeriodNotCalculableException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (string.Equals(ex.Reason, PeriodNotCalculableException.AgreementChangeMidWeek, StringComparison.Ordinal))
        {
            return new PayrollPlanRefusalProblem(
                MidWeekAgreementChangeSentence,
                ex.Reason,
                employedSegmentCount: null,
                new[] { BoundaryCause.AgreementCodeChange.ToString() },
                ruleId: null);
        }

        if (string.Equals(ex.Reason, PeriodNotCalculableException.SplitMonthSingleProfileRoute, StringComparison.Ordinal)
            && ex.EmployedSegmentCount is { } segmentCount)
        {
            return new PayrollPlanRefusalProblem(
                SplitMonthSingleProfileRouteSentence,
                ex.Reason,
                segmentCount,
                ex.InteriorBoundaryCauses.Select(cause => cause.ToString()).ToArray(),
                ruleId: null);
        }

        return null;
    }
}
