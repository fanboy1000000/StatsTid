using StatsTid.Auth;
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

// SEC-021 (owner ruling: Option A) — per-task scope check on the read path. Previously this
// endpoint returned ANY task to ANY EmployeeOrAbove caller with no ownership/scope check: an
// IDOR (insecure direct object reference) letting any authenticated user read any task by id.
// The floor stays at EmployeeOrAbove (which also enables a future non-admin "read your own
// task" workflow); the per-task gate below decides visibility.
//
// Deliberate 404-not-403 posture: BOTH the not-found case AND every access denial return 404,
// so an unauthorized caller cannot distinguish "task exists but you may not see it" from "no
// such task" — a read IDOR must not confirm existence. (Contrast /execute above, which 403s a
// caller-supplied ACTION it refuses to perform; there is no existing resource to hide there.)
app.MapGet("/api/orchestrator/tasks/{id:guid}", async (
    Guid id,
    OrchestratorControlLoop loop,
    OrgScopeValidator scopeValidator,
    HttpContext context,
    CancellationToken ct) =>
{
    var task = await loop.GetTaskAsync(id, ct);
    if (task is null)
        return Results.NotFound();

    var actor = context.GetActorContext();

    // GlobalAdmin is decided FIRST, from the actor's claims only (no subject DB lookup) — the
    // SEC-021 terminated-subject defect fix. Do NOT route a GlobalAdmin through
    // ValidateEmployeeAccessAsync: that validator resolves the ACTIVE subject before its
    // GLOBAL-scope check, so a task about a since-terminated subject would be denied even to a
    // GlobalAdmin. See OrchestratorScopeHelpers.IsGlobalAdmin / EvaluateReadAccessAsync.
    var decision = await OrchestratorScopeHelpers.EvaluateReadAccessAsync(
        task,
        OrchestratorScopeHelpers.IsGlobalAdmin(actor),
        (employeeId, c) => scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, c),
        ct);

    return decision.Allowed ? Results.Ok(task) : Results.NotFound();
}).RequireAuthorization("EmployeeOrAbove");

app.Run();
