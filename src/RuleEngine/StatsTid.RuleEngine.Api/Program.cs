using StatsTid.Infrastructure.Security;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;
using StatsTid.SharedKernel.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RuleRegistry>();
builder.Services.AddStatsTidJwtAuth(builder.Configuration);
builder.Services.AddStatsTidPolicies();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "rule-engine" }));

app.MapPost("/api/rules/evaluate", (EvaluateRequest request, RuleRegistry registry) =>
{
    var result = registry.Evaluate(
        request.RuleId,
        request.Profile,
        request.Entries,
        request.PeriodStart,
        request.PeriodEnd);

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
    var result = registry.EvaluateFlexBalance(
        request.Profile,
        request.Entries,
        request.Absences,
        request.PeriodStart,
        request.PeriodEnd,
        request.PreviousBalance);

    if (!result.Success)
    {
        return Results.BadRequest(new FlexEvaluationResponse
        {
            RuleId = FlexBalanceRule.RuleId,
            EmployeeId = result.EmployeeId,
            Success = false,
            LineItems = [],
            ErrorMessage = "Flex balance evaluation failed",
            PreviousBalance = result.PreviousBalance,
            NewBalance = result.NewBalance,
            Delta = result.Delta,
            WorkedHours = result.WorkedHours,
            AbsenceNormCredits = result.AbsenceNormCredits,
            EffectiveNorm = result.NormHours,
            ExcessForPayout = result.ExcessForPayout
        });
    }

    var payoutItem = FlexBalanceRule.GetPayoutLineItem(result, request.PeriodEnd);
    var lineItems = payoutItem is not null
        ? new List<CalculationLineItem> { payoutItem }
        : new List<CalculationLineItem>();

    return Results.Ok(new FlexEvaluationResponse
    {
        RuleId = FlexBalanceRule.RuleId,
        EmployeeId = result.EmployeeId,
        Success = true,
        LineItems = lineItems,
        PreviousBalance = result.PreviousBalance,
        NewBalance = result.NewBalance,
        Delta = result.Delta,
        WorkedHours = result.WorkedHours,
        AbsenceNormCredits = result.AbsenceNormCredits,
        EffectiveNorm = result.NormHours,
        ExcessForPayout = result.ExcessForPayout
    });
}).RequireAuthorization("Authenticated");

app.MapGet("/api/rules/available/{okVersion}", (string okVersion, RuleRegistry registry) =>
{
    var rules = registry.GetAvailableRules(okVersion);
    return Results.Ok(new { okVersion, rules });
}).RequireAuthorization("Authenticated");

app.Run();
