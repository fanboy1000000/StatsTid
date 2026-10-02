namespace StatsTid.SharedKernel.Models;

/// <summary>
/// S145 / TASK-14505 — response of the Rule Engine's <c>POST /api/rules/evaluate-timeline</c>.
/// Shared because payroll deserializes it (the <see cref="FlexEvaluationResponse"/> precedent).
///
/// <para>
/// <see cref="Results"/> carries the three per-plan results in a fixed order:
/// <c>OVERTIME_CALC</c> (overtime / merarbejde lines, each dated on its week's settlement day),
/// <c>NORM_CHECK_37H</c> (norm totals and one <c>NORMAL_HOURS</c> line per in-period entry), and
/// <c>FLEX_BALANCE</c> (the <c>FLEX_PAYOUT</c> line, or none). <see cref="Flex"/> carries the
/// flex balances. <see cref="ConfigFingerprints"/> holds the fingerprint of the agreement
/// configs the engine resolved for this call (one entry per call) and
/// <see cref="EvaluationModel"/> names the arithmetic version (<c>weekly-timeline-v1</c>):
/// payroll records both so a replay can refuse a changed config or a changed engine by name.
/// </para>
/// </summary>
public sealed class TimelineEvaluationResponse
{
    public required List<CalculationResult> Results { get; init; }
    public required FlexEvaluationResponse Flex { get; init; }
    public required List<string> ConfigFingerprints { get; init; }
    public required string EvaluationModel { get; init; }
}
