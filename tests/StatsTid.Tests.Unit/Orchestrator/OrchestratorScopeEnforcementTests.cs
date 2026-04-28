using System.Text.Json;
using StatsTid.Orchestrator.Contracts;
using StatsTid.Orchestrator.Services;

namespace StatsTid.Tests.Unit.Orchestrator;

/// <summary>
/// Tests for TASK-1901 (Codex BLOCKER on S18 remediation): the orchestrator's
/// <c>/api/orchestrator/execute</c> endpoint must reject requests whose
/// caller-supplied target employeeId is outside the caller's scope BEFORE any
/// task record is persisted.
///
/// The endpoint delegates the full pre-persistence gate to
/// <see cref="OrchestratorScopeHelpers.EvaluateAccessAsync"/>, so these tests
/// exercise that method directly with a fake scope-check delegate. Doing it
/// this way pins the four scope-decision branches the SPRINT-19 validation
/// criteria call out — Employee self / Employee cross-user / LocalAdmin in-scope
/// / LocalAdmin out-of-scope — without standing up the orchestrator service or
/// its DB. The actual scope-check semantics (ownership for Employee, materialized-
/// path coverage for higher roles) are encoded in
/// <c>OrgScopeValidator.ValidateEmployeeAccessAsync</c> and exhaustively pinned
/// by <c>Sprint7ScopeTests</c>; these tests cover the orchestrator-layer wiring
/// (allow-list + identity extraction + delegate invocation + decision shape).
///
/// Layered coverage:
///  1. <c>EvaluateAccessAsync_*</c> — gate decision branches.
///  2. <c>ExtractEmployeeId_*</c> — helper contract for the two payload shapes
///     the Backend dispatches today.
/// </summary>
public class OrchestratorScopeEnforcementTests
{
    // ============================================================
    // EvaluateAccessAsync — decision-branch coverage (TASK-1901 BLOCKER)
    // ============================================================

    private static ExecuteRequest MakeWeeklyCalculationRequest(string? employeeId)
    {
        var parameters = new Dictionary<string, object>();
        if (employeeId is not null) parameters["employeeId"] = employeeId;
        parameters["agreementCode"] = "AC";
        return new ExecuteRequest { TaskType = "weekly-calculation", Parameters = parameters };
    }

    private static ExecuteRequest MakeRuleEvaluationRequest(string employeeId)
    {
        var json = $$"""
        {
            "ruleId": "NORM_CHECK_37H",
            "profile": { "employeeId": "{{employeeId}}", "agreementCode": "AC" },
            "periodStart": "2026-05-01",
            "periodEnd": "2026-05-07"
        }
        """;
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
        return new ExecuteRequest { TaskType = "rule-evaluation", Parameters = parameters };
    }

    /// <summary>
    /// Scope-check stub that records the employeeId it was called with so the
    /// test can assert the gate forwarded the right target. Returns the canned
    /// outcome regardless of input.
    /// </summary>
    private sealed class FakeScopeCheck
    {
        public string? CalledWith { get; private set; }
        public int CallCount { get; private set; }

        public Func<string, CancellationToken, Task<(bool Allowed, string? Reason)>> AsDelegate(
            bool allowed, string? reason = null)
            => (id, _) =>
            {
                CalledWith = id;
                CallCount++;
                return Task.FromResult((allowed, reason));
            };
    }

    [Fact]
    public async Task EvaluateAccessAsync_EmployeeSelfScope_Allows()
    {
        // Branch 1: Employee accessing their own employeeId. The scope-check
        // delegate (production: OrgScopeValidator) returns (true, null). Gate
        // must forward the target id and return Allow.
        var request = MakeWeeklyCalculationRequest("USR01");
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
            request, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal(200, decision.StatusCode);
        Assert.Null(decision.ErrorBody);
        Assert.Equal("USR01", scope.CalledWith);
        Assert.Equal(1, scope.CallCount);
    }

