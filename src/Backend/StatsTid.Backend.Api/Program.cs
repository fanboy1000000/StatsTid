using System.Text.Json;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Backend.Api.Validation;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Security;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

builder.Services.AddSingleton(new DbConnectionFactory(connectionString));
builder.Services.AddSingleton<IEventStore, PostgresEventStore>();
builder.Services.AddHttpClient();

builder.Services.AddStatsTidJwtAuth(builder.Configuration);
builder.Services.AddStatsTidPolicies();
builder.Services.AddSingleton<AuditLogRepository>();
builder.Services.AddSingleton<JwtTokenService>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditLoggingMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "backend-api" }));

// ── Authentication ──

app.MapPost("/api/auth/login", (LoginRequest request, JwtTokenService tokenService) =>
{
    var users = new Dictionary<string, (string Name, string Role, string AgreementCode, string Password)>
    {
        ["admin01"] = ("Administrator", StatsTidRoles.Admin, "AC", "admin"),
        ["mgr01"] = ("Manager One", StatsTidRoles.Manager, "HK", "manager"),
        ["emp001"] = ("Employee AC", StatsTidRoles.Employee, "AC", "employee"),
        ["emp002"] = ("Employee HK", StatsTidRoles.Employee, "HK", "employee"),
        ["emp003"] = ("Employee PROSA", StatsTidRoles.Employee, "PROSA", "employee"),
        ["readonly01"] = ("ReadOnly User", StatsTidRoles.ReadOnly, "AC", "readonly"),
    };

    if (!users.TryGetValue(request.Username, out var user) || request.Password != user.Password)
        return Results.Unauthorized();

    var token = tokenService.GenerateToken(request.Username, user.Name, user.Role, user.AgreementCode);
    var expiration = DateTime.UtcNow.AddMinutes(480);

    return Results.Ok(new LoginResponse
    {
        Token = token,
        ExpiresAt = expiration,
        EmployeeId = request.Username,
        Role = user.Role
    });
});

// ── Time Entries ──

app.MapPost("/api/time-entries", async (RegisterTimeEntryRequest request, IEventStore eventStore, HttpContext context, CancellationToken ct) =>
{
    var actor = context.GetActorContext();

    // Ownership enforcement: Employee can only create for themselves
    if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
        return Results.Forbid();

    // Input validation
    var (isValid, error) = RequestValidator.ValidateTimeEntry(request.EmployeeId, request.Hours, request.AgreementCode, request.OkVersion);
    if (!isValid)
        return Results.BadRequest(new { error });

    var @event = new TimeEntryRegistered
    {
        EmployeeId = request.EmployeeId,
        Date = request.Date,
        Hours = request.Hours,
        StartTime = request.StartTime,
        EndTime = request.EndTime,
        TaskId = request.TaskId,
        ActivityType = request.ActivityType,
        AgreementCode = request.AgreementCode,
        OkVersion = request.OkVersion,
        ActorId = actor.ActorId,
        ActorRole = actor.ActorRole,
        CorrelationId = actor.CorrelationId
    };

    var streamId = $"employee-{request.EmployeeId}";
    await eventStore.AppendAsync(streamId, @event, ct);

    return Results.Created($"/api/time-entries/{request.EmployeeId}", new { eventId = @event.EventId, streamId });
}).RequireAuthorization("EmployeeOrAbove");

app.MapGet("/api/time-entries/{employeeId}", async (string employeeId, IEventStore eventStore, CancellationToken ct) =>
{
    var streamId = $"employee-{employeeId}";
    var events = await eventStore.ReadStreamAsync(streamId, ct);

    var entries = events.OfType<TimeEntryRegistered>().Select(e => new TimeEntry
    {
        EmployeeId = e.EmployeeId,
        Date = e.Date,
        Hours = e.Hours,
        StartTime = e.StartTime,
        EndTime = e.EndTime,
        TaskId = e.TaskId,
        ActivityType = e.ActivityType,
        AgreementCode = e.AgreementCode,
        OkVersion = e.OkVersion,
        RegisteredAt = e.OccurredAt
    }).ToList();

    return Results.Ok(entries);
}).RequireAuthorization("Authenticated");

// ── Absences ──

app.MapPost("/api/absences", async (RegisterAbsenceRequest request, IEventStore eventStore, HttpContext context, CancellationToken ct) =>
{
    var actor = context.GetActorContext();

    // Ownership enforcement: Employee can only create for themselves
    if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
        return Results.Forbid();

    // Input validation
    var (isValid, error) = RequestValidator.ValidateAbsence(request.EmployeeId, request.Hours, request.AbsenceType, request.AgreementCode, request.OkVersion);
    if (!isValid)
        return Results.BadRequest(new { error });

    var @event = new AbsenceRegistered
    {
        EmployeeId = request.EmployeeId,
        Date = request.Date,
        AbsenceType = request.AbsenceType,
        Hours = request.Hours,
        AgreementCode = request.AgreementCode,
        OkVersion = request.OkVersion,
        ActorId = actor.ActorId,
        ActorRole = actor.ActorRole,
        CorrelationId = actor.CorrelationId
    };

    var streamId = $"employee-{request.EmployeeId}";
    await eventStore.AppendAsync(streamId, @event, ct);

    return Results.Created($"/api/absences/{request.EmployeeId}", new { eventId = @event.EventId, streamId });
}).RequireAuthorization("EmployeeOrAbove");

