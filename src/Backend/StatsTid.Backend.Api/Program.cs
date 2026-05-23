using StatsTid.Auth;
using StatsTid.Backend.Api.AuditMappers;
using StatsTid.Backend.Api.Endpoints;
using StatsTid.Backend.Api.Validators;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Audit;
using StatsTid.Infrastructure.Outbox;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Audit;
using StatsTid.SharedKernel.Events;
using StatsTid.SharedKernel.Interfaces;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("EventStore")
    ?? "Host=localhost;Port=5432;Database=statstid;Username=statstid;Password=statstid_dev";

// ── Infrastructure ──
builder.Services.AddSingleton(new DbConnectionFactory(connectionString));

// ── Outbox: dual-binding per ADR-018 D3 + per-service publisher per D2/D6 ──
// PostgresEventStore is the single concrete implementing both IEventStore (read +
// publisher-side append) and IOutboxEnqueue (state-change-site in-tx enqueue).
// The OutboxServiceContext stamps service_id on each enqueued row so the per-
// service OutboxPublisher partitions cleanly. Backend.Api owns Backend's streams
// per ADR-018 D6 stream-ownership table.
builder.Services.AddSingleton(new OutboxServiceContext("backend-api"));
builder.Services.AddSingleton<PostgresEventStore>(sp => new PostgresEventStore(
    sp.GetRequiredService<DbConnectionFactory>(),
    sp.GetRequiredService<OutboxServiceContext>()));
builder.Services.AddSingleton<IEventStore>(sp => sp.GetRequiredService<PostgresEventStore>());
builder.Services.AddSingleton<IOutboxEnqueue>(sp => sp.GetRequiredService<PostgresEventStore>());
builder.Services.AddHostedService<OutboxPublisher>();

builder.Services.AddHttpClient();

// ── Security ──
builder.Services.AddStatsTidJwtAuth(builder.Configuration, builder.Environment);
builder.Services.AddStatsTidPolicies();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<OrgScopeValidator>();

// ── Repositories ──
builder.Services.AddSingleton<AuditLogRepository>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<RoleAssignmentRepository>();
builder.Services.AddSingleton<OrganizationRepository>();
builder.Services.AddSingleton<LocalConfigurationRepository>();
builder.Services.AddSingleton<LocalAgreementProfileRepository>();
builder.Services.AddSingleton<ApprovalPeriodRepository>();
builder.Services.AddSingleton<ProjectRepository>();
builder.Services.AddSingleton<TimerSessionRepository>();
builder.Services.AddSingleton<AbsenceTypeVisibilityRepository>();
builder.Services.AddSingleton<AgreementConfigRepository>();
builder.Services.AddSingleton<PositionOverrideRepository>();
builder.Services.AddSingleton<WageTypeMappingRepository>();
builder.Services.AddSingleton<EntitlementConfigRepository>();
builder.Services.AddSingleton<EmployeeProfileRepository>();
builder.Services.AddSingleton<UserAgreementCodeRepository>();
builder.Services.AddSingleton<IEmploymentProfileResolver, EmploymentProfileResolver>();
builder.Services.AddSingleton<EntitlementBalanceRepository>();
builder.Services.AddSingleton<TimeEntryProjectionRepository>();
builder.Services.AddSingleton<AbsenceProjectionRepository>();
builder.Services.AddSingleton<CompensatoryRestRepository>();
builder.Services.AddSingleton<OvertimeBalanceRepository>();
builder.Services.AddSingleton<OvertimePreApprovalRepository>();
builder.Services.AddSingleton<AuditProjectionRepository>();
builder.Services.AddSingleton<IAuditProjectionMapperRegistry, AuditProjectionMapperRegistry>();
// S44 TASK-4407..4412 — 6 IAuditProjectionMapper<T> + 6 RegisteredAuditEventType marker pairs.
// Mapper + marker registered together so the registry's RegisteredEventTypeNames filter
// matches the set of mappers actually wired. Adding 6 mappers without their marker would
// leave the backfill blind to them; adding markers without mappers would log NoMapper.
builder.Services.AddSingleton<IAuditProjectionMapper<OrganizationCreated>, OrganizationCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(OrganizationCreated), nameof(OrganizationCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<OrganizationUpdated>, OrganizationUpdatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(OrganizationUpdated), nameof(OrganizationUpdated)));
builder.Services.AddSingleton<IAuditProjectionMapper<UserCreated>, UserCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UserCreated), nameof(UserCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<UserUpdated>, UserUpdatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UserUpdated), nameof(UserUpdated)));
builder.Services.AddSingleton<IAuditProjectionMapper<RoleAssignmentGranted>, RoleAssignmentGrantedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(RoleAssignmentGranted), nameof(RoleAssignmentGranted)));
builder.Services.AddSingleton<IAuditProjectionMapper<RoleAssignmentRevoked>, RoleAssignmentRevokedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(RoleAssignmentRevoked), nameof(RoleAssignmentRevoked)));

// ── Services ──
builder.Services.AddSingleton<ConfigResolutionService>();
builder.Services.AddSingleton<ProfileAlignmentValidator>();
builder.Services.AddSingleton<ProjectionBackfillService>();
builder.Services.AddSingleton<AuditProjectionBackfillService>();

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