    [Fact]
    public async Task EvaluateAccessAsync_EmployeeCrossUser_Denies()
    {
        // Branch 2: Employee target outside own ownership. Production validator
        // returns (false, "Employee can only access own data"); gate must turn
        // that into a 403 carrying the reason.
        var request = MakeWeeklyCalculationRequest("USR02");
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
            request,
            scope.AsDelegate(allowed: false, reason: "Employee can only access own data"),
            CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(403, decision.StatusCode);
        Assert.NotNull(decision.ErrorBody);
        Assert.Equal("USR02", scope.CalledWith);
        var body = SerializeErrorBody(decision.ErrorBody!);
        Assert.Contains("Access denied", body);
        Assert.Contains("Employee can only access own data", body);
        Assert.Contains("USR02", body);
    }

    [Fact]
    public async Task EvaluateAccessAsync_LocalAdminInScope_Allows()
    {
        // Branch 3: LocalAdmin whose materialized-path scope covers the target's
        // org. Validator returns (true, null) and the gate must forward the
        // nested rule-evaluation profile.employeeId.
        var request = MakeRuleEvaluationRequest("USR42");
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
            request, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal("USR42", scope.CalledWith);
    }

    [Fact]
    public async Task EvaluateAccessAsync_LocalAdminOutOfScope_Denies()
    {
        // Branch 4: LocalAdmin whose scope does NOT cover the target's org.
        // Validator returns (false, "Actor scope does not cover target organization").
        var request = MakeRuleEvaluationRequest("USR42");
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
            request,
            scope.AsDelegate(allowed: false, reason: "Actor scope does not cover target organization"),
            CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(403, decision.StatusCode);
        Assert.Equal("USR42", scope.CalledWith);
        Assert.Contains("Actor scope does not cover target organization", SerializeErrorBody(decision.ErrorBody!));
    }

    [Fact]
    public async Task EvaluateAccessAsync_RejectsPayrollExportTaskType_WithoutCallingScopeCheck()
    {
        // Allow-list rejection: payroll-export is a TaskDispatcher route but is
        // intentionally NOT reachable via /execute (would let any Employee push
        // a task record under their own JWT before the GlobalAdminOnly guard on
        // /api/payroll/export rejects the dispatch). Scope check must NOT be
        // invoked; the rejection happens before any per-employee work.
        var request = new ExecuteRequest
        {
            TaskType = "payroll-export",
            Parameters = new Dictionary<string, object> { ["employeeId"] = "USR01" }
        };
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
            request, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(403, decision.StatusCode);
        Assert.Equal(0, scope.CallCount);
        var body = SerializeErrorBody(decision.ErrorBody!);
        Assert.Contains("Task type not allowed", body);
        Assert.Contains("payroll-export", body);
    }

    [Fact]
    public async Task EvaluateAccessAsync_RejectsExternalIntegrationTaskType_WithoutCallingScopeCheck()
    {
        // Allow-list rejection: external-integration → /api/external/send only
        // requires "Authenticated", so allowing it through /execute would let
        // any Employee dispatch arbitrary external messages. Same null-call
        // contract for the scope-check delegate.
        var request = new ExecuteRequest
        {
            TaskType = "external-integration",
            Parameters = new Dictionary<string, object> { ["employeeId"] = "USR01" }
        };
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
            request, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(403, decision.StatusCode);
        Assert.Equal(0, scope.CallCount);
        Assert.Contains("external-integration", SerializeErrorBody(decision.ErrorBody!));
    }

    [Fact]
    public async Task EvaluateAccessAsync_RejectsUnknownTaskType_WithoutCallingScopeCheck()
    {
        // Defence in depth: any task type not on the explicit allow-list must
        // 403. This guards against future TaskDispatcher additions silently
        // becoming reachable through /execute.
        var request = new ExecuteRequest
        {
            TaskType = "compliance-check",
            Parameters = new Dictionary<string, object> { ["employeeId"] = "USR01" }
        };
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
            request, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(0, scope.CallCount);
    }

