using System.Text.Json;
using System.Text.RegularExpressions;
using StatsTid.Integrations.Payroll.Services;
using StatsTid.SharedKernel.Segmentation;

namespace StatsTid.Tests.Unit.Payroll;

/// <summary>
/// S144 / TASK-14405 (TASK-14403's definition of done) — the pure mapping from the planner's
/// structured D4 split refusal to the HTTP problem body the payroll host returns as a 422.
///
/// <para>
/// <b>Why it matters (plain language).</b> Before S144 a month the planner refused (a mid-month
/// profile change) surfaced as a bare 500 — the operator learned nothing. The new body says "this
/// month would be evaluated in 2 segments; causes: AgreementCodeChange" so HR knows to handle it
/// manually. But the planner's own message is free text for diagnosis: it names the EMPLOYEE ID and
/// the PERIOD DATES. A response body must never carry those (ADR-040 D7: employment dates stay out
/// of anything that reaches a client), so the mapping builds the body from the structured members
/// only and a fixed sentence — and the redaction is pinned separately from the mapping so a leak
/// cannot hide behind a correct mapping.
/// </para>
///
/// <para>
/// The exception is produced by the REAL planner (a windowless Reject rule + a mid-March
/// agreement-code date), not hand-built, so these pins do not depend on the exception's
/// constructor shape — only on its observable members.
/// </para>
/// </summary>
public sealed class PayrollPlanRefusalProblemTests
{
    private const string EmployeeId = "EMP-REDACT-88213";

    private static readonly DateOnly Mar01 = new(2026, 3, 1);
    private static readonly DateOnly Mar31 = new(2026, 3, 31);

    private static RuleClassification RejectCalc(string ruleId) => new(
        ruleId, Span.Window, SplitBehavior.Reject, Family.Calculation,
        MergeStrategy.RejectIfMultipleSegments, SnapshotContract: null);

    private static BoundarySources AgreementCodeOn(DateOnly date) => new(
        OkTransitions: Array.Empty<(DateOnly, string, string)>(),
        AgreementConfigPromotions: Array.Empty<(DateOnly, string)>(),
        PositionOverrideEffectiveDates: Array.Empty<(DateOnly, string)>(),
        EuWtdRulesetTransitions: Array.Empty<(DateOnly, int, int)>(),
        NonDatedSourceValues: new Dictionary<string, object?>(),
        AgreementCodeEffectiveDates: new[] { date });

    private static PlannerInvariantViolation SplitRefusal() =>
        Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(EmployeeId, Mar01, Mar31, "forward-calc",
                new[] { RejectCalc("OVERTIME_CALC") }, AgreementCodeOn(new DateOnly(2026, 3, 16)),
                PlannerOptions.Default));

    private static PlannerInvariantViolation GeometricViolation() =>
        Assert.Throws<PlannerInvariantViolation>(() =>
            PeriodPlanner.Plan(EmployeeId, Mar31, Mar01, "forward-calc",
                new[] { RejectCalc("OVERTIME_CALC") }, AgreementCodeOn(new DateOnly(2026, 3, 16)),
                PlannerOptions.Default));

    private static string ToWireJson(object problem) =>
        JsonSerializer.Serialize(problem, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>
    /// A split refusal maps to a problem carrying the discriminator, the segment count, the cause
    /// NAMES and the rule id — exactly the structured members, nothing more.
    ///
    /// Red conditions: mutation M-12 — <c>PayrollPlanRefusalProblem.TryCreate</c> returns
    /// <c>null</c> for a split refusal. <c>Assert.NotNull(problem)</c> trips (the host would then
    /// fall through to a bare 500 again).
    /// </summary>
    [Fact]
    public void TryCreate_SplitRefusal_MapsCountCausesAndRuleId()
    {
        var ex = SplitRefusal();
        Assert.True(ex.IsSplitRefusal); // precondition: TASK-14402's members are populated

        var problem = PayrollPlanRefusalProblem.TryCreate(ex);

        Assert.NotNull(problem);
        using var doc = JsonDocument.Parse(ToWireJson(problem!));
        var root = doc.RootElement;
        Assert.Equal("payroll-recalc-blocked", root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(2, root.GetProperty("employedSegmentCount").GetInt32());
        Assert.Equal(new[] { "AgreementCodeChange" },
            root.GetProperty("interiorBoundaryCauses").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal("OVERTIME_CALC", root.GetProperty("ruleId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));
    }

    /// <summary>
    /// A NON-split violation (a geometric bug) maps to <c>null</c>: the handlers then let it
    /// propagate as a 500 — a bug must not be presented as "this month needs manual handling".
    ///
    /// Red conditions: no named mutation — the sibling of M-12 in the other direction. Goes red if
    /// <c>TryCreate</c> returns a problem for every violation, tripping <c>Assert.Null(problem)</c>.
    /// </summary>
    [Fact]
    public void TryCreate_NonSplitViolation_ReturnsNull()
    {
        var ex = GeometricViolation();
        Assert.False(ex.IsSplitRefusal); // precondition

        Assert.Null(PayrollPlanRefusalProblem.TryCreate(ex));
    }

    /// <summary>
    /// The problem, SERIALIZED, contains no calendar date (<c>\d{4}-\d{2}-\d{2}</c>) and not the
    /// employee id that the exception's own message names. The precondition asserts the message
    /// really does carry both — otherwise the redaction assertions could not fail.
    ///
    /// Red conditions: mutation M-13 — <c>TryCreate</c> sets <c>error = ex.Message</c>. The JSON
    /// then contains the period dates and <c>EMP-REDACT-88213</c>, tripping
    /// <c>Assert.DoesNotMatch(date regex, json)</c> (and the employee-id assertion).
    /// </summary>
    [Fact]
    public void TryCreate_SplitRefusal_SerializedProblem_ContainsNoDateAndNoEmployeeId()
    {
        var ex = SplitRefusal();
        Assert.Contains(EmployeeId, ex.Message);
        Assert.Matches(@"\d{4}-\d{2}-\d{2}", ex.Message);

        var problem = PayrollPlanRefusalProblem.TryCreate(ex);
        Assert.NotNull(problem);
        var json = ToWireJson(problem!);

        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", json);
        Assert.DoesNotContain(EmployeeId, json);
        Assert.DoesNotContain("88213", json);
    }
}
