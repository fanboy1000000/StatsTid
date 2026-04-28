using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.Orchestrator.Contracts;
using StatsTid.Orchestrator.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

builder.Services.AddSingleton(new DbConnectionFactory(connectionString));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<TaskDispatcher>();
builder.Services.AddSingleton<OutputValidator>();
builder.Services.AddSingleton<WeeklyCalculationPipeline>();
builder.Services.AddSingleton<OrchestratorControlLoop>();

// Resource-scope enforcement (TASK-1901): orchestrator must validate that the
// caller's scope covers the target employeeId in the request parameters BEFORE
// any task record is persisted. Pulls in the same repositories the Backend uses.
builder.Services.AddSingleton<OrganizationRepository>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<OrgScopeValidator>();

builder.Services.AddStatsTidJwtAuth(builder.Configuration, builder.Environment);
builder.Services.AddStatsTidPolicies();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "orchestrator" }));

// /execute runs scoped per-user tasks (rule-evaluation, weekly-calculation, etc.).
// TASK-1901: the caller's scope MUST cover the target employee in the request
// parameters before a task record is persisted. The previous comment claimed
// downstream Backend scope checks were sufficient, but Codex proved that a task
// record is still created and audited against the attacker-chosen target before
// any downstream rejection — orchestrator-layer audit-log poisoning. Admin-only
// workloads (payroll export, retroactive correction) go directly to the payroll
// service under GlobalAdminOnly, not through this endpoint.
app.MapPost("/api/orchestrator/execute", async (
    ExecuteRequest request,
    OrchestratorControlLoop loop,
    OrgScopeValidator scopeValidator,
    HttpContext context,
    CancellationToken ct) =>
{
    var actor = context.GetActorContext();
    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();

    var decision = await OrchestratorScopeHelpers.EvaluateAccessAsync(
        request,
        (id, c) => scopeValidator.ValidateEmployeeAccessAsync(actor, id, c),
        ct);
    if (!decision.Allowed)
        return Results.Json(decision.ErrorBody, statusCode: decision.StatusCode);

    var task = await loop.ExecuteAsync(request, authHeader, actor.CorrelationId, ct);
    return task.Status == "completed" ? Results.Ok(task) : Results.UnprocessableEntity(task);
}).RequireAuthorization("EmployeeOrAbove");

app.MapGet("/api/orchestrator/tasks/{id:guid}", async (Guid id, OrchestratorControlLoop loop, CancellationToken ct) =>
{
    var task = await loop.GetTaskAsync(id, ct);
    return task is not null ? Results.Ok(task) : Results.NotFound();
}).RequireAuthorization("EmployeeOrAbove");

app.Run();
