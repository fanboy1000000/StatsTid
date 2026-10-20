using System.Globalization;
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
    /// The problem, SERIALIZED, contains no calendar date (<c>\d{4}-\d{2}-\d{2}</c>), neither of
    /// the refusal's own period dates (ISO form or the machine-culture <c>DateOnly.ToString()</c>
    /// form) and not the employee id that the exception's own message names. The precondition
    /// asserts the message really carries the period's start and end in ISO form (the actual
    /// period dates, not merely SOME date — the planner message also holds the literal
    /// "as ruled 2026-09-02", which alone satisfied the old regex precondition) and the employee
    /// id — otherwise the redaction assertions could not fail. The ISO precondition depends on
    /// TASK-14408 (culture-invariant period rendering) and is red until it merges.
    ///
    /// Red conditions: mutation M-13 — <c>TryCreate</c> sets <c>error = ex.Message</c>. The JSON
    /// then contains the period dates and <c>EMP-REDACT-88213</c>, tripping
    /// <c>Assert.DoesNotMatch(date regex, json)</c> (and the employee-id assertion).
    /// </summary>
    [Fact]
    public void TryCreate_SplitRefusal_SerializedProblem_ContainsNoDateAndNoEmployeeId()
    {
        var ex = SplitRefusal();
        var isoStart = Mar01.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var isoEnd = Mar31.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Contains(EmployeeId, ex.Message);
        Assert.Contains(isoStart, ex.Message);
        Assert.Contains(isoEnd, ex.Message);

        var problem = PayrollPlanRefusalProblem.TryCreate(ex);
        Assert.NotNull(problem);
        var json = ToWireJson(problem!);

        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", json);
        Assert.DoesNotContain(isoStart, json);
        Assert.DoesNotContain(isoEnd, json);
        Assert.DoesNotContain(Mar01.ToString(), json);
        Assert.DoesNotContain(Mar31.ToString(), json);
        Assert.DoesNotContain(EmployeeId, json);
        Assert.DoesNotContain("88213", json);
    }

    // ═════════════════════════════════════════════════════════════════════
    // S145 / TASK-14514 — the second 422 variant: an agreement change inside a settled week (Q5 (c))
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A week-check refusal (TASK-14512's <c>PeriodNotCalculableException</c>, reason
    /// <c>agreement-change-mid-week</c>) maps to the SAME <c>payroll-recalc-blocked</c> kind with the discriminating
    /// <c>reason</c>, the cause <c>AgreementCodeChange</c>, a fixed error sentence, and NO <c>ruleId</c> (it is not
    /// a rule's refusal). Consumers tell the two 422 variants apart by <c>reason</c> (refinement A5 :321-322).
    ///
    /// <para>
    /// <b>Why it must map (plain language).</b> <c>TryCreate</c> returned null for anything without a rule id,
    /// which surfaces as a bare 500: an operator would learn nothing. The new variant is what an agreement change on
    /// a Tuesday inside a settled week (HK to AC on 1 September) answers on every payroll route.
    /// </para>
    ///
    /// Red conditions (names a type TASK-14512 creates, so RED by MUTATION at the A.4 gate): MU-26 (the mapping
    /// omits <c>reason</c>) trips the <c>reason</c> assertion; a mapping that returns null for the week variant (the
    /// shape of master's <c>TryCreate</c>) trips <c>Assert.NotNull</c>.
    /// </summary>
    [Fact]
    public void WeekVariant_HasReasonAgreementChangeMidWeek_NoRuleId()
    {
        var ex = WeekRefusal();

        var problem = PayrollPlanRefusalProblem.TryCreate(ex);

        Assert.NotNull(problem);
        using var doc = JsonDocument.Parse(ToWireJson(problem!));
        var root = doc.RootElement;
        Assert.Equal("payroll-recalc-blocked", root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("agreement-change-mid-week", root.GetProperty("reason").GetString());
        Assert.Equal(new[] { "AgreementCodeChange" },
            root.GetProperty("interiorBoundaryCauses").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));
        // No ruleId: absent, or an explicit null (the host test's own formulation).
        Assert.True(
            !root.TryGetProperty("ruleId", out var ruleId) || ruleId.ValueKind == JsonValueKind.Null,
            "the week check's variant carries no ruleId (it is not a rule's refusal)");
    }

    /// <summary>
    /// The week variant, SERIALIZED, contains no calendar date, not the change date the exception's own message
    /// names (ISO or machine-culture form), and not the employee id. The preconditions assert the exception's
    /// message really carries the change date and the employee id, so the redaction assertions CAN fail.
    ///
    /// Red conditions (names a type TASK-14512 creates, so RED by MUTATION at the A.4 gate): MU-27 (the change
    /// date is included in <c>error</c>, for example <c>error = ex.Message</c>) puts the date and
    /// <c>EMP-REDACT-88213</c> into the JSON and trips the date-pattern and employee-id assertions.
    /// </summary>
    [Fact]
    public void WeekVariant_SerializedJson_HasNoDateNoEmployeeId()
    {
        var ex = WeekRefusal();
        var isoChange = ChangeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Contains(EmployeeId, ex.Message);
        Assert.Contains(isoChange, ex.Message);

        var problem = PayrollPlanRefusalProblem.TryCreate(ex);
        Assert.NotNull(problem);
        var json = ToWireJson(problem!);

        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", json);
        Assert.DoesNotContain(isoChange, json);
        Assert.DoesNotContain(ChangeDate.ToString(), json);
        Assert.DoesNotContain(EmployeeId, json);
        Assert.DoesNotContain("88213", json);
    }

    private static readonly DateOnly ChangeDate = new(2026, 9, 1);

    /// <summary>
    /// A week-check refusal for a diagnostic message that names the employee and the change date (as the planner's
    /// own refusal message names the employee and the period), so the redaction pin has something to redact.
    /// </summary>
    private static PeriodNotCalculableException WeekRefusal() =>
        new(
            reason: "agreement-change-mid-week",
            message: "Period for employee '" + EmployeeId + "' has an agreement change on " +
                     ChangeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " inside a settled week.");

    // ═════════════════════════════════════════════════════════════════════
    // S145 Stage B / TASK-14524 (TASK-14522 contract 3, R-76) — the third 422 variant:
    // the raw export routes refuse a month with two or more EMPLOYED segments (Q6 (a))
    // ═════════════════════════════════════════════════════════════════════

    private const string SingleProfileReason = "split-month-single-profile-route";

    /// <summary>The fixed sentence of the Q6 (a) body, verbatim from TASK-14522 contract 3.</summary>
    private const string SingleProfileSentence =
        "This period cannot be exported through this route: a change inside the period splits it into parts with " +
        "different profiles, and this route maps every line under a single profile. Use the calculating routes " +
        "(recalculate, or calculate-and-export) to pay the period.";

    /// <summary>
    /// A Q6 (a) refusal as the raw-route guard throws it: reason <c>split-month-single-profile-route</c>,
    /// the EMPLOYED segment count and the interior boundary causes in date order. The diagnostic message
    /// names the employee and a date so the redaction pin has something to redact. (Constructor shape:
    /// optional named arguments after <c>message</c>, per R-76's "two optional members"; reconciled at B.3.)
    /// </summary>
    private static PeriodNotCalculableException SingleProfileRefusal() =>
        new(
            reason: SingleProfileReason,
            message: "Period for employee '" + EmployeeId + "' has 2 EMPLOYED segments from " +
                     ChangeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".",
            employedSegmentCount: 2,
            interiorBoundaryCauses: new[] { BoundaryCause.EmployeeProfileChange, BoundaryCause.AgreementCodeChange });

    /// <summary>
    /// The third 422 variant: same <c>payroll-recalc-blocked</c> kind, <c>reason</c>
    /// <c>split-month-single-profile-route</c>, the segment count, the cause names in date order, the fixed
    /// sentence, and NO <c>ruleId</c> (it is not a rule's refusal).
    ///
    /// Red conditions (names members TASK-14522 creates, so RED by MUTATION at the B.3 gate): MB-11 (the
    /// body drops <c>reason</c>, or leaks a date) trips the <c>reason</c> assertion; a mapping that returns
    /// null for the new reason (master's behaviour for an unknown reason) trips <c>Assert.NotNull</c>.
    /// </summary>
    [Fact]
    public void SingleProfileRouteVariant_HasReasonCountsAndCauses_NoRuleId()
    {
        var problem = PayrollPlanRefusalProblem.TryCreate(SingleProfileRefusal());

        Assert.NotNull(problem);
        using var doc = JsonDocument.Parse(ToWireJson(problem!));
        var root = doc.RootElement;
        Assert.Equal("payroll-recalc-blocked", root.GetProperty("kind").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(SingleProfileReason, root.GetProperty("reason").GetString());
        Assert.Equal(2, root.GetProperty("employedSegmentCount").GetInt32());
        Assert.Equal(new[] { "EmployeeProfileChange", "AgreementCodeChange" },
            root.GetProperty("interiorBoundaryCauses").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(SingleProfileSentence, root.GetProperty("error").GetString());
        Assert.True(
            !root.TryGetProperty("ruleId", out var ruleId) || ruleId.ValueKind == JsonValueKind.Null,
            "the single-profile-route variant carries no ruleId (it is not a rule's refusal)");
    }

    /// <summary>
    /// The Q6 (a) body, SERIALIZED, contains no calendar date, not the date and employee id the exception's own
    /// message names, and no digit in its sentence. The preconditions assert the message really carries the
    /// date and the employee id, so the redaction assertions CAN fail.
    ///
    /// Red conditions (RED by MUTATION at the B.3 gate): MB-11 (the body takes <c>error = ex.Message</c>, or
    /// otherwise leaks a date) puts the date and <c>EMP-REDACT-88213</c> into the JSON and trips the
    /// date-pattern and employee-id assertions.
    /// </summary>
    [Fact]
    public void SingleProfileRouteVariant_SerializedJson_HasNoDateNoEmployeeId()
    {
        var ex = SingleProfileRefusal();
        var isoChange = ChangeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Contains(EmployeeId, ex.Message);
        Assert.Contains(isoChange, ex.Message);

        var problem = PayrollPlanRefusalProblem.TryCreate(ex);
        Assert.NotNull(problem);
        var json = ToWireJson(problem!);

        Assert.DoesNotMatch(@"\d{4}-\d{2}-\d{2}", json);
        Assert.DoesNotContain(isoChange, json);
        Assert.DoesNotContain(ChangeDate.ToString(), json);
        Assert.DoesNotContain(EmployeeId, json);
        Assert.DoesNotContain("88213", json);

        using var doc = JsonDocument.Parse(json);
        Assert.DoesNotMatch(@"\d", doc.RootElement.GetProperty("error").GetString()!);
    }
}
