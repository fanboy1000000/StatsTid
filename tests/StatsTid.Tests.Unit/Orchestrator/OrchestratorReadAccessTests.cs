using System.Text.Json;
using StatsTid.Auth;
using StatsTid.Orchestrator.Contracts;
using StatsTid.Orchestrator.Services;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Tests.Unit.Orchestrator;

/// <summary>
/// SEC-021 (owner ruling: Option A) — Docker-free unit coverage for the per-task READ
/// access gate on <c>GET /api/orchestrator/tasks/{id}</c>.
///
/// <para>
/// <b>What the finding was, in plain terms:</b> the read endpoint fetched a task by id and
/// returned it to ANY authenticated <c>EmployeeOrAbove</c> caller with NO ownership or scope
/// check — an IDOR (insecure direct object reference): user A could read user B's task simply
/// by knowing/guessing the id. The sibling <c>/execute</c> already scope-checks; the read path
/// did not. Option A adds a per-task scope check while keeping the <c>EmployeeOrAbove</c> floor.
/// </para>
///
/// <para>
/// <b>RED-before-green:</b> before the fix the handler body was
/// <c>return task is not null ? Results.Ok(task) : Results.NotFound();</c> — an out-of-scope
/// caller received <b>200 + the task</b>. The gate under test is the new decision surface; the
/// out-of-scope test below (<see cref="EvaluateReadAccess_NonAdmin_OutOfScope_Denies"/>) is
/// exactly the vector that returned 200 pre-fix and now denies.
/// </para>
///
/// <para>
/// These tests drive the two pure helpers directly, mirroring how
/// <c>OrchestratorScopeEnforcementTests</c> drives <c>EvaluateAccessAsync</c> — no orchestrator
/// host, no database. The scope check is a fake delegate; the real semantics
/// (<c>OrgScopeValidator.ValidateEmployeeAccessAsync</c>) are pinned end-to-end by the
/// Docker-gated <c>OrchestratorTaskReadAuthorizationTests</c> and by <c>Sprint7ScopeTests</c>.
/// </para>
///
/// <para>Layers:</para>
/// <list type="number">
///   <item><c>IsGlobalAdmin_*</c> — the claim-only GlobalAdmin signal (no subject lookup).</item>
///   <item><c>EvaluateReadAccess_*</c> — the ordered decision branches.</item>
///   <item><c>Hydration_*</c> — the serialize → persist → hydrate → extract round-trip that
///   pins the <see cref="JsonElement"/> requirement <c>ExtractEmployeeId</c> depends on.</item>
/// </list>
/// </summary>
public class OrchestratorReadAccessTests
{
    // ------------------------------------------------------------------------
    // Fakes / builders
    // ------------------------------------------------------------------------

    /// <summary>Records whether/with-what the scope-check delegate was invoked so a test can
    /// assert the GlobalAdmin / fail-closed branches SHORT-CIRCUIT before any scope check.</summary>
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

    /// <summary>Builds an <see cref="OrchestratorTask"/> whose InputData is hydrated from a JSON
    /// string the same way <see cref="OrchestratorControlLoop.DeserializeInputData"/> hydrates a
    /// persisted row — i.e. values are <see cref="JsonElement"/>, the production shape.</summary>
    private static OrchestratorTask TaskFromJson(string taskType, string? inputJson)
        => new()
        {
            TaskType = taskType,
            Status = "completed",
            InputData = inputJson is null ? null : OrchestratorControlLoop.DeserializeInputData(inputJson)
        };

    private const string WeeklyJson = """{"employeeId":"USR01","agreementCode":"AC"}""";
    private const string RuleEvalJson =
        """{"ruleId":"NORM_CHECK_37H","profile":{"employeeId":"USR42","agreementCode":"AC"}}""";

    private static ActorContext Actor(string? role, params RoleScope[] scopes)
        => new(ActorId: "ACTOR", ActorRole: role, CorrelationId: Guid.NewGuid(),
               OrgId: null, Scopes: scopes.Length == 0 ? null : scopes);