// ── S31 / TASK-3106: seed employee_profiles for any user lacking a live row ──
// Runs AFTER agreement/entitlement seeders + AFTER init.sql users seed.
// Idempotent: NOT EXISTS predicate skips users with existing live profiles.
// Each new row commits with an EmployeeProfileCreated outbox event atomically
// (ADR-018 D5). Default values (weekly_norm_hours=37.0, part_time_fraction=1.0,
// position=NULL) — admins re-enter correct values post-S31 via the new
// /api/admin/employee-profiles/{employeeId} PUT (TASK-3107).
{
    var dbFactory = app.Services.GetRequiredService<DbConnectionFactory>();
    var outbox = app.Services.GetRequiredService<IOutboxEnqueue>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    await EmployeeProfileSeeder.SeedAsync(dbFactory, outbox, logger);
}

// ── S34 / TASK-3403: seed user_agreement_codes for any active user lacking a live row ──
// Phase 4e (ADR-023 D2 option (b)) bootstrap backfill — reads each user's current
// users.agreement_code scalar and inserts a matching live history row at
// effective_from='0001-01-01' (history-covering default per S33 Step 7a cycle 1
// absorption; stamping today would leave pre-deployment periods uncovered and
// break replay determinism). Idempotent NOT EXISTS predicate + per-row atomic tx
// (row INSERT + audit CREATED + UserAgreementCodeSeeded outbox event). Catches
// PostgresException(SqlState=23505) on partial-unique-index race for concurrent-
// startup safety — ships the fix inline that S31 EmployeeProfileSeeder deferred
// to Phase 4e (candidate #2).
{
    var dbFactory = app.Services.GetRequiredService<DbConnectionFactory>();
    var outbox = app.Services.GetRequiredService<IOutboxEnqueue>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    await UserAgreementCodeBackfillSeeder.SeedAsync(dbFactory, outbox, logger);
}

// ── S27 Phase 4c.6 / Step 7a cycle 1 BLOCKER fix ──
// Apply projection backfill (idempotent; one-shot per startup).
// No-op when projections already mirror events. Required because the
// migrated GET handlers (TimeEndpoints, SkemaEndpoints, BalanceEndpoints,
// ComplianceEndpoints) read exclusively from projection tables; without
// this startup hook, a deploy against an existing events table would
// surface empty GETs until ops manually ran tools/ProjectionBackfill.
// Runs AFTER schema seeders (which create the projection tables) and
// BEFORE app.Run() (which serves GETs that depend on populated projections).
using (var scope = app.Services.CreateScope())
{
    var backfill = scope.ServiceProvider.GetRequiredService<ProjectionBackfillService>();
    var result = await backfill.RunAsync();
    app.Logger.LogInformation(
        "Projection backfill on startup: scanned={Scanned}, time inserted={InsertedTime}, absences inserted={InsertedAbsences}, conflicts={Conflicts}, fallback warnings={Warnings}",
        result.Scanned, result.InsertedTime, result.InsertedAbsences,
        result.ConflictsTime + result.ConflictsAbsences, result.FallbackWarnings);
}

// ── S43 / ADR-026 D7 audit projection backfill (single source of truth) ──
// Unconditional invocation per S27 precedent (no row-count gate; Step 0b
// cycle 1 BLOCKER B2 absorption — a row-count gate would prevent S44's
// newly-mappable events from backfilling once their mappers land).
// Idempotent via AuditProjectionRepository's ON CONFLICT (event_id) DO NOTHING.
// Sub-Sprint 1 (S43) has zero RegisteredAuditEventType markers → backfill
// triggers the fast-path no-op exit (Step 7a cycle 1 Codex W1 absorption).
// Sub-Sprint 2 progressively adds RegisteredAuditEventType + mapper pairs;
// each restart picks up newly-covered events idempotently.
using (var scope = app.Services.CreateScope())
{
    var auditBackfill = scope.ServiceProvider.GetRequiredService<AuditProjectionBackfillService>();
    var result = await auditBackfill.RunAsync();
    app.Logger.LogInformation(
        "Audit projection backfill on startup: scanned={Scanned}, inserted={Inserted}, conflicts={Conflicts}, noMapper={NoMapper}, nullOutboxSkipped={NullOutboxSkipped}, unknown={Unknown}, errors={Errors}",
        result.Scanned, result.Inserted, result.Conflicts, result.NoMapper,
        result.NullOutboxSkipped, result.UnknownEventTypes, result.DeserializationErrors);
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
app.MapEntitlementConfigEndpoints();
app.MapEmployeeProfileEndpoints();
app.MapBalanceEndpoints();
app.MapComplianceEndpoints();
app.MapOvertimeEndpoints();

app.Run();

// Marker for Microsoft.AspNetCore.Mvc.Testing's WebApplicationFactory<TEntryPoint>
// to anchor on the top-level-statements compilation unit. Required because top-level
// statements emit an internal Program class by default, which the test assembly cannot
// reference. This is the documented industry-standard pattern (Microsoft docs).
public partial class Program { }