    [Fact]
    public async Task EvaluateAccessAsync_RejectsAllowListedTaskTypeMissingEmployeeId()
    {
        // Null-fall-through closure: an allow-listed task type whose parameters
        // lack an identity-bearing field must 403, NOT pass through. Pre-fix the
        // gate let null-id requests reach loop.ExecuteAsync — a task record
        // would be persisted before any downstream rejection. Since both
        // currently allow-listed task types are required to carry an
        // identity, this is a malformed-request rejection.
        var request = MakeWeeklyCalculationRequest(employeeId: null);
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
            request, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(403, decision.StatusCode);
        Assert.Equal(0, scope.CallCount);
        Assert.Contains("Missing target employeeId", SerializeErrorBody(decision.ErrorBody!));
    }

    [Fact]
    public async Task EvaluateAccessAsync_AllowDecisionIsTheCanonicalSingleton()
    {
        // The Allow path must return the static singleton so callers can
        // pattern-match on identity if they want. Pin the contract.
        var request = MakeWeeklyCalculationRequest("USR01");
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
            request, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.Same(OrchestratorAccessDecision.Allow, decision);
    }

    [Fact]
    public void IsAllowedExecuteTaskType_CoversAllowedAndRejectsOthers()
    {
        Assert.True(OrchestratorScopeHelpers.IsAllowedExecuteTaskType("rule-evaluation"));
        Assert.True(OrchestratorScopeHelpers.IsAllowedExecuteTaskType("weekly-calculation"));
        Assert.False(OrchestratorScopeHelpers.IsAllowedExecuteTaskType("payroll-export"));
        Assert.False(OrchestratorScopeHelpers.IsAllowedExecuteTaskType("external-integration"));
        Assert.False(OrchestratorScopeHelpers.IsAllowedExecuteTaskType(null));
        Assert.False(OrchestratorScopeHelpers.IsAllowedExecuteTaskType(""));
    }

    [Fact]
    public void AllowedExecuteTaskTypes_IsTheSingleSourceOfTruth()
    {
        // Pin the allow-list contents so any future additions are deliberate
        // (and force the corresponding identity-extraction + test work). If
        // this assertion needs updating, also update ExtractEmployeeId and
        // OrchestratorScopeEnforcementTests.
        Assert.Equal(
            new[] { "rule-evaluation", "weekly-calculation" },
            OrchestratorScopeHelpers.AllowedExecuteTaskTypes);
    }

    private static string SerializeErrorBody(object body) =>
        JsonSerializer.Serialize(body);

    // ============================================================
    // ExtractEmployeeId — per-task-type payload-shape contract
    // ============================================================
    // These pin the helper's contract directly so the gate-level tests above
    // can rely on it without re-asserting JSON shape every time.
    //
    // Codex first-pass S19 review found that the original (task-type-blind)
    // helper preferred top-level over nested unconditionally — letting a
    // caller satisfy ValidateEmployeeAccessAsync with their own id while the
    // RuleEngine consumed `request.Profile.EmployeeId` from the nested
    // payload. The fix makes the helper task-type-aware so the gate
    // validates the SAME id the downstream consumer reads, and rejects
    // requests whose two id fields disagree.

    // ---- weekly-calculation: top-level only --------------------------------

    [Fact]
    public void ExtractEmployeeId_WeeklyCalculation_ReadsTopLevelEmployeeId()
    {
        // Mirrors TimeEndpoints.cs `/api/time-entries/calculate-week` payload —
        // the production shape WeeklyCalculationPipeline.ExecuteAsync consumes
        // via parameters["employeeId"].
        const string json = """
        {
            "employeeId": "USR01",
            "agreementCode": "AC",
            "okVersion": "OK26",
            "periodStart": "2026-05-01",
            "periodEnd": "2026-05-07",
            "weeklyNormHours": 37.0,
            "partTimeFraction": 1.0,
            "previousFlexBalance": 0.0
        }
        """;
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("weekly-calculation", parameters);

        Assert.Equal("USR01", extraction.EmployeeId);
        Assert.False(extraction.Conflict);
    }

