using System.Text.Json.Serialization;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Integrations.Payroll.Services;

/// <summary>
/// S144 / TASK-14403 — the HTTP problem body the payroll host returns (as a 422) when a month cannot be
/// calculated automatically. Since S145 / TASK-14512 it has two variants, both with
/// <c>kind = payroll-recalc-blocked</c>:
/// <list type="bullet">
/// <item><b>The planner's split refusal</b> (S144, unchanged): a whole-period rule would have to be
/// evaluated in two or more pieces (the ADR-016 D4 split refusal) — e.g. a mid-month agreement-code
/// change. It names the rule (<c>ruleId</c>) and the EMPLOYED segment count, and carries no
/// <c>reason</c>.</item>
/// <item><b>The week check's refusal</b> (S145, Q5 (c)): an agreement change inside a Monday–Sunday week
/// the month settles, on a day other than its Monday (<see cref="PeriodNotCalculableException"/>). It
/// carries <c>reason = agreement-change-mid-week</c> and no <c>ruleId</c> or segment count: it is not a
/// rule's refusal.</item>
/// </list>
/// Consumers tell the variants apart by <c>reason</c>, never by which other members are present.
///
/// <para>
/// <b>Why it exists (plain language).</b> A month can contain a change that splits it into
/// segments — since S144 a mid-month change of the employee's agreement code, since S137 a
/// mid-month profile change. Some rules (overtime, norm) can only be evaluated over the whole
/// period, so the planner refuses rather than pay the month wrong. Before S144 that refusal
/// escaped the endpoints as a bare 500: the operator learned nothing and could not tell it from a
/// crash. Now <c>/api/payroll/recalculate</c> and <c>/api/payroll/calculate-and-export</c> turn it
/// into a 422 saying, in effect, "this month would be evaluated in 2 segments; causes:
/// AgreementCodeChange — handle it manually". The week check's refusal reaches every payroll route
/// the same way, because a week cannot be judged under two agreements.
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
/// or a known week-check reason maps to a problem. Every other planner violation (a gap, an overlap, a
/// missing snapshot) is a defect in the system, not a month that needs manual handling;
/// <see cref="TryCreate(PlannerInvariantViolation)"/> returns <c>null</c> for it and the handlers let it
/// propagate as a 500.
/// </para>
///
/// <para>
/// Wire shapes (web-default JSON, camelCase). The planner variant, byte for byte as in S144 (the
/// absent members are omitted, not written as null):
/// <c>{ "success": false, "error": "&lt;fixed sentence&gt;", "kind": "payroll-recalc-blocked",
/// "employedSegmentCount": 2, "interiorBoundaryCauses": ["AgreementCodeChange"], "ruleId": "OVERTIME_CALC" }</c>.
/// The week variant:
/// <c>{ "success": false, "error": "&lt;fixed sentence&gt;", "kind": "payroll-recalc-blocked",
/// "reason": "agreement-change-mid-week", "interiorBoundaryCauses": ["AgreementCodeChange"] }</c>.
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
    /// The week variant's cause (<see cref="PeriodNotCalculableException.AgreementChangeMidWeek"/>).
    /// Omitted from the planner variant, whose S144 body it must not change.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; }

    /// <summary>How many EMPLOYED segments the period would have been evaluated in (planner variant only).</summary>
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
    /// S145 / TASK-14512 — maps a week-check refusal to the second 422 variant, or <c>null</c> for a
    /// reason this mapping does not know (a bug — let it propagate as a 500, like a non-split planner
    /// violation). Reads only <see cref="PeriodNotCalculableException.Reason"/>: no date, no employee id.
    /// </summary>
    public static PayrollPlanRefusalProblem? TryCreate(PeriodNotCalculableException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        return ex.Reason switch
        {
            PeriodNotCalculableException.AgreementChangeMidWeek => new PayrollPlanRefusalProblem(
                MidWeekAgreementChangeSentence,
                ex.Reason,
                employedSegmentCount: null,
                new[] { BoundaryCause.AgreementCodeChange.ToString() },
                ruleId: null),
            _ => null,
        };
    }
}