    // ========================================================================
    // 1. IsGlobalAdmin — claim-only signal (no subject/DB lookup)
    // ========================================================================

    [Fact]
    public void IsGlobalAdmin_RoleClaim_IsTrue_EvenWithNoScopes()
    {
        // A GlobalAdmin token is minted under GlobalAdminOnly (requireOrgScope:false) and
        // commonly carries NO scopes — the role claim alone must identify it.
        Assert.True(OrchestratorScopeHelpers.IsGlobalAdmin(Actor(StatsTidRoles.GlobalAdmin)));
    }

    [Fact]
    public void IsGlobalAdmin_GlobalAdminGlobalScope_IsTrue_EvenWithoutRoleClaim()
    {
        // Secondary signal: a GlobalAdmin-ROLE GLOBAL RoleScope denotes global reach even if the
        // role claim string is absent/legacy. (Step-7a hardening: the scope's Role must be
        // GlobalAdmin — see the negative test below.)
        var actor = Actor(role: null, new RoleScope(StatsTidRoles.GlobalAdmin, null, "GLOBAL"));
        Assert.True(OrchestratorScopeHelpers.IsGlobalAdmin(actor));
    }

    [Fact]
    public void IsGlobalAdmin_NonGlobalAdminRole_WithGlobalScopeType_IsFalse()
    {
        // SEC-021 Step-7a hardening: a GLOBAL scope-type whose ROLE is not GlobalAdmin must NOT
        // grant the bypass — otherwise a lower-role token bearing a GLOBAL scope would get an
        // unrestricted task-read bypass (broader than the role-based GlobalAdminOnly policy).
        var actor = Actor(role: StatsTidRoles.LocalAdmin, new RoleScope(StatsTidRoles.LocalAdmin, null, "GLOBAL"));
        Assert.False(OrchestratorScopeHelpers.IsGlobalAdmin(actor));
    }

    [Fact]
    public void IsGlobalAdmin_LocalAdminWithOrgScope_IsFalse()
    {
        var actor = Actor(StatsTidRoles.LocalAdmin, new RoleScope(StatsTidRoles.LocalAdmin, "MIN_A", "ORG_ONLY"));
        Assert.False(OrchestratorScopeHelpers.IsGlobalAdmin(actor));
    }

    [Fact]
    public void IsGlobalAdmin_EmployeeAndBareActor_AreFalse()
    {
        Assert.False(OrchestratorScopeHelpers.IsGlobalAdmin(Actor(StatsTidRoles.Employee)));
        Assert.False(OrchestratorScopeHelpers.IsGlobalAdmin(Actor(role: null)));
    }

    // ========================================================================
    // 2. EvaluateReadAccessAsync — ordered decision branches
    // ========================================================================