    [Fact]
    public void ExtractEmployeeId_WeeklyCalculation_IgnoresUnrelatedNestedProfile()
    {
        // weekly-calculation requests in production never carry a nested
        // profile.employeeId — but if one ever does and matches the top-level,
        // the helper must still return the top-level (the field the
        // pipeline consumes). Pin this so a future payload evolution doesn't
        // accidentally change the source of truth for this task type.
        var parameters = new Dictionary<string, object>
        {
            ["employeeId"] = "USR01",
            ["profile"] = JsonSerializer.SerializeToElement(new { employeeId = "USR01" })
        };

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("weekly-calculation", parameters);

        Assert.Equal("USR01", extraction.EmployeeId);
        Assert.False(extraction.Conflict);
    }

    [Fact]
    public void ExtractEmployeeId_WeeklyCalculation_NullWhenTopLevelMissing()
    {
        // Even if a nested profile.employeeId is present, weekly-calculation
        // MUST NOT silently fall back to it — the pipeline reads top-level,
        // and a request without a top-level id is malformed for this task.
        var parameters = new Dictionary<string, object>
        {
            ["agreementCode"] = "AC",
            ["profile"] = JsonSerializer.SerializeToElement(new { employeeId = "USR42" })
        };

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("weekly-calculation", parameters);

        Assert.Null(extraction.EmployeeId);
        Assert.False(extraction.Conflict);
    }

    [Fact]
    public void ExtractEmployeeId_WeeklyCalculation_HandlesNonStringTopLevelViaToString()
    {
        // The parameters dict is loosely typed (Dictionary<string, object>);
        // numeric or other ToString-able values must still resolve when the
        // dict is constructed in-process (in tests). The JSON path covers the
        // JsonElement-string case via the WeeklyCalculation_ReadsTopLevel test.
        var parameters = new Dictionary<string, object> { ["employeeId"] = 12345 };

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("weekly-calculation", parameters);

        Assert.Equal("12345", extraction.EmployeeId);
    }

    [Fact]
    public void ExtractEmployeeId_WeeklyCalculation_NullWhenTopLevelEmptyOrWhitespace()
    {
        var parametersEmpty = new Dictionary<string, object> { ["employeeId"] = "" };
        var parametersWhitespace = new Dictionary<string, object> { ["employeeId"] = "   " };

        Assert.Null(OrchestratorScopeHelpers.ExtractEmployeeId("weekly-calculation", parametersEmpty).EmployeeId);
        Assert.Null(OrchestratorScopeHelpers.ExtractEmployeeId("weekly-calculation", parametersWhitespace).EmployeeId);
    }

    // ---- rule-evaluation: nested profile.employeeId only -------------------

    [Fact]
    public void ExtractEmployeeId_RuleEvaluation_ReadsNestedProfileEmployeeId()
    {
        // Mirrors TimeEndpoints.cs `/api/time-entries/calculate` payload —
        // the production shape RuleEngine /api/rules/evaluate consumes via
        // request.Profile.EmployeeId. THIS is the field the gate must check
        // for rule-evaluation; the prior top-level-wins helper let a caller
        // satisfy the gate with one id while the rule engine used another.
        const string json = """
        {
            "ruleId": "NORM_CHECK_37H",
            "profile": {
                "employeeId": "USR01",
                "agreementCode": "AC",
                "okVersion": "OK26",
                "weeklyNormHours": 37.0,
                "employmentCategory": "Standard",
                "partTimeFraction": 1.0
            },
            "periodStart": "2026-05-01",
            "periodEnd": "2026-05-07"
        }
        """;
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("rule-evaluation", parameters);

        Assert.Equal("USR01", extraction.EmployeeId);
        Assert.False(extraction.Conflict);
    }

