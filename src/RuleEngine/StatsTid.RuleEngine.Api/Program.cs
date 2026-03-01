using StatsTid.Infrastructure.Security;
using StatsTid.RuleEngine.Api.Contracts;
using StatsTid.RuleEngine.Api.Rules;

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

    return result.Success ? Results.Ok(result) : Results.BadRequest(result);
}).RequireAuthorization("Authenticated");

app.MapGet("/api/rules/available/{okVersion}", (string okVersion, RuleRegistry registry) =>
{
    var rules = registry.GetAvailableRules(okVersion);
    return Results.Ok(new { okVersion, rules });
}).RequireAuthorization("Authenticated");

app.Run();
