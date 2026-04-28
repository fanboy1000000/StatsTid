using System.Text.Json;
using StatsTid.Orchestrator.Contracts;

namespace StatsTid.Orchestrator.Services;

/// <summary>
/// Pre-persistence access gate for <c>/api/orchestrator/execute</c> (TASK-1901).
/// Codex BLOCKER on S18 proved that role-level <c>EmployeeOrAbove</c> guarding is
/// not sufficient: the orchestrator persists a task record under the caller-
/// supplied target employeeId before any downstream call, so a caller with a
/// valid Employee JWT could still poison the audit log against another
/// employee. This helper consolidates three checks the endpoint must perform
/// in order BEFORE it touches the database:
///   1. The task type is on the explicit allow-list for this endpoint.
///   2. The parameters carry a target employeeId that scope can be checked
///      against.
///   3. The supplied scope-check delegate accepts that employeeId for the
///      caller.
///
/// Pulling all three into one method keeps the allow-list a single source of
/// truth and makes every decision branch unit-testable without standing up
/// the orchestrator pipeline.
/// </summary>
public static class OrchestratorScopeHelpers
{
    /// <summary>
    /// Task types the <c>/api/orchestrator/execute</c> endpoint is allowed to
    /// dispatch. The <see cref="TaskDispatcher"/> also routes
    /// <c>payroll-export</c> and <c>external-integration</c>, but those are
    /// reachable only via direct calls to the payroll and external services
    /// respectively — they MUST NOT be invokable through <c>/execute</c>:
    /// <list type="bullet">
    /// <item><c>external-integration</c> → <c>/api/external/send</c> only requires
    /// <c>Authenticated</c>, so allowing it through <c>/execute</c> would let any
    /// Employee dispatch arbitrary external messages with their own JWT.</item>
    /// <item><c>payroll-export</c> → <c>/api/payroll/export</c> requires
    /// <c>GlobalAdminOnly</c> downstream, but the orchestrator persists a task
    /// record BEFORE dispatch, so a non-admin caller would still poison the
    /// audit log even though the dispatch itself is rejected.</item>
    /// </list>
    /// Adding a new task type here REQUIRES corresponding identity extraction in
    /// <see cref="ExtractEmployeeId"/> AND test coverage in
    /// <c>OrchestratorScopeEnforcementTests</c>.
    /// </summary>
    public static IReadOnlyList<string> AllowedExecuteTaskTypes { get; } = new[]
    {
        "rule-evaluation",
        "weekly-calculation"
    };

    public static bool IsAllowedExecuteTaskType(string? taskType)
        => taskType is not null && AllowedExecuteTaskTypes.Contains(taskType);

    /// <summary>
    /// Outcome of <see cref="ExtractEmployeeId(string, Dictionary{string, object})"/>:
    /// the resolved id, plus a flag for the conflict case in which both the
    /// top-level and the nested-profile fields carry distinct ids. The conflict
    /// case is treated as an access bypass attempt and 403'd by the endpoint —
    /// the caller may be trying to satisfy the gate with one id while the
    /// downstream consumer reads the other.
    /// </summary>
    public sealed record EmployeeIdExtraction(string? EmployeeId, bool Conflict, string? Reason);

    /// <summary>
    /// Extracts the target employeeId from the loosely-typed orchestrator
    /// parameters dictionary. Selection of the field is task-type-aware so the
    /// gate validates the SAME id the downstream consumer reads (Codex BLOCKER
    /// on first S19 review):
    ///   * <c>weekly-calculation</c> → top-level <c>parameters["employeeId"]</c>;
    ///     <see cref="WeeklyCalculationPipeline.ExecuteAsync"/> reads
    ///     <c>parameters["employeeId"]</c> to drive Backend lookups.
    ///   * <c>rule-evaluation</c>    → nested <c>parameters["profile"]["employeeId"]</c>;
    ///     <see cref="TaskDispatcher"/> forwards <c>parameters</c> verbatim to
    ///     <c>RuleEngine /api/rules/evaluate</c>, which reads
    ///     <c>request.Profile.EmployeeId</c>.
    /// If both fields are present and disagree, the request is rejected as a
    /// conflict — historically the helper picked top-level unconditionally,
    /// which let a caller satisfy the gate with their own id while the rule
    /// engine evaluated for a different employee.
    /// </summary>
    public static EmployeeIdExtraction ExtractEmployeeId(string taskType, Dictionary<string, object> parameters)
    {
        if (parameters is null)
            return new EmployeeIdExtraction(null, Conflict: false, Reason: "Missing parameters");

        var topLevel = TryReadTopLevelEmployeeId(parameters);
        var nested = TryReadNestedProfileEmployeeId(parameters);

        // Strict conflict rule applies to every allow-listed task type, even
        // ones whose downstream only reads one field — a request that carries
        // two distinct employee ids is malformed at best and an attempted
        // gate bypass at worst. Reject defensively.
        if (topLevel is not null && nested is not null
            && !string.Equals(topLevel, nested, StringComparison.Ordinal))
        {
            return new EmployeeIdExtraction(
                EmployeeId: null,
                Conflict: true,
                Reason: "Conflicting employeeId in top-level and profile");
        }

        // Per task type, pick the field the downstream consumer reads. If the
        // downstream's field is empty but the other one is present, that's a
        // malformed request: do NOT silently fall back to the wrong field —
        // returning null forces a 403 at the endpoint.
        var resolved = taskType switch
        {
            "rule-evaluation" => nested,
            "weekly-calculation" => topLevel,
            _ => null
        };

        return new EmployeeIdExtraction(resolved, Conflict: false, Reason: null);
    }