    [Fact]
    public void ExtractEmployeeId_RuleEvaluation_IgnoresUnrelatedTopLevelEmployeeId()
    {
        // Production rule-evaluation payloads do not carry a top-level
        // employeeId. If a benign caller does include one (and it matches the
        // nested id), the helper must still return the nested value — that is
        // the field the rule engine reads. The mismatch case is covered by
        // the conflict tests below.
        const string json = """
        {
            "ruleId": "NORM_CHECK_37H",
            "employeeId": "USR01",
            "profile": { "employeeId": "USR01", "agreementCode": "AC", "okVersion": "OK26" }
        }
        """;
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("rule-evaluation", parameters);

        Assert.Equal("USR01", extraction.EmployeeId);
        Assert.False(extraction.Conflict);
    }

    [Fact]
    public void ExtractEmployeeId_RuleEvaluation_NullWhenNestedMissing()
    {
        // For rule-evaluation, a missing nested profile.employeeId must NOT
        // fall back to a present top-level id — the rule engine reads
        // profile.employeeId regardless, so falling back would re-introduce
        // the gate/downstream-mismatch the BLOCKER fix closed.
        const string json = """{ "ruleId": "NORM_CHECK_37H", "employeeId": "USR01" }""";
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("rule-evaluation", parameters);

        Assert.Null(extraction.EmployeeId);
        Assert.False(extraction.Conflict);
    }

    [Fact]
    public void ExtractEmployeeId_RuleEvaluation_HandlesProfileEmployeeIdRegardlessOfCasing()
    {
        const string json = """{ "profile": { "EmployeeId": "USR42" } }""";
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("rule-evaluation", parameters);

        Assert.Equal("USR42", extraction.EmployeeId);
    }

    [Fact]
    public void ExtractEmployeeId_RuleEvaluation_NullWhenProfilePresentButLacksEmployeeId()
    {
        const string json = """{ "profile": { "agreementCode": "AC", "okVersion": "OK26" } }""";
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("rule-evaluation", parameters);

        Assert.Null(extraction.EmployeeId);
    }

    [Fact]
    public void ExtractEmployeeId_RuleEvaluation_NullWhenProfileIsNotAnObject()
    {
        const string jsonString = """{ "profile": "not-an-object" }""";
        const string jsonArray = """{ "profile": [] }""";
        const string jsonNull = """{ "profile": null }""";

        foreach (var json in new[] { jsonString, jsonArray, jsonNull })
        {
            var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
            var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("rule-evaluation", parameters);
            Assert.Null(extraction.EmployeeId);
            Assert.False(extraction.Conflict);
        }
    }

    [Fact]
    public void ExtractEmployeeId_RuleEvaluation_NullWhenNestedEmployeeIdEmptyOrNonString()
    {
        const string jsonEmpty = """{ "profile": { "employeeId": "" } }""";
        const string jsonNumber = """{ "profile": { "employeeId": 12345 } }""";

        Assert.Null(OrchestratorScopeHelpers.ExtractEmployeeId(
            "rule-evaluation", JsonSerializer.Deserialize<Dictionary<string, object>>(jsonEmpty)!).EmployeeId);
        Assert.Null(OrchestratorScopeHelpers.ExtractEmployeeId(
            "rule-evaluation", JsonSerializer.Deserialize<Dictionary<string, object>>(jsonNumber)!).EmployeeId);
    }

    // ---- conflict detection (Codex first-pass BLOCKER closure) -------------

    [Fact]
    public void ExtractEmployeeId_RuleEvaluation_RejectsTopLevelVsNestedMismatch()
    {
        // The BLOCKER vector: caller passes their own id at top-level (to
        // satisfy the gate against their own scope) and the victim id in the
        // nested profile (which the rule engine actually consumes). Helper
        // must report a Conflict, NOT silently pick one.
        const string json = """
        {
            "employeeId": "ATTACKER",
            "profile": { "employeeId": "VICTIM" }
        }
        """;
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("rule-evaluation", parameters);

        Assert.True(extraction.Conflict);
        Assert.Null(extraction.EmployeeId);
        Assert.NotNull(extraction.Reason);
    }

