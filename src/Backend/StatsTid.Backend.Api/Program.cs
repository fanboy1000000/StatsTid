using StatsTid.Auth;
using StatsTid.Backend.Api.AuditMappers;
using StatsTid.Backend.Api.Endpoints;
using StatsTid.Backend.Api.Http;
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
builder.Services.AddHostedService<DelegationExpiryService>();
builder.Services.AddHostedService<SettlementCloseService>(); // S68 ADR-033 slice 1a — period-close poller

builder.Services.AddHttpClient();

// ── S73 / TASK-7300 (R1 + R1a) — the NAMED rule-engine client ──
// ONE mechanism for backend→rule-engine auth carriage: all four call families
// (SkemaEndpoints validate-entitlement ×2, ComplianceEndpoints check-compliance,
// OvertimeEndpoints check-overtime-governance) resolve CreateClient(RuleEngineClient.Name).
// The RuleEngineHeaderForwardingHandler forwards the inbound Authorization +
// X-Correlation-Id (FORWARD-when-actor-exists / MINT-when-no-HttpContext partition —
// see the handler doc). R1a: the handler attaches ONLY to this named client — never the
// default builder above, so non-rule-engine outbound clients never carry the user bearer.
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<RuleEngineHeaderForwardingHandler>();
builder.Services.AddHttpClient(RuleEngineClient.Name, (sp, client) =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        client.BaseAddress = new Uri(
            config[RuleEngineClient.BaseUrlConfigKey] ?? RuleEngineClient.DefaultBaseUrl);
    })
    .AddHttpMessageHandler<RuleEngineHeaderForwardingHandler>();

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
builder.Services.AddSingleton<SkemaRowPreferenceRepository>(); // S72 / TASK-7201 — R4 row-preference container + absence selections
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
builder.Services.AddSingleton<WorkTimeProjectionRepository>();
builder.Services.AddSingleton<CompensatoryRestRepository>();
builder.Services.AddSingleton<OvertimeBalanceRepository>();
builder.Services.AddSingleton<OvertimePreApprovalRepository>();
builder.Services.AddSingleton<AuditProjectionRepository>();
builder.Services.AddSingleton<ReportingLineRepository>();
builder.Services.AddSingleton<UnitRepository>(); // S104 ADR-038 D3/D8 — typed units hierarchy + leaders (structure-only, ZERO scope per D5)
builder.Services.AddSingleton<ManagerVikarRepository>(); // S74 ADR-027 Phase 5 — approver-owned vikar storage
builder.Services.AddSingleton<DesignatedApproverAuthorizer>(); // S74 / TASK-7402 — the ONE R5 canonical approve-authority predicate (A3, ADR-027 D4)
builder.Services.AddSingleton<EmployeeEntitlementEligibilityRepository>(); // S59
builder.Services.AddSingleton<VacationTransferAgreementRepository>(); // S68 ADR-033 slice 1a
builder.Services.AddSingleton<VacationSettlementRepository>(); // S68 ADR-033 slice 1a
builder.Services.AddSingleton<IAuditProjectionMapperRegistry, AuditProjectionMapperRegistry>();
// S44 TASK-4407..4412 — 6 IAuditProjectionMapper<T> + 6 RegisteredAuditEventType marker pairs.
// Mapper + marker registered together so the registry's RegisteredEventTypeNames filter
// matches the set of mappers actually wired. Adding 6 mappers without their marker would
// leave the backfill blind to them; adding markers without mappers would log NoMapper.
builder.Services.AddSingleton<IAuditProjectionMapper<OrganizationCreated>, OrganizationCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(OrganizationCreated), nameof(OrganizationCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<OrganizationUpdated>, OrganizationUpdatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(OrganizationUpdated), nameof(OrganizationUpdated)));
// S98 ADR-035 — GlobalAdmin org-structure ops (soft-delete + re-parent). TENANT_TARGETED;
// target_org_id = the org being deleted/moved (from event payload). Mapper + marker registered
// together (the registry's RegisteredEventTypeNames filter matches the wired mappers).
builder.Services.AddSingleton<IAuditProjectionMapper<OrganizationDeleted>, OrganizationDeletedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(OrganizationDeleted), nameof(OrganizationDeleted)));
builder.Services.AddSingleton<IAuditProjectionMapper<OrganizationMoved>, OrganizationMovedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(OrganizationMoved), nameof(OrganizationMoved)));
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
// S66 / TASK-6604 (ADR-032 D4) — profile-change revaluation balance event (emitted from the
// EmployeeProfile PUT tx onto the employee-{id} stream). TENANT_TARGETED; target = employee_id.
builder.Services.AddSingleton<IAuditProjectionMapper<EntitlementBalanceRevalued>, EntitlementBalanceRevaluedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EntitlementBalanceRevalued), nameof(EntitlementBalanceRevalued)));
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
// S49 TASK-4908 — bulk import audit mapper
builder.Services.AddSingleton<IAuditProjectionMapper<ReportingLineBulkImported>, ReportingLineBulkImportedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(ReportingLineBulkImported), nameof(ReportingLineBulkImported)));
// S74 ADR-027 Phase 5 — manager_vikar lifecycle audit mappers (Infrastructure-located, cross-process:
// ManagerVikarEnded is also emitted by the DelegationExpiryService BackgroundService in Infrastructure).
// ManagerVikarCreated from POST /delegate; ManagerVikarEnded from DELETE /delegate + expiry close (SPRINT-74 R4).
builder.Services.AddSingleton<IAuditProjectionMapper<ManagerVikarCreated>, StatsTid.Infrastructure.AuditMappers.ManagerVikarCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(ManagerVikarCreated), nameof(ManagerVikarCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<ManagerVikarEnded>, StatsTid.Infrastructure.AuditMappers.ManagerVikarEndedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(ManagerVikarEnded), nameof(ManagerVikarEnded)));
// S97 ADR-035 — UserEnhederChanged audit mapper. TENANT_TARGETED; target org from
// context.ResolvedTargetOrgId (the user's primary_org). S103 / TASK-10304 (ADR-038 D10): NO writer
// emits UserEnhederChanged anymore (the legacy tag model is dropped), but the mapper + its
// registration STAY name-keyed for replay-safety of any historical stream.
builder.Services.AddSingleton<IAuditProjectionMapper<UserEnhederChanged>, UserEnhederChangedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UserEnhederChanged), nameof(UserEnhederChanged)));
// S59 ADR-029 — per-employee entitlement eligibility (mapper lives in Infrastructure, cross-process)
builder.Services.AddSingleton<IAuditProjectionMapper<EmployeeEntitlementEligibilitySet>, StatsTid.Infrastructure.AuditMappers.EmployeeEntitlementEligibilitySetAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EmployeeEntitlementEligibilitySet), nameof(EmployeeEntitlementEligibilitySet)));
// S68 ADR-033 slice 1a — vacation-settlement audit mappers (Infrastructure-located, cross-process; dispatched from the SettlementCloseService BackgroundService)
builder.Services.AddSingleton<IAuditProjectionMapper<VacationCarryoverExecuted>, StatsTid.Infrastructure.AuditMappers.VacationCarryoverExecutedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(VacationCarryoverExecuted), nameof(VacationCarryoverExecuted)));
builder.Services.AddSingleton<IAuditProjectionMapper<VacationAutoPaidOut>, StatsTid.Infrastructure.AuditMappers.VacationAutoPaidOutAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(VacationAutoPaidOut), nameof(VacationAutoPaidOut)));
builder.Services.AddSingleton<IAuditProjectionMapper<VacationForfeitedToFeriefond>, StatsTid.Infrastructure.AuditMappers.VacationForfeitedToFeriefondAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(VacationForfeitedToFeriefond), nameof(VacationForfeitedToFeriefond)));
builder.Services.AddSingleton<IAuditProjectionMapper<SettlementManualReviewFlagged>, StatsTid.Infrastructure.AuditMappers.SettlementManualReviewFlaggedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(SettlementManualReviewFlagged), nameof(SettlementManualReviewFlagged)));
// S80 ADR-033 slice 2 — særlige-feriedage §15 stk.2/§17 godtgørelse audit mapper (Infrastructure-located,
// cross-process; SaerligeFeriedagePaidOut from the SPECIAL_HOLIDAY godtgørelse close — SPRINT-80 R8).
builder.Services.AddSingleton<IAuditProjectionMapper<SaerligeFeriedagePaidOut>, StatsTid.Infrastructure.AuditMappers.SaerligeFeriedagePaidOutAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(SaerligeFeriedagePaidOut), nameof(SaerligeFeriedagePaidOut)));
// S70 ADR-033 slice 3a — termination-foundation audit mappers (Infrastructure-located, cross-process;
// EmployeeEmploymentEndDateSet from the admin end-date endpoint, EmployeeEndDateDeactivationApplied from
// the SettlementCloseService Step-A flip, TerminationSettled from the settlement pass — SPRINT-70 R10)
builder.Services.AddSingleton<IAuditProjectionMapper<EmployeeEmploymentEndDateSet>, StatsTid.Infrastructure.AuditMappers.EmployeeEmploymentEndDateSetAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EmployeeEmploymentEndDateSet), nameof(EmployeeEmploymentEndDateSet)));
builder.Services.AddSingleton<IAuditProjectionMapper<EmployeeEndDateDeactivationApplied>, StatsTid.Infrastructure.AuditMappers.EmployeeEndDateDeactivationAppliedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(EmployeeEndDateDeactivationApplied), nameof(EmployeeEndDateDeactivationApplied)));
builder.Services.AddSingleton<IAuditProjectionMapper<TerminationSettled>, StatsTid.Infrastructure.AuditMappers.TerminationSettledAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(TerminationSettled), nameof(TerminationSettled)));
// S71 ADR-033 slice 3b — termination-emission audit mappers (Infrastructure-located, cross-process;
// TerminationPayoutRequested from the §26 request endpoint, TerminationClaimWaived from the waiver
// CAS resolve verb, SettlementReversed from the slice-3b reversal service — SPRINT-71 R5/R6/R10).
// The §7 TerminationModregningApplied event + mapper are PARKED behind the SLS-dialogue task (gate (i)).
builder.Services.AddSingleton<IAuditProjectionMapper<TerminationPayoutRequested>, StatsTid.Infrastructure.AuditMappers.TerminationPayoutRequestedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(TerminationPayoutRequested), nameof(TerminationPayoutRequested)));
builder.Services.AddSingleton<IAuditProjectionMapper<TerminationClaimWaived>, StatsTid.Infrastructure.AuditMappers.TerminationClaimWaivedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(TerminationClaimWaived), nameof(TerminationClaimWaived)));
builder.Services.AddSingleton<IAuditProjectionMapper<SettlementReversed>, StatsTid.Infrastructure.AuditMappers.SettlementReversedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(SettlementReversed), nameof(SettlementReversed)));
// S79 ADR-033 slice 4 — §22 feriehindring audit mapper (Infrastructure-located, cross-process;
// FeriehindringTransferred from the FERIEHINDRING CAS resolve disposition — SPRINT-79 R1/R3). The
// residual §34 remainder reuses the existing VacationForfeitedToFeriefond mapper (registered above).
builder.Services.AddSingleton<IAuditProjectionMapper<FeriehindringTransferred>, StatsTid.Infrastructure.AuditMappers.FeriehindringTransferredAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(FeriehindringTransferred), nameof(FeriehindringTransferred)));
// S103 ADR-038 D10 — Enhedsspor typed `units` event family (evolves Enhed*). All TENANT_TARGETED;
// target_org_id = the unit's owning Organisation (from the event payload), except UnitRenamed
// (no org id in payload → context.ResolvedTargetOrgId, mirroring UserEnhederChanged). Mapper +
// marker registered together (the registry's RegisteredEventTypeNames filter matches the wired
// mappers). NO writer emits these yet — the units CRUD ships in a later sprint; these land with
// the model so the serializer map + audit catalog are complete (P3 forward-auditability).
builder.Services.AddSingleton<IAuditProjectionMapper<UnitCreated>, UnitCreatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UnitCreated), nameof(UnitCreated)));
builder.Services.AddSingleton<IAuditProjectionMapper<UnitRenamed>, UnitRenamedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UnitRenamed), nameof(UnitRenamed)));
builder.Services.AddSingleton<IAuditProjectionMapper<UnitMoved>, UnitMovedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UnitMoved), nameof(UnitMoved)));
builder.Services.AddSingleton<IAuditProjectionMapper<UnitDeleted>, UnitDeletedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UnitDeleted), nameof(UnitDeleted)));
builder.Services.AddSingleton<IAuditProjectionMapper<UnitLeaderDesignated>, UnitLeaderDesignatedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UnitLeaderDesignated), nameof(UnitLeaderDesignated)));
builder.Services.AddSingleton<IAuditProjectionMapper<UnitLeaderRemoved>, UnitLeaderRemovedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UnitLeaderRemoved), nameof(UnitLeaderRemoved)));
builder.Services.AddSingleton<IAuditProjectionMapper<UserUnitChanged>, UserUnitChangedAuditMapper>();
builder.Services.AddSingleton(new RegisteredAuditEventType(typeof(UserUnitChanged), nameof(UserUnitChanged)));

