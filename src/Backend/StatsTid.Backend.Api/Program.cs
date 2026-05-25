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
builder.Services.AddSingleton<ReportingLineRepository>();
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
// S44b TASK-44B-02..05 — 16 IAuditProjectionMapper<T> + 16 RegisteredAuditEventType marker pairs.
// AgreementConfig family (5)
builder.Services.AddSingleton<IAuditProjectionMapper<AgreementConfigCreated>, AgreementConfigCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(AgreementConfigCreated), nameof(AgreementConfigCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<AgreementConfigUpdated>, AgreementConfigUpdatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(AgreementConfigUpdated), nameof(AgreementConfigUpdated)));
builder.Services.AddSingleton<IAuditProjectionMapper<AgreementConfigPublished>, AgreementConfigPublishedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(AgreementConfigPublished), nameof(AgreementConfigPublished)));
builder.Services.AddSingleton<IAuditProjectionMapper<AgreementConfigArchived>, AgreementConfigArchivedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(AgreementConfigArchived), nameof(AgreementConfigArchived)));
builder.Services.AddSingleton<IAuditProjectionMapper<AgreementConfigCloned>, AgreementConfigClonedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(AgreementConfigCloned), nameof(AgreementConfigCloned)));
// Period family (5)
builder.Services.AddSingleton<IAuditProjectionMapper<PeriodSubmitted>, PeriodSubmittedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(PeriodSubmitted), nameof(PeriodSubmitted)));
builder.Services.AddSingleton<IAuditProjectionMapper<PeriodApproved>, PeriodApprovedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(PeriodApproved), nameof(PeriodApproved)));
builder.Services.AddSingleton<IAuditProjectionMapper<PeriodRejected>, PeriodRejectedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(PeriodRejected), nameof(PeriodRejected)));
builder.Services.AddSingleton<IAuditProjectionMapper<PeriodEmployeeApproved>, PeriodEmployeeApprovedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(PeriodEmployeeApproved), nameof(PeriodEmployeeApproved)));
builder.Services.AddSingleton<IAuditProjectionMapper<PeriodReopened>, PeriodReopenedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(PeriodReopened), nameof(PeriodReopened)));
// Overtime family (3)
builder.Services.AddSingleton<IAuditProjectionMapper<OvertimePreApprovalCreated>, OvertimePreApprovalCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(OvertimePreApprovalCreated), nameof(OvertimePreApprovalCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<OvertimePreApprovalApproved>, OvertimePreApprovalApprovedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(OvertimePreApprovalApproved), nameof(OvertimePreApprovalApproved)));
builder.Services.AddSingleton<IAuditProjectionMapper<OvertimePreApprovalRejected>, OvertimePreApprovalRejectedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(OvertimePreApprovalRejected), nameof(OvertimePreApprovalRejected)));
// UserAgreementCode family (3)
builder.Services.AddSingleton<IAuditProjectionMapper<UserAgreementCodeChanged>, UserAgreementCodeChangedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UserAgreementCodeChanged), nameof(UserAgreementCodeChanged)));
builder.Services.AddSingleton<IAuditProjectionMapper<UserAgreementCodeSeeded>, UserAgreementCodeSeededAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UserAgreementCodeSeeded), nameof(UserAgreementCodeSeeded)));
builder.Services.AddSingleton<IAuditProjectionMapper<UserAgreementCodeSuperseded>, UserAgreementCodeSupersededAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UserAgreementCodeSuperseded), nameof(UserAgreementCodeSuperseded)));
// S44c TASK-44C-01..06 — 25 IAuditProjectionMapper<T> + 25 RegisteredAuditEventType marker pairs.
// PositionOverride family (4)
builder.Services.AddSingleton<IAuditProjectionMapper<PositionOverrideCreated>, PositionOverrideCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(PositionOverrideCreated), nameof(PositionOverrideCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<PositionOverrideUpdated>, PositionOverrideUpdatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(PositionOverrideUpdated), nameof(PositionOverrideUpdated)));
builder.Services.AddSingleton<IAuditProjectionMapper<PositionOverrideActivated>, PositionOverrideActivatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(PositionOverrideActivated), nameof(PositionOverrideActivated)));
builder.Services.AddSingleton<IAuditProjectionMapper<PositionOverrideDeactivated>, PositionOverrideDeactivatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(PositionOverrideDeactivated), nameof(PositionOverrideDeactivated)));
// WageTypeMapping family (4)
builder.Services.AddSingleton<IAuditProjectionMapper<WageTypeMappingCreated>, WageTypeMappingCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(WageTypeMappingCreated), nameof(WageTypeMappingCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<WageTypeMappingUpdated>, WageTypeMappingUpdatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(WageTypeMappingUpdated), nameof(WageTypeMappingUpdated)));
builder.Services.AddSingleton<IAuditProjectionMapper<WageTypeMappingDeleted>, WageTypeMappingDeletedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(WageTypeMappingDeleted), nameof(WageTypeMappingDeleted)));
builder.Services.AddSingleton<IAuditProjectionMapper<WageTypeMappingSuperseded>, WageTypeMappingSupersededAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(WageTypeMappingSuperseded), nameof(WageTypeMappingSuperseded)));
// EntitlementConfig family (4 — incl Seeded, mapper-only)
builder.Services.AddSingleton<IAuditProjectionMapper<EntitlementConfigSeeded>, EntitlementConfigSeededAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EntitlementConfigSeeded), nameof(EntitlementConfigSeeded)));
builder.Services.AddSingleton<IAuditProjectionMapper<EntitlementConfigCreated>, EntitlementConfigCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EntitlementConfigCreated), nameof(EntitlementConfigCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<EntitlementConfigSuperseded>, EntitlementConfigSupersededAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EntitlementConfigSuperseded), nameof(EntitlementConfigSuperseded)));
builder.Services.AddSingleton<IAuditProjectionMapper<EntitlementConfigSoftDeleted>, EntitlementConfigSoftDeletedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EntitlementConfigSoftDeleted), nameof(EntitlementConfigSoftDeleted)));
// EmployeeProfile family (4)
builder.Services.AddSingleton<IAuditProjectionMapper<EmployeeProfileCreated>, EmployeeProfileCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EmployeeProfileCreated), nameof(EmployeeProfileCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<EmployeeProfileUpdated>, EmployeeProfileUpdatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EmployeeProfileUpdated), nameof(EmployeeProfileUpdated)));
builder.Services.AddSingleton<IAuditProjectionMapper<EmployeeProfileSuperseded>, EmployeeProfileSupersededAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EmployeeProfileSuperseded), nameof(EmployeeProfileSuperseded)));
builder.Services.AddSingleton<IAuditProjectionMapper<EmployeeProfileSoftDeleted>, EmployeeProfileSoftDeletedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EmployeeProfileSoftDeleted), nameof(EmployeeProfileSoftDeleted)));
// Local config family (2 — LocalConfigurationChanged is mapper-only)
builder.Services.AddSingleton<IAuditProjectionMapper<LocalAgreementProfileChanged>, LocalAgreementProfileChangedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(LocalAgreementProfileChanged), nameof(LocalAgreementProfileChanged)));
builder.Services.AddSingleton<IAuditProjectionMapper<LocalConfigurationChanged>, LocalConfigurationChangedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(LocalConfigurationChanged), nameof(LocalConfigurationChanged)));
// ADR-024 events (7 — all mapper-only, no emit sites)
builder.Services.AddSingleton<IAuditProjectionMapper<RoleConfigOverrideCreated>, RoleConfigOverrideCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(RoleConfigOverrideCreated), nameof(RoleConfigOverrideCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<RoleConfigOverrideUpdated>, RoleConfigOverrideUpdatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(RoleConfigOverrideUpdated), nameof(RoleConfigOverrideUpdated)));
builder.Services.AddSingleton<IAuditProjectionMapper<RoleConfigOverrideSuperseded>, RoleConfigOverrideSupersededAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(RoleConfigOverrideSuperseded), nameof(RoleConfigOverrideSuperseded)));
builder.Services.AddSingleton<IAuditProjectionMapper<RoleConfigOverrideSoftDeleted>, RoleConfigOverrideSoftDeletedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(RoleConfigOverrideSoftDeleted), nameof(RoleConfigOverrideSoftDeleted)));
builder.Services.AddSingleton<IAuditProjectionMapper<MerarbejdeDiscretionary>, MerarbejdeDiscretionaryAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(MerarbejdeDiscretionary), nameof(MerarbejdeDiscretionary)));
builder.Services.AddSingleton<IAuditProjectionMapper<OvertimeNecessityAcknowledged>, OvertimeNecessityAcknowledgedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(OvertimeNecessityAcknowledged), nameof(OvertimeNecessityAcknowledged)));
builder.Services.AddSingleton<IAuditProjectionMapper<ConfigBugCorrected>, ConfigBugCorrectedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(ConfigBugCorrected), nameof(ConfigBugCorrected)));
// S45 — cross-process mapper (lives in Infrastructure, not Backend.Api)
builder.Services.AddSingleton<IAuditProjectionMapper<RetroactiveCorrectionRequested>, StatsTid.Infrastructure.AuditMappers.RetroactiveCorrectionRequestedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(RetroactiveCorrectionRequested), nameof(RetroactiveCorrectionRequested)));
// S48 ADR-027 — reporting-line audit mappers
builder.Services.AddSingleton<IAuditProjectionMapper<ReportingLineAssigned>, ReportingLineAssignedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(ReportingLineAssigned), nameof(ReportingLineAssigned)));
builder.Services.AddSingleton<IAuditProjectionMapper<ReportingLineSuperseded>, ReportingLineSupersededAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(ReportingLineSuperseded), nameof(ReportingLineSuperseded)));

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
app.MapAgreementEntitlementEndpoints();
app.MapEmployeeProfileEndpoints();
app.MapBalanceEndpoints();
app.MapComplianceEndpoints();
app.MapOvertimeEndpoints();
app.MapAuditEndpoints();
app.MapReportingLineEndpoints();

app.Run();

// Marker for Microsoft.AspNetCore.Mvc.Testing's WebApplicationFactory<TEntryPoint>
// to anchor on the top-level-statements compilation unit. Required because top-level
// statements emit an internal Program class by default, which the test assembly cannot
// reference. This is the documented industry-standard pattern (Microsoft docs).
public partial class Program { }
