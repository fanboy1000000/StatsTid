using StatsTid.Backend.Api.Endpoints;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Interfaces;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

// ── Infrastructure ──
builder.Services.AddSingleton(new DbConnectionFactory(connectionString));
builder.Services.AddSingleton<IEventStore, PostgresEventStore>();
builder.Services.AddHttpClient();

// ── Security ──
builder.Services.AddStatsTidJwtAuth(builder.Configuration);
builder.Services.AddStatsTidPolicies();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<OrgScopeValidator>();

// ── Repositories ──
builder.Services.AddSingleton<AuditLogRepository>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<RoleAssignmentRepository>();
builder.Services.AddSingleton<OrganizationRepository>();
builder.Services.AddSingleton<LocalConfigurationRepository>();
builder.Services.AddSingleton<ApprovalPeriodRepository>();
builder.Services.AddSingleton<ProjectRepository>();
builder.Services.AddSingleton<TimerSessionRepository>();
builder.Services.AddSingleton<AbsenceTypeVisibilityRepository>();
builder.Services.AddSingleton<AgreementConfigRepository>();
builder.Services.AddSingleton<PositionOverrideRepository>();
builder.Services.AddSingleton<WageTypeMappingRepository>();
builder.Services.AddSingleton<EntitlementConfigRepository>();
builder.Services.AddSingleton<EntitlementBalanceRepository>();

// ── Services ──
builder.Services.AddSingleton<ConfigResolutionService>();

var useDbAuth = builder.Configuration.GetValue<bool>("Auth:UseDatabase", false);

var app = builder.Build();

// ── Seed agreement configs from static data if DB is empty (ADR-014) ──
using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<AgreementConfigRepository>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await AgreementConfigSeeder.SeedAsync(repo, logger);
}

// ── Seed entitlement configs if DB is empty ──
{
    var dbFactory = app.Services.GetRequiredService<DbConnectionFactory>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    await EntitlementConfigSeeder.SeedAsync(dbFactory, logger);
}

// ── Middleware ──
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditLoggingMiddleware>();

// ── Health ──
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "backend-api" }));

// ── Endpoint Groups ──
app.MapAuthEndpoints(useDbAuth);
app.MapTimeEndpoints();
app.MapAdminEndpoints();
app.MapApprovalEndpoints();
app.MapConfigEndpoints();
app.MapSkemaEndpoints();
app.MapTimerEndpoints();
app.MapProjectEndpoints();
app.MapAgreementConfigEndpoints();
app.MapPositionOverrideEndpoints();
app.MapWageTypeMappingEndpoints();
app.MapBalanceEndpoints();

app.Run();