// ── Services ──
builder.Services.AddSingleton<ConfigResolutionService>();
builder.Services.AddSingleton<StatsTid.Infrastructure.VacationSettlementService>(); // S68 ADR-033 slice 1a — the atomic settlement pass
// S71 / TASK-7104 (ADR-033 slice 3b) — the reversal surface: the §26 payout-request repository
// (created by 7104, consumed by the 7102 request endpoint + the reversal's R6 VOID), the ONE
// shared employment-end-date lifecycle writer (SPRINT-71 R4 — consumed by the reversal service
// now, by the 7102-refactored end-date PUT later), and the operator-authorized reversal service
// (driven by the 7102 reversal endpoint). All stateless over DI'd repos ⇒ singleton-safe.
builder.Services.AddSingleton<StatsTid.Infrastructure.TerminationPayoutRequestRepository>();
builder.Services.AddSingleton<StatsTid.Infrastructure.EmploymentEndDateLifecycleWriter>();
builder.Services.AddSingleton<StatsTid.Infrastructure.SettlementReversalService>();
// S65 / TASK-6502 — shared per-day "Arbejdstid"-norm resolver (extracted from the Skema
// month read; also consumed by the Balance year-overview read). Stateless: the per-request
// config cache is local to each ComputeRangeAsync call, so singleton is safe.
builder.Services.AddSingleton<StatsTid.Backend.Api.Services.DailyNormCalculator>();
// S66 / TASK-6603 — vacation-consumption (feriedage) calculator (ADR-032 D1/D3). Composes the
// shared DailyNormCalculator (one norm impl, no drift) + the dated profile resolver for the
// ANNUAL_ACTIVITY fallback discriminator. Stateless (per-call caches) ⇒ singleton-safe.
builder.Services.AddSingleton<StatsTid.Backend.Api.Services.ConsumptionCalculator>();
// S81 / TASK-8101 (R3) — singleton factory for the per-request graceful dated-entitlement-config
// resolver (hoisted byte-for-byte from the YearOverview handler's former local functions). The
// resolver INSTANCE is per-request (its agreement-by-date + live-by-(type,agreement) caches are
// request-scoped); the FACTORY (holding the two singleton repos) is the singleton.
builder.Services.AddSingleton<StatsTid.Infrastructure.DatedEntitlementConfigResolverFactory>();
// S65 / TASK-6502 — server "today" seam for the new year-overview endpoint ONLY (Step-0b
// Reviewer NOTE). TimeProvider.System is the production default; TASK-6504's test host
// overrides it with a fixed provider. No other endpoint is refactored onto this seam.
builder.Services.AddSingleton(TimeProvider.System);
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