    private static string? TryReadTopLevelEmployeeId(Dictionary<string, object> parameters)
    {
        return TryReadStringValue(parameters, "employeeId", out var v) ? v : null;
    }

    private static string? TryReadNestedProfileEmployeeId(Dictionary<string, object> parameters)
    {
        if (!parameters.TryGetValue("profile", out var profileRaw) || profileRaw is null)
            return null;

        if (profileRaw is JsonElement el && el.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, "employeeId", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.String)
                {
                    var v = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Evaluates the full pre-persistence access gate for one
    /// <c>/api/orchestrator/execute</c> request. The endpoint converts the
    /// returned decision to either a 403 response or a green light to call
    /// <c>OrchestratorControlLoop.ExecuteAsync</c>. Centralising the decision
    /// keeps the allow-list, the identity-bearing payload contract, and the
    /// scope check in one testable surface.
    ///
    /// <paramref name="scopeCheck"/> is a delegate so the gate can be unit-
    /// tested without instantiating <c>OrgScopeValidator</c> or its DB-backed
    /// repositories. In production the endpoint passes
    /// <c>(id, ct) =&gt; scopeValidator.ValidateEmployeeAccessAsync(actor, id, ct)</c>.
    /// </summary>
    public static async Task<OrchestratorAccessDecision> EvaluateAccessAsync(
        ExecuteRequest request,
        Func<string, CancellationToken, Task<(bool Allowed, string? Reason)>> scopeCheck,
        CancellationToken ct)
    {
        if (!IsAllowedExecuteTaskType(request.TaskType))
        {
            return new OrchestratorAccessDecision(
                Allowed: false,
                StatusCode: 403,
                ErrorBody: new
                {
                    error = "Task type not allowed via /api/orchestrator/execute",
                    taskType = request.TaskType,
                    allowedTaskTypes = AllowedExecuteTaskTypes
                });
        }

        // Both currently-allowed task types carry an identity-bearing payload by
        // design (weekly-calculation top-level, rule-evaluation profile.employeeId).
        // A missing or unrecognisable employeeId is a malformed request — rejecting
        // it here closes the null-fall-through that would otherwise let a caller
        // persist a task record without a scope check. A request whose top-level
        // and nested ids disagree is a conflict (potential gate-bypass attempt
        // — Codex first-pass S19 BLOCKER) and gets a distinct error code so the
        // log clearly shows the rejection reason.
        var extraction = ExtractEmployeeId(request.TaskType, request.Parameters);
        if (extraction.Conflict)
        {
            return new OrchestratorAccessDecision(
                Allowed: false,
                StatusCode: 403,
                ErrorBody: new
                {
                    error = "Conflicting target employeeId in parameters",
                    taskType = request.TaskType,
                    reason = extraction.Reason
                });
        }
        var targetEmployeeId = extraction.EmployeeId;
        if (string.IsNullOrEmpty(targetEmployeeId))
        {
            return new OrchestratorAccessDecision(
                Allowed: false,
                StatusCode: 403,
                ErrorBody: new
                {
                    error = "Missing target employeeId in parameters",
                    taskType = request.TaskType
                });
        }

        var (allowed, reason) = await scopeCheck(targetEmployeeId, ct);
        if (!allowed)
        {
            return new OrchestratorAccessDecision(
                Allowed: false,
                StatusCode: 403,
                ErrorBody: new
                {
                    error = "Access denied",
                    reason,
                    targetEmployeeId
                });
        }

        return OrchestratorAccessDecision.Allow;
    }

    private static bool TryReadStringValue(Dictionary<string, object> dict, string key, out string? value)
    {
        value = null;
        if (!dict.TryGetValue(key, out var raw) || raw is null)
            return false;

        if (raw is JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.String:
                    var s = el.GetString();
                    if (string.IsNullOrWhiteSpace(s)) return false;
                    value = s;
                    return true;
                case JsonValueKind.Number:
                    value = el.ToString();
                    return !string.IsNullOrWhiteSpace(value);
                default:
                    return false;
            }
        }

        var str = raw.ToString();
        if (string.IsNullOrWhiteSpace(str)) return false;
        value = str;
        return true;
    }
}

/// <summary>
/// Outcome of <see cref="OrchestratorScopeHelpers.EvaluateAccessAsync"/>. When
/// <see cref="Allowed"/> is false, the endpoint returns
/// <c>Results.Json(<see cref="ErrorBody"/>, statusCode: <see cref="StatusCode"/>)</c>.
/// </summary>
public sealed record OrchestratorAccessDecision(bool Allowed, int StatusCode, object? ErrorBody)
{
    public static OrchestratorAccessDecision Allow { get; } =
        new(Allowed: true, StatusCode: 200, ErrorBody: null);
}
