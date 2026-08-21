using System.Net.Http.Json;
using System.Text.Json;
using StatsTid.Auth;
using StatsTid.Backend.Api.Contracts;
using StatsTid.Infrastructure;
using StatsTid.Infrastructure.Security;
using StatsTid.SharedKernel.Exceptions;
using StatsTid.SharedKernel.Interfaces;
using StatsTid.SharedKernel.Models;
using StatsTid.SharedKernel.Normalization;
using StatsTid.SharedKernel.Security;

namespace StatsTid.Backend.Api.Endpoints;

public static class ComplianceEndpoints
{
    public static WebApplication MapComplianceEndpoints(this WebApplication app)
    {
        // ── GET /api/compliance/{employeeId}/period — Check compliance for a period ──
        app.MapGet("/api/compliance/{employeeId}/period", async (
            string employeeId,
            int year,
            int month,
            UserRepository userRepo,
            IHttpClientFactory httpClientFactory,
            TimeEntryProjectionRepository timeEntryProjectionRepo,
            IEmploymentProfileResolver profileResolver,
            OrgScopeValidator scopeValidator,
            DesignatedApproverAuthorizer designatedAuthorizer,
            // S128 / TASK-12804 (RES-002) — period resolution for the leader-tier month gate.
            ApprovalPeriodRepository approvalRepo,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            // Access control
            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                // S88-8801 B2 — ADDITIVE designated-approver OR-branch (mirrors the approve endpoint's
                // OR-pattern, ApprovalEndpoints:263-271). The team-overview roster is the DESIGNATED-
                // approver set, which (ADR-027 D13) admits cross-afdeling vikar/escalation approvers
                // whose org-scope does NOT cover the employee; without this branch their lazy Advarsel
                // fetch on the expandable detail row would 403 (a systematic hole masked as a transient
                // fault). org-scope stays the primary gate; the edge only ADDS access — every existing
                // caller (employee-self / HR / org-scope) is preserved.
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                {
                    // S105 / ADR-038 D4 — the edge OR the secondary-unit-leader path (the same centralized
                    // predicate the team-overview roster + allocation-breakdown gate use, so a unit leader
                    // who can ACT can also lazy-fetch the Advarsel detail). org-scope stays the primary gate.
                    var today = DateOnly.FromDateTime(DateTime.UtcNow);
                    var hasEdgeOrUnit = await designatedAuthorizer.IsEffectiveApproverOrUnitLeaderAsync(
                        actor.ActorId!, employeeId, asOf: today, ct: ct);
                    if (!hasEdgeOrUnit)
                        return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
                }
            }

            var user = await userRepo.GetByIdAsync(employeeId, ct);
            if (user is null)
                return Results.NotFound(new { error = "Employee not found" });

            var daysInMonth = DateTime.DaysInMonth(year, month);
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = new DateOnly(year, month, daysInMonth);

            // ── S128 / TASK-12804 (RES-002) — THE LEADER MONTH GATE on this sibling read ────────
            // The manager-visibility rule (a manager sees NOTHING of a month the employee has not
            // sent — S124/TASK-12402; REJECTED withheld too since S127/R1) now covers this read:
            // its compliance verdicts are derived from the same in-progress registrations the rule
            // withholds. S128 rulings: R1 TIERED — self and HR-or-above (the corrective tier) are
            // exempt, decided by the shared ApprovalReadTier; R5 NARROW-ONLY — the population
            // admitted above (self / org-scope / the S88-8801 B2 designated edge) is untouched, the
            // gate only SUBTRACTS within it; R6 = 403 via the shared Skema-shape construction site.
            // Fail-closed: no period row ⇒ withheld.
            if (await ApprovalReadTier.IsLeaderTierReadAsync(scopeValidator, actor, employeeId, ct))
            {
                var period = await approvalRepo.GetByEmployeeAndPeriodAsync(employeeId, monthStart, monthEnd, ct);
                if (!ApprovalVisibility.IsSubmittedToManager(period?.Status))
                    return ApprovalReadTier.MonthNotSubmittedForbidden();
            }

            // Fetch time entries from projection (sync-in-tx with the POST that wrote them — read-your-write per ADR-018 D12).
            // ADR-039 D5b (GAP-B, no dropped hours at a period edge): widen the read's LOWER bound
            // by one day. A midnight-crossing shift filed on the LAST day of the PREVIOUS month
            // (e.g. 31-Mar 23:00→02:00) carries post-midnight hours that belong (by wall clock +
            // ADR-003) to THIS month's first day; without the extra day the source row is never
            // fetched and those OK-correct hours are lost from BOTH months' compliance view. We
            // read [monthStart-1 .. monthEnd], THEN normalize (splitting each crossing into a
            // day-D + day-D+1 half), and RestPeriodRule's own period filter [monthStart..monthEnd]
            // then keeps exactly the halves that belong to this month — the prev-month pre-half is
            // dropped there, and a crossing on monthEnd yields a next-month post-half the filter
            // drops here (the NEXT month picks it up via ITS OWN widened read — no double count).
            var readStart = monthStart.AddDays(-1);
            var timeEntryRows = await timeEntryProjectionRepo.GetByEmployeeAndDateRangeAsync(employeeId, readStart, monthEnd, ct);
            var timeEntries = timeEntryRows
                .Select(r => new TimeEntry
                {
                    EmployeeId = r.EmployeeId,
                    Date = r.Date,
                    Hours = r.Hours,
                    StartTime = r.StartTime,
                    EndTime = r.EndTime,
                    TaskId = r.TaskId,
                    ActivityType = r.ActivityType,
                    AgreementCode = r.AgreementCode,
                    OkVersion = r.OkVersion,
                    VoluntaryUnsocialHours = r.VoluntaryUnsocialHours,
                    // ADR-039 D4 — continuity link from the immutable source event id, so a
                    // midnight-crossing shift's two normalized halves (below) share one stint
                    // identity and a rest check can rejoin them as ONE continuous work period.
                    SourceStintId = r.EventId,
                })
                .ToList();

            // ADR-039 (S132 TASK-132-1b-1) — normalize midnight-crossing entries on the
            // COMPLIANCE INPUT (before shipping to the rule engine), so post-midnight hours are
            // attributed to the correct calendar day / OK-version (D3) and the per-day hours
            // checks count them on D+1. Same pure, shared implementation as the payroll calc
            // path (D6). The projection rows above are DISPLAY-faithful and untouched (D5a) —
            // this transform derives the calc view only. A crossing shift on the last day of the
            // queried month yields a D+1 half in the next month; RestPeriodRule's own period
            // filter drops it here (those hours belong to the next period — see TASK-1b-3 contract).
            var normalizedEntries = MidnightCrossingNormalizer.Normalize(timeEntries);

            // Call Rule Engine via HTTP (PAT-005).
            // S73 / TASK-7300 (R1): the NAMED rule-engine client — BaseAddress +
            // Authorization/X-Correlation-Id forwarding are wired centrally in Program.cs
            // (RuleEngineClient / RuleEngineHeaderForwardingHandler). This was one of the
            // BARE call sites of the S73 incident (no bearer → rule engine 401 → 503 here).
            var httpClient = httpClientFactory.CreateClient(Http.RuleEngineClient.Name);
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            // ADR-023 D1+D3 cutover: resolve fully-hydrated dated profile via
            // EmploymentProfileResolver. Non-PCS rule-engine HTTP caller →
            // fail-closed on null (caller maps to 500 via existing middleware per
            // ADR-023 D3). Replaces hardcoded WeeklyNormHours=37.0m +
            // EmploymentCategory="STANDARD" defaults; dated weekly_norm_hours +
            // live-joined agreement_code/ok_version/employment_category come
            // from the resolver per ADR-023 D2 (employment_category gap is
            // Phase 4e launch-blocking).
            var profile = await profileResolver.GetByEmployeeIdAtAsync(employeeId, monthStart, ct)
                ?? throw new EmployeeProfileNotFoundException(employeeId, monthStart);

            var complianceRequest = new
            {
                profile,
                entries = normalizedEntries,
                periodStart = monthStart,
                periodEnd = monthEnd,
            };

            var response = await httpClient.PostAsJsonAsync(
                "/api/rules/check-compliance", complianceRequest, jsonOptions, ct);

            if (!response.IsSuccessStatusCode)
                return Results.Json(new { error = "Compliance check service unavailable" }, statusCode: 503);

            var result = await response.Content.ReadFromJsonAsync<ComplianceCheckResult>(jsonOptions, ct);

            // S120 / TASK-12000 — OWNER RULING #3 (dead-branch class, the S118-ruling-#1
            // lineage; ONE ruling, TWO ops — see the governance sibling): a null here means
            // ReadFromJsonAsync deserialized a literal-null 2xx body from the rule engine —
            // PROVEN defensive dead code (RestPeriodRule.Evaluate returns a non-nullable
            // ComplianceCheckResult and the endpoint Results.Ok's it; a garbled body throws →
            // 500; unavailability is the 503 above). 502 upstream-invalid (the SkemaEndpoints
            // null-deserialization idiom) makes the declared 200 STRUCTURALLY the full result.
            if (result is null)
                return Results.Json(new { error = "Invalid compliance check response" }, statusCode: 502);

            return Results.Ok(result);
        }).RequireAuthorization("EmployeeOrAbove")
        // S120 / TASK-12000 — the NAMED SharedKernel model IS the wire shape (the handler
        // passes the rule-engine result through verbatim; PAT-012 named-model rule).
        .Produces<ComplianceCheckResult>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden); // S128 / TASK-12804 — the leader-tier month gate

        // ── GET /api/compliance/{employeeId}/compensatory-rest — Get compensatory rest entries ──
        app.MapGet("/api/compliance/{employeeId}/compensatory-rest", async (
            string employeeId,
            CompensatoryRestRepository compensatoryRestRepo,
            OrgScopeValidator scopeValidator,
            HttpContext context,
            CancellationToken ct) =>
        {
            var actor = context.GetActorContext();

            if (actor.ActorRole == StatsTidRoles.Employee && employeeId != actor.ActorId)
                return Results.Json(new { error = "Access denied" }, statusCode: 403);

            if (actor.ActorRole != StatsTidRoles.Employee)
            {
                var (allowed, reason) = await scopeValidator.ValidateEmployeeAccessAsync(actor, employeeId, ct);
                if (!allowed)
                    return Results.Json(new { error = "Access denied", reason }, statusCode: 403);
            }

            var entries = await compensatoryRestRepo.GetByEmployeeAsync(employeeId, ct);
            // S120 / TASK-12000 — named record (BYTE-IDENTICAL wire JSON; a BARE ARRAY).
            return Results.Ok(entries.Select(e => new CompensatoryRestItem(
                Id: e.Id,
                EmployeeId: e.EmployeeId,
                SourceDate: e.SourceDate,
                CompensatoryDate: e.CompensatoryDate,
                Hours: e.Hours,
                Status: e.Status,
                CreatedAt: e.CreatedAt)));
        }).RequireAuthorization("EmployeeOrAbove")
        .Produces<IEnumerable<CompensatoryRestItem>>(StatusCodes.Status200OK); // S120 / TASK-12000

        return app;
    }
}
