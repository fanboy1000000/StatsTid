using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S144 / TASK-14403 — the HTTP problem body the payroll host returns (as a 422) when the planner
/// refuses a month because a whole-period rule would have to be evaluated in two or more pieces
/// (the ADR-016 D4 split refusal).
///
/// <para>
/// <b>Why it exists (plain language).</b> A month can contain a change that splits it into
/// segments — since S144 a mid-month change of the employee's agreement code, since S137 a
/// mid-month profile change. Some rules (overtime, norm) can only be evaluated over the whole
/// period, so the planner refuses rather than pay the month wrong. Before S144 that refusal
/// escaped the endpoints as a bare 500: the operator learned nothing and could not tell it from a
/// crash. Now <c>/api/payroll/recalculate</c> and <c>/api/payroll/calculate-and-export</c> turn it
/// into a 422 saying, in effect, "this month would be evaluated in 2 segments; causes:
/// AgreementCodeChange — handle it manually".
/// </para>
///
/// <para>
/// <b>What it deliberately leaves out.</b> The planner's exception message is free text for
/// diagnosis (logs, tests); it names the employee id and the period dates. A response body is a
/// client contract, and a stable one needs a fixed shape rather than forwarded free text, so this
/// body is built ONLY from the exception's structured members plus a fixed sentence — never from
/// <see cref="Exception.Message"/>. (The period and employee id are the caller's own input, but
/// the message is not a contract; ADR-040 D7 separately keeps employment and change dates out of
/// client bodies.) The shape is QUAL-149's register sub-item: counts and causes, no dates.
/// </para>
///
/// <para>
/// <b>Bugs stay bugs.</b> Only a genuine split refusal (<see cref="PlannerInvariantViolation.IsSplitRefusal"/>)
/// maps to a problem. Every other planner violation (a gap, an overlap, a missing snapshot) is a
/// defect in the system, not a month that needs manual handling; <see cref="TryCreate"/> returns
/// <c>null</c> for it and the handlers let it propagate as a 500.
/// </para>
///
/// <para>
/// Wire shape (web-default JSON, camelCase):
/// <c>{ "success": false, "error": "&lt;fixed sentence&gt;", "kind": "payroll-recalc-blocked",
/// "employedSegmentCount": 2, "interiorBoundaryCauses": ["AgreementCodeChange"], "ruleId": "OVERTIME_CALC" }</c>.
/// </para>
/// </summary>
public sealed class PayrollPlanRefusalProblem
{
    /// <summary>The discriminator a client branches on (sibling of <c>payroll-not-exported</c>).</summary>
    public const string KindValue = "payroll-recalc-blocked";

    /// <summary>
    /// The fixed, plain-language sentence. It contains no date, no id and no digit, so it can
    /// never leak one; the specifics travel in the structured fields.
    /// </summary>
    public const string ErrorSentence =
        "This period cannot be calculated automatically: a change inside the period splits it into " +
        "segments, and a whole-period rule cannot be evaluated in separate segments. The period " +
        "must be handled manually.";

    private PayrollPlanRefusalProblem(
        int employedSegmentCount, IReadOnlyList<string> interiorBoundaryCauses, string ruleId)
    {
        EmployedSegmentCount = employedSegmentCount;
        InteriorBoundaryCauses = interiorBoundaryCauses;
        RuleId = ruleId;
    }

    /// <summary>Always <c>false</c> — matches the host's other failure bodies.</summary>
    public bool Success => false;

    /// <summary>The fixed sentence (<see cref="ErrorSentence"/>) — never the exception message.</summary>
    public string Error => ErrorSentence;

    /// <summary>Always <see cref="KindValue"/>.</summary>
    public string Kind => KindValue;

    /// <summary>How many EMPLOYED segments the period would have been evaluated in.</summary>
    public int EmployedSegmentCount { get; }

    /// <summary>The distinct interior boundary cause NAMES, in date order (e.g. <c>AgreementCodeChange</c>).</summary>
    public IReadOnlyList<string> InteriorBoundaryCauses { get; }

    /// <summary>The whole-period rule that refused the split.</summary>
    public string RuleId { get; }

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
            segmentCount,
            ex.InteriorBoundaryCauses.Select(cause => cause.ToString()).ToArray(),
            ex.SplitRefusalRuleId);
    }
}
