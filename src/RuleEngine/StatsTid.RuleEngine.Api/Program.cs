using StatsTid.Auth;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Calendar;
using StatsTid.SharedKernel.Models;

// The Copenhagen time-zone startup gate, first, as in the Backend (owner ruling OQ-11): a host
// that cannot resolve Europe/Copenhagen refuses to boot instead of failing a compliance call. The
// daily rest check measures elapsed hours in that zone, which this host passes to the rule; the
// rule never looks it up itself (ADR-002).
CopenhagenBusinessDate.EnsureHostZoneResolves();

var builder = WebApplication.CreateBuilder(args);

// QUAL-008 render fold-in — render the ambient CorrelationId log scope in the default console sink.
builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);

builder.Services.AddSingleton<RuleRegistry>();
builder.Services.AddStatsTidJwtAuth(builder.Configuration, builder.Environment);
builder.Services.AddStatsTidPolicies();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "rule-engine" }));

app.MapPost("/api/rules/evaluate", (EvaluateRequest request, RuleRegistry registry) =>
{
    // The five routes that read an agreement configuration take it from the request (ADR-043
    // D12, R-8) and answer a refused one 400 { success: false, error } with a fixed, value-free
    // sentence (SEC-039; RuleRegistry.ProfileConfig, ComplianceConfigForDay, EvaluateTimeline).
    CalculationResult result;
    try
    {
        result = registry.Evaluate(
            request.RuleId,
            request.Profile,
            request.Entries,
            request.PeriodStart,
            request.PeriodEnd,
            request.Config);
    }
    catch (AgreementConfigRefusedException ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }

    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization("Authenticated");

app.MapPost("/api/rules/evaluate-absence", (EvaluateAbsenceRequest request, RuleRegistry registry) =>
{
    var result = registry.EvaluateAbsenceRule(
        request.Profile,
        request.Absences,
        request.PeriodStart,
        request.PeriodEnd);

    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization("Authenticated");

app.MapPost("/api/rules/evaluate-flex", (EvaluateFlexRequest request, RuleRegistry registry) =>
{
    // S145 R8: a one-piece timeline on the weekly core; the payout line is the core's, dated on
    // the settlement day (for a legacy call, the period's last day — as before).
    FlexBalanceResult result;
    CalculationLineItem? payoutItem;
    try
    {
        (result, payoutItem) = registry.EvaluateFlexBalanceWithPayout(
            request.Profile,
            request.Entries,
            request.Absences,
            request.PeriodStart,
            request.PeriodEnd,
            request.PreviousBalance,
            request.Config);
    }
    catch (AgreementConfigRefusedException ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }

    if (!result.Success)
    {
        return Results.BadRequest(ToFlexResponse(result, null, "Flex balance evaluation failed"));
    }

    return Results.Ok(ToFlexResponse(result, payoutItem, null));
}).RequireAuthorization("Authenticated");

// S145 / TASK-14505 — the week-by-week evaluation payroll calls once per plan: overtime /
// merarbejde per settled Monday–Sunday week (dated on its settlement day), the norm check over the
// period's own covered days, and flex settled once on the period's last covered day. Each piece
// carries its own config, which must name the piece's agreement code and the OK version of its
// days (ADR-043 D12, A-10); payroll freezes the configs it sent, and the response carries the
// evaluation-model marker, which payroll records for replay (ADR-016 D10). Pure apart from
// HTTP: the registry and the core do no I/O (ADR-002).
app.MapPost("/api/rules/evaluate-timeline", (EvaluateTimelineRequest request, RuleRegistry registry) =>
{
    TimelineEvaluation evaluation;
    try
    {
        evaluation = registry.EvaluateTimeline(request);
    }
    catch (TimelineInputException ex)
    {
        // Fixed, value-free sentences only (no employee id, dates or hours echoed — SEC-039).
        return Results.BadRequest(new { success = false, error = ex.Message });
    }

    var core = evaluation.Core;
    var flexLineItems = core.FlexPayout is not null
        ? new List<CalculationLineItem> { core.FlexPayout }
        : new List<CalculationLineItem>();

    return Results.Ok(new TimelineEvaluationResponse
    {
        Results =
        [
            core.Overtime,
            core.Norm,
            new CalculationResult
            {
                RuleId = FlexBalanceRule.RuleId,
                EmployeeId = core.Flex.EmployeeId,
                Success = core.Flex.Success,
                LineItems = flexLineItems
            },
        ],
        Flex = ToFlexResponse(core.Flex, core.FlexPayout, null),
        EvaluationModel = evaluation.EvaluationModel
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/rules/available/{okVersion}", (string okVersion, RuleRegistry registry) =>
{
    var rules = registry.GetAvailableRules(okVersion);
    return Results.Ok(new { okVersion, rules });
}).RequireAuthorization("Authenticated");

// Read-only passthrough of the in-memory ADR-016 classification inventory used by
// the segmentation planner. Consumed by Payroll's HTTP-backed
// IRuleClassificationProvider (TASK-2010) so PeriodPlanner.Plan() / FromManifest()
// see the resolved (Span, SplitBehavior, Family, MergeStrategy, SnapshotContract)
// triple per rule. ADR-002 purity holds — RuleRegistry is in-memory; no I/O.
app.MapGet("/api/rules/classifications", (RuleRegistry registry) =>
{
    return Results.Ok(registry.GetAll());
}).RequireAuthorization("Authenticated");

app.MapPost("/api/rules/validate-entitlement", (ValidateEntitlementRequest request) =>
{
    var result = EntitlementValidationRule.Evaluate(request);
    return Results.Ok(result);
}).RequireAuthorization("Authenticated");

app.MapPost("/api/rules/check-compliance", (CheckComplianceRequest request) =>
{
    // Each check reads the config in force on its own day, from the request's dated runs (RR-7).
    Func<DateOnly, AgreementRuleConfig> configForDay;
    try
    {
        configForDay = RuleRegistry.ComplianceConfigForDay(request);
    }
    catch (AgreementConfigRefusedException ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }

    var result = RestPeriodRule.Evaluate(
        request.Profile, request.Entries, request.PeriodStart, request.PeriodEnd, configForDay,
        request.WorkIntervals, CopenhagenBusinessDate.Zone);
    return Results.Ok(result);
}).RequireAuthorization("Authenticated");

app.MapPost("/api/rules/check-overtime-governance", (CheckOvertimeGovernanceRequest request) =>
{
    AgreementRuleConfig config;
    try
    {
        config = RuleRegistry.ProfileConfig(request.Profile, request.Config);
    }
    catch (AgreementConfigRefusedException ex)
    {
        return Results.BadRequest(new { success = false, error = ex.Message });
    }

    var result = OvertimeGovernanceRule.Evaluate(
        request.Profile, request.OvertimeHoursInPeriod, request.HasPreApproval,
        request.PeriodStart, request.PeriodEnd, config);
    return Results.Ok(result);
}).RequireAuthorization("Authenticated");

app.Run();

static FlexEvaluationResponse ToFlexResponse(FlexBalanceResult result, CalculationLineItem? payoutItem, string? errorMessage) =>
    new()
    {
        RuleId = FlexBalanceRule.RuleId,
        EmployeeId = result.EmployeeId,
        Success = result.Success,
        LineItems = payoutItem is not null
            ? new List<CalculationLineItem> { payoutItem }
            : new List<CalculationLineItem>(),
        ErrorMessage = errorMessage,
        PreviousBalance = result.PreviousBalance,
        NewBalance = result.NewBalance,
        Delta = result.Delta,
        WorkedHours = result.WorkedHours,
        AbsenceNormCredits = result.AbsenceNormCredits,
        EffectiveNorm = result.NormHours,
        ExcessForPayout = result.ExcessForPayout
    };