// ── S103 / TASK-10304 / ADR-038 (Enhedsspor Phase 1a): the S97 Enhed backfill seeder is
// retired with the legacy Enhed tag tables + the free-text display column on employee profiles
// (dropped in init.sql, greenfield reseed — ADR-038 D9). The `units` model + its CRUD + any unit
// backfill ship in a later phase. The Enhed* event records + their EventSerializer registrations
// + audit mappers STAY name-keyed for replay-safety (D10).

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
app.MapUnitEndpoints(); // S104 ADR-038 D3 — units CRUD + leader designate/remove + same-Org person unit-assign
app.MapApprovalEndpoints();
app.MapConfigEndpoints();
app.MapSkemaEndpoints();
app.MapProjectEndpoints();
app.MapAgreementConfigEndpoints();
app.MapPositionOverrideEndpoints();
app.MapWageTypeMappingEndpoints();
app.MapEntitlementConfigEndpoints();
app.MapAgreementEntitlementEndpoints();
app.MapEmployeeProfileEndpoints();
app.MapEntitlementEligibilityEndpoints(); // S59 / TASK-5906 — CHILD_SICK eligibility + DOB (HR-only)
app.MapEmploymentDateEndpoints(); // S60 / TASK-6006 — employment_start_date set/read (HR-only)
app.MapBalanceEndpoints();
app.MapComplianceEndpoints();
app.MapOvertimeEndpoints();
app.MapAuditEndpoints();
app.MapReportingLineEndpoints();
app.MapVacationSettlementEndpoints(); // S68 ADR-033 slice 1a — §21 agreement + D10 resolve + §24 payout-pending
app.MapTerminationPayoutRequestEndpoints(); // S71 / TASK-7102 — the §26 anmodning record + event (ADR-033 slice 3b, SPRINT-71 R6)
app.MapSettlementReversalEndpoints();       // S71 / TASK-7102 — operator-authorized settlement reversal (ADR-033 D4/D5, SPRINT-71 R4)

app.Run();

// Marker for Microsoft.AspNetCore.Mvc.Testing's WebApplicationFactory<TEntryPoint>
// to anchor on the top-level-statements compilation unit. Required because top-level
// statements emit an internal Program class by default, which the test assembly cannot
// reference. This is the documented industry-standard pattern (Microsoft docs).
public partial class Program { }