app.MapGet("/api/absences/{employeeId}", async (string employeeId, IEventStore eventStore, CancellationToken ct) =>
{
    var streamId = $"employee-{employeeId}";
    var events = await eventStore.ReadStreamAsync(streamId, ct);

    var absences = events.OfType<AbsenceRegistered>().Select(e => new AbsenceEntry
    {
        EmployeeId = e.EmployeeId,
        Date = e.Date,
        AbsenceType = e.AbsenceType,
        Hours = e.Hours,
        AgreementCode = e.AgreementCode,
        OkVersion = e.OkVersion
    }).ToList();

    return Results.Ok(absences);
}).RequireAuthorization("Authenticated");

// ── Flex Balance ──

app.MapGet("/api/flex-balance/{employeeId}", async (string employeeId, IEventStore eventStore, CancellationToken ct) =>
{
    var streamId = $"employee-{employeeId}";
    var events = await eventStore.ReadStreamAsync(streamId, ct);

    var latest = events.OfType<FlexBalanceUpdated>().LastOrDefault();

    if (latest is null)
        return Results.Ok(new { employeeId, balance = 0m, message = "No flex balance events found" });

    return Results.Ok(new
    {
        employeeId,
        balance = latest.NewBalance,
        previousBalance = latest.PreviousBalance,
        delta = latest.Delta,
        reason = latest.Reason
    });
}).RequireAuthorization("Authenticated");

// ── Weekly Calculation (composite) ──

app.MapPost("/api/time-entries/calculate", async (
    CalculateRequest request,
    IEventStore eventStore,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    HttpContext context,
    CancellationToken ct) =>
{
    var actor = context.GetActorContext();

    // Ownership enforcement: Employee can only calculate for themselves
    if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
        return Results.Forbid();

    // Input validation
    var (isValid, error) = RequestValidator.ValidateTimeEntry(request.EmployeeId, request.WeeklyNormHours, request.AgreementCode, request.OkVersion);
    if (!isValid)
        return Results.BadRequest(new { error });

    if (request.PartTimeFraction is > 0 and <= 1)
    {
        // valid
    }
    else
    {
        var (ptValid, ptError) = RequestValidator.ValidatePartTimeFraction(request.PartTimeFraction);
        if (!ptValid)
            return Results.BadRequest(new { error = ptError });
    }

    var orchestratorUrl = configuration["ServiceUrls:Orchestrator"]
        ?? "http://orchestrator:8080";

    var payload = new
    {
        taskType = "rule-evaluation",
        parameters = new Dictionary<string, object>
        {
            ["ruleId"] = "NORM_CHECK_37H",
            ["profile"] = new EmploymentProfile
            {
                EmployeeId = request.EmployeeId,
                AgreementCode = request.AgreementCode,
                OkVersion = request.OkVersion,
                WeeklyNormHours = request.WeeklyNormHours,
                EmploymentCategory = "Standard",
                PartTimeFraction = request.PartTimeFraction
            },
            ["periodStart"] = request.PeriodStart.ToString("yyyy-MM-dd"),
            ["periodEnd"] = request.PeriodEnd.ToString("yyyy-MM-dd")
        }
    };

    var client = httpClientFactory.CreateClient();
    // Propagate auth and correlation headers
    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
    if (authHeader is not null)
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);
    client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", actor.CorrelationId.ToString());

    var response = await client.PostAsJsonAsync($"{orchestratorUrl}/api/orchestrator/execute", payload, ct);
    var body = await response.Content.ReadAsStringAsync(ct);

    return response.IsSuccessStatusCode
        ? Results.Ok(JsonSerializer.Deserialize<object>(body))
        : Results.UnprocessableEntity(JsonSerializer.Deserialize<object>(body));
}).RequireAuthorization("EmployeeOrAbove");

app.MapPost("/api/time-entries/calculate-week", async (
    WeeklyCalculateRequest request,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    HttpContext context,
    CancellationToken ct) =>
{
    var actor = context.GetActorContext();

    // Ownership enforcement: Employee can only calculate for themselves
    if (actor.ActorRole == StatsTidRoles.Employee && request.EmployeeId != actor.ActorId)
        return Results.Forbid();

    var orchestratorUrl = configuration["ServiceUrls:Orchestrator"]
        ?? "http://orchestrator:8080";

    var weekEnd = request.WeekStartDate.AddDays(6);

    var payload = new
    {
        taskType = "weekly-calculation",
        parameters = new Dictionary<string, object>
        {
            ["employeeId"] = request.EmployeeId,
            ["agreementCode"] = request.AgreementCode,
            ["okVersion"] = request.OkVersion,
            ["periodStart"] = request.WeekStartDate.ToString("yyyy-MM-dd"),
            ["periodEnd"] = weekEnd.ToString("yyyy-MM-dd"),
            ["weeklyNormHours"] = request.WeeklyNormHours,
            ["partTimeFraction"] = request.PartTimeFraction,
            ["previousFlexBalance"] = request.PreviousFlexBalance
        }
    };

    var client = httpClientFactory.CreateClient();
    // Propagate auth and correlation headers
    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
    if (authHeader is not null)
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);
    client.DefaultRequestHeaders.TryAddWithoutValidation("X-Correlation-Id", actor.CorrelationId.ToString());

    var response = await client.PostAsJsonAsync($"{orchestratorUrl}/api/orchestrator/execute", payload, ct);
    var body = await response.Content.ReadAsStringAsync(ct);

    return response.IsSuccessStatusCode
        ? Results.Ok(JsonSerializer.Deserialize<object>(body))
        : Results.UnprocessableEntity(JsonSerializer.Deserialize<object>(body));
}).RequireAuthorization("EmployeeOrAbove");

app.Run();
