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

builder.Services.AddStatsTidJwtAuth(builder.Configuration);
builder.Services.AddStatsTidPolicies();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "orchestrator" }));

// /execute runs scoped per-user tasks (rule-evaluation, weekly-calculation) on behalf of
// the calling Backend endpoint, which has already enforced per-actor org/self scope.
// Admin-only workloads (payroll export, retroactive correction) go directly to the
// payroll service under GlobalAdminOnly, not through this endpoint.
app.MapPost("/api/orchestrator/execute", async (ExecuteRequest request, OrchestratorControlLoop loop, HttpContext context, CancellationToken ct) =>
{
    var actor = context.GetActorContext();
    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();

    var task = await loop.ExecuteAsync(request, authHeader, actor.CorrelationId, ct);
    return task.Status == "completed" ? Results.Ok(task) : Results.UnprocessableEntity(task);
}).RequireAuthorization("EmployeeOrAbove");

app.MapGet("/api/orchestrator/tasks/{id:guid}", async (Guid id, OrchestratorControlLoop loop, CancellationToken ct) =>
{
    var task = await loop.GetTaskAsync(id, ct);
    return task is not null ? Results.Ok(task) : Results.NotFound();
}).RequireAuthorization("EmployeeOrAbove");

app.Run();