    [Fact]
    public async Task EvaluateReadAccess_GlobalAdmin_Allows_WithoutCallingScopeCheck()
    {
        var task = TaskFromJson("weekly-calculation", WeeklyJson);
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateReadAccessAsync(
            task, isGlobalAdmin: true, scope.AsDelegate(allowed: false), CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal(0, scope.CallCount); // bypass short-circuits before any subject resolution
    }

    [Fact]
    public async Task EvaluateReadAccess_GlobalAdmin_Allows_EvenWhenSubjectUnresolvable()
    {
        // The terminated-subject defect, at the helper level: a GlobalAdmin must be able to read
        // a task whose subject would be denied by the ACTIVE-subject validator (terminated /
        // deleted / never-resolvable). The delegate here stands in for that validator returning
        // deny ("Target employee not found") — but it is never even called, because GlobalAdmin
        // bypasses subject resolution entirely. Task input is deliberately null (unresolvable).
        var task = TaskFromJson("weekly-calculation", inputJson: null);
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateReadAccessAsync(
            task, isGlobalAdmin: true,
            scope.AsDelegate(allowed: false, reason: "Target employee not found"),
            CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal(0, scope.CallCount);
    }

    [Fact]
    public async Task EvaluateReadAccess_NonAdmin_InScopeSubject_Allows_WeeklyCalculation()
    {
        var task = TaskFromJson("weekly-calculation", WeeklyJson);
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateReadAccessAsync(
            task, isGlobalAdmin: false, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal("USR01", scope.CalledWith); // top-level subject, the field the pipeline reads
        Assert.Equal(1, scope.CallCount);
    }

    [Fact]
    public async Task EvaluateReadAccess_NonAdmin_InScopeSubject_Allows_RuleEvaluation()
    {
        var task = TaskFromJson("rule-evaluation", RuleEvalJson);
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateReadAccessAsync(
            task, isGlobalAdmin: false, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal("USR42", scope.CalledWith); // nested profile.employeeId, the field the rule engine reads
    }

    [Fact]
    public async Task EvaluateReadAccess_NonAdmin_OutOfScope_Denies()
    {
        // The exact pre-fix IDOR vector: an authenticated non-admin whose scope does NOT cover
        // the task's subject. Pre-fix → 200 + task; now → deny (endpoint renders 404).
        var task = TaskFromJson("weekly-calculation", WeeklyJson);
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateReadAccessAsync(
            task, isGlobalAdmin: false,
            scope.AsDelegate(allowed: false, reason: "Actor scope does not cover target organization"),
            CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal("USR01", scope.CalledWith);
    }

    [Fact]
    public async Task EvaluateReadAccess_NonAdmin_OwnerlessTaskType_Denies_WithoutCallingScopeCheck()
    {
        // An ownerless/admin task type (payroll-export is a real TaskDispatcher route) has no
        // subject ExtractEmployeeId knows how to read → null → fail-closed deny, and the scope
        // check must NOT run (there is no subject to check).
        var task = TaskFromJson("payroll-export", """{"employeeId":"USR01"}""");
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateReadAccessAsync(
            task, isGlobalAdmin: false, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(0, scope.CallCount);
    }

    [Theory]
    [InlineData(null)]                                  // NULL input_data (IsDBNull path)
    [InlineData("""{"agreementCode":"AC"}""")]          // present but no employeeId
    public async Task EvaluateReadAccess_NonAdmin_MalformedOrMissingSubject_Denies_FailClosed(string? inputJson)
    {
        var task = TaskFromJson("weekly-calculation", inputJson);
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateReadAccessAsync(
            task, isGlobalAdmin: false, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(0, scope.CallCount);
    }

    [Fact]
    public async Task EvaluateReadAccess_GlobalAdmin_MalformedInput_StillAllows()
    {
        // Symmetric to the fail-closed deny above: a GlobalAdmin bypasses subject resolution, so
        // malformed/null input never turns into a deny for a global caller.
        var task = TaskFromJson("weekly-calculation", inputJson: null);
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateReadAccessAsync(
            task, isGlobalAdmin: true, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.True(decision.Allowed);
        Assert.Equal(0, scope.CallCount);
    }

    [Fact]
    public async Task EvaluateReadAccess_NonAdmin_ConflictingSubjectIds_Denies_WithoutCallingScopeCheck()
    {
        // top-level vs nested profile.employeeId disagree → ExtractEmployeeId reports a Conflict
        // → deny before any scope check (the same gate-bypass defense /execute applies).
        var task = TaskFromJson("rule-evaluation",
            """{"employeeId":"ATTACKER","profile":{"employeeId":"VICTIM"}}""");
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateReadAccessAsync(
            task, isGlobalAdmin: false, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(0, scope.CallCount);
    }

    // ========================================================================
    // 3. Hydration round-trip — pins the JsonElement requirement (SEC-021)
    // ========================================================================
    // These replicate the production data path exactly:
    //   client JSON  --(model bind: Deserialize<Dictionary<string,object>>)-->  request.Parameters
    //   request.Parameters  --(PersistTaskAsync: Serialize)-->  input_data JSONB text
    //   input_data text  --(GetTaskAsync: DeserializeInputData)-->  hydrated InputData
    // and assert ExtractEmployeeId then finds the subject — which it can ONLY do if hydration
    // yields JsonElement values (its rule-evaluation path requires profile to be a JsonElement
    // Object). A nested-Dictionary hydration would silently return null and fail the gate closed.

    [Fact]
    public void Hydration_WeeklyCalculation_YieldsJsonElement_AndExtractFindsSubject()
    {
        var bound = JsonSerializer.Deserialize<Dictionary<string, object>>(WeeklyJson)!; // model-bound request.Parameters
        var persisted = JsonSerializer.Serialize(bound);                                 // PersistTaskAsync writes this to input_data
        var hydrated = OrchestratorControlLoop.DeserializeInputData(persisted)!;         // GetTaskAsync reads it back

        Assert.IsType<JsonElement>(hydrated["employeeId"]); // values are JsonElement, not raw strings
        Assert.Equal("USR01", OrchestratorScopeHelpers.ExtractEmployeeId("weekly-calculation", hydrated).EmployeeId);
    }

    [Fact]
    public void Hydration_RuleEvaluation_NestedProfileIsJsonElementObject_AndExtractFindsSubject()
    {
        var bound = JsonSerializer.Deserialize<Dictionary<string, object>>(RuleEvalJson)!;
        var persisted = JsonSerializer.Serialize(bound);
        var hydrated = OrchestratorControlLoop.DeserializeInputData(persisted)!;

        // THE pin: the nested profile must be a JsonElement of ValueKind Object, or
        // TryReadNestedProfileEmployeeId returns null and the gate fails closed for a
        // legitimately in-scope caller.
        Assert.True(hydrated["profile"] is JsonElement { ValueKind: JsonValueKind.Object });
        Assert.Equal("USR42", OrchestratorScopeHelpers.ExtractEmployeeId("rule-evaluation", hydrated).EmployeeId);
    }

    [Fact]
    public void Hydration_NullOrWhitespaceJson_ReturnsNull()
    {
        // IsDBNull(4) at the call site passes null; guard also covers an empty/whitespace column.
        Assert.Null(OrchestratorControlLoop.DeserializeInputData(null));
        Assert.Null(OrchestratorControlLoop.DeserializeInputData("   "));
    }

    [Theory]
    [InlineData("[1,2,3]")]        // valid JSON, but an ARRAY (not an object)
    [InlineData("\"scalar\"")]     // valid JSON, but a bare STRING scalar
    [InlineData("42")]             // valid JSON, but a bare NUMBER scalar
    [InlineData("{not json")]      // MALFORMED JSON
    public void Hydration_NonObjectOrMalformedJson_ReturnsNull_FailClosed(string storedJson)
    {
        // SEC-021 Step-5a Codex BLOCKER: a stored input_data row that is valid-JSON-but-not-an-
        // object, or outright malformed, must NOT throw (which would surface as a 500 from the
        // read handler BEFORE the access check). It fails CLOSED to null so the gate denies a
        // non-admin. Unreachable via the sole writer, but structural against a future raw INSERT.
        var hydrated = OrchestratorControlLoop.DeserializeInputData(storedJson);

        Assert.Null(hydrated);
    }

    [Fact]
    public async Task EvaluateReadAccess_NonAdmin_MalformedStoredInput_Denies_FailClosed()
    {
        // The end-to-end consequence of the fail-closed hydration above: a task whose input_data
        // could not be hydrated (→ null InputData) resolves to a null subject and the non-admin
        // is denied WITHOUT a scope check — a 404, never a 500.
        var task = new OrchestratorTask
        {
            TaskType = "weekly-calculation",
            Status = "completed",
            InputData = OrchestratorControlLoop.DeserializeInputData("[1,2,3]") // → null
        };
        var scope = new FakeScopeCheck();

        var decision = await OrchestratorScopeHelpers.EvaluateReadAccessAsync(
            task, isGlobalAdmin: false, scope.AsDelegate(allowed: true), CancellationToken.None);

        Assert.False(decision.Allowed);
        Assert.Equal(0, scope.CallCount);
    }
}
