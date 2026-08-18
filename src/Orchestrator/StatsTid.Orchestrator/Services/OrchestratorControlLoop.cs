using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.Orchestrator.Contracts;
using System.Text.Json;

namespace StatsTid.Orchestrator.Services;

public sealed class OrchestratorControlLoop
{
    private readonly TaskDispatcher _dispatcher;
    private readonly OutputValidator _validator;
    private readonly WeeklyCalculationPipeline _weeklyPipeline;
    private readonly DbConnectionFactory _connectionFactory;
    private readonly ILogger<OrchestratorControlLoop> _logger;

    public OrchestratorControlLoop(
        TaskDispatcher dispatcher,
        OutputValidator validator,
        WeeklyCalculationPipeline weeklyPipeline,
        DbConnectionFactory connectionFactory,
        ILogger<OrchestratorControlLoop> logger)
    {
        _dispatcher = dispatcher;
        _validator = validator;
        _weeklyPipeline = weeklyPipeline;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<OrchestratorTask> ExecuteAsync(
        ExecuteRequest request,
        string? authorizationHeader = null,
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        var task = new OrchestratorTask
        {
            TaskType = request.TaskType,
            Status = "pending",
            InputData = request.Parameters
        };

        // 1. Log task creation
        await PersistTaskAsync(task, ct);
        _logger.LogInformation("Task {TaskId} created: {TaskType}", task.TaskId, task.TaskType);

        // 2. Assign to agent
        task.AssignedAgent = ResolveAgent(request.TaskType);
        task.Status = "assigned";
        task.StartedAt = DateTime.UtcNow;
        await UpdateTaskAsync(task, ct);
        _logger.LogInformation("Task {TaskId} assigned to {Agent}", task.TaskId, task.AssignedAgent);

        // 3. Handle weekly-calculation as a composite pipeline
        if (request.TaskType == "weekly-calculation")
        {
            return await ExecuteWeeklyCalculation(task, request.Parameters, authorizationHeader, correlationId, ct);
        }

        // 4. Dispatch to service (single-rule path)
        try
        {
            var response = await _dispatcher.DispatchAsync(request.TaskType, request.Parameters, authorizationHeader, correlationId, ct);

            if (response is null)
            {
                task.Status = "failed";
                task.ErrorMessage = $"No route for task type: {request.TaskType}";
                await UpdateTaskAsync(task, ct);
                return task;
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);

            // 5. Validate output
            if (response.IsSuccessStatusCode && _validator.Validate(request.TaskType, responseBody))
            {
                task.Status = "completed";
                task.OutputData = JsonSerializer.Deserialize<object>(responseBody);
                task.CompletedAt = DateTime.UtcNow;
                _logger.LogInformation("Task {TaskId} completed successfully", task.TaskId);
            }
            else
            {
                task.Status = "failed";
                task.ErrorMessage = $"HTTP {response.StatusCode}: {responseBody}";
                _logger.LogWarning("Task {TaskId} failed: {Error}", task.TaskId, task.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Task {TaskId} failed with exception", task.TaskId);
        }

        await UpdateTaskAsync(task, ct);
        return task;
    }

    private async Task<OrchestratorTask> ExecuteWeeklyCalculation(
        OrchestratorTask task,
        Dictionary<string, object> parameters,
        string? authorizationHeader,
        Guid? correlationId,
        CancellationToken ct)
    {
        try
        {
            var result = await _weeklyPipeline.ExecuteAsync(parameters, authorizationHeader, correlationId, ct);

            task.Status = "completed";
            task.OutputData = result;
            task.CompletedAt = DateTime.UtcNow;
            _logger.LogInformation("Weekly calculation {TaskId} completed for {EmployeeId}",
                task.TaskId, result.EmployeeId);
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Weekly calculation {TaskId} failed", task.TaskId);
        }

        await UpdateTaskAsync(task, ct);
        return task;
    }

    public async Task<OrchestratorTask?> GetTaskAsync(Guid taskId, CancellationToken ct = default)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            SELECT task_id, task_type, status, assigned_agent, input_data, output_data,
                   created_at, started_at, completed_at, error_message
            FROM orchestrator_tasks WHERE task_id = @taskId
            """, conn);
        cmd.Parameters.AddWithValue("taskId", taskId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new OrchestratorTask
        {
            TaskId = reader.GetGuid(0),
            TaskType = reader.GetString(1),
            Status = reader.GetString(2),
            AssignedAgent = reader.IsDBNull(3) ? null : reader.GetString(3),
            // SEC-021: hydrate the persisted input_data (col 4) so the read-access gate can
            // resolve the task's subject employee. Column 4 was SELECTed but never mapped
            // before, so InputData was always null and no subject could be extracted. jsonb is
            // read as its raw JSON text (the same pattern WorkTimeProjectionRepository uses),
            // then deserialized via System.Text.Json — see DeserializeInputData for why the
            // JsonElement shape matters. output_data (col 5) is intentionally still not mapped
            // (unchanged pre-existing behavior; the access decision does not read it).
            InputData = reader.IsDBNull(4) ? null : DeserializeInputData(reader.GetString(4)),
            CreatedAt = reader.GetDateTime(6),
            StartedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            CompletedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            ErrorMessage = reader.IsDBNull(9) ? null : reader.GetString(9)
        };
    }

    /// <summary>
    /// Hydrates the persisted <c>input_data</c> JSONB text back into the loosely-typed
    /// parameters dictionary. This is the exact inverse of <see cref="PersistTaskAsync"/>'s
    /// <c>JsonSerializer.Serialize(task.InputData)</c>.
    ///
    /// <para>
    /// <b>Why System.Text.Json specifically (SEC-021):</b> deserializing to
    /// <c>Dictionary&lt;string, object&gt;</c> with System.Text.Json makes every value a
    /// <see cref="JsonElement"/>, including a nested <c>profile</c> object. The read-access
    /// gate's <c>rule-evaluation</c> subject extraction
    /// (<see cref="OrchestratorScopeHelpers.ExtractEmployeeId"/> →
    /// <c>TryReadNestedProfileEmployeeId</c>) requires the <c>profile</c> value to be a
    /// <see cref="JsonElement"/> of <see cref="JsonValueKind.Object"/>. Hydrating to a nested
    /// <c>Dictionary</c> instead would make that extraction silently return null and fail the
    /// gate closed (a 404 for a legitimately in-scope caller). Exposed <c>static</c> and pure so
    /// the serialize → persist → hydrate → extract round-trip is unit-testable without a database.
    /// </para>
    /// </summary>
    public static Dictionary<string, object>? DeserializeInputData(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        // Fail-closed on any stored JSON that is not a deserializable object (SEC-021 Step-5a
        // Codex BLOCKER). System.Text.Json throws JsonException when the JSONB text is valid
        // JSON but NOT an object (an array or scalar) or is malformed. Left to propagate it
        // would surface as a 500 from the read handler BEFORE the access check runs — the spec
        // requires malformed stored input to fail CLOSED (a null subject → a 404 deny for a
        // non-admin), never a 500. Unreachable via the sole writer (PersistTaskAsync always
        // serializes a Dictionary), but structural: a future raw-INSERT/migration could persist
        // a non-object row. Returning null routes it through the gate's fail-closed path;
        // GlobalAdmin is unaffected because the bypass is decided before hydration matters.
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ResolveAgent(string taskType)
    {
        return taskType switch
        {
            "rule-evaluation" => "RuleEngineAgent",
            "weekly-calculation" => "WeeklyCalculationAgent",
            "payroll-export" => "PayrollAgent",
            "external-integration" => "IntegrationAgent",
            _ => null
        };
    }

    private async Task PersistTaskAsync(OrchestratorTask task, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO orchestrator_tasks (task_id, task_type, status, input_data, created_at)
            VALUES (@taskId, @taskType, @status, @inputData::jsonb, @createdAt)
            """, conn);
        cmd.Parameters.AddWithValue("taskId", task.TaskId);
        cmd.Parameters.AddWithValue("taskType", task.TaskType);
        cmd.Parameters.AddWithValue("status", task.Status);
        cmd.Parameters.AddWithValue("inputData",
            task.InputData is not null ? JsonSerializer.Serialize(task.InputData) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("createdAt", task.CreatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpdateTaskAsync(OrchestratorTask task, CancellationToken ct)
    {
        await using var conn = _connectionFactory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE orchestrator_tasks SET
                status = @status,
                assigned_agent = @agent,
                output_data = @outputData::jsonb,
                started_at = @startedAt,
                completed_at = @completedAt,
                error_message = @errorMessage
            WHERE task_id = @taskId
            """, conn);
        cmd.Parameters.AddWithValue("taskId", task.TaskId);
        cmd.Parameters.AddWithValue("status", task.Status);
        cmd.Parameters.AddWithValue("agent", (object?)task.AssignedAgent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("outputData",
            task.OutputData is not null ? JsonSerializer.Serialize(task.OutputData) : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("startedAt", (object?)task.StartedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("completedAt", (object?)task.CompletedAt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("errorMessage", (object?)task.ErrorMessage ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