    [Fact]
    public void ExtractEmployeeId_WeeklyCalculation_RejectsTopLevelVsNestedMismatch()
    {
        // Same conflict rule applies symmetrically: even though the pipeline
        // for weekly-calculation reads only top-level today, a request that
        // ALSO carries a different nested id is malformed and a likely bypass
        // attempt aimed at a future code path. Reject defensively.
        const string json = """
        {
            "employeeId": "ATTACKER",
            "profile": { "employeeId": "VICTIM" }
        }
        """;
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("weekly-calculation", parameters);

        Assert.True(extraction.Conflict);
        Assert.Null(extraction.EmployeeId);
    }

    [Fact]
    public async Task EvaluateAccessAsync_RuleEvaluation_RejectsTopLevelVsNestedMismatch_BeforeScopeCheck()
    {
        // End-to-end through the gate: a conflict request must 403 BEFORE
        // calling the scope-check delegate. Critical because the bypass
        // depended on the gate calling scope-check with the attacker's id and
        // returning Allow — closing the conflict at the helper level is only
        // useful if the endpoint short-circuits on it.
        const string json = """
        {
            "ruleId": "NORM_CHECK_37H",
            "employeeId": "ATTACKER",
            "profile": { "employeeId": "VICTIM" }
        }
        """;
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
        var request = new ExecuteRequest { TaskType = "rule-evaluation", Parameters = parameters };
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
            request, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(403, decision.StatusCode);
        Assert.Equal(0, scope.CallCount);  // scope check must NOT have been invoked
        var body = SerializeErrorBody(decision.ErrorBody!);
        Assert.Contains("Conflicting target employeeId", body);
    }

    [Fact]
    public async Task EvaluateAccessAsync_WeeklyCalculation_RejectsTopLevelVsNestedMismatch_BeforeScopeCheck()
    {
        // Symmetry pin (S19 internal Reviewer NOTE): rule-evaluation has an
        // end-to-end conflict short-circuit test above; this is the
        // weekly-calculation analogue. Today the conflict branch in
        // OrchestratorScopeHelpers.EvaluateAccessAsync is task-type-blind, so
        // both task types route through the same code path and cycle 1's
        // BLOCKER closure covers them transitively. Pinning the symmetry
        // explicitly survives a future refactor that pushes conflict
        // detection into a per-task-type branch — without this test, the
        // weekly-calculation conflict path could regress silently.
        const string json = """
        {
            "employeeId": "ATTACKER",
            "agreementCode": "AC",
            "profile": { "employeeId": "VICTIM" }
        }
        """;
        var parameters = JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
        var request = new ExecuteRequest { TaskType = "weekly-calculation", Parameters = parameters };
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
            request, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(403, decision.StatusCode);
        Assert.Equal(0, scope.CallCount);
        var body = SerializeErrorBody(decision.ErrorBody!);
        Assert.Contains("Conflicting target employeeId", body);
    }

    // ---- general null-safety -----------------------------------------------

    [Fact]
    public void ExtractEmployeeId_NullParameters_ReturnsNullEmployeeIdNoConflict()
    {
        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("rule-evaluation", null!);

        Assert.Null(extraction.EmployeeId);
        Assert.False(extraction.Conflict);
    }

    [Fact]
    public void ExtractEmployeeId_UnknownTaskType_ReturnsNullEmployeeId()
    {
        // Defence in depth: the helper is task-type-aware and only knows
        // weekly-calculation and rule-evaluation. Any other task type returns
        // null (which the endpoint treats as 403). This means future task
        // types REQUIRE deliberate addition here AND in the allow-list AND in
        // the corresponding test class — no silent default to "use top-level".
        var parameters = new Dictionary<string, object>
        {
            ["employeeId"] = "USR01",
            ["profile"] = JsonSerializer.SerializeToElement(new { employeeId = "USR01" })
        };

        var extraction = OrchestratorScopeHelpers.ExtractEmployeeId("unknown-task-type", parameters);

        Assert.Null(extraction.EmployeeId);
        Assert.False(extraction.Conflict);
    }
}
